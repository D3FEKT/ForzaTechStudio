using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Syroot.BinaryData;

namespace ForzaTechStudio.Services
{
    // Parser for Forza LightPresets.bin files (LDFB format).
    // Supports V1 (FH3/FH4) and V2 (FH5) layouts.
    public class LightPresetsBinParser
    {
        private const uint FourCC_LDFB = 0x4246444C; // "LDFB" little-endian


        public enum ParameterId : byte
        {
            // -- Light (ForwardPlus::LightPresetDesc) --
            LightEnabled              = 0x00,
            LightType                 = 0x01,
            LightColour               = 0x02,
            LightRange                = 0x03,
            LightIntensity            = 0x04,
            LightPenumbraAngle        = 0x05,
            LightConeAngle            = 0x06,
            LightAttenuationProfile   = 0x07,
            LightActivationThreshold  = 0x08,
            LightActivationTransition = 0x09,
            LightShadowType           = 0x0A,
            LightProjectedTexture     = 0x0B,

            // -- Glow enable --
            GlowEnabled               = 0x0C,

            // -- Glow (LightGlowManager::PresetDesc) --
            GlowSize                  = 0x0D,
            GlowZoomCompensation      = 0x0E,
            GlowPerspectiveCompensation = 0x0F,
            GlowBrightness            = 0x10,
            GlowColour                = 0x11,
            GlowPrimaryBrightness     = 0x12,
            GlowPrimaryFalloffStart   = 0x13,
            GlowPrimaryFalloffEnd     = 0x14,
            GlowSecondaryBrightness   = 0x15,
            GlowSecondaryFalloffStart = 0x16,
            GlowSecondaryFalloffEnd   = 0x17,
            GlowOcclusionFalloffPower = 0x18,
            GlowActivationThreshold   = 0x19,
            GlowActivationTransition  = 0x1A,

            // -- Glow sub-element: Glow (ElementGlowDesc extends ElementCommonDesc) --
            GlowGlowBrightness             = 0x1B,
            GlowGlowColourContribution      = 0x1C,
            GlowGlowColour                  = 0x1D,
            GlowGlowRelativeSize            = 0x1E,
            GlowGlowGamma                   = 0x1F,
            GlowGlowBrightnessFalloffProfile = 0x20,
            GlowGlowBrightnessFalloffStart  = 0x21,
            GlowGlowBrightnessFalloffEnd    = 0x22,
            GlowGlowPeakBrightness          = 0x23,
            GlowGlowScreenSizePeakLimit     = 0x24,
            GlowGlowThreshold               = 0x25,

            // -- Glow sub-element: Flare (ElementFlareDesc extends ElementCommonDesc) --
            GlowFlareBrightness             = 0x26,
            GlowFlareColourContribution     = 0x27,
            GlowFlareColour                 = 0x28,
            GlowFlareRelativeSize           = 0x29,
            GlowFlareGamma                  = 0x2A,
            GlowFlareBrightnessFalloffProfile = 0x2B,
            GlowFlareBrightnessFalloffStart = 0x2C,
            GlowFlareBrightnessFalloffEnd   = 0x2D,
            GlowFlareSizeClip               = 0x2E,

            // -- Glow sub-element: Streak (ElementStreakDesc extends ElementCommonDesc) --
            GlowStreakBrightness             = 0x2F,
            GlowStreakColourContribution     = 0x30,
            GlowStreakColour                 = 0x31,
            GlowStreakRelativeSize           = 0x32,
            GlowStreakGamma                  = 0x33,
            GlowStreakBrightnessFalloffProfile = 0x34,
            GlowStreakBrightnessFalloffStart = 0x35,
            GlowStreakBrightnessFalloffEnd   = 0x36,
            GlowStreakAspectY                = 0x37,
            GlowStreakOrientation            = 0x38,

            // -- Glow occlusion (OcclusionDesc) --
            GlowOcclusionConeAngle     = 0x39,
            GlowOcclusionRadius        = 0x3A,
            GlowOcclusionPushOut       = 0x3B,
            GlowOcclusionDistantPushOut = 0x3C,

            // -- Glow distance fade --
            GlowDistanceFadeStart      = 0x3D,
            GlowDistanceFadeEnd        = 0x3E,
            GlowDistanceFadeAmount     = 0x3F,

            // -- Flare extra --
            GlowFlareSpread            = 0x40,
            GlowFlareMinBrightness     = 0x41,
            GlowFlareMaxBrightness     = 0x42,
            GlowFlareMinLength         = 0x43,
            GlowFlareMaxLength         = 0x44,

            // -- Cinematic flags --
            LightCinematicsOnly        = 0x45,
            LightNonCinematics         = 0x46,
            GlowCubeMapOnly            = 0x47,

            // -- Flare rotation --
            GlowFlareRotationSpeed     = 0x48,
        }

        // ForwardPlus::LightType::Enum values.
        public enum LightTypeEnum : uint
        {
            Spot         = 1,
            PointAmbient = 2,
            SpotSimple   = 3,
        }

        // ForwardPlus::LightShadowType::Enum values.
        public enum LightShadowTypeEnum : uint
        {
            ShadowOnly      = 3,
            DynamicNoShadow = 4,
        }


        public enum ParameterDataType
        {
            Bool,     // 1 byte  (0/1)
            Float,    // 4 bytes (IEEE 754 single)
            UInt32,   // 4 bytes (enum/index)
            Vec3,     // 12 bytes (XMFLOAT3: 3 � float)
            String,   // variable length, null-terminated char[]
            Raw,      // unknown / fallback
        }



        public class LightPresetsData
        {
            public uint Version { get; set; }
            public uint PresetCount { get; set; }
            // Original file size from the header (m_Size). Used for round-trip.
            public uint FileSize { get; set; }
            // V2 only: preset IDs / hashes, one per preset, in order.
            public List<uint> PresetIds { get; set; } = new();
            public List<LightPreset> Presets { get; set; } = new();
        }

        public class LightPreset
        {
            // V2 preset hash/ID (0 for V1 files).
            public uint Id { get; set; }
            public uint PresetSize { get; set; }
            public List<LightParameter> Parameters { get; set; } = new();
            // Raw bytes of this entire preset block, starting at the 8-byte header
            // (PresetSize + ParameterCount) through to blockStart + PresetSize.
            // Captured verbatim so callers can round-trip or dump the exact wire bytes.
            public byte[] RawBlockBytes { get; set; } = Array.Empty<byte>();
        }

        public class LightParameter
        {
            public byte Id { get; set; }
            public byte Length { get; set; }
            // Raw payload bytes (always preserved for round-trip).
            public byte[] Value { get; set; } = Array.Empty<byte>();

            // ?? Strongly-typed accessors ??

            // Returns the <see cref="ParameterId"/> enum if the ID is in range, otherwise null.
            public ParameterId? ParameterIdEnum =>
                Enum.IsDefined(typeof(ParameterId), Id) ? (ParameterId)Id : null;

            // Expected data type for this parameter's ID.
            public ParameterDataType DataType => GetDataType(Id);


            public string Name => GetParameterName(Id);

            // Read value as bool (for 1-byte bool parameters).
            public bool? AsBool => Length == 1 && Value.Length >= 1 ? Value[0] != 0 : null;

            // Read value as float (for 4-byte float parameters).
            public float? AsFloat => Length == 4 && Value.Length >= 4
                ? BitConverter.ToSingle(Value, 0) : null;

            // Read value as uint32 (for 4-byte int/enum parameters).
            public uint? AsUInt32 => Length == 4 && Value.Length >= 4
                ? BitConverter.ToUInt32(Value, 0) : null;

            // Read value as Vec3 / XMFLOAT3 (for 12-byte colour/direction parameters).
            public Vector3? AsVec3 => Length == 12 && Value.Length >= 12
                ? new Vector3(
                    BitConverter.ToSingle(Value, 0),
                    BitConverter.ToSingle(Value, 4),
                    BitConverter.ToSingle(Value, 8))
                : null;

            // Read value as string (for null-terminated char[] parameters like projected texture).
            public string AsString
            {
                get
                {
                    if (Length == 0 || Value.Length == 0)
                        return null;
                    // Trim trailing null bytes
                    int end = Array.IndexOf(Value, (byte)0);
                    int count = end >= 0 ? end : Value.Length;
                    return count > 0 ? Encoding.ASCII.GetString(Value, 0, count) : string.Empty;
                }
            }

            // Returns a human-readable formatted string of the value, suitable for display.
            public string DisplayValue
            {
                get
                {
                    if (Length == 0)
                        return "<default>";

                    var dt = DataType;
                    switch (dt)
                    {
                        case ParameterDataType.Bool:
                            return AsBool == true ? "true" : "false";

                        case ParameterDataType.UInt32:
                            uint? uval = AsUInt32;
                            if (uval == null) break;
                            // Resolve known enums
                            if (Id == (byte)ParameterId.LightType)
                                return Enum.IsDefined(typeof(LightTypeEnum), uval.Value)
                                    ? $"{(LightTypeEnum)uval.Value} ({uval.Value})"
                                    : uval.Value.ToString();
                            if (Id == (byte)ParameterId.LightShadowType)
                                return Enum.IsDefined(typeof(LightShadowTypeEnum), uval.Value)
                                    ? $"{(LightShadowTypeEnum)uval.Value} ({uval.Value})"
                                    : uval.Value.ToString();
                            return uval.Value.ToString();

                        case ParameterDataType.Float:
                            return AsFloat?.ToString("F4") ?? $"0x{BitConverter.ToString(Value).Replace("-", "")}";

                        case ParameterDataType.Vec3:
                            var v = AsVec3;
                            return v != null
                                ? $"({v.Value.X:F3}, {v.Value.Y:F3}, {v.Value.Z:F3})"
                                : $"0x{BitConverter.ToString(Value).Replace("-", "")}";

                        case ParameterDataType.String:
                            return AsString ?? string.Empty;
                    }

                    // Raw fallback
                    return $"<raw len={Length}> {BitConverter.ToString(Value).Replace("-", " ")}";
                }
            }

            // ?? Mutators (write back to Value byte array) ??

            // Set a bool value (1 byte).
            public void SetBool(bool val) { Value = [(byte)(val ? 1 : 0)]; Length = 1; }

            // Set a float value (4 bytes LE).
            public void SetFloat(float val) { Value = BitConverter.GetBytes(val); Length = 4; }

            // Set a uint32 value (4 bytes LE).
            public void SetUInt32(uint val) { Value = BitConverter.GetBytes(val); Length = 4; }

            // Set a Vec3 / XMFLOAT3 value (12 bytes: 3 � float LE).
            public void SetVec3(Vector3 val)
            {
                Value = new byte[12];
                Buffer.BlockCopy(BitConverter.GetBytes(val.X), 0, Value, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(val.Y), 0, Value, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(val.Z), 0, Value, 8, 4);
                Length = 12;
            }

            // Set a null-terminated string value.
            public void SetString(string val)
            {
                string s = val ?? string.Empty;
                // Include null terminator as the engine expects for projected texture strings
                byte[] strBytes = Encoding.ASCII.GetBytes(s);
                Value = new byte[strBytes.Length + 1]; // +1 for null terminator
                Buffer.BlockCopy(strBytes, 0, Value, 0, strBytes.Length);
                // Value[^1] is already 0
                Length = (byte)Value.Length;
            }
        }

        // ?????????????????????????????????????????????????????????????????????
        //  Parameter metadata lookups
        // ?????????????????????????????????????????????????????????????????????

        // Returns the expected <see cref="ParameterDataType"/> for a given parameter ID byte.
        // Matches the type classification from the 010 Editor template.
        public static ParameterDataType GetDataType(byte id)
        {
            return id switch
            {
                // Bool IDs (1 byte)
                0x00 or 0x0C or 0x45 or 0x46 or 0x47 => ParameterDataType.Bool,

                // UInt32 / enum IDs (4 bytes as integer)
                0x01 or 0x07 or 0x0A or 0x38 => ParameterDataType.UInt32,

                // Vec3 colour IDs (12 bytes: XMFLOAT3)
                0x02 or 0x11 or 0x1D or 0x28 or 0x31 => ParameterDataType.Vec3,

                // String (variable length, null-terminated)
                0x0B => ParameterDataType.String,

                // All other known IDs in range 0x03�0x48 are float (4 bytes)
                >= 0x03 and <= 0x48 => ParameterDataType.Float,

                // Unknown / out of range
                _ => ParameterDataType.Raw,
            };
        }


        public static string GetParameterName(byte id)
        {
            return id switch
            {
                0x00 => "LightEnabled",
                0x01 => "Light.Type",
                0x02 => "Light.Colour",
                0x03 => "Light.Range",
                0x04 => "Light.Intensity",
                0x05 => "Light.PenumbraAngle",
                0x06 => "Light.ConeAngle",
                0x07 => "Light.AttenuationProfile",
                0x08 => "Light.ActivationThreshold",
                0x09 => "Light.ActivationTransition",
                0x0A => "Light.ShadowType",
                0x0B => "Light.ProjectedTexture",
                0x0C => "GlowEnabled",
                0x0D => "Glow.Size",
                0x0E => "Glow.ZoomCompensation",
                0x0F => "Glow.PerspectiveCompensation",
                0x10 => "Glow.Brightness",
                0x11 => "Glow.Colour",
                0x12 => "Glow.PrimaryBrightness",
                0x13 => "Glow.PrimaryFalloffStart",
                0x14 => "Glow.PrimaryFalloffEnd",
                0x15 => "Glow.SecondaryBrightness",
                0x16 => "Glow.SecondaryFalloffStart",
                0x17 => "Glow.SecondaryFalloffEnd",
                0x18 => "Glow.OcclusionFalloffPower",
                0x19 => "Glow.ActivationThreshold",
                0x1A => "Glow.ActivationTransition",
                0x1B => "Glow.Glow.Brightness",
                0x1C => "Glow.Glow.ColourContribution",
                0x1D => "Glow.Glow.Colour",
                0x1E => "Glow.Glow.RelativeSize",
                0x1F => "Glow.Glow.Gamma",
                0x20 => "Glow.Glow.BrightnessFalloffProfile",
                0x21 => "Glow.Glow.BrightnessFalloffStart",
                0x22 => "Glow.Glow.BrightnessFalloffEnd",
                0x23 => "Glow.Glow.PeakBrightness",
                0x24 => "Glow.Glow.ScreenSizePeakLimit",
                0x25 => "Glow.Glow.Threshold",
                0x26 => "Glow.Flare.Brightness",
                0x27 => "Glow.Flare.ColourContribution",
                0x28 => "Glow.Flare.Colour",
                0x29 => "Glow.Flare.RelativeSize",
                0x2A => "Glow.Flare.Gamma",
                0x2B => "Glow.Flare.BrightnessFalloffProfile",
                0x2C => "Glow.Flare.BrightnessFalloffStart",
                0x2D => "Glow.Flare.BrightnessFalloffEnd",
                0x2E => "Glow.Flare.SizeClip",
                0x2F => "Glow.Streak.Brightness",
                0x30 => "Glow.Streak.ColourContribution",
                0x31 => "Glow.Streak.Colour",
                0x32 => "Glow.Streak.RelativeSize",
                0x33 => "Glow.Streak.Gamma",
                0x34 => "Glow.Streak.BrightnessFalloffProfile",
                0x35 => "Glow.Streak.BrightnessFalloffStart",
                0x36 => "Glow.Streak.BrightnessFalloffEnd",
                0x37 => "Glow.Streak.AspectY",
                0x38 => "Glow.Streak.Orientation",
                0x39 => "Glow.Occlusion.ConeAngle",
                0x3A => "Glow.Occlusion.Radius",
                0x3B => "Glow.Occlusion.PushOut",
                0x3C => "Glow.Occlusion.DistantPushOut",
                0x3D => "Glow.DistanceFadeStart",
                0x3E => "Glow.DistanceFadeEnd",
                0x3F => "Glow.DistanceFadeAmount",
                0x40 => "Glow.Flare.Spread",
                0x41 => "Glow.Flare.MinBrightness",
                0x42 => "Glow.Flare.MaxBrightness",
                0x43 => "Glow.Flare.MinLength",
                0x44 => "Glow.Flare.MaxLength",
                0x45 => "Light.CinematicsOnly",
                0x46 => "Light.NonCinematics",
                0x47 => "Glow.CubeMapOnly",
                0x48 => "Glow.Flare.RotationSpeed",
                _ => $"UNKNOWN_0x{id:X2}",
            };
        }

        // Returns the tooltip description for a parameter ID.
        public static string GetParameterDescription(byte id)
        {
            return id switch
            {
                0x00 => "Enable/disable the light",
                0x01 => "1=Spot | 2=PointAmbient | 3=SpotSimple",
                0x02 => "RGB linear float",
                0x03 => "Attenuation range (world units)",
                0x04 => "Luminous intensity multiplier",
                0x05 => "Spot penumbra angle (radians)",
                0x06 => "Spot cone half-angle (radians); engine clamps to < 675.0",
                0x07 => "Index into attenuation curve table",
                0x08 => "Camera distance at which light activates",
                0x09 => "Blend distance for activation fade",
                0x0A => "3=ShadowOnly | 4=DynamicNoShadow",
                0x0B => "Null-terminated texture asset path string",
                0x0C => "Enable/disable the lens-glow effect",
                0x0D => "Base screen-space size",
                0x0E => "FOV zoom compensation factor",
                0x0F => "Perspective size compensation",
                0x10 => "Master glow brightness",
                0x11 => "RGB glow tint",
                0x12 => "Primary halo brightness",
                0x13 => "Primary halo falloff start distance",
                0x14 => "Primary halo falloff end distance",
                0x15 => "Secondary halo brightness",
                0x16 => "Secondary halo falloff start",
                0x17 => "Secondary halo falloff end",
                0x18 => "Occlusion power curve exponent",
                0x19 => "Camera distance activation threshold",
                0x1A => "Activation blend distance",
                0x1B => "Glow Brightness",
                0x1C => "How much the light colour tints the glow",
                0x1D => "RGB override colour",
                0x1E => "Size relative to parent glow",
                0x1F => "Gamma curve",
                0x20 => "Falloff curve profile index",
                0x21 => "BrightnessFalloffStart",
                0x22 => "BrightnessFalloffEnd",
                0x23 => "Glow-specific: peak brightness cap",
                0x24 => "Peak brightness screen-size limit",
                0x25 => "Visibility threshold",
                0x26 => "Flare Brightness",
                0x27 => "Flare ColourContribution",
                0x28 => "RGB flare tint",
                0x29 => "Flare RelativeSize",
                0x2A => "Flare Gamma",
                0x2B => "Flare BrightnessFalloffProfile",
                0x2C => "Flare BrightnessFalloffStart",
                0x2D => "Flare BrightnessFalloffEnd",
                0x2E => "Size clipping threshold",
                0x2F => "Streak Brightness",
                0x30 => "Streak ColourContribution",
                0x31 => "RGB streak tint",
                0x32 => "Streak RelativeSize",
                0x33 => "Streak Gamma",
                0x34 => "Streak BrightnessFalloffProfile",
                0x35 => "Streak BrightnessFalloffStart",
                0x36 => "Streak BrightnessFalloffEnd",
                0x37 => "Streak vertical aspect ratio",
                0x38 => "Raw enum (size=4)",
                0x39 => "Ray-cast cone half-angle",
                0x3A => "Occlusion test sphere radius",
                0x3B => "Push-out distance from surface",
                0x3C => "Distant push-out distance",
                0x3D => "Start of distance fade range",
                0x3E => "End of distance fade range",
                0x3F => "Amount of brightness fade at end",
                0x40 => "Default: 1.0 (set in constructor)",
                0x41 => "Flare MinBrightness",
                0x42 => "Default: 1.0",
                0x43 => "Flare MinLength",
                0x44 => "Default: 1.0",
                0x45 => "Also sets Glow.CinematicsOnly simultaneously",
                0x46 => "Light visible in non-cinematic gameplay",
                0x47 => "Glow visible only in cube map reflections",
                0x48 => "Lens-flare rotation speed (rad/s)",
                _ => "Unknown parameter",
            };
        }

        // ?????????????????????????????????????????????????????????????????????
        //  Parse
        // ?????????????????????????????????????????????????????????????????????

        public LightPresetsData Parse(Stream stream)
        {
            var data = new LightPresetsData();

            using var bs = new BinaryStream(stream, ByteConverter.Little, leaveOpen: true);

            // LightFinalDataHeader (16 bytes)
            uint fourCC = bs.ReadUInt32();
            if (fourCC != FourCC_LDFB)
                throw new InvalidDataException(
                    $"Bad magic: expected LDFB (0x{FourCC_LDFB:X8}), got 0x{fourCC:X8}");

            data.FileSize = bs.ReadUInt32();
            data.Version = bs.ReadUInt32();
            data.PresetCount = bs.ReadUInt32();

            // V2 ID table
            if (data.Version >= 2)
            {
                for (uint i = 0; i < data.PresetCount; i++)
                    data.PresetIds.Add(bs.ReadUInt32());
            }

            // Preset entries
            for (uint i = 0; i < data.PresetCount; i++)
            {
                long blockStart = stream.Position;

                uint presetSize = bs.ReadUInt32();
                uint paramCount = bs.ReadUInt32();

                var preset = new LightPreset
                {
                    Id = data.Version >= 2 && i < (uint)data.PresetIds.Count
                        ? data.PresetIds[(int)i]
                        : 0,
                    PresetSize = presetSize
                };

                for (uint p = 0; p < paramCount; p++)
                {
                    byte paramId = bs.Read1Byte();
                    byte paramLen = bs.Read1Byte();
                    byte[] paramValue = paramLen > 0 ? bs.ReadBytes(paramLen) : Array.Empty<byte>();

                    preset.Parameters.Add(new LightParameter
                    {
                        Id = paramId,
                        Length = paramLen,
                        Value = paramValue
                    });
                }

                // Engine advances cursor by presetSize from block start (covers any alignment/padding).
                long blockEnd = blockStart + presetSize;

                // Re-read raw bytes for this block (seek back, read, seek forward).
                // Only do so if the block is a sane size to avoid huge allocations.
                if (presetSize > 0 && presetSize <= 0x10000)
                {
                    stream.Position = blockStart;
                    preset.RawBlockBytes = bs.ReadBytes((int)presetSize);
                }

                stream.Position = blockEnd;
                data.Presets.Add(preset);
            }

            return data;
        }

        // ?????????????????????????????????????????????????????????????????????
        //  Serialize � byte-parity round-trip writer
        // ?????????????????????????????????????????????????????????????????????

        // Writes the presets data back to a stream in the LDFB binary format.
        // Automatically recalculates <c>m_PresetSize</c> per preset and <c>m_Size</c> (total file size)
        // to ensure consistency when presets have been added, removed, or modified.
        public void Serialize(Stream stream, LightPresetsData data)
        {
            using var bs = new BinaryStream(stream, ByteConverter.Little, leaveOpen: true);

            long fileStart = stream.Position;

            // LightFinalDataHeader (16 bytes)
            bs.WriteUInt32(FourCC_LDFB);
            bs.WriteUInt32(0); // placeholder for m_Size � patched at end
            bs.WriteUInt32(data.Version);
            uint presetCount = (uint)data.Presets.Count;
            bs.WriteUInt32(presetCount);

            // V2 ID table
            if (data.Version >= 2)
            {
                for (int i = 0; i < presetCount; i++)
                {
                    uint id = i < data.PresetIds.Count ? data.PresetIds[i] : data.Presets[i].Id;
                    bs.WriteUInt32(id);
                }
            }

            // Preset entries
            foreach (var preset in data.Presets)
            {
                long blockStart = stream.Position;

                // Write 8-byte header (placeholders)
                bs.WriteUInt32(0); // placeholder for m_PresetSize
                bs.WriteUInt32((uint)preset.Parameters.Count);

                // Write parameters
                foreach (var param in preset.Parameters)
                {
                    bs.WriteByte(param.Id);
                    bs.WriteByte(param.Length);
                    if (param.Length > 0 && param.Value != null && param.Value.Length > 0)
                        bs.Write(param.Value, 0, param.Length);
                }

                long blockEnd = stream.Position;
                uint computedSize = (uint)(blockEnd - blockStart);

                // Patch m_PresetSize
                stream.Position = blockStart;
                bs.WriteUInt32(computedSize);
                stream.Position = blockEnd;
            }

            // Patch m_Size (total file size)
            long fileEnd = stream.Position;
            uint totalSize = (uint)(fileEnd - fileStart);
            stream.Position = fileStart + 4; // offset of m_Size field
            bs.WriteUInt32(totalSize);
            stream.Position = fileEnd;
        }
    }
}
