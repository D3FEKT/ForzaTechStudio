using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ForzaTechStudio.Services
{
    //  Parsed data structures (public API), code borrowed 

    public class GrannyFileData
    {
        public bool IsValid { get; set; }
        public bool IsGsf { get; set; }
        public uint TypeTag { get; set; }
        public uint Version { get; set; }
        public string SourceFileName { get; set; }
        public string StatusMessage { get; set; }

        public List<GrannySkeleton> Skeletons { get; set; } = new();
        public List<GrannyAnimation> Animations { get; set; } = new();
        public List<GrannyTrackGroup> TrackGroups { get; set; } = new();
        public List<GrannyModel> Models { get; set; } = new();
        public GsfCharacterInfo CharacterInfo { get; set; }

        internal GrannyContainer Container { get; set; }
    }

    // granny::model — references a skeleton and an array of mesh bindings.
    public class GrannyModel
    {
        public string Name { get; set; }
        // Name of the skeleton this model drives (matched by name in Forza).
        public string SkeletonName { get; set; }
        public int MeshBindingCount { get; set; }
    }

    public class GrannySkeleton
    {
        public string Name { get; set; }
        public int LODType { get; set; }
        public List<GrannyBone> Bones { get; set; } = new();
    }

    public class GrannyBone
    {
        public string Name { get; set; }
        public int ParentIndex { get; set; } = -1;
        public GrannyTransform LocalTransform { get; set; } = new();
        public Matrix4x4 InverseWorld4x4 { get; set; } = Matrix4x4.Identity;
        public float LODError { get; set; }
        public Matrix4x4 WorldTransform { get; set; } = Matrix4x4.Identity;
    }

    public class GrannyTransform
    {
        public const int SizeBytes = 68;

        public uint Flags { get; set; }
        public Vector3 Position { get; set; }
        public Quaternion Orientation { get; set; } = Quaternion.Identity;
        public Vector3 ScaleShear0 { get; set; } = Vector3.UnitX;
        public Vector3 ScaleShear1 { get; set; } = Vector3.UnitY;
        public Vector3 ScaleShear2 { get; set; } = Vector3.UnitZ;

        public Matrix4x4 ToMatrix()
        {
            var scale = Matrix4x4.Identity;
            if ((Flags & 0x4) != 0)
            {
                scale.M11 = ScaleShear0.X; scale.M12 = ScaleShear0.Y; scale.M13 = ScaleShear0.Z;
                scale.M21 = ScaleShear1.X; scale.M22 = ScaleShear1.Y; scale.M23 = ScaleShear1.Z;
                scale.M31 = ScaleShear2.X; scale.M32 = ScaleShear2.Y; scale.M33 = ScaleShear2.Z;
            }
            var rotation = (Flags & 0x2) != 0
                ? Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(Orientation))
                : Matrix4x4.Identity;
            var translation = (Flags & 0x1) != 0
                ? Matrix4x4.CreateTranslation(Position)
                : Matrix4x4.Identity;
            return scale * rotation * translation;
        }
    }

    public class GrannyAnimation
    {
        public string Name { get; set; }
        public float Duration { get; set; }
        public float TimeStep { get; set; }
        public float Oversampling { get; set; }
        public int DefaultLoopCount { get; set; }
        public int Flags { get; set; }
        public List<GrannyTrackGroup> TrackGroups { get; set; } = new();
    }

    public class GrannyTrackGroup
    {
        public string Name { get; set; }
        public int Flags { get; set; }
        public Vector3 LoopTranslation { get; set; }
        public GrannyTransform InitialPlacement { get; set; } = new();
        public List<GrannyTransformTrack> TransformTracks { get; set; } = new();
    }

    public class GrannyTransformTrack
    {
        public string Name { get; set; }
        public int Flags { get; set; }
        public List<TransformKeyframe> Keyframes { get; set; } = new();

        // Raw curve info for debugging/advanced use
        public GrannyCurveInfo PositionCurve { get; set; }
        public GrannyCurveInfo OrientationCurve { get; set; }
        public GrannyCurveInfo ScaleShearCurve { get; set; }

        public bool HasKeyframes => Keyframes.Count > 0;

        // True if any of the three curves contains non-identity animation data.
        public bool HasAnimationData =>
            (PositionCurve != null && !PositionCurve.IsIdentity) ||
            (OrientationCurve != null && !OrientationCurve.IsIdentity) ||
            (ScaleShearCurve != null && !ScaleShearCurve.IsIdentity);

        // Total number of knots across all non-identity curves (for display purposes).
        public int KnotCount
        {
            get
            {
                int count = 0;
                if (PositionCurve != null && !PositionCurve.IsIdentity)
                    count = Math.Max(count, PositionCurve.Knots?.Length ?? 0);
                if (OrientationCurve != null && !OrientationCurve.IsIdentity)
                    count = Math.Max(count, OrientationCurve.Knots?.Length ?? 0);
                if (ScaleShearCurve != null && !ScaleShearCurve.IsIdentity)
                    count = Math.Max(count, ScaleShearCurve.Knots?.Length ?? 0);
                return count;
            }
        }
    }

    // Stores parsed Granny curve data from a VariantReference (Curve2).
    // Supports DaIdentity, DaConstant32f, DaK32fC32f, DaKeyframes32f, and similar formats.
    public class GrannyCurveInfo
    {
        public string FormatName { get; set; } = "Unknown";
        public bool IsIdentity { get; set; }
        public bool IsConstant { get; set; }
        public float[] Knots { get; set; } = Array.Empty<float>();
        public float[] Controls { get; set; } = Array.Empty<float>();
        public int Dimension { get; set; } // 3 for position, 4 for orientation, 9 for scale/shear
        // True when Controls is non-null and non-empty — guards against failed parses.
        public bool HasData => Controls != null && Controls.Length > 0;

        // B-spline degree from the curve header (0=constant, 1=linear, 2=quadratic, 3=cubic).
        public byte Degree { get; set; }
        // Packed nibble table for D4nK16uC15u/D4nK8uC7u quaternion scale/offset lookup. 4 nibbles, one per component.
        public ushort ScaleOffsetTableEntries { get; set; }
        // Per-dimension control scales for compressed curves (DaK16uC16u, D3K16uC16u, etc.).
        public float[] ControlScales { get; set; }
        // Per-dimension control offsets for compressed curves.
        public float[] ControlOffsets { get; set; }
    }

    public class TransformKeyframe
    {
        public float Time { get; set; }
        public Vector3 Position { get; set; }
        public Quaternion Orientation { get; set; } = Quaternion.Identity;
        public Vector3 Scale { get; set; } = Vector3.One;
        // Full row-major 3×3 scale/shear matrix (9 floats) when the ScaleShear curve has dimension 9. Null for diagonal-only scale.
        public float[] ScaleShear9 { get; set; }
    }

    public class GsfCharacterInfo
    {
        public int NumUniqueTokenized { get; set; }
        public int AnimationSlotCount { get; set; }
        public List<GsfAnimationSlot> AnimationSlots { get; set; } = new();
        public int AnimationSetCount { get; set; }
        public List<GsfAnimationSet> AnimationSets { get; set; } = new();
        public string ModelNameHint { get; set; }
        public int ModelIndexHint { get; set; }
    }

    public class GsfAnimationSlot
    {
        public string Name { get; set; }
        public int Index { get; set; }
    }

    public class GsfAnimationSet
    {
        public string Name { get; set; }
        public List<GsfSourceFileRef> SourceFileReferences { get; set; } = new();
        public List<GsfAnimationSpec> AnimationSpecs { get; set; } = new();
    }

    public class GsfSourceFileRef
    {
        public string SourceFilename { get; set; }
        public int ExpectedAnimCount { get; set; }
        public uint AnimCRC { get; set; }
    }

    public class GsfAnimationSpec
    {
        public int AnimationIndex { get; set; }
        public string ExpectedName { get; set; }
    }

    //  Raw container – byte-accurate round-trip support

    internal class GrannyContainer
    {
        public bool IsBigEndian;
        public int PointerSize = 4; // 4 or 8, detected from magic bytes
        public byte[] RawFileBytes;

        // Magic (32 bytes)
        public byte[] MagicBytes = new byte[16];
        public uint HeaderSize;
        public uint HeaderFormat;
        public uint[] MagicReserved = new uint[2];

        // File header (normalised to 72 bytes in RAM)
        public uint Version;
        public uint TotalSize;
        public uint CRC;
        public uint SectionArrayOffset;
        public uint SectionArrayCount;
        public uint RootTypeSectionIndex;
        public uint RootTypeOffset;
        public uint RootObjSectionIndex;
        public uint RootObjOffset;
        public uint TypeTag;
        public uint[] ExtraTags = new uint[4];
        public uint StringDatabaseCRC;
        public uint[] ReservedUnused = new uint[3];

        // Sections (44 bytes each)
        public List<GrannySectionRaw> Sections = new();
        public List<byte[]> SectionData = new();
        public Dictionary<int, Dictionary<uint, GrannyFixup>> Fixups = new();
        public Dictionary<int, List<GrannyMarshalling>> Marshallings = new();
    }

    internal class GrannySectionRaw
    {
        public uint Format;
        public uint DataOffset;
        public uint DataSize;
        public uint ExpandedDataSize;
        public uint InternalAlignment;
        public uint First16Bit;
        public uint First8Bit;
        public uint PointerFixupOffset;
        public uint PointerFixupCount;
        public uint MixedMarshallingOffset;
        public uint MixedMarshallingCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct GrannyFixup
    {
        public uint FromOffset;
        public uint ToSectionIndex;
        public uint ToOffset;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct GrannyMarshalling
    {
        public uint Count;
        public uint DataOffset;
        public uint TypeSectionIndex;
        public uint TypeOffset;
    }

    internal static class GrannyCompressionFormat
    {
        public const uint None    = 0;
        public const uint Oodle0  = 1;
        public const uint Oodle1  = 2;
        public const uint BitKnit = 3;
    }

    //  P/Invoke for granny2_x64.dll
    internal static class Granny2Native
    {
        private const string DllName = "granny2_x64.dll";
        private static bool _available = true;
        private static bool _checkedOnce = false;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GrannyDecompressData")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool _GrannyDecompressData(
            int format,
            [MarshalAs(UnmanagedType.Bool)] bool fileIsByteReversed,
            int compressedBytesSize,
            [In] byte[] compressedBytes,
            int stop0, int stop1, int stop2,
            [Out] byte[] decompressedBytes);

        public static bool IsAvailable
        {
            get
            {
                if (!_checkedOnce)
                {
                    _checkedOnce = true;
                    string dllPath = Path.Combine(AppContext.BaseDirectory, DllName);
                    if (!File.Exists(dllPath))
                        _available = false;
                }
                return _available;
            }
        }

        public static bool Decompress(
            int format, bool byteReversed,
            byte[] compressed,
            int stop0, int stop1, int stop2,
            byte[] output)
        {
            if (!IsAvailable) return false;
            try
            {
                return _GrannyDecompressData(format, byteReversed,
                    compressed.Length, compressed,
                    stop0, stop1, stop2, output);
            }
            catch (DllNotFoundException)        { _available = false; return false; }
            catch (EntryPointNotFoundException)  { _available = false; return false; }
            catch (BadImageFormatException)      { _available = false; return false; }
        }
    }

    //  Granny type-system
    internal enum GrannyMemberType : uint
    {
        End              = 0,
        Inline           = 1,
        Reference        = 2,
        ReferenceToArray = 3,
        ArrayOfReferences= 4,
        VariantReference = 5,
        Removed          = 6,
        Switchable       = 7,
        String           = 8,
        Transform        = 9,
        Real32           = 10,
        Int8             = 11,
        UInt8            = 12,
        BinormalInt8      = 13,
        NormalUInt8       = 14,
        Int16            = 15,
        UInt16           = 16,
        BinormalInt16     = 17,
        NormalUInt16      = 18,
        Int32            = 19,
        UInt32           = 20,
        Real16           = 21,
        EmptyReference   = 22,
        Bool             = 23,
    }

    internal struct GrannyTypeDef
    {
        public GrannyMemberType Type;
        public string Name;
        public List<GrannyTypeDef> Children;
        public int ArrayWidth;
    }

    //  Main parser — complete rewrite with full bounds safety
    public class GrannyParserService
    {
        public static void InitializeNativeLibrary()
        {
            // Accessing this property triggers the File.Exists check inside the internal Granny2Native class
            _ = Granny2Native.IsAvailable;
        }

        public const uint GSF_TAG_FM7 = 0x806574A7;
        public const uint GSF_TAG_FH5 = 0x806574F5;
        public const uint GSF_TAG_DOC = 0x806574E2;

        // Magic byte patterns for supported GR2 formats.
        private static readonly byte[] Magic_32LE =
            { 0x29,0xDE,0x6C,0xC0, 0xBA,0xA4,0x53,0x2B, 0x25,0xF5,0xB7,0xA5, 0xF6,0x66,0xE2,0xEE };
        private static readonly byte[] Magic_64LE =
            { 0xE5,0x9B,0x49,0x5E, 0x6F,0x63,0x1F,0x14, 0x1E,0x13,0xEB,0xA9, 0x90,0xBE,0xED,0xC4 };
        private static readonly byte[] Magic_32BE =
            { 0x0E,0x11,0x95,0xB5, 0x6A,0xA5,0xB5,0x4B, 0xEB,0x28,0x28,0x50, 0x25,0x78,0xB3,0x04 };
        private static readonly byte[] Magic_64BE =
            { 0x31,0x95,0xD4,0xE3, 0x20,0xDC,0x4F,0x62, 0xCC,0x36,0xD0,0x3A, 0xB1,0x82,0xFF,0x89 };
        // Old magic (v6.x Granny2, used by Forza Horizon Xbox One/Durango).
        // Pointer size is in HeaderFormat bit 1: set = 32-bit, clear = 64-bit.
        private static readonly byte[] Magic_Old =
            { 0xB8,0x67,0xB0,0xCA, 0xF8,0x6D,0xB1,0x0F, 0x84,0x72,0x8C,0x7E, 0x5E,0x19,0x00,0x1E };
        // Legacy / v5 magic (GRNFileMV_Old @ 0x141f59338)
        private static readonly byte[] Magic_v5_Old =
            { 0x29,0xDE,0x6C,0xC0, 0xBA,0xA4,0x53,0x2B, 0x25,0xF5,0xB7,0xA5, 0xF6,0x66,0xE2,0xEE };

        // Instance pointer size, set during Parse() from container detection
        private int _ptrSize = 4;

        // Public API

        public async Task<GrannyFileData> ParseAsync(string filePath) =>
            await Task.Run(() => Parse(filePath));

        public GrannyFileData Parse(string filePath)
        {
            byte[] raw;
            try { raw = File.ReadAllBytes(filePath); }
            catch (Exception ex)
            {
                return new GrannyFileData { IsValid = false, StatusMessage = $"Read error: {ex.Message}" };
            }
            return Parse(raw, filePath);
        }

        public GrannyFileData Parse(byte[] raw, string virtualPath = null)
        {
            var result = new GrannyFileData();
            try
            {
                if (raw == null || raw.Length < 32)
                    throw new InvalidDataException($"File too small ({raw?.Length ?? 0} bytes).");

                result.Container = ReadContainer(raw);
                var c = result.Container;
                _ptrSize = c.PointerSize;

                result.Version = c.Version;
                result.TypeTag = c.TypeTag;
                result.IsValid = true;
                result.IsGsf = c.TypeTag == GSF_TAG_FM7 || c.TypeTag == GSF_TAG_FH5 || c.TypeTag == GSF_TAG_DOC;

                var rootType = ReadTypeDefArray(c, (int)c.RootTypeSectionIndex, (int)c.RootTypeOffset);
                if (rootType.Count > 0)
                    ParseRootObject(result, c, rootType, (int)c.RootObjSectionIndex, (int)c.RootObjOffset);

                foreach (var skel in result.Skeletons)
                    ComputeWorldTransforms(skel);

                string modelInfo = result.Models.Count > 0 ? $", {result.Models.Count} model(s)" : "";
                result.StatusMessage = result.IsGsf
                    ? $"Valid GSF (tag=0x{c.TypeTag:X8}). {result.Skeletons.Count} skeleton(s), {result.Animations.Count} animation(s){modelInfo}."
                    : $"Valid GR2 (tag=0x{c.TypeTag:X8}). {result.Skeletons.Count} skeleton(s), {result.Animations.Count} animation(s){modelInfo}.";
            }
            catch (Exception ex)
            {
                result.StatusMessage = $"Parse error: {ex.Message}";
                result.IsValid = false;
            }
            return result;
        }

        public void Write(GrannyFileData data, string outputPath)
        {
            if (data?.Container?.RawFileBytes == null)
                throw new InvalidOperationException("No raw bytes available for write.");
            File.WriteAllBytes(outputPath, data.Container.RawFileBytes);
        }

        public bool VerifyRoundTrip(string filePath, out string errorMsg)
        {
            errorMsg = null;
            try
            {
                byte[] original = File.ReadAllBytes(filePath);
                var parsed = Parse(filePath);
                if (!parsed.IsValid) { errorMsg = parsed.StatusMessage; return false; }
                string tmp = Path.GetTempFileName();
                try
                {
                    Write(parsed, tmp);
                    byte[] written = File.ReadAllBytes(tmp);
                    if (original.Length != written.Length)
                    { errorMsg = $"Size mismatch: orig={original.Length}, written={written.Length}"; return false; }
                    for (int i = 0; i < original.Length; i++)
                        if (original[i] != written[i])
                        { errorMsg = $"Byte diff at 0x{i:X}: 0x{original[i]:X2} vs 0x{written[i]:X2}"; return false; }
                    return true;
                }
                finally { try { File.Delete(tmp); } catch { } }
            }
            catch (Exception ex) { errorMsg = ex.Message; return false; }
        }

        //  Safe buffer helpers — all reads are bounds-checked

        private static bool CanRead(byte[] data, int offset, int size)
        {
            if (data == null || offset < 0 || size < 0) return false;
            return offset <= data.Length - size;
        }

        private static uint RU32(byte[] d, int off) =>
            CanRead(d, off, 4) ? BitConverter.ToUInt32(d, off) : 0u;

        private static int RI32(byte[] d, int off) =>
            CanRead(d, off, 4) ? BitConverter.ToInt32(d, off) : 0;

        private static float RF32(byte[] d, int off) =>
            CanRead(d, off, 4) ? BitConverter.ToSingle(d, off) : 0f;

        private static uint RU32(byte[] d, int off, bool be) =>
            be ? BSwap32(CanRead(d, off, 4) ? BitConverter.ToUInt32(d, off) : 0u)
               : (CanRead(d, off, 4) ? BitConverter.ToUInt32(d, off) : 0u);

        private static int RI32(byte[] d, int off, bool be) =>
            be ? (int)BSwap32(CanRead(d, off, 4) ? BitConverter.ToUInt32(d, off) : 0u)
               : (CanRead(d, off, 4) ? BitConverter.ToInt32(d, off) : 0);

        private static float RF32(byte[] d, int off, bool be)
        {
            if (!CanRead(d, off, 4)) return 0f;
            uint bits = BitConverter.ToUInt32(d, off);
            if (be) bits = BSwap32(bits);
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }
        
        private static Vector3 RV3(byte[] d, int off)
        {
            if (!CanRead(d, off, 12)) return Vector3.Zero;
            return new Vector3(
                BitConverter.ToSingle(d, off),
                BitConverter.ToSingle(d, off + 4),
                BitConverter.ToSingle(d, off + 8));
        }

        private static GrannyTransform RTransform(byte[] d, int off)
        {
            var t = new GrannyTransform();
            if (!CanRead(d, off, GrannyTransform.SizeBytes)) return t;
            t.Flags       = BitConverter.ToUInt32(d, off);
            t.Position    = new Vector3(BitConverter.ToSingle(d, off+ 4), BitConverter.ToSingle(d, off+ 8), BitConverter.ToSingle(d, off+12));
            t.Orientation = new Quaternion(BitConverter.ToSingle(d, off+16), BitConverter.ToSingle(d, off+20), BitConverter.ToSingle(d, off+24), BitConverter.ToSingle(d, off+28));
            t.ScaleShear0 = new Vector3(BitConverter.ToSingle(d, off+32), BitConverter.ToSingle(d, off+36), BitConverter.ToSingle(d, off+40));
            t.ScaleShear1 = new Vector3(BitConverter.ToSingle(d, off+44), BitConverter.ToSingle(d, off+48), BitConverter.ToSingle(d, off+52));
            t.ScaleShear2 = new Vector3(BitConverter.ToSingle(d, off+56), BitConverter.ToSingle(d, off+60), BitConverter.ToSingle(d, off+64));
            return t;
        }

        private static GrannyTransform RTransform(byte[] d, int off, bool be)
        {
            var t = new GrannyTransform();
            if (!CanRead(d, off, GrannyTransform.SizeBytes)) return t;
            t.Flags       = RU32(d, off, be);
            t.Position    = new Vector3(RF32(d, off+ 4, be), RF32(d, off+ 8, be), RF32(d, off+12, be));
            t.Orientation = new Quaternion(RF32(d, off+16, be), RF32(d, off+20, be), RF32(d, off+24, be), RF32(d, off+28, be));
            t.ScaleShear0 = new Vector3(RF32(d, off+32, be), RF32(d, off+36, be), RF32(d, off+40, be));
            t.ScaleShear1 = new Vector3(RF32(d, off+44, be), RF32(d, off+48, be), RF32(d, off+52, be));
            t.ScaleShear2 = new Vector3(RF32(d, off+56, be), RF32(d, off+60, be), RF32(d, off+64, be));
            return t;
        }

        private static Matrix4x4 RMat4(byte[] d, int off)
        {
            if (!CanRead(d, off, 64)) return Matrix4x4.Identity;
            return new Matrix4x4(
                BitConverter.ToSingle(d, off+ 0), BitConverter.ToSingle(d, off+ 4),
                BitConverter.ToSingle(d, off+ 8), BitConverter.ToSingle(d, off+12),
                BitConverter.ToSingle(d, off+16), BitConverter.ToSingle(d, off+20),
                BitConverter.ToSingle(d, off+24), BitConverter.ToSingle(d, off+28),
                BitConverter.ToSingle(d, off+32), BitConverter.ToSingle(d, off+36),
                BitConverter.ToSingle(d, off+40), BitConverter.ToSingle(d, off+44),
                BitConverter.ToSingle(d, off+48), BitConverter.ToSingle(d, off+52),
                BitConverter.ToSingle(d, off+56), BitConverter.ToSingle(d, off+60));
        }

        private static byte[] SecData(GrannyContainer c, int secIdx)
        {
            if (secIdx < 0 || secIdx >= c.SectionData.Count) return Array.Empty<byte>();
            return c.SectionData[secIdx];
        }

        // Magic identification

        // Returns (isBigEndian, pointerSize).
        // NOTE: for Magic_Old files the pointer size here is a default only;
        // ReadContainer overrides it from HeaderFormat after reading the initial header.
        private static (bool IsBigEndian, int PointerSize) IdentifyMagicFull(byte[] magic16)
        {
            if (magic16.Length < 16) throw new InvalidDataException("Magic block too short.");

            if (BytesMatch(magic16, Magic_32LE)) return (false, 4);
            if (BytesMatch(magic16, Magic_64LE)) return (false, 8);
            // Magic_Old: pointer size determined later from HeaderFormat (default 4 here)
            if (BytesMatch(magic16, Magic_Old))  return (false, 4);
            // Legacy v5 magic — also uses HeaderFormat for pointer size
            if (BytesMatch(magic16, Magic_v5_Old)) return (false, 4);
            if (BytesMatch(magic16, Magic_32BE)) return (true,  4);
            if (BytesMatch(magic16, Magic_64BE)) return (true,  8);

            if (BytesMatchReversed(magic16, Magic_32BE)) return (true,  4);
            if (BytesMatchReversed(magic16, Magic_64BE)) return (true,  8);

            System.Diagnostics.Debug.WriteLine(
                $"WARNING: Unknown Granny magic: {BitConverter.ToString(magic16)}. Assuming 64-bit LE.");
            return (false, 8);
        }

        private static bool BytesMatch(byte[] a, byte[] b)
        {
            for (int i = 0; i < 16; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static bool BytesMatchReversed(byte[] file, byte[] pattern)
        {
            for (int w = 0; w < 4; w++)
            {
                int b = w * 4;
                if (file[b] != pattern[b+3] || file[b+1] != pattern[b+2] ||
                    file[b+2] != pattern[b+1] || file[b+3] != pattern[b])
                    return false;
            }
            return true;
        }

        // Endian helpers

        private static uint BSwap32(uint v) =>
            (v >> 24) | ((v >> 8) & 0xFF00u) | ((v << 8) & 0xFF0000u) | (v << 24);

        private static void BSwapWords(byte[] buf)
        {
            for (int i = 0; i + 3 < buf.Length; i += 4)
            {
                byte t = buf[i]; buf[i] = buf[i + 3]; buf[i + 3] = t;
                t = buf[i + 1]; buf[i + 1] = buf[i + 2]; buf[i + 2] = t;
            }
        }

        private static void BSwapSection(GrannySectionRaw s)
        {
            s.Format               = BSwap32(s.Format);
            s.DataOffset           = BSwap32(s.DataOffset);
            s.DataSize             = BSwap32(s.DataSize);
            s.ExpandedDataSize     = BSwap32(s.ExpandedDataSize);
            s.InternalAlignment    = BSwap32(s.InternalAlignment);
            s.First16Bit           = BSwap32(s.First16Bit);
            s.First8Bit            = BSwap32(s.First8Bit);
            s.PointerFixupOffset   = BSwap32(s.PointerFixupOffset);
            s.PointerFixupCount    = BSwap32(s.PointerFixupCount);
            s.MixedMarshallingOffset = BSwap32(s.MixedMarshallingOffset);
            s.MixedMarshallingCount  = BSwap32(s.MixedMarshallingCount);
        }

        // Section data reader

        private static byte[] ReadSectionData(byte[] raw, GrannySectionRaw sec, bool bigEndian)
        {
            if (sec.DataSize == 0)
                return sec.ExpandedDataSize > 0 ? new byte[sec.ExpandedDataSize] : Array.Empty<byte>();

            if (sec.DataOffset >= (uint)raw.Length)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"WARNING: Section DataOffset 0x{sec.DataOffset:X} past EOF ({raw.Length}). Returning empty.");
                return sec.ExpandedDataSize > 0 ? new byte[sec.ExpandedDataSize] : Array.Empty<byte>();
            }

            long available = (long)raw.Length - (long)sec.DataOffset;
            int readSize = (int)Math.Min((long)sec.DataSize, available);
            if (readSize <= 0)
                return sec.ExpandedDataSize > 0 ? new byte[sec.ExpandedDataSize] : Array.Empty<byte>();
            byte[] onDisk = new byte[readSize];
            Array.Copy(raw, (int)sec.DataOffset, onDisk, 0, readSize);

            uint comprFmt = sec.Format & 0x3u;

            if (comprFmt == GrannyCompressionFormat.None ||
                sec.DataSize == sec.ExpandedDataSize ||
                sec.ExpandedDataSize == 0)
            {
                return onDisk;
            }

            if (sec.ExpandedDataSize > 256 * 1024 * 1024)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"WARNING: Implausible expanded size {sec.ExpandedDataSize}. Returning raw.");
                return onDisk;
            }

            byte[] output = new byte[sec.ExpandedDataSize];
            int stop0 = (int)sec.First16Bit;
            int stop1 = (int)sec.First8Bit;
            int stop2 = (int)sec.ExpandedDataSize;

            // Granny2 uses a built-in Oodle0/Oodle1 compression model (not standalone Oodle-core).
            // granny2_x64.dll exposes GrannyDecompressData for both formats via a 3-stop model.
            if (Granny2Native.IsAvailable)
            {
                if (Granny2Native.Decompress((int)comprFmt, bigEndian, onDisk,
                                              stop0, stop1, stop2, output))
                    return output;

                System.Diagnostics.Debug.WriteLine(
                    $"WARNING: granny2_x64 GrannyDecompressData failed (fmt={comprFmt}, " +
                    $"compressed={onDisk.Length}, expanded={sec.ExpandedDataSize}). " +
                    "Returning raw bytes — section data will be invalid.");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"WARNING: granny2_x64.dll not available; cannot decompress GR2 section " +
                    $"(fmt={comprFmt}).  Copy granny2_x64.dll to the application directory.");
            }

            // Do NOT fall back to the generic Oodle-core library (oo2core_*.dll).
            // GR2's Oodle0/Oodle1 are proprietary RAD internal formats that are
            // incompatible with the public OodleLZ API.
            return onDisk;
        }

        //  Fixup resolution

        private static (int Section, int Offset) ResolvePointer(GrannyContainer c, int secIdx, int offsetInSection)
        {
            if (!c.Fixups.TryGetValue(secIdx, out var dict)) return (-1, 0);
            if (dict.TryGetValue((uint)offsetInSection, out var f))
                return ((int)f.ToSectionIndex, (int)f.ToOffset);
            return (-1, 0);
        }

        private static string ResolveString(GrannyContainer c, int secIdx, int ptrOffset)
        {
            var t = ResolvePointer(c, secIdx, ptrOffset);
            if (t.Section < 0) return null;
            var d = SecData(c, t.Section);
            int s = t.Offset;
            if (s < 0 || s >= d.Length) return null;
            int e = s;
            while (e < d.Length && d[e] != 0) e++;
            if (e == s) return string.Empty;
            return Encoding.ASCII.GetString(d, s, e - s);
        }

        //  Type-system parsing

        private List<GrannyTypeDef> ReadTypeDefArray(GrannyContainer c, int secIdx, int offset, HashSet<(int, int)> visiting = null)
        {
            if (visiting == null) visiting = new HashSet<(int, int)>();
            if (!visiting.Add((secIdx, offset))) return new List<GrannyTypeDef>();

            var result = new List<GrannyTypeDef>();
            var data = SecData(c, secIdx);
            int RecordSize = DetectTypeDefStride(c, secIdx);
            int pos = offset;

            for (int safety = 0; safety < 8192; safety++)
            {
                if (!CanRead(data, pos, RecordSize)) break;

                uint rawType = c.IsBigEndian ? BSwap32(BitConverter.ToUInt32(data, pos)) : BitConverter.ToUInt32(data, pos);
                if (rawType == 0) break;

                var type = (GrannyMemberType)rawType;
                var td = new GrannyTypeDef { Type = type };
                td.Name = ResolveString(c, secIdx, pos + 4);

                int arrayWidthOffset = RecordSize == 44 ? 20 : 12;
                td.ArrayWidth = c.IsBigEndian
                    ? (int)BSwap32(BitConverter.ToUInt32(data, pos + arrayWidthOffset))
                    : BitConverter.ToInt32(data, pos + arrayWidthOffset);

                int refTypeOffset = RecordSize == 44 ? 12 : 8;
                var childPtr = ResolvePointer(c, secIdx, pos + refTypeOffset);
                td.Children = childPtr.Section >= 0
                    ? ReadTypeDefArray(c, childPtr.Section, childPtr.Offset, visiting)
                    : new List<GrannyTypeDef>();

                result.Add(td);
                pos += RecordSize;
            }
            
            visiting.Remove((secIdx, offset));
            return result;
        }

        // Type-def record layout: 20 + 3*ptrSize bytes (44 on 64-bit, 32 on 32-bit).
        private int DetectTypeDefStride(GrannyContainer c, int secIdx) =>
            _ptrSize == 8 ? 44 : 32;

        //  Member size calculation

        private int GetMemberSize(GrannyTypeDef td)
        {
            int count = Math.Max(1, td.ArrayWidth);
            return td.Type switch
            {
                GrannyMemberType.Inline           => CalculateStructSize(td.Children) * count,
                GrannyMemberType.Reference        => _ptrSize,
                GrannyMemberType.ReferenceToArray  => 4 + _ptrSize,
                GrannyMemberType.ArrayOfReferences => 4 + _ptrSize,
                GrannyMemberType.VariantReference  => 2 * _ptrSize,
                GrannyMemberType.String           => _ptrSize,
                GrannyMemberType.Transform        => GrannyTransform.SizeBytes * count,
                GrannyMemberType.Real32           => 4 * count,
                GrannyMemberType.Int32 or GrannyMemberType.UInt32 or GrannyMemberType.Bool => 4 * count,
                GrannyMemberType.Int16 or GrannyMemberType.UInt16
                    or GrannyMemberType.BinormalInt16 or GrannyMemberType.NormalUInt16
                    or GrannyMemberType.Real16 => 2 * count,
                GrannyMemberType.Int8 or GrannyMemberType.UInt8
                    or GrannyMemberType.BinormalInt8 or GrannyMemberType.NormalUInt8 => 1 * count,
                GrannyMemberType.EmptyReference   => _ptrSize,
                GrannyMemberType.Removed          => 0,
                _ => 4 * count,
            };
        }

        private int CalculateStructSize(List<GrannyTypeDef> members)
        {
            if (members == null) return 0;
            int total = 0;
            foreach (var m in members) total += GetMemberSize(m);
            return total;
        }

        //  Root object dispatcher

        private void ParseRootObject(GrannyFileData result, GrannyContainer c,
                                      List<GrannyTypeDef> typeDefs, int secIdx, int offset)
        {
            var data = SecData(c, secIdx);
            bool be = c.IsBigEndian;
            int pos = offset;
            foreach (var td in typeDefs)
            {
                int consumed = ParseMember(result, c, td, secIdx, pos, data, be);
                if (consumed <= 0)
                    consumed = GetMemberSize(td);
                pos += consumed;
            }
        }

        private int ParseMember(GrannyFileData result, GrannyContainer c,
                                 GrannyTypeDef td, int secIdx, int offset, byte[] data, bool be = false)
        {
            int count = Math.Max(1, td.ArrayWidth);
            int PTR = _ptrSize;
            int ARRAY_HEADER = 4 + PTR;
            switch (td.Type)
            {
                case GrannyMemberType.Inline:
                {
                    int total = 0;
                    for (int i = 0; i < count; i++)
                    {
                        int sub = 0;
                        foreach (var ch in td.Children)
                        {
                            int chSize = ParseMember(result, c, ch, secIdx, offset + total + sub, data, be);
                            if (chSize <= 0) chSize = GetMemberSize(ch);
                            sub += chSize;
                        }
                        total += sub;
                    }
                    return total;
                }
                case GrannyMemberType.Reference:
                    return PTR;
                case GrannyMemberType.ReferenceToArray:
                {
                    if (!CanRead(data, offset, ARRAY_HEADER)) return ARRAY_HEADER;
                    int n = RI32(data, offset, be);
                    var ptr = ResolvePointer(c, secIdx, offset + 4);
                    if (ptr.Section >= 0 && n > 0 && n < 1_000_000)
                        DispatchArray(result, c, td, ptr.Section, ptr.Offset, n, isRefArray: false);
                    return ARRAY_HEADER;
                }
                case GrannyMemberType.ArrayOfReferences:
                {
                    if (!CanRead(data, offset, ARRAY_HEADER)) return ARRAY_HEADER;
                    int n = RI32(data, offset, be);
                    var ptr = ResolvePointer(c, secIdx, offset + 4);
                    if (ptr.Section >= 0 && n > 0 && n < 1_000_000)
                        DispatchArray(result, c, td, ptr.Section, ptr.Offset, n, isRefArray: true);
                    return ARRAY_HEADER;
                }
                case GrannyMemberType.VariantReference:
                    return 2 * PTR;
                case GrannyMemberType.String:
                {
                    string val = ResolveString(c, secIdx, offset);
                    if (td.Name == "FromFileName") result.SourceFileName = val;
                    return PTR;
                }
                case GrannyMemberType.Transform:        return GrannyTransform.SizeBytes * count;
                case GrannyMemberType.Real32:            return 4 * count;
                case GrannyMemberType.Int32:
                case GrannyMemberType.UInt32:
                case GrannyMemberType.Bool:              return 4 * count;
                case GrannyMemberType.Int16:
                case GrannyMemberType.UInt16:
                case GrannyMemberType.BinormalInt16:
                case GrannyMemberType.NormalUInt16:
                case GrannyMemberType.Real16:            return 2 * count;
                case GrannyMemberType.Int8:
                case GrannyMemberType.UInt8:
                case GrannyMemberType.BinormalInt8:
                case GrannyMemberType.NormalUInt8:       return 1 * count;
                case GrannyMemberType.EmptyReference:    return PTR;
                case GrannyMemberType.Removed:           return 0;
                default:                                 return 4 * count;
            }
        }

        private void DispatchArray(GrannyFileData result, GrannyContainer c,
                                    GrannyTypeDef td, int secIdx, int offset, int count, bool isRefArray)
        {
            string name = td.Name;
            int PTR = c.PointerSize;
            if (isRefArray)
            {
                var arrData = SecData(c, secIdx);
                if (name == "Skeletons")
                {
                    for (int i = 0; i < count; i++)
                    {
                        int ptrOff = offset + i * PTR;
                        if (!CanRead(arrData, ptrOff, PTR)) break;
                        var t = ResolvePointer(c, secIdx, ptrOff);
                        if (t.Section >= 0) ParseSkeletons(result, c, td.Children, t.Section, t.Offset, 1);
                    }
                }
                else if (name == "Animations")
                {
                    for (int i = 0; i < count; i++)
                    {
                        int ptrOff = offset + i * PTR;
                        if (!CanRead(arrData, ptrOff, PTR)) break;
                        var t = ResolvePointer(c, secIdx, ptrOff);
                        if (t.Section >= 0) ParseAnimations(result, c, td.Children, t.Section, t.Offset, 1);
                    }
                }
                else if (name == "TrackGroups")
                {
                    for (int i = 0; i < count; i++)
                    {
                        int ptrOff = offset + i * PTR;
                        if (!CanRead(arrData, ptrOff, PTR)) break;
                        var t = ResolvePointer(c, secIdx, ptrOff);
                        if (t.Section >= 0) ParseTrackGroups(result, c, td.Children, t.Section, t.Offset, 1);
                    }
                }
                else if (name == "Models")
                {
                    for (int i = 0; i < count; i++)
                    {
                        int ptrOff = offset + i * PTR;
                        if (!CanRead(arrData, ptrOff, PTR)) break;
                        var t = ResolvePointer(c, secIdx, ptrOff);
                        if (t.Section >= 0) ParseModels(result, c, td.Children, t.Section, t.Offset, 1);
                    }
                }
            }
            else
            {
                if      (name == "Skeletons")      ParseSkeletons(result, c, td.Children, secIdx, offset, count);
                else if (name == "Animations")     ParseAnimations(result, c, td.Children, secIdx, offset, count);
                else if (name == "TrackGroups")     ParseTrackGroups(result, c, td.Children, secIdx, offset, count);
                else if (name == "Models")         ParseModels(result, c, td.Children, secIdx, offset, count);
                else if (name == "AnimationSlots") ParseAnimSlots(result, c, td.Children, secIdx, offset, count);
                else if (name == "AnimationSets")  ParseAnimSets(result, c, td.Children, secIdx, offset, count);
            }
        }

        //  Skeleton

        private void ParseSkeletons(GrannyFileData result, GrannyContainer c,
                                     List<GrannyTypeDef> types, int secIdx, int offset, int count)
        {
            int sz = CalculateStructSize(types);
            if (sz <= 0) sz = 16;
            var data = SecData(c, secIdx);
            for (int i = 0; i < count; i++)
            {
                int off = offset + i * sz;
                if (!CanRead(data, off, Math.Min(sz, 4))) break;
                var skel = ReadSkeleton(c, types, secIdx, off);
                if (skel != null) result.Skeletons.Add(skel);
            }
        }

        private GrannySkeleton ReadSkeleton(GrannyContainer c, List<GrannyTypeDef> types, int secIdx, int off)
        {
            var skel = new GrannySkeleton();
            var data = SecData(c, secIdx);
            int pos = off;
            foreach (var td in types)
            {
                switch (td.Name)
                {
                    case "Name":
                        skel.Name = ResolveString(c, secIdx, pos);
                        pos += GetMemberSize(td);
                        break;
                    case "Bones":
                        if (td.Type == GrannyMemberType.ReferenceToArray)
                        {
                            int n = RI32(data, pos);
                            var t = ResolvePointer(c, secIdx, pos + 4);
                            if (t.Section >= 0 && n > 0 && n < 100_000)
                                ReadBones(skel, c, td.Children, t.Section, t.Offset, n);
                            pos += GetMemberSize(td);
                        }
                        else pos += GetMemberSize(td);
                        break;
                    case "LODType":
                        skel.LODType = RI32(data, pos);
                        pos += 4;
                        break;
                    default:
                        pos += GetMemberSize(td);
                        break;
                }
            }
            return skel;
        }

        private void ReadBones(GrannySkeleton skel, GrannyContainer c,
                                List<GrannyTypeDef> types, int secIdx, int offset, int count)
        {
            int sz = CalculateStructSize(types);
            if (sz <= 0)
                sz = _ptrSize == 8 ? 152 : 144;
            
            var data = SecData(c, secIdx);
            for (int i = 0; i < count; i++)
            {
                int off = offset + i * sz;
                if (!CanRead(data, off, Math.Min(sz, _ptrSize + 4))) break;
                var bone = ReadBone(c, types, secIdx, off, data);
                if (bone != null) skel.Bones.Add(bone);
            }
        }

        private GrannyBone ReadBone(GrannyContainer c, List<GrannyTypeDef> types, int secIdx, int off, byte[] data)
        {
            var bone = new GrannyBone();
            int pos = off;
            bool be = c.IsBigEndian;
            
            foreach (var td in types)
            {
                switch (td.Name)
                {
                    case "Name":
                        bone.Name = ResolveString(c, secIdx, pos);
                        pos += _ptrSize;
                        break;
                    case "ParentIndex":
                        if (CanRead(data, pos, 4))
                            bone.ParentIndex = RI32(data, pos, be);
                        pos += 4;
                        break;
                    case "LocalTransform":
                    case "Transform":
                        if (CanRead(data, pos, GrannyTransform.SizeBytes))
                            bone.LocalTransform = RTransform(data, pos, be);
                        pos += GrannyTransform.SizeBytes;
                        break;
                    case "InverseWorld4x4":
                    case "InverseWorldTransform":
                        if (CanRead(data, pos, 64))
                        {
                            float[] matrixData = new float[16];
                            for (int i = 0; i < 16; i++)
                            {
                                matrixData[i] = RF32(data, pos + i * 4, be);
                            }
                            bone.InverseWorld4x4 = new Matrix4x4(
                                matrixData[0], matrixData[1], matrixData[2], matrixData[3],
                                matrixData[4], matrixData[5], matrixData[6], matrixData[7],
                                matrixData[8], matrixData[9], matrixData[10], matrixData[11],
                                matrixData[12], matrixData[13], matrixData[14], matrixData[15]
                            );
                        }
                        pos += 64;
                        break;
                    case "LODError":
                        if (CanRead(data, pos, 4))
                            bone.LODError = RF32(data, pos, be);
                        pos += 4;
                        break;
                    default:
                        pos += GetMemberSize(td);
                        break;
                }
            }
            return bone;
        }

        //  Model

        // Parses granny_model entries from the file-info "Models" array.
        private void ParseModels(GrannyFileData result, GrannyContainer c,
                                  List<GrannyTypeDef> types, int secIdx, int offset, int count)
        {
            int sz = CalculateStructSize(types);
            // granny_model (64-bit): ptr Name + ptr Skeleton + transform(68) + int MeshBindingCount + ptr MeshBindings
            // = 8 + 8 + 68 + 4 + 8 = 96 bytes (with 4-byte padding for alignment = 100?)
            if (sz <= 0) sz = _ptrSize == 8 ? 96 : 80;
            var data = SecData(c, secIdx);
            for (int i = 0; i < count; i++)
            {
                int off = offset + i * sz;
                if (!CanRead(data, off, Math.Min(sz, _ptrSize))) break;
                var model = ReadModel(c, types, secIdx, off);
                if (model != null) result.Models.Add(model);
            }
        }

        private GrannyModel ReadModel(GrannyContainer c, List<GrannyTypeDef> types, int secIdx, int off)
        {
            var model = new GrannyModel();
            var data = SecData(c, secIdx);
            int pos = off;
            bool be = c.IsBigEndian;
            foreach (var td in types)
            {
                switch (td.Name)
                {
                    case "Name":
                        model.Name = ResolveString(c, secIdx, pos);
                        pos += GetMemberSize(td);
                        break;
                    case "Skeleton":
                        // Reference to the granny_skeleton struct; read its first string field (Name)
                        if (td.Type == GrannyMemberType.Reference)
                        {
                            var skelPtr = ResolvePointer(c, secIdx, pos);
                            if (skelPtr.Section >= 0)
                                model.SkeletonName = ResolveString(c, skelPtr.Section, skelPtr.Offset);
                        }
                        pos += GetMemberSize(td);
                        break;
                    case "InitialPlacement":
                        pos += GrannyTransform.SizeBytes;
                        break;
                    case "MeshBindings":
                        if (td.Type == GrannyMemberType.ReferenceToArray && CanRead(data, pos, 4))
                            model.MeshBindingCount = RI32(data, pos, be);
                        pos += GetMemberSize(td);
                        break;
                    default:
                        pos += GetMemberSize(td);
                        break;
                }
            }
            return model;
        }

        //  Animation

        private void ParseAnimations(GrannyFileData result, GrannyContainer c,
                                      List<GrannyTypeDef> types, int secIdx, int offset, int count)
        {
            int sz = CalculateStructSize(types);
            if (sz <= 0) sz = 28;
            var data = SecData(c, secIdx);
            for (int i = 0; i < count; i++)
            {
                int off = offset + i * sz;
                if (!CanRead(data, off, Math.Min(sz, 4))) break;
                var anim = ReadAnimation(c, types, secIdx, off);
                if (anim != null) result.Animations.Add(anim);
            }
        }

        private GrannyAnimation ReadAnimation(GrannyContainer c, List<GrannyTypeDef> types, int secIdx, int off)
        {
            var anim = new GrannyAnimation();
            var data = SecData(c, secIdx);
            int pos = off;
            foreach (var td in types)
            {
                switch (td.Name)
                {
                    case "Name":
                        anim.Name = ResolveString(c, secIdx, pos);
                        pos += GetMemberSize(td);
                        break;
                    case "Duration":
                        anim.Duration = RF32(data, pos); pos += 4; break;
                    case "TimeStep":
                        anim.TimeStep = RF32(data, pos); pos += 4; break;
                    case "Oversampling":
                        anim.Oversampling = RF32(data, pos); pos += 4; break;
                    case "TrackGroups":
                        if (td.Type == GrannyMemberType.ArrayOfReferences)
                        {
                            int n = RI32(data, pos);
                            var tp = ResolvePointer(c, secIdx, pos + 4);
                            if (tp.Section >= 0 && n > 0 && n < 10_000)
                            {
                                var tpData = SecData(c, tp.Section);
                                for (int k = 0; k < n; k++)
                                {
                                    int ptrOff = tp.Offset + k * _ptrSize;
                                    if (!CanRead(tpData, ptrOff, _ptrSize)) break;
                                    var rt = ResolvePointer(c, tp.Section, ptrOff);
                                    if (rt.Section >= 0)
                                    {
                                        var tg = ReadTrackGroup(c, td.Children, rt.Section, rt.Offset);
                                        if (tg != null) anim.TrackGroups.Add(tg);
                                    }
                                }
                            }
                            pos += GetMemberSize(td);
                        }
                        else if (td.Type == GrannyMemberType.ReferenceToArray)
                        {
                            int n = RI32(data, pos);
                            var tp = ResolvePointer(c, secIdx, pos + 4);
                            if (tp.Section >= 0 && n > 0 && n < 10_000)
                            {
                                int esz = CalculateStructSize(td.Children);
                                if (esz <= 0) esz = 80;
                                for (int k = 0; k < n; k++)
                                {
                                    var tg = ReadTrackGroup(c, td.Children, tp.Section, tp.Offset + k * esz);
                                    if (tg != null) anim.TrackGroups.Add(tg);
                                }
                            }
                            pos += GetMemberSize(td);
                        }
                        else pos += GetMemberSize(td);
                        break;
                    case "DefaultLoopCount":
                        anim.DefaultLoopCount = RI32(data, pos); pos += 4; break;
                    case "Flags":
                        anim.Flags = RI32(data, pos); pos += 4; break;
                    default:
                        pos += GetMemberSize(td);
                        break;
                }
            }
            return anim;
        }

        //  Track Group

        private void ParseTrackGroups(GrannyFileData result, GrannyContainer c,
                                       List<GrannyTypeDef> types, int secIdx, int offset, int count)
        {
            int sz = CalculateStructSize(types);
            if (sz <= 0) sz = 104;
            var data = SecData(c, secIdx);
            for (int i = 0; i < count; i++)
            {
                int off = offset + i * sz;
                if (!CanRead(data, off, Math.Min(sz, 4))) break;
                var tg = ReadTrackGroup(c, types, secIdx, off);
                if (tg != null) result.TrackGroups.Add(tg);
            }
        }

        private GrannyTrackGroup ReadTrackGroup(GrannyContainer c, List<GrannyTypeDef> types, int secIdx, int off)
        {
            var tg = new GrannyTrackGroup();
            var data = SecData(c, secIdx);
            int pos = off;
            foreach (var td in types)
            {
                switch (td.Name)
                {
                    case "Name":
                        tg.Name = ResolveString(c, secIdx, pos);
                        pos += GetMemberSize(td);
                        break;
                    case "TransformTracks":
                        if (td.Type == GrannyMemberType.ReferenceToArray)
                        {
                            int n = RI32(data, pos);
                            var tp = ResolvePointer(c, secIdx, pos + 4);
                            if (tp.Section >= 0 && n > 0 && n < 50_000)
                            {
                                int esz = CalculateStructSize(td.Children);
                                if (esz <= 0) esz = 20;
                                for (int k = 0; k < n; k++)
                                {
                                    var tt = ReadTransformTrack(c, td.Children, tp.Section, tp.Offset + k * esz);
                                    if (tt != null) tg.TransformTracks.Add(tt);
                                }
                            }
                            pos += GetMemberSize(td);
                        }
                        else pos += GetMemberSize(td);
                        break;
                    case "InitialPlacement":
                        tg.InitialPlacement = RTransform(data, pos);
                        pos += GrannyTransform.SizeBytes;
                        break;
                    case "Flags":
                        tg.Flags = RI32(data, pos); pos += 4; break;
                    case "LoopTranslation":
                        tg.LoopTranslation = RV3(data, pos); pos += 12; break;
                    default:
                        pos += GetMemberSize(td);
                        break;
                }
            }
            return tg;
        }

        //  Transform Track + Curve Data Extraction

        private GrannyTransformTrack ReadTransformTrack(GrannyContainer c,
                                                         List<GrannyTypeDef> types, int secIdx, int off)
        {
            var tt = new GrannyTransformTrack();
            var data = SecData(c, secIdx);
            int pos = off;
            foreach (var td in types)
            {
                switch (td.Name)
                {
                    case "Name":
                        tt.Name = ResolveString(c, secIdx, pos);
                        pos += GetMemberSize(td);
                        break;
                    case "Flags":
                        if (CanRead(data, pos, 4))
                            tt.Flags = RI32(data, pos);
                        pos += 4;
                        break;
                    case "OrientationCurve":
                        tt.OrientationCurve = ReadCurve2(c, td, secIdx, pos, 4);
                        pos += GetMemberSize(td);
                        break;
                    case "PositionCurve":
                        tt.PositionCurve = ReadCurve2(c, td, secIdx, pos, 3);
                        pos += GetMemberSize(td);
                        break;
                    case "ScaleShearCurve":
                        tt.ScaleShearCurve = ReadCurve2(c, td, secIdx, pos, 9);
                        pos += GetMemberSize(td);
                        break;
                    default:
                        pos += GetMemberSize(td);
                        break;
                }
            }

            BuildKeyframes(tt);  // legacy: synthesise keyframes for backward compat (deprecated)
            return tt;
        }

        private GrannyCurveInfo ReadCurve2(GrannyContainer c, GrannyTypeDef td, int secIdx, int pos, int expectedDimension)
        {
            var curve = new GrannyCurveInfo { Dimension = expectedDimension };

            if (td.Type == GrannyMemberType.Inline && td.Children != null && td.Children.Count > 0)
            {
                int childPos = pos;
                foreach (var child in td.Children)
                {
                    if (child.Name == "CurveData" && child.Type == GrannyMemberType.VariantReference)
                    {
                        ReadCurveVariantReference(c, curve, secIdx, childPos);
                        break;
                    }
                    childPos += GetMemberSize(child);
                }
            }
            else if (td.Type == GrannyMemberType.VariantReference)
            {
                ReadCurveVariantReference(c, curve, secIdx, pos);
            }

            return curve;
        }

        private void ReadCurveVariantReference(GrannyContainer c, GrannyCurveInfo curve, int secIdx, int pos)
        {
            int PTR = _ptrSize;
            var typePtr = ResolvePointer(c, secIdx, pos);
            var dataPtr = ResolvePointer(c, secIdx, pos + PTR);

            if (typePtr.Section < 0 && dataPtr.Section < 0)
            {
                curve.IsIdentity = true;
                curve.FormatName = "DaIdentity";
                return;
            }

            if (typePtr.Section >= 0)
            {
                var curveTypeDefs = ReadTypeDefArray(c, typePtr.Section, typePtr.Offset);
                curve.FormatName = IdentifyCurveFormatFromType(curveTypeDefs);
            }

            if (dataPtr.Section < 0)
            {
                curve.IsIdentity = true;
                return;
            }

            if (curve.FormatName.Contains("DaIdentity"))
            {
                curve.IsIdentity = true;
            }
            else if (curve.FormatName.Contains("Constant"))
            {
                curve.IsConstant = true;
                ReadCurveDataFields(c, curve, typePtr, dataPtr);
            }
            else
            {
                ReadCurveDataFields(c, curve, typePtr, dataPtr);
            }
        }

        private string IdentifyCurveFormatFromType(List<GrannyTypeDef> typeDefs)
        {
            if (typeDefs.Count == 0) return "Unknown";
            foreach (var td in typeDefs)
            {
                if (td.Name == null) continue;
                // Standard Granny header struct names — exact match takes priority.
                // Known names: CurveDataHeader_DaIdentity, DaConstant32f, DaK32fC32f,
                //   DaKeyframes32f, DaK16uC16u, D4nK16uC15u, D3K16uC16u, D3K8uC8u, etc.
                if (td.Name.StartsWith("CurveDataHeader_"))
                    return td.Name.Substring("CurveDataHeader_".Length);
                // Fallback substring checks for files where the header is not the first record
                if (td.Name.Contains("DaIdentity"))  return "DaIdentity";
                if (td.Name.Contains("DaConstant"))  return "DaConstant32f";
                if (td.Name.Contains("DaK32fC32f"))  return "DaK32fC32f";
                if (td.Name.Contains("DaKeyframes")) return "DaKeyframes32f";
                if (td.Name.Contains("DaK16u"))      return "DaK16uC16u";
                if (td.Name.Contains("D4nK16u"))     return "D4nK16uC15u";
                if (td.Name.Contains("D3K16u"))      return "D3K16uC16u";
                if (td.Name.Contains("D3K8u"))       return "D3K8uC8u";
                if (td.Name.Contains("DaK8u"))       return "DaK8uC8u";
            }
            return "Unknown";
        }

        private void ReadCurveDataFields(GrannyContainer c, GrannyCurveInfo curve,
            (int Section, int Offset) typePtr, (int Section, int Offset) dataPtr)
        {
            var typeDefs = ReadTypeDefArray(c, typePtr.Section, typePtr.Offset);
            var data = SecData(c, dataPtr.Section);
            int pos = dataPtr.Offset;

            // Collected dequantization parameters for compressed uint16/uint8 curves
            // (DaK16uC16u, D4nK16uC15u, D3K16uC16u, etc.)
            float[] controlScaleOffsets = null;  // per-dimension: [scale0, offset0, scale1, offset1, ...]
            float knotScale = 0f;                    // decoded knot scale: realTime = knotScale * uint16RawKnot
            ushort scaleOffsetTableEntries = 0;   // packed nibble table for D4n quaternion decompression
            byte curveDegree = 0;                 // B-spline degree from CurveDataHeader (0..3)

            // Pass 1: collect scalar metadata (OneOverKnotScale, ControlScaleOffsets, etc.)
            // Must run before array decoding so that field order in the binary does not matter.
            {
                int posP1 = dataPtr.Offset;
                foreach (var td in typeDefs)
                {
                    int memberSize = GetMemberSize(td);
                    switch (td.Name)
                    {
                        // CurveDataHeader: 2-byte inline struct {Format, Degree}; advance 2 bytes past it.
                        case var n when n != null && n.StartsWith("CurveDataHeader_"):
                            if (CanRead(data, posP1, 2))
                                curveDegree = data[posP1 + 1];
                            break;
                        case "OneOverKnotScale":
                            // SDK: knotScale = 1.0 / OneOverKnotScale; knot = knotScale * raw
                            if (CanRead(data, posP1, 4))
                            {
                                float raw = RF32(data, posP1);
                                knotScale = raw != 0f ? 1.0f / raw : 0f;
                            }
                            break;
                        case "OneOverKnotScaleTrunc":
                            // Truncated uint16: top 16 bits of IEEE754 float, bottom 16 = 0
                            if (CanRead(data, posP1, 2))
                            {
                                ushort trunc = BitConverter.ToUInt16(data, posP1);
                                uint asUint = (uint)trunc << 16;
                                float decoded = BitConverter.ToSingle(BitConverter.GetBytes(asUint), 0);
                                knotScale = decoded != 0f ? 1.0f / decoded : 0f;
                            }
                            break;
                        case "ScaleOffsetTableEntries":
                            // D4nK16uC15u / D4nK8uC7u: uint16 packed nibble table (4 nibbles, 1 per quat component)
                            if (td.Type == GrannyMemberType.UInt16 || td.Type == GrannyMemberType.Int16)
                            {
                                if (CanRead(data, posP1, 2))
                                    scaleOffsetTableEntries = BitConverter.ToUInt16(data, posP1);
                            }
                            else if (td.Type == GrannyMemberType.ReferenceToArray
                                && CanRead(data, posP1, 4 + _ptrSize))
                            {
                                int count = RI32(data, posP1);
                                var ptr = ResolvePointer(c, dataPtr.Section, posP1 + 4);
                                if (ptr.Section >= 0 && count > 0 && count < 1024)
                                {
                                    var soData = SecData(c, ptr.Section);
                                    controlScaleOffsets = new float[count];
                                    for (int i = 0; i < count; i++)
                                    {
                                        int foff = ptr.Offset + i * 4;
                                        controlScaleOffsets[i] = CanRead(soData, foff, 4)
                                            ? BitConverter.ToSingle(soData, foff) : 1f;
                                    }
                                }
                            }
                            else if (td.Type == GrannyMemberType.Real32)
                            {
                                int count = Math.Max(1, td.ArrayWidth);
                                controlScaleOffsets = new float[count];
                                for (int i = 0; i < count; i++)
                                {
                                    int foff = posP1 + i * 4;
                                    controlScaleOffsets[i] = CanRead(data, foff, 4)
                                        ? BitConverter.ToSingle(data, foff) : 1f;
                                }
                            }
                            break;
                        case "ControlScaleOffsets":
                        case "ControlScales":
                        case "ControlOffsets":
                            if (td.Type == GrannyMemberType.ReferenceToArray
                                && CanRead(data, posP1, 4 + _ptrSize))
                            {
                                int count = RI32(data, posP1);
                                var ptr = ResolvePointer(c, dataPtr.Section, posP1 + 4);
                                if (ptr.Section >= 0 && count > 0 && count < 1024)
                                {
                                    var soData = SecData(c, ptr.Section);
                                    controlScaleOffsets = new float[count];
                                    for (int i = 0; i < count; i++)
                                    {
                                        int foff = ptr.Offset + i * 4;
                                        controlScaleOffsets[i] = CanRead(soData, foff, 4)
                                            ? BitConverter.ToSingle(soData, foff) : 1f;
                                    }
                                }
                            }
                            else if (td.Type == GrannyMemberType.Real32)
                            {
                                int count = Math.Max(1, td.ArrayWidth);
                                controlScaleOffsets = new float[count];
                                for (int i = 0; i < count; i++)
                                {
                                    int foff = posP1 + i * 4;
                                    controlScaleOffsets[i] = CanRead(data, foff, 4)
                                        ? BitConverter.ToSingle(data, foff) : 1f;
                                }
                            }
                            break;
                    }
                    posP1 += memberSize;
                }
            }

            // Pass 2: decode array fields using the collected scalar parameters
            foreach (var td in typeDefs)
            {
                int memberSize = GetMemberSize(td);
                switch (td.Name)
                {
                    // CurveDataHeader is 2 bytes in the binary blob — advance over it naturally.
                    case var n when n != null && n.StartsWith("CurveDataHeader_"):
                        break;
                    // Float32 knots (DaK32fC32f, DaKeyframes32f)
                    case "Knots" when td.Type == GrannyMemberType.ReferenceToArray:
                    {
                        if (CanRead(data, pos, 4 + _ptrSize))
                        {
                            int count = RI32(data, pos);
                            var ptr = ResolvePointer(c, dataPtr.Section, pos + 4);
                            if (ptr.Section >= 0 && count > 0 && count < 100_000)
                            {
                                var knotsData = SecData(c, ptr.Section);
                                curve.Knots = new float[count];
                                for (int i = 0; i < count; i++)
                                {
                                    int foff = ptr.Offset + i * 4;
                                    curve.Knots[i] = CanRead(knotsData, foff, 4)
                                        ? BitConverter.ToSingle(knotsData, foff) : 0f;
                                }
                            }
                        }
                        break;
                    }
                    // D4nK16uC15u: 4-component normalized quaternion; 3x uint15 (XYZ) with W implicit, sign in bit 15 of Z word.
                    case "Controls" when td.Type == GrannyMemberType.ReferenceToArray
                                      && curve.FormatName == "D4nK16uC15u":
                    {
                        if (CanRead(data, pos, 4 + _ptrSize))
                        {
                            int count = RI32(data, pos);
                            var ptr = ResolvePointer(c, dataPtr.Section, pos + 4);
                            if (ptr.Section >= 0 && count > 0 && count < 1_000_000)
                            {
                                var controlsData = SecData(c, ptr.Section);
                                curve.Dimension = 4;  // quaternion
                                curve.Controls = DequantizeD4nK16uC15uControls(controlsData, ptr.Offset, count, scaleOffsetTableEntries);
                            }
                        }
                        break;
                    }
                    // Compressed uint16-ReferenceToArray controls (DaK16uC16u, D3K16uC16u)
                    // MUST come before the generic float32 ReferenceToArray case.
                    case "Controls" when td.Type == GrannyMemberType.ReferenceToArray
                                      && curve.FormatName.Contains("16u"):
                    {
                        if (CanRead(data, pos, 4 + _ptrSize))
                        {
                            int count = RI32(data, pos);
                            var ptr = ResolvePointer(c, dataPtr.Section, pos + 4);
                            if (ptr.Section >= 0 && count > 0 && count < 1_000_000)
                            {
                                var controlsData = SecData(c, ptr.Section);
                                curve.Controls = DequantizeUInt16Controls(
                                    controlsData, ptr.Offset, count, controlScaleOffsets);
                            }
                        }
                        break;
                    }
                    // Float32 controls (DaK32fC32f, DaKeyframes32f, DaConstant32f refarray)
                    case "Controls" when td.Type == GrannyMemberType.ReferenceToArray:
                    {
                        if (CanRead(data, pos, 4 + _ptrSize))
                        {
                            int count = RI32(data, pos);
                            var ptr = ResolvePointer(c, dataPtr.Section, pos + 4);
                            if (ptr.Section >= 0 && count > 0 && count < 1_000_000)
                            {
                                var controlsData = SecData(c, ptr.Section);
                                curve.Controls = new float[count];
                                for (int i = 0; i < count; i++)
                                {
                                    int foff = ptr.Offset + i * 4;
                                    curve.Controls[i] = CanRead(controlsData, foff, 4)
                                        ? BitConverter.ToSingle(controlsData, foff) : 0f;
                                }
                            }
                        }
                        break;
                    }
                    // Inline float32 controls (DaConstant32f: stored directly)
                    case "Controls" when td.Type == GrannyMemberType.Real32:
                    {
                        int dim = Math.Max(1, td.ArrayWidth);
                        curve.Controls = new float[dim];
                        for (int i = 0; i < dim; i++)
                        {
                            int foff = pos + i * 4;
                            curve.Controls[i] = CanRead(data, foff, 4)
                                ? BitConverter.ToSingle(data, foff) : 0f;
                        }
                        break;
                    }
                    // Compressed uint16 controls (DaK16uC16u, D4nK16uC15u, D3K16uC16u)
                    // Controls are quantized uint16 values; requires ControlScaleOffsets to unpack.
                    case "Controls" when td.Type == GrannyMemberType.UInt16:
                    {
                        // Inline uint16 array width — treat as quantized controls
                        int dim = Math.Max(1, td.ArrayWidth);
                        if (CanRead(data, pos, dim * 2))
                        {
                            curve.Controls = DequantizeUInt16Controls(data, pos, dim, controlScaleOffsets);
                        }
                        break;
                    }
                    // ControlScaleOffsets: float pairs [scale, offset] per dimension
                    case "ControlScaleOffsets":
                    case "ScaleOffsetTableEntries":
                    case "ControlScales":
                    case "ControlOffsets":
                    {
                        // Accept both ReferenceToArray and inline Real32 variants
                        if (td.Type == GrannyMemberType.ReferenceToArray
                            && CanRead(data, pos, 4 + _ptrSize))
                        {
                            int count = RI32(data, pos);
                            var ptr = ResolvePointer(c, dataPtr.Section, pos + 4);
                            if (ptr.Section >= 0 && count > 0 && count < 1024)
                            {
                                var soData = SecData(c, ptr.Section);
                                controlScaleOffsets = new float[count];
                                for (int i = 0; i < count; i++)
                                {
                                    int foff = ptr.Offset + i * 4;
                                    controlScaleOffsets[i] = CanRead(soData, foff, 4)
                                        ? BitConverter.ToSingle(soData, foff) : 1f;
                                }
                            }
                        }
                        else if (td.Type == GrannyMemberType.Real32)
                        {
                            int count = Math.Max(1, td.ArrayWidth);
                            controlScaleOffsets = new float[count];
                            for (int i = 0; i < count; i++)
                            {
                                int foff = pos + i * 4;
                                controlScaleOffsets[i] = CanRead(data, foff, 4)
                                    ? BitConverter.ToSingle(data, foff) : 1f;
                            }
                        }
                        break;
                    }
                    // OneOverKnotScale: reciprocal of the knot range (D4nK16uC15u)
                    case "OneOverKnotScale":
                        if (CanRead(data, pos, 4))
                        {
                            float raw = RF32(data, pos);
                            knotScale = raw != 0f ? 1.0f / raw : 0f;
                        }
                        break;
                    // KnotsControls combined array (D4nK16uC15u and other variants)
                    case "KnotsControls":
                    {
                        if (td.Type == GrannyMemberType.ReferenceToArray
                            && CanRead(data, pos, 4 + _ptrSize))
                        {
                            int count = RI32(data, pos);
                            var ptr = ResolvePointer(c, dataPtr.Section, pos + 4);
                            if (ptr.Section >= 0 && count > 0 && count < 1_000_000)
                            {
                                var kcData = SecData(c, ptr.Section);
                                if (curve.FormatName == "D4nK16uC15u")
                                {
                                    // Layout: knotCount uint16 knots + knotCount×3 uint16 quaternion components
                                    // Total uint16 elements = 4×knotCount  →  knotCount = count / 4
                                    int knotCount  = count / 4;
                                    int ctrlCount  = knotCount * 3;
                                    curve.Dimension = 4;
                                    curve.Knots = new float[knotCount];
                                    for (int i = 0; i < knotCount; i++)
                                    {
                                        int boff = ptr.Offset + i * 2;
                                        ushort rawKnot = CanRead(kcData, boff, 2)
                                            ? BitConverter.ToUInt16(kcData, boff) : (ushort)0;
                                        curve.Knots[i] = knotScale * rawKnot;
                                    }
                                    // Controls: next ctrlCount uint16s decoded as quantised quaternions
                                    int ctrlByteStart = ptr.Offset + knotCount * 2;
                                    curve.Controls = DequantizeD4nK16uC15uControls(
                                        kcData, ctrlByteStart, ctrlCount, scaleOffsetTableEntries);
                                }
                                else if (curve.FormatName == "D4nK8uC7u")
                                {
                                    // Layout: knotCount uint8 knots + knotCount×3 uint8 quaternion components
                                    // Total uint8 elements = 4×knotCount  →  knotCount = count / 4
                                    int knotCount  = count / 4;
                                    int ctrlCount  = knotCount * 3;
                                    curve.Dimension = 4;
                                    curve.Knots = new float[knotCount];
                                    for (int i = 0; i < knotCount; i++)
                                    {
                                        int boff = ptr.Offset + i;
                                        byte rawKnot = CanRead(kcData, boff, 1) ? kcData[boff] : (byte)0;
                                        curve.Knots[i] = knotScale * rawKnot;
                                    }
                                    int ctrlByteStart = ptr.Offset + knotCount;
                                    curve.Controls = DequantizeD4nK8uC7uControls(
                                        kcData, ctrlByteStart, ctrlCount, scaleOffsetTableEntries);
                                }
                                else if (curve.FormatName.Contains("K16u"))
                                {
                                    // DaK16uC16u, D3K16uC16u KnotsControls: uint16 knots then uint16 controls
                                    int dim = curve.Dimension > 0 ? curve.Dimension : 3;
                                    // Degree+1 control points per span; for B-splines with N knots:
                                    // knotCount = count / (1 + dim)  is a rough heuristic
                                    // Actually: first knotCount values are knots, rest are controls
                                    // The SDK layout: KnotControlCount = totalUint16Count
                                    // We need to infer the split — use the degree to compute:
                                    // For now, use the knot count from any previously parsed Knots field,
                                    // or infer: knotCount is stored implicitly via KnotControlCount >> shift
                                    // For D3K16uC16u: KnotControlCount layout depends on degree
                                    // Simple approach: controls come after knots, and #knots < #controls
                                    int knotCount = count / (dim + 1);
                                    int ctrlCount = count - knotCount;
                                    curve.Knots = new float[knotCount];
                                    for (int i = 0; i < knotCount; i++)
                                    {
                                        int boff = ptr.Offset + i * 2;
                                        ushort rawKnot = CanRead(kcData, boff, 2)
                                            ? BitConverter.ToUInt16(kcData, boff) : (ushort)0;
                                        curve.Knots[i] = knotScale * rawKnot;
                                    }
                                    int ctrlByteStart = ptr.Offset + knotCount * 2;
                                    curve.Controls = DequantizeUInt16Controls(
                                        kcData, ctrlByteStart, ctrlCount, controlScaleOffsets);
                                }
                                else
                                {
                                    // Other KnotsControls formats: store raw float32 values
                                    curve.Knots = Array.Empty<float>();
                                    curve.Controls = new float[count];
                                    for (int i = 0; i < count; i++)
                                    {
                                        int foff = ptr.Offset + i * 4;
                                        curve.Controls[i] = CanRead(kcData, foff, 4)
                                            ? BitConverter.ToSingle(kcData, foff) : 0f;
                                    }
                                }
                            }
                        }
                        break;
                    }
                }
                pos += memberSize;
            }

            // Store curve metadata for B-spline evaluation
            curve.Degree = curveDegree;
            curve.ScaleOffsetTableEntries = scaleOffsetTableEntries;
            if (controlScaleOffsets != null)
            {
                // Split interleaved [scale0, offset0, scale1, offset1, ...] into separate arrays
                // or store D3/Da format scales/offsets directly
                if (curve.FormatName.Contains("D3") || curve.FormatName.Contains("Da"))
                {
                    int dim = controlScaleOffsets.Length / 2;
                    if (dim > 0)
                    {
                        curve.ControlScales = new float[dim];
                        curve.ControlOffsets = new float[dim];
                        for (int i = 0; i < dim; i++)
                        {
                            curve.ControlScales[i] = controlScaleOffsets[i * 2];
                            curve.ControlOffsets[i] = controlScaleOffsets[i * 2 + 1];
                        }
                    }
                }
            }
        }

        // Dequantizes uint16 or uint15 controls using optional scale/offset table.
        // Formula: float_value = (uint16_value / 65535.0f) * scale + offset
        // For D4nK16uC15u (normalized quaternion), uses 15-bit values [0, 32767].
        private static float[] DequantizeUInt16Controls(byte[] data, int offset, int count, float[] scaleOffsets)
        {
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                int byteOff = offset + i * 2;
                if (byteOff + 1 >= data.Length) break;
                ushort raw = BitConverter.ToUInt16(data, byteOff);

                float scale = 1f, off = 0f;
                if (scaleOffsets != null && scaleOffsets.Length >= 2)
                {
                    // scaleOffsets layout: [scale0, offset0, scale1, offset1, ...]
                    int dim = scaleOffsets.Length / 2;
                    int d = (dim > 0) ? (i % dim) : 0;
                    scale = scaleOffsets[d * 2];
                    off   = scaleOffsets[d * 2 + 1];
                }
                result[i] = (raw / 65535f) * scale + off;
            }
            return result;
        }

        // Granny2 QuaternionCurveScaleOffsetTable — 16 entries × 2 floats (scale, offset).
        // Each entry provides per-component dequantization: value = offset + scale × (raw &amp; 0x7FFF).
        private static readonly float[] QuaternionScaleOffsetTable = new float[]
        {
            // 1/sqrt(2) ≈ 0.707106781
             1.414213562f, -0.707106781f,   // entry  0
             0.707106781f, -0.353553391f,   // entry  1
             0.353553391f, -0.530330086f,   // entry  2
             0.353553391f, -0.176776695f,   // entry  3
             0.353553391f,  0.176776695f,   // entry  4
             0.176776695f, -0.176776695f,   // entry  5
             0.176776695f, -0.088388348f,   // entry  6
             0.176776695f,  0.000000000f,   // entry  7
            // Negated copies (entries 8-15)
            -1.414213562f,  0.707106781f,   // entry  8
            -0.707106781f,  0.353553391f,   // entry  9
            -0.353553391f,  0.530330086f,   // entry 10
            -0.353553391f,  0.176776695f,   // entry 11
            -0.353553391f, -0.176776695f,   // entry 12
            -0.176776695f,  0.176776695f,   // entry 13
            -0.176776695f,  0.088388348f,   // entry 14
            -0.176776695f,  0.000000000f,   // entry 15
        };

        // Decodes D4nK16uC15u quantized quaternion controls (3 × uint16 per quaternion).
        // Per quaternion encoding:
        //  - Word 0 bit 15: sign of the missing (largest-magnitude) component
        //  - ((Word 1 >> 14) &amp; 2) | (Word 2 >> 15): index of the missing component (0-3)
        //  - Each word's lower 15 bits: compressed component value
        //  - Components are dequantized via the ScaleOffsetTable and written cyclically
        //    starting from (MissingComponentIndex + 1) mod 4
        //  - Missing component = ±sqrt(1 − sum_of_squares)
        private static float[] DequantizeD4nK16uC15uControls(byte[] data, int offset, int count, ushort scaleOffsetTableEntries)
        {
            // count = total uint16 values stored = quatCount × 3
            int quatCount = count / 3;
            var result = new float[quatCount * 4];

            // Extract per-component scales and offsets from the packed nibble table
            float[] ctrlScales = new float[4];
            float[] ctrlOffsets = new float[4];
            ushort entry = scaleOffsetTableEntries;
            for (int dim = 0; dim < 4; dim++)
            {
                int tableIndex = entry & 0xF;
                entry >>= 4;
                ctrlScales[dim] = QuaternionScaleOffsetTable[tableIndex * 2] / 32767f;
                ctrlOffsets[dim] = QuaternionScaleOffsetTable[tableIndex * 2 + 1];
            }

            for (int i = 0; i < quatCount; i++)
            {
                int b = offset + i * 6; // 3 × uint16 = 6 bytes per quaternion
                if (b + 5 >= data.Length) break;
                ushort w0 = BitConverter.ToUInt16(data, b);
                ushort w1 = BitConverter.ToUInt16(data, b + 2);
                ushort w2 = BitConverter.ToUInt16(data, b + 4);

                // Bit 15 of word 0 = sign of the missing component
                bool missingIsNegative = (w0 & 0x8000) != 0;
                // 2-bit index: bit 1 from word 1 bit 15, bit 0 from word 2 bit 15
                int missingComponentIndex = ((w1 >> 14) & 0x2) | (w2 >> 15);

                // Extract 15-bit values (mask off top bits used for metadata)
                ushort[] rawCtrl = new ushort[] {
                    (ushort)(w0 & 0x7FFF),
                    (ushort)(w1 & 0x7FFF),
                    (ushort)(w2 & 0x7FFF)
                };

                // Dequantize and swizzle: write 3 source components starting at (missing+1) mod 4
                int dstComp = missingComponentIndex;
                float sumSq = 0f;
                float[] quat = new float[4];
                for (int src = 0; src < 3; src++)
                {
                    dstComp = (dstComp + 1) & 0x3;
                    float val = ctrlOffsets[dstComp] + ctrlScales[dstComp] * rawCtrl[src];
                    sumSq += val * val;
                    quat[dstComp] = val;
                }

                // Recover the missing (largest) component from unit quaternion constraint
                float missing = MathF.Sqrt(MathF.Max(0f, 1f - sumSq));
                if (missingIsNegative) missing = -missing;
                quat[missingComponentIndex] = missing;

                int r = i * 4;
                result[r]     = quat[0]; // X
                result[r + 1] = quat[1]; // Y
                result[r + 2] = quat[2]; // Z
                result[r + 3] = quat[3]; // W
            }

            // Ensure quaternion continuity: flip q[i] if dot(q[i-1], q[i]) < 0
            for (int i = 1; i < quatCount; i++)
            {
                int a = (i - 1) * 4, c = i * 4;
                float dot = result[a] * result[c] + result[a + 1] * result[c + 1]
                          + result[a + 2] * result[c + 2] + result[a + 3] * result[c + 3];
                if (dot < 0f)
                {
                    result[c]     = -result[c];
                    result[c + 1] = -result[c + 1];
                    result[c + 2] = -result[c + 2];
                    result[c + 3] = -result[c + 3];
                }
            }

            return result;
        }

        // Decodes D4nK8uC7u quantized quaternion controls (3 × uint8 per quaternion).
        // Same algorithm as D4nK16uC15u but with 7-bit controls instead of 15-bit.
        private static float[] DequantizeD4nK8uC7uControls(byte[] data, int offset, int count, ushort scaleOffsetTableEntries)
        {
            int quatCount = count / 3;
            var result = new float[quatCount * 4];

            float[] ctrlScales = new float[4];
            float[] ctrlOffsets = new float[4];
            ushort entry = scaleOffsetTableEntries;
            for (int dim = 0; dim < 4; dim++)
            {
                int tableIndex = entry & 0xF;
                entry >>= 4;
                ctrlScales[dim] = QuaternionScaleOffsetTable[tableIndex * 2] / 127f;
                ctrlOffsets[dim] = QuaternionScaleOffsetTable[tableIndex * 2 + 1];
            }

            for (int i = 0; i < quatCount; i++)
            {
                int b = offset + i * 3; // 3 bytes per quaternion
                if (b + 2 >= data.Length) break;
                byte w0 = data[b];
                byte w1 = data[b + 1];
                byte w2 = data[b + 2];

                bool missingIsNegative = (w0 & 0x80) != 0;
                int missingComponentIndex = ((w1 >> 6) & 0x2) | (w2 >> 7);

                byte[] rawCtrl = new byte[] {
                    (byte)(w0 & 0x7F),
                    (byte)(w1 & 0x7F),
                    (byte)(w2 & 0x7F)
                };

                int dstComp = missingComponentIndex;
                float sumSq = 0f;
                float[] quat = new float[4];
                for (int src = 0; src < 3; src++)
                {
                    dstComp = (dstComp + 1) & 0x3;
                    float val = ctrlOffsets[dstComp] + ctrlScales[dstComp] * rawCtrl[src];
                    sumSq += val * val;
                    quat[dstComp] = val;
                }

                float missing = MathF.Sqrt(MathF.Max(0f, 1f - sumSq));
                if (missingIsNegative) missing = -missing;
                quat[missingComponentIndex] = missing;

                int r = i * 4;
                result[r]     = quat[0];
                result[r + 1] = quat[1];
                result[r + 2] = quat[2];
                result[r + 3] = quat[3];
            }

            // Ensure quaternion continuity
            for (int i = 1; i < quatCount; i++)
            {
                int a = (i - 1) * 4, c = i * 4;
                float dot = result[a] * result[c] + result[a + 1] * result[c + 1]
                          + result[a + 2] * result[c + 2] + result[a + 3] * result[c + 3];
                if (dot < 0f)
                {
                    result[c]     = -result[c];
                    result[c + 1] = -result[c + 1];
                    result[c + 2] = -result[c + 2];
                    result[c + 3] = -result[c + 3];
                }
            }

            return result;
        }


        private void BuildKeyframes(GrannyTransformTrack tt)
        {
            var posCurve = tt.PositionCurve;
            var oriCurve = tt.OrientationCurve;
            var ssCurve = tt.ScaleShearCurve;

            bool posIsIdentity = posCurve == null || posCurve.IsIdentity;
            bool oriIsIdentity = oriCurve == null || oriCurve.IsIdentity;
            bool ssIsIdentity = ssCurve == null || ssCurve.IsIdentity;

            if (posIsIdentity && oriIsIdentity && ssIsIdentity)
                return;

            var times = new SortedSet<float>();
            CollectKnots(times, posCurve);
            CollectKnots(times, oriCurve);
            CollectKnots(times, ssCurve);

            if (times.Count == 0)
                times.Add(0f);

            foreach (float t in times)
            {
                var kf = new TransformKeyframe { Time = t };

                if (!posIsIdentity && posCurve.Controls.Length >= 3)
                    kf.Position = SampleCurveVec3(posCurve, t);

                if (!oriIsIdentity && oriCurve.Controls.Length >= 4)
                    kf.Orientation = SampleCurveQuat(oriCurve, t);

                if (!ssIsIdentity && ssCurve.Controls.Length >= 3)
                {
                    kf.Scale = SampleCurveScale(ssCurve, t);
                    if (ssCurve.Dimension == 9)
                        kf.ScaleShear9 = SampleCurveScaleFull9(ssCurve, t);
                }

                tt.Keyframes.Add(kf);
            }
        }

        private static void CollectKnots(SortedSet<float> times, GrannyCurveInfo curve)
        {
            if (curve == null || curve.Knots == null) return;
            foreach (float t in curve.Knots)
            {
                if (!float.IsNaN(t) && !float.IsInfinity(t))
                    times.Add(t);
            }
        }

        private static Vector3 SampleCurveVec3(GrannyCurveInfo curve, float t)
        {
            if (curve.Controls.Length < 3) return Vector3.Zero;

            if (curve.IsConstant || curve.Knots.Length == 0)
                return new Vector3(curve.Controls[0], curve.Controls[1], curve.Controls[2]);

            int dim = 3;
            int knotCount = curve.Knots.Length;
            if (curve.Controls.Length < knotCount * dim)
                return new Vector3(curve.Controls[0], curve.Controls.Length > 1 ? curve.Controls[1] : 0f, curve.Controls.Length > 2 ? curve.Controls[2] : 0f);

            if (t <= curve.Knots[0])
                return new Vector3(curve.Controls[0], curve.Controls[1], curve.Controls[2]);
            if (t >= curve.Knots[knotCount - 1])
            {
                int b = (knotCount - 1) * dim;
                return new Vector3(curve.Controls[b], curve.Controls[b + 1], curve.Controls[b + 2]);
            }

            for (int i = 0; i < knotCount - 1; i++)
            {
                if (t >= curve.Knots[i] && t <= curve.Knots[i + 1])
                {
                    float range = curve.Knots[i + 1] - curve.Knots[i];
                    float alpha = range > 0.00001f ? (t - curve.Knots[i]) / range : 0f;
                    int b0 = i * dim, b1 = (i + 1) * dim;
                    return new Vector3(
                        curve.Controls[b0] + (curve.Controls[b1] - curve.Controls[b0]) * alpha,
                        curve.Controls[b0 + 1] + (curve.Controls[b1 + 1] - curve.Controls[b0 + 1]) * alpha,
                        curve.Controls[b0 + 2] + (curve.Controls[b1 + 2] - curve.Controls[b0 + 2]) * alpha);
                }
            }
            return new Vector3(curve.Controls[0], curve.Controls[1], curve.Controls[2]);
        }

        private static Quaternion SampleCurveQuat(GrannyCurveInfo curve, float t)
        {
            if (curve.Controls.Length < 4) return Quaternion.Identity;

            if (curve.IsConstant || curve.Knots.Length == 0)
                return Quaternion.Normalize(new Quaternion(curve.Controls[0], curve.Controls[1], curve.Controls[2], curve.Controls[3]));

            int dim = 4;
            int knotCount = curve.Knots.Length;
            if (curve.Controls.Length < knotCount * dim)
                return Quaternion.Normalize(new Quaternion(curve.Controls[0], curve.Controls[1], curve.Controls[2], curve.Controls[3]));

            if (t <= curve.Knots[0])
                return Quaternion.Normalize(new Quaternion(curve.Controls[0], curve.Controls[1], curve.Controls[2], curve.Controls[3]));
            if (t >= curve.Knots[knotCount - 1])
            {
                int b = (knotCount - 1) * dim;
                return Quaternion.Normalize(new Quaternion(curve.Controls[b], curve.Controls[b + 1], curve.Controls[b + 2], curve.Controls[b + 3]));
            }

            for (int i = 0; i < knotCount - 1; i++)
            {
                if (t >= curve.Knots[i] && t <= curve.Knots[i + 1])
                {
                    float range = curve.Knots[i + 1] - curve.Knots[i];
                    float alpha = range > 0.00001f ? (t - curve.Knots[i]) / range : 0f;
                    int b0 = i * dim, b1 = (i + 1) * dim;
                    var q0 = Quaternion.Normalize(new Quaternion(curve.Controls[b0], curve.Controls[b0 + 1], curve.Controls[b0 + 2], curve.Controls[b0 + 3]));
                    var q1 = Quaternion.Normalize(new Quaternion(curve.Controls[b1], curve.Controls[b1 + 1], curve.Controls[b1 + 2], curve.Controls[b1 + 3]));
                    return Quaternion.Normalize(Quaternion.Slerp(q0, q1, alpha));
                }
            }
            return Quaternion.Normalize(new Quaternion(curve.Controls[0], curve.Controls[1], curve.Controls[2], curve.Controls[3]));
        }

        private static Vector3 SampleCurveScale(GrannyCurveInfo curve, float t)
        {
            if (curve.Controls.Length < 3) return Vector3.One;

            if (curve.IsConstant || curve.Knots.Length == 0)
            {
                if (curve.Dimension == 9 && curve.Controls.Length >= 9)
                    return new Vector3(curve.Controls[0], curve.Controls[4], curve.Controls[8]);
                return new Vector3(curve.Controls[0], curve.Controls[1], curve.Controls[2]);
            }

            int dim = curve.Dimension > 0 ? curve.Dimension : 3;
            int knotCount = curve.Knots.Length;

            if (curve.Controls.Length < knotCount * dim)
            {
                if (dim == 9 && curve.Controls.Length >= 9)
                    return new Vector3(curve.Controls[0], curve.Controls[4], curve.Controls[8]);
                return new Vector3(curve.Controls[0], curve.Controls.Length > 1 ? curve.Controls[1] : 1f, curve.Controls.Length > 2 ? curve.Controls[2] : 1f);
            }

            Vector3 ExtractScale(int baseIdx)
            {
                if (dim == 9 && baseIdx + 8 < curve.Controls.Length)
                    return new Vector3(curve.Controls[baseIdx], curve.Controls[baseIdx + 4], curve.Controls[baseIdx + 8]);
                return new Vector3(
                    curve.Controls[baseIdx],
                    baseIdx + 1 < curve.Controls.Length ? curve.Controls[baseIdx + 1] : 1f,
                    baseIdx + 2 < curve.Controls.Length ? curve.Controls[baseIdx + 2] : 1f);
            }

            if (t <= curve.Knots[0]) return ExtractScale(0);
            if (t >= curve.Knots[knotCount - 1]) return ExtractScale((knotCount - 1) * dim);

            for (int i = 0; i < knotCount - 1; i++)
            {
                if (t >= curve.Knots[i] && t <= curve.Knots[i + 1])
                {
                    float range = curve.Knots[i + 1] - curve.Knots[i];
                    float alpha = range > 0.00001f ? (t - curve.Knots[i]) / range : 0f;
                    return Vector3.Lerp(ExtractScale(i * dim), ExtractScale((i + 1) * dim), alpha);
                }
            }
            return ExtractScale(0);
        }

        // Returns all 9 floats (row-major 3×3) of a dim==9 scale/shear curve at time t.
        // Returns null if the curve is not dim==9 or lacks sufficient controls.
        private static float[] SampleCurveScaleFull9(GrannyCurveInfo curve, float t)
        {
            if (curve.Dimension != 9 || curve.Controls == null || curve.Controls.Length < 9)
                return null;

            float[] Extract9(int baseIdx)
            {
                if (baseIdx + 8 >= curve.Controls.Length) return null;
                return new float[]
                {
                    curve.Controls[baseIdx],     curve.Controls[baseIdx + 1], curve.Controls[baseIdx + 2],
                    curve.Controls[baseIdx + 3], curve.Controls[baseIdx + 4], curve.Controls[baseIdx + 5],
                    curve.Controls[baseIdx + 6], curve.Controls[baseIdx + 7], curve.Controls[baseIdx + 8]
                };
            }

            if (curve.IsConstant || curve.Knots == null || curve.Knots.Length == 0)
                return Extract9(0);

            int knotCount = curve.Knots.Length;
            if (curve.Controls.Length < knotCount * 9)
                return Extract9(0);

            if (t <= curve.Knots[0]) return Extract9(0);
            if (t >= curve.Knots[knotCount - 1]) return Extract9((knotCount - 1) * 9);

            for (int i = 0; i < knotCount - 1; i++)
            {
                if (t >= curve.Knots[i] && t <= curve.Knots[i + 1])
                {
                    float range = curve.Knots[i + 1] - curve.Knots[i];
                    float alpha = range > 0.00001f ? (t - curve.Knots[i]) / range : 0f;
                    var a = Extract9(i * 9);
                    var b = Extract9((i + 1) * 9);
                    if (a == null || b == null) return a ?? b;
                    var result = new float[9];
                    for (int j = 0; j < 9; j++)
                        result[j] = a[j] + (b[j] - a[j]) * alpha;
                    return result;
                }
            }
            return Extract9(0);
        }

        //  GSF: Animation Slots / Sets

        private void ParseAnimSlots(GrannyFileData result, GrannyContainer c,
                                     List<GrannyTypeDef> types, int secIdx, int offset, int count)
        {
            result.CharacterInfo ??= new GsfCharacterInfo();
            int sz = CalculateStructSize(types);
            if (sz <= 0) sz = 8;
            var data = SecData(c, secIdx);
            for (int i = 0; i < count; i++)
            {
                int pos = offset + i * sz;
                if (!CanRead(data, pos, Math.Min(sz, 4))) break;
                var slot = new GsfAnimationSlot();
                foreach (var td in types)
                {
                    switch (td.Name)
                    {
                        case "AnimSlotName":
                            slot.Name = ResolveString(c, secIdx, pos);
                            pos += GetMemberSize(td);
                            break;
                        case "AnimSlotIndex":
                            slot.Index = RI32(data, pos); pos += 4; break;
                        default:
                            pos += GetMemberSize(td); break;
                    }
                }
                result.CharacterInfo.AnimationSlots.Add(slot);
            }
        }

        private void ParseAnimSets(GrannyFileData result, GrannyContainer c,
                                    List<GrannyTypeDef> types, int secIdx, int offset, int count)
        {
            result.CharacterInfo ??= new GsfCharacterInfo();
            int sz = CalculateStructSize(types);
            if (sz <= 0) sz = 24;
            var data = SecData(c, secIdx);
            for (int i = 0; i < count; i++)
            {
                int startPos = offset + i * sz;
                if (!CanRead(data, startPos, Math.Min(sz, 4))) break;
                int pos = startPos;
                var set = new GsfAnimationSet();
                foreach (var td in types)
                {
                    switch (td.Name)
                    {
                        case "Name":
                            set.Name = ResolveString(c, secIdx, pos);
                            pos += GetMemberSize(td);
                            break;
                        case "SourceFileReferences":
                            if (td.Type == GrannyMemberType.ReferenceToArray ||
                                td.Type == GrannyMemberType.ArrayOfReferences)
                            {
                                int n = RI32(data, pos);
                                var tp = ResolvePointer(c, secIdx, pos + 4);
                                if (tp.Section >= 0 && n > 0 && n < 100_000)
                                {
                                    int esz = CalculateStructSize(td.Children);
                                    if (esz <= 0) esz = 12;
                                    var sfrData = SecData(c, tp.Section);
                                    for (int k = 0; k < n; k++)
                                    {
                                        int rp = tp.Offset + k * esz;
                                        if (!CanRead(sfrData, rp, Math.Min(esz, 4))) break;
                                        var sfr = new GsfSourceFileRef();
                                        int rpos = rp;
                                        foreach (var rtd in td.Children)
                                        {
                                            switch (rtd.Name)
                                            {
                                                case "SourceFilename":
                                                    sfr.SourceFilename = ResolveString(c, tp.Section, rpos);
                                                    rpos += GetMemberSize(rtd);
                                                    break;
                                                case "ExpectedAnimCount":
                                                    sfr.ExpectedAnimCount = RI32(sfrData, rpos);
                                                    rpos += 4;
                                                    break;
                                                case "AnimCRC":
                                                    sfr.AnimCRC = RU32(sfrData, rpos);
                                                    rpos += 4;
                                                    break;
                                                default:
                                                    rpos += GetMemberSize(rtd);
                                                    break;
                                            }
                                        }
                                        set.SourceFileReferences.Add(sfr);
                                    }
                                }
                                pos += GetMemberSize(td);
                            }
                            else pos += GetMemberSize(td);
                            break;
                        case "AnimationSpecs":
                            if (td.Type == GrannyMemberType.ReferenceToArray)
                            {
                                int n = RI32(data, pos);
                                var tp = ResolvePointer(c, secIdx, pos + 4);
                                if (tp.Section >= 0 && n > 0 && n < 100_000)
                                {
                                    int esz = CalculateStructSize(td.Children);
                                    if (esz <= 0) esz = 8;
                                    var specData = SecData(c, tp.Section);
                                    for (int k = 0; k < n; k++)
                                    {
                                        int sp = tp.Offset + k * esz;
                                        if (!CanRead(specData, sp, Math.Min(esz, 4))) break;
                                        var spec = new GsfAnimationSpec();
                                        int spos = sp;
                                        foreach (var std in td.Children)
                                        {
                                            switch (std.Name)
                                            {
                                                case "AnimationIndex":
                                                    spec.AnimationIndex = RI32(specData, spos);
                                                    spos += 4;
                                                    break;
                                                case "ExpectedName":
                                                    spec.ExpectedName = ResolveString(c, tp.Section, spos);
                                                    spos += GetMemberSize(std);
                                                    break;
                                                default:
                                                    spos += GetMemberSize(std);
                                                    break;
                                            }
                                        }
                                        set.AnimationSpecs.Add(spec);
                                    }
                                }
                                pos += GetMemberSize(td);
                            }
                            else pos += GetMemberSize(td);
                            break;
                        default:
                            pos += GetMemberSize(td);
                            break;
                    }
                }
                result.CharacterInfo.AnimationSets.Add(set);
            }
        }

        //  World transform computation

        private void ComputeWorldTransforms(GrannySkeleton skeleton)
        {
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                var bone = skeleton.Bones[i];
                var local = bone.LocalTransform.ToMatrix();
                bone.WorldTransform = (bone.ParentIndex >= 0 && bone.ParentIndex < i)
                    ? local * skeleton.Bones[bone.ParentIndex].WorldTransform
                    : local;
            }
        }

        //  Container reader

        private GrannyContainer ReadContainer(byte[] raw)
        {
            var c = new GrannyContainer { RawFileBytes = raw };
            int fileLen = raw.Length;

            if (fileLen < 32)
                throw new InvalidDataException("File too small for magic block.");

            Array.Copy(raw, 0, c.MagicBytes, 0, 16);
            c.HeaderSize     = BitConverter.ToUInt32(raw, 16);
            c.HeaderFormat   = BitConverter.ToUInt32(raw, 20);
            c.MagicReserved[0] = BitConverter.ToUInt32(raw, 24);
            c.MagicReserved[1] = BitConverter.ToUInt32(raw, 28);

            var (isBE, ptrSize) = IdentifyMagicFull(c.MagicBytes);
            c.IsBigEndian = isBE;
            c.PointerSize = ptrSize;

            // For "Old" and legacy v5 magic files, pointer size is in HeaderFormat bit 1:
            //   bit 1 set → 32-bit pointers; bit 1 clear → 64-bit pointers.
            // Xbox One / Durango GR2 files use Magic_Old with 64-bit pointers.
            if (BytesMatch(c.MagicBytes, Magic_Old) || BytesMatch(c.MagicBytes, Magic_v5_Old))
            {
                uint hf = isBE ? BSwap32(c.HeaderFormat) : c.HeaderFormat;
                c.PointerSize = (hf & 0x2u) != 0 ? 4 : 8;
            }

            const int MagicSize = 32;
            const int FullHeaderSize = 72;

            // Always read FullHeaderSize bytes starting at raw[MagicSize] so all GrnFileInfo
            // fields (RootObjOffset, TypeTag, etc.) are captured.
            // Note: c.HeaderSize encodes the 32-byte magic block size, not the extended header.
            int onDiskHeaderSize = FullHeaderSize;

            int availableFromHeader = fileLen - MagicSize;
            if (availableFromHeader <= 0)
                throw new InvalidDataException("File too small for header.");

            byte[] hdr = new byte[FullHeaderSize];
            Array.Copy(raw, MagicSize, hdr, 0, Math.Min(Math.Min(onDiskHeaderSize, availableFromHeader), FullHeaderSize));

            if (c.IsBigEndian)
                BSwapWords(hdr);

            c.Version            = BitConverter.ToUInt32(hdr, 0x00);
            c.TotalSize          = BitConverter.ToUInt32(hdr, 0x04);
            c.CRC                = BitConverter.ToUInt32(hdr, 0x08);
            c.SectionArrayOffset = BitConverter.ToUInt32(hdr, 0x0C);
            c.SectionArrayCount  = BitConverter.ToUInt32(hdr, 0x10);
            c.RootTypeSectionIndex = BitConverter.ToUInt32(hdr, 0x14);
            c.RootTypeOffset       = BitConverter.ToUInt32(hdr, 0x18);
            c.RootObjSectionIndex  = BitConverter.ToUInt32(hdr, 0x1C);
            c.RootObjOffset        = BitConverter.ToUInt32(hdr, 0x20);
            c.TypeTag            = BitConverter.ToUInt32(hdr, 0x24);
            for (int i = 0; i < 4; i++) c.ExtraTags[i] = BitConverter.ToUInt32(hdr, 0x28 + i * 4);
            c.StringDatabaseCRC  = BitConverter.ToUInt32(hdr, 0x38);
            for (int i = 0; i < 3; i++) c.ReservedUnused[i] = BitConverter.ToUInt32(hdr, 0x3C + i * 4);

            if (c.SectionArrayCount > 64)
                throw new InvalidDataException($"Implausible section count: {c.SectionArrayCount}");

            if (c.SectionArrayCount == 0)
                return c;

            long sectionFilePos = MagicSize + (long)c.SectionArrayOffset;
            const int SectionHeaderSize = 44;
            long sectionArrayEnd = sectionFilePos + (long)c.SectionArrayCount * SectionHeaderSize;
            if (sectionFilePos < 0 || sectionFilePos >= fileLen || sectionArrayEnd > fileLen)
                throw new InvalidDataException(
                    $"Section array at 0x{sectionFilePos:X} ({c.SectionArrayCount} × 44 bytes) " +
                    $"exceeds file bounds ({fileLen} bytes).");

            int pos = (int)sectionFilePos;
            for (int i = 0; i < (int)c.SectionArrayCount; i++)
            {
                var sec = new GrannySectionRaw
                {
                    Format               = BitConverter.ToUInt32(raw, pos + 0),
                    DataOffset           = BitConverter.ToUInt32(raw, pos + 4),
                    DataSize             = BitConverter.ToUInt32(raw, pos + 8),
                    ExpandedDataSize     = BitConverter.ToUInt32(raw, pos + 12),
                    InternalAlignment    = BitConverter.ToUInt32(raw, pos + 16),
                    First16Bit           = BitConverter.ToUInt32(raw, pos + 20),
                    First8Bit            = BitConverter.ToUInt32(raw, pos + 24),
                    PointerFixupOffset   = BitConverter.ToUInt32(raw, pos + 28),
                    PointerFixupCount    = BitConverter.ToUInt32(raw, pos + 32),
                    MixedMarshallingOffset = BitConverter.ToUInt32(raw, pos + 36),
                    MixedMarshallingCount  = BitConverter.ToUInt32(raw, pos + 40),
                };
                if (c.IsBigEndian) BSwapSection(sec);
                c.Sections.Add(sec);
                pos += SectionHeaderSize;
            }

            for (int i = 0; i < c.Sections.Count; i++)
            {
                var sec = c.Sections[i];
                byte[] decompressed = ReadSectionData(raw, sec, c.IsBigEndian);
                c.SectionData.Add(decompressed);
            }

            for (int i = 0; i < c.Sections.Count; i++)
            {
                var sec = c.Sections[i];
                var dict = new Dictionary<uint, GrannyFixup>();
                if (sec.PointerFixupCount > 0 && sec.PointerFixupOffset > 0)
                {
                    long fixEnd = (long)sec.PointerFixupOffset + (long)sec.PointerFixupCount * 12;
                    if (sec.PointerFixupOffset < (uint)fileLen && fixEnd <= fileLen)
                    {
                        int fp = (int)sec.PointerFixupOffset;
                        for (uint k = 0; k < sec.PointerFixupCount; k++, fp += 12)
                        {
                            uint fromOffset     = BitConverter.ToUInt32(raw, fp);
                            uint toSectionIndex = BitConverter.ToUInt32(raw, fp + 4);
                            uint toOffset       = BitConverter.ToUInt32(raw, fp + 8);
                            if (c.IsBigEndian)
                            {
                                fromOffset     = BSwap32(fromOffset);
                                toSectionIndex = BSwap32(toSectionIndex);
                                toOffset       = BSwap32(toOffset);
                            }
                            dict[fromOffset] = new GrannyFixup
                            {
                                FromOffset     = fromOffset,
                                ToSectionIndex = toSectionIndex,
                                ToOffset       = toOffset,
                            };
                        }
                    }
                }
                c.Fixups[i] = dict;
            }

            for (int i = 0; i < c.Sections.Count; i++)
            {
                var sec = c.Sections[i];
                var list = new List<GrannyMarshalling>();
                if (sec.MixedMarshallingCount > 0 && sec.MixedMarshallingOffset > 0)
                {
                    long mEnd = (long)sec.MixedMarshallingOffset + (long)sec.MixedMarshallingCount * 16;
                    if (sec.MixedMarshallingOffset < (uint)fileLen && mEnd <= fileLen)
                    {
                        int mp = (int)sec.MixedMarshallingOffset;
                        for (uint k = 0; k < sec.MixedMarshallingCount; k++, mp += 16)
                        {
                            uint count            = BitConverter.ToUInt32(raw, mp);
                            uint dataOffset       = BitConverter.ToUInt32(raw, mp + 4);
                            uint typeSectionIndex = BitConverter.ToUInt32(raw, mp + 8);
                            uint typeOffset       = BitConverter.ToUInt32(raw, mp + 12);
                            if (c.IsBigEndian)
                            {
                                count            = BSwap32(count);
                                dataOffset       = BSwap32(dataOffset);
                                typeSectionIndex = BSwap32(typeSectionIndex);
                                typeOffset       = BSwap32(typeOffset);
                            }
                            list.Add(new GrannyMarshalling
                            {
                                Count            = count,
                                DataOffset       = dataOffset,
                                TypeSectionIndex = typeSectionIndex,
                                TypeOffset       = typeOffset,
                            });
                        }
                    }
                }
                c.Marshallings[i] = list;
            }

            return c;
        }
    }
}
