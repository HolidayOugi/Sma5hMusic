using System;
using System.IO;
using System.Linq;

namespace Sma5hMusic.GUI.Helpers
{
    public static class NativeLibraryPathHelper
    {
        public static void AddDirectoryToProcessPath(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return;

            var fullDirectory = Path.GetFullPath(directory);

            if (!Directory.Exists(fullDirectory))
                return;

            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            var alreadyExists = currentPath
                .Split(Path.PathSeparator)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Any(p =>
                {
                    try
                    {
                        return string.Equals(
                            Path.GetFullPath(p.Trim()),
                            fullDirectory,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (alreadyExists)
                return;

            Environment.SetEnvironmentVariable(
                "PATH",
                fullDirectory + Path.PathSeparator + currentPath,
                EnvironmentVariableTarget.Process);
        }
    }
}