using Sma5h.Mods.Music.Interfaces;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Interfaces
{
    public interface ISongSpreadsheetService
    {
        bool HasSongListEntries(IMusicMod musicMod, string locale);
        bool HasPinchSongEntries(IMusicMod musicMod, string locale);
        bool HasMainMenuSongEntries(IMusicMod musicMod, string locale);
        Task<bool> CreateSongList(string outputPath, IMusicMod musicMod, string locale);
        Task<bool> CreatePinchSongs(string outputPath, IMusicMod musicMod, string locale);
        Task<bool> CreateMainMenuSongs(string outputPath, IMusicMod musicMod, string locale);
    }
}
