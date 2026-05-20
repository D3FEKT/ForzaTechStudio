using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace ForzaTechStudio.Services
{
    // Groups all mesh data for one Modelbin for OBJ/MTL export.
    public record ModelBinExportData(
        string ModelBinName,
        Bundle? Bundle,
        List<(string Name, ForzaGeometryData Data)> Meshes);

    // Converts meshes to Wavefront OBJ / MTL text.
    public static class ObjExportService
    {
        // Builds the text content for a .obj file.
        public static string BuildObjContent(
            IEnumerable<ModelBinExportData> models,
            string mtlFileName)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();

            sb.AppendLine("# Exported by ForzaTechStudio");
            if (!string.IsNullOrEmpty(mtlFileName))
                sb.AppendLine($"mtllib {mtlFileName}");
            sb.AppendLine();

            // OBJ indices are 1-based and global across the whole file.
            int vBase  = 0;
            int vtBase = 0;
            int vnBase = 0;

            foreach (var model in models)
            {
                string safeModelName = SanitiseName(model.ModelBinName);

                // Comment identifies the source Modelbin.
                sb.AppendLine($"# {safeModelName}");
                sb.AppendLine();

                // Group mesh entries by their geometry name 
                var meshGroups = model.Meshes
                    .Where(m => m.Data?.RawPositions != null && m.Data.RawPositions.Length > 0)
                    .GroupBy(m => m.Data.Name ?? m.Name)
                    .ToList();

                foreach (var meshGroup in meshGroups)
                {
                    string safeObjName = SanitiseName(meshGroup.Key);

                    // One 'o' per unique mesh (LOD); each material becomes a 'g' group inside it.
                    sb.AppendLine($"o {safeObjName}");
                    sb.AppendLine();

                    foreach (var (_, data) in meshGroup)
                    {
                        int vertCount   = data.RawPositions.Length;
                        bool hasNormals = data.Normals != null && data.Normals.Length == vertCount;
                        bool hasUVs     = data.UVs     != null && data.UVs.Length     == vertCount;
                        bool hasIdx     = data.Indices  != null && data.Indices.Length > 0;

                        string safeMat = ExtractMaterialBaseName(data.MaterialName ?? "default");

                        // Named group for this material section within the mesh object.
                        sb.AppendLine($"g {safeMat}");
                        sb.AppendLine($"usemtl {safeMat}");
                        sb.AppendLine();

                        // World-space position reconstruction: raw * PositionScale, then rotate, translate, then apply bone transform.
                        var scale     = data.SourceMesh?.PositionScale     ?? Vector4.One;
                        var translate = data.SourceMesh?.PositionTranslate ?? Vector4.Zero;
                        var rotMatrix = data.GetRotationMatrix();
                        bool hasRot   = rotMatrix != Matrix4x4.Identity;
                        bool hasBone  = data.BoneTransform != Matrix4x4.Identity;

                        foreach (var raw in data.RawPositions)
                        {
                            var scaled = new Vector3(raw.X * scale.X, raw.Y * scale.Y, raw.Z * scale.Z);
                            if (hasRot)
                                scaled = Vector3.Transform(scaled, rotMatrix);

                            var v = new Vector3(scaled.X + translate.X, scaled.Y + translate.Y, scaled.Z + translate.Z);
                            var world = hasBone ? Vector3.Transform(v, data.BoneTransform) : v;

                            sb.AppendLine($"v {world.X.ToString("F6", ci)} {world.Y.ToString("F6", ci)} {world.Z.ToString("F6", ci)}");
                        }

                        // Texture coordinates
                        if (hasUVs)
                            foreach (var uv in data.UVs)
                                sb.AppendLine($"vt {uv.X.ToString("F6", ci)} {uv.Y.ToString("F6", ci)}");

                        // Normals
                        if (hasNormals)
                        {
                            foreach (var n in data.Normals)
                            {
                                var rn = n;
                                if (hasRot)
                                    rn = Vector3.Normalize(Vector3.TransformNormal(rn, rotMatrix));
                                if (hasBone)
                                    rn = Vector3.Normalize(Vector3.TransformNormal(rn, data.BoneTransform));
                                sb.AppendLine($"vn {rn.X.ToString("F6", ci)} {rn.Y.ToString("F6", ci)} {rn.Z.ToString("F6", ci)}");
                            }
                        }

                        sb.AppendLine();

                        // Faces
                        if (hasIdx)
                        {
                            WriteFaces(sb, data.Indices, vBase, vtBase, vnBase, hasUVs, hasNormals);
                        }
                        else
                        {
                            // No index buffer - generate sequential triangle indices.
                            var seq = new int[vertCount];
                            for (int i = 0; i < vertCount; i++) seq[i] = i;
                            WriteFaces(sb, seq, vBase, vtBase, vnBase, hasUVs, hasNormals);
                        }

                        sb.AppendLine();

                        vBase  += vertCount;
                        if (hasUVs)     vtBase += vertCount;
                        if (hasNormals) vnBase += vertCount;
                    }
                }
            }

            return sb.ToString();
        }

        // Builds the text content for a companion .mtl file.

        public static string BuildMtlContent(
            IEnumerable<ModelBinExportData> models,
            IReadOnlyDictionary<string, string>? texturePaths = null)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# Exported by ForzaTechStudio");
            sb.AppendLine();

            // Map: sanitised material name to optional preview colour (first match wins).
            var seen = new Dictionary<string, Vector3?>(StringComparer.OrdinalIgnoreCase);

            foreach (var model in models)
            {
                var previewColors = BuildPreviewColorMap(model.Bundle);

                foreach (var (_, data) in model.Meshes)
                {
                    if (data == null) continue;

                    string rawMat  = data.MaterialName ?? "default";
                    string safeMat = ExtractMaterialBaseName(rawMat);
                    if (seen.ContainsKey(safeMat)) continue;

                    Vector3? color = previewColors.TryGetValue(rawMat, out var found) ? found : null;
                    seen[safeMat] = color;
                }
            }

            foreach (var kvp in seen)
            {
                string name  = kvp.Key;
                var    color = kvp.Value ?? new Vector3(0.8f, 0.8f, 0.8f);

                sb.AppendLine($"newmtl {name}");
                sb.AppendLine("Ka 0.000000 0.000000 0.000000");
                sb.AppendLine($"Kd {color.X.ToString("F6", ci)} {color.Y.ToString("F6", ci)} {color.Z.ToString("F6", ci)}");
                if (texturePaths != null && texturePaths.TryGetValue(name, out var texPath))
                    sb.AppendLine($"map_Kd {texPath}");
                sb.AppendLine("Ks 0.000000 0.000000 0.000000");
                sb.AppendLine("illum 2");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // Internal helpers

        private static void WriteFaces(
            StringBuilder sb,
            int[] indices,
            int vBase, int vtBase, int vnBase,
            bool hasUVs, bool hasNormals)
        {
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int i0 = indices[i]     + vBase + 1;
                int i1 = indices[i + 1] + vBase + 1;
                int i2 = indices[i + 2] + vBase + 1;

                if (hasNormals && hasUVs)
                {
                    int t0 = indices[i]     + vtBase + 1;
                    int t1 = indices[i + 1] + vtBase + 1;
                    int t2 = indices[i + 2] + vtBase + 1;
                    int n0 = indices[i]     + vnBase + 1;
                    int n1 = indices[i + 1] + vnBase + 1;
                    int n2 = indices[i + 2] + vnBase + 1;
                    sb.AppendLine($"f {i0}/{t0}/{n0} {i1}/{t1}/{n1} {i2}/{t2}/{n2}");
                }
                else if (hasUVs)
                {
                    int t0 = indices[i]     + vtBase + 1;
                    int t1 = indices[i + 1] + vtBase + 1;
                    int t2 = indices[i + 2] + vtBase + 1;
                    sb.AppendLine($"f {i0}/{t0} {i1}/{t1} {i2}/{t2}");
                }
                else if (hasNormals)
                {
                    int n0 = indices[i]     + vnBase + 1;
                    int n1 = indices[i + 1] + vnBase + 1;
                    int n2 = indices[i + 2] + vnBase + 1;
                    sb.AppendLine($"f {i0}//{n0} {i1}//{n1} {i2}//{n2}");
                }
                else
                {
                    sb.AppendLine($"f {i0} {i1} {i2}");
                }
            }
        }

        // Builds a material-name 
        // e.g. "game:\media\materials\BODYWORK.materialbin" to "BODYWORK".
        private static Dictionary<string, Vector3> BuildPreviewColorMap(Bundle? bundle)
        {
            var map = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            if (bundle == null) return map;

            foreach (var blob in bundle.Blobs.OfType<ManufacturerColorsBlob>())
            {
                foreach (var group in blob.Groups)
                {
                    foreach (var entry in group.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Path)) continue;

                        // Paths use either \ or / separators.
                        var segments = entry.Path.Split('\\', '/');
                        var fileName = segments[segments.Length - 1];
                        var matName  = Path.GetFileNameWithoutExtension(fileName);

                        if (!string.IsNullOrEmpty(matName) && !map.ContainsKey(matName))
                            map[matName] = entry.PreviewColor;
                    }
                }
            }

            return map;
        }

        // Extracts a clean material base name from a full asset path.

        internal static string ExtractMaterialBaseName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "default";
            // Split on path separators to get the last segment
            var segments = rawName.Split('\\', '/');
            var leaf = segments[segments.Length - 1];
            // Strip known material extensions
            var name = Path.GetFileNameWithoutExtension(leaf);
            return SanitiseName(string.IsNullOrWhiteSpace(name) ? rawName : name);
        }

        private static string SanitiseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed";
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsWhiteSpace(c) || c == '#' || c == '\\' || c == '/')
                    chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
