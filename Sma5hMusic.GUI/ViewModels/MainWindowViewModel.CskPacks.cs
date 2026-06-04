using Microsoft.Extensions.Logging;
using ReactiveUI;
using Sma5h.Mods.Music.Interfaces;
using Sma5hMusic.GUI.Views;
using System;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.ViewModels
{
    public partial class MainWindowViewModel
    {
        private readonly ICskPackBuildService _cskPackBuildService;

        public ReactiveCommand<Unit, Unit> ActionBuildCskPacks { get; }
        public ReactiveCommand<Unit, Unit> ActionBuildSingleCskPack { get; }

        public async Task OnBuildCskPacks()
        {
            await BuildCskPacks(false);
        }

        public async Task OnBuildSingleCskPack()
        {
            var buildStarted = false;
            try
            {
                var availableSeries = await _cskPackBuildService.GetAvailableSeries();
                if (availableSeries.Count == 0)
                {
                    await _messageDialog.ShowError("CSK pack build failed", "No series were found in the currently loaded music mods.");
                    return;
                }

                IsLoading = true;
                IsShowingDebug = true;
                buildStarted = true;
                await _musicPlayer.Stop();
                _logger.LogInformation("Building single CSK pack for all {SeriesCount} available series.", availableSeries.Count);

                await _cskPackBuildService.BuildSingle(availableSeries.Select(p => p.Key));
                await _messageDialog.ShowInformation("Complete", "Single CSK pack build complete.");
            }
            catch (Exception e)
            {
                await _messageDialog.ShowError("CSK pack build failed", e.Message, e);
            }
            finally
            {
                if (buildStarted)
                {
                    IsLoading = false;
                    IsShowingDebug = false;
                }
            }
        }

        private async Task BuildCskPacks(bool singlePack)
        {
            var buildStarted = false;
            try
            {
                var availableSeries = await _cskPackBuildService.GetAvailableSeries();
                if (availableSeries.Count == 0)
                {
                    await _messageDialog.ShowError("CSK pack build failed", "No series were found in the currently loaded music mods.");
                    return;
                }

                var pickerViewModel = new CskPackSeriesPickerModalWindowViewModel(availableSeries);
                var pickerWindow = new CskPackSeriesPickerModalWindow { DataContext = pickerViewModel };
                var pickerResult = await pickerWindow.ShowDialog<CskPackSeriesPickerModalWindow>(_rootDialog.Window);
                if (pickerResult == null)
                    return;

                var selectedSeriesKeys = pickerViewModel.GetSelectedSeriesKeys().ToList();
                if (selectedSeriesKeys.Count == 0)
                    return;

                IsLoading = true;
                IsShowingDebug = true;
                buildStarted = true;
                await _musicPlayer.Stop();
                _logger.LogInformation("Building {CskBuildMode} CSK pack(s) for {SelectedSeriesCount} selected series.", singlePack ? "single" : "modular", selectedSeriesKeys.Count);

                if (singlePack)
                    await _cskPackBuildService.BuildSingle(selectedSeriesKeys);
                else
                    await _cskPackBuildService.Build(selectedSeriesKeys);

                await _messageDialog.ShowInformation("Complete", singlePack ? "Single CSK pack build complete." : "Modular CSK packs build complete.");
            }
            catch (Exception e)
            {
                await _messageDialog.ShowError("CSK pack build failed", e.Message, e);
            }
            finally
            {
                if (buildStarted)
                {
                    IsLoading = false;
                    IsShowingDebug = false;
                }
            }
        }
    }
}
