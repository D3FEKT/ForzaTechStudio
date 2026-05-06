using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace ForzaTechStudio.Services
{
    public class GsfParserService
    {
        // GStateCharacterInfoTag = GStateCIStandardTag + 52
        // GStateCIStandardTag = 0x80657473
        // Result = 0x80657473 + 52 = 0x806574A7
        public const uint GStateCharacterInfoTag = 0x806574A7;
        public const uint GStateCharacterInfoTag_FH5 = 0x806574F5;

        // Magic values (Little Endian 32-bit)
        private static readonly byte[] MagicValue32LE = new byte[] 
        { 
            0x42, 0x4E, 0x54, 0x29, // 0x29544E42 (Reverse due to LE)
            0x79, 0x65, 0x0A, 0x15, // 0x150A6579
            0xE5, 0x11, 0x21, 0xC3, // 0xC32111E5
            0x64, 0x32, 0x23, 0x0C  // 0x0C233264
        };

        public enum GrannyMemberType : uint
        {
            EndMember = 0,
            InlineMember = 1,
            ReferenceMember = 2,
            ReferenceToArrayMember = 3,
            ArrayOfReferencesMember = 4,
            VariantReferenceMember = 5,
            UnsupportedMemberType_6 = 6,
            SwitchableTypeMember = 7,
            StringMember = 8,
            TransformMember = 9,
            Real32Member = 10,
            Int8Member = 11,
            UInt8Member = 12,
            BinormalInt8Member = 13,
            NormalUInt8Member = 14,
            Int16Member = 15,
            UInt16Member = 16,
            BinormalInt16Member = 17,
            NormalUInt16Member = 18,
            Int32Member = 19,
            UInt32Member = 20,
            Real16Member = 21,
            EmptyReferenceMember = 22,
            BooleanMember = 23
        }

        public class GrannyMember
        {
            public GrannyMemberType Type;
            public string Name;
            public List<GrannyMember> MemberType; // Recursion
            public int ArrayWidth;
            public int Extra1;
            public int Extra2;
            public uint Extra3; // Ptr in file
        }

        public class GsfHeaderInfo
        {
            public bool IsValidGranny { get; set; }
            public bool IsGsf { get; set; }
            public uint TypeTag { get; set; }
            public uint Version { get; set; }
            public uint TotalSize { get; set; }
            public uint CRC { get; set; }
            public string StatusMessage { get; set; }
            public List<GrannySection> Sections { get; set; } = new List<GrannySection>();
            
            public uint RootObjectTypeDefinitionOffset { get; set; }
            public uint RootObjectOffset { get; set; }
            
            // Add this:
            public uint RootObjectSectionIndex { get; set; }
            public uint RootTypeDefSectionIndex { get; set; }

            public int SectionArrayCount { get; set; }
            public uint SectionArrayOffset { get; set; }
            
            public int PointerSize { get; set; } = 4; // 4 or 8, detected from magic/typedef stride

            // Loaded Data
            public List<byte[]> SectionData { get; set; } = new List<byte[]>();
            public Dictionary<int, Dictionary<uint, Relocation>> Relocations { get; set; } = new Dictionary<int, Dictionary<uint, Relocation>>();
            public GrannyMember RootType { get; set; }
        }

        public class GrannySection
        {
            public uint Format { get; set; }
            public uint Offset { get; set; }
            public uint CompressedSize { get; set; }
            public uint UncompressedSize { get; set; }
            public uint Alignment { get; set; }
            public uint First16Bit { get; set; }
            public uint First8Bit { get; set; }
            public uint RelocationsOffset { get; set; } // Ptr to Relocation[]
            public uint RelocationsCount { get; set; }
            public uint MarshallingsOffset { get; set; } // Ptr to Marshalling[]
            public uint MarshallingsCount { get; set; }
        }

        public struct Relocation
        {
            public uint Offset; // Offset in the section
            public uint TargetSectionIndex;
            public uint TargetOffset; // Offset in the target section
        }

        public async Task<GsfHeaderInfo> ParseHeaderAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                var result = new GsfHeaderInfo();

                try
                {
                    byte[] raw = File.ReadAllBytes(filePath);
                    if (raw.Length < 32)
                    {
                        result.StatusMessage = "File too short to be a Granny file.";
                        return result;
                    }

                    // ?? Magic block (32 bytes at file offset 0) ??
                    byte[] magic = new byte[16];
                    Array.Copy(raw, 0, magic, 0, 16);

                    // Check known magic patterns
                    bool isKnownMagic = false;
                    int detectedPtrSize = 4;
                    byte[][] knownMagics32 = new byte[][] {
                        MagicValue32LE,
                        new byte[] { 0x29,0xDE,0x6C,0xC0, 0xBA,0xA4,0x53,0x2B, 0x25,0xF5,0xB7,0xA5, 0xF6,0x66,0xE2,0xEE }, // 32-bit LE (alt)
                        new byte[] { 0xB8,0x67,0xB0,0xCA, 0xF8,0x6D,0xB1,0x0F, 0x84,0x72,0x8C,0x7E, 0x5E,0x19,0x00,0x1E }, // Old
                    };
                    byte[][] knownMagics64 = new byte[][] {
                        new byte[] { 0xE5,0x9B,0x49,0x5E, 0x6F,0x63,0x1F,0x14, 0x1E,0x13,0xEB,0xA9, 0x90,0xBE,0xED,0xC4 }, // 64-bit LE
                    };
                    foreach (var pattern in knownMagics64)
                    {
                        bool match = true;
                        for (int i = 0; i < 16; i++) { if (magic[i] != pattern[i]) { match = false; break; } }
                        if (match) { isKnownMagic = true; detectedPtrSize = 8; break; }
                    }
                    if (!isKnownMagic)
                    {
                        foreach (var pattern in knownMagics32)
                        {
                            bool match = true;
                            for (int i = 0; i < 16; i++) { if (magic[i] != pattern[i]) { match = false; break; } }
                            if (match) { isKnownMagic = true; detectedPtrSize = 4; break; }
                        }
                    }

                    result.IsValidGranny = isKnownMagic;
                    if (!isKnownMagic)
                    {
                        result.StatusMessage = "Unknown Granny magic bytes � assuming 64-bit LE.";
                        detectedPtrSize = 8; // Forza files are typically 64-bit
                    }
                    result.PointerSize = detectedPtrSize;

                    uint headerSize = BitConverter.ToUInt32(raw, 16);
                    uint headerFormat = BitConverter.ToUInt32(raw, 20);

                    // ?? Read header body ??
                    // HeaderSize = 32 bytes (32-bit) or 64 bytes (64-bit) on disk
                    const int MagicSize = 32;
                    const int FullHeaderSize = 72;
                    int onDiskHeaderSize = (headerSize >= 8 && headerSize <= 0x1000)
                        ? (int)headerSize : FullHeaderSize;

                    int available = raw.Length - MagicSize;
                    if (available < 20) // need at least Version..SectionArrayCount
                    {
                        result.StatusMessage = "File too small for header body.";
                        return result;
                    }

                    byte[] hdr = new byte[FullHeaderSize];
                    int toCopy = Math.Min(Math.Min(onDiskHeaderSize, available), FullHeaderSize);
                    Array.Copy(raw, MagicSize, hdr, 0, toCopy);

                    // Parse header from normalised buffer
                    result.Version = BitConverter.ToUInt32(hdr, 0x00);
                    result.TotalSize = BitConverter.ToUInt32(hdr, 0x04);
                    result.CRC = BitConverter.ToUInt32(hdr, 0x08);
                    result.SectionArrayOffset = BitConverter.ToUInt32(hdr, 0x0C);
                    result.SectionArrayCount = (int)BitConverter.ToUInt32(hdr, 0x10);

                    uint rootTypeDefSection = BitConverter.ToUInt32(hdr, 0x14);
                    uint rootTypeDefOffset = BitConverter.ToUInt32(hdr, 0x18);
                    result.RootObjectTypeDefinitionOffset = rootTypeDefOffset;
                    result.RootTypeDefSectionIndex = rootTypeDefSection;

                    uint rootObjSection = BitConverter.ToUInt32(hdr, 0x1C);
                    uint rootObjOffset = BitConverter.ToUInt32(hdr, 0x20);
                    result.RootObjectSectionIndex = rootObjSection;
                    result.RootObjectOffset = rootObjOffset;

                    result.TypeTag = BitConverter.ToUInt32(hdr, 0x24);

                    if (result.TypeTag == GStateCharacterInfoTag || result.TypeTag == GStateCharacterInfoTag_FH5)
                    {
                        result.IsGsf = true;
                        result.StatusMessage = "Valid GSF (Granny State File).";
                    }
                    else
                    {
                        result.IsGsf = false;
                        result.StatusMessage = $"Valid Granny File, TypeTag=0x{result.TypeTag:X}.";
                    }

                    // ?? Section headers ??
                    // SectionArrayOffset is relative to header body start (file offset 0x20)
                    if (result.SectionArrayCount > 0 && result.SectionArrayCount <= 64)
                    {
                        long sectionPos = MagicSize + (long)result.SectionArrayOffset;
                        const int SectionHeaderSize = 44;
                        long sectionEnd = sectionPos + (long)result.SectionArrayCount * SectionHeaderSize;

                        if (sectionPos >= MagicSize && sectionEnd <= raw.Length)
                        {
                            int sp = (int)sectionPos;
                            for (int i = 0; i < result.SectionArrayCount; i++, sp += SectionHeaderSize)
                            {
                                var section = new GrannySection();
                                section.Format = BitConverter.ToUInt32(raw, sp + 0);
                                section.Offset = BitConverter.ToUInt32(raw, sp + 4);
                                section.CompressedSize = BitConverter.ToUInt32(raw, sp + 8);
                                section.UncompressedSize = BitConverter.ToUInt32(raw, sp + 12);
                                section.Alignment = BitConverter.ToUInt32(raw, sp + 16);
                                section.First16Bit = BitConverter.ToUInt32(raw, sp + 20);
                                section.First8Bit = BitConverter.ToUInt32(raw, sp + 24);
                                section.RelocationsOffset = BitConverter.ToUInt32(raw, sp + 28);
                                section.RelocationsCount = BitConverter.ToUInt32(raw, sp + 32);
                                section.MarshallingsOffset = BitConverter.ToUInt32(raw, sp + 36);
                                section.MarshallingsCount = BitConverter.ToUInt32(raw, sp + 40);
                                result.Sections.Add(section);
                            }
                        }
                        else
                        {
                            result.StatusMessage += $" [Section array at 0x{sectionPos:X} overruns file]";
                        }
                    }

                    // ?? Load + decompress section data ??
                    for (int i = 0; i < result.Sections.Count; i++)
                    {
                        var section = result.Sections[i];

                        // Bounds check
                        if (section.Offset >= (uint)raw.Length || section.CompressedSize == 0)
                        {
                            result.SectionData.Add(section.UncompressedSize > 0
                                ? new byte[section.UncompressedSize] : Array.Empty<byte>());
                            continue;
                        }

                        long avail = (long)raw.Length - (long)section.Offset;
                        int readSize = (int)Math.Min((long)section.CompressedSize, avail);
                        if (readSize <= 0)
                        {
                            result.SectionData.Add(Array.Empty<byte>());
                            continue;
                        }

                        byte[] data = new byte[readSize];
                        Array.Copy(raw, (int)section.Offset, data, 0, readSize);

                        if (section.CompressedSize != section.UncompressedSize && section.UncompressedSize > 0)
                        {
                            // Decompression
                            try
                            {
                                byte[] decompressed = null;
                                bool decompressedSuccess = false;
                                string error = "";

                                uint compressionId = section.Format & 0x3;
                                if (compressionId != 0 && Granny2Native.IsAvailable)
                                {
                                    try
                                    {
                                        decompressed = new byte[section.UncompressedSize];
                                        decompressedSuccess = Granny2Native.Decompress(
                                            (int)compressionId, false, data,
                                            (int)section.First16Bit, (int)section.First8Bit,
                                            (int)section.UncompressedSize, decompressed);
                                        if (!decompressedSuccess) decompressed = null;
                                    }
                                    catch (Exception ex) { error = "Granny: " + ex.Message; }
                                }

                                if (!decompressedSuccess)
                                {
                                    try
                                    {
                                        decompressed = OodleCompression.Decompress(data, (int)section.UncompressedSize);
                                        if (decompressed != null && decompressed.Length == section.UncompressedSize)
                                            decompressedSuccess = true;
                                    }
                                    catch (Exception ex) { error = ex.Message; }
                                }

                                if (!decompressedSuccess)
                                {
                                    try
                                    {
                                        decompressed = XCompression.Decompress(data, (int)section.UncompressedSize);
                                        decompressedSuccess = true;
                                    }
                                    catch (Exception ex2)
                                    {
                                        if (string.IsNullOrEmpty(error)) error = ex2.Message;
                                        else error += " | LZX: " + ex2.Message;
                                    }
                                }

                                if (decompressedSuccess && decompressed != null)
                                    data = decompressed;
                                else
                                    result.StatusMessage += $" [Sec {i} decomp failed: {error}]";
                            }
                            catch (Exception ex)
                            {
                                result.StatusMessage += $" [Decomp Error: {ex.Message}]";
                            }
                        }
                        result.SectionData.Add(data);
                    }

                    // ?? Load Relocations ??
                    for (int i = 0; i < result.Sections.Count; i++)
                    {
                        var section = result.Sections[i];
                        var dict = new Dictionary<uint, Relocation>();
                        if (section.RelocationsCount > 0 && section.RelocationsOffset > 0)
                        {
                            long fixEnd = (long)section.RelocationsOffset + (long)section.RelocationsCount * 12;
                            if (section.RelocationsOffset < (uint)raw.Length && fixEnd <= raw.Length)
                            {
                                int fp = (int)section.RelocationsOffset;
                                for (int k = 0; k < section.RelocationsCount; k++, fp += 12)
                                {
                                    uint offset = BitConverter.ToUInt32(raw, fp);
                                    dict[offset] = new Relocation
                                    {
                                        Offset = offset,
                                        TargetSectionIndex = BitConverter.ToUInt32(raw, fp + 4),
                                        TargetOffset = BitConverter.ToUInt32(raw, fp + 8),
                                    };
                                }
                            }
                        }
                        result.Relocations[i] = dict;
                    }

                    // Parse Type Definition
                    // First, refine pointer size from typedef stride if possible
                    result.PointerSize = DetectPointerSizeFromStride(result, (int)rootTypeDefSection);
                    int typeDefRecordSize = result.PointerSize == 8 ? 44 : 32;

                    if (rootTypeDefSection < result.SectionData.Count)
                    {
                        result.RootType = ReadRootGrannyMember(result, (int)rootTypeDefSection, (int)rootTypeDefOffset, typeDefRecordSize);
                    }
                }
                catch (Exception ex)
                {
                    result.StatusMessage = $"Error reading file: {ex.Message}";
                }

                return result;
            });
        }
        
        private GrannyMember ReadRootGrannyMember(GsfHeaderInfo info, int sectionIdx, int offset, int recordSize)
        {
             var root = new GrannyMember();
             root.Name = "Root";
             root.Type = GrannyMemberType.InlineMember;
             root.MemberType = ReadGrannyMemberArrayAt(info, sectionIdx, offset, recordSize, new HashSet<(int, int)>());
             return root;
        }

        private GrannyMember ReadGrannyMember(GsfHeaderInfo info, int sectionIdx, int offset, int recordSize, HashSet<(int, int)> visiting)
        {
            if (sectionIdx < 0 || sectionIdx >= info.SectionData.Count) return null;
            byte[] data = info.SectionData[sectionIdx];
            if (offset + recordSize > data.Length) return null; 

            var member = new GrannyMember();
            
            member.Type = (GrannyMemberType)BitConverter.ToUInt32(data, offset);
            if (member.Type == GrannyMemberType.EndMember) return member;

            // Dereference Name � always at offset +4
            member.Name = ReadString(info, sectionIdx, offset + 4); 

            // ArrayWidth offset depends on record size:
            // 32-byte records: Type(4) + Name*(4) + RefType*(4) + ArrayWidth at +12
            // 44-byte records: Type(4) + Name*(8) + RefType*(8) + ArrayWidth at +20
            int arrayWidthOffset = recordSize == 44 ? 20 : 12;
            member.ArrayWidth = BitConverter.ToInt32(data, offset + arrayWidthOffset);

            // ReferenceType pointer at +8 (32-byte) or +12 (44-byte)
            int refTypeOffset = recordSize == 44 ? 12 : 8;
            if (HasRelocationFor(info, sectionIdx, offset + refTypeOffset))
            {
                 member.MemberType = ReadGrannyMemberArrayFromPointer(info, sectionIdx, offset + refTypeOffset, recordSize, visiting);
            }
            
            return member;
        }

        private bool HasRelocationFor(GsfHeaderInfo info, int sectionIdx, int offset)
        {
             if (!info.Relocations.TryGetValue(sectionIdx, out var dict)) return false;
             return dict.ContainsKey((uint)offset);
        }

        private List<GrannyMember> ReadGrannyMemberArrayFromPointer(GsfHeaderInfo info, int baseSectionIdx, int ptrOffsetInSection, int recordSize, HashSet<(int, int)> visiting)
        {
             var target = ResolvePointer(info, baseSectionIdx, ptrOffsetInSection);
             if (target.SectionIndex == -1) return new List<GrannyMember>();

             return ReadGrannyMemberArrayAt(info, target.SectionIndex, target.Offset, recordSize, visiting);
        }

        private List<GrannyMember> ReadGrannyMemberArrayAt(GsfHeaderInfo info, int sectionIdx, int offset, int recordSize, HashSet<(int, int)> visiting)
        {
             if (!visiting.Add((sectionIdx, offset))) return new List<GrannyMember>(); // Prevent infinite recursion

             var list = new List<GrannyMember>();
             int currentOffset = offset;
             
             int safety = 0;
             while(safety++ < 1000)
             {
                 var m = ReadGrannyMember(info, sectionIdx, currentOffset, recordSize, visiting);
                 if (m == null || m.Type == GrannyMemberType.EndMember) break;
                 list.Add(m);
                 currentOffset += recordSize;
             }
             
             visiting.Remove((sectionIdx, offset));
             return list;
        }

        private (int SectionIndex, int Offset) ResolvePointer(GsfHeaderInfo info, int sectionIdx, int offsetInSection)
        {
             if (info.Relocations.TryGetValue(sectionIdx, out var dict))
             {
                 if (dict.TryGetValue((uint)offsetInSection, out var r))
                 {
                     return ((int)r.TargetSectionIndex, (int)r.TargetOffset);
                 }
             }
             return (-1, 0);
        }

        private string ReadString(GsfHeaderInfo info, int sectionIdx, int ptrOffset)
        {
            var target = ResolvePointer(info, sectionIdx, ptrOffset);
            if (target.SectionIndex == -1 || target.SectionIndex >= info.SectionData.Count) return "<null>";
            
            byte[] data = info.SectionData[target.SectionIndex];
            int start = target.Offset;
            if (start >= data.Length) return "<invalid>";
            
            int end = start;
            while(end < data.Length && data[end] != 0) end++;
            
            return Encoding.ASCII.GetString(data, start, end - start);
        }

        private int DetectPointerSizeFromStride(GsfHeaderInfo info, int typeDefSectionIdx)
        {
            if (!info.Relocations.TryGetValue(typeDefSectionIdx, out var relocs) || relocs.Count == 0)
                return info.PointerSize; // keep magic-detected value

            if (typeDefSectionIdx >= info.SectionData.Count || info.SectionData[typeDefSectionIdx].Length == 0)
                return info.PointerSize;

            // Collect offsets of fixups that resolve to printable name strings
            var nameOffsets = new List<int>();
            foreach (var r in relocs.Values)
            {
                int srcOff = (int)r.Offset;
                int dstSecIdx = (int)r.TargetSectionIndex;
                int dstOff = (int)r.TargetOffset;
                if (dstSecIdx >= info.SectionData.Count) continue;
                byte[] dstData = info.SectionData[dstSecIdx];
                if (dstOff >= dstData.Length) continue;

                int end = dstOff;
                int maxEnd = Math.Min(dstOff + 128, dstData.Length);
                while (end < maxEnd && dstData[end] != 0) end++;
                if (end == dstOff || end - dstOff < 3) continue;

                bool isPrintable = true;
                for (int i = dstOff; i < end; i++)
                {
                    byte b = dstData[i];
                    if (!((b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') ||
                          (b >= '0' && b <= '9') || b == '_' || b == '.'))
                    { isPrintable = false; break; }
                }
                if (!isPrintable) continue;

                nameOffsets.Add(srcOff);
                if (nameOffsets.Count >= 4) break;
            }

            nameOffsets.Sort();
            if (nameOffsets.Count >= 2)
            {
                int stride = nameOffsets[1] - nameOffsets[0];
                if (stride == 44) return 8;
                if (stride == 32) return 4;

                if (nameOffsets.Count >= 3)
                {
                    int stride2 = nameOffsets[2] - nameOffsets[1];
                    if (stride2 == 44) return 8;
                    if (stride2 == 32) return 4;
                }
            }

            return info.PointerSize; // keep magic-detected value
        }
        
        public TreeViewNode BuildTree(GsfHeaderInfo info)
        {
             if (info.RootType == null) return new TreeViewNode() { Content = "Invalid Parsing State (RootType is null)" };
             
             string rootName = "File Root";
             if (info.IsGsf) rootName = "CharacterRoot";

             var root = new TreeViewNode() { Content = $"{rootName} [GStateCharacterInfo]" };
             
             // Traverse Data + Schema
            int bytesRead = TraverseObject(root, info, info.RootType, (int)info.RootObjectSectionIndex, (int)info.RootObjectOffset, new HashSet<(int, int)>());
            
            if (root.Children.Count == 0)
            {
                root.Children.Add(new TreeViewNode() { Content = "(No children found - Check parsing logic/offsets)" });
                // Debug info
                var debug = new TreeViewNode() { Content = $"Debug: RootType={info.RootType.Name} Members={info.RootType.MemberType?.Count ?? 0}" };
                root.Children.Add(debug);
            }
             
             return root;
        }

        // Returns bytes consumed
        private int TraverseObject(TreeViewNode parentNode, GsfHeaderInfo info, GrannyMember type, int sectionIdx, int offset, HashSet<(int, int)> visiting)
        {
            if (type.Type == GrannyMemberType.InlineMember)
            {
                if (type.MemberType != null)
                {
                    int totalSize = 0;
                    foreach(var member in type.MemberType)
                    {
                        // Add node for member
                        var memberNode = new TreeViewNode();
                        
                        try 
                        {
                            int size = TraverseMember(memberNode, info, member, sectionIdx, offset + totalSize, visiting);
                            totalSize += size;
							
                            // Only add if relevant or has content? No, add everything to show structure.
                            parentNode.Children.Add(memberNode);
                        }
                        catch (Exception ex)
                        {
                            memberNode.Content = $"Error reading {member.Name}: {ex.Message}";
                            parentNode.Children.Add(memberNode);
                        }
                    }
                    return totalSize;
                }
                return 0;
            }
            return 0;
        }

        private int TraverseMember(TreeViewNode node, GsfHeaderInfo info, GrannyMember member, int sectionIdx, int offset, HashSet<(int, int)> visiting)
        {
            // Set default content
            node.Content = $"{member.Name}";

            // Check bounds
            if (sectionIdx < 0 || sectionIdx >= info.SectionData.Count)
            {
                node.Content += " (Invalid Section)";
                return 0;
            }
            byte[] data = info.SectionData[sectionIdx];
            if (offset >= data.Length)
            {
                node.Content += " (Offset Out of Bounds)";
                return 0;
            }
            
            int PTR = info.PointerSize;
            int ARRAY_HEADER = 4 + PTR; // count(int32) + pointer(PTR)
            
            // Handle Inline Array (Fixed width in definition)
            int count = member.ArrayWidth == 0 ? 1 : member.ArrayWidth;
            
            // Special handling for dynamic array types which have Count stored in the data, not fixed in definition
            bool isDynamicArray = (member.Type == GrannyMemberType.ReferenceToArrayMember || member.Type == GrannyMemberType.ArrayOfReferencesMember);

            int totalBytesConsumed = 0;
            int currentOffset = offset;
            
            // Loop for FIXED inline arrays
            int iterationCount = isDynamicArray ? 1 : count; 

            for(int i=0; i < iterationCount; i++)
            {
                TreeViewNode targetNode = node;
                if (iterationCount > 1)
                {
                    targetNode = new TreeViewNode() { Content = $"[{i}]" };
                    node.Children.Add(targetNode);
                }
                
                int size = 0;
                
                switch(member.Type)
                {
                    case GrannyMemberType.InlineMember:
                        size = TraverseObject(targetNode, info, member, sectionIdx, currentOffset, visiting);
                        if (targetNode != node && targetNode.Children.Count == 0) targetNode.Content += " (Empty Struct)";
                        break;
                        
                    case GrannyMemberType.ReferenceMember:
                    {
                        if (currentOffset + PTR > data.Length) { size=0; break; }
                        
                        var target = ResolvePointer(info, sectionIdx, currentOffset);
                        if (target.SectionIndex != -1)
                        {
                            if (member.MemberType != null && member.MemberType.Count > 0)
                            {
                                if (visiting.Add((target.SectionIndex, target.Offset)))
                                {
                                    var dummy = new GrannyMember { Type = GrannyMemberType.InlineMember, MemberType = member.MemberType };
                                    TraverseObject(targetNode, info, dummy, target.SectionIndex, target.Offset, visiting);
                                    visiting.Remove((target.SectionIndex, target.Offset));
                                }
                                else
                                {
                                    targetNode.Content += " (Circular Ref)";
                                }
                            }
                            else
                            {
                                targetNode.Content += " (Ref - No Type Info)";
                            }
                        }
                        else
                        {
                            targetNode.Content += " (null)";
                        }
                        size = PTR;
                        break;
                    }

                    case GrannyMemberType.ReferenceToArrayMember:
                    case GrannyMemberType.ArrayOfReferencesMember:
                    {
                        if (currentOffset + ARRAY_HEADER > data.Length) { size=0; break; }
                        
                        int arrayCount = BitConverter.ToInt32(data, currentOffset);
                        int ptrOffset = currentOffset + 4;
                        
                        var target = ResolvePointer(info, sectionIdx, ptrOffset);
                        
                        if (arrayCount == 0)
                        {
                            targetNode.Content += " [Empty]";
                        }
                        else
                        {
                            targetNode.Content += $" [{arrayCount}]";
                        }

                        if (target.SectionIndex != -1 && arrayCount > 0)
                        {
                            int elementOffset = target.Offset;
                            int displayCount = Math.Min(arrayCount, 200); 

                            for(int k=0; k < displayCount; k++)
                            {
                                var childNode = new TreeViewNode() { Content = $"[{k}]" };
                                targetNode.Children.Add(childNode);
                                
                                int elementSize = 0;
                                
                                if (member.Type == GrannyMemberType.ReferenceToArrayMember)
                                {
                                    var dummy = new GrannyMember { Type = GrannyMemberType.InlineMember, MemberType = member.MemberType };
                                    elementSize = TraverseObject(childNode, info, dummy, target.SectionIndex, elementOffset, visiting);
                                }
                                else // ArrayOfReferencesMember
                                {
                                    var elemTarget = ResolvePointer(info, target.SectionIndex, elementOffset);
                                    if (elemTarget.SectionIndex != -1)
                                    {
                                        if (visiting.Add((elemTarget.SectionIndex, elemTarget.Offset)))
                                        {
                                            var dummy = new GrannyMember { Type = GrannyMemberType.InlineMember, MemberType = member.MemberType };
                                            TraverseObject(childNode, info, dummy, elemTarget.SectionIndex, elemTarget.Offset, visiting);
                                            visiting.Remove((elemTarget.SectionIndex, elemTarget.Offset));
                                        }
                                        else
                                        {
                                            childNode.Content += " (Circular Ref)";
                                        }
                                    }
                                    else
                                    {
                                        childNode.Content += " (null)";
                                    }
                                    elementSize = PTR; // Pointer size
                                }
                                
                                if (elementSize == 0) 
                                {
                                    childNode.Content += " (Size: 0)";
                                    break; 
                                }
                                elementOffset += elementSize;
                            }
                            
                            if (arrayCount > displayCount)
                            {
                                targetNode.Children.Add(new TreeViewNode() { Content = $"... {arrayCount - displayCount} more items ..." });
                            }
                        }

                        size = ARRAY_HEADER;
                        break;
                    }
                    
                    case GrannyMemberType.StringMember:
                    {
                        string str = ReadString(info, sectionIdx, currentOffset);
                        targetNode.Content += $" : \"{str}\"";
                        size = PTR;
                        break;
                    }
                    
                    case GrannyMemberType.TransformMember:
                        if (currentOffset + 68 <= data.Length)
                            targetNode.Content += " (Transform 68 bytes)";
                        size = 68;
                        break;

                    case GrannyMemberType.Real32Member:
                        if (currentOffset + 4 <= data.Length)
                            targetNode.Content += $" : {BitConverter.ToSingle(data, currentOffset)}";
                        size = 4;
                        break;
                        
                    case GrannyMemberType.Int8Member:
                    case GrannyMemberType.BinormalInt8Member:
                        if (currentOffset + 1 <= data.Length)
                            targetNode.Content += $" : {(sbyte)data[currentOffset]}";
                        size = 1;
                        break;
                        
                    case GrannyMemberType.UInt8Member:
                    case GrannyMemberType.NormalUInt8Member:
                        if (currentOffset + 1 <= data.Length)
                            targetNode.Content += $" : {data[currentOffset]}";
                        size = 1;
                        break;

                    case GrannyMemberType.Int16Member:
                    case GrannyMemberType.BinormalInt16Member:
                        if (currentOffset + 2 <= data.Length)
                            targetNode.Content += $" : {BitConverter.ToInt16(data, currentOffset)}";
                        size = 2;
                        break;

                    case GrannyMemberType.UInt16Member:
                    case GrannyMemberType.NormalUInt16Member:
                        if (currentOffset + 2 <= data.Length)
                            targetNode.Content += $" : {BitConverter.ToUInt16(data, currentOffset)}";
                        size = 2;
                        break;

                    case GrannyMemberType.Int32Member:
                        if (currentOffset + 4 <= data.Length)
                            targetNode.Content += $" : {BitConverter.ToInt32(data, currentOffset)}";
                        size = 4;
                        break;

                    case GrannyMemberType.UInt32Member:
                        if (currentOffset + 4 <= data.Length)
                            targetNode.Content += $" : {BitConverter.ToUInt32(data, currentOffset)}";
                        size = 4;
                        break;

                    case GrannyMemberType.Real16Member:
                        size = 2;
                        break;

                    case GrannyMemberType.BooleanMember:
                        if (currentOffset + 4 <= data.Length)
                            targetNode.Content += $" : {(BitConverter.ToUInt32(data, currentOffset) != 0)}";
                        size = 4;
                        break;

                    case GrannyMemberType.EmptyReferenceMember:
                        size = PTR;
                        break;
                    
                    case GrannyMemberType.VariantReferenceMember:
                        size = 2 * PTR; // Type pointer + data pointer
                        break;

                }
                
                if (size == 0) 
                {
                    targetNode.Content += " (Error)";
                    break; 
                }
                currentOffset += size;
                totalBytesConsumed += size;
            }
            
            return totalBytesConsumed;
        }
    }
}
