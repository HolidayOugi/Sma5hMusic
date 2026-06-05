using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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

        public bool IsFfmpegConfigured()
        {
            var executable = _config.CurrentValue.FfmpegPath;
            return !string.IsNullOrWhiteSpace(executable) && File.Exists(executable);
        }

        public async Task<string> NormalizeNus3Audio(
            string toneId,
            string filename,
            string modPath)
        {
            return await Task.Run(() =>
            {
                if (!IsNus3Audio(filename))
                    throw new InvalidOperationException($"'{Path.GetFileName(filename)}' is not a NUS3AUDIO file.");

                Directory.CreateDirectory(modPath);
                Directory.CreateDirectory(GetTempPath());

                var outputFile = Path.Combine(modPath, $"{toneId}.nus3audio");

                if (File.Exists(outputFile))
                    throw new InvalidOperationException($"The destination file '{Path.GetFileName(outputFile)}' already exists in the selected mod.");

                return NormalizeNus3AudioToFile(toneId, filename, outputFile);
            });
        }

        public async Task<string> NormalizeExistingNus3Audio(
            string toneId,
            string filename)
        {
            return await Task.Run(() =>
            {
                if (!IsNus3Audio(filename))
                    throw new InvalidOperationException($"'{Path.GetFileName(filename)}' is not a NUS3AUDIO file.");

                if (!File.Exists(filename))
                    throw new FileNotFoundException($"The NUS3AUDIO file '{filename}' could not be found.", filename);

                Directory.CreateDirectory(GetTempPath());

                var tempId = Guid.NewGuid().ToString("N");
                var normalizedNus3AudioFile = Path.Combine(GetTempPath(), $"{tempId}.nus3audio");

                try
                {
                    NormalizeNus3AudioToFile(toneId, filename, normalizedNus3AudioFile);
                    File.Copy(normalizedNus3AudioFile, filename, true);
                    return filename;
                }
                finally
                {
                    DeleteTempFile(normalizedNus3AudioFile);
                }
            });
        }

        private string NormalizeNus3AudioToFile(
            string toneId,
            string filename,
            string outputFile)
        {
            var tempId = Guid.NewGuid().ToString("N");
            var extractedWavFile = Path.Combine(GetTempPath(), $"{tempId}_source.wav");
            var normalizedWavFile = Path.Combine(GetTempPath(), $"{tempId}_normalized.wav");
            var tempLopusFile = Path.Combine(GetTempPath(), $"{tempId}.lopus");

            try
            {
                var loopPoints = ExtractNus3AudioLoopPoints(filename);

                if (loopPoints == null)
                    throw new InvalidOperationException($"Could not read loop points from '{Path.GetFileName(filename)}'.");

                _logger.LogInformation(
                    "Extracted NUS3AUDIO loop points. File={File}, LoopStart={LoopStart}, LoopEnd={LoopEnd}.",
                    filename,
                    loopPoints.Value.LoopStartSample,
                    loopPoints.Value.LoopEndSample
                );

                ExtractNus3AudioToWavFile(filename, extractedWavFile);

                var extractedInfo = GetAudioInfo(extractedWavFile).GetAwaiter().GetResult();

                var loopStart48k = ConvertSampleRate(loopPoints.Value.LoopStartSample, extractedInfo.SampleRate);
                var loopEnd48k = ConvertSampleRate(loopPoints.Value.LoopEndSample, extractedInfo.SampleRate);

                var targetLufs = GetFfmpegLoudnormTarget();

                _logger.LogInformation(
                    "Normalizing extracted NUS3AUDIO WAV. Input={InputFile}, Output={OutputFile}, TargetLUFS={TargetLUFS}.",
                    extractedWavFile,
                    normalizedWavFile,
                    targetLufs
                );

                NormalizeAudioToWav(extractedWavFile, normalizedWavFile, targetLufs);
                (loopStart48k, loopEnd48k) = FitLoopPointsToWav(normalizedWavFile, loopStart48k, loopEnd48k);

                _logger.LogInformation(
                    "Encoding normalized NUS3AUDIO WAV to LOPUS with old loop points {LoopStart}-{LoopEnd}.",
                    loopStart48k,
                    loopEnd48k
                );

                var encoderOutput = RunTool(
                    GetVGAudioCliExe(),
                    normalizedWavFile,
                    tempLopusFile,
                    "-l",
                    $"{loopStart48k}-{loopEnd48k}",
                    "--bitrate",
                    "64000",
                    "--cbr",
                    "--opusheader",
                    "namco"
                );

                EnsureLopusCreated(tempLopusFile, encoderOutput);

                _logger.LogInformation("Creating normalized NUS3AUDIO {OutputFile}.", outputFile);

                RunTool(GetNus3AudioExe(), "-n", "-w", outputFile);
                RunTool(GetNus3AudioExe(), "-A", toneId, tempLopusFile, "-w", outputFile);

                return outputFile;
            }
            finally
            {
                DeleteTempFile(extractedWavFile);
                DeleteTempFile(normalizedWavFile);
                DeleteTempFile(tempLopusFile);
            }
        }

        private (uint LoopStartSample, uint LoopEndSample)? ExtractNus3AudioLoopPoints(string filename)
        {
            var output = RunTool(GetVgmStreamExe(), "-m", filename);

            uint? loopStart = null;
            uint? loopEnd = null;

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var startMatch = Regex.Match(line, @"loop start:\s*(\d+)", RegexOptions.IgnoreCase);
                var endMatch = Regex.Match(line, @"loop end:\s*(\d+)", RegexOptions.IgnoreCase);

                if (startMatch.Success && uint.TryParse(startMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLoopStart))
                    loopStart = parsedLoopStart;

                if (endMatch.Success && uint.TryParse(endMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLoopEnd))
                    loopEnd = parsedLoopEnd;
            }

            if (loopStart.HasValue && loopEnd.HasValue)
                return (loopStart.Value, loopEnd.Value);

            return null;
        }

        private double GetAudioNormalizationTargetLufs()
        {
            var value = _config.CurrentValue.Sma5hMusicGUI?.AudioNormalizationTargetLufs ?? 0;

            return value > 0
                ? value
                : 14;
        }

        private double GetFfmpegLoudnormTarget()
        {
            return -Math.Abs(GetAudioNormalizationTargetLufs());
        }

        private void NormalizeAudioToWav(string inputFile, string outputFile, double targetLufs)
        {
            var outputDirectory = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var firstPassFilter = string.Format(
                CultureInfo.InvariantCulture,
                "loudnorm=I={0}:TP=-1:LRA=11:print_format=json",
                targetLufs
            );

            var firstPassOutput = RunFfmpeg(
                "-y",
                "-i", inputFile,
                "-af", firstPassFilter,
                "-f", "null",
                "-"
            );

            var stats = ParseLoudnormStats(firstPassOutput);

            var secondPassFilter = string.Format(
                CultureInfo.InvariantCulture,
                "loudnorm=I={0}:TP=-1:LRA=11:measured_I={1}:measured_LRA={2}:measured_TP={3}:measured_thresh={4}:offset={5}:linear=true:print_format=summary",
                targetLufs,
                stats["input_i"],
                stats["input_lra"],
                stats["input_tp"],
                stats["input_thresh"],
                stats["target_offset"]
            );

            RunFfmpeg(
                "-y",
                "-i", inputFile,
                "-af", secondPassFilter,
                "-ar", TargetSampleRate.ToString(CultureInfo.InvariantCulture),
                "-acodec", "pcm_s16le",
                outputFile
            );

            if (!File.Exists(outputFile))
                throw new InvalidOperationException("Audio normalization completed, but the normalized WAV file could not be found.");
        }

        private Dictionary<string, string> ParseLoudnormStats(string output)
        {
            var keys = new[]
            {
                "input_i",
                "input_tp",
                "input_lra",
                "input_thresh",
                "target_offset"
            };

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in keys)
            {
                var match = Regex.Match(
                    output,
                    $"\"{Regex.Escape(key)}\"\\s*:\\s*\"?([^\",\\r\\n}}]+)\"?",
                    RegexOptions.IgnoreCase
                );

                if (!match.Success)
                    throw new InvalidOperationException($"Could not read loudnorm value '{key}' from ffmpeg output.");

                result[key] = match.Groups[1].Value.Trim();
            }

            return result;
        }

        private string RunFfmpeg(params string[] arguments)
        {
            var executable = _config.CurrentValue.FfmpegPath;

            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                throw new FileNotFoundException("ffmpeg.exe is not configured. Set its path in Global Settings.", executable);

            _logger.LogInformation(
                "Running ffmpeg: {Executable} {Arguments}",
                executable,
                string.Join(" ", arguments.Select(p => $"\"{p}\""))
            );

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Prevent ffmpeg from trying to read interactive input.
            startInfo.ArgumentList.Add("-nostdin");

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            var stdout = new System.Text.StringBuilder();
            var stderr = new System.Text.StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;

                stdout.AppendLine(e.Data);
                _logger.LogInformation("ffmpeg stdout: {Line}", e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;

                stderr.AppendLine(e.Data);
                _logger.LogInformation("ffmpeg stderr: {Line}", e.Data);
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();

            var output = stdout.ToString();
            var error = stderr.ToString();

            _logger.LogInformation(
                "ffmpeg exited. ExitCode={ExitCode}. StdOut={StdOut}. StdErr={StdErr}",
                process.ExitCode,
                output,
                error
            );

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg failed: {error}{output}");

            return string.Join(
                Environment.NewLine,
                new[] { output, error }.Where(p => !string.IsNullOrWhiteSpace(p))
            );
        }

    }
}
