using ReactiveUI.Fody.Helpers;

namespace Sma5hMusic.GUI.ViewModels
{
    public class AudioConversionProgressModalWindowViewModel : ViewModelBase
    {
        [Reactive]
        public int Current { get; set; }

        [Reactive]
        public int Total { get; set; } = 1;

        [Reactive]
        public string Message { get; set; } = "Converting Audio File...";

        [Reactive]
        public string Filename { get; set; }

        [Reactive]
        public bool IsIndeterminate { get; set; } = true;

        public void SetConverting(string filename)
        {
            Current = 0;
            Total = 1;
            Message = "Converting Audio File...";
            Filename = filename;
            IsIndeterminate = true;
        }

        public void SetNormalizing(string filename)
        {
            Current = 0;
            Total = 1;
            Message = "Normalizing Audio File...";
            Filename = filename;
            IsIndeterminate = true;
        }

        public void SetComplete()
        {
            Current = 1;
            IsIndeterminate = false;
        }
    }
}
