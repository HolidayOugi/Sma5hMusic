using Avalonia.Controls;
using ReactiveUI;
using Sma5hMusic.GUI.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;

namespace Sma5hMusic.GUI.ViewModels
{
    public class GameMultiPickerModalWindowViewModel : ViewModelBase
    {
        private bool _hasSelection;
        private GameMultiPickerSeriesOptionViewModel _selectedSeries;

        public ObservableCollection<GameMultiPickerOptionViewModel> Games { get; }
        public ObservableCollection<GameMultiPickerSeriesOptionViewModel> Series { get; }
        public ReactiveCommand<Window, Unit> ActionCancel { get; }
        public ReactiveCommand<Window, Unit> ActionOK { get; }
        public ReactiveCommand<Unit, Unit> ActionEnableAll { get; }
        public ReactiveCommand<Unit, Unit> ActionDisableAll { get; }

        public bool HasSelection
        {
            get => _hasSelection;
            private set => this.RaiseAndSetIfChanged(ref _hasSelection, value);
        }

        public GameMultiPickerSeriesOptionViewModel SelectedSeries
        {
            get => _selectedSeries;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedSeries, value);
                this.RaisePropertyChanged(nameof(FilteredGames));
            }
        }

        public IEnumerable<GameMultiPickerOptionViewModel> FilteredGames
        {
            get
            {
                if (SelectedSeries == null)
                    return Enumerable.Empty<GameMultiPickerOptionViewModel>();

                return Games.Where(p => string.Equals(p.UiSeriesId, SelectedSeries.UiSeriesId, StringComparison.OrdinalIgnoreCase));
            }
        }

        public GameMultiPickerModalWindowViewModel(IEnumerable<GameTitleSortOption> games)
        {
            Games = new ObservableCollection<GameMultiPickerOptionViewModel>(
                games
                    .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.UiGameTitleId, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new GameMultiPickerOptionViewModel(p)));

            Series = new ObservableCollection<GameMultiPickerSeriesOptionViewModel>(
                Games
                    .GroupBy(p => p.UiSeriesId)
                    .Select(p => new GameMultiPickerSeriesOptionViewModel(
                        p.Key,
                        p.FirstOrDefault()?.SeriesTitle ?? p.Key,
                        p.Count()))
                    .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.UiSeriesId, StringComparer.OrdinalIgnoreCase));

            foreach (var item in Games)
                item.WhenAnyValue(p => p.IsSelected).Subscribe(_ => RefreshSelectionState());

            ActionCancel = ReactiveCommand.Create<Window>(Cancel);
            ActionOK = ReactiveCommand.Create<Window>(Save, this.WhenAnyValue(p => p.HasSelection));
            ActionEnableAll = ReactiveCommand.Create(SetAllEnabled);
            ActionDisableAll = ReactiveCommand.Create(SetAllDisabled);
            SelectedSeries = Series.FirstOrDefault();
            RefreshSelectionState();
        }

        public IEnumerable<string> GetSelectedGameTitleIds()
        {
            return Games.Where(p => p.IsSelected).Select(p => p.UiGameTitleId);
        }

        private void SetAllEnabled()
        {
            foreach (var item in FilteredGames)
                item.IsSelected = true;
        }

        private void SetAllDisabled()
        {
            foreach (var item in FilteredGames)
                item.IsSelected = false;
        }

        private void RefreshSelectionState()
        {
            HasSelection = Games.Any(p => p.IsSelected);
        }

        private void Cancel(Window window)
        {
            window.Close();
        }

        private void Save(Window window)
        {
            window.Close(window);
        }
    }

    public class GameMultiPickerOptionViewModel : ReactiveObject
    {
        private bool _isSelected;

        public string UiGameTitleId { get; }
        public string UiSeriesId { get; }
        public string SeriesTitle { get; }
        public string Title { get; }
        public int SongCount { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        public GameMultiPickerOptionViewModel(GameTitleSortOption option)
        {
            UiGameTitleId = option.UiGameTitleId;
            UiSeriesId = option.UiSeriesId;
            SeriesTitle = option.SeriesTitle;
            Title = option.Title;
            SongCount = option.SongCount;
        }
    }

    public class GameMultiPickerSeriesOptionViewModel
    {
        public string UiSeriesId { get; }
        public string Title { get; }
        public int GameCount { get; }

        public GameMultiPickerSeriesOptionViewModel(string uiSeriesId, string title, int gameCount)
        {
            UiSeriesId = uiSeriesId;
            Title = !string.IsNullOrWhiteSpace(title) ? title : uiSeriesId;
            GameCount = gameCount;
        }
    }
}
