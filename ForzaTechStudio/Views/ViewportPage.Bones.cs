using ForzaTools.Bundles.Blobs;
using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ForzaTechStudio.Views
{
    // Bone editing section (Model Properties > Bone expander)
    public sealed partial class ViewportPage : Page
    {
        private bool _isUpdatingBoneUi = false;
        private bool _useBoneTransformsForViewport = true;

        // UpdateBoneUI
        // Called from MeshSelector_SelectionChanged / ModelBinSelector_SelectionChanged.
        // Shows/hides the BoneInfoExpander and populates BoneNameText + BM11–BM44.
        internal void UpdateBoneUI()
        {
            if (BoneInfoExpander == null) return;

            if (ModelBinSelector.SelectedItem is not ModelBinNode modelBin)
            {
                BoneInfoExpander.Visibility = Visibility.Collapsed;
                return;
            }

            // Only show the expander when the modelbin has a skeleton blob
            var skel = modelBin.Bundle.Blobs.OfType<SkeletonBlob>().FirstOrDefault();
            if (skel == null || skel.Bones.Count == 0)
            {
                BoneInfoExpander.Visibility = Visibility.Collapsed;
                return;
            }

            BoneInfoExpander.Visibility = Visibility.Visible;

            // Resolve the meshes currently targeted by MeshSelector (may be empty)
            var meshScope = MeshSelector.SelectedItem as MeshScopeItem;
            List<MeshNode> meshes = ResolveScopeMeshes(modelBin, meshScope);

            if (meshes.Count == 0)
            {
                // No mesh selected yet — show placeholder state
                _isUpdatingBoneUi = true;
                try
                {
                    BoneNameText.Text = "(select a mesh)";
                    ClearBoneMatrixFields();
                }
                finally
                {
                    _isUpdatingBoneUi = false;
                }
                return;
            }

            var first = meshes[0];
            var geo = first.GeometryData;

            // Check for multiple bone names
            bool multipleBones = meshes.Skip(1).Any(m =>
                m.GeometryData?.BoneName != geo?.BoneName);

            _isUpdatingBoneUi = true;
            try
            {
                if (multipleBones)
                {
                    BoneNameText.Text = "(multiple bones)";
                    ClearBoneMatrixFields();
                }
                else
                {
                    string boneName = geo?.BoneName ?? string.Empty;
                    int boneIdx = geo?.BoneIndex ?? -1;
                    if (boneIdx >= 0 && boneIdx < skel.Bones.Count)
                        boneName = $"[{boneIdx}] {skel.Bones[boneIdx].Name}";
                    else if (string.IsNullOrEmpty(boneName))
                        boneName = "(no bone)";

                    BoneNameText.Text = boneName;

                    // Populate the 4×4 matrix boxes from SourceBone.Matrix (local)
                    var mat = geo?.SourceBone?.Matrix ?? Matrix4x4.Identity;
                    PopulateBoneMatrixFields(mat);
                }
            }
            finally
            {
                _isUpdatingBoneUi = false;
            }
        }

        // BoneMatrix_TextChanged  — live update when user edits a cell
        private void BoneMatrix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingBoneUi) return;
            if (ModelBinSelector.SelectedItem is not ModelBinNode modelBin) return;

            var meshScope = MeshSelector.SelectedItem as MeshScopeItem;
            var meshes = ResolveScopeMeshes(modelBin, meshScope);
            if (meshes.Count == 0) return;

            var currentMatrix = meshes[0].GeometryData?.SourceBone?.Matrix ?? Matrix4x4.Identity;

            // Parse the visible TRS fields; bail if any are invalid.
            if (!TryParseBoneMatrixFields(currentMatrix, out Matrix4x4 localMatrix)) return;

            var skel = modelBin.Bundle.Blobs.OfType<SkeletonBlob>().FirstOrDefault();

            foreach (var mesh in meshes)
            {
                var geo = mesh.GeometryData;
                if (geo?.SourceBone == null) continue;

                // Write back to the bone object (modifies the bundle in-place)
                geo.SourceBone.Matrix = localMatrix;

                // Recompute cached world matrix
                if (skel != null)
                {
                    var newWorld = RecomputeBoneWorldMatrix(skel, geo.BoneIndex);
                    geo.BoneTransform = newWorld;

                    // Update 3D viewport if the toggle is on
                    if (_useBoneTransformsForViewport)
                        UpdateMeshRenderingWithBoneTransform(mesh, newWorld);
                }
            }
        }

        // ChangeBone_Click  — popup to pick a different bone
        private async void ChangeBone_Click(object sender, RoutedEventArgs e)
        {
            if (ModelBinSelector.SelectedItem is not ModelBinNode modelBin) return;

            var skel = modelBin.Bundle.Blobs.OfType<SkeletonBlob>().FirstOrDefault();
            if (skel == null || skel.Bones.Count == 0)
            {
                await ShowError("No skeleton found in this modelbin.");
                return;
            }

            var meshScope = MeshSelector.SelectedItem as MeshScopeItem;
            var meshes = ResolveScopeMeshes(modelBin, meshScope);
            if (meshes.Count == 0) return;

            // Build a ListView of all bones
            var boneList = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 320
            };

            var items = skel.Bones.Select((b, i) => $"[{i}] {b.Name}").ToList();
            boneList.ItemsSource = items;

            // Pre-select current bone of first mesh
            int currentIdx = meshes[0].GeometryData?.BoneIndex ?? -1;
            if (currentIdx >= 0 && currentIdx < items.Count)
                boneList.SelectedIndex = currentIdx;

            var dialog = new ContentDialog
            {
                Title = "Select Bone",
                Content = boneList,
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            int selectedIndex = boneList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= skel.Bones.Count) return;

            var selectedBone = skel.Bones[selectedIndex];

            // Apply new bone to all meshes in scope
            foreach (var mesh in meshes)
            {
                var geo = mesh.GeometryData;
                if (geo == null) continue;

                var meshBlob = geo.SourceMesh;
                if (meshBlob == null) continue;

                meshBlob.RigidBoneIndex = (short)selectedIndex;
                geo.BoneIndex = (short)selectedIndex;
                geo.BoneName = selectedBone.Name;
                geo.SourceBone = selectedBone;

                var newWorld = RecomputeBoneWorldMatrix(skel, selectedIndex);
                geo.BoneTransform = newWorld;

                if (_useBoneTransformsForViewport)
                    UpdateMeshRenderingWithBoneTransform(mesh, newWorld);
            }

            // Refresh UI
            UpdateBoneUI();
        }

        // UseBoneTransforms_Click  — viewport toggle
        private void UseBoneTransforms_Click(object sender, RoutedEventArgs e)
        {
            _useBoneTransformsForViewport = UseBoneTransformsToggle.IsChecked == true;

            if (ModelBinSelector.SelectedItem is not ModelBinNode modelBin) return;
            var meshScope = MeshSelector.SelectedItem as MeshScopeItem;
            var meshes = ResolveScopeMeshes(modelBin, meshScope);

            foreach (var mesh in meshes)
            {
                var geo = mesh.GeometryData;
                if (geo?.SourceMesh == null) continue;

                if (_useBoneTransformsForViewport &&
                    geo.SourceBone != null &&
                    BoneTransformService.IsSignificantBone(geo.BoneIndex))
                {
                    UpdateMeshRenderingWithBoneTransform(mesh, geo.BoneTransform);
                }
                else
                {
                    var mb = geo.SourceMesh;
                    UpdateMeshRendering(mesh,
                        mb.PositionScale.X, mb.PositionScale.Y, mb.PositionScale.Z,
                        mb.PositionTranslate.X, mb.PositionTranslate.Y, mb.PositionTranslate.Z);
                }
            }
        }

        // RecomputeBoneWorldMatrix

        private static Matrix4x4 RecomputeBoneWorldMatrix(SkeletonBlob skel, int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= skel.Bones.Count)
                return Matrix4x4.Identity;

            // Build the full world-matrix array for all bones up to boneIndex
            int count = Math.Min(boneIndex + 1, skel.Bones.Count);
            var worlds = new Matrix4x4[count];
            for (int i = 0; i < count; i++)
            {
                var b = skel.Bones[i];
                var local = b.Matrix;
                if (b.ParentId >= 0 && b.ParentId < i)
                    worlds[i] = local * worlds[b.ParentId];
                else
                    worlds[i] = local;
            }

            return worlds[boneIndex];
        }

        // Helpers
        private List<MeshNode> ResolveScopeMeshes(ModelBinNode modelBin, MeshScopeItem? scope)
        {
            if (scope == null)
                return modelBin.Children.OfType<MeshNode>().ToList();

            if (scope.Node != null)
                return new List<MeshNode> { scope.Node };

            if (!string.IsNullOrEmpty(scope.MaterialGroup))
                return modelBin.Children.OfType<MeshNode>()
                    .Where(m => m.GeometryData?.MaterialName == scope.MaterialGroup)
                    .ToList();

            return modelBin.Children.OfType<MeshNode>().ToList();
        }

        private void PopulateBoneMatrixFields(Matrix4x4 m)
        {
            PopulateTransformEditorFields(
                BonePosX,
                BonePosY,
                BonePosZ,
                BoneRotX,
                BoneRotY,
                BoneRotZ,
                BoneScaleX,
                BoneScaleY,
                BoneScaleZ,
                m);
        }

        private void ClearBoneMatrixFields()
        {
            foreach (var tb in new[] { BonePosX, BonePosY, BonePosZ,
                                       BoneRotX, BoneRotY, BoneRotZ,
                                       BoneScaleX, BoneScaleY, BoneScaleZ })
            {
                tb.Text = string.Empty;
            }
        }

        private bool TryParseBoneMatrixFields(Matrix4x4 currentMatrix, out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.Identity;
            static bool P(TextBox box, out float value) =>
                float.TryParse(box.Text,
                               System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture,
                               out value);

            if (!P(BonePosX, out float posX) || !P(BonePosY, out float posY) || !P(BonePosZ, out float posZ) ||
                !P(BoneRotX, out float rotX) || !P(BoneRotY, out float rotY) || !P(BoneRotZ, out float rotZ) ||
                !P(BoneScaleX, out float scaleX) || !P(BoneScaleY, out float scaleY) || !P(BoneScaleZ, out float scaleZ))
            {
                return false;
            }

            matrix = ComposeTransformMatrix(
                new Vector3(posX, posY, posZ),
                new Vector3(rotX, rotY, rotZ),
                new Vector3(scaleX, scaleY, scaleZ),
                currentMatrix);
            return true;
        }
    }
}
