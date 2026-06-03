using Sma5hMusic.GUI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Interfaces
{
    public interface IAudioImportService
    {
        bool RequiresConversion(string filename);
        bool IsFfmpegConfigured();
        Task<AudioImportInfo> GetAudioInfo(string filename);
        Task<IReadOnlyList<AutoLoopPoint>> CalculateAutoLoopPoints(string filename, uint sampleRate, uint totalSamples);
        Task<LoopPreviewInfo> CreateLoopPreview(string filename, uint loopStartSample, uint loopEndSample, uint totalSamples);
        void CleanupLoopPreviews();
        Task<string> ConvertToNus3Audio(string toneId, string filename, string modPath, uint loopStartSample, uint loopEndSample, bool applyNormalization = false);
        bool IsNus3Audio(string filename);
        Task<string> NormalizeNus3Audio(string toneId, string filename, string modPath);
    }
}
