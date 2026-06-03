using ReactiveUI.Fody.Helpers;

namespace Sma5hMusic.GUI.ViewModels
{
    public class ScriptProgressModalWindowViewModel : ViewModelBase
    {
        [Reactive]
        public int Current { get; set; }

        [Reactive]
        public int Total { get; set; }

        [Reactive]
        public string Message { get; set; } = "Preparing...";

        [Reactive]
        public string Footer { get; set; } = "Please wait. Closing this window will cancel the operation.";

        [Reactive]
        public bool IsIndeterminate { get; set; } = true;

        public void SetPreparing(string message)
        {
            Current = 0;
            Total = 0;
            IsIndeterminate = true;
            Message = message;
        }

        public void SetProgress(string action, string itemName, int current, int total)
        {
            Current = current;
            Total = total;
            IsIndeterminate = total <= 0;

            Message = total > 0
                ? $"{action}: {current}/{total}\r\n{itemName}"
                : $"{action}: {current}\r\n{itemName}";
        }
    }
}