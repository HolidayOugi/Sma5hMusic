using AutoMapper;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using paracobNET;
using Sma5h.Data;
using Sma5h.Data.Ui.Param.Database;
using Sma5h.Helpers;
using Sma5h.Interfaces;
using Sma5h.Mods.Data.Sound.Config;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.MusicMods;
using Sma5h.Mods.Music.MusicMods.MusicModModels;
using Sma5h.ResourceProviders;
using Sma5h.ResourceProviders.Constants;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sma5h.Mods.Music.Services
{
    public class MusicModReverseService : IMusicModReverseService
    {
        private readonly ILogger _logger;
        private readonly IMapper _mapper;
        private readonly PrcResourceProvider _prcProvider;
        private readonly MsbtResourceProvider _msbtProvider;
        private readonly BgmPropertyProvider _bgmPropertyProvider;

        public MusicModReverseService(IEnumerable<IResourceProvider> resourceProviders, IMapper mapper, ILogger<MusicModReverseService> logger)
        {
            _logger = logger;
            _mapper = mapper;
            _prcProvider = resourceProviders.OfType<PrcResourceProvider>().First();
            _msbtProvider = resourceProviders.OfType<MsbtResourceProvider>().First();
            _bgmPropertyProvider = resourceProviders.OfType<BgmPropertyProvider>().First();
        }

        public MusicModConfig Reverse(string coreResourcesPath, string outputPath, string modOutputPath, string modName = null, MusicModInformation modInformation = null)
        {
            if (string.IsNullOrWhiteSpace(coreResourcesPath))
                throw new ArgumentException("Core resources path is required.", nameof(coreResourcesPath));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));
            if (string.IsNullOrWhiteSpace(modOutputPath))
                throw new ArgumentException("Mod output path is required.", nameof(modOutputPath));

            var core = LoadSnapshot(coreResourcesPath);
            var output = LoadSnapshot(outputPath);
            var newBgmIds = output.BgmDbRootEntries.Keys.Except(core.BgmDbRootEntries.Keys).OrderBy(p => p).ToList();

            _logger.LogInformation("Reverse MusicMod: found {BgmCount} new BGM entry/entries.", newBgmIds.Count);

            var metadata = new MusicModConfig(Guid.NewGuid().ToString())
            {
                Name = !string.IsNullOrWhiteSpace(modInformation?.Name)
                    ? modInformation.Name
                    : string.IsNullOrWhiteSpace(modName) ? Path.GetFileName(Path.TrimEndingDirectorySeparator(modOutputPath)) : modName,
                Author = modInformation?.Author,
                Website = modInformation?.Website,
                Description = modInformation?.Description,
                Series = new List<SeriesConfig>()
            };

            var seriesById = new Dictionary<string, SeriesConfig>();
            var gameById = new Dictionary<string, GameConfig>();

            Directory.CreateDirectory(modOutputPath);

            foreach (var uiBgmId in newBgmIds)
            {
                var dbRoot = output.BgmDbRootEntries[uiBgmId];
                if (!output.GameTitleEntries.TryGetValue(dbRoot.UiGameTitleId, out var gameTitle))
                {
                    _logger.LogWarning("Skipping {UiBgmId}: game title {GameTitleId} was not found.", uiBgmId, dbRoot.UiGameTitleId);
                    continue;
                }

                if (!output.SeriesEntries.TryGetValue(gameTitle.UiSeriesId, out var seriesEntry))
                {
                    _logger.LogWarning("Skipping {UiBgmId}: series {SeriesId} was not found.", uiBgmId, gameTitle.UiSeriesId);
                    continue;
                }

                var streamSet = output.StreamSetEntries.GetValueOrDefault(dbRoot.StreamSetId);
                var infoId = GetFirstInfoId(streamSet);
                var assignedInfo = infoId != null ? output.AssignedInfoEntries.GetValueOrDefault(infoId) : null;
                var streamProperty = assignedInfo != null ? output.StreamPropertyEntries.GetValueOrDefault(assignedInfo.StreamId) : null;
                var toneId = GetToneId(streamProperty, dbRoot);
                var bgmProperty = toneId != null ? output.BgmPropertyEntries.GetValueOrDefault(toneId) : null;

                if (streamSet == null || assignedInfo == null || streamProperty == null || bgmProperty == null)
                {
                    _logger.LogWarning("Skipping {UiBgmId}: linked stream/property records are incomplete. StreamSetId={StreamSetId} HasStreamSet={HasStreamSet} InfoId={InfoId} HasAssignedInfo={HasAssignedInfo} StreamId={StreamId} HasStreamProperty={HasStreamProperty} ToneId={ToneId} HasBgmProperty={HasBgmProperty}",
                        uiBgmId,
                        dbRoot.StreamSetId,
                        streamSet != null,
                        infoId,
                        assignedInfo != null,
                        assignedInfo?.StreamId,
                        streamProperty != null,
                        toneId,
                        bgmProperty != null);
                    continue;
                }

                if (!seriesById.TryGetValue(seriesEntry.UiSeriesId, out var seriesConfig))
                {
                    seriesConfig = _mapper.Map<SeriesConfig>(seriesEntry);
                    seriesConfig.Games = new List<GameConfig>();
                    seriesById.Add(seriesEntry.UiSeriesId, seriesConfig);
                    metadata.Series.Add(seriesConfig);
                }

                if (!gameById.TryGetValue(gameTitle.UiGameTitleId, out var gameConfig))
                {
                    gameConfig = _mapper.Map<GameConfig>(gameTitle);
                    gameConfig.Bgms = new List<BgmConfig>();
                    gameById.Add(gameTitle.UiGameTitleId, gameConfig);
                    seriesConfig.Games.Add(gameConfig);
                }

                var filename = $"{toneId}.nus3audio";
                CopyNus3Audio(outputPath, toneId, Path.Combine(modOutputPath, filename));

                gameConfig.Bgms.Add(new BgmConfig
                {
                    ToneId = toneId,
                    Filename = filename,
                    NUS3BankConfig = new NUS3BankConfig
                    {
                        AudioVolume = ReadNus3BankVolume(outputPath, toneId)
                    },
                    BgmProperties = _mapper.Map<BgmPropertyEntryConfig>(bgmProperty),
                    DbRoot = _mapper.Map<BgmDbRootConfig>(dbRoot),
                    AssignedInfo = _mapper.Map<BgmAssignedInfoConfig>(assignedInfo),
                    StreamSet = _mapper.Map<BgmStreamSetConfig>(streamSet),
                    StreamProperty = _mapper.Map<BgmStreamPropertyConfig>(streamProperty)
                });
            }

            var metadataPath = Path.Combine(modOutputPath, MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE);
            File.WriteAllText(metadataPath, JsonConvert.SerializeObject(metadata, Formatting.Indented), new UTF8Encoding(false));
            _logger.LogInformation("Reverse MusicMod: wrote {MetadataPath}.", metadataPath);

            return metadata;
        }

        private ResourceSnapshot LoadSnapshot(string rootPath)
        {
            var bgmDbPath = Path.Combine(rootPath, PrcExtConstants.PRC_UI_BGM_DB_PATH);
            var gameTitleDbPath = Path.Combine(rootPath, PrcExtConstants.PRC_UI_GAMETITLE_DB_PATH);
            var seriesDbPath = Path.Combine(rootPath, PrcExtConstants.PRC_UI_SERIES_DB_PATH);
            var bgmPropertyPath = Path.Combine(rootPath, BgmPropertyFileConstants.BGM_PROPERTY_PATH);

            EnsureSnapshotFileExists(bgmDbPath);
            EnsureSnapshotFileExists(gameTitleDbPath);
            EnsureSnapshotFileExists(seriesDbPath);
            EnsureSnapshotFileExists(bgmPropertyPath);

            var bgmDb = _prcProvider.ReadFile<PrcUiBgmDatabase>(bgmDbPath, true);
            var gameTitleDb = _prcProvider.ReadFile<PrcUiGameTitleDatabase>(gameTitleDbPath);
            var seriesDb = _prcProvider.ReadFile<PrcUiSeriesDatabase>(seriesDbPath);
            var bgmProperty = _bgmPropertyProvider.ReadFile<BinBgmProperty>(bgmPropertyPath);

            if (bgmDb == null || gameTitleDb == null || seriesDb == null || bgmProperty == null)
                throw new InvalidOperationException($"Could not read required music resources from {rootPath}.");

            var snapshot = new ResourceSnapshot();
            var bgmMsbts = LoadMsbtDatabases(rootPath, MsbtExtConstants.MSBT_BGM);
            var titleMsbts = LoadMsbtDatabases(rootPath, MsbtExtConstants.MSBT_TITLE);
            var toneIds = GetToneIds(rootPath, bgmProperty);
            var seriesIds = new Dictionary<string, string>();
            var gameTitleIds = new Dictionary<string, string>();

            foreach (var value in seriesDb.DbRootEntries.Values)
                AddGeneratedId(seriesIds, value.UiSeriesId, MusicConstants.InternalIds.SERIES_ID_PREFIX, value.NameId);

            foreach (var value in gameTitleDb.DbRootEntries.Values)
                AddGeneratedId(gameTitleIds, value.UiGameTitleId, MusicConstants.InternalIds.GAME_TITLE_ID_PREFIX, value.NameId);

            foreach (var value in bgmDb.DbRootEntries.Values)
            {
                value.UiBgmId = ResolveGeneratedId(value.UiBgmId, toneIds, MusicConstants.InternalIds.UI_BGM_ID_PREFIX);
                value.StreamSetId = ResolveGeneratedId(value.StreamSetId, toneIds, MusicConstants.InternalIds.STREAM_SET_PREFIX);
                value.UiGameTitleId = ResolveKnownId(value.UiGameTitleId, gameTitleIds);
                value.UiGameTitleId1 = ResolveKnownId(value.UiGameTitleId1, gameTitleIds);
                value.UiGameTitleId2 = ResolveKnownId(value.UiGameTitleId2, gameTitleIds);
                value.UiGameTitleId3 = ResolveKnownId(value.UiGameTitleId3, gameTitleIds);
                value.UiGameTitleId4 = ResolveKnownId(value.UiGameTitleId4, gameTitleIds);

                var entry = _mapper.Map(value, new BgmDbRootEntry(value.UiBgmId));
                FillBgmMsbt(entry, bgmMsbts);
                snapshot.BgmDbRootEntries.Add(entry.UiBgmId, entry);
            }

            foreach (var value in bgmDb.StreamSetEntries.Values)
            {
                value.StreamSetId = ResolveGeneratedId(value.StreamSetId, toneIds, MusicConstants.InternalIds.STREAM_SET_PREFIX);
                value.Info0 = ResolveGeneratedId(value.Info0, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info1 = ResolveGeneratedId(value.Info1, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info2 = ResolveGeneratedId(value.Info2, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info3 = ResolveGeneratedId(value.Info3, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info4 = ResolveGeneratedId(value.Info4, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info5 = ResolveGeneratedId(value.Info5, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info6 = ResolveGeneratedId(value.Info6, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info7 = ResolveGeneratedId(value.Info7, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info8 = ResolveGeneratedId(value.Info8, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info9 = ResolveGeneratedId(value.Info9, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info10 = ResolveGeneratedId(value.Info10, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info11 = ResolveGeneratedId(value.Info11, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info12 = ResolveGeneratedId(value.Info12, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info13 = ResolveGeneratedId(value.Info13, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info14 = ResolveGeneratedId(value.Info14, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.Info15 = ResolveGeneratedId(value.Info15, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);

                snapshot.StreamSetEntries.Add(value.StreamSetId, _mapper.Map(value, new BgmStreamSetEntry(value.StreamSetId)));
            }

            foreach (var value in bgmDb.AssignedInfoEntries.Values)
            {
                value.InfoId = ResolveGeneratedId(value.InfoId, toneIds, MusicConstants.InternalIds.INFO_ID_PREFIX);
                value.StreamId = ResolveGeneratedId(value.StreamId, toneIds, MusicConstants.InternalIds.STREAM_PREFIX);

                snapshot.AssignedInfoEntries.Add(value.InfoId, _mapper.Map(value, new BgmAssignedInfoEntry(value.InfoId)));
            }

            foreach (var value in bgmDb.StreamPropertyEntries.Values)
            {
                value.StreamId = ResolveGeneratedId(value.StreamId, toneIds, MusicConstants.InternalIds.STREAM_PREFIX);

                snapshot.StreamPropertyEntries.Add(value.StreamId, _mapper.Map(value, new BgmStreamPropertyEntry(value.StreamId)));
            }

            foreach (var value in bgmProperty.Entries.Values)
            {
                value.NameId = ResolveGeneratedId(value.NameId, toneIds, string.Empty);
                var filename = Path.Combine(rootPath, "stream;", "sound", "bgm", string.Format(MusicConstants.GameResources.NUS3AUDIO_FILE, value.NameId));
                snapshot.BgmPropertyEntries.Add(value.NameId, _mapper.Map(value, new BgmPropertyEntry(value.NameId, filename)));
            }

            foreach (var value in gameTitleDb.DbRootEntries.Values)
            {
                value.UiGameTitleId = ResolveKnownId(value.UiGameTitleId, gameTitleIds);
                value.UiSeriesId = ResolveKnownId(value.UiSeriesId, seriesIds);

                var entry = _mapper.Map(value, new GameTitleEntry(value.UiGameTitleId));
                FillTitleMsbt(entry.MSBTTitle, entry.MSBTTitleKey, titleMsbts);
                snapshot.GameTitleEntries.Add(entry.UiGameTitleId, entry);
            }

            foreach (var value in seriesDb.DbRootEntries.Values)
            {
                value.UiSeriesId = ResolveKnownId(value.UiSeriesId, seriesIds);

                var entry = _mapper.Map(value, new SeriesEntry(value.UiSeriesId));
                FillTitleMsbt(entry.MSBTTitle, entry.MSBTTitleKey, titleMsbts);
                snapshot.SeriesEntries.Add(entry.UiSeriesId, entry);
            }

            return snapshot;
        }

        private static void EnsureSnapshotFileExists(string file)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException($"Required music resource file was not found: {file}", file);
        }

        private Dictionary<string, MsbtDatabase> LoadMsbtDatabases(string rootPath, string resourcePattern)
        {
            var output = new Dictionary<string, MsbtDatabase>();
            foreach (var locale in LocaleHelper.ValidLocales)
            {
                var file = Path.Combine(rootPath, string.Format(resourcePattern, locale));
                if (File.Exists(file))
                    output.Add(locale, _msbtProvider.ReadFile<MsbtDatabase>(file));
            }
            return output;
        }

        private static List<string> GetToneIds(string rootPath, BinBgmProperty bgmProperty)
        {
            var toneIds = new HashSet<string>(bgmProperty.Entries.Keys.Where(p => !string.IsNullOrEmpty(p)));
            var bgmPath = Path.Combine(rootPath, "stream;", "sound", "bgm");
            if (Directory.Exists(bgmPath))
            {
                foreach (var file in Directory.EnumerateFiles(bgmPath, "*.nus3audio"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (name.StartsWith(MusicConstants.InternalIds.NUS3AUDIO_FILE_PREFIX, StringComparison.OrdinalIgnoreCase))
                        toneIds.Add(name.Substring(MusicConstants.InternalIds.NUS3AUDIO_FILE_PREFIX.Length));
                }
            }

            return toneIds.ToList();
        }

        private static void FillBgmMsbt(BgmDbRootEntry entry, Dictionary<string, MsbtDatabase> msbts)
        {
            if (string.IsNullOrEmpty(entry.NameId))
                return;

            foreach (var msbt in msbts)
            {
                AddMsbtValue(entry.Title, msbt.Key, msbt.Value, entry.TitleKey, ConvertFromGameTextTag);
                AddMsbtValue(entry.Author, msbt.Key, msbt.Value, entry.AuthorKey);
                AddMsbtValue(entry.Copyright, msbt.Key, msbt.Value, entry.CopyrightKey);
            }
        }

        private static void FillTitleMsbt(Dictionary<string, string> target, string key, Dictionary<string, MsbtDatabase> msbts)
        {
            if (string.IsNullOrEmpty(key))
                return;

            foreach (var msbt in msbts)
                AddMsbtValue(target, msbt.Key, msbt.Value, key);
        }

        private static void AddMsbtValue(Dictionary<string, string> target, string locale, MsbtDatabase database, string key, Func<string, string> converter = null)
        {
            if (database?.Entries != null && database.Entries.TryGetValue(key, out var value))
                target[locale] = converter == null ? value : converter(value);
        }

        private static string ConvertFromGameTextTag(string input)
        {
            return input.Replace("\u000e\u0000\u0002\u0002P", "{{").Replace("\u000e\u0000\u0002\u0002d", "}}");
        }

        private static string ResolveGeneratedId(string value, IEnumerable<string> toneIds, string prefix)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return value;

            foreach (var toneId in toneIds)
            {
                var candidate = prefix + toneId;
                if (Hash40Equals(value, candidate))
                    return candidate;
            }

            return value;
        }

        private static void AddGeneratedId(IDictionary<string, string> output, string value, string prefix, string nameId)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(nameId))
                return;

            var candidate = prefix + nameId;
            if (value.Equals(candidate, StringComparison.OrdinalIgnoreCase) || Hash40Matches(value, candidate))
                output[value] = candidate;
        }

        private static string ResolveKnownId(string value, IDictionary<string, string> knownIds)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return knownIds.TryGetValue(value, out var resolved) ? resolved : value;
        }

        private static bool Hash40Equals(string hashValue, string candidate)
        {
            return Convert.ToUInt64(hashValue, 16) == Hash40Util.StringToHash40(candidate);
        }

        private static bool Hash40Matches(string value, string candidate)
        {
            return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && Hash40Equals(value, candidate);
        }

        private static string GetFirstInfoId(BgmStreamSetEntry streamSet)
        {
            if (streamSet == null)
                return null;

            return new[]
            {
                streamSet.Info0, streamSet.Info1, streamSet.Info2, streamSet.Info3,
                streamSet.Info4, streamSet.Info5, streamSet.Info6, streamSet.Info7,
                streamSet.Info8, streamSet.Info9, streamSet.Info10, streamSet.Info11,
                streamSet.Info12, streamSet.Info13, streamSet.Info14, streamSet.Info15
            }.FirstOrDefault(p => !string.IsNullOrEmpty(p));
        }

        private static string GetToneId(BgmStreamPropertyEntry streamProperty, BgmDbRootEntry dbRoot)
        {
            if (!string.IsNullOrEmpty(streamProperty?.DataName0))
                return streamProperty.DataName0;

            var streamId = streamProperty?.StreamId;
            if (!string.IsNullOrEmpty(streamId) && streamId.StartsWith(MusicConstants.InternalIds.STREAM_PREFIX))
                return streamId.Substring(MusicConstants.InternalIds.STREAM_PREFIX.Length);

            if (!string.IsNullOrEmpty(dbRoot?.UiBgmId) && dbRoot.UiBgmId.StartsWith(MusicConstants.InternalIds.UI_BGM_ID_PREFIX))
                return dbRoot.UiBgmId.Substring(MusicConstants.InternalIds.UI_BGM_ID_PREFIX.Length);

            return null;
        }

        private static void CopyNus3Audio(string outputPath, string toneId, string destinationFile)
        {
            var sourceFile = Path.Combine(outputPath, "stream;", "sound", "bgm", string.Format(MusicConstants.GameResources.NUS3AUDIO_FILE, toneId));
            if (!File.Exists(sourceFile))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));
            File.Copy(sourceFile, destinationFile, true);
        }

        private static float ReadNus3BankVolume(string outputPath, string toneId)
        {
            var bankFile = Path.Combine(outputPath, "stream;", "sound", "bgm", string.Format(MusicConstants.GameResources.NUS3BANK_FILE, toneId));
            if (!File.Exists(bankFile))
                return 2.7f;

            var bytes = File.ReadAllBytes(bankFile);
            var matches = Locate(bytes, new byte[] { 0xE8, 0x22, 0x00, 0x00 }).ToList();
            if (matches.Count != 3 || matches[1] + 8 > bytes.Length)
                return 2.7f;

            return (float)Math.Round(BitConverter.ToSingle(bytes, matches[1] + 4), 2, MidpointRounding.AwayFromZero);
        }

        private static IEnumerable<int> Locate(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var found = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                    yield return i;
            }
        }

        private class ResourceSnapshot
        {
            public Dictionary<string, BgmDbRootEntry> BgmDbRootEntries { get; } = new Dictionary<string, BgmDbRootEntry>();
            public Dictionary<string, BgmStreamSetEntry> StreamSetEntries { get; } = new Dictionary<string, BgmStreamSetEntry>();
            public Dictionary<string, BgmAssignedInfoEntry> AssignedInfoEntries { get; } = new Dictionary<string, BgmAssignedInfoEntry>();
            public Dictionary<string, BgmStreamPropertyEntry> StreamPropertyEntries { get; } = new Dictionary<string, BgmStreamPropertyEntry>();
            public Dictionary<string, BgmPropertyEntry> BgmPropertyEntries { get; } = new Dictionary<string, BgmPropertyEntry>();
            public Dictionary<string, GameTitleEntry> GameTitleEntries { get; } = new Dictionary<string, GameTitleEntry>();
            public Dictionary<string, SeriesEntry> SeriesEntries { get; } = new Dictionary<string, SeriesEntry>();
        }
    }
}
