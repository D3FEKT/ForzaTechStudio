using System.IO;
using System.IO.Compression;

namespace ForzaTechStudio.Services
{
    public static class ZipArchiveHelper
    {
        public static void ReplaceEntry(string zipPath, string entryName, byte[] data)
        {
            using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.ReadWrite))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry(entryName);
                if (entry != null)
                {
                    entry.Delete();
                }

                // Normal Deflate compression uses a 32kb dictionary by default in ZipArchive
                var newEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using (var entryStream = newEntry.Open())
                {
                    entryStream.Write(data, 0, data.Length);
                }
            }
        }
    }
}
