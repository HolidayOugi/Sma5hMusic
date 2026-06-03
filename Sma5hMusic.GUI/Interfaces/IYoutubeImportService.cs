using Sma5hMusic.GUI.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Interfaces
{
    public interface IYoutubeImportService
    {
        bool IsYtDlpConfigured();
        bool IsFfmpegConfigured();

        Task<bool> IsPlaylist(string url);
        Task<int> GetPlaylistItemCount(string url, CancellationToken cancellationToken = default);

        Task<YoutubeDownloadResult> DownloadAudio(
            string url,
            bool allowPlaylist = false,
            int playlistTotal = 0,
            Action<int, int> onProgress = null,
            CancellationToken cancellationToken = default);

        void CleanupDownload(YoutubeDownloadResult download);
    }
}