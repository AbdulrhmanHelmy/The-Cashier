using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace AlKhayal.UnitTests
{
    internal static class TestRepositoryLocator
    {
        public static DirectoryInfo Find([CallerFilePath] string sourceFile = null)
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory, Path.GetDirectoryName(sourceFile) })
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "AlKhayal.slnx"))) return directory;
                    directory = directory.Parent;
                }
            }
            return null;
        }
    }
}
