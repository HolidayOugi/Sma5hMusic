using Avalonia.Controls;
using ReactiveUI;
using System.Globalization;
using System.Reactive;

namespace Sma5hMusic.GUI.ViewModels
{
    public class SongVolumeAdjustmentPickerModalWindowViewModel : ViewModelBase
    {
        private double _volumeAdjustment;
        private readonly double _minimumVolume;
        private readonly double _maximumVolume;
        private readonly int _decimalPlaces;

        public double VolumeAdjustment
        {
            get => _volumeAdjustment;
            set
            {
                var roundedValue = System.Math.Clamp(System.Math.Round(value, _decimalPlaces), _minimumVolume, _maximumVolume);
                this.RaiseAndSetIfChanged(ref _volumeAdjustment, roundedValue);
                this.RaisePropertyChanged(nameof(VolumeAdjustmentText));
            }
        }

        public string VolumeAdjustmentText
        {
            get => VolumeAdjustment.ToString($"0.{new string('0', _decimalPlaces)}", CultureInfo.CurrentCulture);
            set
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsedValue))
                    VolumeAdjustment = parsedValue;
            }
        }

        public string WindowTitle { get; }
        public string HeaderTitle { get; }
        public string Label { get; }
        public string Tooltip { get; }
        public double MinimumVolume => _minimumVolume;
        public double MaximumVolume => _maximumVolume;

        public ReactiveCommand<Window, Unit> ActionCancel { get; }
        public ReactiveCommand<Window, Unit> ActionOK { get; }

        public SongVolumeAdjustmentPickerModalWindowViewModel()
            : this(
                "Increase / Decrease Volume for all Songs",
                "Increase / Decrease Amount",
                "Amount to add to every mod song volume.",
                -10.0,
                10.0,
                1)
        {
        }

        public SongVolumeAdjustmentPickerModalWindowViewModel(
            string title,
            string label,
            string tooltip,
            double minimumVolume,
            double maximumVolume,
            int decimalPlaces)
        {
            WindowTitle = title;
            HeaderTitle = title;
            Label = label;
            Tooltip = tooltip;
            _minimumVolume = minimumVolume;
            _maximumVolume = maximumVolume;
            _decimalPlaces = decimalPlaces;

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
