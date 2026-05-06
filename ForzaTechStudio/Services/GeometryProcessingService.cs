using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace ForzaTechStudio.Services
{
    public class ProcessedGeometry
    {
        public byte[][] PositionData;
        public byte[][] NormalUVData;
        public byte[][] IndexData;
        public Vector4 PositionScale;
        public Vector4 PositionTranslate;
        public Vector3 BoundingBoxMin;
        public Vector3 BoundingBoxMax;
    }

    public struct GeometryInput
    {
        public string Name;
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector2[] UVs;
        public Vector4[] Tangents;
        public int[] Indices;
    }

    public class GeometryProcessingService
    {
        public ProcessedGeometry ProcessGeometry(GeometryInput input, ForzaGameTarget target = ForzaGameTarget.FH5)
        {
            var result = new ProcessedGeometry();

            if (input.Positions == null || input.Positions.Length == 0)
                throw new ArgumentException("Input geometry must have positions.");

            // 1. Calculate Bounds
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);

            foreach (var p in input.Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            result.BoundingBoxMin = min;
            result.BoundingBoxMax = max;

            Vector3 center = (min + max) / 2.0f;
            Vector3 range = max - min;

            float maxDimension = Math.Max(range.X, Math.Max(range.Y, range.Z));
            if (maxDimension == 0) maxDimension = 1.0f;

            float expansionFactor = 0.1f;
            float expandedRange = maxDimension * (1.0f + expansionFactor);
            float radius = expandedRange / 2.0f;

            result.PositionScale = new Vector4(radius, radius, radius, 1.0f);
            result.PositionTranslate = new Vector4(center, 0.0f);

            int vertexCount = input.Positions.Length;
            result.PositionData = new byte[vertexCount][];
            result.NormalUVData = new byte[vertexCount][];

            // Get the layout info for the target game
            var layout = ModelBuilderService.GetGameLayoutInfoInternal(target);
            bool isFH2 = target is ForzaGameTarget.FH2 or ForzaGameTarget.FM5;

            // 2. Generate Vertex Buffers
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 pos = input.Positions[i];
                Vector3 norm = (input.Normals != null && i < input.Normals.Length) ? Vector3.Normalize(input.Normals[i]) : Vector3.UnitY;
                Vector2 uv = (input.UVs != null && i < input.UVs.Length) ? input.UVs[i] : Vector2.Zero;
                Vector4 tangent = (input.Tangents != null && i < input.Tangents.Length) ? input.Tangents[i] : new Vector4(1, 0, 0, 1);

                // Buffer 0 (Position, 8 bytes, same for all games)
                using (var ms = new MemoryStream(8))
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(QuantizePosition(pos.X, center.X, radius));
                    bw.Write(QuantizePosition(pos.Y, center.Y, radius));
                    bw.Write(QuantizePosition(pos.Z, center.Z, radius));
                    bw.Write((short)(norm.X * 32767));
                    result.PositionData[i] = ms.ToArray();
                }

                // Buffer 1 (Norm/UV/Tan/Color — game-specific stride and format)
                using (var ms = new MemoryStream(layout.Stride))
                using (var bw = new BinaryWriter(ms))
                {
                    // 1. NORMAL — format varies by game
                    if (isFH2)
                    {
                        // FH2/FM5: R16G16B16A16_FLOAT (8 bytes) — encode Y,Z,unused,unused as half-floats
                        bw.Write(FloatToHalf(norm.Y));
                        bw.Write(FloatToHalf(norm.Z));
                        bw.Write(FloatToHalf(0.0f)); // b unused
                        bw.Write(FloatToHalf(1.0f)); // a
                    }
                    else
                    {
                        // FM6+: R16G16_SNORM (4 bytes) — encode Y,Z as SNORM16
                        bw.Write((short)(norm.Y * 32767));
                        bw.Write((short)(norm.Z * 32767));
                    }

                    // 2. TEXCOORDs — R16G16_UNORM (4 bytes each), same for all games
                    ushort u = (ushort)(Math.Clamp(uv.X, 0f, 1f) * 65535.0f);
                    ushort v = (ushort)(Math.Clamp(1.0f - uv.Y, 0f, 1f) * 65535.0f); // V-Flip

                    for (int k = 0; k < layout.TexcoordCount; k++)
                    {
                        bw.Write(u);
                        bw.Write(v);
                    }

                    // 3. TANGENTs — format varies by game
                    if (isFH2)
                    {
                        // FH2/FM5: R16G16B16A16_FLOAT (8 bytes each)
                        for (int k = 0; k < layout.TangentCount; k++)
                        {
                            bw.Write(FloatToHalf(tangent.X));
                            bw.Write(FloatToHalf(tangent.Y));
                            bw.Write(FloatToHalf(tangent.Z));
                            bw.Write(FloatToHalf(tangent.W));
                        }
                    }
                    else
                    {
                        // FM6+: R10G10B10A2_UNORM (4 bytes each)
                        uint packedTangent = Pack1010102(tangent.X, tangent.Y, tangent.Z, tangent.W);
                        for (int k = 0; k < layout.TangentCount; k++)
                        {
                            bw.Write(packedTangent);
                        }
                    }

                    // 4. COLOR — R8G8B8A8_UNORM (4 bytes), only if game supports it
                    if (layout.HasColor)
                    {
                        bw.Write((uint)0xFFFFFFFF); // White (RGBA 255)
                    }

                    result.NormalUVData[i] = ms.ToArray();
                }
            }

            // 3. Generate Index Buffer
            if (input.Indices == null)
            {
                result.IndexData = new byte[0][];
            }
            else
            {
                int indexCount = input.Indices.Length;
                result.IndexData = new byte[indexCount][];
                for (int i = 0; i < indexCount; i++)
                {
                    result.IndexData[i] = BitConverter.GetBytes(input.Indices[i]);
                }
            }

            return result;
        }

        private short QuantizePosition(float value, float center, float radius)
        {
            float dist = value - center;
            float normalized = dist / radius;
            if (normalized < -1.0f) normalized = -1.0f;
            if (normalized > 1.0f) normalized = 1.0f;
            return (short)(normalized * 32767.0f);
        }

        private uint Pack1010102(float x, float y, float z, float w)
        {
            // R10G10B10A2_UNORM: map [-1,1] signed range to [0,1] unsigned, then quantize
            uint ix = (uint)(Math.Clamp((x * 0.5f + 0.5f), 0f, 1f) * 1023.0f + 0.5f) & 0x3FF;
            uint iy = (uint)(Math.Clamp((y * 0.5f + 0.5f), 0f, 1f) * 1023.0f + 0.5f) & 0x3FF;
            uint iz = (uint)(Math.Clamp((z * 0.5f + 0.5f), 0f, 1f) * 1023.0f + 0.5f) & 0x3FF;
            uint iw = (uint)(Math.Clamp((w * 0.5f + 0.5f), 0f, 1f) * 3.0f + 0.5f) & 0x3;
            return ix | (iy << 10) | (iz << 20) | (iw << 30);
        }

        private ushort FloatToHalf(float value)
        {
            return BitConverter.HalfToUInt16Bits((Half)value);
        }
    }
}
