using Sma5hMusic.GUI.Models;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Interfaces
{
    public interface IYoutubeImportService
    {
        bool IsYtDlpConfigured();
        bool IsFfmpegConfigured();
        Task<bool> IsPlaylist(string url);
        Task<YoutubeDownloadResult> DownloadAudio(string url, bool allowPlaylist = false);
        void CleanupDownload(YoutubeDownloadResult download);
    }
}
