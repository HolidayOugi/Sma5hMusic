using System;
using System.Collections.Generic;

namespace Sma5hMusic.GUI.Models
{
    public class YoutubeDownloadResult
    {
        public string Filename { get; set; }
        public IReadOnlyCollection<string> Filenames { get; set; } = Array.Empty<string>();
        public string TempDirectory { get; set; }
        public int FailedItemsCount { get; set; }
    }
}
