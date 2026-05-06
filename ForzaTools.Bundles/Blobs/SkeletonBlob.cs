using Syroot.BinaryData;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace ForzaTools.Bundles.Blobs
{
    public class Bone
    {
        public string Name { get; set; }
        public short ParentId { get; set; } = -1;
        public short FirstChildIndex { get; set; } = -1;
        public short NextIndex { get; set; } = -1;
        public Matrix4x4 Matrix { get; set; } = Matrix4x4.Identity;
    }

    public class SkeletonBlob : BundleBlob
    {
        public List<Bone> Bones { get; set; } = new List<Bone>();

        // The "whole data array" often found at the end of the blob in v1.0+
        public byte[] UnknownData { get; set; }

        public SkeletonBlob()
        {
            // Populate default bone list in the specified order
            // Note: <root> in the prompt is interpreted as the container tag, starting list at "root"
            string[] defaultBoneNames = new string[]
            {
                "<root>",
                "root",
                "anchor_butt",
                "controlArm_LF",
                "controlArm_LR",
                "controlArm_RF",
                "controlArm_RR",
                "hubLF",
                "spindleLF",
                "hubLR",
                "spindleLR",
                "hubRF",
                "spindleRF",
                "hubRR",
                "spindleRR",
                "boneDoorLF",
                "boneGlassLF",
                "boneMirrorL_001",
                "boneDoorRF",
                "boneGlassRF",
                "boneMirrorR_001",
                "boneExhaustR_001",
                "boneFuel",
                "boneSpeed",
                "boneTach",
                "boneTemp",
                "boneHandBrake_001",
                "boneMirrorC_001",
                "boneBrake",
                "anchor_brake",
                "boneClutch",
                "anchor_clutch",
                "boneGas",
                "anchor_gas",
                "anchor_shifter",
                "boneSteeringWheelSpindle",
                "anchor_lefthand",
                "anchor_righthand",
                "boneWiperL",
                "boneWiperBladeL",
                "boneWiperR",
                "boneWiperBladeR",
                "rotorLF_center",
                "rotorLR_center",
                "rotorRF_center",
                "rotorRR_center"
            };

            foreach (var name in defaultBoneNames)
            {
                Bones.Add(new Bone { Name = name });
            }
        }

        public override void ReadBlobData(BinaryStream bs)
        {
            ushort boneCount = bs.ReadUInt16();

            Bones.Clear();
            for (int i = 0; i < boneCount; i++)
            {
                var bone = new Bone();

                // Read String (Int32 Length + Chars)
                int nameLen = bs.ReadInt32();
                bone.Name = bs.ReadString(nameLen, Encoding.UTF8);

                bone.ParentId = bs.ReadInt16();
                bone.FirstChildIndex = bs.ReadInt16();
                bone.NextIndex = bs.ReadInt16();

                // Read Matrix (16 floats)
                float m11 = bs.ReadSingle(); float m12 = bs.ReadSingle(); float m13 = bs.ReadSingle(); float m14 = bs.ReadSingle();
                float m21 = bs.ReadSingle(); float m22 = bs.ReadSingle(); float m23 = bs.ReadSingle(); float m24 = bs.ReadSingle();
                float m31 = bs.ReadSingle(); float m32 = bs.ReadSingle(); float m33 = bs.ReadSingle(); float m34 = bs.ReadSingle();
                float m41 = bs.ReadSingle(); float m42 = bs.ReadSingle(); float m43 = bs.ReadSingle(); float m44 = bs.ReadSingle();

                bone.Matrix = new Matrix4x4(
                    m11, m12, m13, m14,
                    m21, m22, m23, m24,
                    m31, m32, m33, m34,
                    m41, m42, m43, m44
                );

                Bones.Add(bone);
            }

            // Read Unknown Data Array (Version >= 1.0)
            if (VersionMajor >= 1)
            {
                // In some parsers this is reading the remaining bytes or a specific length
                // Assuming standard length-prefixed format or reading remaining if stream allows
                // Based on Bundle_grub.txt logic for "unk__v1_0_length"

                // Note: Some formats might not have the length prefix if it's strictly tail data, 
                // but usually Forza blobs prefix dynamic arrays.
                try
                {
                    // Attempt to read length
                    uint unknownLength = bs.ReadUInt32();
                    if (unknownLength > 0 && unknownLength < bs.Length - bs.Position + 1000) // Sanity check
                    {
                        UnknownData = bs.ReadBytes((int)unknownLength);
                    }
                }
                catch
                {
                    // End of stream or invalid data
                }
            }
        }

        public override void SerializeBlobData(BinaryStream bs)
        {
            WriteBones(bs);
        }

        public override void CreateModelBinBlobData(BinaryStream bs)
        {
            WriteBones(bs);
        }

        private void WriteBones(BinaryStream bs)
        {
            // 1. Write Bone Count
            bs.WriteUInt16((ushort)Bones.Count);

            // 2. Write Each Bone
            foreach (var bone in Bones)
            {
                // Name (Int32 Length + ASCII/UTF8 Bytes)
                string name = bone.Name ?? "";
                bs.WriteInt32(name.Length);
                bs.Write(Encoding.UTF8.GetBytes(name));

                // Indices
                bs.WriteInt16(bone.ParentId);
                bs.WriteInt16(bone.FirstChildIndex);
                bs.WriteInt16(bone.NextIndex);

                // Matrix
                // If the bone has a valid matrix (read from file or modified), write it. 
                // Otherwise the default value of bone.Matrix is Identity, which corresponds to the "placeholder floats".
                bs.WriteSingle(bone.Matrix.M11); bs.WriteSingle(bone.Matrix.M12); bs.WriteSingle(bone.Matrix.M13); bs.WriteSingle(bone.Matrix.M14);
                bs.WriteSingle(bone.Matrix.M21); bs.WriteSingle(bone.Matrix.M22); bs.WriteSingle(bone.Matrix.M23); bs.WriteSingle(bone.Matrix.M24);
                bs.WriteSingle(bone.Matrix.M31); bs.WriteSingle(bone.Matrix.M32); bs.WriteSingle(bone.Matrix.M33); bs.WriteSingle(bone.Matrix.M34);
                bs.WriteSingle(bone.Matrix.M41); bs.WriteSingle(bone.Matrix.M42); bs.WriteSingle(bone.Matrix.M43); bs.WriteSingle(bone.Matrix.M44);
            }

            // 3. Write Unknown Data Array (If Version >= 1.0)
            if (VersionMajor >= 1)
            {
                if (UnknownData != null && UnknownData.Length > 0)
                {
                    bs.WriteUInt32((uint)UnknownData.Length);
                    bs.Write(UnknownData);
                }
                else
                {
                    // Write 0 length if no data present
                    bs.WriteUInt32(0);
                }
            }
        }
    }
}