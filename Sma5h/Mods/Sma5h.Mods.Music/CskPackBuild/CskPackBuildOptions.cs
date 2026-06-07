using Sma5h.Mods.Music;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public class CskPackBuildOptions : Sma5hMusicOverrideOptions
    {
        #region Properties

        public Sma5hMusicOptions.Sma5hMusicOptionsSection Sma5hMusic { get; set; }

        public CskPackBuildGuiOptionsSection Sma5hMusicGUI { get; set; }

        #endregion

        public class CskPackBuildGuiOptionsSection
        {
            public string DefaultGUILocale { get; set; }
            public string DefaultMSBTLocale { get; set; }
        }
    }
}
