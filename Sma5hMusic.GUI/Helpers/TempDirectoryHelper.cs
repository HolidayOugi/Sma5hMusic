using System;
using System.IO;
using System.Linq;

namespace Sma5hMusic.GUI.Helpers
{
    public static class TempDirectoryHelper
    {
        public static void DeleteIfEmpty(string directory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    return;

                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory, false);
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        public static void DeleteRecursive(string directory)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}