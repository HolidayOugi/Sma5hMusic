using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;
using Sma5hMusic.GUI.ViewModels;
using Sma5hMusic.GUI.Views.Fields;
using System.Reactive.Disposables;

namespace Sma5hMusic.GUI.Views
{
    public class YoutubeImportModalWindow : ReactiveWindow<YoutubeImportModalWindowViewModel>
    {
        private PropertyTextField UrlValidation => this.FindControl<PropertyTextField>("Url");

        public YoutubeImportModalWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            this.WhenActivated(disposables =>
            {
                this.BindValidation(ViewModel, vm => vm.Url, view => view.UrlValidation.ValidationError)
                    .DisposeWith(disposables);
            });
        }
    }
}
