using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5hMusic.GUI.Helpers;
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
    public partial class AudioImportService : IAudioImportService
    {
        private const uint TargetSampleRate = 48_000;
        private static readonly string[] SourceAudioExtensions =
        {
            ".mp3", ".flac", ".wav", ".ogg", ".m4a"
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
                var soxInputFile = CreateSoxCompatibleInputCopy(filename);

                try
                {
                    var sampleRate = ReadUInt(RunTool(GetSoxExe(), "--i", "-r", soxInputFile), "sample rate", filename);
                    var totalSamples = TryReadTotalSamples(soxInputFile);

                    if (totalSamples == 0)
                        throw new InvalidOperationException($"Could not determine the total sample count for '{Path.GetFileName(filename)}'.");

                    return new AudioImportInfo
                    {
                        SampleRate = sampleRate,
                        TotalSamples = totalSamples
                    };
                }
                finally
                {
                    DeleteSoxCompatibleInputCopy(filename, soxInputFile);
                }
            });
        }

        public bool IsNus3Audio(string filename)
        {
            return string.Equals(
                Path.GetExtension(filename),
                ".nus3audio",
                StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string> ConvertToNus3Audio(
            string toneId,
            string filename,
            string modPath,
            uint loopStartSample,
            uint loopEndSample,
            bool applyNormalization = false)
        {
            return await Task.Run(() =>
            {
                if (!RequiresConversion(filename))
                    return filename;

                Directory.CreateDirectory(modPath);
                Directory.CreateDirectory(GetTempPath());

                var tempId = Guid.NewGuid().ToString("N");
                var tempNormalizedWavFile = Path.Combine(GetTempPath(), $"{tempId}_normalized.wav");
                var tempWavFile = Path.Combine(GetTempPath(), $"{tempId}.wav");
                var tempLopusFile = Path.Combine(GetTempPath(), $"{tempId}.lopus");
                var outputFile = Path.Combine(modPath, $"{toneId}.nus3audio");

                if (File.Exists(outputFile))
                    throw new InvalidOperationException($"The destination file '{Path.GetFileName(outputFile)}' already exists in the selected mod.");

                var soxInputFile = CreateSoxCompatibleInputCopy(filename);

                try
                {
                    var info = GetAudioInfo(soxInputFile).GetAwaiter().GetResult();

                    if (loopEndSample == 0 || loopEndSample > info.TotalSamples)
                        throw new InvalidOperationException($"Loop end sample must be between 1 and {info.TotalSamples}.");

                    if (loopStartSample > loopEndSample)
                        throw new InvalidOperationException("Loop start sample must be lower than or equal to loop end sample.");

                    var loopStart48k = ConvertSampleRate(loopStartSample, info.SampleRate);
                    var loopEnd48k = ConvertSampleRate(loopEndSample, info.SampleRate);

                    if (applyNormalization)
                    {
                        var targetLufs = GetFfmpegLoudnormTarget();

                        _logger.LogInformation(
                            "Normalizing audio before import. Input={InputFile}, Output={NormalizedFile}, TargetLUFS={TargetLUFS}.",
                            filename,
                            tempNormalizedWavFile,
                            targetLufs
                        );

                        NormalizeAudioToWav(soxInputFile, tempNormalizedWavFile, targetLufs);

                        File.Copy(tempNormalizedWavFile, tempWavFile, true);
                    }
                    else
                    {
                        _logger.LogInformation("Converting {InputFile} to WAV 48kHz for import.", filename);

                        RunTool(
                            GetSoxExe(),
                            soxInputFile,
                            "-r", TargetSampleRate.ToString(CultureInfo.InvariantCulture),
                            "-b", "16",
                            "-e", "signed-integer",
                            tempWavFile
                        );
                    }

                    (loopStart48k, loopEnd48k) = FitLoopPointsToWav(tempWavFile, loopStart48k, loopEnd48k);

                    _logger.LogInformation("Encoding temporary LOPUS with loop {LoopStart}-{LoopEnd}.", loopStart48k, loopEnd48k);

                    var encoderOutput = RunTool(
                        GetVGAudioCliExe(),
                        tempWavFile,
                        tempLopusFile,
                        "-l", $"{loopStart48k}-{loopEnd48k}",
                        "--bitrate", "64000",
                        "--cbr",
                        "--opusheader",
                        "namco"
                    );

                    EnsureLopusCreated(tempLopusFile, encoderOutput);

                    _logger.LogInformation("Creating NUS3AUDIO {OutputFile}.", outputFile);

                    RunTool(GetNus3AudioExe(), "-n", "-w", outputFile);
                    RunTool(GetNus3AudioExe(), "-A", toneId, tempLopusFile, "-w", outputFile);

                    return outputFile;
                }
                finally
                {
                    DeleteSoxCompatibleInputCopy(filename, soxInputFile);
                    DeleteTempFile(tempNormalizedWavFile);
                    DeleteTempFile(tempWavFile);
                    DeleteTempFile(tempLopusFile);
                }
            });
        }

        private (uint LoopStartSample, uint LoopEndSample) FitLoopPointsToWav(
            string wavFile,
            uint loopStartSample,
            uint loopEndSample)
        {
            var wavInfo = GetAudioInfo(wavFile).GetAwaiter().GetResult();

            if (wavInfo.TotalSamples < 2)
                throw new InvalidOperationException("The generated WAV file is too short to contain valid loop points.");

            var maxLoopSample = wavInfo.TotalSamples - 1;

            if (loopStartSample > maxLoopSample)
                throw new InvalidOperationException($"Loop start sample must be lower than {wavInfo.TotalSamples} after audio conversion.");

            var adjustedLoopEndSample = Math.Min(loopEndSample, maxLoopSample);

            if (adjustedLoopEndSample < loopStartSample)
                throw new InvalidOperationException("Loop end sample became lower than loop start after audio conversion.");

            if (adjustedLoopEndSample != loopEndSample)
            {
                _logger.LogInformation(
                    "Adjusted loop end to fit generated WAV. File={WavFile}, RequestedLoopEnd={RequestedLoopEnd}, AdjustedLoopEnd={AdjustedLoopEnd}, TotalSamples={TotalSamples}.",
                    wavFile,
                    loopEndSample,
                    adjustedLoopEndSample,
                    wavInfo.TotalSamples
                );
            }

            return (loopStartSample, adjustedLoopEndSample);
        }

        private static void EnsureLopusCreated(string lopusFile, string encoderOutput)
        {
            if (File.Exists(lopusFile))
                return;

            var details = string.IsNullOrWhiteSpace(encoderOutput)
                ? string.Empty
                : $" VGAudioCli output: {encoderOutput.Trim()}";

            throw new InvalidOperationException($"VGAudioCli completed without creating the temporary LOPUS file.{details}");
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
                RunTool(GetSoxExe(), filename, "-r", TargetSampleRate.ToString(CultureInfo.InvariantCulture), "-b", "16", "-e", "signed-integer", tempWavFile);
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

        private string CreateSoxCompatibleInputCopy(string filename)
        {
            if (!filename.Any(p => p > 127))
                return filename;

            Directory.CreateDirectory(GetTempPath());

            var extension = Path.GetExtension(filename);
            var tempFilename = Path.Combine(GetTempPath(), $"{Guid.NewGuid():N}_source{extension}");

            _logger.LogInformation(
                "Copying audio source to a SoX-compatible temporary path. Original={OriginalFile}, Temporary={TemporaryFile}.",
                filename,
                tempFilename
            );

            File.Copy(filename, tempFilename);
            return tempFilename;
        }

        private static void DeleteSoxCompatibleInputCopy(string originalFilename, string inputFilename)
        {
            if (!string.Equals(originalFilename, inputFilename, StringComparison.OrdinalIgnoreCase))
                DeleteTempFile(inputFilename);
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
