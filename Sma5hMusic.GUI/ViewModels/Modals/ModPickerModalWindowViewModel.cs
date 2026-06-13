using DynamicData;
using ReactiveUI;
using Sma5hMusic.GUI.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;

namespace Sma5hMusic.GUI.ViewModels
{
    public class ModPickerModalWindowViewModel : ModalBaseViewModel<ModEntryViewModel>, IDisposable
    {
        private readonly ReadOnlyObservableCollection<ModEntryViewModel> _mods;
        private readonly IDisposable _modsSubscription;

        public ReadOnlyObservableCollection<ModEntryViewModel> Mods { get { return _mods; } }
        public string WindowTitle { get; }
        public string Header { get; }
        public string FieldLabel { get; }
        public string FieldToolTip { get; }

        public ModPickerModalWindowViewModel(
            IViewModelManager viewModelManager,
            string windowTitle = "Mod Picker",
            string header = "Pick Mod to Edit",
            string fieldLabel = "Mod",
            string fieldToolTip = "Pick an existing mod to modify.")
        {
            WindowTitle = windowTitle;
            Header = header;
            FieldLabel = fieldLabel;
            FieldToolTip = fieldToolTip;

            //Bind observables
            _modsSubscription = viewModelManager.ObservableModsEntries.Connect()
               .ObserveOn(RxApp.MainThreadScheduler)
               .Bind(out _mods)
               .DisposeMany()
               .Subscribe();
        }

        public void Dispose()
        {
            _modsSubscription.Dispose();
        }

        protected override IObservable<bool> GetValidationRule()
        {
            return this.WhenAnyValue(x => x.SelectedItem, x => x != null && !string.IsNullOrEmpty(x.Name));
        }
    }
}
