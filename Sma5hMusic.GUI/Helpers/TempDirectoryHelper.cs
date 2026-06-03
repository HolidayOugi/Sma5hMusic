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

        public static void DeleteContents(string directory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    return;

                foreach (var file in Directory.EnumerateFiles(directory))
                    DeleteFile(file);

                foreach (var childDirectory in Directory.EnumerateDirectories(directory))
                    DeleteDirectory(childDirectory);

                DeleteIfEmpty(directory);
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        private static void DeleteFile(string file)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        private static void DeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}