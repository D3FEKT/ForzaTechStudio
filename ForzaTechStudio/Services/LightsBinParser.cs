using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Syroot.BinaryData;

namespace ForzaTechStudio.Services
{
    public class LightsBinParser
    {
        // CarLightAttachment: a model/bone attachment point that lights can reference.
        public class ModelEntry
        {
            public string FullPath { get; set; }
            public ushort BoneIndex { get; set; }
        }

        // CarLightLODOverride: LodIndex (u32), ArrayId (u32), Pos/Rot/DamagePos/DamageRot (XMFLOAT4 each).
        public class LightLODOverride
        {
            public uint LodIndex { get; set; }
            public uint ArrayId { get; set; }
            public Vector4 Pos { get; set; }
            public Vector4 Rot { get; set; }
            public Vector4 DamagePos { get; set; }
            public Vector4 DamageRot { get; set; }

            // Convenience accessors
            public Vector3 Position => new Vector3(Pos.X, Pos.Y, Pos.Z);
            public Quaternion Rotation => new Quaternion(Rot.X, Rot.Y, Rot.Z, Rot.W);
        }

        public class LightsBinData
        {
            public uint Magic { get; set; } = 0xDEADBEEF;
            public uint Version { get; set; }
            // Original file size in bytes (informational, not part of the binary header). Used for round-trip validation.
            public long OriginalFileSize { get; set; }
            public List<ModelEntry> Models { get; set; } = new();
            public List<LightGroup> Groups { get; set; } = new();
            public List<LightLODOverride> LODOverrides { get; set; } = new();
        }

        // CarLight: Id, FunctionalityAndFlags, AttachmentIndex, Pos/Rot/DamagePos/DamageRot (XMFLOAT4s), PresetName (v2+), GUID (v3+). Sequential, no padding.
        public class LightGroup
        {
            public uint Id { get; set; }
            public uint Flags { get; set; }

            public bool IsExterior                             { get => (Flags & 1)   != 0; set { if (value) Flags |= 1;   else Flags &= ~1u; } }
            public bool IsCockpit                              { get => (Flags & 2)   != 0; set { if (value) Flags |= 2;   else Flags &= ~2u; } }
            public bool CastsShadows                           { get => (Flags & 4)   != 0; set { if (value) Flags |= 4;   else Flags &= ~4u; } }
            public bool IsHood                                 { get => (Flags & 8)   != 0; set { if (value) Flags |= 8;   else Flags &= ~8u; } }
            public bool IsWindshieldReflection                 { get => (Flags & 16)  != 0; set { if (value) Flags |= 16;  else Flags &= ~16u; } }
            public bool IsDriverlessCockpit                    { get => (Flags & 32)  != 0; set { if (value) Flags |= 32;  else Flags &= ~32u; } }
            public bool IsWindshieldReflectionDriverlessCockpit{ get => (Flags & 64)  != 0; set { if (value) Flags |= 64;  else Flags &= ~64u; } }
            public bool IsProxyLOD                             { get => (Flags & 128) != 0; set { if (value) Flags |= 128; else Flags &= ~128u; } }

            // AttachmentIndex: index into the Attachments (Models) list.
            // Also referred to as ModelbinNumber in prior tools.
            public uint AttachmentIndex { get; set; }

            // Derived from AttachmentIndex lookup after parsing
            public string ModelName { get; set; }
            public string FullModelPath { get; set; }

            // Position and rotation (world-space, from the Pos/Rot XMFLOAT4 vectors)
            // Pos.W and Rot.W are stored but typically unused as scale/padding.
            public Vector4 Pos { get; set; }
            public Vector4 Rot { get; set; }

            // Damage-state overrides (used when car damage is applied)
            public Vector4 DamagePos { get; set; }
            public Vector4 DamageRot { get; set; }

            // Light preset name (version >= 2 only)
            public string PresetName { get; set; }

            // Unknown GUID (version >= 3 only): 16 bytes total
            //   uint32 Data1, uint16 Data2, uint16 Data3, byte[8] Data4
            public Guid V3Guid { get; set; }

            // Row aliases matching the binary order: Pos, Rot, DamagePos, DamageRot.
            // Row1 = Pos, Row2 = Rot, Row3 = DamagePos, Row4 = DamageRot.
            public Vector4 Row1 { get => Pos;       set => Pos       = value; }
            public Vector4 Row2 { get => Rot;       set => Rot       = value; }
            public Vector4 Row3 { get => DamagePos; set => DamagePos = value; }
            public Vector4 Row4 { get => DamageRot; set => DamageRot = value; }

            // Transform: matrix built from the 4 rows (Pos/Rot/DamagePos/DamageRot as matrix rows).
            // Setting decomposes back into the individual row fields.
            public Matrix4x4 Transform
            {
                get => new Matrix4x4(
                    Pos.X,       Pos.Y,       Pos.Z,       Pos.W,
                    Rot.X,       Rot.Y,       Rot.Z,       Rot.W,
                    DamagePos.X, DamagePos.Y, DamagePos.Z, DamagePos.W,
                    DamageRot.X, DamageRot.Y, DamageRot.Z, DamageRot.W);
                set
                {
                    Pos       = new Vector4(value.M11, value.M12, value.M13, value.M14);
                    Rot       = new Vector4(value.M21, value.M22, value.M23, value.M24);
                    DamagePos = new Vector4(value.M31, value.M32, value.M33, value.M34);
                    DamageRot = new Vector4(value.M41, value.M42, value.M43, value.M44);
                }
            }

            // Convenience accessors
            public Vector3 Position
            {
                get => new Vector3(Pos.X, Pos.Y, Pos.Z);
                set => Pos = new Vector4(value.X, value.Y, value.Z, Pos.W);
            }
            public Quaternion Rotation => new Quaternion(Rot.X, Rot.Y, Rot.Z, Rot.W);
            public Vector3 DamagePosition => new Vector3(DamagePos.X, DamagePos.Y, DamagePos.Z);
            public Quaternion DamageRotation => new Quaternion(DamageRot.X, DamageRot.Y, DamageRot.Z, DamageRot.W);
        }

        // Reads a string serialized as: uint32 length (no null) + raw ASCII chars.
        // Verified via IOSys::SerializeContainer<std::string>: writes Mysize then each char.
        private static string ReadString(BinaryStream bs)
        {
            uint len = bs.ReadUInt32();
            if (len == 0)
                return string.Empty;
            byte[] bytes = bs.ReadBytes((int)len);
            return Encoding.ASCII.GetString(bytes);
        }

        // Writes a string as: uint32 length (no null) + raw ASCII chars.
        private static void WriteString(BinaryStream bs, string value)
        {
            string s = value ?? string.Empty;
            byte[] bytes = Encoding.ASCII.GetBytes(s);
            bs.WriteUInt32((uint)bytes.Length);
            if (bytes.Length > 0)
                bs.Write(bytes);
        }

        private static Vector4 ReadVector4(BinaryStream bs)
            => new Vector4(bs.ReadSingle(), bs.ReadSingle(), bs.ReadSingle(), bs.ReadSingle());

        private static void WriteVector4(BinaryStream bs, Vector4 v)
        {
            bs.WriteSingle(v.X);
            bs.WriteSingle(v.Y);
            bs.WriteSingle(v.Z);
            bs.WriteSingle(v.W);
        }

        // Reads a GUID in the Forza on-disk layout:
        //   uint32 Data1, uint16 Data2, uint16 Data3, byte[8] Data4 (all little-endian)
        // This matches the Win32 GUID / 010 Editor GUID struct used by ForzaLights.bt.
        private static Guid ReadGuid(BinaryStream bs)
        {
            uint   data1 = bs.ReadUInt32();
            ushort data2 = bs.ReadUInt16();
            ushort data3 = bs.ReadUInt16();
            byte[] data4 = bs.ReadBytes(8);
            return new Guid(data1, data2, data3,
                data4[0], data4[1], data4[2], data4[3],
                data4[4], data4[5], data4[6], data4[7]);
        }

        // Writes a GUID in the same on-disk layout.
        private static void WriteGuid(BinaryStream bs, Guid guid)
        {
            byte[] raw = guid.ToByteArray(); // .NET: [0..3]=Data1 LE, [4..5]=Data2 LE, [6..7]=Data3 LE, [8..15]=Data4
            // Write as: uint32 Data1, uint16 Data2, uint16 Data3, byte[8] Data4
            bs.WriteUInt32(BitConverter.ToUInt32(raw, 0));
            bs.WriteUInt16(BitConverter.ToUInt16(raw, 4));
            bs.WriteUInt16(BitConverter.ToUInt16(raw, 6));
            bs.Write(raw, 8, 8);
        }


        public LightsBinData ParseToData(Stream stream)
        {
            var data = new LightsBinData();
            data.OriginalFileSize = stream.Length;

            using var bs = new BinaryStream(stream, ByteConverter.Little);

            // Read magic; if not 0xDEADBEEF this is a version-0 file with no magic/version header
            uint magic = bs.ReadUInt32();
            if (magic == 0xDEADBEEF)
            {
                data.Magic = magic;
                data.Version = bs.ReadUInt32();
            }
            else
            {
                // Version 0: no magic, no version field; seek back and treat count as next field
                data.Magic = 0xDEADBEEF;
                data.Version = 0;
                stream.Seek(-4, SeekOrigin.Current);
            }

            // Read attachments (CarLightAttachment: path string + uint16 BoneIndex)
            uint attachmentCount = bs.ReadUInt32();
            for (int i = 0; i < attachmentCount; i++)
            {
                string fullPath = ReadString(bs);
                ushort boneIndex = bs.ReadUInt16();

                data.Models.Add(new ModelEntry
                {
                    FullPath = fullPath,
                    BoneIndex = boneIndex
                });
            }

            // Read lights (CarLight entries)
            uint lightCount = bs.ReadUInt32();
            for (int i = 0; i < lightCount; i++)
            {
                var group = new LightGroup
                {
                    Id              = bs.ReadUInt32(),
                    Flags           = bs.ReadUInt32(),
                    AttachmentIndex = bs.ReadUInt32(),
                    Pos             = ReadVector4(bs),
                    Rot             = ReadVector4(bs),
                    DamagePos       = ReadVector4(bs),
                    DamageRot       = ReadVector4(bs)
                };

                if (data.Version >= 2)
                    group.PresetName = ReadString(bs);

                if (data.Version >= 3)
                    group.V3Guid = ReadGuid(bs);

                // Resolve attachment path
                if (group.AttachmentIndex < data.Models.Count)
                {
                    string fullPath = data.Models[(int)group.AttachmentIndex].FullPath;
                    group.FullModelPath = fullPath;
                    int lastSlash = fullPath.LastIndexOfAny(['/', '\\']);
                    group.ModelName = lastSlash >= 0 ? fullPath[(lastSlash + 1)..] : fullPath;
                }

                data.Groups.Add(group);
            }

            // Read LOD overrides (version >= 1)
            if (data.Version >= 1 && stream.Position + 4 <= stream.Length)
            {
                try
                {
                    uint overrideCount = bs.ReadUInt32();
                    for (int i = 0; i < overrideCount; i++)
                    {
                        data.LODOverrides.Add(new LightLODOverride
                        {
                            LodIndex  = bs.ReadUInt32(),
                            ArrayId   = bs.ReadUInt32(),
                            Pos       = ReadVector4(bs),
                            Rot       = ReadVector4(bs),
                            DamagePos = ReadVector4(bs),
                            DamageRot = ReadVector4(bs)
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading LOD overrides: {ex.Message}");
                }
            }

            return data;
        }

        public List<LightGroup> Parse(Stream stream)
        {
            var data = ParseToData(stream);
            return data.Groups;
        }

        public void Serialize(Stream stream, LightsBinData data)
        {
            using var bs = new BinaryStream(stream, ByteConverter.Little);

            // Version-0 files have no magic or version header.
            // Only write the magic+version when version > 0 to preserve round-trip fidelity.
            if (data.Version > 0)
            {
                bs.WriteUInt32(data.Magic);
                bs.WriteUInt32(data.Version);
            }

            // Write attachments
            bs.WriteUInt32((uint)data.Models.Count);
            foreach (var model in data.Models)
            {
                WriteString(bs, model.FullPath);
                bs.WriteUInt16(model.BoneIndex);
            }

            // Write lights
            bs.WriteUInt32((uint)data.Groups.Count);
            foreach (var group in data.Groups)
            {
                bs.WriteUInt32(group.Id);
                bs.WriteUInt32(group.Flags);
                bs.WriteUInt32(group.AttachmentIndex);
                WriteVector4(bs, group.Pos);
                WriteVector4(bs, group.Rot);
                WriteVector4(bs, group.DamagePos);
                WriteVector4(bs, group.DamageRot);

                if (data.Version >= 2)
                    WriteString(bs, group.PresetName);

                if (data.Version >= 3)
                    WriteGuid(bs, group.V3Guid);
            }

            // Write LOD overrides (version >= 1)
            if (data.Version >= 1)
            {
                bs.WriteUInt32((uint)data.LODOverrides.Count);
                foreach (var ovr in data.LODOverrides)
                {
                    bs.WriteUInt32(ovr.LodIndex);
                    bs.WriteUInt32(ovr.ArrayId);
                    WriteVector4(bs, ovr.Pos);
                    WriteVector4(bs, ovr.Rot);
                    WriteVector4(bs, ovr.DamagePos);
                    WriteVector4(bs, ovr.DamageRot);
                }
            }
        }
    }
}
