using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sma5hMusic.GUI.Views;
using Microsoft.Extensions.Logging;

namespace Sma5hMusic.GUI.ViewModels
{
    public partial class MainWindowViewModel
    {
        private async Task GenerateModFromBuildFiles()
        {
            var wasShowingDebug = IsShowingDebug;
            try
            {
                IsShowingDebug = true;
                _logger.LogInformation("Generate Mod from build files requested.");

                var buildFolder = await _fileDialog.OpenFolderDialog(_rootDialog.Window);
                if (string.IsNullOrWhiteSpace(buildFolder))
                {
                    _logger.LogInformation("Generate Mod from build files cancelled: no build folder selected.");
                    return;
                }

                _logger.LogInformation("Generate Mod from build files selected folder: {BuildFolder}", buildFolder);

                if (!IsValidMusicBuildFolder(buildFolder))
                {
                    _logger.LogWarning("Generate Mod from build files rejected invalid folder: {BuildFolder}", buildFolder);
                    await _messageDialog.ShowError(
                        "Generate Mod from build files",
                        $"The selected folder does not look like a Sma5hMusic build output.\r\nIt must contain sound, stream; and ui folders:\r\n{buildFolder}");
                    return;
                }

                var modInfoVm = new GenerateModFromBuildFilesModalWindowViewModel(_appSettings);
                var modInfoWindow = new GenerateModFromBuildFilesModalWindow { DataContext = modInfoVm };
                var modInfoResult = await modInfoWindow.ShowDialog<GenerateModFromBuildFilesModalWindow>(_rootDialog.Window);
                if (modInfoResult == null)
                {
                    _logger.LogInformation("Generate Mod from build files cancelled: no mod information entered.");
                    return;
                }

                var modInformation = modInfoVm.GetMusicModInformation();
                var modOutputPath = Path.Combine(_appSettings.CurrentValue.Sma5hMusic.ModPath, modInfoVm.ModPath);
                _logger.LogInformation("Generate Mod from build files target mod: {ModName} at {ModOutputPath}", modInformation.Name, modOutputPath);

                IsLoading = true;
                IsShowingDebug = true;
                _logger.LogInformation("Generating music mod {ModName} from build folder {BuildFolder}", modInformation.Name, buildFolder);

                var metadata = await Task.Run(() => _musicModReverseService.Reverse(
                    _appSettings.CurrentValue.GameResourcesPath,
                    buildFolder,
                    modOutputPath,
                    modInformation.Name,
                    modInformation));

                await OnInitData();
                var songCount = CountSongs(metadata);
                _logger.LogInformation("Generate Mod from build files completed: {ModName} with {SongCount} song(s).", modInformation.Name, songCount);
                await _messageDialog.ShowInformation(
                    "Generate Mod from build files",
                    $"Created mod '{modInformation.Name}' with {songCount} song(s).\r\n\r\n{modOutputPath}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Generate Mod from build files failed.");
                await _messageDialog.ShowError(
                    "Generate Mod from build files",
                    "There was an error while generating the mod from the selected build files. Please check the logs.",
                    e);
            }
            finally
            {
                IsLoading = false;
                IsShowingDebug = wasShowingDebug;
            }
        }

        private static bool IsValidMusicBuildFolder(string buildFolder)
        {
            return Directory.Exists(Path.Combine(buildFolder, "sound")) &&
                   Directory.Exists(Path.Combine(buildFolder, "stream;")) &&
                   Directory.Exists(Path.Combine(buildFolder, "ui")) &&
                   File.Exists(Path.Combine(buildFolder, "sound", "config", "bgm_property.bin")) &&
                   File.Exists(Path.Combine(buildFolder, "ui", "param", "database", "ui_bgm_db.prc"));
        }

        private static int CountSongs(Sma5h.Mods.Music.MusicMods.MusicModModels.MusicModConfig metadata)
        {
            return metadata.Series?
                .SelectMany(p => p.Games ?? Enumerable.Empty<Sma5h.Mods.Music.MusicMods.MusicModModels.GameConfig>())
                .SelectMany(p => p.Bgms ?? Enumerable.Empty<Sma5h.Mods.Music.MusicMods.MusicModModels.BgmConfig>())
                .Count() ?? 0;
        }
    }
}
