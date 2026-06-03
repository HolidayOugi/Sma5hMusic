using System.Collections.Generic;

namespace Sma5hMusic.GUI.Models
{
    public class Nus3AudioBatchNormalizationResult
    {
        public int TotalFiles { get; set; }
        public int NormalizedFiles { get; set; }
        public List<string> FailedFiles { get; set; } = new List<string>();
    }
}