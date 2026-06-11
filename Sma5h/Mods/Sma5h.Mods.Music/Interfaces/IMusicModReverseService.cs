using Sma5h.Mods.Music.MusicMods.MusicModModels;

namespace Sma5h.Mods.Music.Interfaces
{
    public interface IMusicModReverseService
    {
        MusicModConfig Reverse(string coreResourcesPath, string outputPath, string modOutputPath, string modName = null);
    }
}
