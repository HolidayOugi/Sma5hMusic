using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5hMusic.GUI.Interfaces;
using Sma5hMusic.GUI.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Services
{
    public class AudioImportService : IAudioImportService
    {
        private const uint TargetSampleRate = 48_000;
        private const uint PreviewContextSamples = TargetSampleRate * 6;
        private static readonly string[] SourceAudioExtensions =
        {
            ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".aiff", ".aif", ".wma"
        };

        private readonly IOptionsMonitor<ApplicationSettings> _config;
        private readonly ILogger _logger;

        public AudioImportService(IOptionsMonitor<ApplicationSettings> config, ILogger<AudioImportService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public bool RequiresConversion(string filename)
        {
            var extension = Path.GetExtension(filename);
            return SourceAudioExtensions.Any(p => string.Equals(p, extension, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<AudioImportInfo> GetAudioInfo(string filename)
        {
            return await Task.Run(() =>
            {
                Directory.CreateDirectory(GetTempPath());

                var sampleRate = ReadUInt(RunTool(GetSoxExe(), "--i", "-r", filename), "sample rate", filename);
                var totalSamples = TryReadTotalSamples(filename);

                if (totalSamples == 0)
                    throw new InvalidOperationException($"Could not determine the total sample count for '{Path.GetFileName(filename)}'.");

                return new AudioImportInfo
                {
                    SampleRate = sampleRate,
                    TotalSamples = totalSamples
                };
            });
        }

        public async Task<IReadOnlyList<AutoLoopPoint>> CalculateAutoLoopPoints(string filename, uint sampleRate, uint totalSamples)
        {
            return await Task.Run(() =>
            {
                _logger.LogInformation("Automatic loop point calculation requested. File={Filename}, SampleRate={SampleRate}, TotalSamples={TotalSamples}.",
                    filename, sampleRate, totalSamples);

                var output = RunPymusiclooper("export-points", "--path", filename, "--fmt", "SAMPLES", "--alt-export-top", "-1");
                var loopPoints = ParsePymusiclooperOutput(output, sampleRate, totalSamples)
                    .ToList();

                _logger.LogInformation("Automatic loop point calculation completed. File={Filename}, ParsedCandidates={CandidateCount}.",
                    filename, loopPoints.Count);

                foreach (var loopPoint in loopPoints)
                {
                    _logger.LogInformation("Automatic loop candidate #{Rank}: Start={LoopStartSample}, End={LoopEndSample}, Score={Score}, NoteDifference={NoteDifference}, LoudnessDifference={LoudnessDifference}, StartTime={StartMinutes}:{StartSeconds}.{StartMilliseconds}, EndTime={EndMinutes}:{EndSeconds}.{EndMilliseconds}.",
                        loopPoint.Rank, loopPoint.LoopStartSample, loopPoint.LoopEndSample, loopPoint.Score, loopPoint.NoteDifference, loopPoint.LoudnessDifference,
                        loopPoint.LoopStartMinutes, loopPoint.LoopStartSeconds, loopPoint.LoopStartMilliseconds,
                        loopPoint.LoopEndMinutes, loopPoint.LoopEndSeconds, loopPoint.LoopEndMilliseconds);
                }

                return loopPoints;
            });
        }

        public async Task<LoopPreviewInfo> CreateLoopPreview(string filename, uint loopStartSample, uint loopEndSample, uint totalSamples)
        {
            return await Task.Run(() =>
            {
                _logger.LogInformation("Loop preview requested for {Filename}. LoopStartSample={LoopStartSample}, LoopEndSample={LoopEndSample}, TotalSamples={TotalSamples}.",
                    filename, loopStartSample, loopEndSample, totalSamples);

                var info = GetAudioInfo(filename).GetAwaiter().GetResult();
                _logger.LogInformation("Loop preview source info: SampleRate={SampleRate}, TotalSamples={DetectedTotalSamples}.", info.SampleRate, info.TotalSamples);

                if (loopEndSample == 0 || loopEndSample > totalSamples)
                    throw new InvalidOperationException($"Loop end sample must be between 1 and {totalSamples}.");

                if (loopStartSample > loopEndSample)
                    throw new InvalidOperationException("Loop start sample must be lower than or equal to loop end sample.");

                Directory.CreateDirectory(GetTempPath());

                var tempId = Guid.NewGuid().ToString("N");
                var sourceWavFile = Path.Combine(GetTempPath(), $"{tempId}_source48k.wav");
                var tempWavFile = Path.Combine(GetTempPath(), $"{tempId}_preview.wav");
                var restartWavFile = Path.Combine(GetTempPath(), $"{tempId}_restart.wav");
                var endingWavFile = Path.Combine(GetTempPath(), $"{tempId}_ending.wav");
                var previewFile = Path.Combine(GetTempPath(), $"{tempId}.wav");

                try
                {
                    var loopStart48k = ConvertSampleRate(loopStartSample, info.SampleRate);
                    var loopEnd48k = ConvertSampleRate(loopEndSample, info.SampleRate);
                    var loopLength48k = loopEnd48k - loopStart48k;
                    var endingPreviewDuration48k = Math.Max(1u, Math.Min(PreviewContextSamples, loopEnd48k));
                    var requestedPreviewStart48k = loopEnd48k - endingPreviewDuration48k;
                    var restartPreviewDuration48k = Math.Max(1u, Math.Min(PreviewContextSamples, loopLength48k));

                    _logger.LogInformation("Loop preview converted samples: LoopStart48k={LoopStart48k}, LoopEnd48k={LoopEnd48k}, RequestedPreviewStart48k={RequestedPreviewStart48k}, EndingPreviewDuration48k={EndingPreviewDuration48k}, RestartPreviewDuration48k={RestartPreviewDuration48k}.",
                        loopStart48k, loopEnd48k, requestedPreviewStart48k, endingPreviewDuration48k, restartPreviewDuration48k);

                    _logger.LogInformation("Creating full 48kHz WAV for loop preview before trimming. Input={InputFile}, Output={SourceWavFile}.",
                        filename, sourceWavFile);
                    RunTool(GetSoxExe(), filename, "-r", TargetSampleRate.ToString(CultureInfo.InvariantCulture), sourceWavFile);

                    ExtractWavSegment(sourceWavFile, endingWavFile, requestedPreviewStart48k, loopEnd48k - requestedPreviewStart48k, TargetSampleRate);
                    ExtractWavSegment(sourceWavFile, restartWavFile, loopStart48k, restartPreviewDuration48k, TargetSampleRate);
                    RunTool(GetSoxExe(), "--combine", "concatenate", endingWavFile, restartWavFile, tempWavFile);
                    File.Move(tempWavFile, previewFile);

                    _logger.LogInformation("Loop preview WAV created in playback order. EndingSegmentStart48k={EndingStart48k}, EndingDuration48k={EndingDuration48k}, RestartSegmentStart48k={RestartStart48k}, RestartDuration48k={RestartDuration48k}.",
                        requestedPreviewStart48k, loopEnd48k - requestedPreviewStart48k, loopStart48k, restartPreviewDuration48k);

                    _logger.LogInformation("Loop preview WAV ready: {PreviewFile}. Exists={Exists}, Length={Length}.",
                        previewFile, File.Exists(previewFile), File.Exists(previewFile) ? new FileInfo(previewFile).Length : 0);

                    return new LoopPreviewInfo
                    {
                        Filename = previewFile,
                        PreviewLengthSamples = endingPreviewDuration48k + restartPreviewDuration48k,
                        FirstSegmentSourceStartSample = ConvertSampleRate(requestedPreviewStart48k, TargetSampleRate, info.SampleRate),
                        SecondSegmentSourceStartSample = loopStartSample,
                        SecondSegmentPreviewStartSample = endingPreviewDuration48k
                    };
                }
                finally
                {
                    DeleteTempFile(sourceWavFile);
                    DeleteTempFile(tempWavFile);
                    DeleteTempFile(restartWavFile);
                    DeleteTempFile(endingWavFile);
                }
            });
        }

        public void CleanupLoopPreviews()
        {
            try
            {
                var tempPath = GetTempPath();
                if (!Directory.Exists(tempPath))
                    return;

                foreach (var file in Directory.EnumerateFiles(tempPath, "*.lopus")
                    .Concat(Directory.EnumerateFiles(tempPath, "*.wav")))
                {
                    DeleteTempFile(file);
                    _logger.LogInformation("Deleted stale loop preview file {LoopPreviewFile}.", file);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not cleanup stale loop preview files.");
            }
        }

        public async Task<string> ConvertToNus3Audio(string toneId, string filename, string modPath, uint loopStartSample, uint loopEndSample)
        {
            return await Task.Run(() =>
            {
                if (!RequiresConversion(filename))
                    return filename;

                var info = GetAudioInfo(filename).GetAwaiter().GetResult();
                if (loopEndSample == 0 || loopEndSample > info.TotalSamples)
                    throw new InvalidOperationException($"Loop end sample must be between 1 and {info.TotalSamples}.");

                if (loopStartSample > loopEndSample)
                    throw new InvalidOperationException("Loop start sample must be lower than or equal to loop end sample.");

                Directory.CreateDirectory(modPath);
                Directory.CreateDirectory(GetTempPath());

                var tempId = Guid.NewGuid().ToString("N");
                var tempWavFile = Path.Combine(GetTempPath(), $"{tempId}.wav");
                var tempLopusFile = Path.Combine(GetTempPath(), $"{tempId}.lopus");
                var outputFile = Path.Combine(modPath, $"{toneId}.nus3audio");

                if (File.Exists(outputFile))
                    throw new InvalidOperationException($"The destination file '{Path.GetFileName(outputFile)}' already exists in the selected mod.");

                try
                {
                    var loopStart48k = ConvertSampleRate(loopStartSample, info.SampleRate);
                    var loopEnd48k = ConvertSampleRate(loopEndSample, info.SampleRate);

                    _logger.LogInformation("Converting {InputFile} to WAV 48kHz for import.", filename);
                    RunTool(GetSoxExe(), filename, "-r", TargetSampleRate.ToString(CultureInfo.InvariantCulture), tempWavFile);

                    _logger.LogInformation("Encoding temporary LOPUS with loop {LoopStart}-{LoopEnd}.", loopStart48k, loopEnd48k);
                    RunTool(GetVGAudioCliExe(), tempWavFile, tempLopusFile, "-l", $"{loopStart48k}-{loopEnd48k}", "--bitrate", "64000", "--cbr", "--opusheader", "namco");

                    _logger.LogInformation("Creating NUS3AUDIO {OutputFile}.", outputFile);
                    RunTool(GetNus3AudioExe(), "-n", "-w", outputFile);
                    RunTool(GetNus3AudioExe(), "-A", toneId, tempLopusFile, "-w", outputFile);

                    return outputFile;
                }
                finally
                {
                    DeleteTempFile(tempWavFile);
                    DeleteTempFile(tempLopusFile);
                }
            });
        }

        private uint TryReadTotalSamples(string filename)
        {
            try
            {
                return ReadUInt(RunTool(GetSoxExe(), "--i", "-s", filename), "total sample count", filename);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not read sample count with sox. Falling back to vgmstream.");
            }

            return ReadTotalSamplesWithVgmStream(filename);
        }

        private uint ReadTotalSamplesWithVgmStream(string filename)
        {
            var tempId = Guid.NewGuid().ToString("N");
            var tempWavFile = Path.Combine(GetTempPath(), $"{tempId}.wav");

            try
            {
                RunTool(GetSoxExe(), filename, "-r", TargetSampleRate.ToString(CultureInfo.InvariantCulture), tempWavFile);
                var output = RunTool(GetVgmStreamExe(), "-m", tempWavFile);
                var match = Regex.Match(output, @"stream total samples:\s*(\d+)", RegexOptions.IgnoreCase);

                return match.Success && uint.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalSamples)
                    ? totalSamples
                    : 0;
            }
            finally
            {
                DeleteTempFile(tempWavFile);
            }
        }

        private static uint ReadUInt(string value, string label, string filename)
        {
            var cleanValue = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (!uint.TryParse(cleanValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                throw new InvalidOperationException($"Could not read the {label} for '{Path.GetFileName(filename)}'.");

            return result;
        }

        private static uint ConvertSampleRate(uint sample, uint sourceSampleRate)
        {
            return ConvertSampleRate(sample, sourceSampleRate, TargetSampleRate);
        }

        private static uint ConvertSampleRate(uint sample, uint sourceSampleRate, uint targetSampleRate)
        {
            return (uint)Math.Round(sample * (targetSampleRate / (double)sourceSampleRate));
        }

        private static uint SamplesToMs(uint sample, uint sampleRate)
        {
            return sampleRate == 0 ? 0 : (uint)Math.Round(sample * 1000.0 / sampleRate);
        }

        private static void SplitMilliseconds(uint milliseconds, out uint minutes, out uint seconds, out uint remainingMilliseconds)
        {
            minutes = milliseconds / 60000;
            var remainder = milliseconds % 60000;
            seconds = remainder / 1000;
            remainingMilliseconds = remainder % 1000;
        }

        private string RunTool(string executable, params string[] arguments)
        {
            if (!File.Exists(executable))
                throw new FileNotFoundException($"Required tool not found: {executable}", executable);

            _logger.LogInformation("Running tool: {Executable} {Arguments}", executable, string.Join(" ", arguments.Select(p => $"\"{p}\"")));

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            _logger.LogInformation("Tool exited: {Executable}. ExitCode={ExitCode}. StdOut={StdOut}. StdErr={StdErr}",
                Path.GetFileName(executable), process.ExitCode, output, error);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{Path.GetFileName(executable)} failed: {error}{output}");

            return output;
        }

        private string RunPymusiclooper(params string[] arguments)
        {
            try
            {
                return RunCommand("pymusiclooper", arguments);
            }
            catch (Win32Exception e)
            {
                _logger.LogWarning(e, "Could not launch pymusiclooper directly. Trying pymusiclooper.exe.");
            }

            try
            {
                return RunCommand("pymusiclooper.exe", arguments);
            }
            catch (Win32Exception e)
            {
                _logger.LogWarning(e, "Could not launch pymusiclooper.exe. Trying python -m pymusiclooper.");
            }

            try
            {
                return RunCommand("python", new[] { "-m", "pymusiclooper" }.Concat(arguments).ToArray());
            }
            catch (Win32Exception e)
            {
                _logger.LogError(e, "pymusiclooper could not be launched. Make sure pymusiclooper is installed and available in PATH.");
                throw new FileNotFoundException("pymusiclooper was not found. Please install pymusiclooper and make sure it is available in PATH.", "pymusiclooper", e);
            }
        }

        private string RunCommand(string executable, params string[] arguments)
        {
            _logger.LogInformation("Running command: {Executable} {Arguments}", executable, string.Join(" ", arguments.Select(p => $"\"{p}\"")));

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            _logger.LogInformation("Command exited: {Executable}. ExitCode={ExitCode}. StdOut={StdOut}. StdErr={StdErr}",
                executable, process.ExitCode, output, error);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{executable} failed: {error}{output}");

            return string.Join(Environment.NewLine, new[] { output, error }.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private IReadOnlyList<AutoLoopPoint> ParsePymusiclooperOutput(string output, uint sampleRate, uint totalSamples)
        {
            _logger.LogInformation("Parsing pymusiclooper output. RawOutput={RawOutput}", output);

            var candidates = new List<AutoLoopPoint>();
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                _logger.LogDebug("Parsing pymusiclooper output line: {Line}", trimmedLine);

                var values = Regex.Matches(trimmedLine, @"[-+]?\d+(?:[.,]\d+)?")
                    .Cast<Match>()
                    .Select(p => p.Value.Replace(',', '.'))
                    .ToList();

                if (values.Count < 2)
                {
                    _logger.LogDebug("Skipping pymusiclooper line because it has fewer than two numeric values: {Line}", trimmedLine);
                    continue;
                }

                if (!uint.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var loopStartSample) ||
                    !uint.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var loopEndSample))
                {
                    _logger.LogDebug("Skipping pymusiclooper line because start/end samples could not be parsed: {Line}", trimmedLine);
                    continue;
                }

                if (loopEndSample == 0 || loopStartSample > loopEndSample || loopEndSample > totalSamples)
                {
                    _logger.LogDebug("Skipping pymusiclooper line because samples are outside the valid range. Line={Line}, Start={Start}, End={End}, TotalSamples={TotalSamples}",
                        trimmedLine, loopStartSample, loopEndSample, totalSamples);
                    continue;
                }

                var noteDifference = TryParseDouble(values, 2);
                var loudnessDifference = TryParseDouble(values, 3);
                var score = TryParseDouble(values, 4);
                var scorePercentage = score <= 1 ? score * 100 : score;
                SplitMilliseconds(SamplesToMs(loopStartSample, sampleRate), out var startMinutes, out var startSeconds, out var startMilliseconds);
                SplitMilliseconds(SamplesToMs(loopEndSample, sampleRate), out var endMinutes, out var endSeconds, out var endMilliseconds);

                candidates.Add(new AutoLoopPoint
                {
                    LoopStartSample = loopStartSample,
                    LoopEndSample = loopEndSample,
                    NoteDifference = noteDifference,
                    LoudnessDifference = loudnessDifference,
                    Score = scorePercentage,
                    ScoreText = $"{scorePercentage:0.##}%",
                    LoopStartTimeText = FormatTime(startMinutes, startSeconds, startMilliseconds),
                    LoopEndTimeText = FormatTime(endMinutes, endSeconds, endMilliseconds),
                    LoopStartMinutes = startMinutes,
                    LoopStartSeconds = startSeconds,
                    LoopStartMilliseconds = startMilliseconds,
                    LoopEndMinutes = endMinutes,
                    LoopEndSeconds = endSeconds,
                    LoopEndMilliseconds = endMilliseconds
                });
            }

            var rankedCandidates = candidates
                .GroupBy(p => new { p.LoopStartSample, p.LoopEndSample })
                .Select(p => p.First())
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.LoopStartSample)
                .ToList();

            for (var i = 0; i < rankedCandidates.Count; i++)
            {
                rankedCandidates[i].Rank = i + 1;
                rankedCandidates[i].RankText = rankedCandidates[i].Rank.ToString(CultureInfo.InvariantCulture);
            }

            return rankedCandidates;
        }

        private static double TryParseDouble(IReadOnlyList<string> values, int index)
        {
            return values.Count > index && double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : 0;
        }

        private static string FormatTime(uint minutes, uint seconds, uint milliseconds)
        {
            return $"{minutes}:{seconds:00}.{milliseconds:000}";
        }

        private void ExtractWavSegment(string inputFile, string outputFile, uint startSample48k, uint durationSamples48k, uint sourceSampleRate)
        {
            var startSourceSample = ConvertSampleRate(startSample48k, TargetSampleRate, sourceSampleRate);
            var durationSourceSample = Math.Max(1u, ConvertSampleRate(durationSamples48k, TargetSampleRate, sourceSampleRate));
            _logger.LogInformation("Extracting preview WAV segment. Input={InputFile}, Output={OutputFile}, Start48k={Start48k}, Duration48k={Duration48k}, StartSource={StartSource}, DurationSource={DurationSource}.",
                inputFile, outputFile, startSample48k, durationSamples48k, startSourceSample, durationSourceSample);
            RunTool(GetSoxExe(), inputFile, "-r", TargetSampleRate.ToString(CultureInfo.InvariantCulture), outputFile, "trim", $"{startSourceSample}s", $"{durationSourceSample}s");
        }

        private string GetSoxExe()
        {
            return Path.Combine(_config.CurrentValue.ToolsPath, "sox", "sox.exe");
        }

        private string GetNus3AudioExe()
        {
            return Path.Combine(_config.CurrentValue.ToolsPath, "Nus3Audio", "nus3audio.exe");
        }

        private string GetVGAudioCliExe()
        {
            return Path.Combine(_config.CurrentValue.ToolsPath, "VGAudioCli.exe");
        }

        private string GetVgmStreamExe()
        {
            var vgmStreamPath = Path.Combine(_config.CurrentValue.ToolsPath, "vgmstream");
            var candidates = new[]
            {
                Path.Combine(vgmStreamPath, "test.exe"),
                Path.Combine(vgmStreamPath, "vgmstream-cli.exe"),
                Path.Combine(vgmStreamPath, "vgmstream.exe")
            };

            return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }

        private string GetTempPath()
        {
            return Path.Combine(_config.CurrentValue.TempPath, "AudioImport");
        }

        private static void DeleteTempFile(string filename)
        {
            try
            {
                if (File.Exists(filename))
                    File.Delete(filename);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
