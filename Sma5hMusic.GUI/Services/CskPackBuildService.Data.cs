using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music.Interfaces;
using System;
using System.Collections.Generic;

namespace Sma5hMusic.GUI.Services
{
    public partial class CskPackBuildService
    {
        private class CskModContext
        {
            public IMusicMod Mod { get; set; }
            public string MetadataPath { get; set; }
            public JObject Metadata { get; set; }
            public string PackName { get; set; }
            public string SafePackName { get; set; }
            public List<JObject> SeriesList { get; set; }
            public Dictionary<string, string> SeriesIdToName { get; set; }
        }

        private class CskBuildResources
        {
            public Dictionary<string, string> CoreGameSeriesById { get; set; }
            public HashSet<string> CoreBgmIds { get; set; }
            public JObject PlaylistData { get; set; }
            public JObject OrderOverride { get; set; }
            public JObject CoreBgmOverride { get; set; }
            public JObject CoreGameOverride { get; set; }
            public JObject CoreSeriesOverride { get; set; }
            public JObject StageOverride { get; set; }
        }

        private class BgmBuildEntry
        {
            public string NameId { get; set; }
            public float AudioVolume { get; set; }
            public string Filename { get; set; }
        }

        private enum CskPackBuildMode
        {
            Modular,
            Single
        }

        private static readonly HashSet<string> DlcSeries = new HashSet<string>(new[]
        {
            "persona", "dragonquest", "banjokazooie", "fatalfury", "arms", "minecraft", "tekken", "kingdomhearts"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> VanillaSeries = new HashSet<string>(new[]
        {
            "mario", "mariokart", "donkeykong", "zelda",
            "metroid", "yoshi", "kirby", "starfox", "pokemon", "fzero", "mother",
            "fireemblem", "gamewatch", "palutena", "wario", "pikmin",
            "doubutsu", "wiifit", "punchout", "xenoblade", "metalgear", "sonic",
            "rockman", "pacman", "streetfighter", "finalfantasy", "bayonetta",
            "splatoon", "castlevania", "smashbros", "arms", "persona",
            "dragonquest", "banjokazooie", "fatalfury", "minecraft",
            "tekken", "kingdomhearts", "etc"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, List<string>> SeriesToPlaylist = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["doubutsu"] = new List<string> { "bgmanimal" },
            ["bayonetta"] = new List<string> { "bgmbeyo" },
            ["dragonquest"] = new List<string> { "bgmbrave" },
            ["banjokazooie"] = new List<string> { "bgmbuddy" },
            ["tekken"] = new List<string> { "bgmdemon" },
            ["donkeykong"] = new List<string> { "bgmdk" },
            ["fatalfury"] = new List<string> { "bgmdolly" },
            ["castlevania"] = new List<string> { "bgmdracula" },
            ["finalfantasy"] = new List<string> { "bgmedge", "bgmff" },
            ["xenoblade"] = new List<string> { "bgmelement", "bgmxenoblade" },
            ["fireemblem"] = new List<string> { "bgmfe", "bgmmaster" },
            ["starfox"] = new List<string> { "bgmfox" },
            ["fzero"] = new List<string> { "bgmfzero" },
            ["gamewatch"] = new List<string> { "bgmgamewatch" },
            ["palutena"] = new List<string> { "bgmicaros" },
            ["persona"] = new List<string> { "bgmjack" },
            ["kirby"] = new List<string> { "bgmkirby" },
            ["mario"] = new List<string> { "bgmmario" },
            ["metalgear"] = new List<string> { "bgmmetalgear" },
            ["metroid"] = new List<string> { "bgmmetroid" },
            ["mariokart"] = new List<string> { "bgmmkart" },
            ["mother"] = new List<string> { "bgmmother" },
            ["etc"] = new List<string> { "bgmother" },
            ["pacman"] = new List<string> { "bgmpacman" },
            ["minecraft"] = new List<string> { "bgmpickel" },
            ["pikmin"] = new List<string> { "bgmpikmin" },
            ["pokemon"] = new List<string> { "bgmpokemon" },
            ["punchout"] = new List<string> { "bgmpunchout" },
            ["rockman"] = new List<string> { "bgmrockman" },
            ["streetfighter"] = new List<string> { "bgmsf" },
            ["smashbros"] = new List<string> { "bgmsmashbtl" },
            ["sonic"] = new List<string> { "bgmsonic" },
            ["splatoon"] = new List<string> { "bgmspla" },
            ["arms"] = new List<string> { "bgmtantan" },
            ["kingdomhearts"] = new List<string> { "bgmtrail" },
            ["wario"] = new List<string> { "bgmwario" },
            ["wiifit"] = new List<string> { "bgmwiifit" },
            ["yoshi"] = new List<string> { "bgmyoshi" },
            ["zelda"] = new List<string> { "bgmzelda" }
        };
    }
}
