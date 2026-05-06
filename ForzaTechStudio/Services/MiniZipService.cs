using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace ForzaTechStudio.Services
{
    // Data types

    public enum MiniZipResourceType
    {
        Procedural = 0,
        PhysicsTemplate = 1,
        MemoryPlaceholder = 2,
        AIOpenWorldBlock = 3,
        ModelBin = 4,
        Texture = 5,
        Unknown = 6,
        ModelGr2 = 7,
        TriggerZone = 8,
        AudioSoundscape = 9,
        AudioSoundBank = 10,
        LightBlock = 11,
        PVSZone = 12,
        AmbientOcclusion = 13,
        WaterDepth = 14,
        HavokNavMesh = 15,
        VoxelGIPack = 16,
        MegaTextureChunk = 17,
        MegaTextureSource = 18,
        ProceduralMap = 19,
        FilePack = 20,
        IndirectLight = 22,
        MegaTextureSourceHQ = 24,
        EntityStreamingCell = 25,
        VolumetricFog = 26,
        VolumetricFogTexture = 27,
        Unk28 = 28,
    }

    public class MiniZipEntryInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }      // May be empty if no ChunkContents file
        public string Extension { get; set; } // Derived from ResourceType
        public MiniZipResourceType ResourceType { get; set; }
        public uint UncompressedSize { get; set; }
        public uint CompressedSize { get; set; }
        public ushort CompressMethod { get; set; }
        public ushort ParentDirIndex { get; set; }
        internal ulong DataOffset { get; set; }
        internal byte Padding { get; set; }
    }

    // MiniZipService

    // Reads and extracts Playground MiniZip (.minizip / PGZP) files.
    // These are chunk-based streaming archives used by Forza titles.
    // They require a companion ChunkMap*.dat file in the same directory.
    public class MiniZipService : IDisposable
    {
        public const uint MAGIC = 0x505A4750; // PGZP

        public uint Version { get; private set; }
        public uint NumDirEntries { get; private set; }
        public uint NumFolders { get; private set; }
        public uint FilesPerChunk { get; private set; }
        public uint NumSubChunks { get; private set; }

        public List<MiniZipEntryInfo> Entries { get; } = new();

        private string _filePath;
        private Stream _baseStream;

        // Raw sub-chunk data for extraction
        private readonly List<MiniZipSubChunk> _subChunks = new();
        private MiniZipRawEntry _lastEntry;

        public MiniZipService(string path)
        {
            _filePath = path;
            _baseStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Load();
        }

        // Loading

        private void Load()
        {
            using var br = new BinaryReader(new NonClosingStreamWrapper(_baseStream), System.Text.Encoding.UTF8, leaveOpen: true);
            _baseStream.Position = 0;

            if (br.ReadUInt32() != MAGIC)
                throw new InvalidDataException("Not a minizip file (magic did not match).");

            Version = br.ReadUInt32();
            uint folderIndicesOffset = br.ReadUInt32();
            NumDirEntries = br.ReadUInt32();
            NumFolders = br.ReadUInt32();
            FilesPerChunk = br.ReadUInt32();
            NumSubChunks = br.ReadUInt32();
            br.ReadUInt32(); // unk

            // Skip folder indices
            // (We don't need them for viewing/extracting)

            // Read sub-chunks & file entries
            _baseStream.Position = folderIndicesOffset;

            // Calculate size of folder indices block so we can skip it
            uint totalSizeIndices = sizeof(uint) * NumFolders + 4;
            if (((sizeof(uint) * (byte)NumFolders + 4) % 8) != 0)
                totalSizeIndices = sizeof(uint) * NumFolders + 8;

            // Advance past folder indices
            _baseStream.Position = folderIndicesOffset + totalSizeIndices;

            long numFiles = NumDirEntries;
            int idx = 0;
            for (int i = 0; i < NumSubChunks; i++)
            {
                var chunk = new MiniZipSubChunk();
                chunk.Index = i;
                chunk.DataStartOffset = br.ReadUInt64();

                int filesThisChunk = (int)Math.Min(FilesPerChunk, numFiles);
                for (int j = 0; j < filesThisChunk; j++)
                {
                    var raw = ReadRawEntry(br);
                    raw.Index = idx;
                    raw.ChunkFileIndex = j;
                    raw.ParentChunk = chunk;
                    raw.DataOffset = chunk.DataStartOffset + raw.RelativeDataOffset;
                    chunk.Entries.Add(raw);
                    idx++;
                }

                numFiles -= filesThisChunk;
                _subChunks.Add(chunk);
            }

            _lastEntry = ReadRawEntry(br);

            CalculateCompressedSizes();
            LoadCompanionFiles();
        }

        private MiniZipRawEntry ReadRawEntry(BinaryReader br)
        {
            var e = new MiniZipRawEntry();
            e.RelativeDataOffset = br.ReadUInt32();
            e.UncompressedSize = br.ReadUInt32();
            ushort flags = br.ReadUInt16();
            if (Version >= 101)
            {
                e.CompressMethod = (ushort)(flags & 0xFFF);
                e.Padding = (byte)(flags >> 12);
            }
            else
            {
                e.CompressMethod = (ushort)(flags & 0x3FFF);
                e.Padding = (byte)(flags >> 14);
            }
            e.ParentDirIndex = br.ReadUInt16();
            return e;
        }

        private void CalculateCompressedSizes()
        {
            for (int i = 0; i < NumDirEntries; i++)
            {
                var info = GetRawEntry(i);
                if (info.CompressMethod == 0)
                    continue;

                var next = i < NumDirEntries - 1 ? GetRawEntry(i + 1) : _lastEntry;
                info.CompressedSize = (uint)(next.DataOffset - info.DataOffset);
                info.CompressedSize -= info.Padding;
            }
        }

        private void LoadCompanionFiles()
        {
            string dir = Path.GetDirectoryName(_filePath);
            string baseName = Path.GetFileNameWithoutExtension(_filePath);

            // Try to read ChunkMap
            List<MiniZipResourceType> types = null;
            if (baseName.StartsWith("GeoChunk") && int.TryParse(baseName.AsSpan("GeoChunk".Length), out int geoId))
            {
                string chunkMapPath = Path.Combine(dir, $"ChunkMap{geoId}.dat");
                if (File.Exists(chunkMapPath))
                    types = LoadChunkMap(chunkMapPath);

                // Try ChunkContents for names
                string contentsPath = Path.Combine(dir, $"ChunkContentsMiniZip{geoId}.txt");
                List<string> names = null;
                if (File.Exists(contentsPath))
                    names = LoadChunkContents(contentsPath);

                BuildEntries(types, names);
            }
            else
            {
                // Generic minizip without naming convention - still try to load
                BuildEntries(null, null);
            }
        }

        private List<MiniZipResourceType> LoadChunkMap(string path)
        {
            var result = new List<MiniZipResourceType>();
            using var fs = new FileStream(path, FileMode.Open);
            using var br = new BinaryReader(fs);
            for (uint i = 0; i < NumDirEntries; i++)
            {
                br.ReadInt16(); // unk
                br.ReadByte();  // unk2
                result.Add((MiniZipResourceType)br.ReadByte());
            }
            return result;
        }

        private List<string> LoadChunkContents(string path)
        {
            var result = new List<string>();
            using var sr = new StreamReader(path);
            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();
                if (string.IsNullOrEmpty(line)) continue;
                line = line.Replace("<PREZIPPED>", "").Replace("d:\\", "");
                string[] spl = line.Split('|');
                result.Add(spl[0]);
            }
            return result;
        }

        private void BuildEntries(List<MiniZipResourceType> types, List<string> names)
        {
            for (int i = 0; i < NumDirEntries; i++)
            {
                var raw = GetRawEntry(i);
                var resType = (types != null && i < types.Count) ? types[i] : MiniZipResourceType.Unknown;
                string ext = GetExtension(resType);
                string name = (names != null && i < names.Count) ? names[i] : $"{i}.{ext}";

                Entries.Add(new MiniZipEntryInfo
                {
                    Index = i,
                    Name = name,
                    Extension = ext,
                    ResourceType = resType,
                    UncompressedSize = raw.UncompressedSize,
                    CompressedSize = raw.CompressMethod != 0 ? raw.CompressedSize : raw.UncompressedSize,
                    CompressMethod = raw.CompressMethod,
                    ParentDirIndex = raw.ParentDirIndex,
                    DataOffset = raw.DataOffset,
                    Padding = raw.Padding,
                });
            }
        }

        // Extraction

        // Extracts all entries to the given output directory.
        public void ExtractAll(string outputDir)
        {
            for (int i = 0; i < Entries.Count; i++)
                ExtractEntry(i, outputDir);
        }

        // Reads the first 4 bytes of an entry's decompressed content to check for a magic number,
        // without fully decompressing the entry. Returns false if the entry is too small or an error occurs.
        public bool ProbeEntryMagic(int index, out uint magic)
        {
            magic = 0;
            try
            {
                var raw = GetRawEntry(index);
                if (raw.UncompressedSize < 4) return false;

                _baseStream.Position = (long)raw.DataOffset;
                Stream src = GetDecompressor(_baseStream, raw.CompressMethod);

                byte[] buf = new byte[4];
                int read = 0;
                while (read < 4)
                {
                    int n = src.Read(buf, read, 4 - read);
                    if (n == 0) return false;
                    read += n;
                }

                magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(buf);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Extracts a single entry by index directly into <paramref name="outputStream"/>.
        public void ExtractEntryToStream(int index, Stream outputStream)
        {
            var raw = GetRawEntry(index);
            _baseStream.Position = (long)raw.DataOffset;

            Stream src = GetDecompressor(_baseStream, raw.CompressMethod);
            const int BufferSize = 0x20000;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            long rem = raw.UncompressedSize;
            while (rem > 0)
            {
                int chunk = (int)Math.Min(rem, BufferSize);
                src.ReadExactly(buffer, 0, chunk);
                outputStream.Write(buffer, 0, chunk);
                rem -= chunk;
            }
            ArrayPool<byte>.Shared.Return(buffer);
        }

        // Extracts a single entry by index to the given output directory.
        public void ExtractEntry(int index, string outputDir)
        {
            var entry = Entries[index];
            var raw = GetRawEntry(index);

            string outputPath = Path.Combine(outputDir, entry.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using var output = new FileStream(outputPath, FileMode.Create);
            _baseStream.Position = (long)raw.DataOffset;

            Stream src = GetDecompressor(_baseStream, raw.CompressMethod);
            const int BufferSize = 0x20000;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            long rem = raw.UncompressedSize;
            while (rem > 0)
            {
                int chunk = (int)Math.Min(rem, BufferSize);
                src.ReadExactly(buffer, 0, chunk);
                output.Write(buffer, 0, chunk);
                rem -= chunk;
            }
            ArrayPool<byte>.Shared.Return(buffer);
        }

        // Rebuilds the minizip file with one entry replaced by <paramref name="replacementFilePath"/>.
        // The replacement is compressed using the same method as the original entry (Store or Deflate).
        // The rebuilt file is written to <paramref name="outputPath"/> (may be the same as the source).
        public static void ReplaceEntry(string sourcePath, int entryIndex, string replacementFilePath, string outputPath)
        {
            // Load the minizip fully so we have all structure data
            using var src = new MiniZipService(sourcePath);

            if (entryIndex < 0 || entryIndex >= src.Entries.Count)
                throw new ArgumentOutOfRangeException(nameof(entryIndex));

            var targetInfo = src.Entries[entryIndex];
            var targetRaw = src.GetRawEntry(entryIndex);

            // Read and (re-)compress the replacement file using the same method as the original
            byte[] newData;
            uint newUncompressedSize;
            ushort newMethod = targetRaw.CompressMethod;

            {
                byte[] rawBytes = File.ReadAllBytes(replacementFilePath);
                newUncompressedSize = (uint)rawBytes.Length;

                if (newMethod == 8) // Deflate
                {
                    using var ms = new MemoryStream();
                    if (src.Version == 101)
                    {
                        using var ds = new SharpCompress.Compressors.Deflate.DeflateStream(
                            ms, SharpCompress.Compressors.CompressionMode.Compress);
                        ds.Write(rawBytes, 0, rawBytes.Length);
                    }
                    else
                    {
                        using var ds = new ICSharpCode.SharpZipLib.Zip.Compression.Streams.DeflaterOutputStream(ms);
                        ds.Write(rawBytes, 0, rawBytes.Length);
                        ds.Finish();
                    }
                    newData = ms.ToArray();
                }
                else if (newMethod == 22)
                {
                    throw new NotSupportedException("Method 22 (deflate + tfit) replacement is not supported.");
                }
                else
                {
                    // Store � raw bytes as-is
                    newData = rawBytes;
                }
            }

            // Read all raw compressed payloads for every entry (except the one being replaced)
            var payloads = new List<byte[]>(src.Entries.Count);
            for (int i = 0; i < src.Entries.Count; i++)
            {
                if (i == entryIndex)
                {
                    payloads.Add(newData);
                }
                else
                {
                    var raw = src.GetRawEntry(i);
                    byte[] payload = new byte[raw.CompressMethod != 0 ? raw.CompressedSize : raw.UncompressedSize];
                    src._baseStream.Position = (long)raw.DataOffset;
                    src._baseStream.ReadExactly(payload, 0, payload.Length);
                    payloads.Add(payload);
                }
            }

            string tempPath = outputPath + ".tmp";
            try
            {
                src.WriteRebuilt(tempPath, payloads, newUncompressedSize, (ushort)newMethod, entryIndex);

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

        // Writes a rebuilt minizip to <paramref name="destPath"/> using the given per-entry payloads.
        private void WriteRebuilt(string destPath, List<byte[]> payloads, uint newUncompressedSize, ushort newMethod, int replacedIndex)
        {
            // Re-emit the full binary format: header, folder indices, chunk records (DataStartOffset + per-file entries), then data blocks.

            using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: true);

            // Reconstruct per-entry uncompressed sizes & methods
            var uncompSizes = new uint[NumDirEntries];
            var methods = new ushort[NumDirEntries];
            var parentDirs = new ushort[NumDirEntries];
            for (int i = 0; i < NumDirEntries; i++)
            {
                var raw = GetRawEntry(i);
                uncompSizes[i] = raw.UncompressedSize;
                methods[i] = raw.CompressMethod;
                parentDirs[i] = raw.ParentDirIndex;
            }
            uncompSizes[replacedIndex] = newUncompressedSize;
            methods[replacedIndex] = newMethod;

            // Read original folder indices block so we can preserve it
            // Re-read from source to get the raw folder indices bytes
            _baseStream.Position = 0;
            using var srcBr = new BinaryReader(new NonClosingStreamWrapper(_baseStream), System.Text.Encoding.UTF8, leaveOpen: true);
            srcBr.ReadUInt32(); // magic
            srcBr.ReadUInt32(); // version
            uint folderIndicesOffset = srcBr.ReadUInt32();
            srcBr.ReadUInt32(); srcBr.ReadUInt32(); srcBr.ReadUInt32(); srcBr.ReadUInt32(); srcBr.ReadUInt32(); // rest of header

            uint totalSizeIndices = sizeof(uint) * NumFolders + 4;
            if (((sizeof(uint) * (byte)NumFolders + 4) % 8) != 0)
                totalSizeIndices = sizeof(uint) * NumFolders + 8;

            _baseStream.Position = folderIndicesOffset;
            byte[] folderIndicesBlock = new byte[totalSizeIndices];
            _baseStream.ReadExactly(folderIndicesBlock, 0, (int)totalSizeIndices);

            // Calculate new data layout
            // Distribute payloads into sub-chunks of FilesPerChunk each
            // We'll compute DataStartOffset for each chunk relative to the start of the data region,
            // then fix it up after we know where the header ends.

            // Compute header space needed: 32-byte header + folder-indices + chunk records + last entry.

            uint headerSize = folderIndicesOffset; // = 32
            uint indicesSize = totalSizeIndices;
            // Each chunk: 8 (DataStartOffset) + FilesPerChunk * 8 (entries, each 8 bytes: 4+4+2+2)
            // LastEntry: 8 bytes
            long chunkRecordsStart = headerSize + indicesSize;
            long chunkRecordsSize = 0;
            long numFiles = NumDirEntries;
            for (int i = 0; i < NumSubChunks; i++)
            {
                int filesThisChunk = (int)Math.Min(FilesPerChunk, numFiles);
                chunkRecordsSize += 8 + filesThisChunk * 8; // DataStartOffset + N entry records
                numFiles -= filesThisChunk;
            }
            chunkRecordsSize += 8; // LastEntry

            long dataRegionStart = chunkRecordsStart + chunkRecordsSize;

            // Compute new DataStartOffset and RelativeDataOffset for each chunk/entry
            // Each sub-chunk's data block is contiguous. Within a sub-chunk, entries are ordered by index.
            // RelativeDataOffset is relative to the chunk's DataStartOffset.
            var chunkDataStarts = new ulong[NumSubChunks];   // absolute data offsets
            // For each entry: its absolute data offset
            var entryAbsoluteOffsets = new ulong[NumDirEntries];

            ulong currentDataPos = (ulong)dataRegionStart;
            numFiles = NumDirEntries;
            int entryIdx = 0;
            for (int ci = 0; ci < NumSubChunks; ci++)
            {
                chunkDataStarts[ci] = currentDataPos;
                int filesThisChunk = (int)Math.Min(FilesPerChunk, numFiles);
                for (int j = 0; j < filesThisChunk; j++)
                {
                    entryAbsoluteOffsets[entryIdx] = currentDataPos;
                    currentDataPos += (ulong)payloads[entryIdx].Length;
                    // Add original padding bytes (preserve padding for non-replaced entries, 0 for replaced)
                    if (entryIdx != replacedIndex)
                    {
                        var raw = GetRawEntry(entryIdx);
                        currentDataPos += raw.Padding;
                    }
                    entryIdx++;
                }
                numFiles -= filesThisChunk;
            }
            ulong lastEntryAbsOffset = currentDataPos; // used for last-entry RelativeDataOffset

            // Write header (32 bytes)
            bw.Write(MAGIC);
            bw.Write(Version);
            bw.Write(folderIndicesOffset); // same offset
            bw.Write(NumDirEntries);
            bw.Write(NumFolders);
            bw.Write(FilesPerChunk);
            bw.Write(NumSubChunks);
            bw.Write((uint)0); // unk preserved as 0

            // Write folder indices block
            fs.Write(folderIndicesBlock, 0, folderIndicesBlock.Length);

            // Write chunk records
            numFiles = NumDirEntries;
            entryIdx = 0;
            for (int ci = 0; ci < NumSubChunks; ci++)
            {
                bw.Write(chunkDataStarts[ci]); // DataStartOffset

                int filesThisChunk = (int)Math.Min(FilesPerChunk, numFiles);
                for (int j = 0; j < filesThisChunk; j++)
                {
                    ulong relOffset = entryAbsoluteOffsets[entryIdx] - chunkDataStarts[ci];
                    bw.Write((uint)relOffset);        // RelativeDataOffset
                    bw.Write(uncompSizes[entryIdx]);   // UncompressedSize

                    ushort flags;
                    byte padding = entryIdx == replacedIndex ? (byte)0 : GetRawEntry(entryIdx).Padding;
                    if (Version >= 101)
                        flags = (ushort)((methods[entryIdx] & 0xFFF) | ((padding & 0xF) << 12));
                    else
                        flags = (ushort)((methods[entryIdx] & 0x3FFF) | ((padding & 0x3) << 14));
                    bw.Write(flags);
                    bw.Write(parentDirs[entryIdx]);

                    entryIdx++;
                }
                numFiles -= filesThisChunk;
            }

            // Write LastEntry
            // LastEntry RelativeDataOffset is the total data size (points just past the last chunk's data)
            ulong lastChunkBase = chunkDataStarts[NumSubChunks - 1];
            bw.Write((uint)(lastEntryAbsOffset - lastChunkBase)); // RelativeDataOffset
            bw.Write(_lastEntry.UncompressedSize);
            ushort lastFlags;
            if (Version >= 101) lastFlags = (ushort)((_lastEntry.CompressMethod & 0xFFF) | ((_lastEntry.Padding & 0xF) << 12));
            else lastFlags = (ushort)((_lastEntry.CompressMethod & 0x3FFF) | ((_lastEntry.Padding & 0x3) << 14));
            bw.Write(lastFlags);
            bw.Write(_lastEntry.ParentDirIndex);

            // Write payload data
            for (int i = 0; i < NumDirEntries; i++)
            {
                fs.Write(payloads[i], 0, payloads[i].Length);
                // Write original padding bytes (preserve alignment) for non-replaced entries
                if (i != replacedIndex)
                {
                    var raw = GetRawEntry(i);
                    if (raw.Padding > 0)
                    {
                        byte[] pad = new byte[raw.Padding];
                        fs.Write(pad, 0, pad.Length);
                    }
                }
            }
        }

        // Helpers

        private MiniZipRawEntry GetRawEntry(int index)
        {
            if (index < 0 || index >= NumDirEntries)
                throw new IndexOutOfRangeException("File index is out of range");
            int chunkIndex = (int)(index / FilesPerChunk);
            int indexInChunk = (int)(index % FilesPerChunk);
            return _subChunks[chunkIndex].Entries[indexInChunk];
        }

        private Stream GetDecompressor(Stream baseStream, int method)
        {
            if (method == 8)
            {
                if (Version == 101)
                    return new SharpCompress.Compressors.Deflate.DeflateStream(baseStream, SharpCompress.Compressors.CompressionMode.Decompress);
                else
                    return new InflaterInputStream(baseStream);
            }
            else if (method == 22)
                throw new NotSupportedException("Method 22 (deflate + tfit) for minizip is not supported");
            else
                return baseStream;
        }

        public static string GetExtension(MiniZipResourceType contentType)
        {
            return contentType switch
            {
                MiniZipResourceType.Procedural => "pgeo",
                MiniZipResourceType.PhysicsTemplate => "phys",
                MiniZipResourceType.AIOpenWorldBlock => "owb",
                MiniZipResourceType.ModelBin => "modelbin",
                MiniZipResourceType.Texture => "pb",
                MiniZipResourceType.ModelGr2 => "gr2",
                MiniZipResourceType.TriggerZone => "tz",
                MiniZipResourceType.VoxelGIPack => "zip",
                MiniZipResourceType.AudioSoundscape => "soundscape",
                MiniZipResourceType.AudioSoundBank => "bank",
                MiniZipResourceType.LightBlock => "lightblock",
                MiniZipResourceType.PVSZone => "pvsz",
                MiniZipResourceType.WaterDepth => "dds",
                MiniZipResourceType.HavokNavMesh => "hkx",
                MiniZipResourceType.MegaTextureChunk => "mtxmoddxt",
                MiniZipResourceType.MegaTextureSource => "dxt",
                MiniZipResourceType.ProceduralMap => "mtp",
                MiniZipResourceType.IndirectLight => "gipack",
                MiniZipResourceType.MegaTextureSourceHQ => "hqdxt",
                MiniZipResourceType.VolumetricFog => "tdft",
                MiniZipResourceType.VolumetricFogTexture => "fvt",
                MiniZipResourceType.Unk28 => "predeform",
                _ => $"unk{(int)contentType}",
            };
        }

        // Returns true if the file at <paramref name="path"/> is a Playground MiniZip (PGZP magic).
        public static bool IsMiniZipFile(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs);
                if (fs.Length < 4) return false;
                return br.ReadUInt32() == MAGIC;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _baseStream?.Dispose();
        }

        // Internal types

        private class MiniZipSubChunk
        {
            public int Index { get; set; }
            public ulong DataStartOffset { get; set; }
            public List<MiniZipRawEntry> Entries { get; set; } = new();
        }

        private class MiniZipRawEntry
        {
            public uint RelativeDataOffset { get; set; }
            public ulong DataOffset { get; set; }
            public uint CompressedSize { get; set; }
            public uint UncompressedSize { get; set; }
            public ushort CompressMethod { get; set; }
            public byte Padding { get; set; }
            public ushort ParentDirIndex { get; set; }
            public int Index { get; set; }
            public int ChunkFileIndex { get; set; }
            public MiniZipSubChunk ParentChunk { get; set; }
        }
    }

    // Helper: stream wrapper that does not close the underlying stream
    internal class NonClosingStreamWrapper : Stream
    {
        private readonly Stream _inner;
        public NonClosingStreamWrapper(Stream inner) => _inner = inner;
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { /* do not close inner */ }
    }
}
