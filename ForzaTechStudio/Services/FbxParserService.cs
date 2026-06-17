using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace ForzaTechStudio.Services
{
    public class FbxParserService
    {
        public SceneData Parse(string filePath) => Parse(filePath, new ImportSettings());

        public SceneData Parse(string filePath, ImportSettings settings)
        {
            string importPath = filePath;
            string? tempPath = null;
            try
            {
                tempPath = CreateFooterPatchedBinaryFbxIfNeeded(filePath);
                if (tempPath != null)
                    importPath = tempPath;

                var root = FbxBinaryReader.Parse(importPath);
                return ExtractSceneData(root, filePath, settings);
            }
            finally
            {
                if (tempPath != null)
                {
                    try { File.Delete(tempPath); }
                    catch { }
                }
            }
        }

        private static SceneData ExtractSceneData(FbxNode root, string filePath, ImportSettings settings)
        {
            var objectsNode = root.FindChild("Objects") ?? throw new InvalidDataException("FBX file missing Objects section.");
            Matrix4x4 axisConversion = BuildAxisConversionMatrix(settings.ForwardAxis, settings.UpAxis);
            bool requiresWindingFlip = RequiresWindingFlip(axisConversion);

            var geometryNodes = new Dictionary<long, FbxNode>();
            var modelNames = new Dictionary<long, string>();
            foreach (var obj in objectsNode.Children)
            {
                if (obj.Properties.Count == 0)
                    continue;

                if (!TryReadInt64(obj.GetProperty(0), out long id))
                    continue;

                if (obj.Name.AsSpan().SequenceEqual("Geometry"u8))
                    geometryNodes[id] = obj;
                else if (obj.Name.AsSpan().SequenceEqual("Model"u8))
                    modelNames[id] = GetNodeDisplayName(obj, "Model");
            }

            var geometryModelNames = BuildGeometryModelNameMap(root.FindChild("Connections"), geometryNodes, modelNames);

            List<Vector3> positions = new();
            List<Vector3> normals = new();
            List<List<Vector2>> uvChannels = new();
            List<Vector4> colors = new();
            List<SceneGroup> groups = new();

            foreach (var (geometryId, geometryNode) in geometryNodes)
            {
                if (!string.Equals(geometryNode.GetProperty(2) as string, "Mesh", StringComparison.OrdinalIgnoreCase))
                    continue;

                double[]? rawVertices = ReadDoubleArray(geometryNode.FindChild("Vertices")?.GetProperty(0));
                int[]? polygonVertexIndices = ReadIntArray(geometryNode.FindChild("PolygonVertexIndex")?.GetProperty(0));
                if (rawVertices == null || rawVertices.Length < 3 || polygonVertexIndices == null || polygonVertexIndices.Length < 3)
                    continue;

                int controlPointCount = rawVertices.Length / 3;
                var controlPoints = new Vector3[controlPointCount];
                for (int i = 0; i < controlPointCount; i++)
                    controlPoints[i] = Vector3.Transform(ReadVector3(rawVertices, i, Vector3.Zero), axisConversion);

                var uvLayers = geometryNode.FindChildren("LayerElementUV").ToArray();
                while (uvChannels.Count < uvLayers.Length)
                    uvChannels.Add(new List<Vector2>());

                string meshName = geometryModelNames.TryGetValue(geometryId, out string? modelName)
                    ? modelName
                    : GetNodeDisplayName(geometryNode, "Mesh");

                var group = new SceneGroup
                {
                    Name = meshName,
                    MaterialName = "Default"
                };

                var currentFace = new List<(int ControlPointIndex, int PolygonVertexIndex)>();
                for (int polygonVertexIndex = 0; polygonVertexIndex < polygonVertexIndices.Length; polygonVertexIndex++)
                {
                    int encodedIndex = polygonVertexIndices[polygonVertexIndex];
                    currentFace.Add((encodedIndex < 0 ? ~encodedIndex : encodedIndex, polygonVertexIndex));

                    if (encodedIndex >= 0)
                        continue;

                    if (currentFace.Count >= 3)
                    {
                        for (int faceIndex = 1; faceIndex < currentFace.Count - 1; faceIndex++)
                        {
                            AddCorner(currentFace[0], geometryNode, controlPoints, uvLayers, positions, normals, uvChannels, colors, group.Indices, axisConversion);
                            AddCorner(
                                requiresWindingFlip ? currentFace[faceIndex + 1] : currentFace[faceIndex],
                                geometryNode, controlPoints, uvLayers, positions, normals, uvChannels, colors, group.Indices,
                                axisConversion);
                            AddCorner(
                                requiresWindingFlip ? currentFace[faceIndex] : currentFace[faceIndex + 1],
                                geometryNode, controlPoints, uvLayers, positions, normals, uvChannels, colors, group.Indices,
                                axisConversion);
                        }
                    }

                    currentFace.Clear();
                }

                if (group.Indices.Count > 0)
                    groups.Add(group);
            }

            if (positions.Count == 0 || groups.Count == 0)
                throw new InvalidDataException("FBX file did not contain any importable mesh geometry.");

            foreach (var channel in uvChannels)
            {
                while (channel.Count < positions.Count)
                    channel.Add(Vector2.Zero);
            }

            if (uvChannels.Count == 0)
            {
                uvChannels.Add(new List<Vector2>(positions.Count));
                for (int i = 0; i < positions.Count; i++)
                    uvChannels[0].Add(Vector2.Zero);
            }

            return new SceneData
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Positions = positions.ToArray(),
                Normals = normals.ToArray(),
                UVChannels = uvChannels.Select(channel => channel.ToArray()).ToArray(),
                Tangents = CalculateTangents(positions.ToArray(), normals.ToArray(), uvChannels[0].ToArray(), groups),
                Colors = colors.ToArray(),
                Groups = groups,
                MaterialLib = string.Empty
            };
        }

        private static Dictionary<long, string> BuildGeometryModelNameMap(FbxNode? connectionsNode, Dictionary<long, FbxNode> geometryNodes, Dictionary<long, string> modelNames)
        {
            var result = new Dictionary<long, string>();
            if (connectionsNode == null)
                return result;

            foreach (var connection in connectionsNode.Children)
            {
                if (!connection.Name.AsSpan().SequenceEqual("C"u8) || connection.Properties.Count < 3)
                    continue;

                if (!string.Equals(connection.GetProperty(0) as string, "OO", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryReadInt64(connection.GetProperty(1), out long sourceId) ||
                    !TryReadInt64(connection.GetProperty(2), out long destinationId))
                {
                    continue;
                }

                if (geometryNodes.ContainsKey(sourceId) && modelNames.TryGetValue(destinationId, out string? modelName))
                    result[sourceId] = modelName;
            }

            return result;
        }

        private static void AddCorner(
            (int ControlPointIndex, int PolygonVertexIndex) corner,
            FbxNode geometryNode,
            Vector3[] controlPoints,
            FbxNode[] uvLayers,
            List<Vector3> positions,
            List<Vector3> normals,
            List<List<Vector2>> uvChannels,
            List<Vector4> colors,
            List<int> indices,
            Matrix4x4 axisConversion)
        {
            if ((uint)corner.ControlPointIndex >= (uint)controlPoints.Length)
                return;

            int finalIndex = positions.Count;
            positions.Add(controlPoints[corner.ControlPointIndex]);

            Vector3 normal = ReadLayerVector3(geometryNode.FindChild("LayerElementNormal"), "Normals", "NormalsIndex", corner.ControlPointIndex, corner.PolygonVertexIndex, Vector3.UnitY);
            normal = Vector3.TransformNormal(normal, axisConversion);
            if (normal.LengthSquared() > 0.000001f)
                normal = Vector3.Normalize(normal);
            normals.Add(normal);

            for (int channelIndex = 0; channelIndex < uvLayers.Length; channelIndex++)
            {
                while (uvChannels[channelIndex].Count < finalIndex)
                    uvChannels[channelIndex].Add(Vector2.Zero);

                uvChannels[channelIndex].Add(ReadLayerVector2(uvLayers[channelIndex], "UV", "UVIndex", corner.ControlPointIndex, corner.PolygonVertexIndex, Vector2.Zero));
            }

            colors.Add(ReadLayerVector4(geometryNode.FindChild("LayerElementColor"), "Colors", "ColorIndex", corner.ControlPointIndex, corner.PolygonVertexIndex, Vector4.One));
            indices.Add(finalIndex);
        }

        private static Vector3 ReadLayerVector3(FbxNode? layer, string directNodeName, string indexNodeName, int controlPointIndex, int polygonVertexIndex, Vector3 fallback)
        {
            double[]? values = ReadDoubleArray(layer?.FindChild(directNodeName)?.GetProperty(0));
            int directIndex = ResolveLayerIndex(layer, indexNodeName, controlPointIndex, polygonVertexIndex, values?.Length / 3 ?? 0);
            return values != null ? ReadVector3(values, directIndex, fallback) : fallback;
        }

        private static Vector2 ReadLayerVector2(FbxNode? layer, string directNodeName, string indexNodeName, int controlPointIndex, int polygonVertexIndex, Vector2 fallback)
        {
            double[]? values = ReadDoubleArray(layer?.FindChild(directNodeName)?.GetProperty(0));
            int directIndex = ResolveLayerIndex(layer, indexNodeName, controlPointIndex, polygonVertexIndex, values?.Length / 2 ?? 0);
            return values != null ? ReadVector2(values, directIndex, fallback) : fallback;
        }

        private static Vector4 ReadLayerVector4(FbxNode? layer, string directNodeName, string indexNodeName, int controlPointIndex, int polygonVertexIndex, Vector4 fallback)
        {
            double[]? values = ReadDoubleArray(layer?.FindChild(directNodeName)?.GetProperty(0));
            int directIndex = ResolveLayerIndex(layer, indexNodeName, controlPointIndex, polygonVertexIndex, values?.Length / 4 ?? 0);
            return values != null ? ReadVector4(values, directIndex, fallback) : fallback;
        }

        private static int ResolveLayerIndex(FbxNode? layer, string indexNodeName, int controlPointIndex, int polygonVertexIndex, int directCount)
        {
            if (layer == null || directCount <= 0)
                return -1;

            string mapping = layer.FindChild("MappingInformationType")?.GetProperty(0) as string ?? "ByPolygonVertex";
            string reference = layer.FindChild("ReferenceInformationType")?.GetProperty(0) as string ?? "Direct";

            int mappedIndex = mapping switch
            {
                "ByVertice" => controlPointIndex,
                "ByVertex" => controlPointIndex,
                "ByPolygonVertex" => polygonVertexIndex,
                "AllSame" => 0,
                _ => polygonVertexIndex
            };

            if (reference == "IndexToDirect" || reference == "Index")
            {
                int[]? indices = ReadIntArray(layer.FindChild(indexNodeName)?.GetProperty(0));
                if (indices == null || mappedIndex < 0 || mappedIndex >= indices.Length)
                    return -1;
                mappedIndex = indices[mappedIndex];
            }

            return mappedIndex >= 0 && mappedIndex < directCount ? mappedIndex : -1;
        }

        private static string GetNodeDisplayName(FbxNode node, string fallback)
        {
            if (node.GetProperty(1) is not string rawName || string.IsNullOrWhiteSpace(rawName))
                return fallback;

            int classSeparator = rawName.IndexOf("\0\u0001", StringComparison.Ordinal);
            if (classSeparator >= 0)
                rawName = rawName[..classSeparator];
            else
            {
                int nullSeparator = rawName.IndexOf('\0');
                if (nullSeparator >= 0)
                    rawName = rawName[..nullSeparator];
            }

            return string.IsNullOrWhiteSpace(rawName) ? fallback : rawName;
        }

        private static double[]? ReadDoubleArray(object? value)
        {
            return value switch
            {
                double[] doubles => doubles,
                float[] floats => floats.Select(v => (double)v).ToArray(),
                int[] ints => ints.Select(v => (double)v).ToArray(),
                long[] longs => longs.Select(v => (double)v).ToArray(),
                _ => null
            };
        }

        private static int[]? ReadIntArray(object? value)
        {
            return value switch
            {
                int[] ints => ints,
                long[] longs => longs.Select(v => v < int.MinValue || v > int.MaxValue ? 0 : (int)v).ToArray(),
                short[] shorts => shorts.Select(v => (int)v).ToArray(),
                sbyte[] bytes => bytes.Select(v => (int)v).ToArray(),
                _ => null
            };
        }

        private static bool TryReadInt64(object? value, out long result)
        {
            switch (value)
            {
                case long longValue:
                    result = longValue;
                    return true;
                case int intValue:
                    result = intValue;
                    return true;
                case short shortValue:
                    result = shortValue;
                    return true;
                case sbyte sbyteValue:
                    result = sbyteValue;
                    return true;
                case uint uintValue:
                    result = uintValue;
                    return true;
                case ulong ulongValue when ulongValue <= long.MaxValue:
                    result = (long)ulongValue;
                    return true;
                case string stringValue:
                    return long.TryParse(stringValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
                default:
                    result = 0;
                    return false;
            }
        }

        private static Vector3 ReadVector3(double[] values, int index, Vector3 fallback)
        {
            int offset = index * 3;
            return offset >= 0 && offset + 2 < values.Length
                ? new Vector3((float)values[offset], (float)values[offset + 1], (float)values[offset + 2])
                : fallback;
        }

        private static Vector2 ReadVector2(double[] values, int index, Vector2 fallback)
        {
            int offset = index * 2;
            return offset >= 0 && offset + 1 < values.Length
                ? new Vector2((float)values[offset], (float)values[offset + 1])
                : fallback;
        }

        private static Vector4 ReadVector4(double[] values, int index, Vector4 fallback)
        {
            int offset = index * 4;
            return offset >= 0 && offset + 3 < values.Length
                ? new Vector4((float)values[offset], (float)values[offset + 1], (float)values[offset + 2], (float)values[offset + 3])
                : fallback;
        }

        private static Matrix4x4 BuildAxisConversionMatrix(CoordinateAxis forwardAxis, CoordinateAxis upAxis)
        {
            Vector3 sourceForward = GetAxisVector(forwardAxis);
            Vector3 sourceUp = GetAxisVector(upAxis);

            if (Math.Abs(Vector3.Dot(sourceForward, sourceUp)) > 0.001f)
            {
                Vector3 sourceRight = Vector3.Cross(sourceUp, sourceForward);
                sourceUp = Vector3.Normalize(Vector3.Cross(sourceForward, sourceRight));
            }

            Vector3 sourceRight2 = Vector3.Normalize(Vector3.Cross(sourceUp, sourceForward));
            Vector3 targetForward = -Vector3.UnitZ;
            Vector3 targetUp = Vector3.UnitY;
            Vector3 targetRight = Vector3.UnitX;

            return new Matrix4x4(
                Vector3.Dot(sourceRight2, targetRight), Vector3.Dot(sourceRight2, targetUp), Vector3.Dot(sourceRight2, targetForward), 0,
                Vector3.Dot(sourceUp, targetRight), Vector3.Dot(sourceUp, targetUp), Vector3.Dot(sourceUp, targetForward), 0,
                Vector3.Dot(sourceForward, targetRight), Vector3.Dot(sourceForward, targetUp), Vector3.Dot(sourceForward, targetForward), 0,
                0, 0, 0, 1);
        }

        private static Vector3 GetAxisVector(CoordinateAxis axis) => axis switch
        {
            CoordinateAxis.PositiveX => Vector3.UnitX,
            CoordinateAxis.NegativeX => -Vector3.UnitX,
            CoordinateAxis.PositiveY => Vector3.UnitY,
            CoordinateAxis.NegativeY => -Vector3.UnitY,
            CoordinateAxis.PositiveZ => Vector3.UnitZ,
            CoordinateAxis.NegativeZ => -Vector3.UnitZ,
            _ => Vector3.UnitY
        };

        private static bool RequiresWindingFlip(Matrix4x4 matrix)
        {
            float determinant = matrix.M11 * (matrix.M22 * matrix.M33 - matrix.M23 * matrix.M32)
                              - matrix.M12 * (matrix.M21 * matrix.M33 - matrix.M23 * matrix.M31)
                              + matrix.M13 * (matrix.M21 * matrix.M32 - matrix.M22 * matrix.M31);
            return determinant < 0;
        }

        private static Vector4[] CalculateTangents(Vector3[] positions, Vector3[] normals, Vector2[] uvs, List<SceneGroup> groups)
        {
            var tangents = new Vector3[positions.Length];
            var bitangents = new Vector3[positions.Length];
            var finalTangents = new Vector4[positions.Length];

            foreach (var group in groups)
            {
                for (int i = 0; i + 2 < group.Indices.Count; i += 3)
                {
                    int i0 = group.Indices[i];
                    int i1 = group.Indices[i + 1];
                    int i2 = group.Indices[i + 2];

                    Vector3 edge1 = positions[i1] - positions[i0];
                    Vector3 edge2 = positions[i2] - positions[i0];
                    Vector2 deltaUv1 = uvs[i1] - uvs[i0];
                    Vector2 deltaUv2 = uvs[i2] - uvs[i0];
                    float denominator = deltaUv1.X * deltaUv2.Y - deltaUv2.X * deltaUv1.Y;
                    if (Math.Abs(denominator) < 0.000001f)
                        continue;

                    float factor = 1.0f / denominator;
                    Vector3 tangent = (edge1 * deltaUv2.Y - edge2 * deltaUv1.Y) * factor;
                    Vector3 bitangent = (edge2 * deltaUv1.X - edge1 * deltaUv2.X) * factor;

                    tangents[i0] += tangent;
                    tangents[i1] += tangent;
                    tangents[i2] += tangent;
                    bitangents[i0] += bitangent;
                    bitangents[i1] += bitangent;
                    bitangents[i2] += bitangent;
                }
            }

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 normal = normals.Length > i ? normals[i] : Vector3.UnitY;
                Vector3 tangent = tangents[i] - normal * Vector3.Dot(normal, tangents[i]);
                if (tangent.LengthSquared() < 0.000001f || float.IsNaN(tangent.X))
                    tangent = Vector3.UnitX;
                else
                    tangent = Vector3.Normalize(tangent);

                float handedness = Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[i]) < 0.0f ? -1.0f : 1.0f;
                finalTangents[i] = new Vector4(tangent, handedness);
            }

            return finalTangents;
        }

        private static string? CreateFooterPatchedBinaryFbxIfNeeded(string filePath)
        {
            if (!IsBinaryFbx(filePath) || HasBinaryFooter(filePath))
                return null;

            string tempPath = Path.Combine(Path.GetTempPath(), $"fbx_{Guid.NewGuid():N}.fbx");
            File.Copy(filePath, tempPath, overwrite: false);

            using var stream = File.Open(tempPath, FileMode.Append, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);
            WriteBinaryFooter(writer, 7400);
            return tempPath;
        }

        private static bool IsBinaryFbx(string filePath)
        {
            byte[] expected = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1A\0");
            if (!File.Exists(filePath) || new FileInfo(filePath).Length < expected.Length + sizeof(uint))
                return false;
            byte[] actual = new byte[expected.Length];
            using var stream = File.OpenRead(filePath);
            return stream.Read(actual, 0, actual.Length) == actual.Length && actual.SequenceEqual(expected);
        }

        private static bool HasBinaryFooter(string filePath)
        {
            byte[] footerEnd =
            {
                0xF8, 0x5A, 0x8C, 0x6A, 0xDE, 0xF5, 0xD9, 0x7E,
                0xEC, 0xE9, 0x0C, 0xE3, 0x75, 0x8F, 0x29, 0x0B
            };
            long length = new FileInfo(filePath).Length;
            if (length < footerEnd.Length) return false;
            byte[] actual = new byte[footerEnd.Length];
            using var stream = File.OpenRead(filePath);
            stream.Seek(-footerEnd.Length, SeekOrigin.End);
            return stream.Read(actual, 0, actual.Length) == actual.Length && actual.SequenceEqual(footerEnd);
        }

        private static void WriteBinaryFooter(BinaryWriter writer, uint version)
        {
            writer.Write(new byte[]
            {
                0xFA, 0xBC, 0xAB, 0x09, 0xD0, 0xC8, 0xD4, 0x66,
                0xB1, 0x76, 0xFB, 0x83, 0x1C, 0xF7, 0x26, 0x7E
            });
            writer.Write((uint)0);
            writer.Write(version);
            writer.Write(new byte[120]);
            writer.Write(new byte[]
            {
                0xF8, 0x5A, 0x8C, 0x6A, 0xDE, 0xF5, 0xD9, 0x7E,
                0xEC, 0xE9, 0x0C, 0xE3, 0x75, 0x8F, 0x29, 0x0B
            });
        }
    }
}