using ReactiveUI.Fody.Helpers;

namespace Sma5hMusic.GUI.ViewModels
{
    public class YoutubeDownloadProgressModalWindowViewModel : ViewModelBase
    {
        [Reactive]
        public int Current { get; set; }

        [Reactive]
        public int Total { get; set; }

        [Reactive]
        public string Message { get; set; } = "Preparing download...";

        [Reactive]
        public bool IsIndeterminate { get; set; } = true;

        public void SetPreparing()
        {
            Current = 0;
            Total = 0;
            IsIndeterminate = true;
            Message = "Preparing download...";
        }

        public void SetProgress(int current, int total)
        {
            Current = current;
            Total = total;
            IsIndeterminate = total <= 0;

            Message = total > 0
                ? $"Downloaded songs: {current}/{total}"
                : $"Downloaded songs: {current}";
        }
    }
}