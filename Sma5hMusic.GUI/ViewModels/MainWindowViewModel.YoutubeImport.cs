using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Sma5hMusic.GUI.Interfaces;
using Sma5hMusic.GUI.Models;
using Sma5hMusic.GUI.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.ViewModels
{
    public partial class MainWindowViewModel
    {
        private readonly IYoutubeImportService _youtubeImportService;
        private readonly YoutubeImportModalWindowViewModel _vmYoutubeImport;

        public async Task AddNewYoutubeBgmEntry(ModEntryViewModel managerMod)
        {
            if (managerMod?.MusicMod == null)
            {
                await _messageDialog.ShowError("Error", "The mod could not be found.");
                return;
            }

            if (!_youtubeImportService.IsYtDlpConfigured())
            {
                await _messageDialog.ShowError(
                    "yt-dlp is not configured",
                    "Set the path to yt-dlp.exe in Global Settings before importing songs from YouTube."
                );
                return;
            }

            if (!_youtubeImportService.IsFfmpegConfigured())
            {
                await _messageDialog.ShowError(
                    "ffmpeg is not configured",
                    "Set the path to ffmpeg.exe in Global Settings before importing songs from YouTube."
                );
                return;
            }

            _vmYoutubeImport.Reset();

            var modalYoutubeImport = new YoutubeImportModalWindow
            {
                DataContext = _vmYoutubeImport
            };

            var result = await modalYoutubeImport.ShowDialog<YoutubeImportModalWindow>(_rootDialog.Window);

            if (result == null)
                return;

            if (_vmYoutubeImport.ImportFromTextFile)
            {
                await AddNewYoutubeBgmEntriesFromTextFile(managerMod);
                return;
            }

            bool isPlaylist;

            try
            {
                isPlaylist = await _youtubeImportService.IsPlaylist(_vmYoutubeImport.Url);
            }
            catch (Exception e)
            {
                await _messageDialog.ShowError("YouTube import failed", e.Message, e);
                return;
            }

            var links = new List<(string Url, bool IsPlaylist)>()
            {
                (_vmYoutubeImport.Url, isPlaylist)
            };

            await DownloadYoutubeLinksAndImport(managerMod, links, true);
        }

        private async Task AddNewYoutubeBgmEntriesFromTextFile(ModEntryViewModel managerMod)
        {
            var textFile = await _fileDialog.OpenFileDialogYoutubeLinksText(_rootDialog.Window);

            if (string.IsNullOrWhiteSpace(textFile))
                return;

            List<string> lines;

            try
            {
                lines = File.ReadAllLines(textFile)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();
            }
            catch (Exception e)
            {
                await _messageDialog.ShowError("YouTube import failed", e.Message, e);
                return;
            }

            var links = new List<(string Url, bool IsPlaylist)>();
            var invalidLinks = 0;

            foreach (var line in lines)
            {
                try
                {
                    var isPlaylist = await _youtubeImportService.IsPlaylist(line);
                    links.Add((line, isPlaylist));
                }
                catch
                {
                    invalidLinks++;
                }
            }

            if (links.Count == 0)
            {
                await _messageDialog.ShowInformation(
                    "YouTube import",
                    "No valid links were found."
                );
                return;
            }

            var singleSongs = links.Count(p => !p.IsPlaylist);
            var playlists = links.Count(p => p.IsPlaylist);

            var singleSongsText = singleSongs == 1 ? "single song" : "single songs";
            var playlistsText = playlists == 1 ? "playlist" : "playlists";
            var invalidLinksText = invalidLinks == 1 ? "invalid link" : "invalid links";

            await _messageDialog.ShowInformation(
                "YouTube import",
                $"Found {singleSongs} {singleSongsText}, {playlists} {playlistsText} and {invalidLinks} {invalidLinksText}. Starting download."
            );

            await DownloadYoutubeLinksAndImport(managerMod, links, false);
        }

        private async Task DownloadYoutubeLinksAndImport(
            ModEntryViewModel managerMod,
            IReadOnlyCollection<(string Url, bool IsPlaylist)> links,
            bool confirmPlaylists)
        {
            if (links == null || links.Count == 0)
                return;

            YoutubeDownloadProgressModalWindow progressWindow = null;
            YoutubeDownloadProgressModalWindowViewModel progressVm = null;
            Task progressDialogTask = null;

            var downloadedFiles = new List<string>();
            var downloads = new List<YoutubeDownloadResult>();
            var failedLinks = new List<string>();
            var totalSongs = 0;

            using var cancellationTokenSource = new CancellationTokenSource();

            var userCancelled = false;
            var downloadFailed = false;
            var closingProgressWindowProgrammatically = false;

            try
            {
                var hasPlaylist = links.Any(p => p.IsPlaylist);

                if (confirmPlaylists && hasPlaylist)
                {
                    var confirm = await _messageDialog.ShowWarningConfirm(
                        "Input is a playlist",
                        "Input is a playlist. All songs will be downloaded and processed. Do you want to continue?"
                    );

                    if (!confirm)
                        return;
                }

                foreach (var link in links)
                {
                    cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    if (link.IsPlaylist)
                    {
                        totalSongs += await _youtubeImportService.GetPlaylistItemCount(
                            link.Url,
                            cancellationTokenSource.Token
                        );
                    }
                    else
                    {
                        totalSongs++;
                    }
                }

                if (totalSongs <= 0)
                    totalSongs = links.Count;

                progressVm = new YoutubeDownloadProgressModalWindowViewModel();
                progressVm.SetProgress(0, totalSongs);

                progressWindow = new YoutubeDownloadProgressModalWindow
                {
                    DataContext = progressVm,
                    Width = 420,
                    Height = 170,
                    MinWidth = 420,
                    MinHeight = 170,
                    MaxWidth = 420,
                    MaxHeight = 170,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                progressWindow.Closing += (sender, args) =>
                {
                    if (closingProgressWindowProgrammatically)
                        return;

                    if (!cancellationTokenSource.IsCancellationRequested)
                    {
                        userCancelled = true;
                        cancellationTokenSource.Cancel();
                    }
                };

                progressDialogTask = progressWindow.ShowDialog(_rootDialog.Window);

                var completedSongs = 0;

                foreach (var link in links)
                {
                    cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    var completedBeforeThisDownload = completedSongs;

                    try
                    {
                        var download = await _youtubeImportService.DownloadAudio(
                            link.Url,
                            link.IsPlaylist,
                            totalSongs,
                            (current, total) =>
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    progressVm.SetProgress(completedBeforeThisDownload + current, totalSongs);
                                });
                            },
                            cancellationTokenSource.Token
                        );

                        downloads.Add(download);

                        var files = download.Filenames?
                            .Where(File.Exists)
                            .ToList() ?? new List<string>();

                        if (files.Count == 0 && File.Exists(download.Filename))
                            files.Add(download.Filename);

                        downloadedFiles.AddRange(files);

                        completedSongs += files.Count;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "YouTube download failed for URL {Url}. Continuing with the next link.", link.Url);

                        failedLinks.Add(link.Url);

                        // Count the failed single song as processed, otherwise the bar can remain stuck at 1/2, 31/32, etc.
                        if (!link.IsPlaylist)
                            completedSongs++;
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        progressVm.SetProgress(Math.Min(completedSongs, totalSongs), totalSongs);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                userCancelled = true;
            }
            catch (Exception e)
            {
                downloadFailed = true;
                await _messageDialog.ShowError("YouTube Import failed", e.Message, e);
                return;
            }
            finally
            {
                if (progressWindow != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        closingProgressWindowProgrammatically = true;

                        if (progressWindow.IsVisible)
                            progressWindow.Close();
                    });
                }

                if (progressDialogTask != null)
                    await progressDialogTask;

                if (userCancelled || downloadFailed)
                {
                    foreach (var download in downloads)
                        _youtubeImportService.CleanupDownload(download);
                }
            }

            if (userCancelled)
            {
                await _messageDialog.ShowInformation(
                    "YouTube Import cancelled",
                    "The YouTube Download was cancelled."
                );
                return;
            }

            if (downloadedFiles.Count == 0)
            {
                if (failedLinks.Count > 0)
                {
                    var songText = failedLinks.Count == 1 ? "song" : "songs";

                    await _messageDialog.ShowError(
                        "YouTube Import Failed",
                        $"yt-dlp could not find {failedLinks.Count} {songText}. Please check that the link is correct."
                    );
                }
                else
                {
                    await _messageDialog.ShowError(
                        "YouTube Import Failed",
                        "The YouTube Download completed, but no audio files were found."
                    );
                }

                return;
            }

            if (failedLinks.Count > 0)
            {
                var songText = failedLinks.Count == 1 ? "song" : "songs";

                await _messageDialog.ShowError(
                    "YouTube Import Warning",
                    $"yt-dlp could not find {failedLinks.Count} {songText}. Please check that the link is correct."
                );
            }

            try
            {
                await ImportAudioFiles(managerMod, downloadedFiles);
            }
            finally
            {
                foreach (var download in downloads)
                    _youtubeImportService.CleanupDownload(download);
            }
        }
    }
}
