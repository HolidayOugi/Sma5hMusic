using Microsoft.Extensions.Logging;
using Sma5hMusic.GUI.Helpers;
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
    public partial class AudioImportService
    {
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

                foreach (var loopPoint in loopPoints.Take(15))
                {
                    _logger.LogInformation("Automatic loop candidate #{Rank}: Start={LoopStartSample}, End={LoopEndSample}, Score={Score}, NoteDifference={NoteDifference}, LoudnessDifference={LoudnessDifference}, StartTime={StartMinutes}:{StartSeconds}.{StartMilliseconds}, EndTime={EndMinutes}:{EndSeconds}.{EndMilliseconds}.",
                        loopPoint.Rank, loopPoint.LoopStartSample, loopPoint.LoopEndSample, loopPoint.Score, loopPoint.NoteDifference, loopPoint.LoudnessDifference,
                        loopPoint.LoopStartMinutes, loopPoint.LoopStartSeconds, loopPoint.LoopStartMilliseconds,
                        loopPoint.LoopEndMinutes, loopPoint.LoopEndSeconds, loopPoint.LoopEndMilliseconds);
                }

                if (loopPoints.Count > 15)
                    _logger.LogInformation("Additional automatic loop candidates omitted from log. OmittedCandidates={OmittedCandidates}.", loopPoints.Count - 15);

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

                var loopPreviewTempPath = GetLoopPreviewTempPath();
                Directory.CreateDirectory(loopPreviewTempPath);

                var tempId = Guid.NewGuid().ToString("N");
                var sourceWavFile = Path.Combine(loopPreviewTempPath, $"{tempId}_source48k.wav");
                var tempWavFile = Path.Combine(loopPreviewTempPath, $"{tempId}_preview.wav");
                var restartWavFile = Path.Combine(loopPreviewTempPath, $"{tempId}_restart.wav");
                var endingWavFile = Path.Combine(loopPreviewTempPath, $"{tempId}_ending.wav");
                var previewFile = Path.Combine(loopPreviewTempPath, $"{tempId}.wav");
                var soxInputFile = CreateSoxCompatibleInputCopy(filename);

                try
                {
                    var loopStart48k = ConvertSampleRate(loopStartSample, info.SampleRate);
                    var loopEnd48k = ConvertSampleRate(loopEndSample, info.SampleRate);
                    var loopLength48k = loopEnd48k - loopStart48k;
                    var previewSeconds = GetLoopPreviewSeconds();
                    var previewContextSamples = TargetSampleRate * previewSeconds;
                    var endingPreviewDuration48k = Math.Max(1u, Math.Min(previewContextSamples, loopEnd48k));
                    var requestedPreviewStart48k = loopEnd48k - endingPreviewDuration48k;
                    var restartPreviewDuration48k = Math.Max(1u, Math.Min(previewContextSamples, loopLength48k));

                    _logger.LogInformation("Loop preview converted samples: PreviewSeconds={PreviewSeconds}, LoopStart48k={LoopStart48k}, LoopEnd48k={LoopEnd48k}, RequestedPreviewStart48k={RequestedPreviewStart48k}, EndingPreviewDuration48k={EndingPreviewDuration48k}, RestartPreviewDuration48k={RestartPreviewDuration48k}.",
                        previewSeconds, loopStart48k, loopEnd48k, requestedPreviewStart48k, endingPreviewDuration48k, restartPreviewDuration48k);

                    _logger.LogInformation("Creating full 48kHz WAV for loop preview before trimming. Input={InputFile}, Output={SourceWavFile}.",
                        filename, sourceWavFile);
                    RunTool(GetSoxExe(), soxInputFile, "-r", TargetSampleRate.ToString(CultureInfo.InvariantCulture), "-b", "16", "-e", "signed-integer", sourceWavFile);

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
                    DeleteSoxCompatibleInputCopy(filename, soxInputFile);
                    DeleteTempFile(sourceWavFile);
                    DeleteTempFile(tempWavFile);
                    DeleteTempFile(restartWavFile);
                    DeleteTempFile(endingWavFile);
                }
            });
        }

        private uint GetLoopPreviewSeconds()
        {
            var value = _config.CurrentValue.Sma5hMusicGUI?.LoopPreviewSeconds ?? 0;
            return value >= 2 && value <= 10 ? value : 6;
        }

        public void CleanupLoopPreviews()
        {
            try
            {
                var tempPath = GetLoopPreviewTempPath();
                if (!Directory.Exists(tempPath))
                    return;

                foreach (var file in Directory.EnumerateFiles(tempPath))
                {
                    DeleteTempFile(file);
                    _logger.LogInformation("Deleted stale loop preview file {LoopPreviewFile}.", file);
                }
                TempDirectoryHelper.DeleteIfEmpty(tempPath);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not cleanup stale loop preview files.");
            }
        }

        private string GetLoopPreviewTempPath()
        {
            return Path.Combine(GetTempPath(), "LoopPreviews");
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
                _logger.LogError(e, "pymusiclooper could not be launched.");
                throw new FileNotFoundException(
                    "pymusiclooper was not found. Please install it and add it to PATH. Restart the application after installing.",
                    "pymusiclooper",
                    e
                );
            }
            catch (InvalidOperationException e) when (IsPymusiclooperMissingError(e.Message))
            {
                _logger.LogError(e, "python was launched, but the pymusiclooper module was not found.");
                throw new FileNotFoundException(
                    "pymusiclooper was not found. Please install it and add it to PATH. Restart the application after installing.",
                    "pymusiclooper",
                    e
                );
            }
        }

        private static bool IsPymusiclooperMissingError(string message)
        {
            return !string.IsNullOrWhiteSpace(message) &&
                message.IndexOf("No module named pymusiclooper", StringComparison.OrdinalIgnoreCase) >= 0;
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

            _logger.LogInformation("Command exited: {Executable}. ExitCode={ExitCode}. StdOutLength={StdOutLength}. StdErrLength={StdErrLength}.",
                executable, process.ExitCode, output.Length, error.Length);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{executable} failed: {GetCommandErrorExcerpt(error, output)}");

            return string.Join(Environment.NewLine, new[] { output, error }.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static string GetCommandErrorExcerpt(params string[] messages)
        {
            const int maxLength = 4_000;
            var message = string.Join(Environment.NewLine, messages.Where(p => !string.IsNullOrWhiteSpace(p)));

            if (message.Length <= maxLength)
                return message;

            return "[Earlier command output omitted]" + Environment.NewLine + message.Substring(message.Length - maxLength);
        }

        private IReadOnlyList<AutoLoopPoint> ParsePymusiclooperOutput(string output, uint sampleRate, uint totalSamples)
        {
            var candidates = new List<AutoLoopPoint>();
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var skippedNonDataLines = 0;
            var skippedInvalidSampleLines = 0;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                var values = Regex.Matches(trimmedLine, @"[-+]?\d+(?:[.,]\d+)?")
                    .Cast<Match>()
                    .Select(p => p.Value.Replace(',', '.'))
                    .ToList();

                if (values.Count < 2)
                {
                    skippedNonDataLines++;
                    continue;
                }

                if (!uint.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var loopStartSample) ||
                    !uint.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var loopEndSample))
                {
                    skippedInvalidSampleLines++;
                    continue;
                }

                if (loopEndSample == 0 || loopStartSample > loopEndSample || loopEndSample > totalSamples)
                {
                    skippedInvalidSampleLines++;
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
                    ScoreText = scorePercentage == 100 ? "100%" : $"{scorePercentage:0.00}%",
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

            _logger.LogInformation(
                "Parsed pymusiclooper output. Lines={LineCount}, ValidCandidates={ValidCandidateCount}, SkippedNonDataLines={SkippedNonDataLines}, SkippedInvalidSampleLines={SkippedInvalidSampleLines}.",
                lines.Length, candidates.Count, skippedNonDataLines, skippedInvalidSampleLines);

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
            RunTool(GetSoxExe(), inputFile, outputFile, "trim", $"{startSourceSample}s", $"{durationSourceSample}s");
        }

    }
}
