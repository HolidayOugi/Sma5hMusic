using Sma5hMusic.GUI.Models;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Interfaces
{
    public interface IYoutubeImportService
    {
        bool IsYtDlpConfigured();
        bool IsFfmpegConfigured();
        Task<YoutubeDownloadResult> DownloadAudio(string url);
        void CleanupDownload(YoutubeDownloadResult download);
    }
}
