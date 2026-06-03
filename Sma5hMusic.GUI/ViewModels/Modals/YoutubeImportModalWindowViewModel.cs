using Avalonia.Controls;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;
using System;
using System.Reactive;

namespace Sma5hMusic.GUI.ViewModels
{
    public class YoutubeImportModalWindowViewModel : ReactiveValidationObject
    {
        public ReactiveCommand<Window, Unit> ActionCancel { get; }
        public ReactiveCommand<Window, Unit> ActionImport { get; }

        [Reactive]
        public string Url { get; set; }

        public YoutubeImportModalWindowViewModel()
        {
            this.ValidationRule(p => p.Url, IsYoutubeUrl, "Enter a valid YouTube URL.");

            ActionCancel = ReactiveCommand.Create<Window>(p => p.Close());
            ActionImport = ReactiveCommand.Create<Window>(p => p.Close(p), this.WhenAnyValue(p => p.ValidationContext.IsValid));
        }

        public void Reset()
        {
            Url = string.Empty;
        }

        private static bool IsYoutubeUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            return string.Equals(uri.Host, "youtu.be", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "youtube.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
