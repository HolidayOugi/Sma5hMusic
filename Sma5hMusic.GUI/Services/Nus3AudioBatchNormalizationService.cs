using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5hMusic.GUI.Interfaces;
using Sma5hMusic.GUI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Services
{
    public class Nus3AudioBatchNormalizationService : INus3AudioBatchNormalizationService
    {
        private readonly IAudioImportService _audioImportService;
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<ApplicationSettings> _config;

        public Nus3AudioBatchNormalizationService(
            IAudioImportService audioImportService,
            IOptionsMonitor<ApplicationSettings> config,
            ILogger<Nus3AudioBatchNormalizationService> logger)
        {
            _audioImportService = audioImportService;
            _config = config;
            _logger = logger;
        }

        public IReadOnlyList<string> GetNus3AudioFiles(string musicModsPath)
        {
            if (string.IsNullOrWhiteSpace(musicModsPath) || !Directory.Exists(musicModsPath))
                return new List<string>();

            return Directory
                .EnumerateFiles(musicModsPath, "*.nus3audio", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public Task<Nus3AudioBatchNormalizationResult> NormalizeFiles(
            IReadOnlyList<string> files,
            string musicModsPath,
            Action<int, int, string> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                var result = new Nus3AudioBatchNormalizationResult
                {
                    TotalFiles = files?.Count ?? 0
                };

                if (files == null || files.Count == 0)
                    return result;

                for (var i = 0; i < files.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var file = files[i];
                    var relativeFile = GetRelativePath(musicModsPath, file);

                    onProgress?.Invoke(i, files.Count, relativeFile);

                    try
                    {
                        await NormalizeFile(file, cancellationToken);
                        result.NormalizedFiles++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        result.FailedFiles.Add(relativeFile);
                        _logger.LogError(e, "Could not normalize NUS3AUDIO file {Filename}", file);
                    }

                    onProgress?.Invoke(i + 1, files.Count, relativeFile);
                }

                return result;
            }, cancellationToken);
        }

        private async Task NormalizeFile(string filename, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var toneId = Path.GetFileNameWithoutExtension(filename);
            var tempDirectory = Path.Combine(
                _config.CurrentValue.TempPath,
                "Nus3AudioBatchNormalization",
                Guid.NewGuid().ToString("N")
            );

            Directory.CreateDirectory(tempDirectory);

            try
            {
                var normalizedFile = await _audioImportService.NormalizeNus3Audio(
                    toneId,
                    filename,
                    tempDirectory
                );

                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(normalizedFile) || !File.Exists(normalizedFile))
                    throw new InvalidOperationException($"The normalized file for '{Path.GetFileName(filename)}' was not created.");

                File.Copy(normalizedFile, filename, true);
            }
            finally
            {
                DeleteTempDirectory(tempDirectory);
            }
        }

        private static string GetRelativePath(string rootPath, string filename)
        {
            var fullRootPath = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var fullFilename = Path.GetFullPath(filename);

            return fullFilename.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase)
                ? fullFilename.Substring(fullRootPath.Length)
                : filename;
        }

        private static void DeleteTempDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}