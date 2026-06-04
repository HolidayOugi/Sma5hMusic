using System.Threading.Tasks;

namespace VGMMusic
{
    public interface IVGMMusicPlayer
    {
        int TotalTime { get; }
        int CurrentTime { get; }
        int CurrentSample { get; }
        bool Loaded { get; }
        bool Play();
        bool ApplyVolume { get; set; }
        float Volume { get; set; }
        Task<bool> Play(string filename);
        Task<bool> Play(string filename, int startSample);
        Task<VGMAudioCuePoints> GetAudioCuePoints(string filename);
        Task<bool> Stop();
    }
}
