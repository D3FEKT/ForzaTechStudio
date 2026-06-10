using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace ForzaTechStudio.Services
{


    public class ObjParserService
    {
        private class VertexKey : IEquatable<VertexKey>
        {
            public int V, VT, VN;
            public override bool Equals(object obj) => obj is VertexKey k && Equals(k);
            public bool Equals(VertexKey other) => V == other.V && VT == other.VT && VN == other.VN;
            public override int GetHashCode() => HashCode.Combine(V, VT, VN);
        }

        public SceneData ParseObj(string filePath) => ParseObj(filePath, new ImportSettings());

        public SceneData ParseObj(string filePath, ImportSettings settings)
        {
            List<Vector3> rawPositions = new();
            List<Vector3> rawNormals = new();
            List<Vector2> rawUVs = new();

            List<Vector3> finalPositions = new();
            List<Vector3> finalNormals = new();
            List<Vector2> finalUVs = new();

            var groups = new List<SceneGroup>();
            var currentGroup = new SceneGroup { Name = "Default" };
            groups.Add(currentGroup);

            string materialLib = null;
            var indexCache = new Dictionary<VertexKey, int>();

            // Build the axis conversion matrix based on settings
            Matrix4x4 axisConversion = BuildAxisConversionMatrix(settings.ForwardAxis, settings.UpAxis);
            bool requiresWindingFlip = RequiresWindingFlip(axisConversion);

            foreach (var line in File.ReadLines(filePath))
            {
                var trim = line.Trim();
                if (string.IsNullOrWhiteSpace(trim) || trim.StartsWith('#')) continue;

                var tokens = trim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;

                switch (tokens[0])
                {
                    case "mtllib":
                        if (tokens.Length > 1) materialLib = tokens[1];
                        break;

                    case "usemtl":
                        if (tokens.Length > 1) currentGroup.MaterialName = tokens[1];
                        break;

                    case "g":
                    case "o":
                        string groupName = tokens.Length > 1 ? tokens[1] : $"Group_{groups.Count}";
                        if (currentGroup.Indices.Count > 0)
                        {
                            currentGroup = new SceneGroup { Name = groupName };
                            groups.Add(currentGroup);
                        }
                        else
                        {
                            currentGroup.Name = groupName;
                        }
                        break;

                    case "v":
                        if (tokens.Length >= 4)
                            rawPositions.Add(new Vector3(
                                ParseFloat(tokens[1]),
                                ParseFloat(tokens[2]),
                                ParseFloat(tokens[3])));
                        break;

                    case "vn":
                        if (tokens.Length >= 4)
                            rawNormals.Add(new Vector3(
                                ParseFloat(tokens[1]),
                                ParseFloat(tokens[2]),
                                ParseFloat(tokens[3])));
                        break;

                    case "vt":
                        if (tokens.Length >= 3)
                            rawUVs.Add(new Vector2(
                                ParseFloat(tokens[1]),
                                ParseFloat(tokens[2])));
                        break;

                    case "f":
                        int vCount = tokens.Length - 1;
                        // Triangulate the face (fan triangulation)
                        for (int i = 0; i < vCount - 2; i++)
                        {
                            int idx0 = ProcessVertexToken(tokens[1], rawPositions, rawNormals, rawUVs,
                                finalPositions, finalNormals, finalUVs, indexCache);
                            int idx1 = ProcessVertexToken(tokens[2 + i], rawPositions, rawNormals, rawUVs,
                                finalPositions, finalNormals, finalUVs, indexCache);
                            int idx2 = ProcessVertexToken(tokens[3 + i], rawPositions, rawNormals, rawUVs,
                                finalPositions, finalNormals, finalUVs, indexCache);

                            // Skip degenerate triangles
                            if (idx0 != idx1 && idx1 != idx2 && idx0 != idx2)
                            {
                                if (requiresWindingFlip)
                                {
                                    // Flip winding order for negative determinant transforms
                                    currentGroup.Indices.Add(idx0);
                                    currentGroup.Indices.Add(idx2);
                                    currentGroup.Indices.Add(idx1);
                                }
                                else
                                {
                                    currentGroup.Indices.Add(idx0);
                                    currentGroup.Indices.Add(idx1);
                                    currentGroup.Indices.Add(idx2);
                                }
                            }
                        }
                        break;
                }
            }

            // Apply axis conversion to positions and normals
            for (int i = 0; i < finalPositions.Count; i++)
            {
                finalPositions[i] = Vector3.Transform(finalPositions[i], axisConversion);
            }

            for (int i = 0; i < finalNormals.Count; i++)
            {
                finalNormals[i] = Vector3.TransformNormal(finalNormals[i], axisConversion);
                if (finalNormals[i].LengthSquared() > 0.000001f)
                    finalNormals[i] = Vector3.Normalize(finalNormals[i]);
            }

            // Generate default normals if none provided
            if (finalNormals.Count == 0 && finalPositions.Count > 0)
            {
                for (int i = 0; i < finalPositions.Count; i++)
                    finalNormals.Add(Vector3.UnitY);
            }

            // Filter out empty groups
            groups.RemoveAll(g => g.Indices.Count == 0);

            // Calculate tangents from UV coordinates
            var tangents = CalculateTangents(
                finalPositions.ToArray(),
                finalNormals.ToArray(),
                finalUVs.ToArray(),
                groups);

            // Default vertex colors (white)
            var colors = new Vector4[finalPositions.Count];
            Array.Fill(colors, new Vector4(1, 1, 1, 1));

            return new SceneData
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                MaterialLib = materialLib,
                Positions = finalPositions.ToArray(),
                Normals = finalNormals.ToArray(),
                UVChannels = new[] { finalUVs.ToArray() },
                Tangents = tangents,
                Colors = colors,
                Groups = groups
            };
        }

        // Builds a transformation matrix to convert from the source coordinate system to the target coordinate system.
        // Target system: Forward = -Z, Up = +Y (standard right-handed OpenGL/DirectX convention).
        private Matrix4x4 BuildAxisConversionMatrix(CoordinateAxis forwardAxis, CoordinateAxis upAxis)
        {
            // Get the source forward and up vectors
            Vector3 srcForward = GetAxisVector(forwardAxis);
            Vector3 srcUp = GetAxisVector(upAxis);

            // Ensure forward and up are perpendicular
            if (Math.Abs(Vector3.Dot(srcForward, srcUp)) > 0.001f)
            {
                // If not perpendicular, adjust up to be perpendicular to forward
                Vector3 srcRight = Vector3.Cross(srcUp, srcForward);
                srcUp = Vector3.Normalize(Vector3.Cross(srcForward, srcRight));
            }

            // Calculate the source right vector
            Vector3 srcRight2 = Vector3.Normalize(Vector3.Cross(srcUp, srcForward));

            // Target coordinate system: Forward = -Z, Up = +Y, Right = +X
            Vector3 tgtForward = -Vector3.UnitZ;
            Vector3 tgtUp = Vector3.UnitY;
            Vector3 tgtRight = Vector3.UnitX;

            // Build transformation matrix
            // Each row of the matrix maps from source to target
            Matrix4x4 result = new Matrix4x4(
                Vector3.Dot(srcRight2, tgtRight), Vector3.Dot(srcRight2, tgtUp), Vector3.Dot(srcRight2, tgtForward), 0,
                Vector3.Dot(srcUp, tgtRight), Vector3.Dot(srcUp, tgtUp), Vector3.Dot(srcUp, tgtForward), 0,
                Vector3.Dot(srcForward, tgtRight), Vector3.Dot(srcForward, tgtUp), Vector3.Dot(srcForward, tgtForward), 0,
                0, 0, 0, 1
            );

            return result;
        }

        // Returns the unit vector for the specified axis.
        private Vector3 GetAxisVector(CoordinateAxis axis) => axis switch
        {
            CoordinateAxis.PositiveX => Vector3.UnitX,
            CoordinateAxis.NegativeX => -Vector3.UnitX,
            CoordinateAxis.PositiveY => Vector3.UnitY,
            CoordinateAxis.NegativeY => -Vector3.UnitY,
            CoordinateAxis.PositiveZ => Vector3.UnitZ,
            CoordinateAxis.NegativeZ => -Vector3.UnitZ,
            _ => Vector3.UnitY
        };

        // Determines if the transformation matrix has a negative determinant, requiring winding order flip.
        private bool RequiresWindingFlip(Matrix4x4 matrix)
        {
            // Calculate the determinant of the 3x3 rotation/scale part
            float det = matrix.M11 * (matrix.M22 * matrix.M33 - matrix.M23 * matrix.M32)
                      - matrix.M12 * (matrix.M21 * matrix.M33 - matrix.M23 * matrix.M31)
                      + matrix.M13 * (matrix.M21 * matrix.M32 - matrix.M22 * matrix.M31);
            return det < 0;
        }

        // Calculates tangent vectors using the MikkTSpace-like algorithm.
        private Vector4[] CalculateTangents(Vector3[] positions, Vector3[] normals, Vector2[] uvs, List<SceneGroup> groups)
        {
            int vertexCount = positions.Length;
            var tangents = new Vector3[vertexCount];
            var bitangents = new Vector3[vertexCount];
            var finalTangents = new Vector4[vertexCount];

            // Accumulate tangent/bitangent contributions from each triangle
            foreach (var group in groups)
            {
                for (int i = 0; i < group.Indices.Count; i += 3)
                {
                    int i0 = group.Indices[i];
                    int i1 = group.Indices[i + 1];
                    int i2 = group.Indices[i + 2];

                    Vector3 v0 = positions[i0];
                    Vector3 v1 = positions[i1];
                    Vector3 v2 = positions[i2];

                    Vector2 uv0 = uvs.Length > i0 ? uvs[i0] : Vector2.Zero;
                    Vector2 uv1 = uvs.Length > i1 ? uvs[i1] : Vector2.Zero;
                    Vector2 uv2 = uvs.Length > i2 ? uvs[i2] : Vector2.Zero;

                    Vector3 edge1 = v1 - v0;
                    Vector3 edge2 = v2 - v0;

                    Vector2 deltaUV1 = uv1 - uv0;
                    Vector2 deltaUV2 = uv2 - uv0;

                    float denom = deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y;
                    float r = Math.Abs(denom) > 1e-6f ? 1.0f / denom : 0.0f;

                    Vector3 tangent = new Vector3(
                        (deltaUV2.Y * edge1.X - deltaUV1.Y * edge2.X) * r,
                        (deltaUV2.Y * edge1.Y - deltaUV1.Y * edge2.Y) * r,
                        (deltaUV2.Y * edge1.Z - deltaUV1.Y * edge2.Z) * r);

                    Vector3 bitangent = new Vector3(
                        (deltaUV1.X * edge2.X - deltaUV2.X * edge1.X) * r,
                        (deltaUV1.X * edge2.Y - deltaUV2.X * edge1.Y) * r,
                        (deltaUV1.X * edge2.Z - deltaUV2.X * edge1.Z) * r);

                    tangents[i0] += tangent;
                    tangents[i1] += tangent;
                    tangents[i2] += tangent;

                    bitangents[i0] += bitangent;
                    bitangents[i1] += bitangent;
                    bitangents[i2] += bitangent;
                }
            }

            // Orthonormalize and compute handedness
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 n = normals[i];
                Vector3 t = tangents[i];

                // Gram-Schmidt orthogonalize
                Vector3 orthoTangent = Vector3.Normalize(t - n * Vector3.Dot(n, t));

                // Handle zero-length tangents
                if (float.IsNaN(orthoTangent.X) || orthoTangent.LengthSquared() < 1e-6f)
                {
                    orthoTangent = new Vector3(1, 0, 0); // Default tangent
                }

                // Calculate handedness (w component)
                float handedness = Vector3.Dot(Vector3.Cross(n, t), bitangents[i]) < 0.0f ? -1.0f : 1.0f;

                finalTangents[i] = new Vector4(orthoTangent, handedness);
            }

            return finalTangents;
        }

        public Dictionary<string, string> ParseMtl(string filePath)
        {
            var result = new Dictionary<string, string>();
            string currentMaterial = null;

            if (!File.Exists(filePath)) return result;

            foreach (var line in File.ReadLines(filePath))
            {
                var trim = line.Trim();
                if (string.IsNullOrWhiteSpace(trim) || trim.StartsWith('#')) continue;

                var tokens = trim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2) continue;

                if (tokens[0] == "newmtl")
                {
                    currentMaterial = tokens[1];
                }
                else if (tokens[0] == "map_Kd" && currentMaterial != null)
                {
                    string texName = Path.GetFileName(tokens[1]);
                    result[currentMaterial] = texName;
                }
            }
            return result;
        }

        private int ProcessVertexToken(
            string token,
            List<Vector3> rPos, List<Vector3> rNorm, List<Vector2> rUV,
            List<Vector3> fPos, List<Vector3> fNorm, List<Vector2> fUV,
            Dictionary<VertexKey, int> cache)
        {
            var parts = token.Split('/');

            int vIdx = ParseIndex(parts[0], rPos.Count);
            int vtIdx = (parts.Length > 1 && parts[1].Length > 0) ? ParseIndex(parts[1], rUV.Count) : 0;
            int vnIdx = (parts.Length > 2 && parts[2].Length > 0) ? ParseIndex(parts[2], rNorm.Count) : 0;

            var key = new VertexKey { V = vIdx, VT = vtIdx, VN = vnIdx };

            if (cache.TryGetValue(key, out int existingIndex))
            {
                return existingIndex;
            }

            int newIndex = fPos.Count;

            fPos.Add(vIdx > 0 && vIdx <= rPos.Count ? rPos[vIdx - 1] : Vector3.Zero);
            fUV.Add(vtIdx > 0 && vtIdx <= rUV.Count ? rUV[vtIdx - 1] : Vector2.Zero);
            fNorm.Add(vnIdx > 0 && vnIdx <= rNorm.Count ? rNorm[vnIdx - 1] : Vector3.UnitY);

            cache[key] = newIndex;
            return newIndex;
        }

        private static int ParseIndex(string s, int count)
        {
            if (int.TryParse(s, out int i))
            {
                return i < 0 ? count + i + 1 : i;
            }
            return 0;
        }

        private static float ParseFloat(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
    }
}
