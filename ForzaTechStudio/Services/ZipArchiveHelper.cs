using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ForzaTechStudio.Services
{
    public static class ZipArchiveHelper
    {
        private const ushort StoreMethod = 0;
        private const ushort DeflateMethod = 8;
        private const ushort Utf8Flag = 0x0800;

        public static void ReplaceEntry(string zipPath, string entryName, byte[] data)
        {
            ReplaceEntries(zipPath, new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [NormalizeEntryName(entryName)] = data
            });
        }

        public static void ReplaceEntries(string zipPath, IReadOnlyDictionary<string, byte[]> replacements)
        {
            if (string.IsNullOrWhiteSpace(zipPath))
                throw new ArgumentException("ZIP path is required.", nameof(zipPath));
            if (replacements == null || replacements.Count == 0)
                return;

            var normalisedReplacements = replacements
                .ToDictionary(kvp => NormalizeEntryName(kvp.Key), kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            string directory = Path.GetDirectoryName(zipPath) ?? ".";
            string tempPath = Path.Combine(directory, $".{Path.GetFileName(zipPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                List<ZipRebuildEntry> rebuildEntries;
                using (var zip = new CustomZipFile(zipPath))
                {
                    var entries = zip.GetEntries();
                    rebuildEntries = new List<ZipRebuildEntry>(entries.Count);

                    foreach (var entry in entries)
                    {
                        string normalisedName = NormalizeEntryName(entry.Name);
                        byte[] payload;
                        if (entry.IsDirectory)
                        {
                            payload = Array.Empty<byte>();
                        }
                        else if (normalisedReplacements.TryGetValue(normalisedName, out var replacement))
                        {
                            payload = replacement;
                        }
                        else
                        {
                            payload = zip.ExtractToMemory(entry);
                        }

                        rebuildEntries.Add(new ZipRebuildEntry(
                            normalisedName,
                            entry.IsDirectory,
                            entry.LastModified == DateTime.MinValue ? File.GetLastWriteTime(zipPath) : entry.LastModified,
                            payload));
                    }
                }

                WriteZip(tempPath, rebuildEntries);
                ReplaceFile(tempPath, zipPath);
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
        }

        private static void WriteZip(string outputPath, IReadOnlyList<ZipRebuildEntry> entries)
        {
            using var fs = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false);
            var directoryEntries = new List<CentralDirectoryEntry>(entries.Count);

            foreach (var entry in entries)
            {
                var entryName = entry.IsDirectory && !entry.Name.EndsWith('/') ? entry.Name + "/" : entry.Name;
                var nameBytes = Encoding.UTF8.GetBytes(entryName);
                ushort method = entry.IsDirectory ? StoreMethod : DeflateMethod;
                byte[] dataToWrite = entry.IsDirectory ? Array.Empty<byte>() : Deflate(entry.Data);
                uint crc = entry.IsDirectory ? 0 : ComputeCrc32(entry.Data);
                (ushort time, ushort date) = ToDosDateTime(entry.LastModified);
                long localHeaderOffset = fs.Position;

                writer.Write(0x04034b50u);
                writer.Write((ushort)(method == DeflateMethod ? 20 : 10));
                writer.Write(Utf8Flag);
                writer.Write(method);
                writer.Write(time);
                writer.Write(date);
                writer.Write(crc);
                writer.Write((uint)dataToWrite.Length);
                writer.Write((uint)entry.Data.Length);
                writer.Write((ushort)nameBytes.Length);
                writer.Write((ushort)0);
                writer.Write(nameBytes);
                writer.Write(dataToWrite);

                directoryEntries.Add(new CentralDirectoryEntry(
                    nameBytes,
                    method,
                    time,
                    date,
                    crc,
                    (uint)dataToWrite.Length,
                    (uint)entry.Data.Length,
                    (uint)localHeaderOffset,
                    entry.IsDirectory));
            }

            long centralDirectoryOffset = fs.Position;
            foreach (var entry in directoryEntries)
            {
                writer.Write(0x02014b50u);
                writer.Write((ushort)20);
                writer.Write((ushort)(entry.Method == DeflateMethod ? 20 : 10));
                writer.Write(Utf8Flag);
                writer.Write(entry.Method);
                writer.Write(entry.Time);
                writer.Write(entry.Date);
                writer.Write(entry.Crc);
                writer.Write(entry.CompressedSize);
                writer.Write(entry.UncompressedSize);
                writer.Write((ushort)entry.NameBytes.Length);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write(entry.IsDirectory ? 0x10u : 0u);
                writer.Write(entry.LocalHeaderOffset);
                writer.Write(entry.NameBytes);
            }

            long centralDirectorySize = fs.Position - centralDirectoryOffset;
            writer.Write(0x06054b50u);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)directoryEntries.Count);
            writer.Write((ushort)directoryEntries.Count);
            writer.Write((uint)centralDirectorySize);
            writer.Write((uint)centralDirectoryOffset);
            writer.Write((ushort)0);
        }

        private static byte[] Deflate(byte[] data)
        {
            using var output = new MemoryStream();
            var deflater = new Deflater(Deflater.DEFAULT_COMPRESSION, true);
            using (var deflate = new DeflaterOutputStream(output, deflater))
            {
                deflate.Write(data, 0, data.Length);
                deflate.Finish();
            }
            return output.ToArray();
        }

        private static void ReplaceFile(string sourcePath, string destinationPath)
        {
            try
            {
                File.Replace(sourcePath, destinationPath, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Delete(destinationPath);
                File.Move(sourcePath, destinationPath);
            }
            catch (IOException)
            {
                File.Delete(destinationPath);
                File.Move(sourcePath, destinationPath);
            }
        }

        private static string NormalizeEntryName(string entryName) => entryName.Replace('\\', '/');

        private static (ushort Time, ushort Date) ToDosDateTime(DateTime value)
        {
            if (value.Year < 1980)
                value = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Local);

            ushort time = (ushort)((value.Hour << 11) | (value.Minute << 5) | (value.Second / 2));
            ushort date = (ushort)(((value.Year - 1980) << 9) | (value.Month << 5) | value.Day);
            return (time, date);
        }

        private static uint ComputeCrc32(byte[] bytes)
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                    value = (value & 1) == 1 ? (value >> 1) ^ 0xedb88320u : value >> 1;
                table[i] = value;
            }

            uint crc = 0xffffffffu;
            foreach (byte b in bytes)
            {
                byte index = (byte)((crc & 0xff) ^ b);
                crc = (crc >> 8) ^ table[index];
            }
            return ~crc;
        }

        private sealed record ZipRebuildEntry(string Name, bool IsDirectory, DateTime LastModified, byte[] Data);

        private sealed record CentralDirectoryEntry(
            byte[] NameBytes,
            ushort Method,
            ushort Time,
            ushort Date,
            uint Crc,
            uint CompressedSize,
            uint UncompressedSize,
            uint LocalHeaderOffset,
            bool IsDirectory);
    }
}
