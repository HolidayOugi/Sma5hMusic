using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Sma5hMusic.GUI.Helpers;
using Sma5hMusic.GUI.Interfaces;
using Sma5hMusic.GUI.ViewModels;
using Splat;
using System.IO;
using static Sma5hMusic.GUI.Helpers.StylesHelper;

namespace Sma5hMusic.GUI
{
    public class App : Application
    {
        public override void Initialize()
        {
            var uiScale = Program.Configuration.GetValue<UIScale>("Sma5hMusicGUI:UIScale");
            var uiTheme = Program.Configuration.GetValue<UITheme>("Sma5hMusicGUI:UITheme");

            Styles.Insert(0, StylesHelper.GetUITheme(uiTheme));
            AvaloniaXamlLoader.Load(this);
            Styles.Add(StylesHelper.GetUIScale(uiScale));
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = Locator.Current.GetService<IDialogWindow>() as Window;
                mainWindow.DataContext = Locator.Current.GetService<MainWindowViewModel>();
                desktop.MainWindow = mainWindow;

                desktop.Exit += (sender, args) =>
                {
                    CleanupTempDirectory();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static void CleanupTempDirectory()
        {
            var tempPath = Program.Configuration["TempPath"];

            if (string.IsNullOrWhiteSpace(tempPath))
                tempPath = "Temp";

            var fullTempPath = Path.IsPathRooted(tempPath)
                ? tempPath
                : Path.Combine(Directory.GetCurrentDirectory(), tempPath);

            TempDirectoryHelper.DeleteContents(Path.Combine(fullTempPath, "AudioImport"));
            TempDirectoryHelper.DeleteContents(Path.Combine(fullTempPath, "AudioMetadata"));
            TempDirectoryHelper.DeleteContents(Path.Combine(fullTempPath, "YoutubeImport"));
            TempDirectoryHelper.DeleteContents(Path.Combine(fullTempPath, "Nus3AudioBatchNormalization"));

            TempDirectoryHelper.DeleteIfEmpty(fullTempPath);
        }
    }
}
