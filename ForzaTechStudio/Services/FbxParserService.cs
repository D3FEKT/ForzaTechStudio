using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Assimp;
using Assimp.Configs;

namespace ForzaTechStudio.Services
{
    public class FbxParserService
    {
        public SceneData Parse(string filePath)
        {
            var importer = new AssimpContext();

            // Configure Assimp to handle the heavy lifting:

            var steps = PostProcessSteps.Triangulate |
                        PostProcessSteps.CalculateTangentSpace |
                        PostProcessSteps.GenerateSmoothNormals |
                        PostProcessSteps.JoinIdenticalVertices;

            Scene scene;
            try
            {
                scene = importer.ImportFile(filePath, steps);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load FBX: {ex.Message}", ex);
            }

            if (scene == null || scene.SceneFlags.HasFlag(SceneFlags.Incomplete) || scene.RootNode == null)
            {
                throw new Exception("FBX file structure is invalid or incomplete.");
            }

            // Flatten data
            List<Vector3> allPositions = new();
            List<Vector3> allNormals = new();
            List<Vector2> allUVs = new();
            List<Vector4> allTangents = new();
            List<Vector4> allColors = new();
            List<SceneGroup> groups = new();



            foreach (var mesh in scene.Meshes)
            {
                int baseVertexIndex = allPositions.Count;

                // 1. Vertices
                if (mesh.HasVertices)
                    allPositions.AddRange(mesh.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z)));

                // 2. Normals
                if (mesh.HasNormals)
                    allNormals.AddRange(mesh.Normals.Select(n => new Vector3(n.X, n.Y, n.Z)));
                else
                    allNormals.AddRange(Enumerable.Repeat(Vector3.UnitY, mesh.VertexCount));

                // 3. UVs (Channel 0)
                if (mesh.HasTextureCoords(0))
                {
                    // Assimp UVs are 3D, we only need 2D
                    allUVs.AddRange(mesh.TextureCoordinateChannels[0].Select(uv => new Vector2(uv.X, uv.Y)));
                }
                else
                {
                    allUVs.AddRange(Enumerable.Repeat(Vector2.Zero, mesh.VertexCount));
                }

                // 4. Tangents & Handedness
                if (mesh.HasTangentBasis)
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
                UVs = allUVs.ToArray(),
                Tangents = allTangents.ToArray(),
                Colors = allColors.ToArray(),
                Groups = groups,
                MaterialLib = null // FBX embeds materials
            };
        }
    }
}