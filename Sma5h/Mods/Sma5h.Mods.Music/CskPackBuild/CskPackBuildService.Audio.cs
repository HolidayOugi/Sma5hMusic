using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public partial class CskPackBuildService
    {
        #region BGM Files

        private string GenerateBgmFiles(IEnumerable<CskModContext> contexts, string tempRoot, HashSet<string> selectedSeriesKeys, JObject coreGameOverride)
        {
            ClearDirectory(tempRoot);

            var outputBgmFolder = Path.Combine(tempRoot, "stream;", "sound", "bgm");
            Directory.CreateDirectory(outputBgmFolder);

            _nus3AudioService.ResetGeneratedNus3BankIds();

            var bgmEntries = contexts
                .SelectMany(context => GetSelectedBgmBuildEntries(context, selectedSeriesKeys, coreGameOverride))
                .Where(p => !string.IsNullOrEmpty(p.NameId) && !string.IsNullOrEmpty(p.Filename))
                .GroupBy(p => p.NameId, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.First())
                .ToList();

            _logger.LogInformation("Generating {Count} nus3audio/nus3bank file(s) for CSK packs.", bgmEntries.Count);

            foreach (var bgmPropertyEntry in bgmEntries)
            {
                var nusBankOutputFile = Path.Combine(outputBgmFolder, string.Format(MusicConstants.GameResources.NUS3BANK_FILE, bgmPropertyEntry.NameId));
                var nusAudioOutputFile = Path.Combine(outputBgmFolder, string.Format(MusicConstants.GameResources.NUS3AUDIO_FILE, bgmPropertyEntry.NameId));

                _logger.LogInformation("Generating Nus3Bank for {NameId}", bgmPropertyEntry.NameId);
                _nus3AudioService.GenerateNus3Bank(bgmPropertyEntry.NameId, bgmPropertyEntry.AudioVolume, nusBankOutputFile);

                if (File.Exists(nusAudioOutputFile))
                    File.Delete(nusAudioOutputFile);

                _logger.LogInformation("Generating or copying Nus3Audio for {NameId}", bgmPropertyEntry.NameId);
                if (!_nus3AudioService.GenerateNus3Audio(bgmPropertyEntry.NameId, bgmPropertyEntry.Filename, nusAudioOutputFile))
                    throw new InvalidOperationException($"The song {bgmPropertyEntry.NameId} could not be processed from {bgmPropertyEntry.Filename}.");
            }

            return outputBgmFolder;
        }

        private IEnumerable<BgmBuildEntry> GetSelectedBgmBuildEntries(CskModContext context, HashSet<string> selectedSeriesKeys, JObject coreGameOverride)
        {
            foreach (var series in context.SeriesList.Where(series => selectedSeriesKeys.Contains(CreateSeriesKey(context.Mod, series))))
            {
                foreach (JObject game in GetArray(series, "games"))
                {
                    foreach (JObject bgm in GetArray(game, "bgms"))
                        yield return CreateBgmBuildEntry(context.Mod.ModPath, bgm);
                }

                foreach (var movedGame in GetCoreGameMovedGames(series, context.Metadata, coreGameOverride))
                {
                    foreach (JObject bgm in GetArray(movedGame, "bgms"))
                        yield return CreateBgmBuildEntry(context.Mod.ModPath, bgm);
                }
            }
        }

        private BgmBuildEntry CreateBgmBuildEntry(string modPath, JObject bgm)
        {
            var bgmProperty = bgm["bgm_properties"] as JObject;
            var nameId = GetString(bgmProperty, "name_id");
            var filename = GetString(bgm, "filename");

            return new BgmBuildEntry
            {
                NameId = nameId,
                Filename = Path.Combine(modPath, filename),
                AudioVolume = GetFloat(bgm["nus3bank_config"], "volume", 2.7f)
            };
        }

        #endregion

    }
}
