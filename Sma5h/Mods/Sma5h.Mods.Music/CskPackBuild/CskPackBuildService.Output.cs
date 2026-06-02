using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public partial class CskPackBuildService
    {
        #region Output

        private string PrepareOutputRoot()
        {
            var configuredOutputPath = _config.CurrentValue.OutputPath;
            if (string.IsNullOrWhiteSpace(configuredOutputPath))
                throw new InvalidOperationException("Output path is not configured.");

            var outputRoot = Path.GetFullPath(configuredOutputPath);
            if (string.Equals(outputRoot, Path.GetPathRoot(outputRoot), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to clear the drive root: {outputRoot}");

            _logger.LogInformation("[CSK] Clearing output folder {OutputPath}", outputRoot);
            ClearDirectory(outputRoot);
            return outputRoot;
        }

        private static void ClearDirectory(string path)
        {
            Directory.CreateDirectory(path);

            foreach (var file in Directory.GetFiles(path))
                File.Delete(file);

            foreach (var directory in Directory.GetDirectories(path))
                Directory.Delete(directory, true);
        }

        #endregion

    }
}
