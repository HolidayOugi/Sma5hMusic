using Sma5hMusic.GUI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Interfaces
{
    public interface IAudioImportService
    {
        bool RequiresConversion(string filename);
        Task<AudioImportInfo> GetAudioInfo(string filename);
        Task<IReadOnlyList<AutoLoopPoint>> CalculateAutoLoopPoints(string filename, uint sampleRate, uint totalSamples);
        Task<LoopPreviewInfo> CreateLoopPreview(string filename, uint loopStartSample, uint loopEndSample, uint totalSamples);
        void CleanupLoopPreviews();
        Task<string> ConvertToNus3Audio(string toneId, string filename, string modPath, uint loopStartSample, uint loopEndSample);
    }
}
