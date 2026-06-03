using Sma5hMusic.GUI.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Interfaces
{
    public interface INus3AudioBatchNormalizationService
    {
        IReadOnlyList<string> GetNus3AudioFiles(string musicModsPath);

        Task<Nus3AudioBatchNormalizationResult> NormalizeFiles(
            IReadOnlyList<string> files,
            string musicModsPath,
            Action<int, int, string> onProgress = null,
            CancellationToken cancellationToken = default);
    }
}