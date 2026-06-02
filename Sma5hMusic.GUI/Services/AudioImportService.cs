using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5hMusic.GUI.Interfaces;
using Sma5hMusic.GUI.Models;
using System;
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
                var tempWavFile = Path.Combine(GetTempPath(), $"{tempId}.wav");
                var restartWavFile = Path.Combine(GetTempPath(), $"{tempId}_restart.wav");
                var endingWavFile = Path.Combine(GetTempPath(), $"{tempId}_ending.wav");
                var previewFile = Path.Combine(GetTempPath(), $"{tempId}.lopus");

                try
                {
                    var loopStart48k = ConvertSampleRate(loopStartSample, info.SampleRate);
                    var loopEnd48k = ConvertSampleRate(loopEndSample, info.SampleRate);
                    var preRollSamples = Math.Max(1u, (uint)Math.Round(loopEnd48k * 0.1));
                    var requestedPreviewStart48k = loopEnd48k > preRollSamples ? loopEnd48k - preRollSamples : 0;
                    var restartPreviewDuration48k = Math.Max(1u, Math.Min(preRollSamples, loopEnd48k - loopStart48k));
                    var restartPreviewEnd48k = loopStart48k + restartPreviewDuration48k;
                    uint relativeLoopStart48k;
                    uint relativeLoopEnd48k;
                    uint previewStartSample;
                    uint firstSegmentSourceStart;
                    uint firstSegmentPreviewLength;
                    uint secondSegmentSourceStart = 0;
                    uint secondSegmentPreviewStart = 0;
                    uint secondSegmentPreviewDuration = 0;
                    var hasSecondSegment = false;

                    _logger.LogInformation("Loop preview converted samples: LoopStart48k={LoopStart48k}, LoopEnd48k={LoopEnd48k}, RequestedPreviewStart48k={RequestedPreviewStart48k}, RestartPreviewEnd48k={RestartPreviewEnd48k}.",
                        loopStart48k, loopEnd48k, requestedPreviewStart48k, restartPreviewEnd48k);

                    if (requestedPreviewStart48k <= restartPreviewEnd48k)
                    {
                        var clipStart48k = Math.Min(requestedPreviewStart48k, loopStart48k);
                        ExtractWavSegment(filename, tempWavFile, clipStart48k, loopEnd48k - clipStart48k, info.SampleRate);
                        relativeLoopStart48k = loopStart48k - clipStart48k;
                        relativeLoopEnd48k = loopEnd48k - clipStart48k;
                        previewStartSample = requestedPreviewStart48k - clipStart48k;
                        firstSegmentSourceStart = ConvertSampleRate(clipStart48k, TargetSampleRate, info.SampleRate);
                        firstSegmentPreviewLength = relativeLoopEnd48k;
                    }
                    else
                    {
                        ExtractWavSegment(filename, restartWavFile, loopStart48k, restartPreviewDuration48k, info.SampleRate);
                        ExtractWavSegment(filename, endingWavFile, requestedPreviewStart48k, loopEnd48k - requestedPreviewStart48k, info.SampleRate);
                        RunTool(GetSoxExe(), restartWavFile, endingWavFile, tempWavFile);
                        relativeLoopStart48k = 0;
                        relativeLoopEnd48k = restartPreviewDuration48k + (loopEnd48k - requestedPreviewStart48k);
                        previewStartSample = restartPreviewDuration48k;
                        firstSegmentSourceStart = loopStartSample;
                        firstSegmentPreviewLength = restartPreviewDuration48k;
                        secondSegmentSourceStart = ConvertSampleRate(requestedPreviewStart48k, TargetSampleRate, info.SampleRate);
                        secondSegmentPreviewStart = restartPreviewDuration48k;
                        secondSegmentPreviewDuration = loopEnd48k - requestedPreviewStart48k;
                        hasSecondSegment = true;
                    }

                    _logger.LogInformation("Loop preview relative samples: RelativeLoopStart48k={RelativeLoopStart48k}, RelativeLoopEnd48k={RelativeLoopEnd48k}, PreviewStartSample={PreviewStartSample}.",
                        relativeLoopStart48k, relativeLoopEnd48k, previewStartSample);

                    _logger.LogInformation("Loop preview temp WAV created: {TempWavFile}. Exists={Exists}, Length={Length}.",
                        tempWavFile, File.Exists(tempWavFile), File.Exists(tempWavFile) ? new FileInfo(tempWavFile).Length : 0);

                    RunTool(GetVGAudioCliExe(), tempWavFile, previewFile, "-l", $"{relativeLoopStart48k}-{relativeLoopEnd48k}", "--bitrate", "64000", "--cbr", "--opusheader", "namco");
                    _logger.LogInformation("Loop preview LOPUS created: {PreviewFile}. Exists={Exists}, Length={Length}.",
                        previewFile, File.Exists(previewFile), File.Exists(previewFile) ? new FileInfo(previewFile).Length : 0);

                    return new LoopPreviewInfo
                    {
                        Filename = previewFile,
                        StartSample = checked((int)previewStartSample),
                        PreviewLoopStartSample = relativeLoopStart48k,
                        PreviewLoopEndSample = relativeLoopEnd48k,
                        FirstSegmentSourceStartSample = firstSegmentSourceStart,
                        FirstSegmentPreviewStartSample = 0,
                        FirstSegmentPreviewLengthSamples = firstSegmentPreviewLength,
                        SecondSegmentSourceStartSample = secondSegmentSourceStart,
                        SecondSegmentPreviewStartSample = secondSegmentPreviewStart,
                        SecondSegmentPreviewDurationSamples = secondSegmentPreviewDuration,
                        HasSecondSegment = hasSecondSegment
                    };
                }
                finally
                {
                    DeleteTempFile(tempWavFile);
                    DeleteTempFile(restartWavFile);
                    DeleteTempFile(endingWavFile);
                }
            });
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
