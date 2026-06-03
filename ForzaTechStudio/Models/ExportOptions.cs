using System;
using System.Collections.Generic;
using System.Numerics;
using ForzaTools.Bundles.Blobs;

namespace ForzaTechStudio.Models
{
    public enum ExportUpAxis { YUp, ZUp }

    public enum ExportTextureFormat { Dds, Png, Jpg, Tga }

    public enum ExportUvMode { AllChannels, PrimaryOnly, None }

    // User-configurable settings applied to viewport model exports.
    public sealed class ExportOptions
    {
        public ExportUpAxis UpAxis { get; set; } = ExportUpAxis.YUp;
        public float ScaleFactor { get; set; } = 1.0f;
        public ExportTextureFormat TextureFormat { get; set; } = ExportTextureFormat.Dds;

        // LOD filter. When AllLods is true every mesh is exported regardless of SelectedLods.
        // SelectedLods values: -1 = LODS, 0..5 = LOD0..LOD5.
        public bool AllLods { get; set; } = true;
        public HashSet<int> SelectedLods { get; set; } = new();

        public bool IncludeBones { get; set; } = false; // FBX only
        public ExportUvMode UvMode { get; set; } = ExportUvMode.AllChannels;
        public bool IncludeVertexColors { get; set; } = true;
        public bool MultiFileExport { get; set; } = false;

        // Part filter by mesh name. Empty set = export every part.
        public HashSet<string> ExcludedParts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IncludeUVs => UvMode != ExportUvMode.None;

        // Combined up-axis rotation + uniform scale applied to exported geometry.
        public Matrix4x4 GetAxisScaleMatrix()
        {
            float s = ScaleFactor <= 0f ? 1f : ScaleFactor;
            var m = Matrix4x4.CreateScale(s);
            if (UpAxis == ExportUpAxis.ZUp)
                m *= Matrix4x4.CreateRotationX(MathF.PI / 2f); // Y-up -> Z-up
            return m;
        }

        public bool HasAxisScale => UpAxis != ExportUpAxis.YUp || ScaleFactor != 1.0f;

        // True if the mesh passes the current LOD and part filters.
        public bool ShouldExportMesh(string meshName, MeshBlob? mesh)
        {
            if (!string.IsNullOrEmpty(meshName) && ExcludedParts.Contains(meshName))
                return false;
            return MatchesLod(mesh);
        }

        private bool MatchesLod(MeshBlob? mesh)
        {
            if (AllLods || SelectedLods.Count == 0 || mesh == null)
                return true;

            if (SelectedLods.Contains(-1) && mesh.LOD_LODS) return true;
            if (SelectedLods.Contains(0) && mesh.LOD_LOD0) return true;
            if (SelectedLods.Contains(1) && mesh.LOD_LOD1) return true;
            if (SelectedLods.Contains(2) && mesh.LOD_LOD2) return true;
            if (SelectedLods.Contains(3) && mesh.LOD_LOD3) return true;
            if (SelectedLods.Contains(4) && mesh.LOD_LOD4) return true;
            if (SelectedLods.Contains(5) && mesh.LOD_LOD5) return true;
            return false;
        }
    }
}
