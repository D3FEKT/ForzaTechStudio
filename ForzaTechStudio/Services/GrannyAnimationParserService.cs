using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForzaTechStudio.Services
{
    // ??? Data models ??????????????????????????????????????????????????

    public class GrannyAnimFileInfo
    {
        public string FilePath { get; set; }
        public byte[] RawData { get; set; }
        public int FileSize { get; set; }

        // Header
        public uint Version { get; set; }
        public uint TotalSize { get; set; }
        public uint CRC { get; set; }
        public uint TypeTag { get; set; }
        public int RootObjectSection { get; set; }
        public int RootObjectOffset { get; set; }
        public int RootTypeSection { get; set; }
        public int RootTypeOffset { get; set; }

        // Sections
        public List<GrannyAnimSection> Sections { get; set; } = new();
        public List<byte[]> SectionData { get; set; } = new();
        public Dictionary<int, Dictionary<int, (int DstSection, int DstOffset)>> FixupMaps { get; set; } = new();

        // Detected pointer size (4 or 8)
        public int PointerSize { get; set; } = 4;

        public bool IsGsf => TypeTag == GrannyParserService.GSF_TAG_FM7 ||
                              TypeTag == GrannyParserService.GSF_TAG_FH5 ||
                              TypeTag == GrannyParserService.GSF_TAG_DOC;
    }

    public class GrannyAnimSection
    {
        public int Index { get; set; }
        public uint Format { get; set; }
        public uint DataOffset { get; set; }
        public uint DataSize { get; set; }
        public uint ExpandedDataSize { get; set; }
        public uint Alignment { get; set; }
        public uint First16Bit { get; set; }
        public uint First8Bit { get; set; }
        public uint PtrFixupOffset { get; set; }
        public uint PtrFixupCount { get; set; }
        public uint MmFixupOffset { get; set; }
        public uint MmFixupCount { get; set; }
    }

    public class ParsedAnimation
    {
        public int SectionIndex { get; set; }
        public int Offset { get; set; }
        public int AbsoluteOffset { get; set; }
        public string Name { get; set; }
        public float Duration { get; set; }
        public float TimeStep { get; set; }
        public float Oversampling { get; set; }
        public int TrackGroupCount { get; set; }
        public List<ParsedTrackGroup> TrackGroups { get; set; } = new();
    }

    public class ParsedTrackGroup
    {
        public int Index { get; set; }
        public int SectionIndex { get; set; }
        public int Offset { get; set; }
        public int AbsoluteOffset { get; set; }
        public string Name { get; set; }
        public int VectorTrackCount { get; set; }
        public int TransformTrackCount { get; set; }
        public List<ParsedTransformTrack> TransformTracks { get; set; } = new();
    }

    public class ParsedTransformTrack
    {
        public int Index { get; set; }
        public int SectionIndex { get; set; }
        public int Offset { get; set; }
        public int AbsoluteOffset { get; set; }
        public string Name { get; set; }
        public int Flags { get; set; }
        public int StructSize { get; set; }
        public ParsedCurve2 Orientation { get; set; }
        public ParsedCurve2 Position { get; set; }
        public ParsedCurve2 ScaleShear { get; set; }
    }

    public class ParsedCurve2
    {
        public string Label { get; set; }
        public int Offset { get; set; }
        public int AbsoluteOffset { get; set; }
        public string FormatName { get; set; } = "Unknown";
        public List<float> DataFloats { get; set; } = new();
        public string DataHex { get; set; } = "";
        public int DataAbsoluteOffset { get; set; }
        public bool HasData => DataFloats.Count > 0;
    }

    public class QueuedAnimEdit
    {
        public string FieldLabel { get; set; }
        public int AbsoluteOffset { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string PackType { get; set; } // "float32", "int32", "uint32"
        public string Context { get; set; }
    }

    // ??? Parser service ???????????????????????????????????????????????

    public class GrannyAnimationParserService
    {
        public async Task<(GrannyAnimFileInfo FileInfo, List<ParsedAnimation> Animations, List<string> Errors)>
            ParseAsync(string filePath)
        {
            return await Task.Run(() => Parse(filePath));
        }

        public (GrannyAnimFileInfo FileInfo, List<ParsedAnimation> Animations, List<string> Errors) Parse(string filePath)
        {
            var errors = new List<string>();
            var animations = new List<ParsedAnimation>();

            var fi = new GrannyAnimFileInfo { FilePath = filePath };

            try
            {
                fi.RawData = File.ReadAllBytes(filePath);
                fi.FileSize = fi.RawData.Length;

                if (fi.FileSize < 104) // 32 magic + 72 header
                    throw new InvalidDataException($"File too small ({fi.FileSize} bytes).");

                ReadHeader(fi);
                ReadSections(fi);
                LoadSectionData(fi);
                LoadFixups(fi);
                DetectPointerSize(fi);

                // Parse animations from root object
                ParseAnimationsFromRoot(fi, animations, errors);
            }
            catch (Exception ex)
            {
                errors.Add($"Parse error: {ex.Message}");
            }

            return (fi, animations, errors);
        }

        // ??? Save with edits ??????????????????????????????????????????

        public byte[] ApplyEditsAndSave(GrannyAnimFileInfo fi, List<QueuedAnimEdit> edits)
        {
            var edited = new byte[fi.RawData.Length];
            Array.Copy(fi.RawData, edited, fi.RawData.Length);

            foreach (var edit in edits)
            {
                byte[] patch = PackValue(edit.PackType, edit.NewValue);
                int off = edit.AbsoluteOffset;
                if (off < 0 || off + patch.Length > edited.Length)
                    throw new InvalidOperationException($"Offset 0x{off:X} out of bounds for {edit.FieldLabel}");
                Array.Copy(patch, 0, edited, off, patch.Length);
            }

            // Recalculate CRC
            RecalculateCrc(edited);

            return edited;
        }

        public static byte[] PackValue(string packType, string text)
        {
            text = text.Trim();
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("Value is empty");

            return packType switch
            {
                "float32" => BitConverter.GetBytes(float.Parse(text)),
                "int32" => BitConverter.GetBytes(int.Parse(text)),
                "uint32" => BitConverter.GetBytes(uint.Parse(text)),
                _ => throw new ArgumentException($"Unknown pack type: {packType}")
            };
        }

        // ??? Header reading ??????????????????????????????????????????

        private static void ReadHeader(GrannyAnimFileInfo fi)
        {
            var raw = fi.RawData;

            // Normalised header at offset 0x20, read up to 72 bytes
            const int MagicSize = 32;
            const int FullHeaderSize = 72;

            uint headerSize = BitConverter.ToUInt32(raw, 16);
            int onDisk = (headerSize >= 8 && headerSize <= 0x1000)
                ? (int)headerSize : FullHeaderSize;

            byte[] hdr = new byte[FullHeaderSize];
            int toCopy = Math.Min(Math.Min(onDisk, raw.Length - MagicSize), FullHeaderSize);
            Array.Copy(raw, MagicSize, hdr, 0, toCopy);

            fi.Version = BitConverter.ToUInt32(hdr, 0x00);
            fi.TotalSize = BitConverter.ToUInt32(hdr, 0x04);
            fi.CRC = BitConverter.ToUInt32(hdr, 0x08);
            uint sectionArrayOffset = BitConverter.ToUInt32(hdr, 0x0C);
            uint sectionArrayCount = BitConverter.ToUInt32(hdr, 0x10);
            fi.RootTypeSection = (int)BitConverter.ToUInt32(hdr, 0x14);
            fi.RootTypeOffset = (int)BitConverter.ToUInt32(hdr, 0x18);
            fi.RootObjectSection = (int)BitConverter.ToUInt32(hdr, 0x1C);
            fi.RootObjectOffset = (int)BitConverter.ToUInt32(hdr, 0x20);
            fi.TypeTag = BitConverter.ToUInt32(hdr, 0x24);
            
            // Detect pointer size from magic bytes
            byte[] magic = new byte[16];
            Array.Copy(raw, 0, magic, 0, 16);
            byte[] magic64LE = { 0xE5,0x9B,0x49,0x5E, 0x6F,0x63,0x1F,0x14, 0x1E,0x13,0xEB,0xA9, 0x90,0xBE,0xED,0xC4 };
            bool is64 = true;
            for (int i = 0; i < 16; i++) { if (magic[i] != magic64LE[i]) { is64 = false; break; } }
            fi.PointerSize = is64 ? 8 : 4;
        }

        private static void ReadSections(GrannyAnimFileInfo fi)
        {
            var raw = fi.RawData;
            const int MagicSize = 32;
            const int SectionHeaderSize = 44;

            byte[] hdr = new byte[72];
            int toCopy = Math.Min(72, raw.Length - MagicSize);
            Array.Copy(raw, MagicSize, hdr, 0, toCopy);

            uint sectionArrayOffset = BitConverter.ToUInt32(hdr, 0x0C);
            uint sectionArrayCount = BitConverter.ToUInt32(hdr, 0x10);

            if (sectionArrayCount == 0 || sectionArrayCount > 64)
                return;

            long sectionPos = MagicSize + (long)sectionArrayOffset;
            long sectionEnd = sectionPos + (long)sectionArrayCount * SectionHeaderSize;

            if (sectionPos < MagicSize || sectionEnd > raw.Length)
                return;

            int sp = (int)sectionPos;
            for (int i = 0; i < (int)sectionArrayCount; i++, sp += SectionHeaderSize)
            {
                fi.Sections.Add(new GrannyAnimSection
                {
                    Index = i,
                    Format = BitConverter.ToUInt32(raw, sp + 0),
                    DataOffset = BitConverter.ToUInt32(raw, sp + 4),
                    DataSize = BitConverter.ToUInt32(raw, sp + 8),
                    ExpandedDataSize = BitConverter.ToUInt32(raw, sp + 12),
                    Alignment = BitConverter.ToUInt32(raw, sp + 16),
                    First16Bit = BitConverter.ToUInt32(raw, sp + 20),
                    First8Bit = BitConverter.ToUInt32(raw, sp + 24),
                    PtrFixupOffset = BitConverter.ToUInt32(raw, sp + 28),
                    PtrFixupCount = BitConverter.ToUInt32(raw, sp + 32),
                    MmFixupOffset = BitConverter.ToUInt32(raw, sp + 36),
                    MmFixupCount = BitConverter.ToUInt32(raw, sp + 40),
                });
            }
        }

        private static void LoadSectionData(GrannyAnimFileInfo fi)
        {
            var raw = fi.RawData;
            foreach (var sec in fi.Sections)
            {
                if (sec.DataSize == 0 || sec.DataOffset >= (uint)raw.Length)
                {
                    fi.SectionData.Add(sec.ExpandedDataSize > 0
                        ? new byte[sec.ExpandedDataSize] : Array.Empty<byte>());
                    continue;
                }

                long avail = (long)raw.Length - (long)sec.DataOffset;
                int readSize = (int)Math.Min((long)sec.DataSize, avail);
                if (readSize <= 0)
                {
                    fi.SectionData.Add(Array.Empty<byte>());
                    continue;
                }

                byte[] data = new byte[readSize];
                Array.Copy(raw, (int)sec.DataOffset, data, 0, readSize);

                uint comprFmt = sec.Format & 0x3u;
                if (comprFmt != 0 && sec.DataSize != sec.ExpandedDataSize && sec.ExpandedDataSize > 0)
                {
                    byte[] decompressed = null;

                    // Try native granny2 with correct stop values from section header
                    if (Granny2Native.IsAvailable)
                    {
                        try
                        {
                            decompressed = new byte[sec.ExpandedDataSize];
                            if (!Granny2Native.Decompress((int)comprFmt, false, data,
                                    (int)sec.First16Bit, (int)sec.First8Bit,
                                    (int)sec.ExpandedDataSize, decompressed))
                                decompressed = null;
                        }
                        catch { decompressed = null; }
                    }

                    // Try Oodle
                    if (decompressed == null)
                    {
                        try
                        {
                            decompressed = OodleCompression.Decompress(data, (int)sec.ExpandedDataSize);
                            if (decompressed?.Length != (int)sec.ExpandedDataSize)
                                decompressed = null;
                        }
                        catch { decompressed = null; }
                    }

                    if (decompressed != null)
                        data = decompressed;
                }

                fi.SectionData.Add(data);
            }
        }

        private static void LoadFixups(GrannyAnimFileInfo fi)
        {
            var raw = fi.RawData;
            foreach (var sec in fi.Sections)
            {
                var fmap = new Dictionary<int, (int, int)>();

                if (sec.PtrFixupCount > 0 && sec.PtrFixupOffset > 0)
                {
                    long fixEnd = (long)sec.PtrFixupOffset + (long)sec.PtrFixupCount * 12;
                    if (sec.PtrFixupOffset < (uint)raw.Length && fixEnd <= raw.Length)
                    {
                        int fp = (int)sec.PtrFixupOffset;
                        for (uint k = 0; k < sec.PtrFixupCount; k++, fp += 12)
                        {
                            int srcOff = (int)BitConverter.ToUInt32(raw, fp);
                            int dstSec = (int)BitConverter.ToUInt32(raw, fp + 4);
                            int dstOff = (int)BitConverter.ToUInt32(raw, fp + 8);
                            fmap[srcOff] = (dstSec, dstOff);
                        }
                    }
                }

                fi.FixupMaps[sec.Index] = fmap;
            }
        }

        // ??? Pointer size detection (ported from Python) ?????????????

        private static void DetectPointerSize(GrannyAnimFileInfo fi)
        {
            int rtSec = fi.RootTypeSection;
            if (rtSec < 0 || rtSec >= fi.SectionData.Count || fi.SectionData[rtSec].Length == 0)
            {
                return; // Keep magic-detected value
            }

            if (!fi.FixupMaps.TryGetValue(rtSec, out var fmap) || fmap.Count == 0)
            {
                return; // Keep magic-detected value
            }

            // Collect offsets of fixups resolving to printable name strings
            var nameOffsets = new List<int>();
            foreach (var off in fmap.Keys.OrderBy(k => k))
            {
                var (dstSec, dstOff) = fmap[off];
                var dstData = GetSectionData(fi, dstSec);
                if (dstOff >= dstData.Length) continue;

                int end = Array.IndexOf(dstData, (byte)0, dstOff, Math.Min(128, dstData.Length - dstOff));
                if (end < 0 || end - dstOff < 3) continue;

                bool isPrintable = true;
                for (int i = dstOff; i < end; i++)
                {
                    byte b = dstData[i];
                    if (!((b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') ||
                          (b >= '0' && b <= '9') || b == '_' || b == '.'))
                    { isPrintable = false; break; }
                }
                if (!isPrintable) continue;

                nameOffsets.Add(off);
                if (nameOffsets.Count >= 4) break;
            }

            if (nameOffsets.Count >= 2)
            {
                int stride = nameOffsets[1] - nameOffsets[0];
                if (stride == 44) { fi.PointerSize = 8; return; }
                if (stride == 32) { fi.PointerSize = 4; return; }

                if (nameOffsets.Count >= 3)
                {
                    int stride2 = nameOffsets[2] - nameOffsets[1];
                    if (stride2 == 44) { fi.PointerSize = 8; return; }
                    if (stride2 == 32) { fi.PointerSize = 4; return; }
                }
            }

            // Keep magic-detected value if detection fails
        }

        // ??? Helpers ??????????????????????????????????????????????????

        private static byte[] GetSectionData(GrannyAnimFileInfo fi, int secIdx)
        {
            if (secIdx < 0 || secIdx >= fi.SectionData.Count) return Array.Empty<byte>();
            return fi.SectionData[secIdx];
        }

        private static (int Section, int Offset)? ResolvePtr(GrannyAnimFileInfo fi, int secIdx, int offset)
        {
            if (fi.FixupMaps.TryGetValue(secIdx, out var fmap) && fmap.TryGetValue(offset, out var target))
                return target;
            return null;
        }

        private static string ReadStringViaFixup(GrannyAnimFileInfo fi, int secIdx, int ptrOffset)
        {
            var target = ResolvePtr(fi, secIdx, ptrOffset);
            if (target == null) return "<null>";
            var (tSec, tOff) = target.Value;
            var d = GetSectionData(fi, tSec);
            if (tOff >= d.Length) return "<invalid>";
            int end = tOff;
            while (end < d.Length && d[end] != 0) end++;
            if (end == tOff) return string.Empty;
            return Encoding.UTF8.GetString(d, tOff, end - tOff);
        }

        private static int AbsOffset(GrannyAnimFileInfo fi, int secIdx, int offset)
        {
            if (secIdx < 0 || secIdx >= fi.Sections.Count) return offset;
            return (int)fi.Sections[secIdx].DataOffset + offset;
        }

        private static int ReadI32(byte[] d, int off) =>
            (off >= 0 && off + 4 <= d.Length) ? BitConverter.ToInt32(d, off) : 0;

        private static float ReadF32(byte[] d, int off) =>
            (off >= 0 && off + 4 <= d.Length) ? BitConverter.ToSingle(d, off) : 0f;

        // ??? Animation parsing (ported from Python AnimationParser) ??

        private static void ParseAnimationsFromRoot(GrannyAnimFileInfo fi,
            List<ParsedAnimation> animations, List<string> errors)
        {
            if (fi.IsGsf)
            {
                errors.Add("GSF files do not contain granny_file_info root objects. Skipping animation parse.");
                return;
            }

            int PTR = fi.PointerSize;
            int rootSec = fi.RootObjectSection;
            int rootOff = fi.RootObjectOffset;
            var secData = GetSectionData(fi, rootSec);

            if (secData.Length == 0) { errors.Add("Root object section is empty"); return; }

            // granny_file_info: 3 header pointers then 9 counted arrays (int32 + ptr each) for Textures through Animations.
            int ARRAY_SLOT = 4 + PTR;
            int baseOff = 3 * PTR;

            int IDX_ANIMATIONS = 8;
            int offAnimCount = rootOff + baseOff + IDX_ANIMATIONS * ARRAY_SLOT;
            int offAnimPtr = offAnimCount + 4;

            if (offAnimPtr + PTR > secData.Length) { errors.Add("granny_file_info truncated"); return; }

            int animCount = ReadI32(secData, offAnimCount);
            if (animCount <= 0 || animCount > 100000) { errors.Add($"Invalid AnimationCount={animCount}"); return; }

            // Follow Animations** pointer
            var animArr = ResolvePtr(fi, rootSec, offAnimPtr);
            if (animArr == null) { errors.Add("Animations** pointer has no fixup"); return; }

            var (arrSec, arrOff) = animArr.Value;

            for (int ai = 0; ai < animCount; ai++)
            {
                int ptrOff = arrOff + ai * PTR;
                var animTarget = ResolvePtr(fi, arrSec, ptrOff);
                if (animTarget == null) continue;
                var (aSec, aOff) = animTarget.Value;

                var anim = ParseAnimation(fi, aSec, aOff, errors);
                if (anim != null)
                    animations.Add(anim);
            }
        }

        private static ParsedAnimation ParseAnimation(GrannyAnimFileInfo fi, int secIdx, int offset, List<string> errors)
        {
            int PTR = fi.PointerSize;
            var secData = GetSectionData(fi, secIdx);
            int minSize = 2 * PTR + 16;
            if (offset + minSize > secData.Length) return null;

            var anim = new ParsedAnimation
            {
                SectionIndex = secIdx,
                Offset = offset,
                AbsoluteOffset = AbsOffset(fi, secIdx, offset),
                Name = ReadStringViaFixup(fi, secIdx, offset),
                Duration = ReadF32(secData, offset + PTR),
                TimeStep = ReadF32(secData, offset + PTR + 4),
                Oversampling = ReadF32(secData, offset + PTR + 8),
                TrackGroupCount = ReadI32(secData, offset + PTR + 12),
            };

            int tgPtrOff = offset + PTR + 16;
            var tgArr = ResolvePtr(fi, secIdx, tgPtrOff);
            if (tgArr != null && anim.TrackGroupCount > 0)
            {
                var (tgaSec, tgaOff) = tgArr.Value;
                for (int tgi = 0; tgi < anim.TrackGroupCount; tgi++)
                {
                    int ptrOff = tgaOff + tgi * PTR;
                    var tgTarget = ResolvePtr(fi, tgaSec, ptrOff);
                    if (tgTarget == null) continue;
                    var (tgs, tgo) = tgTarget.Value;
                    var tg = ParseTrackGroup(fi, tgs, tgo, tgi);
                    if (tg != null) anim.TrackGroups.Add(tg);
                }
            }

            return anim;
        }

        private static ParsedTrackGroup ParseTrackGroup(GrannyAnimFileInfo fi, int secIdx, int offset, int index)
        {
            int PTR = fi.PointerSize;
            var secData = GetSectionData(fi, secIdx);
            int minSize = 3 * PTR + 8;
            if (offset + minSize > secData.Length) return null;

            var tg = new ParsedTrackGroup
            {
                Index = index,
                SectionIndex = secIdx,
                Offset = offset,
                AbsoluteOffset = AbsOffset(fi, secIdx, offset),
                Name = ReadStringViaFixup(fi, secIdx, offset),
                VectorTrackCount = ReadI32(secData, offset + PTR),
                TransformTrackCount = ReadI32(secData, offset + 2 * PTR + 4),
            };

            int ttPtrOff = offset + 2 * PTR + 8;
            var ttPtr = ResolvePtr(fi, secIdx, ttPtrOff);
            if (ttPtr != null && tg.TransformTrackCount > 0)
            {
                var (ttSec, ttBase) = ttPtr.Value;
                int TT_SIZE = 7 * PTR + 4;
                for (int ttI = 0; ttI < tg.TransformTrackCount; ttI++)
                {
                    int ttOff = ttBase + ttI * TT_SIZE;
                    var tt = ParseTransformTrack(fi, ttSec, ttOff, ttI);
                    if (tt != null) tg.TransformTracks.Add(tt);
                }
            }

            return tg;
        }

        private static ParsedTransformTrack ParseTransformTrack(GrannyAnimFileInfo fi, int secIdx, int offset, int index)
        {
            int PTR = fi.PointerSize;
            int CURVE2 = 2 * PTR;
            int TT_SIZE = 7 * PTR + 4;
            var secData = GetSectionData(fi, secIdx);

            if (offset + TT_SIZE > secData.Length) return null;

            int orientOff = offset + PTR + 4;
            int posOff = orientOff + CURVE2;
            int ssOff = posOff + CURVE2;

            return new ParsedTransformTrack
            {
                Index = index,
                SectionIndex = secIdx,
                Offset = offset,
                AbsoluteOffset = AbsOffset(fi, secIdx, offset),
                Name = ReadStringViaFixup(fi, secIdx, offset),
                Flags = ReadI32(secData, offset + PTR),
                StructSize = TT_SIZE,
                Orientation = ParseCurve2(fi, secIdx, orientOff, "Orientation"),
                Position = ParseCurve2(fi, secIdx, posOff, "Position"),
                ScaleShear = ParseCurve2(fi, secIdx, ssOff, "ScaleShear"),
            };
        }

        private static ParsedCurve2 ParseCurve2(GrannyAnimFileInfo fi, int secIdx, int offset, string label)
        {
            int PTR = fi.PointerSize;
            int CURVE2 = 2 * PTR;
            var secData = GetSectionData(fi, secIdx);

            var curve = new ParsedCurve2
            {
                Label = label,
                Offset = offset,
                AbsoluteOffset = AbsOffset(fi, secIdx, offset),
            };

            if (offset + CURVE2 > secData.Length) return curve;

            var typeTarget = ResolvePtr(fi, secIdx, offset);
            var objTarget = ResolvePtr(fi, secIdx, offset + PTR);

            // Identify curve format from type definition
            if (typeTarget != null)
            {
                var (tSec, tOff) = typeTarget.Value;
                curve.FormatName = IdentifyCurveFormat(fi, tSec, tOff);
            }

            // Read curve data
            if (objTarget != null)
            {
                var (oSec, oOff) = objTarget.Value;
                var objData = GetSectionData(fi, oSec);
                int chunkSize = Math.Min(64, objData.Length - oOff);
                if (chunkSize > 0)
                {
                    var raw = new byte[chunkSize];
                    Array.Copy(objData, oOff, raw, 0, chunkSize);
                    curve.DataHex = Convert.ToHexString(raw).ToLowerInvariant();
                    curve.DataAbsoluteOffset = AbsOffset(fi, oSec, oOff);

                    int nFloats = chunkSize / 4;
                    for (int i = 0; i < nFloats; i++)
                        curve.DataFloats.Add(ReadF32(objData, oOff + i * 4));
                }
            }

            // Identity curves: no fixups, raw values zero
            if (typeTarget == null && objTarget == null)
            {
                bool allZero = true;
                for (int b = 0; b < CURVE2 && b + offset < secData.Length; b++)
                    if (secData[offset + b] != 0) { allZero = false; break; }
                if (allZero)
                    curve.FormatName = "DaIdentity (no data)";
            }

            return curve;
        }

        private static string IdentifyCurveFormat(GrannyAnimFileInfo fi, int secIdx, int offset)
        {
            int PTR = fi.PointerSize;
            if (!fi.FixupMaps.TryGetValue(secIdx, out var fmap))
                return "Unknown";

            int searchRange = (PTR == 8 ? 44 : 32) * 2;
            foreach (var foff in fmap.Keys.OrderBy(k => k))
            {
                if (foff < offset) continue;
                if (foff >= offset + searchRange) break;

                var (dstSec, dstOff) = fmap[foff];
                var dstData = GetSectionData(fi, dstSec);
                if (dstOff >= dstData.Length) continue;

                int end = dstOff;
                while (end < dstData.Length && end < dstOff + 64 && dstData[end] != 0) end++;
                if (end == dstOff) continue;

                string s;
                try { s = Encoding.ASCII.GetString(dstData, dstOff, end - dstOff); }
                catch { continue; }

                if (string.IsNullOrEmpty(s) || s.Contains("Padding", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (s.StartsWith("CurveDataHeader_") || s.StartsWith("Da") || s.StartsWith("D3") || s.StartsWith("D4"))
                    return s;
            }

            // Fallback: first field name
            var nameTarget = ResolvePtr(fi, secIdx, offset + 4);
            if (nameTarget != null)
            {
                var (ns, no) = nameTarget.Value;
                var nd = GetSectionData(fi, ns);
                if (no < nd.Length)
                {
                    int end = no;
                    while (end < nd.Length && end < no + 64 && nd[end] != 0) end++;
                    if (end > no)
                    {
                        string name = Encoding.ASCII.GetString(nd, no, end - no);
                        if (!string.IsNullOrEmpty(name))
                            return $"TypeDef:{name}";
                    }
                }
            }

            return "Unknown";
        }

        // CRC recalculation

        // Standard CRC32 with polynomial 0xEDB88320.
        // Matches granny_crc.cpp (BeginCRC32/AddToCRC32/EndCRC32).
        private static uint GrannyCrc32(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
                crc = (GrannyCrc32Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8));
            return crc ^ 0xFFFFFFFF;
        }

        private static uint GrannyCrc32Continue(uint crc, byte[] data, int offset, int length)
        {
            // Continue from a previous CRC without the final XOR
            crc ^= 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
                crc = (GrannyCrc32Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8));
            return crc ^ 0xFFFFFFFF;
        }

        private static readonly uint[] GrannyCrc32Table = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++)
                    c = (c & 1) != 0 ? (0xEDB88320 ^ (c >> 1)) : (c >> 1);
                table[i] = c;
            }
            return table;
        }

        // Recalculate the Granny file CRC.
        // CRC covers: section array bytes + file bytes from HeaderSize to EOF.
        // Matches transcode_gr2_type4_to_uncompressed.py's implementation.
        private static void RecalculateCrc(byte[] raw)
        {
            // Update TotalSize at offset 0x20+0x04
            byte[] sizeBytes = BitConverter.GetBytes(raw.Length);
            Array.Copy(sizeBytes, 0, raw, 0x20 + 0x04, 4);

            // Zero CRC field before recalc
            raw[0x20 + 0x08] = 0;
            raw[0x20 + 0x09] = 0;
            raw[0x20 + 0x0A] = 0;
            raw[0x20 + 0x0B] = 0;

            // HeaderSize is at file offset 0x10 (in the magic block)
            uint headerSize = BitConverter.ToUInt32(raw, 0x10);
            uint sectionArrayOffset = BitConverter.ToUInt32(raw, 0x20 + 0x0C);
            uint sectionCount = BitConverter.ToUInt32(raw, 0x20 + 0x10);
            int sectionArrayAbs = 0x20 + (int)sectionArrayOffset;
            int sectionArraySize = (int)sectionCount * 44;

            // CRC = CRC32(section_array_bytes) continued with CRC32(file_bytes[headerSize..EOF])
            uint crc = GrannyCrc32(raw, sectionArrayAbs, sectionArraySize);
            if ((int)headerSize < raw.Length)
                crc = GrannyCrc32Continue(crc, raw, (int)headerSize, raw.Length - (int)headerSize);

            // Write CRC back
            byte[] crcBytes = BitConverter.GetBytes(crc);
            Array.Copy(crcBytes, 0, raw, 0x20 + 0x08, 4);
        }
    }
}
