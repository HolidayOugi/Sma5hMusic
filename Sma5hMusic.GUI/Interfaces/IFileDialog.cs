using Avalonia.Controls;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Interfaces
{
    public interface IFileDialog
    {
        Task<string[]> OpenFileDialogAudioMultiple(Window parent = null);
        Task<string> OpenFileDialogAudioSingle(Window parent = null);
        Task<string> OpenFileDialogImageSingle(Window parent = null);
        Task<string> OpenFileDialogYtDlp(Window parent = null);
        Task<string> OpenFileDialogFfmpeg(Window parent = null);
        Task<string> OpenFileDialogYoutubeLinksText(Window parent = null);
        Task<string> OpenFolderDialog(Window parent = null);
        Task<string> SaveFileCSVDialog(Window parent = null);
        void OpenFolder(string folderPath);
    }
}
