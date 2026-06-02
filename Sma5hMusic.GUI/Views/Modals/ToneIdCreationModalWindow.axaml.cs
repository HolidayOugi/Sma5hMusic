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
    public class ToneIdCreationModalWindow : ReactiveWindow<ToneIdCreationModalWindowModel>
    {
        private PropertyTextField ToneIdValidation => this.FindControl<PropertyTextField>("ToneId");
        private PropertyUIntField LoopStartMsValidation => this.FindControl<PropertyUIntField>("LoopStartMs");
        private PropertyUIntField LoopStartSampleValidation => this.FindControl<PropertyUIntField>("LoopStartSample");
        private PropertyUIntField LoopEndMsValidation => this.FindControl<PropertyUIntField>("LoopEndMs");
        private PropertyUIntField LoopEndSampleValidation => this.FindControl<PropertyUIntField>("LoopEndSample");

        public ToneIdCreationModalWindow()
        {
            this.InitializeComponent();
            Closing += async (sender, e) =>
            {
                if (ViewModel != null)
                    await ViewModel.ClosePreview();
            };
            Closed += (sender, e) =>
            {
                ViewModel?.CleanupLoopPreviewFiles();
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            this.WhenActivated(disposables =>
            {
                this.BindValidation(ViewModel, vm => vm.ToneId, view => view.ToneIdValidation.ValidationError)
                .DisposeWith(disposables);
                this.BindValidation(ViewModel, vm => vm.LoopStartMs, view => view.LoopStartMsValidation.ValidationError)
                .DisposeWith(disposables);
                this.BindValidation(ViewModel, vm => vm.LoopStartSample, view => view.LoopStartSampleValidation.ValidationError)
                .DisposeWith(disposables);
                this.BindValidation(ViewModel, vm => vm.LoopEndMs, view => view.LoopEndMsValidation.ValidationError)
                .DisposeWith(disposables);
                this.BindValidation(ViewModel, vm => vm.LoopEndSample, view => view.LoopEndSampleValidation.ValidationError)
                .DisposeWith(disposables);
            });
        }
    }
}
