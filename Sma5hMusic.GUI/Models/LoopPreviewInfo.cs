namespace Sma5hMusic.GUI.Models
{
    public class LoopPreviewInfo
    {
        public string Filename { get; set; }
        public uint PreviewLengthSamples { get; set; }
        public uint FirstSegmentSourceStartSample { get; set; }
        public uint SecondSegmentSourceStartSample { get; set; }
        public uint SecondSegmentPreviewStartSample { get; set; }
    }
}
