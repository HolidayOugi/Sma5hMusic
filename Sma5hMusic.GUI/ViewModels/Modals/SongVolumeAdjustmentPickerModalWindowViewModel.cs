using Avalonia.Controls;
using ReactiveUI;
using System.Globalization;
using System.Reactive;

namespace Sma5hMusic.GUI.ViewModels
{
    public class SongVolumeAdjustmentPickerModalWindowViewModel : ViewModelBase
    {
        private double _volumeAdjustment;

        public double VolumeAdjustment
        {
            get => _volumeAdjustment;
            set
            {
                var roundedValue = System.Math.Clamp(System.Math.Round(value, 1), -10.0, 10.0);
                this.RaiseAndSetIfChanged(ref _volumeAdjustment, roundedValue);
                this.RaisePropertyChanged(nameof(VolumeAdjustmentText));
            }
        }

        public string VolumeAdjustmentText
        {
            get => VolumeAdjustment.ToString("0.0", CultureInfo.CurrentCulture);
            set
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsedValue))
                    VolumeAdjustment = parsedValue;
            }
        }

        public ReactiveCommand<Window, Unit> ActionCancel { get; }
        public ReactiveCommand<Window, Unit> ActionOK { get; }

        public SongVolumeAdjustmentPickerModalWindowViewModel()
        {
            ActionCancel = ReactiveCommand.Create<Window>(CancelChanges);
            ActionOK = ReactiveCommand.Create<Window>(SaveChanges);
        }

        private void CancelChanges(Window w)
        {
            w.Close();
        }

        private void SaveChanges(Window w)
        {
            w.Close(w);
        }
    }
}
