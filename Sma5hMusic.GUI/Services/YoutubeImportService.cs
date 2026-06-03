using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5hMusic.GUI.Interfaces;
using Sma5hMusic.GUI.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Services
{
    public class YoutubeImportService : IYoutubeImportService
    {
        private readonly IOptionsMonitor<ApplicationSettings> _config;
        private readonly ILogger _logger;

        public YoutubeImportService(IOptionsMonitor<ApplicationSettings> config, ILogger<YoutubeImportService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public bool IsYtDlpConfigured()
        {
            var executable = _config.CurrentValue.YtDlpPath;
            return !string.IsNullOrWhiteSpace(executable) && File.Exists(executable);
        }

        public bool IsFfmpegConfigured()
        {
            var executable = _config.CurrentValue.FfmpegPath;
            return !string.IsNullOrWhiteSpace(executable) && File.Exists(executable);
        }


        public async Task<YoutubeDownloadResult> DownloadAudio(string url)
        {
            return await Task.Run(() =>
            {
                ValidateYoutubeUrl(url);

                var ytexecutable = _config.CurrentValue.YtDlpPath;
                var ffmpegExecutable = _config.CurrentValue.FfmpegPath;
                if (string.IsNullOrWhiteSpace(ytexecutable) || !File.Exists(ytexecutable))
                    throw new FileNotFoundException("yt-dlp.exe is not configured. Set its path in Global Settings.", ytexecutable);

                if (string.IsNullOrWhiteSpace(ffmpegExecutable) || !File.Exists(ffmpegExecutable))
                    throw new FileNotFoundException("ffmpeg.exe is not configured. Set its path in Global Settings.", ffmpegExecutable);

                var tempRoot = GetTempRoot();
                var tempDirectory = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);

                try
                {
                    var outputTemplate = Path.Combine(tempDirectory, "%(title)s.%(ext)s");
                    var output = RunYtDlp(ytexecutable,
                        "--no-playlist",
                        "--no-progress",
                        "--format", "bestaudio/best",
                        "--extract-audio",
                        "--ffmpeg-location", ffmpegExecutable,
                        "--audio-format", "mp3",
                        "--audio-quality", "0",
                        "--print", "after_move:filepath",
                        "--output", outputTemplate,
                        url);

                    var filename = output
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim().Trim('"'))
                        .LastOrDefault(File.Exists);

                    if (string.IsNullOrWhiteSpace(filename))
                        filename = Directory.EnumerateFiles(tempDirectory, "*.mp3").SingleOrDefault();

                    if (string.IsNullOrWhiteSpace(filename) || !File.Exists(filename))
                        throw new InvalidOperationException("yt-dlp completed, but the downloaded audio file could not be found.");

                    _logger.LogInformation("YouTube audio download completed. Url={Url}, Filename={Filename}.", url, filename);
                    return new YoutubeDownloadResult
                    {
                        Filename = filename,
                        TempDirectory = tempDirectory
                    };
                }
                catch
                {
                    DeleteTempDirectory(tempDirectory);
                    throw;
                }
            });
        }

        public void CleanupDownload(YoutubeDownloadResult download)
        {
            if (download == null || string.IsNullOrWhiteSpace(download.TempDirectory))
                return;

            DeleteTempDirectory(download.TempDirectory);
        }

        private string RunYtDlp(string executable, params string[] arguments)
        {
            _logger.LogInformation("Running yt-dlp: {Executable} {Arguments}", executable, string.Join(" ", arguments.Select(p => $"\"{p}\"")));

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

            _logger.LogInformation("yt-dlp exited. ExitCode={ExitCode}. StdOut={StdOut}. StdErr={StdErr}", process.ExitCode, output, error);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"yt-dlp failed: {error}{output}");

            return output;
        }

        private static void ValidateYoutubeUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !IsYoutubeHost(uri.Host))
            {
                throw new InvalidOperationException("Enter a valid YouTube URL.");
            }
        }

        private static bool IsYoutubeHost(string host)
        {
            return string.Equals(host, "youtu.be", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "youtube.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
        }

        private string GetTempRoot()
        {
            return Path.GetFullPath(Path.Combine(_config.CurrentValue.TempPath, "YoutubeImport"));
        }

        private void DeleteTempDirectory(string directory)
        {
            try
            {
                var tempRoot = GetTempRoot();
                var fullDirectory = Path.GetFullPath(directory);
                var rootPrefix = tempRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                if (!fullDirectory.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Refusing to delete YouTube import directory outside the configured temp root: {Directory}", fullDirectory);
                    return;
                }

                if (Directory.Exists(fullDirectory))
                {
                    Directory.Delete(fullDirectory, true);
                    _logger.LogInformation("Deleted temporary YouTube import directory {Directory}.", fullDirectory);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not delete temporary YouTube import directory {Directory}.", directory);
            }
        }
    }
}
