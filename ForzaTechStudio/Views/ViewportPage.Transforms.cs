using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using SDX = SharpDX;

namespace ForzaTechStudio.Views
{
    // Transform Editing and Saving Methods
    public sealed partial class ViewportPage : Page
    {
        // Finds all sibling MeshNodes (other LODs, shadow copies) that share the same
        // SourceMeshIndex within the same parent ModelBinNode.
        // Returns only siblings NOT already in the provided set.
        private List<MeshNode> CollectSiblingMeshNodes(MeshNode mesh, HashSet<MeshNode> excludeSet)
        {
            var siblings = new List<MeshNode>();
            if (mesh.GeometryData?.SourceMesh == null) return siblings;

            var parentBin = mesh.ParentModelBin ?? mesh.Parent as ModelBinNode;
            if (parentBin == null)
            {
                var p = mesh.Parent;
                while (p != null && p is not ModelBinNode)
                    p = p.Parent;
                parentBin = p as ModelBinNode;
            }
            if (parentBin == null) return siblings;

            var sourceMeshIndex = mesh.GeometryData.SourceMesh.SourceMeshIndex;
            foreach (var child in parentBin.Children)
            {
                if (child is MeshNode sibling && sibling != mesh && !excludeSet.Contains(sibling))
                {
                    if (sibling.GeometryData?.SourceMesh != null &&
                        sibling.GeometryData.SourceMesh.SourceMeshIndex == sourceMeshIndex)
                    {
                        siblings.Add(sibling);
                    }
                }
            }
            return siblings;
        }

        private void Transform_TextChanged(object sender, TextChangedEventArgs args)
        {
             if (_isUpdatingUi) return;

             if (IsTransformUiSuppressed)
                 return;

             // Mesh multi-select delta mode
             if (_isMultiSelectActive && _multiSelectedMeshes.Count > 0)
             {
                 // Parse delta values from the UI fields
                 float dSX = ParseMultiSelectScaleDelta(ScaleX.Text);
                 float dSY = ParseMultiSelectScaleDelta(ScaleY.Text);
                 float dSZ = ParseMultiSelectScaleDelta(ScaleZ.Text);
                 float dRX = float.TryParse(RotX.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdRX) ? vdRX : 0f;
                 float dRY = float.TryParse(RotY.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdRY) ? vdRY : 0f;
                 float dRZ = float.TryParse(RotZ.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdRZ) ? vdRZ : 0f;
                 float dTX = float.TryParse(TransX.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdTX) ? vdTX : 0f;
                 float dTY = float.TryParse(TransY.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdTY) ? vdTY : 0f;
                 float dTZ = float.TryParse(TransZ.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdTZ) ? vdTZ : 0f;

                 // Collect all meshes that need undo tracking (selected + their LOD/shadow siblings)
                 var selectedSet = new HashSet<MeshNode>(_multiSelectedMeshes);
                 var allAffected = new List<MeshNode>(_multiSelectedMeshes);
                 foreach (var mesh in _multiSelectedMeshes)
                 {
                     var siblings = CollectSiblingMeshNodes(mesh, selectedSet);
                     foreach (var sib in siblings)
                     {
                         if (selectedSet.Add(sib))
                             allAffected.Add(sib);
                     }
                 }

                 var multiUndoAction = BeginTransformAction(allAffected, "Multi-Select Transform");

                 foreach (var mesh in _multiSelectedMeshes)
                 {
                     if (mesh.GeometryData?.SourceMesh == null) continue;
                     if (!_multiSelectSnapshots.TryGetValue(mesh, out var snap)) continue;

                     var meshBlob = mesh.GeometryData.SourceMesh;
                     float newSX = snap.Scale.X + dSX;
                     float newSY = snap.Scale.Y + dSY;
                     float newSZ = snap.Scale.Z + dSZ;
                     float newRX = snap.Rotation.X + dRX;
                     float newRY = snap.Rotation.Y + dRY;
                     float newRZ = snap.Rotation.Z + dRZ;

                     meshBlob.PositionScale = new Vector4(newSX, newSY, newSZ, snap.Scale.W);
                     meshBlob.PositionTranslate = ApplyWorldTranslationDelta(mesh, snap.Translate, new Vector3(dTX, dTY, dTZ));
                     mesh.GeometryData.RotationEulerDegrees = new Vector3(newRX, newRY, newRZ);

                     bool hasBone = mesh.GeometryData.SourceBone != null &&
                                    BoneTransformService.IsSignificantBone(mesh.GeometryData.BoneIndex);
                     if (hasBone)
                         UpdateMeshRenderingWithBoneTransform(mesh, mesh.GeometryData.BoneTransform);
                     else
                         UpdateMeshRendering(mesh, newSX, newSY, newSZ,
                             meshBlob.PositionTranslate.X, meshBlob.PositionTranslate.Y, meshBlob.PositionTranslate.Z);

                     // Propagate to sibling meshes (other LODs, shadow copies)
                     var siblings = CollectSiblingMeshNodes(mesh, new HashSet<MeshNode>(_multiSelectedMeshes));
                     foreach (var sibling in siblings)
                     {
                         if (sibling.GeometryData?.SourceMesh == null) continue;
                         var sibBlob = sibling.GeometryData.SourceMesh;
                         sibBlob.PositionScale = new Vector4(newSX, newSY, newSZ, sibBlob.PositionScale.W);
                         sibBlob.PositionTranslate = ApplyWorldTranslationDelta(sibling, snap.Translate, new Vector3(dTX, dTY, dTZ));
                         sibling.GeometryData.RotationEulerDegrees = new Vector3(newRX, newRY, newRZ);

                         bool sibHasBone = sibling.GeometryData.SourceBone != null &&
                                           BoneTransformService.IsSignificantBone(sibling.GeometryData.BoneIndex);
                         if (sibHasBone)
                             UpdateMeshRenderingWithBoneTransform(sibling, sibling.GeometryData.BoneTransform);
                         else
                             UpdateMeshRendering(sibling, newSX, newSY, newSZ,
                                 sibBlob.PositionTranslate.X, sibBlob.PositionTranslate.Y, sibBlob.PositionTranslate.Z);
                     }
                 }

                 CommitTransformAction(multiUndoAction);
                 RefreshHighlight();
                 RefreshOverlays();
                 return;
             }

             // ModelBin/material/all-mesh conflicting scope delta mode.
             if (_isModelScopeDeltaTransformActive && _modelScopeDeltaMeshes.Count > 0)
             {
                 float dSX = float.TryParse(ScaleX.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdSX) ? vdSX : 0f;
                 float dSY = float.TryParse(ScaleY.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdSY) ? vdSY : 0f;
                 float dSZ = float.TryParse(ScaleZ.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdSZ) ? vdSZ : 0f;
                 float dRX = float.TryParse(RotX.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdRX) ? vdRX : 0f;
                 float dRY = float.TryParse(RotY.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdRY) ? vdRY : 0f;
                 float dRZ = float.TryParse(RotZ.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdRZ) ? vdRZ : 0f;
                 float dTX = float.TryParse(TransX.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdTX) ? vdTX : 0f;
                 float dTY = float.TryParse(TransY.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdTY) ? vdTY : 0f;
                 float dTZ = float.TryParse(TransZ.Text, System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out float vdTZ) ? vdTZ : 0f;

                 var targetMeshes = _modelScopeDeltaMeshes
                     .Where(mesh => mesh.GeometryData?.SourceMesh != null && _modelScopeDeltaSnapshots.ContainsKey(mesh))
                     .Distinct()
                     .ToList();

                 var scopeUndoAction = BeginTransformAction(targetMeshes, "ModelBin Scope Transform");

                 foreach (var mesh in targetMeshes)
                 {
                     if (mesh.GeometryData?.SourceMesh == null) continue;
                     if (!_modelScopeDeltaSnapshots.TryGetValue(mesh, out var snap)) continue;

                     var meshBlob = mesh.GeometryData.SourceMesh;
                     float newSX = snap.Scale.X + dSX;
                     float newSY = snap.Scale.Y + dSY;
                     float newSZ = snap.Scale.Z + dSZ;
                     float newRX = snap.Rotation.X + dRX;
                     float newRY = snap.Rotation.Y + dRY;
                     float newRZ = snap.Rotation.Z + dRZ;

                     meshBlob.PositionScale = new Vector4(newSX, newSY, newSZ, snap.Scale.W);
                     meshBlob.PositionTranslate = ApplyWorldTranslationDelta(mesh, snap.Translate, new Vector3(dTX, dTY, dTZ));
                     mesh.GeometryData.RotationEulerDegrees = new Vector3(newRX, newRY, newRZ);

                     bool hasBone = mesh.GeometryData.SourceBone != null &&
                                    BoneTransformService.IsSignificantBone(mesh.GeometryData.BoneIndex);
                     if (hasBone)
                         UpdateMeshRenderingWithBoneTransform(mesh, mesh.GeometryData.BoneTransform);
                     else
                         UpdateMeshRendering(mesh, newSX, newSY, newSZ,
                             meshBlob.PositionTranslate.X, meshBlob.PositionTranslate.Y, meshBlob.PositionTranslate.Z);
                 }

                 CommitTransformAction(scopeUndoAction);
                 RefreshHighlight();
                 RefreshOverlays();
                 return;
             }

             if (ModelBinSelector.SelectedItem is not ModelBinNode modelBin) return;
             var meshScope = MeshSelector.SelectedItem as MeshScopeItem;
             if (meshScope == null) return;

             // Determine affected meshes
             IEnumerable<MeshNode> affectedMeshes;
             if (meshScope.Node != null)
             {
                 affectedMeshes = new[] { meshScope.Node };
             }
             else if (!string.IsNullOrEmpty(meshScope.MaterialGroup))
             {
                 affectedMeshes = modelBin.Children.OfType<MeshNode>()
                     .Where(m => m.GeometryData?.MaterialName == meshScope.MaterialGroup);
             }
             else
             {
                 affectedMeshes = modelBin.Children.OfType<MeshNode>();
             }

             var meshList = affectedMeshes.ToList();

             // Begin undo tracking
             var undoAction = BeginTransformAction(meshList, "Transform Edit");

             // Parse new values (nullable for conflict handling)
             float? sx = float.TryParse(ScaleX.Text, out float vSX) ? vSX : null;
             float? sy = float.TryParse(ScaleY.Text, out float vSY) ? vSY : null;
             float? sz = float.TryParse(ScaleZ.Text, out float vSZ) ? vSZ : null;
             float? rx = float.TryParse(RotX.Text, out float vRX) ? vRX : null;
             float? ry = float.TryParse(RotY.Text, out float vRY) ? vRY : null;
             float? rz = float.TryParse(RotZ.Text, out float vRZ) ? vRZ : null;
             float? tx = float.TryParse(TransX.Text, out float vTX) ? vTX : null;
             float? ty = float.TryParse(TransY.Text, out float vTY) ? vTY : null;
             float? tz = float.TryParse(TransZ.Text, out float vTZ) ? vTZ : null;

             // Update each affected mesh
             foreach (var mesh in meshList)
             {
                 if (mesh.GeometryData?.SourceMesh == null) continue;
                 
                 var meshBlob = mesh.GeometryData.SourceMesh;
                 
                 // Get current values
                 var currentScale = meshBlob.PositionScale;
                 var currentTrans = meshBlob.PositionTranslate;
                 
                 // Calculate new values (use current if field is conflicted/empty)
                 float newSX = sx ?? currentScale.X;
                 float newSY = sy ?? currentScale.Y;
                 float newSZ = sz ?? currentScale.Z;
                 
                 // Rotation
                 var currentRot = mesh.GeometryData.RotationEulerDegrees;
                 float newRX = rx ?? currentRot.X;
                 float newRY = ry ?? currentRot.Y;
                 float newRZ = rz ?? currentRot.Z;
                 
                 float newTX = tx ?? currentTrans.X;
                 float newTY = ty ?? currentTrans.Y;
                 float newTZ = tz ?? currentTrans.Z;

                 // Check if values actually changed to avoid unnecessary updates
                 bool scaleChanged = Math.Abs(newSX - currentScale.X) > 0.0001f ||
                                    Math.Abs(newSY - currentScale.Y) > 0.0001f ||
                                    Math.Abs(newSZ - currentScale.Z) > 0.0001f;
                 
                 bool rotChanged = Math.Abs(newRX - currentRot.X) > 0.0001f ||
                                   Math.Abs(newRY - currentRot.Y) > 0.0001f ||
                                   Math.Abs(newRZ - currentRot.Z) > 0.0001f;
                                    
                 bool transChanged = Math.Abs(newTX - currentTrans.X) > 0.0001f ||
                                    Math.Abs(newTY - currentTrans.Y) > 0.0001f ||
                                    Math.Abs(newTZ - currentTrans.Z) > 0.0001f;

                 if (!scaleChanged && !rotChanged && !transChanged)
                 {
                     // No changes, skip this mesh
                     continue;
                 }

                 // Check if mesh has a bone
                 bool hasBone = mesh.GeometryData.SourceBone != null && 
                                BoneTransformService.IsSignificantBone(mesh.GeometryData.BoneIndex);

                 // Update scale if changed
                 if (scaleChanged)
                 {
                     meshBlob.PositionScale = new Vector4(newSX, newSY, newSZ, currentScale.W);
                 }
                 
                 // Update rotation if changed
                 if (rotChanged)
                 {
                     mesh.GeometryData.RotationEulerDegrees = new Vector3(newRX, newRY, newRZ);
                 }
                 
                 // Update mesh translate if changed
                 if (transChanged)
                 {
                     var delta = new Vector3(newTX - currentTrans.X, newTY - currentTrans.Y, newTZ - currentTrans.Z);
                     meshBlob.PositionTranslate = ApplyWorldTranslationDelta(mesh, currentTrans, delta);
                 }

                 // Update rendering if any value changed
                 if (scaleChanged || rotChanged || transChanged)
                 {
                     if (hasBone)
                     {
                         // BONE MODE: Use current bone transform (don't reset to Identity)
                         UpdateMeshRenderingWithBoneTransform(mesh, mesh.GeometryData.BoneTransform);
                     }
                     else
                     {
                         // NON-BONE MODE: Update rendering with mesh values
                         UpdateMeshRendering(mesh, newSX, newSY, newSZ,
                             meshBlob.PositionTranslate.X, meshBlob.PositionTranslate.Y, meshBlob.PositionTranslate.Z);
                     }
                 }
             }

             // Commit undo action
             CommitTransformAction(undoAction);
             
             RefreshHighlight();
             RefreshOverlays();
        }

        private static float ParseMultiSelectScaleDelta(string text)
        {
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value))
            {
                return 0f;
            }

            return Math.Abs(value - 1f) <= 0.0001f ? 0f : value;
        }
        
        private void UpdateMeshRendering(MeshNode mesh, float sx, float sy, float sz, float tx, float ty, float tz)
        {
            if (!_renderMap.TryGetValue(mesh, out var model) || model is not MeshGeometryModel3D mesh3d)
                return;

            var geometry = mesh3d.Geometry as MeshGeometry3D;
            if (geometry == null || mesh.GeometryData?.RawPositions == null)
                return;

            var newPositions = new Vector3Collection(mesh.GeometryData.RawPositions.Length);
            var boneTransform = IsFiniteMatrix(mesh.GeometryData.BoneTransform) ? mesh.GeometryData.BoneTransform : Matrix4x4.Identity;
            var rotMatrix = mesh.GeometryData.GetRotationMatrix();
            bool hasRotation = rotMatrix != Matrix4x4.Identity;
            
            for (int i = 0; i < mesh.GeometryData.RawPositions.Length; i++)
            {
                Vector3 raw = mesh.GeometryData.RawPositions[i];
                float nx = raw.X * sx;
                float ny = raw.Y * sy;
                float nz = raw.Z * sz;
                
                var scaled = new Vector3(nx, ny, nz);
                
                // Apply rotation (after scale, before translate)
                if (hasRotation)
                    scaled = Vector3.Transform(scaled, rotMatrix);
                
                var v = new Vector3(scaled.X + tx, scaled.Y + ty, scaled.Z + tz);
                var transformed = Vector3.Transform(v, boneTransform);

                if (!IsFiniteVector(transformed))
                    transformed = IsFiniteVector(v) ? v : Vector3.Zero;
                
                newPositions.Add(new SDX.Vector3(transformed.X, transformed.Y, transformed.Z));
            }
            
            geometry.Positions = newPositions;

            UpdateGeometryNormals(geometry, mesh.GeometryData, boneTransform);

            geometry.UpdateBounds();
        }

        private void UpdateMeshRenderingWithBoneTransform(MeshNode mesh, Matrix4x4 newBoneMatrix)
        {
            if (!_renderMap.TryGetValue(mesh, out var model) || model is not MeshGeometryModel3D mesh3d)
                return;
                
            var geometry = mesh3d.Geometry as MeshGeometry3D;
            if (geometry == null || mesh.GeometryData?.RawPositions == null) 
                return;

            var meshBlob = mesh.GeometryData.SourceMesh;
            if (meshBlob == null) return;

            // Sanity-check: a degenerate invBind/animatedWorld can produce NaN/Infinity (e.g. non-invertible GR2 bind-pose), which causes HelixToolkit to throw on UpdateBounds().
            if (!IsFiniteMatrix(newBoneMatrix)) return;

            var newPositions = new Vector3Collection(mesh.GeometryData.RawPositions.Length);
            var rotMatrix = mesh.GeometryData.GetRotationMatrix();
            bool hasRotation = rotMatrix != Matrix4x4.Identity;
            
            for (int i = 0; i < mesh.GeometryData.RawPositions.Length; i++)
            {
                Vector3 raw = mesh.GeometryData.RawPositions[i];
                
                // Apply mesh Scale (unchanged)
                float nx = raw.X * meshBlob.PositionScale.X;
                float ny = raw.Y * meshBlob.PositionScale.Y;
                float nz = raw.Z * meshBlob.PositionScale.Z;
                
                var scaled = new Vector3(nx, ny, nz);
                
                // Apply rotation (after scale, before translate)
                if (hasRotation)
                    scaled = Vector3.Transform(scaled, rotMatrix);
                
                var v = new Vector3(scaled.X + meshBlob.PositionTranslate.X, 
                                    scaled.Y + meshBlob.PositionTranslate.Y, 
                                    scaled.Z + meshBlob.PositionTranslate.Z);
                
                // Apply NEW bone transform
                var transformed = Vector3.Transform(v, newBoneMatrix);

                // Guard individual vertex ? a single bad input vertex shouldn't crash the whole mesh.
                if (!float.IsFinite(transformed.X) || !float.IsFinite(transformed.Y) || !float.IsFinite(transformed.Z))
                    transformed = v; // fall back to un-transformed position

                newPositions.Add(new SDX.Vector3(transformed.X, transformed.Y, transformed.Z));
            }
            
            geometry.Positions = newPositions;

            UpdateGeometryNormals(geometry, mesh.GeometryData, newBoneMatrix);

            geometry.UpdateBounds();
        }

        private void UpdateGeometryNormals(MeshGeometry3D geometry, ForzaGeometryData data, Matrix4x4 boneTransform)
        {
            if (data.Normals == null)
                return;

            geometry.Normals = BuildTransformedNormals(data, boneTransform);
        }

        private Vector3Collection BuildTransformedNormals(ForzaGeometryData data, Matrix4x4 boneTransform)
        {
            var normals = new Vector3Collection(data.Normals?.Length ?? 0);
            if (data.Normals == null)
                return normals;

            var rotMatrix = data.GetRotationMatrix();
            bool hasRotation = rotMatrix != Matrix4x4.Identity;
            bool hasBoneTransform = IsFiniteMatrix(boneTransform) && boneTransform != Matrix4x4.Identity;

            // Pre-compute the inverse-transpose of the bone matrix for correct normal transformation.
            // Vector3.TransformNormal uses the upper-left 3x3 as-is, which is only correct for
            // orthogonal matrices (pure rotation). For matrices with any scale, normals must be
            // transformed by the inverse-transpose to preserve perpendicularity to the surface.
            Matrix4x4 boneNormalMatrix = Matrix4x4.Identity;
            if (hasBoneTransform)
            {
                if (Matrix4x4.Invert(boneTransform, out var invBone))
                    boneNormalMatrix = Matrix4x4.Transpose(invBone);
                else
                    boneNormalMatrix = boneTransform; // fallback to original if non-invertible
            }

            foreach (var normal in data.Normals)
            {
                var transformed = normal;
                if (hasRotation)
                    transformed = Vector3.TransformNormal(transformed, rotMatrix);
                if (hasBoneTransform)
                    transformed = Vector3.TransformNormal(transformed, boneNormalMatrix);

                var normalized = NormalizeOrDefault(transformed, Vector3.UnitY);
                normals.Add(new SDX.Vector3(normalized.X, normalized.Y, normalized.Z));
            }

            return normals;
        }

        private Vector3 ConvertWorldTranslationDeltaToMeshLocal(ForzaGeometryData geometry, Vector3 worldDelta)
        {
            if (geometry.SourceBone == null ||
                !BoneTransformService.IsSignificantBone(geometry.BoneIndex) ||
                !IsFiniteMatrix(geometry.BoneTransform) ||
                !Matrix4x4.Invert(geometry.BoneTransform, out var inverseBone))
            {
                return worldDelta;
            }

            return Vector3.TransformNormal(worldDelta, inverseBone);
        }

        private Vector4 ApplyWorldTranslationDelta(MeshNode mesh, Vector4 baseTranslate, Vector3 worldDelta)
        {
            var localDelta = ConvertWorldTranslationDeltaToMeshLocal(mesh.GeometryData, worldDelta);
            return new Vector4(
                baseTranslate.X + localDelta.X,
                baseTranslate.Y + localDelta.Y,
                baseTranslate.Z + localDelta.Z,
                baseTranslate.W);
        }

        // Returns true only if every element of the matrix is a finite (non-NaN, non-Inf) float.
        private static bool IsFiniteMatrix(Matrix4x4 m)
        {
            return float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14)
                && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24)
                && float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34)
                && float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
        }

        private static bool IsFiniteVector(Vector3 v)
        {
            return float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        }

        private static Vector3 NormalizeOrDefault(Vector3 v, Vector3 fallback)
        {
            if (!IsFiniteVector(v) || v.LengthSquared() <= 1e-12f)
                return fallback;

            var normalized = Vector3.Normalize(v);
            return IsFiniteVector(normalized) ? normalized : fallback;
        }
        
        private void ResetTransform_Click(object sender, RoutedEventArgs e)
        {
             // ?? Multi-select reset ??
             if (_isMultiSelectActive && _multiSelectedMeshes.Count > 0)
             {
                 // Collect all affected meshes (selected + their LOD/shadow siblings)
                 var selectedSet = new HashSet<MeshNode>(_multiSelectedMeshes);
                 var allAffected = new List<MeshNode>(_multiSelectedMeshes);
                 foreach (var mesh in _multiSelectedMeshes)
                 {
                     var siblings = CollectSiblingMeshNodes(mesh, selectedSet);
                     foreach (var sib in siblings)
                     {
                         if (selectedSet.Add(sib))
                             allAffected.Add(sib);
                     }
                 }

                 var multiResetAction = BeginTransformAction(allAffected, "Multi-Select Reset");
                 _isUpdatingUi = true;

                 foreach (var mesh in _multiSelectedMeshes)
                 {
                     if (mesh.GeometryData?.SourceMesh == null) continue;

                     var meshBlob = mesh.GeometryData.SourceMesh;
                     meshBlob.PositionScale = mesh.OriginalPositionScale;
                     meshBlob.PositionTranslate = mesh.OriginalPositionTranslate;
                     mesh.GeometryData.RotationEulerDegrees = mesh.OriginalRotationEulerDegrees;

                     bool hasBone = mesh.GeometryData.SourceBone != null &&
                                    BoneTransformService.IsSignificantBone(mesh.GeometryData.BoneIndex);
                     if (hasBone)
                         UpdateMeshRenderingWithBoneTransform(mesh, mesh.GeometryData.OriginalBoneTransform);
                     else
                         UpdateMeshRendering(mesh, mesh.OriginalPositionScale.X, mesh.OriginalPositionScale.Y, mesh.OriginalPositionScale.Z,
                                            mesh.OriginalPositionTranslate.X, mesh.OriginalPositionTranslate.Y, mesh.OriginalPositionTranslate.Z);

                     // Reset sibling meshes (other LODs, shadow copies)
                     var siblings = CollectSiblingMeshNodes(mesh, new HashSet<MeshNode>(_multiSelectedMeshes));
                     foreach (var sibling in siblings)
                     {
                         if (sibling.GeometryData?.SourceMesh == null) continue;
                         var sibBlob = sibling.GeometryData.SourceMesh;
                         sibBlob.PositionScale = sibling.OriginalPositionScale;
                         sibBlob.PositionTranslate = sibling.OriginalPositionTranslate;
                         sibling.GeometryData.RotationEulerDegrees = sibling.OriginalRotationEulerDegrees;

                         bool sibHasBone = sibling.GeometryData.SourceBone != null &&
                                           BoneTransformService.IsSignificantBone(sibling.GeometryData.BoneIndex);
                         if (sibHasBone)
                             UpdateMeshRenderingWithBoneTransform(sibling, sibling.GeometryData.OriginalBoneTransform);
                         else
                             UpdateMeshRendering(sibling, sibling.OriginalPositionScale.X, sibling.OriginalPositionScale.Y, sibling.OriginalPositionScale.Z,
                                                sibling.OriginalPositionTranslate.X, sibling.OriginalPositionTranslate.Y, sibling.OriginalPositionTranslate.Z);
                     }
                 }

                 // Re-snapshot and reset UI fields to 0
                 SnapshotMultiSelectValues();
                 EnableAndSet(ScaleX, "0.00000");
                 EnableAndSet(ScaleY, "0.00000");
                 EnableAndSet(ScaleZ, "0.00000");
                 EnableAndSet(RotX, "0.00000");
                 EnableAndSet(RotY, "0.00000");
                 EnableAndSet(RotZ, "0.00000");
                 EnableAndSet(TransX, "0.00000");
                 EnableAndSet(TransY, "0.00000");
                 EnableAndSet(TransZ, "0.00000");

                 _isUpdatingUi = false;
                 CommitTransformAction(multiResetAction);
                 RefreshHighlight();
                 RefreshOverlays();
                 return;
             }

             var meshScope = MeshSelector.SelectedItem as MeshScopeItem;
             if (meshScope == null) return;
             
             var modelBin = ModelBinSelector.SelectedItem as ModelBinNode;
             if (modelBin == null) return;

             // Determine affected meshes
             IEnumerable<MeshNode> affectedMeshes;
             if (meshScope.Node != null)
             {
                 affectedMeshes = new[] { meshScope.Node };
             }
             else if (!string.IsNullOrEmpty(meshScope.MaterialGroup))
             {
                 affectedMeshes = modelBin.Children.OfType<MeshNode>()
                     .Where(m => m.GeometryData?.MaterialName == meshScope.MaterialGroup);
             }
             else
             {
                 affectedMeshes = modelBin.Children.OfType<MeshNode>();
             }

             var meshList = affectedMeshes.ToList();

             // Begin undo tracking
             var undoAction = BeginTransformAction(meshList, "Reset Transform");

             _isUpdatingUi = true;

             foreach (var mesh in meshList)
             {
                 var geometry = mesh.GeometryData;
                 if (geometry?.SourceMesh == null) continue;
                 
                 var meshBlob = geometry.SourceMesh;
                 var sourceBone = geometry.SourceBone;
                 
                 // Reset scale, rotation, and translate
                 meshBlob.PositionScale = mesh.OriginalPositionScale;
                 meshBlob.PositionTranslate = mesh.OriginalPositionTranslate;
                 geometry.RotationEulerDegrees = mesh.OriginalRotationEulerDegrees;

                 if (sourceBone != null && BoneTransformService.IsSignificantBone(geometry.BoneIndex))
                 {
                     // BONE MODE: Reset bone to original transform
                     sourceBone.Matrix = geometry.OriginalBoneTransform;
                     
                     // Update cached bone transform
                     geometry.BoneTransform = geometry.OriginalBoneTransform;
                     
                     // Update rendering with original bone matrix
                     UpdateMeshRenderingWithBoneTransform(mesh, geometry.OriginalBoneTransform);
                 }
                 else
                 {
                     // NON-BONE MODE: Update rendering with mesh values
                     UpdateMeshRendering(mesh, 
                         meshBlob.PositionScale.X, 
                         meshBlob.PositionScale.Y, 
                         meshBlob.PositionScale.Z,
                         meshBlob.PositionTranslate.X, 
                         meshBlob.PositionTranslate.Y, 
                         meshBlob.PositionTranslate.Z);
                 }
             }

             _isUpdatingUi = false;

             // Commit undo action
             CommitTransformAction(undoAction);

             UpdateTransformUI();
             RefreshHighlight();
             RefreshOverlays();
        }
        
        // Bakes rotation into vertex positions and updates PositionScale/PositionTranslate.
        // Required before saving since the modelbin format has no rotation field.
        private void BakeRotationsIntoVertexData(ModelBinNode modelBin)
        {
            var bundle = modelBin.Bundle;
            var layouts = bundle.Blobs.OfType<VertexLayoutBlob>().ToArray();

            // Build vertex buffer map (same logic as ModelImporter)
            var vbMap = new Dictionary<int, VertexBufferBlob>();
            foreach (var vb in bundle.Blobs.OfType<VertexBufferBlob>())
            {
                var idMeta = vb.Metadatas.OfType<IdentifierMetadata>().FirstOrDefault();
                if (idMeta != null) vbMap[(int)idMeta.Id] = vb;
            }

            foreach (var child in modelBin.Children)
            {
                if (child is not MeshNode meshNode) continue;
                var geo = meshNode.GeometryData;
                if (geo?.SourceMesh == null || geo.RawPositions == null) continue;
                if (geo.RotationEulerDegrees == Vector3.Zero) continue;

                var meshBlob = geo.SourceMesh;
                var rotMatrix = geo.GetRotationMatrix();
                int vertexCount = geo.RawPositions.Length;

                System.Diagnostics.Debug.WriteLine(
                    $"[Viewport/Bake] Baking rotation ({geo.RotationEulerDegrees.X:F1}°, {geo.RotationEulerDegrees.Y:F1}°, {geo.RotationEulerDegrees.Z:F1}°) " +
                    $"for mesh '{meshNode.Name}' ({vertexCount} vertices, normals={geo.Normals?.Length ?? 0}, " +
                    $"posFormat={geo.SourceMesh.VertexLayoutIndex}, minVertexIndex={geo.MinVertexIndex})");

                // Step 1: Compute rotated-scaled positions (before translate)
                var rotatedScaled = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    var raw = geo.RawPositions[i];
                    float sx = raw.X * meshBlob.PositionScale.X;
                    float sy = raw.Y * meshBlob.PositionScale.Y;
                    float sz = raw.Z * meshBlob.PositionScale.Z;
                    rotatedScaled[i] = Vector3.Transform(new Vector3(sx, sy, sz), rotMatrix);
                }

                // Step 2: Compute new AABB analytically from the 8 corners of the original bounding box.

                var bboxMin = new Vector3(float.MaxValue);
                var bboxMax = new Vector3(float.MinValue);
                for (int cx = -1; cx <= 1; cx += 2)
                for (int cy = -1; cy <= 1; cy += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    var corner = Vector3.Transform(
                        new Vector3(cx * meshBlob.PositionScale.X,
                                    cy * meshBlob.PositionScale.Y,
                                    cz * meshBlob.PositionScale.Z),
                        rotMatrix);
                    bboxMin = Vector3.Min(bboxMin, corner);
                    bboxMax = Vector3.Max(bboxMax, corner);
                }

                // Step 3: Compute new scale (half-extent) and translate (center)
                // Keep the old translate as an offset 
                var newScale = (bboxMax - bboxMin) / 2f;
                var center = (bboxMax + bboxMin) / 2f;
                var newTranslate = new Vector3(
                    center.X + meshBlob.PositionTranslate.X,
                    center.Y + meshBlob.PositionTranslate.Y,
                    center.Z + meshBlob.PositionTranslate.Z);

                // Guard against degenerate scale (e.g. flat mesh on an axis)
                if (newScale.X < 1e-7f) newScale = new Vector3(1e-7f, newScale.Y, newScale.Z);
                if (newScale.Y < 1e-7f) newScale = new Vector3(newScale.X, 1e-7f, newScale.Z);
                if (newScale.Z < 1e-7f) newScale = new Vector3(newScale.X, newScale.Y, 1e-7f);

                // Step 4: Compute new raw positions in [-1, 1] range
                var newRawPositions = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    newRawPositions[i] = new Vector3(
                        (rotatedScaled[i].X - center.X) / newScale.X,
                        (rotatedScaled[i].Y - center.Y) / newScale.Y,
                        (rotatedScaled[i].Z - center.Z) / newScale.Z);
                }

                // Step 5: Write SNORM16 values back to vertex buffer
                // Also rotate normals and pass them so POSITION.W, which stores normal X, is updated.
                Vector3[]? rotatedNormals = null;
                if (geo.Normals != null && geo.Normals.Length == vertexCount)
                {
                    rotatedNormals = new Vector3[vertexCount];
                    for (int i = 0; i < vertexCount; i++)
                        rotatedNormals[i] = NormalizeOrDefault(Vector3.TransformNormal(geo.Normals[i], rotMatrix), Vector3.UnitY);
                }

                int minVertexIndex = geo.MinVertexIndex;
                var layout = ResolveVertexLayout(bundle, layouts, meshBlob);
                bool splitNormalX = layout != null && GetLayoutElements(layout).Any(item =>
                    item.Semantic == "NORMAL" && item.Format == 37);

                bool wrote = WritePositionsToVertexBuffer(bundle, meshBlob, layout, vbMap, minVertexIndex, newRawPositions,
                    splitNormalX ? rotatedNormals : null);
                if (!wrote)
                {
                    System.Diagnostics.Debug.WriteLine($"[Viewport/Bake] Position write failed for mesh '{meshNode.Name}' (unsupported format or missing buffer); still baking normals/tangents.");
                }

                // Step 5b: Write rotated normals/tangents to keep the tangent basis in sync.
                WriteRotatedSurfaceVectorsToVertexBuffers(bundle, meshBlob, layout, vbMap, minVertexIndex, rotMatrix, rotatedNormals, vertexCount, !splitNormalX || wrote);

                // Step 6: Update mesh blob scale/translate
                meshBlob.PositionScale = new Vector4(newScale.X, newScale.Y, newScale.Z, meshBlob.PositionScale.W);
                meshBlob.PositionTranslate = new Vector4(newTranslate.X, newTranslate.Y, newTranslate.Z, meshBlob.PositionTranslate.W);

                // Step 7: Update in-memory data
                geo.RawPositions = newRawPositions;
                if (rotatedNormals != null) geo.Normals = rotatedNormals;
                geo.RotationEulerDegrees = Vector3.Zero;
                meshNode.OriginalPositionScale = meshBlob.PositionScale;
                meshNode.OriginalPositionTranslate = meshBlob.PositionTranslate;
                meshNode.OriginalRotationEulerDegrees = Vector3.Zero;
            }
        }

        // Writes new SNORM16 positions into the vertex buffer.
        // Also writes normal X into POSITION.W only for split-normal layouts.
        private bool WritePositionsToVertexBuffer(
            Bundle bundle,
            MeshBlob mesh,
            VertexLayoutBlob? layout,
            Dictionary<int, VertexBufferBlob> vbMap,
            int minVertexIndex,
            Vector3[] newRawPositions,
            Vector3[]? rotatedNormals = null)
        {
            if (layout == null) return false;

            var positionElements = GetLayoutElements(layout).Where(item => item.Semantic == "POSITION").ToList();
            if (positionElements.Count == 0) return false;
            var posElement = positionElements[0];

            // Only support SNORM16 (format 13) 
            if (posElement.Format != 13) return false;

            // Resolve vertex buffer
            if (!TryResolveWritableElementBuffer(bundle, mesh, vbMap, posElement.Element, out var data, out var baseOffset, out var stride, out var usageOffset))
                return false;

            // Write new SNORM16 positions
            for (int i = 0; i < newRawPositions.Length; i++)
            {
                long vertexId = minVertexIndex + i + mesh.IndexedVertexOffset;
                long addr = baseOffset + usageOffset + (vertexId * stride) + posElement.Offset;

                if (addr >= 0 && addr + 8 <= data.Length)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan((int)addr), ToSnorm16(newRawPositions[i].X));
                    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan((int)addr + 2), ToSnorm16(newRawPositions[i].Y));
                    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan((int)addr + 4), ToSnorm16(newRawPositions[i].Z));

                    // Write POSITION.W = normal.X for layouts that split normal X into the position stream.
                    if (rotatedNormals != null && i < rotatedNormals.Length)
                        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan((int)addr + 6), ToSnorm16(rotatedNormals[i].X));
                }
            }

            return true;
        }

        private void WriteRotatedSurfaceVectorsToVertexBuffers(
            Bundle bundle,
            MeshBlob mesh,
            VertexLayoutBlob? layout,
            Dictionary<int, VertexBufferBlob> vbMap,
            int minVertexIndex,
            Matrix4x4 rotMatrix,
            Vector3[]? rotatedNormals,
            int vertexCount,
            bool canWriteSplitNormalX)
        {
            if (layout == null) return;

            var surfaceElements = GetLayoutElements(layout)
                .Where(item => item.Semantic == "NORMAL" || item.Semantic == "TANGENT")
                .ToList();

            if (surfaceElements.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Viewport/BakeSurface] No NORMAL/TANGENT elements found for mesh (VertexLayoutIndex={mesh.VertexLayoutIndex}).");
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[Viewport/BakeSurface] Found {surfaceElements.Count} surface vector element(s) for mesh (VertexLayoutIndex={mesh.VertexLayoutIndex}): " +
                string.Join(", ", surfaceElements.Select(item => $"{item.Semantic}{item.SemanticIndex}:fmt={item.Format}")));

            foreach (var item in surfaceElements)
            {
                if (!IsWritableSurfaceVectorFormat(item.Format))
                {
                    System.Diagnostics.Debug.WriteLine($"[Viewport/BakeSurface] Unsupported {item.Semantic}{item.SemanticIndex} format {item.Format} for mesh (VertexLayoutIndex={mesh.VertexLayoutIndex}), skipping.");
                    continue;
                }

                if (item.Semantic == "NORMAL" && item.Format == 37 && !canWriteSplitNormalX)
                {
                    System.Diagnostics.Debug.WriteLine($"[Viewport/BakeSurface] Skipping split NORMAL{item.SemanticIndex} because POSITION.W could not be updated for mesh (VertexLayoutIndex={mesh.VertexLayoutIndex}).");
                    continue;
                }

                if (!TryResolveWritableElementBuffer(bundle, mesh, vbMap, item.Element, out var data, out var baseOffset, out var stride, out var usageOffset))
                    continue;

                int elementSize = GetVertexFormatSize(item.Format);

                for (int i = 0; i < vertexCount; i++)
                {
                    long vertexId = minVertexIndex + i + mesh.IndexedVertexOffset;
                    long addr = baseOffset + usageOffset + (vertexId * stride) + item.Offset;

                    if (addr < 0 || addr + elementSize > data.Length) continue;

                    var span = data.AsSpan((int)addr, elementSize);
                    var fallback = item.Semantic == "NORMAL" ? Vector3.UnitY : Vector3.UnitX;
                    if (item.Semantic == "NORMAL" && (rotatedNormals == null || i >= rotatedNormals.Length))
                        continue;

                    var rotated = item.Semantic == "NORMAL" && rotatedNormals != null && i < rotatedNormals.Length
                        ? rotatedNormals[i]
                        : NormalizeOrDefault(Vector3.TransformNormal(ReadSurfaceVector(span, item.Format, fallback), rotMatrix), fallback);

                    if (item.Semantic == "TANGENT" && rotatedNormals != null && i < rotatedNormals.Length)
                        rotated = OrthonormalizeTangent(rotated, rotatedNormals[i]);
                    else
                        rotated = NormalizeOrDefault(rotated, fallback);

                    WriteSurfaceVector(span, item.Format, rotated);
                }
            }
        }

        private static VertexLayoutBlob? ResolveVertexLayout(Bundle bundle, VertexLayoutBlob[] layouts, MeshBlob mesh)
        {
            var layoutById = bundle.Blobs.OfType<VertexLayoutBlob>()
                .FirstOrDefault(l => l.Metadatas.OfType<IdentifierMetadata>().Any(m => (int)m.Id == mesh.VertexLayoutIndex));
            if (layoutById != null)
                return layoutById;
            if (mesh.VertexLayoutIndex >= 0 && mesh.VertexLayoutIndex < layouts.Length)
                return layouts[mesh.VertexLayoutIndex];
            return layouts.Length > 0 ? layouts[0] : null;
        }

        private readonly struct LayoutElementInfo
        {
            public LayoutElementInfo(D3D12_INPUT_LAYOUT_DESC element, int offset, string semantic)
            {
                Element = element;
                Offset = offset;
                Semantic = semantic;
                SemanticIndex = element.SemanticIndex;
                Format = (int)element.Format;
            }

            public D3D12_INPUT_LAYOUT_DESC Element { get; }
            public int Offset { get; }
            public string Semantic { get; }
            public int SemanticIndex { get; }
            public int Format { get; }
        }

        private static List<LayoutElementInfo> GetLayoutElements(VertexLayoutBlob layout)
        {
            var items = new List<LayoutElementInfo>();
            var slotOffsets = new Dictionary<int, int>();

            foreach (var element in layout.Elements)
            {
                int slot = element.InputSlot;
                if (!slotOffsets.ContainsKey(slot)) slotOffsets[slot] = 0;

                string semantic = (element.SemanticNameIndex >= 0 && element.SemanticNameIndex < layout.SemanticNames.Count)
                    ? layout.SemanticNames[element.SemanticNameIndex]
                    : "UNKNOWN";

                items.Add(new LayoutElementInfo(element, slotOffsets[slot], semantic));
                slotOffsets[slot] += GetVertexFormatSize((int)element.Format);
            }

            return items;
        }

        private static bool TryResolveWritableElementBuffer(
            Bundle bundle,
            MeshBlob mesh,
            Dictionary<int, VertexBufferBlob> vbMap,
            D3D12_INPUT_LAYOUT_DESC element,
            out byte[] data,
            out int baseOffset,
            out long stride,
            out long usageOffset)
        {
            data = Array.Empty<byte>();
            baseOffset = 0;
            stride = 0;
            usageOffset = 0;

            var usage = mesh.VertexBuffers.FirstOrDefault(v => v.InputSlot == element.InputSlot);
            if (usage == null) return false;

            if (!vbMap.TryGetValue(usage.Index, out var vb) || vb == null)
            {
                if (usage.Index < 0 || usage.Index >= bundle.Blobs.Count)
                    return false;
                vb = bundle.Blobs[usage.Index] as VertexBufferBlob;
                if (vb == null) return false;
            }

            var rawData = vb.Header?.GetRawData();
            if (rawData != null && rawData.Length > 0)
            {
                data = rawData;
                baseOffset = 0;
            }
            else
            {
                data = vb.GetContents();
                baseOffset = 16;
            }

            if (data == null || data.Length == 0) return false;

            stride = vb.Header?.Stride > 0 ? vb.Header.Stride : usage.Stride;
            if (stride == 0) stride = 28;
            usageOffset = usage.Offset;
            return true;
        }

        private static bool IsWritableSurfaceVectorFormat(int format) =>
            format == 6 || format == 10 || format == 13 || format == 24 || format == 37;

        private static Vector3 ReadSurfaceVector(ReadOnlySpan<byte> span, int format, Vector3 fallback)
        {
            return format switch
            {
                6 => NormalizeOrDefault(new Vector3(
                    BinaryPrimitives.ReadSingleLittleEndian(span),
                    BinaryPrimitives.ReadSingleLittleEndian(span.Slice(4)),
                    BinaryPrimitives.ReadSingleLittleEndian(span.Slice(8))), fallback),
                10 => NormalizeOrDefault(new Vector3(
                    (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(span)),
                    (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2))),
                    (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(4)))), fallback),
                13 => NormalizeOrDefault(new Vector3(
                    FromSnorm16(BinaryPrimitives.ReadInt16LittleEndian(span)),
                    FromSnorm16(BinaryPrimitives.ReadInt16LittleEndian(span.Slice(2))),
                    FromSnorm16(BinaryPrimitives.ReadInt16LittleEndian(span.Slice(4)))), fallback),
                24 => NormalizeOrDefault(UnpackR10G10B10A2Vector3(BinaryPrimitives.ReadUInt32LittleEndian(span)), fallback),
                37 => NormalizeOrDefault(new Vector3(0f,
                    FromSnorm16(BinaryPrimitives.ReadInt16LittleEndian(span)),
                    FromSnorm16(BinaryPrimitives.ReadInt16LittleEndian(span.Slice(2)))), fallback),
                _ => fallback
            };
        }

        private static void WriteSurfaceVector(Span<byte> span, int format, Vector3 value)
        {
            value = NormalizeOrDefault(value, Vector3.UnitY);

            if (format == 6)
            {
                BinaryPrimitives.WriteSingleLittleEndian(span, value.X);
                BinaryPrimitives.WriteSingleLittleEndian(span.Slice(4), value.Y);
                BinaryPrimitives.WriteSingleLittleEndian(span.Slice(8), value.Z);
            }
            else if (format == 10)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span, BitConverter.HalfToUInt16Bits((Half)value.X));
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), BitConverter.HalfToUInt16Bits((Half)value.Y));
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4), BitConverter.HalfToUInt16Bits((Half)value.Z));
            }
            else if (format == 13)
            {
                BinaryPrimitives.WriteInt16LittleEndian(span, ToSnorm16(value.X));
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2), ToSnorm16(value.Y));
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(4), ToSnorm16(value.Z));
            }
            else if (format == 24)
            {
                uint existing = BinaryPrimitives.ReadUInt32LittleEndian(span);
                BinaryPrimitives.WriteUInt32LittleEndian(span, PackR10G10B10A2Vector3(value, existing & 0xC0000000u));
            }
            else if (format == 37)
            {
                BinaryPrimitives.WriteInt16LittleEndian(span, ToSnorm16(value.Y));
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2), ToSnorm16(value.Z));
            }
        }

        private static Vector3 OrthonormalizeTangent(Vector3 tangent, Vector3 normal)
        {
            normal = NormalizeOrDefault(normal, Vector3.UnitY);
            var adjusted = tangent - normal * Vector3.Dot(tangent, normal);
            return NormalizeOrDefault(adjusted, NormalizeOrDefault(tangent, Vector3.UnitX));
        }

        private static int GetVertexFormatSize(int format) =>
            format switch { 6 => 12, 10 => 8, 13 => 8, 16 => 8, 24 => 4, 28 => 4, 35 => 4, 37 => 4, _ => 4 };

        private static Vector3 UnpackR10G10B10A2Vector3(uint packed)
        {
            float x = ((packed >> 0) & 0x3FF) / 1023f * 2f - 1f;
            float y = ((packed >> 10) & 0x3FF) / 1023f * 2f - 1f;
            float z = ((packed >> 20) & 0x3FF) / 1023f * 2f - 1f;
            return new Vector3(x, y, z);
        }

        private static uint PackR10G10B10A2Vector3(Vector3 value, uint preservedAlphaBits)
        {
            uint x = (uint)Math.Clamp(MathF.Round((value.X * 0.5f + 0.5f) * 1023f), 0, 1023);
            uint y = (uint)Math.Clamp(MathF.Round((value.Y * 0.5f + 0.5f) * 1023f), 0, 1023);
            uint z = (uint)Math.Clamp(MathF.Round((value.Z * 0.5f + 0.5f) * 1023f), 0, 1023);
            return (x & 0x3FF) | ((y & 0x3FF) << 10) | ((z & 0x3FF) << 20) | preservedAlphaBits;
        }

        private static short ToSnorm16(float value) =>
            (short)Math.Clamp(MathF.Round(value * 32767f), -32767, 32767);

        private static float FromSnorm16(short value) =>
            Math.Clamp(value / 32767f, -1f, 1f);

        // Temporarily patches LODS-only meshes to become LOD0 for saving.
        // Returns originals so they can be reverted afterwards.
        private static List<(MeshBlob Blob, ushort OrigFlags, byte OrigLod1, byte OrigLod2)> PatchLodConversionForSave(Bundle bundle, bool apply)
        {
            var originals = new List<(MeshBlob, ushort, byte, byte)>();
            if (!apply) return originals;

            foreach (var blob in bundle.Blobs.OfType<MeshBlob>())
            {
                // Only convert meshes that have LODS set (bit 0) but NOT the LOD0-specific bit (bit 1)
                if (blob.LOD_LODS && (blob.LODFlags & 2) == 0)
                {
                    originals.Add((blob, blob.LODFlags, blob.LODLevel1, blob.LODLevel2));
                    blob.LODLevel1 = 0;
                    blob.LODLevel2 = 255;
                    blob.LOD_LOD0 = true; // sets bits 0+1
                }
            }
            return originals;
        }

        private static void RevertLodConversionAfterSave(List<(MeshBlob Blob, ushort OrigFlags, byte OrigLod1, byte OrigLod2)> originals)
        {
            foreach (var (blob, flags, lod1, lod2) in originals)
            {
                blob.LODFlags = flags;
                blob.LODLevel1 = lod1;
                blob.LODLevel2 = lod2;
            }
        }

        private async void SaveCurrentModel_Click(object sender, RoutedEventArgs e)
        {
            var viewportSelectionNodes = GetViewportMultiSelectionSaveFileNodes();
            if (ShouldShowViewportSaveFlyout() && viewportSelectionNodes.Count > 0)
            {
                ShowSaveCurrentOrSelectedFlyout(viewportSelectionNodes);
                return;
            }

            await SaveCurrentAsync(saveAs: false);
        }

        private async void SaveCurrentModelAs_Click(object sender, RoutedEventArgs e)
        {
            await SaveCurrentAsync(saveAs: true);
        }

        private async Task SaveModelBinAs(ModelBinNode modelBin)
        {
             var savePicker = new FileSavePicker();
             var window = App.MainWindow;
             var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
             WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);

             savePicker.SuggestedStartLocation = PickerLocationId.Desktop;
             savePicker.SuggestedFileName = Path.GetFileNameWithoutExtension(modelBin.Name ?? "model") + "_modified.modelbin";
             savePicker.FileTypeChoices.Add("Forza Modelbin", new[] { ".modelbin" });

             var outputFile = await savePicker.PickSaveFileAsync();
             if (outputFile == null) return;

             bool convertLods = ConvertLodsToLod0Toggle?.IsChecked == true;
             await Task.Run(() =>
             {
                 var lodOriginals = PatchLodConversionForSave(modelBin.Bundle, convertLods);
                 try
                 {
                     using var fs = File.Create(outputFile.Path);
                     modelBin.Bundle.SerializeConverted(fs);
                 }
                 finally
                 {
                     RevertLodConversionAfterSave(lodOriginals);
                 }
             });

             var dialog = new ContentDialog
             {
                 Title = "Success",
                 Content = $"Model saved successfully to:\n{outputFile.Name}\n\nModel and skeleton saved.",
                 CloseButtonText = "OK",
                 XamlRoot = this.XamlRoot
             };
             await dialog.ShowAsync();
        }

        private async Task SaveLightsBinAs(LightsBinNode node)
        {
            if (node.OriginalData == null)
            {
                await ShowError("No lights data to save.");
                return;
            }

            try
            {
                // Serialize
                byte[] bytes = await Task.Run(() =>
                {
                    using var ms = new MemoryStream();
                    var parser = new LightsBinParser();
                    parser.Serialize(ms, node.OriginalData);
                    return ms.ToArray();
                });

                // Always show save dialog for Save As
                var savePicker = new FileSavePicker();
                var window = App.MainWindow;
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);

                savePicker.SuggestedStartLocation = PickerLocationId.Desktop;
                savePicker.SuggestedFileName = string.IsNullOrEmpty(node.FilePath) 
                    ? "lights_modified.bin" 
                    : Path.GetFileNameWithoutExtension(node.FilePath) + "_modified.bin";
                savePicker.FileTypeChoices.Add("Lights Binary", new[] { ".bin" });

                var outputFile = await savePicker.PickSaveFileAsync();
                if (outputFile == null) return;

                // Write to file
                await File.WriteAllBytesAsync(outputFile.Path, bytes);
                
                // Update file path
                node.FilePath = outputFile.Path;
                node.IsDirty = false;

                var dialog = new ContentDialog
                {
                    Title = "Success",
                    Content = $"Lights.bin saved successfully to:\n{outputFile.Name}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowError($"Failed to save lights.bin.\n\nError: {ex.Message}");
            }
        }
        
        private async Task SaveLightsBin(LightsBinNode node)
        {
            if (node.OriginalData == null)
            {
                await ShowError("No lights data to save.");
                return;
            }

            try
            {
                IsLoading = true;
                LoadingStatus = "Saving lights.bin...";


                // Serialize
                byte[] bytes = await Task.Run(() =>
                {
                    using var ms = new MemoryStream();
                    var parser = new LightsBinParser();
                    parser.Serialize(ms, node.OriginalData);
                    return ms.ToArray();
                });

                // If we have a file path and it exists, save over it
                string? savePath = node.FilePath;
                if (!string.IsNullOrEmpty(node.SourceZipPath) && !string.IsNullOrEmpty(node.ZipEntryName))
                {
                    await Task.Run(() => ZipArchiveHelper.ReplaceEntry(node.SourceZipPath, node.ZipEntryName, bytes));
                    node.IsDirty = false;

                    var dialog = new ContentDialog
                    {
                        Title = "Success",
                        Content = $"Updated entry '{node.ZipEntryName}' inside ZIP archive.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
                else if (!string.IsNullOrEmpty(savePath) && File.Exists(savePath))
                {
                    // Save over the current file
                    await File.WriteAllBytesAsync(savePath, bytes);
                    node.IsDirty = false;

                    var dialog = new ContentDialog
                    {
                        Title = "Success",
                        Content = $"Lights.bin saved successfully to:\n{Path.GetFileName(savePath)}",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
                else
                {
                    // No existing file, use Save As behavior
                    await SaveLightsBinAs(node);
                }
            }
            catch (Exception ex)
            {
                await ShowError($"Failed to save lights.bin.\n\nError: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadingStatus = "";
            }
        }

        private async Task SaveLightGroup(LightGroupNode lightGroup)
        {
            // Find parent LightsBinNode
            var parent = lightGroup.Parent;
            while (parent != null && !(parent is LightsBinNode))
            {
                parent = parent.Parent;
            }

            if (parent is LightsBinNode lightsBin)
            {
                await SaveLightsBin(lightsBin);
            }
            else
            {
                await ShowError("Cannot find parent lights.bin file.");
            }
        }

        private LightsBinNode? GetActiveLightsBinNode()
        {
            if (LightPartSelector.SelectedItem is LightGroupNode selectedGroup && selectedGroup.Parent is LightsBinNode selectedLightsBin)
                return selectedLightsBin;

            IViewerNode? current = ViewModel.SelectedNode;
            while (current != null && current is not LightsBinNode)
            {
                current = current.Parent;
            }

            return current as LightsBinNode;
        }

        private async Task ShowError(string msg)
        {
             var dialog = new ContentDialog
             {
                 Title = "Error",
                 Content = msg,
                 CloseButtonText = "OK"
             };
             await ShowViewportDialogAsync(dialog);
        }

        private void ChangeValue(TextBox box, float delta)
        {
             box.IsEnabled = true;
             if (float.TryParse(box.Text,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out float val))
             {
                 box.Text = (val + delta).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
             }
             else
             {
                 box.Text = (delta > 0 ? 1.0f : 0.0f).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
             }
        }

        private static void PopulateTransformEditorFields(
            TextBox posX,
            TextBox posY,
            TextBox posZ,
            TextBox rotX,
            TextBox rotY,
            TextBox rotZ,
            TextBox scaleX,
            TextBox scaleY,
            TextBox scaleZ,
            Matrix4x4 matrix)
        {
            DecomposeTransformMatrix(matrix, out var position, out var rotationDegrees, out var scale);

            PopulateVector3Fields(posX, posY, posZ, position);
            PopulateVector3Fields(rotX, rotY, rotZ, rotationDegrees);
            PopulateVector3Fields(scaleX, scaleY, scaleZ, scale);
        }

        private static void DecomposeTransformMatrix(
            Matrix4x4 matrix,
            out Vector3 position,
            out Vector3 rotationDegrees,
            out Vector3 scale)
        {
            position = matrix.Translation;
            rotationDegrees = Vector3.Zero;
            scale = Vector3.One;

            if (!Matrix4x4.Decompose(matrix, out var decomposedScale, out var rotation, out var decomposedPosition))
                return;

            position = decomposedPosition;
            rotationDegrees = QuaternionToEulerDegrees(rotation);
            scale = decomposedScale;
        }

        private static Matrix4x4 ComposeTransformMatrix(
            Vector3 position,
            Vector3 rotationDegrees,
            Vector3 scale,
            Matrix4x4 currentMatrix)
        {
            var rotation = Matrix4x4.CreateFromQuaternion(EulerDegreesToQuaternion(rotationDegrees));
            var matrix = Matrix4x4.CreateScale(scale) * rotation * Matrix4x4.CreateTranslation(position);

            matrix.M14 = currentMatrix.M14;
            matrix.M24 = currentMatrix.M24;
            matrix.M34 = currentMatrix.M34;
            matrix.M44 = currentMatrix.M44;
            return matrix;
        }

        private static Vector3 ReadVector3Fields(
            TextBox xBox,
            TextBox yBox,
            TextBox zBox,
            Vector3 fallback)
        {
            return new Vector3(
                ParseTransformField(xBox, fallback.X),
                ParseTransformField(yBox, fallback.Y),
                ParseTransformField(zBox, fallback.Z));
        }

        private static float ParseTransformField(TextBox box, float fallback)
        {
            return float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;
        }

        private static void PopulateVector3Fields(TextBox xBox, TextBox yBox, TextBox zBox, Vector3 value)
        {
            xBox.Text = value.X.ToString("F3", CultureInfo.InvariantCulture);
            yBox.Text = value.Y.ToString("F3", CultureInfo.InvariantCulture);
            zBox.Text = value.Z.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static Quaternion EulerDegreesToQuaternion(Vector3 degrees)
        {
            const float degreesToRadians = MathF.PI / 180f;
            return Quaternion.Normalize(
                Quaternion.CreateFromYawPitchRoll(
                    degrees.Y * degreesToRadians,
                    degrees.X * degreesToRadians,
                    degrees.Z * degreesToRadians));
        }

        private static Vector3 QuaternionToEulerDegrees(Quaternion quaternion)
        {
            if (quaternion.LengthSquared() <= 1e-12f)
                return Vector3.Zero;

            quaternion = Quaternion.Normalize(quaternion);

            // Match EulerDegreesToQuaternion/ComposeTransformMatrix, which uses
            // CreateFromYawPitchRoll(y, x, z) == Rz * Rx * Ry in System.Numerics.
            var matrix = Matrix4x4.CreateFromQuaternion(quaternion);

            float x = MathF.Asin(Math.Clamp(-matrix.M32, -1f, 1f));
            float cosX = MathF.Cos(x);

            float y;
            float z;
            if (MathF.Abs(cosX) > 1e-5f)
            {
                y = MathF.Atan2(matrix.M31, matrix.M33);
                z = MathF.Atan2(matrix.M12, matrix.M22);
            }
            else
            {
                // In gimbal lock, Y and Z are coupled. Pick a stable canonical form.
                y = MathF.Atan2(-matrix.M13, matrix.M11);
                z = 0f;
            }

            const float radiansToDegrees = 180f / MathF.PI;
            return new Vector3(x * radiansToDegrees, y * radiansToDegrees, z * radiansToDegrees);
        }

        private void TransformSpinnerUp_Click(object sender, RoutedEventArgs e) => ChangeTaggedValue(sender, 1f);

        private void TransformSpinnerDown_Click(object sender, RoutedEventArgs e) => ChangeTaggedValue(sender, -1f);

        private void ChangeTaggedValue(object sender, float direction)
        {
            if (sender is not FrameworkElement element || element.Tag is not string tag)
                return;

            string[] parts = tag.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                return;

            if (FindName(parts[0]) is not TextBox box)
                return;

            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float step))
                return;

            ChangeValue(box, step * direction);
        }

        private void ScaleX_Up(object sender, RoutedEventArgs e) => ChangeValue(ScaleX, 0.001f);
        private void ScaleX_Down(object sender, RoutedEventArgs e) => ChangeValue(ScaleX, -0.001f);
        private void ScaleY_Up(object sender, RoutedEventArgs e) => ChangeValue(ScaleY, 0.001f);
        private void ScaleY_Down(object sender, RoutedEventArgs e) => ChangeValue(ScaleY, -0.001f);
        private void ScaleZ_Up(object sender, RoutedEventArgs e) => ChangeValue(ScaleZ, 0.001f);
        private void ScaleZ_Down(object sender, RoutedEventArgs e) => ChangeValue(ScaleZ, -0.001f);

        private void RotX_Up(object sender, RoutedEventArgs e) => ChangeValue(RotX, 1.0f);
        private void RotX_Down(object sender, RoutedEventArgs e) => ChangeValue(RotX, -1.0f);
        private void RotY_Up(object sender, RoutedEventArgs e) => ChangeValue(RotY, 1.0f);
        private void RotY_Down(object sender, RoutedEventArgs e) => ChangeValue(RotY, -1.0f);
        private void RotZ_Up(object sender, RoutedEventArgs e) => ChangeValue(RotZ, 1.0f);
        private void RotZ_Down(object sender, RoutedEventArgs e) => ChangeValue(RotZ, -1.0f);

        private void TransX_Up(object sender, RoutedEventArgs e) => ChangeValue(TransX, 0.001f);
        private void TransX_Down(object sender, RoutedEventArgs e) => ChangeValue(TransX, -0.001f);
        private void TransY_Up(object sender, RoutedEventArgs e) => ChangeValue(TransY, 0.001f);
        private void TransY_Down(object sender, RoutedEventArgs e) => ChangeValue(TransY, -0.001f);
        private void TransZ_Up(object sender, RoutedEventArgs e) => ChangeValue(TransZ, 0.001f);
        private void TransZ_Down(object sender, RoutedEventArgs e) => ChangeValue(TransZ, -0.001f);

        //  Light Transform position spinner handlers 
        private void LtPosX_Up(object sender, RoutedEventArgs e) => ChangeValue(LtPosX, 0.001f);
        private void LtPosX_Down(object sender, RoutedEventArgs e) => ChangeValue(LtPosX, -0.001f);
        private void LtPosY_Up(object sender, RoutedEventArgs e) => ChangeValue(LtPosY, 0.001f);
        private void LtPosY_Down(object sender, RoutedEventArgs e) => ChangeValue(LtPosY, -0.001f);
        private void LtPosZ_Up(object sender, RoutedEventArgs e) => ChangeValue(LtPosZ, 0.001f);
        private void LtPosZ_Down(object sender, RoutedEventArgs e) => ChangeValue(LtPosZ, -0.001f);
        private void LtPosW_Up(object sender, RoutedEventArgs e) => ChangeValue(LtPosW, 0.001f);
        private void LtPosW_Down(object sender, RoutedEventArgs e) => ChangeValue(LtPosW, -0.001f);
        private void LtRotX_Up(object sender, RoutedEventArgs e) => ChangeValue(LtRotX, 0.001f);
        private void LtRotX_Down(object sender, RoutedEventArgs e) => ChangeValue(LtRotX, -0.001f);
        private void LtRotY_Up(object sender, RoutedEventArgs e) => ChangeValue(LtRotY, 0.001f);
        private void LtRotY_Down(object sender, RoutedEventArgs e) => ChangeValue(LtRotY, -0.001f);
        private void LtRotZ_Up(object sender, RoutedEventArgs e) => ChangeValue(LtRotZ, 0.001f);
        private void LtRotZ_Down(object sender, RoutedEventArgs e) => ChangeValue(LtRotZ, -0.001f);
        private void LtRotW_Up(object sender, RoutedEventArgs e) => ChangeValue(LtRotW, 0.001f);
        private void LtRotW_Down(object sender, RoutedEventArgs e) => ChangeValue(LtRotW, -0.001f);
        private void LtDmgPosX_Up(object sender, RoutedEventArgs e) => ChangeValue(LtDmgPosX, 0.001f);
        private void LtDmgPosX_Down(object sender, RoutedEventArgs e) => ChangeValue(LtDmgPosX, -0.001f);
        private void LtDmgPosY_Up(object sender, RoutedEventArgs e) => ChangeValue(LtDmgPosY, 0.001f);
        private void LtDmgPosY_Down(object sender, RoutedEventArgs e) => ChangeValue(LtDmgPosY, -0.001f);
        private void LtDmgPosZ_Up(object sender, RoutedEventArgs e) => ChangeValue(LtDmgPosZ, 0.001f);
        private void LtDmgPosZ_Down(object sender, RoutedEventArgs e) => ChangeValue(LtDmgPosZ, -0.001f);
        private void LtDmgPosW_Up(object sender, RoutedEventArgs e) => ChangeValue(LtDmgPosW, 0.001f);
        private void LtDmgPosW_Down(object sender, RoutedEventArgs e) => ChangeValue(LtDmgPosW, -0.001f);
        private void LtDmgRotX_Up(object sender, RoutedEventArgs e) => ChangeValue(LtDmgRotX, 0.001f);
        private void LtDmgRotX_Down(object sender, RoutedEventArgs e) => ChangeValue(LtDmgRotX, -0.001f);
        private void LtDmgRotY_Up(object sender, RoutedEventArgs e) => ChangeValue(LtDmgRotY, 0.001f);
        private void LtDmgRotY_Down(object sender, RoutedEventArgs e) => ChangeValue(LtDmgRotY, -0.001f);
        private void LtDmgRotZ_Up(object sender, RoutedEventArgs e) => ChangeValue(LtDmgRotZ, 0.001f);
        private void LtDmgRotZ_Down(object sender, RoutedEventArgs e) => ChangeValue(LtDmgRotZ, -0.001f);
        private void LtDmgRotW_Up(object sender, RoutedEventArgs e) => ChangeValue(LtDmgRotW, 0.001f);
        private void LtDmgRotW_Down(object sender, RoutedEventArgs e) => ChangeValue(LtDmgRotW, -0.001f);

        //  Bone Matrix spinner handlers 
        private void BM11_Up(object sender, RoutedEventArgs e) => ChangeValue(BM11, 0.001f);
        private void BM11_Down(object sender, RoutedEventArgs e) => ChangeValue(BM11, -0.001f);
        private void BM12_Up(object sender, RoutedEventArgs e) => ChangeValue(BM12, 0.001f);
        private void BM12_Down(object sender, RoutedEventArgs e) => ChangeValue(BM12, -0.001f);
        private void BM13_Up(object sender, RoutedEventArgs e) => ChangeValue(BM13, 0.001f);
        private void BM13_Down(object sender, RoutedEventArgs e) => ChangeValue(BM13, -0.001f);
        private void BM14_Up(object sender, RoutedEventArgs e) => ChangeValue(BM14, 0.001f);
        private void BM14_Down(object sender, RoutedEventArgs e) => ChangeValue(BM14, -0.001f);
        private void BM21_Up(object sender, RoutedEventArgs e) => ChangeValue(BM21, 0.001f);
        private void BM21_Down(object sender, RoutedEventArgs e) => ChangeValue(BM21, -0.001f);
        private void BM22_Up(object sender, RoutedEventArgs e) => ChangeValue(BM22, 0.001f);
        private void BM22_Down(object sender, RoutedEventArgs e) => ChangeValue(BM22, -0.001f);
        private void BM23_Up(object sender, RoutedEventArgs e) => ChangeValue(BM23, 0.001f);
        private void BM23_Down(object sender, RoutedEventArgs e) => ChangeValue(BM23, -0.001f);
        private void BM24_Up(object sender, RoutedEventArgs e) => ChangeValue(BM24, 0.001f);
        private void BM24_Down(object sender, RoutedEventArgs e) => ChangeValue(BM24, -0.001f);
        private void BM31_Up(object sender, RoutedEventArgs e) => ChangeValue(BM31, 0.001f);
        private void BM31_Down(object sender, RoutedEventArgs e) => ChangeValue(BM31, -0.001f);
        private void BM32_Up(object sender, RoutedEventArgs e) => ChangeValue(BM32, 0.001f);
        private void BM32_Down(object sender, RoutedEventArgs e) => ChangeValue(BM32, -0.001f);
        private void BM33_Up(object sender, RoutedEventArgs e) => ChangeValue(BM33, 0.001f);
        private void BM33_Down(object sender, RoutedEventArgs e) => ChangeValue(BM33, -0.001f);
        private void BM34_Up(object sender, RoutedEventArgs e) => ChangeValue(BM34, 0.001f);
        private void BM34_Down(object sender, RoutedEventArgs e) => ChangeValue(BM34, -0.001f);
        private void BM41_Up(object sender, RoutedEventArgs e) => ChangeValue(BM41, 0.001f);
        private void BM41_Down(object sender, RoutedEventArgs e) => ChangeValue(BM41, -0.001f);
        private void BM42_Up(object sender, RoutedEventArgs e) => ChangeValue(BM42, 0.001f);
        private void BM42_Down(object sender, RoutedEventArgs e) => ChangeValue(BM42, -0.001f);
        private void BM43_Up(object sender, RoutedEventArgs e) => ChangeValue(BM43, 0.001f);
        private void BM43_Down(object sender, RoutedEventArgs e) => ChangeValue(BM43, -0.001f);
        private void BM44_Up(object sender, RoutedEventArgs e) => ChangeValue(BM44, 0.001f);
        private void BM44_Down(object sender, RoutedEventArgs e) => ChangeValue(BM44, -0.001f);

        //  AvPins spinner handlers 
        private void AvPinPosX_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinPosX, 0.001f);
        private void AvPinPosX_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinPosX, -0.001f);
        private void AvPinPosY_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinPosY, 0.001f);
        private void AvPinPosY_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinPosY, -0.001f);
        private void AvPinPosZ_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinPosZ, 0.001f);
        private void AvPinPosZ_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinPosZ, -0.001f);
        private void AvPinAxisYaw_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinAxisYaw, 0.001f);
        private void AvPinAxisYaw_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinAxisYaw, -0.001f);
        private void AvPinAxisPitch_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinAxisPitch, 0.001f);
        private void AvPinAxisPitch_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinAxisPitch, -0.001f);
        private void AvPinApexYaw_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinApexYaw, 0.001f);
        private void AvPinApexYaw_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinApexYaw, -0.001f);
        private void AvPinApexPitch_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinApexPitch, 0.001f);
        private void AvPinApexPitch_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinApexPitch, -0.001f);
        private void AvPinActiveApexYaw_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinActiveApexYaw, 0.001f);
        private void AvPinActiveApexYaw_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinActiveApexYaw, -0.001f);
        private void AvPinActiveApexPitch_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinActiveApexPitch, 0.001f);
        private void AvPinActiveApexPitch_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinActiveApexPitch, -0.001f);
        private void AvPinMidApexYaw_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinMidApexYaw, 0.001f);
        private void AvPinMidApexYaw_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinMidApexYaw, -0.001f);
        private void AvPinMidApexPitch_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinMidApexPitch, 0.001f);
        private void AvPinMidApexPitch_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinMidApexPitch, -0.001f);
        private void AvPinNearRadius_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinNearRadius, 0.001f);
        private void AvPinNearRadius_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinNearRadius, -0.001f);
        private void AvPinMidRadius_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinMidRadius, 0.001f);
        private void AvPinMidRadius_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinMidRadius, -0.001f);
        private void AvPinFarRadius_Up(object sender, RoutedEventArgs e) => ChangeValue(AvPinFarRadius, 0.001f);
        private void AvPinFarRadius_Down(object sender, RoutedEventArgs e) => ChangeValue(AvPinFarRadius, -0.001f);


        // Light Transform Expander handlers 

        private void LightTransform_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi) return;

            if (_isMultiLightSelectActive && _multiSelectedLightGroups.Count > 0)
            {
                ApplyMultiLightDeltaFromUI();
                return;
            }

            if (LightPartSelector.SelectedItem is LightGroupNode group)
                ApplySingleLightGroupFromUI(group);
        }

        private float ParseLt(TextBox box, float fallback) =>
            float.TryParse(box.Text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float v) ? v : fallback;

        private void ApplySingleLightGroupFromUI(LightGroupNode group)
        {
            var g = group.GroupData;
            var newPos    = new Vector4(ParseLt(LtPosX, g.Pos.X),       ParseLt(LtPosY, g.Pos.Y),       ParseLt(LtPosZ, g.Pos.Z),       ParseLt(LtPosW, g.Pos.W));
            var newRot    = new Vector4(ParseLt(LtRotX, g.Rot.X),       ParseLt(LtRotY, g.Rot.Y),       ParseLt(LtRotZ, g.Rot.Z),       ParseLt(LtRotW, g.Rot.W));
            var newDmgPos = new Vector4(ParseLt(LtDmgPosX, g.DamagePos.X), ParseLt(LtDmgPosY, g.DamagePos.Y), ParseLt(LtDmgPosZ, g.DamagePos.Z), ParseLt(LtDmgPosW, g.DamagePos.W));
            var newDmgRot = new Vector4(ParseLt(LtDmgRotX, g.DamageRot.X), ParseLt(LtDmgRotY, g.DamageRot.Y), ParseLt(LtDmgRotZ, g.DamageRot.Z), ParseLt(LtDmgRotW, g.DamageRot.W));

            bool changed = newPos != g.Pos || newRot != g.Rot || newDmgPos != g.DamagePos || newDmgRot != g.DamageRot;
            if (!changed) return;

            g.Pos       = newPos;
            g.Rot       = newRot;
            g.DamagePos = newDmgPos;
            g.DamageRot = newDmgRot;

            UpdateLightRowNodeLabels(group);

            if (group.IsChecked != false)
            {
                HideLight(group);
                RenderLight(group);
                if (_currentLightHighlightTargets.Contains(group))
                    UpdateHighlightForLightGroups(_currentLightHighlightTargets);
            }

            if (group.Parent is LightsBinNode lb)
                lb.IsDirty = true;
        }

        private void ApplyMultiLightDeltaFromUI()
        {
            float dPX = ParseLt(LtPosX, 0f),    dPY = ParseLt(LtPosY, 0f),    dPZ = ParseLt(LtPosZ, 0f),    dPW = ParseLt(LtPosW, 0f);
            float dRX = ParseLt(LtRotX, 0f),    dRY = ParseLt(LtRotY, 0f),    dRZ = ParseLt(LtRotZ, 0f),    dRW = ParseLt(LtRotW, 0f);
            float dDPX = ParseLt(LtDmgPosX, 0f), dDPY = ParseLt(LtDmgPosY, 0f), dDPZ = ParseLt(LtDmgPosZ, 0f), dDPW = ParseLt(LtDmgPosW, 0f);
            float dDRX = ParseLt(LtDmgRotX, 0f), dDRY = ParseLt(LtDmgRotY, 0f), dDRZ = ParseLt(LtDmgRotZ, 0f), dDRW = ParseLt(LtDmgRotW, 0f);

            foreach (var lg in _multiSelectedLightGroups)
            {
                if (!_multiSelectLightSnapshots.TryGetValue(lg, out var snap)) continue;
                lg.GroupData.Pos       = new Vector4(snap.Pos.X + dPX,    snap.Pos.Y + dPY,    snap.Pos.Z + dPZ,    snap.Pos.W + dPW);
                lg.GroupData.Rot       = new Vector4(snap.Rot.X + dRX,    snap.Rot.Y + dRY,    snap.Rot.Z + dRZ,    snap.Rot.W + dRW);
                lg.GroupData.DamagePos = new Vector4(snap.DmgPos.X + dDPX, snap.DmgPos.Y + dDPY, snap.DmgPos.Z + dDPZ, snap.DmgPos.W + dDPW);
                lg.GroupData.DamageRot = new Vector4(snap.DmgRot.X + dDRX, snap.DmgRot.Y + dDRY, snap.DmgRot.Z + dDRZ, snap.DmgRot.W + dDRW);

                UpdateLightRowNodeLabels(lg);

                if (lg.IsChecked != false)
                {
                    HideLight(lg);
                    RenderLight(lg);
                }
                if (lg.Parent is LightsBinNode lb)
                    lb.IsDirty = true;
            }

            UpdateHighlightForLightGroups(_multiSelectedLightGroups);
        }

        private void UpdateLightRowNodeLabels(LightGroupNode group)
        {
            for (int i = 0; i < group.Children.Count && i < 4; i++)
            {
                if (group.Children[i] is not LightRowNode rn) continue;
                Vector4 rowData = i switch
                {
                    0 => group.GroupData.Pos,
                    1 => group.GroupData.Rot,
                    2 => group.GroupData.DamagePos,
                    3 => group.GroupData.DamageRot,
                    _ => rn.RowData
                };
                string label = i switch { 0 => "Pos", 1 => "Rot", 2 => "DmgPos", 3 => "DmgRot", _ => $"Row{i}" };
                rn.RowData = rowData;
                rn.Name = $"{label}: {rowData.X:F5}, {rowData.Y:F5}, {rowData.Z:F5}, {rowData.W:F5}";
            }
        }

        // ── Undo/Redo System ─────────────────────────────────────────────────────

        private readonly Stack<IUndoAction> _undoStack = new();
        private readonly Stack<IUndoAction> _redoStack = new();
        private const int MaxUndoHistory = 100;

        private void PushUndo(IUndoAction action)
        {
            _undoStack.Push(action);
            _redoStack.Clear();

            // Trim history
            if (_undoStack.Count > MaxUndoHistory)
            {
                var items = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = 0; i < MaxUndoHistory; i++)
                    _undoStack.Push(items[MaxUndoHistory - 1 - i]);
            }

            UpdateUndoRedoButtons();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0) return;

            var action = _undoStack.Pop();
            action.Undo();
            _redoStack.Push(action);

            UpdateUndoRedoButtons();
            RefreshAfterUndoRedo(action);
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;

            var action = _redoStack.Pop();
            action.Redo();
            _undoStack.Push(action);

            UpdateUndoRedoButtons();
            RefreshAfterUndoRedo(action);
        }

        private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
        private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

        private void UpdateUndoRedoButtons()
        {
            var undoBtn = UndoBtn;
            if (undoBtn != null)
            {
                undoBtn.IsEnabled = _undoStack.Count > 0;
                ToolTipService.SetToolTip(undoBtn,
                    _undoStack.Count > 0 ? $"Undo: {_undoStack.Peek().Description}" : "Nothing to undo");
            }

            var redoBtn = RedoBtn;
            if (redoBtn != null)
            {
                redoBtn.IsEnabled = _redoStack.Count > 0;
                ToolTipService.SetToolTip(redoBtn,
                    _redoStack.Count > 0 ? $"Redo: {_redoStack.Peek().Description}" : "Nothing to redo");
            }
        }

        private void RefreshAfterUndoRedo(IUndoAction action)
        {
            if (action is MeshTransformAction meshAction)
            {
                foreach (var entry in meshAction.Entries)
                {
                    var mesh = entry.Mesh;
                    var geometry = mesh.GeometryData;
                    if (geometry?.SourceMesh == null) continue;

                    var meshBlob = geometry.SourceMesh;
                    bool hasBone = geometry.SourceBone != null &&
                                   Services.BoneTransformService.IsSignificantBone(geometry.BoneIndex);

                    if (hasBone)
                    {
                        UpdateMeshRenderingWithBoneTransform(mesh, geometry.BoneTransform);
                    }
                    else
                    {
                        UpdateMeshRendering(mesh,
                            meshBlob.PositionScale.X, meshBlob.PositionScale.Y, meshBlob.PositionScale.Z,
                            meshBlob.PositionTranslate.X, meshBlob.PositionTranslate.Y, meshBlob.PositionTranslate.Z);
                    }
                }

                UpdateModelBinDirtyState(meshAction.Entries
                    .Where(entry => entry.Mesh != null)
                    .Select(entry => entry.Mesh!));

                UpdateTransformUI();
                RefreshHighlight();
            }
            else if (action is LocatorTransformAction locatorAction)
            {
                RefreshLocatorCone(locatorAction.Node);
                if (ViewModel.SelectedNode == locatorAction.Node)
                {
                    PopulateMatrixFields(locatorAction.Node.LocatorEntry.SceneTransform);
                }
            }
            else if (action is AvPinTransformAction avPinAction)
            {
                RefreshAvPinCone(avPinAction.Node);
                if (ViewModel.SelectedNode == avPinAction.Node)
                {
                    PopulateAvPinFields(avPinAction.Node.PoiData?.Visibility);
                }
            }
            else if (action is LightGroupTransformAction lightAction)
            {
                foreach (var entry in lightAction.Entries)
                {
                    if (entry.Group == null)
                        continue;

                    UpdateLightRowNodeLabels(entry.Group);
                    if (entry.Group.IsChecked != false)
                    {
                        HideLight(entry.Group);
                        RenderLight(entry.Group);
                    }
                }

                if (_currentLightHighlightTargets.Count > 0)
                    UpdateHighlightForLightGroups(_currentLightHighlightTargets);

                if (LightPartSelector.SelectedItem is LightGroupNode selectedGroup &&
                    lightAction.Entries.Any(entry => entry.Group == selectedGroup))
                {
                    UpdateLightTransformUI(selectedGroup);
                }
            }
        }

        // Records the current transform state of the given meshes before a transform operation begins.
        // Call this BEFORE modifying values, then call CommitTransformAction() after.
        private MeshTransformAction BeginTransformAction(IEnumerable<MeshNode> meshes, string description)
        {
            var entries = new List<MeshTransformEntry>();
            foreach (var mesh in meshes)
            {
                var geometry = mesh.GeometryData;
                if (geometry?.SourceMesh == null) continue;

                entries.Add(new MeshTransformEntry
                {
                    Mesh = mesh,
                    OldScale = geometry.SourceMesh.PositionScale,
                    OldTranslate = geometry.SourceMesh.PositionTranslate,
                    OldRotation = geometry.RotationEulerDegrees
                });
            }
            return new MeshTransformAction(description, entries);
        }

        // Captures the new state after the transform and pushes the action.
        private void CommitTransformAction(MeshTransformAction action)
        {
            bool anyChanged = false;
            foreach (var entry in action.Entries)
            {
                var geometry = entry.Mesh.GeometryData;
                if (geometry?.SourceMesh == null) continue;

                entry.NewScale = geometry.SourceMesh.PositionScale;
                entry.NewTranslate = geometry.SourceMesh.PositionTranslate;
                entry.NewRotation = geometry.RotationEulerDegrees;

                if (entry.OldScale != entry.NewScale || entry.OldTranslate != entry.NewTranslate || entry.OldRotation != entry.NewRotation)
                    anyChanged = true;
            }

            if (anyChanged)
            {
                UpdateModelBinDirtyState(action.Entries
                    .Where(entry => entry.Mesh != null)
                    .Select(entry => entry.Mesh!));
                PushUndo(action);
            }
        }

        private void UpdateModelBinDirtyState(IEnumerable<MeshNode> meshes)
        {
            var touchedBins = new HashSet<ModelBinNode>();

            foreach (var mesh in meshes)
            {
                var modelBin = mesh.ParentModelBin ?? FindAncestor<ModelBinNode>(mesh);
                if (modelBin != null)
                    touchedBins.Add(modelBin);
            }

            foreach (var modelBin in touchedBins)
                modelBin.IsDirty = IsModelBinTransformDirty(modelBin);
        }

        private static bool IsModelBinTransformDirty(ModelBinNode modelBin)
        {
            foreach (var mesh in modelBin.Children.OfType<MeshNode>())
            {
                if (MeshTransformDiffersFromOriginal(mesh))
                    return true;
            }

            return false;
        }

        private static bool MeshTransformDiffersFromOriginal(MeshNode mesh)
        {
            if (mesh.GeometryData?.SourceMesh == null)
                return false;

            var sourceMesh = mesh.GeometryData.SourceMesh;
            return !NearlyEqual(sourceMesh.PositionScale, mesh.OriginalPositionScale)
                || !NearlyEqual(sourceMesh.PositionTranslate, mesh.OriginalPositionTranslate)
                || !NearlyEqual(mesh.GeometryData.RotationEulerDegrees, mesh.OriginalRotationEulerDegrees);
        }

        private static bool NearlyEqual(Vector4 left, Vector4 right, float epsilon = 0.0001f)
        {
            return MathF.Abs(left.X - right.X) <= epsilon
                && MathF.Abs(left.Y - right.Y) <= epsilon
                && MathF.Abs(left.Z - right.Z) <= epsilon
                && MathF.Abs(left.W - right.W) <= epsilon;
        }

        private static bool NearlyEqual(Vector3 left, Vector3 right, float epsilon = 0.0001f)
        {
            return MathF.Abs(left.X - right.X) <= epsilon
                && MathF.Abs(left.Y - right.Y) <= epsilon
                && MathF.Abs(left.Z - right.Z) <= epsilon;
        }
    }

    // ── Undo action types (used by ViewportPage) ────────────────────────────────

    // Interface for all undo/redo actions in the viewport.
    public interface IUndoAction
    {
        string Description { get; }
        void Undo();
        void Redo();
    }

    // Stores before/after transform for a single mesh
    public class MeshTransformEntry
    {
        public MeshNode? Mesh { get; set; }
        public Vector4 OldScale { get; set; }
        public Vector4 OldTranslate { get; set; }
        public Vector3 OldRotation { get; set; }
        public Vector4 NewScale { get; set; }
        public Vector4 NewTranslate { get; set; }
        public Vector3 NewRotation { get; set; }
    }

    // Undo action for mesh transform changes
    public class MeshTransformAction : IUndoAction
    {
        public string Description { get; }
        public List<MeshTransformEntry> Entries { get; }

        public MeshTransformAction(string description, List<MeshTransformEntry> entries)
        {
            Description = description;
            Entries = entries;
        }

        public void Undo()
        {
            foreach (var entry in Entries)
            {
                if (entry.Mesh.GeometryData?.SourceMesh == null) continue;
                entry.Mesh.GeometryData.SourceMesh.PositionScale = entry.OldScale;
                entry.Mesh.GeometryData.SourceMesh.PositionTranslate = entry.OldTranslate;
                entry.Mesh.GeometryData.RotationEulerDegrees = entry.OldRotation;
            }
        }

        public void Redo()
        {
            foreach (var entry in Entries)
            {
                if (entry.Mesh.GeometryData?.SourceMesh == null) continue;
                entry.Mesh.GeometryData.SourceMesh.PositionScale = entry.NewScale;
                entry.Mesh.GeometryData.SourceMesh.PositionTranslate = entry.NewTranslate;
                entry.Mesh.GeometryData.RotationEulerDegrees = entry.NewRotation;
            }
        }
    }

    // Undo action for locator transform
    public class LocatorTransformAction : IUndoAction
    {
        public string Description { get; }
        public LocatorNode Node { get; }
        public Matrix4x4 OldTransform { get; }
        public Matrix4x4 NewTransform { get; set; }

        public LocatorTransformAction(string description, LocatorNode node, Matrix4x4 oldTransform)
        {
            Description = description;
            Node = node;
            OldTransform = oldTransform;
        }

        public void Undo()
        {
            Node.LocatorEntry.SceneTransform = OldTransform;
            if (Node.Parent is LocatorsXmlNode xmlRoot)
                xmlRoot.IsDirty = true;
        }

        public void Redo()
        {
            Node.LocatorEntry.SceneTransform = NewTransform;
            if (Node.Parent is LocatorsXmlNode xmlRoot)
                xmlRoot.IsDirty = true;
        }
    }

    public class AvPinTransformAction : IUndoAction
    {
        public string Description { get; }
        public AvPinNode Node { get; }
        public double OldPosX { get; }
        public double OldPosY { get; }
        public double OldPosZ { get; }
        public double OldAxisYaw { get; }
        public double OldAxisPitch { get; }
        public double NewPosX { get; set; }
        public double NewPosY { get; set; }
        public double NewPosZ { get; set; }
        public double NewAxisYaw { get; set; }
        public double NewAxisPitch { get; set; }

        public bool HasChanges => OldPosX != NewPosX || OldPosY != NewPosY || OldPosZ != NewPosZ || OldAxisYaw != NewAxisYaw || OldAxisPitch != NewAxisPitch;

        public AvPinTransformAction(
            string description,
            AvPinNode node,
            double oldPosX,
            double oldPosY,
            double oldPosZ,
            double oldAxisYaw,
            double oldAxisPitch)
        {
            Description = description;
            Node = node;
            OldPosX = oldPosX;
            OldPosY = oldPosY;
            OldPosZ = oldPosZ;
            OldAxisYaw = oldAxisYaw;
            OldAxisPitch = oldAxisPitch;
        }

        public void Undo()
        {
            Apply(OldPosX, OldPosY, OldPosZ, OldAxisYaw, OldAxisPitch);
        }

        public void Redo()
        {
            Apply(NewPosX, NewPosY, NewPosZ, NewAxisYaw, NewAxisPitch);
        }

        private void Apply(double posX, double posY, double posZ, double axisYaw, double axisPitch)
        {
            var visibility = Node.PoiData?.Visibility;
            if (visibility == null)
                return;

            visibility.PosX = posX;
            visibility.PosY = posY;
            visibility.PosZ = posZ;
            visibility.AxisYaw = axisYaw;
            visibility.AxisPitch = axisPitch;

            if (Node.Parent is AvPinsFileNode fileNode)
                fileNode.IsDirty = true;
        }
    }

    public class LightGroupTransformEntry
    {
        public LightGroupNode? Group { get; set; }
        public Vector4 OldPos { get; set; }
        public Vector4 OldRot { get; set; }
        public Vector4 OldDamagePos { get; set; }
        public Vector4 OldDamageRot { get; set; }
        public Vector4 NewPos { get; set; }
        public Vector4 NewRot { get; set; }
        public Vector4 NewDamagePos { get; set; }
        public Vector4 NewDamageRot { get; set; }

        public bool HasChanges => OldPos != NewPos || OldRot != NewRot || OldDamagePos != NewDamagePos || OldDamageRot != NewDamageRot;
    }

    public class LightGroupTransformAction : IUndoAction
    {
        public string Description { get; }
        public List<LightGroupTransformEntry> Entries { get; }

        public LightGroupTransformAction(string description, List<LightGroupTransformEntry> entries)
        {
            Description = description;
            Entries = entries;
        }

        public void Undo()
        {
            foreach (var entry in Entries)
                Apply(entry, entry.OldPos, entry.OldRot, entry.OldDamagePos, entry.OldDamageRot);
        }

        public void Redo()
        {
            foreach (var entry in Entries)
                Apply(entry, entry.NewPos, entry.NewRot, entry.NewDamagePos, entry.NewDamageRot);
        }

        private static void Apply(LightGroupTransformEntry entry, Vector4 pos, Vector4 rot, Vector4 damagePos, Vector4 damageRot)
        {
            if (entry.Group?.GroupData == null)
                return;

            entry.Group.GroupData.Pos = pos;
            entry.Group.GroupData.Rot = rot;
            entry.Group.GroupData.DamagePos = damagePos;
            entry.Group.GroupData.DamageRot = damageRot;

            if (entry.Group.Parent is LightsBinNode lightsBin)
                lightsBin.IsDirty = true;
        }
    }
}
