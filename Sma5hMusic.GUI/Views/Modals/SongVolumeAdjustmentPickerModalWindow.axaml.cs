using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Sma5hMusic.GUI.Views
{
    public class SongVolumeAdjustmentPickerModalWindow : Window
    {
        public SongVolumeAdjustmentPickerModalWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
