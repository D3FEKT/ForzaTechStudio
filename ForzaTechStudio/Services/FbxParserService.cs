using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Assimp;
using Assimp.Configs;

namespace ForzaTechStudio.Services
{
    public class FbxParserService
    {
        public SceneData Parse(string filePath)
        {
            var importer = new AssimpContext();
            string importPath = filePath;
            string? tempPath = null;

            // Configure Assimp to handle the heavy lifting:

            var steps = PostProcessSteps.Triangulate |
                        PostProcessSteps.CalculateTangentSpace |
                        PostProcessSteps.GenerateSmoothNormals |
                        PostProcessSteps.JoinIdenticalVertices;

            Scene scene;
            try
            {
                tempPath = CreateFooterPatchedBinaryFbxIfNeeded(filePath);
                if (tempPath != null)
                    importPath = tempPath;

                scene = importer.ImportFile(importPath, steps);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load FBX: {ex.Message}", ex);
            }
            finally
            {
                if (tempPath != null)
                {
                    try { File.Delete(tempPath); }
                    catch { }
                }
            }

            if (scene == null || scene.SceneFlags.HasFlag(SceneFlags.Incomplete) || scene.RootNode == null)
            {
                throw new Exception("FBX file structure is invalid or incomplete.");
            }

            // Flatten data
            List<Vector3> allPositions = new();
            List<Vector3> allNormals = new();
            List<List<Vector2>> uvChannels = new();  // [channel][vertex]
            List<Vector4> allTangents = new();
            List<Vector4> allColors = new();
            List<SceneGroup> groups = new();



            foreach (var mesh in scene.Meshes)
            {
                if (!mesh.HasVertices || mesh.VertexCount <= 0)
                    continue;

                int baseVertexIndex = allPositions.Count;
                bool hasNormals = mesh.HasNormals && mesh.Normals.Count >= mesh.VertexCount;

                // 1. Vertices
                allPositions.AddRange(mesh.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z)));

                // 2. Normals
                if (hasNormals)
                    allNormals.AddRange(mesh.Normals.Select(n => new Vector3(n.X, n.Y, n.Z)));
                else
                    allNormals.AddRange(Enumerable.Repeat(Vector3.UnitY, mesh.VertexCount));

                // 3. UVs (all available channels)
                for (int c = 0; c < mesh.TextureCoordinateChannelCount; c++)
                {
                    if (!mesh.HasTextureCoords(c)) continue;

                    // Lazily create this channel and back-fill earlier meshes with zero
                    while (uvChannels.Count <= c)
                        uvChannels.Add(new List<Vector2>(Enumerable.Repeat(Vector2.Zero, baseVertexIndex)));

                    // Assimp UVs are 3D, we only need 2D
                    var channel = mesh.TextureCoordinateChannels[c];
                    if (channel.Count >= mesh.VertexCount)
                        uvChannels[c].AddRange(channel.Take(mesh.VertexCount).Select(uv => new Vector2(uv.X, uv.Y)));
                    else
                        uvChannels[c].AddRange(Enumerable.Repeat(Vector2.Zero, mesh.VertexCount));
                }

                // Pad every channel to the global vertex count (zero-fill meshes lacking a channel)
                foreach (var channel in uvChannels)
                {
                    int missing = allPositions.Count - channel.Count;
                    if (missing > 0)
                        channel.AddRange(Enumerable.Repeat(Vector2.Zero, missing));
                }

                // 4. Tangents & Handedness
                if (mesh.HasTangentBasis && hasNormals && mesh.Tangents.Count >= mesh.VertexCount && mesh.BiTangents.Count >= mesh.VertexCount)
                {
                    for (int i = 0; i < mesh.VertexCount; i++)
                    {
                        var t = mesh.Tangents[i];
                        var b = mesh.BiTangents[i];
                        var n = mesh.Normals[i];

                        // Calculate Handedness (W)
                        // Dot(Cross(Normal, Tangent), Bitangent)
                        // Assimp types need conversion
                        Vector3 tVec = new Vector3(t.X, t.Y, t.Z);
                        Vector3 bVec = new Vector3(b.X, b.Y, b.Z);
                        Vector3 nVec = new Vector3(n.X, n.Y, n.Z);

                        float w = (Vector3.Dot(Vector3.Cross(nVec, tVec), bVec) < 0.0f) ? -1.0f : 1.0f;
                        allTangents.Add(new Vector4(tVec, w));
                    }
                }
                else
                {
                    allTangents.AddRange(Enumerable.Repeat(new Vector4(1, 0, 0, 1), mesh.VertexCount));
                }

                // 5. Colors (Channel 0)
                if (mesh.HasVertexColors(0))
                {
                    allColors.AddRange(mesh.VertexColorChannels[0].Select(c => new Vector4(c.R, c.G, c.B, c.A)));
                }
                else
                {
                    allColors.AddRange(Enumerable.Repeat(Vector4.One, mesh.VertexCount));
                }

                // 6. Indices
                var indices = mesh.GetIndices();
                // Offset indices by the base vertex index of this mesh in the global list
                var offsetIndices = indices.Select(i => i + baseVertexIndex).ToList();

                // 7. Group / Material
                string matName = "Default";
                if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.MaterialCount)
                {
                    matName = scene.Materials[mesh.MaterialIndex].Name;
                }

                groups.Add(new SceneGroup
                {
                    Name = mesh.Name ?? $"Mesh_{groups.Count}",
                    MaterialName = matName,
                    Indices = offsetIndices
                });
            }

            return new SceneData
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Positions = allPositions.ToArray(),
                Normals = allNormals.ToArray(),
                UVChannels = uvChannels.Select(c => c.ToArray()).ToArray(),
                Tangents = allTangents.ToArray(),
                Colors = allColors.ToArray(),
                Groups = groups,
                MaterialLib = string.Empty // FBX embeds materials
            };
        }

        private static string? CreateFooterPatchedBinaryFbxIfNeeded(string filePath)
        {
            if (!IsBinaryFbx(filePath) || HasBinaryFooter(filePath))
                return null;

            string tempPath = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(filePath)}_{Guid.NewGuid():N}.fbx");
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
            if (length < footerEnd.Length)
                return false;

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