using Avalonia.Controls;
using ReactiveUI;
using Sma5h.Mods.Music.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;

namespace Sma5hMusic.GUI.ViewModels
{
    public class CskPackSeriesPickerModalWindowViewModel : ViewModelBase
    {
        private bool _hasSelection;

        public ObservableCollection<CskPackSeriesOptionViewModel> Series { get; }
        public ReactiveCommand<Window, Unit> ActionCancel { get; }
        public ReactiveCommand<Window, Unit> ActionOK { get; }
        public ReactiveCommand<Unit, Unit> ActionEnableAll { get; }
        public ReactiveCommand<Unit, Unit> ActionDisableAll { get; }

        public bool HasSelection
        {
            get => _hasSelection;
            private set => this.RaiseAndSetIfChanged(ref _hasSelection, value);
        }

        public CskPackSeriesPickerModalWindowViewModel(IEnumerable<CskPackSeriesOption> series)
        {
            Series = new ObservableCollection<CskPackSeriesOptionViewModel>(
                series
                    .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.ModName, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new CskPackSeriesOptionViewModel(p)));

            foreach (var item in Series)
                item.WhenAnyValue(p => p.IsSelected).Subscribe(_ => RefreshSelectionState());

            ActionCancel = ReactiveCommand.Create<Window>(Cancel);
            ActionOK = ReactiveCommand.Create<Window>(Save, this.WhenAnyValue(p => p.HasSelection));
            ActionEnableAll = ReactiveCommand.Create(SetAllEnabled);
            ActionDisableAll = ReactiveCommand.Create(SetAllDisabled);
            RefreshSelectionState();
        }

        public IEnumerable<string> GetSelectedSeriesKeys()
        {
            return Series.Where(p => p.IsSelected).Select(p => p.Key);
        }

        private void SetAllEnabled()
        {
            foreach (var item in Series)
                item.IsSelected = true;
        }

        private void SetAllDisabled()
        {
            foreach (var item in Series)
                item.IsSelected = false;
        }

        private void RefreshSelectionState()
        {
            HasSelection = Series.Any(p => p.IsSelected);
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

    public class CskPackSeriesOptionViewModel : ReactiveObject
    {
        private bool _isSelected;

        public string Key { get; }
        public string DisplayName { get; }
        public string NameId { get; }
        public string UiSeriesId { get; }
        public string ModName { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        public CskPackSeriesOptionViewModel(CskPackSeriesOption option)
        {
            Key = option.Key;
            DisplayName = option.DisplayName;
            NameId = option.NameId;
            UiSeriesId = option.UiSeriesId;
            ModName = option.ModName;
        }
    }
}
