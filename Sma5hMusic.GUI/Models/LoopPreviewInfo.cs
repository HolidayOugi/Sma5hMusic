namespace Sma5hMusic.GUI.Models
{
    public class LoopPreviewInfo
    {
        public string Filename { get; set; }
        public int StartSample { get; set; }
        public uint PreviewLoopStartSample { get; set; }
        public uint PreviewLoopEndSample { get; set; }
        public uint FirstSegmentSourceStartSample { get; set; }
        public uint FirstSegmentPreviewStartSample { get; set; }
        public uint FirstSegmentPreviewLengthSamples { get; set; }
        public uint SecondSegmentSourceStartSample { get; set; }
        public uint SecondSegmentPreviewStartSample { get; set; }
        public uint SecondSegmentPreviewDurationSamples { get; set; }
        public bool HasSecondSegment { get; set; }
    }
}
