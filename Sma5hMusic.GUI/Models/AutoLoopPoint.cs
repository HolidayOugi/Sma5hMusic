namespace Sma5hMusic.GUI.Models
{
    public class AutoLoopPoint
    {
        public int Rank { get; set; }
        public uint LoopStartSample { get; set; }
        public uint LoopEndSample { get; set; }
        public double NoteDifference { get; set; }
        public double LoudnessDifference { get; set; }
        public double Score { get; set; }
        public string ScoreText { get; set; }
        public string RankText { get; set; }
        public string LoopStartTimeText { get; set; }
        public string LoopEndTimeText { get; set; }
        public uint LoopStartMinutes { get; set; }
        public uint LoopStartSeconds { get; set; }
        public uint LoopStartMilliseconds { get; set; }
        public uint LoopEndMinutes { get; set; }
        public uint LoopEndSeconds { get; set; }
        public uint LoopEndMilliseconds { get; set; }
    }
}
