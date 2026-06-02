using Sma5hMusic.GUI.Models;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Interfaces
{
    public interface IAudioImportService
    {
        bool RequiresConversion(string filename);
        Task<AudioImportInfo> GetAudioInfo(string filename);
        Task<string> ConvertToNus3Audio(string toneId, string filename, string modPath, uint loopStartSample, uint loopEndSample);
    }
}
