using Sma5h.Mods.Music.MusicMods.MusicModModels;
using Sma5h.Mods.Music.Models;

namespace Sma5h.Mods.Music.Interfaces
{
    public interface IMusicModReverseService
    {
        MusicModConfig Reverse(string coreResourcesPath, string outputPath, string modOutputPath, string modName = null, MusicModInformation modInformation = null);
    }
}
