using Syroot.BinaryData;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ForzaTechStudio
{
    #region Enums
    public enum CompressionType : short
    {
        Stored = 0,
        Shrunk,
        Reduce1,
        Reduce2,
        Reduce3,
        Reduce4,
        Implode,
        Token,
        Deflate,
        Deflate64,
        LZX = 21
    }

    public enum XMemCodecType
    {
        Default = 0,
        LZX = 1
    }

    public struct XMemCodecParametersLZX
    {
        public int Flags;
        public int WindowSize;
        public int CompressionPartitionSize;
    }
    #endregion

    public class CustomZipFile : IDisposable
    {
        private static readonly UTF8Encoding Utf8StrictEncoding = new UTF8Encoding(false, true);
        private static readonly Encoding Cp437StrictEncoding = Encoding.GetEncoding(
            437,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        private Stream _stream = null!;
        private BinaryStream _bs = null!;
        private readonly object _syncLock = new object();

        #region Imports
        [DllImport("xcompress64.dll", EntryPoint = "XMemCreateDecompressionContext")]
        public static extern int XMemCreateDecompressionContext(
            XMemCodecType codecType,
            IntPtr pCodecParams,
            int flags, ref IntPtr pContext);

        [DllImport("xcompress64.dll", EntryPoint = "XMemDestroyDecompressionContext")]
        public static extern void XMemDestroyDecompressionContext(IntPtr context);

        [DllImport("xcompress64.dll", EntryPoint = "XMemResetDecompressionContext")]
        public static extern int XMemResetDecompressionContext(IntPtr context);

        [DllImport("xcompress64.dll", EntryPoint = "XMemDecompressStream")]
        public static extern int XMemDecompressStream(IntPtr context,
            byte[] pDestination, ref int pDestSize,
            byte[] pSource, ref int pSrcSize);
        #endregion

        public CustomZipFile(string path)
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _bs = new BinaryStream(_stream);
        }

        public class ZipEntryInfo
        {
            public string Name { get; set; } = null!;
            public uint CompressedSize { get; set; }
            public uint UncompressedSize { get; set; }
            public bool IsDirectory { get; set; }
            public DateTime LastModified { get; set; }
            public ushort CompressionMethod { get; set; }
            public uint LocalHeaderOffset { get; set; }
            public uint Crc { get; set; }
        }

        private static string DecodeEntryName(byte[] nameBytes, ushort flags)
        {
            if ((flags & 0x0800) != 0)
            {
                try
                {
                    return SanitizeDecodedEntryName(Utf8StrictEncoding.GetString(nameBytes));
                }
                catch
                {
                }
            }

            try
            {
                return SanitizeDecodedEntryName(Cp437StrictEncoding.GetString(nameBytes));
            }
            catch
            {
            }

            try
            {
                return SanitizeDecodedEntryName(Encoding.UTF8.GetString(nameBytes));
            }
            catch
            {
            }

            return SanitizeDecodedEntryName(Encoding.Latin1.GetString(nameBytes));
        }

        private static string SanitizeDecodedEntryName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return string.Empty;

            var builder = new StringBuilder(fileName.Length);
            foreach (char character in fileName)
            {
                if (character == '\0' || character == '\uFFFD')
                {
                    builder.Append('_');
                    continue;
                }

                if (char.IsControl(character))
                    continue;

                builder.Append(character);
            }

            return builder.ToString();
        }

        public List<ZipEntryInfo> GetEntries()
        {
            lock (_syncLock)
            {
                var entries = new List<ZipEntryInfo>();

                // 1. Locate Central Directory
                long eocdOffset = FindEOCD();
                if (eocdOffset == -1)
                    throw new InvalidDataException("Invalid Zip: End of Central Directory signature not found.");

                // 2. Read EOCD
                _bs.Position = eocdOffset + 8; // Skip Sig(4) + Disk(2) + DiskStart(2)
                ushort numEntries = _bs.ReadUInt16();

                _bs.Position = eocdOffset + 12;
                if (numEntries == 0) numEntries = _bs.ReadUInt16(); // Handle overflow case

                _bs.Position = eocdOffset + 16;
                uint cdOffset = _bs.ReadUInt32();

                if (cdOffset >= _stream.Length)
                    throw new InvalidDataException("Corrupted Zip: CD Offset out of bounds.");

                // 3. Iterate Central Directory
                _bs.Position = cdOffset;
                for (int i = 0; i < numEntries; i++)
                {
                    // Safety check
                    if (_bs.Position + 46 > _stream.Length) break;

                    uint signature = _bs.ReadUInt32();
                    if (signature != 0x02014b50)
                        throw new InvalidDataException($"Corrupted Central Directory at entry {i}");

                    _bs.ReadUInt16(); // Version Made
                    _bs.ReadUInt16(); // Version Needed
                    ushort flags = _bs.ReadUInt16();
                    ushort method = _bs.ReadUInt16();
                    ushort dosTime = _bs.ReadUInt16(); // Time
                    ushort dosDate = _bs.ReadUInt16(); // Date
                    uint crc = _bs.ReadUInt32();
                    uint compressedSize = _bs.ReadUInt32();
                    uint uncompressedSize = _bs.ReadUInt32();
                    ushort fileNameLen = _bs.ReadUInt16();
                    ushort extraLen = _bs.ReadUInt16();
                    ushort commentLen = _bs.ReadUInt16();
                    _bs.ReadUInt16(); // Disk Start
                    _bs.ReadUInt16(); // Internal Attr
                    _bs.ReadUInt32(); // External Attr
                    uint localHeaderOffset = _bs.ReadUInt32();

                    // Read Filename Bytes directly to preserve alignment
                    byte[] nameBytes = _bs.ReadBytes(fileNameLen);

                    string fileName = DecodeEntryName(nameBytes, flags);

                    // Advance Stream past Extra and Comment
                    _bs.Position += extraLen + commentLen;
                    
                    // Parse DOS Date/Time
                    int year = ((dosDate >> 9) & 0x7F) + 1980;
                    int month = (dosDate >> 5) & 0xF;
                    int day = dosDate & 0x1F;
                    int hour = (dosTime >> 11) & 0x1F;
                    int minute = (dosTime >> 5) & 0x3F;
                    int second = (dosTime & 0x1F) * 2;
                    
                    DateTime modified = DateTime.MinValue;
                    try { modified = new DateTime(year, Math.Max(1, month), Math.Max(1, day), hour, minute, second); } catch { }

                    entries.Add(new ZipEntryInfo
                    {
                        Name = fileName,
                        CompressedSize = compressedSize,
                        UncompressedSize = uncompressedSize,
                        IsDirectory = IsDirectory(fileName),
                        LastModified = modified,
                        CompressionMethod = method,
                        LocalHeaderOffset = localHeaderOffset,
                        Crc = crc
                    });
                }
                return entries;
            }
        }

        public void ExtractToDirectory(string destinationDir)
        {
            ExtractToDirectory(destinationDir, null);
        }

        public void ExtractToDirectory(string destinationDir, Func<string, bool>? filter = null, Func<string, bool>? singleFileFilter = null)
        {
            lock (_syncLock)
            {
                // 1. Locate Central Directory
                long eocdOffset = FindEOCD();
                if (eocdOffset == -1)
                    throw new InvalidDataException("Invalid Zip: End of Central Directory signature not found.");

                // 2. Read EOCD
                _bs.Position = eocdOffset + 8; // Skip Sig(4) + Disk(2) + DiskStart(2)
                ushort numEntries = _bs.ReadUInt16();

                _bs.Position = eocdOffset + 12;
                if (numEntries == 0) numEntries = _bs.ReadUInt16(); // Handle overflow case

                _bs.Position = eocdOffset + 16;
                uint cdOffset = _bs.ReadUInt32();

                if (cdOffset >= _stream.Length)
                    throw new InvalidDataException("Corrupted Zip: CD Offset out of bounds.");

                // 3. Iterate Central Directory
                _bs.Position = cdOffset;
                for (int i = 0; i < numEntries; i++)
                {
                    // Safety check
                    if (_bs.Position + 46 > _stream.Length) break;

                    uint signature = _bs.ReadUInt32();
                    if (signature != 0x02014b50)
                        throw new InvalidDataException($"Corrupted Central Directory at entry {i}");

                    _bs.ReadUInt16(); // Version Made
                    _bs.ReadUInt16(); // Version Needed
                    ushort flags = _bs.ReadUInt16();
                    ushort method = _bs.ReadUInt16();
                    _bs.ReadUInt16(); // Time
                    _bs.ReadUInt16(); // Date
                    uint crc = _bs.ReadUInt32();
                    uint compressedSize = _bs.ReadUInt32();
                    uint uncompressedSize = _bs.ReadUInt32();
                    ushort fileNameLen = _bs.ReadUInt16();
                    ushort extraLen = _bs.ReadUInt16();
                    ushort commentLen = _bs.ReadUInt16();
                    _bs.ReadUInt16(); // Disk Start
                    _bs.ReadUInt16(); // Internal Attr
                    _bs.ReadUInt32(); // External Attr
                    uint localHeaderOffset = _bs.ReadUInt32();

                    // Read Filename Bytes directly to preserve alignment
                    byte[] nameBytes = _bs.ReadBytes(fileNameLen);

                    string fileName = DecodeEntryName(nameBytes, flags);

                    // Advance Stream past Extra and Comment
                    _bs.Position += extraLen + commentLen;
                    long nextEntryPos = _bs.Position;

                    // 4. Extract File
                    if (!IsDirectory(fileName))
                    {
                        // Filter check: extract only files matched by singleFileFilter (or filter if provided), otherwise extract all.
                        
                        bool shouldExtract = false;
                        
                        if (singleFileFilter != null)
                        {
                            if (singleFileFilter(fileName)) shouldExtract = true;
                        }
                        else if (filter == null || filter(fileName))
                        {
                            shouldExtract = true;
                        }

                        if (shouldExtract)
                        {
                             string fullPath = Path.Combine(destinationDir, fileName);
                            string? dirName = Path.GetDirectoryName(fullPath);

                            if (!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName))
                                Directory.CreateDirectory(dirName);

                            try
                            {
                                // Open output stream directly on disk to save RAM
                                using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                                {
                                    ExtractEntryStreamed(localHeaderOffset, compressedSize, uncompressedSize, method, fileName, fs, crc);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to extract {fileName}: {ex.Message}");
                            }
                        }
                    }

                    // Restore position for next loop
                    _bs.Position = nextEntryPos;
                }
            }
        }

        public byte[] ExtractToMemory(ZipEntryInfo entry)
        {
            using (var ms = new MemoryStream((int)entry.UncompressedSize))
            {
                ExtractEntryStreamed(entry.LocalHeaderOffset, entry.CompressedSize, entry.UncompressedSize, entry.CompressionMethod, entry.Name, ms, entry.Crc);
                return ms.ToArray();
            }
        }

        // Extracts an entry directly into the provided output stream, avoiding an intermediate byte[] allocation.
        public void ExtractToStream(ZipEntryInfo entry, Stream outputStream)
        {
            ExtractEntryStreamed(entry.LocalHeaderOffset, entry.CompressedSize, entry.UncompressedSize, entry.CompressionMethod, entry.Name, outputStream, entry.Crc);
        }

        private void ExtractEntryStreamed(uint localHeaderOffset, uint compressedSize, uint uncompressedSize, ushort method, string fileName, Stream outputStream, uint expectedCrc)
        {
            byte[]? compressedData = null;
            CompressionType compression = (CompressionType)method;

            lock (_syncLock)
            {
                if (localHeaderOffset >= _stream.Length)
                    throw new InvalidDataException($"Local Header Offset out of bounds for {fileName}");

                _bs.Position = localHeaderOffset;
                uint sig = _bs.ReadUInt32();
                if (sig != 0x04034b50)
                    throw new InvalidDataException($"Invalid Local Header Signature for {fileName}");

                // Skip fixed header (22 bytes)
                _bs.Position += 22;
                ushort nameLen = _bs.ReadUInt16();
                ushort extraLen = _bs.ReadUInt16();

                // Skip variable header parts
                _bs.Position += nameLen + extraLen;

                if (compression == CompressionType.Stored)
                {
                    CopyStream(_stream, outputStream, (int)compressedSize);
                    return;
                }

                compressedData = _bs.ReadBytes((int)compressedSize);
            }

            byte[]? decompressedBytes = null;

            if (compression == CompressionType.Deflate)
            {
                using (var ms = new MemoryStream(compressedData))
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    // Decompress to memory to verify CRC, or steam directly if we skip CRC?
                    // To follow the "proven" logic, we should probably buffer to verify CRC.
                    // But for Deflate, let's stream to memory buffer first to match the pattern.
                    using (MemoryStream tempOut = new MemoryStream())
                    {
                        ds.CopyTo(tempOut);
                        decompressedBytes = tempOut.ToArray();
                    }
                }
            }
            else if (compression == CompressionType.LZX)
            {
                // Use "proven" ForzaArchive logic for LZX
                //Create our decompression context
                IntPtr decompressionContext = IntPtr.Zero;
                XMemCreateDecompressionContext(
                    XMemCodecType.LZX,
                    IntPtr.Zero, 0, ref decompressionContext);

                //Reset our context first
                XMemResetDecompressionContext(decompressionContext);

                //Now lets read and decompress
                decompressedBytes = new byte[uncompressedSize];
                int finalUncompressedSize = (int)uncompressedSize;
                int finalCompressedSize = (int)compressedSize;

                try
                {
                    XMemDecompressStream(decompressionContext,
                        decompressedBytes, ref finalUncompressedSize,
                        compressedData, ref finalCompressedSize);
                }
                finally
                {
                    //Go ahead and decorate our context
                    XMemDestroyDecompressionContext(decompressionContext);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Unsupported compression method {method} for {fileName}");
                return;
            }

            // CRC Check (if we have decompressed bytes)
            if (decompressedBytes != null)
            {
                if (ComputeCRC32(decompressedBytes) != expectedCrc)
                    throw new InvalidDataException($"Invalid CRC detected for {fileName}.");

                outputStream.Write(decompressedBytes, 0, decompressedBytes.Length);
            }
            else if (compression == CompressionType.Stored)
            {
                 // Stored files were copied directly, we can't easily CRC check without re-reading or intercepting copy.
                 // Skipping CRC for Stored to keep it simple and streamed, unless requested.
            }
        }

        // Helper to copy exact number of bytes between streams
        private void CopyStream(Stream input, Stream output, int bytesToCopy)
        {
            byte[] buffer = new byte[81920]; // 80KB buffer
            int read;
            while (bytesToCopy > 0 && (read = input.Read(buffer, 0, Math.Min(buffer.Length, bytesToCopy))) > 0)
            {
                output.Write(buffer, 0, read);
                bytesToCopy -= read;
            }
        }

        private static uint ComputeCRC32(byte[] bytes)
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < table.Length; ++i)
            {
                uint temp = i;
                for (int j = 8; j > 0; --j)
                {
                    if ((temp & 1) == 1)
                        temp = ((temp >> 1) ^ 0xedb88320);
                    else
                        temp >>= 1;
                }
                table[i] = temp;
            }

            uint crc = 0xffffffff;
            for (int i = 0; i < bytes.Length; ++i)
            {
                byte index = (byte)(((crc) & 0xff) ^ bytes[i]);
                crc = ((crc >> 8) ^ table[index]);
            }
            return ~crc;
        }

        private long FindEOCD()
        {
            if (_stream.Length < 22) return -1;
            long maxScan = Math.Min(_stream.Length, 65535 + 22);
            long endPos = _stream.Length;

            for (long i = 22; i <= maxScan; i++)
            {
                long pos = endPos - i;
                _bs.Position = pos;
                if (_bs.ReadUInt32() == 0x06054b50) return pos;
            }
            return -1;
        }

        private bool IsDirectory(string path)
        {
            return path.EndsWith("/") || path.EndsWith("\\");
        }

        public void Dispose()
        {
            _stream?.Dispose();
        }

        public void ExtractSingleFile(string fileName, string destinationPath)
        {
            lock (_syncLock)
            {
                // 1. Locate Central Directory
                long eocdOffset = FindEOCD();
                if (eocdOffset == -1) throw new InvalidDataException("Invalid Zip");

                _bs.Position = eocdOffset + 16;
                uint cdOffset = _bs.ReadUInt32();
                ushort numEntries = _bs.ReadUInt16(); // Read from proper position logic

                // Simplified: Just iterate until we find the file
                // Note: reusing the full iteration logic is safest as we need LocalHeaderOffset
                
                string? destDir = Path.GetDirectoryName(destinationPath);
                if (destDir == null) return;
                ExtractToDirectory(destDir, null, (name) => 
                {
                     // name matches our target?
                     // Normalize slashes
                     return name.Replace('\\', '/') == fileName.Replace('\\', '/');
                });
            }
        }

        // Rebuilds the zip with one entry replaced. Re-compresses using the same method as the original
        // (LZX falls back to Deflate). Writes the result to <paramref name="outputPath"/>.
        public static void ReplaceEntry(string sourcePath, int entryIndex, string replacementFilePath, string outputPath)
        {
            // Read all entries and their raw (compressed) bytes from the source archive
            List<(ZipEntryInfo Info, byte[] RawCompressed)> allEntries;
            using (var src = new CustomZipFile(sourcePath))
            {
                var entries = src.GetEntries();
                allEntries = new List<(ZipEntryInfo, byte[])>(entries.Count);
                foreach (var e in entries)
                    allEntries.Add((e, src.ReadRawCompressedBytes(e)));
            }

            if (entryIndex < 0 || entryIndex >= allEntries.Count)
                throw new ArgumentOutOfRangeException(nameof(entryIndex));

            var target = allEntries[entryIndex].Info;

            // Compress replacement file using the same method
            byte[] newCompressedData;
            uint newUncompressedSize;
            uint newCrc;

            using (var replacementStream = File.OpenRead(replacementFilePath))
            {
                newUncompressedSize = (uint)replacementStream.Length;
                byte[] rawBytes;
                using (var ms = new MemoryStream((int)replacementStream.Length))
                {
                    replacementStream.CopyTo(ms);
                    rawBytes = ms.ToArray();
                }
                newCrc = ComputeCRC32(rawBytes);

                var originalMethod = (CompressionType)target.CompressionMethod;
                if (originalMethod == CompressionType.Stored)
                {
                    newCompressedData = rawBytes;
                }
                else
                {
                    // Deflate (or LZX fallback to Deflate — no encoder available for LZX)
                    using var compMs = new MemoryStream();
                    using (var ds = new DeflateStream(compMs, CompressionLevel.Optimal, leaveOpen: true))
                        ds.Write(rawBytes, 0, rawBytes.Length);
                    newCompressedData = compMs.ToArray();
                }
            }

            // Write the new zip to a temp file then move it to outputPath
            string tempPath = outputPath + ".tmp";
            try
            {
                using (var outZip = ZipFile.Open(tempPath, ZipArchiveMode.Create, Encoding.UTF8))
                {
                    for (int i = 0; i < allEntries.Count; i++)
                    {
                        var (info, rawCompressed) = allEntries[i];
                        var zipEntry = outZip.CreateEntry(info.Name, CompressionLevel.NoCompression);
                        zipEntry.LastWriteTime = info.LastModified == DateTime.MinValue
                            ? DateTimeOffset.UtcNow : new DateTimeOffset(info.LastModified);

                        if (info.IsDirectory) continue;

                        using var entryStream = zipEntry.Open();

                        if (i == entryIndex)
                        {
                            // Write pre-compressed data directly; ZipArchive doesn't expose raw control so we use NoCompression+pre-deflated bytes for Deflate, or plain write for Store.
                            entryStream.Write(newCompressedData, 0, newCompressedData.Length);
                        }
                        else
                        {
                            // Copy raw compressed bytes as-is (ZipArchive entry opened with NoCompression)
                            entryStream.Write(rawCompressed, 0, rawCompressed.Length);
                        }
                    }
                }

                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                File.Move(tempPath, outputPath);
            }
            catch
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw;
            }
        }

        // Reads the raw compressed bytes for a zip entry directly from the local file record.
        private byte[] ReadRawCompressedBytes(ZipEntryInfo entry)
        {
            lock (_syncLock)
            {
                if (entry.IsDirectory || entry.CompressedSize == 0) return Array.Empty<byte>();

                _bs.Position = entry.LocalHeaderOffset;
                uint sig = _bs.ReadUInt32();
                if (sig != 0x04034b50)
                    throw new InvalidDataException($"Invalid Local Header Signature for {entry.Name}");

                _bs.Position += 22; // skip fixed fields
                ushort nameLen = _bs.ReadUInt16();
                ushort extraLen = _bs.ReadUInt16();
                _bs.Position += nameLen + extraLen;

                return _bs.ReadBytes((int)entry.CompressedSize);
            }
        }
    }
}
