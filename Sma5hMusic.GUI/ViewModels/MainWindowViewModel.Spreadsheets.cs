using Microsoft.Extensions.Logging;
using Sma5h.Mods.Music.Interfaces;
using Sma5hMusic.GUI.Dialogs;
using Sma5hMusic.GUI.Views;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.ViewModels
{
    public partial class MainWindowViewModel
    {
        private async Task CreateSongSpreadsheet(string spreadsheetType)
        {
            string defaultFileName;
            string spreadsheetName;
            Func<IMusicMod, string, bool> hasEntries;
            Func<string, IMusicMod, string, Task<bool>> createSpreadsheet;

            switch (spreadsheetType)
            {
                case "SongList":
                    defaultFileName = "Song List.xlsx";
                    spreadsheetName = "Song List";
                    hasEntries = _songSpreadsheetService.HasSongListEntries;
                    createSpreadsheet = _songSpreadsheetService.CreateSongList;
                    break;
                case "PinchSongs":
                    defaultFileName = "Pinch Songs.xlsx";
                    spreadsheetName = "Pinch Songs";
                    hasEntries = _songSpreadsheetService.HasPinchSongEntries;
                    createSpreadsheet = _songSpreadsheetService.CreatePinchSongs;
                    break;
                case "MainMenu":
                    defaultFileName = "Main Menu Songs.xlsx";
                    spreadsheetName = "Main Menu Songs";
                    hasEntries = _songSpreadsheetService.HasMainMenuSongEntries;
                    createSpreadsheet = _songSpreadsheetService.CreateMainMenuSongs;
                    break;
                default:
                    await _messageDialog.ShowError("Create Song Spreadsheet", "Unknown spreadsheet type.");
                    return;
            }

            var currentLocale = _viewModelManager.CurrentLocale;

            var mods = _viewModelManager.GetModsViewModels()
                .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (mods.Count == 0)
            {
                await _messageDialog.ShowWarning("Create Song Spreadsheet", "No music mods are loaded. No spreadsheet was created.");
                return;
            }

            ModEntryViewModel selectedMod;
            if (mods.Count == 1)
            {
                selectedMod = mods[0];
            }
            else
            {
                using var pickerViewModel = new ModPickerModalWindowViewModel(
                    _viewModelManager,
                    "Create Song Spreadsheet",
                    "Pick a Mod",
                    "Mod",
                    "Pick the mod whose songs should be exported.");
                var picker = new ModalDialog<ModPickerModalWindow, ModPickerModalWindowViewModel, ModEntryViewModel>(pickerViewModel);
                selectedMod = await picker.ShowPickerDialog(_rootDialog.Window);
                if (selectedMod == null)
                    return;
            }

            try
            {
                if (!hasEntries(selectedMod.MusicMod, currentLocale))
                {
                    await _messageDialog.ShowWarning(
                        "Create Song Spreadsheet",
                        $"'{selectedMod.Name}' has no entries for {spreadsheetName}. No spreadsheet was created.");
                    return;
                }

                var outputPath = await _fileDialog.SaveFileSpreadsheetDialog(defaultFileName);
                if (string.IsNullOrEmpty(outputPath))
                    return;

                if (!await createSpreadsheet(outputPath, selectedMod.MusicMod, currentLocale))
                {
                    await _messageDialog.ShowWarning(
                        "Create Song Spreadsheet",
                        $"'{selectedMod.Name}' has no entries for {spreadsheetName}. No spreadsheet was created.");
                    return;
                }

                await _messageDialog.ShowInformation("Create Song Spreadsheet", $"Saved {defaultFileName}.", 420);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to create song spreadsheet {SpreadsheetType} for mod {ModId}.", spreadsheetType, selectedMod.Id);
                await _messageDialog.ShowError("Create Song Spreadsheet", "The spreadsheet could not be created. Please check the logs.", e);
            }
        }
    }
}
