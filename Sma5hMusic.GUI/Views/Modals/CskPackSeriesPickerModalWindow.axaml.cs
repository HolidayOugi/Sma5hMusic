using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Sma5hMusic.GUI.Views
{
    public class CskPackSeriesPickerModalWindow : Window
    {
        public CskPackSeriesPickerModalWindow()
        {
            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
