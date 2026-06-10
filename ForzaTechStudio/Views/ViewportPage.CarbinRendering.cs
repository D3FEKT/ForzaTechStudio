using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using ForzaTools.Bundles.Blobs;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ForzaTechStudio.Views
{
    public sealed partial class ViewportPage : Page
    {
        private readonly Dictionary<CarbinModelNode, List<GeometryModel3D>> _carbinRenderMap = new();
        private readonly Dictionary<GeometryModel3D, CarbinModelNode> _carbinHitMap = new();
        private readonly Dictionary<GeometryModel3D, (ForzaGeometryData Geometry, ModelBinNode ModelBin)> _carbinMaterialContextMap = new();
        private readonly Dictionary<GeometryModel3D, MeshNode> _carbinSourceMeshMap = new();
        private readonly Dictionary<CarbinModelNode, ModelBinNode?> _carbinModelBinCache = new();

        private void RenderCarbinModel(CarbinModelNode node)
        {
            var modelGroup = _modelGroup;
            if (modelGroup == null || node.Model == null || node.IsChecked != true || !node.UseTransforms)
                return;

            var modelBin = FindMatchingModelBin(node);
            if (modelBin == null)
                return;

            if (_carbinRenderMap.TryGetValue(node, out var cachedElements))
            {
                foreach (var element in cachedElements)
                    SetRenderElementVisible(element, true);

                RefreshSuppressedOriginalMeshes(modelBin);
                return;
            }

            if (!TryResolveCarbinInstanceTransform(node, modelBin, out var instanceTransform))
                return;

            var elements = new List<GeometryModel3D>();

            foreach (var mesh in modelBin.Children.OfType<MeshNode>().Where(m => m.IsChecked == true && m.GeometryData != null))
            {
                var instanceGeometry = CreateCarbinInstanceGeometry(mesh.GeometryData, instanceTransform);
                var model = CreateMesh3D(instanceGeometry, modelBin);
                modelGroup.Children.Add(model);
                elements.Add(model);
                _carbinHitMap[model] = node;
                _carbinMaterialContextMap[model] = (instanceGeometry, modelBin);
                _carbinSourceMeshMap[model] = mesh;
            }

            if (elements.Count > 0)
            {
                _carbinRenderMap[node] = elements;
                RefreshSuppressedOriginalMeshes(modelBin);
            }
        }

        private void HideCarbinModel(CarbinModelNode node)
        {
            var modelBin = FindMatchingModelBin(node);

            if (_carbinRenderMap.TryGetValue(node, out var elements))
                foreach (var element in elements)
                    SetRenderElementVisible(element, false);

            RefreshSuppressedOriginalMeshes(modelBin);
        }

        private void ReleaseCarbinModel(CarbinModelNode node)
        {
            if (!_carbinRenderMap.TryGetValue(node, out var elements))
                return;

            var modelGroup = _modelGroup;
            if (modelGroup != null)
            {
                foreach (var element in elements)
                {
                    modelGroup.Children.Remove(element);
                    _carbinHitMap.Remove(element);
                    _carbinMaterialContextMap.Remove(element);
                    _carbinSourceMeshMap.Remove(element);
                }
            }

            _carbinRenderMap.Remove(node);
        }

        private void RefreshAllCarbinInstances()
        {
            _carbinModelBinCache.Clear();

            foreach (var node in _carbinRenderMap.Keys.ToList())
                ReleaseCarbinModel(node);

            foreach (var carbinModel in EnumerateViewerNodes<CarbinModelNode>(ViewModel.Roots))
            {
                if (carbinModel.IsChecked == true && carbinModel.UseTransforms)
                    RenderCarbinModel(carbinModel);
            }

            RefreshSuppressedOriginalMeshesForAllModelBins();
        }

        private void RefreshCarbinInstancesForModel(ModelBinNode? modelBin)
        {
            if (modelBin == null)
                return;

            foreach (var carbinModel in EnumerateViewerNodes<CarbinModelNode>(ViewModel.Roots))
            {
                if (FindMatchingModelBin(carbinModel) != modelBin)
                    continue;

                if (carbinModel.IsChecked != true || !carbinModel.UseTransforms)
                {
                    ReleaseCarbinModel(carbinModel);
                    RefreshSuppressedOriginalMeshes(modelBin);
                    continue;
                }

                if (!HasVisibleCarbinSourceMeshes(modelBin))
                {
                    HideCarbinModel(carbinModel);
                    continue;
                }

                if (CarbinCacheMatchesVisibleSourceMeshes(carbinModel, modelBin))
                    RenderCarbinModel(carbinModel);
                else
                    RebuildCarbinModel(carbinModel);
            }
        }

        private void RebuildCarbinModel(CarbinModelNode node)
        {
            var modelBin = FindMatchingModelBin(node);
            ReleaseCarbinModel(node);

            if (node.IsChecked == true && node.UseTransforms)
                RenderCarbinModel(node);
            else
                RefreshSuppressedOriginalMeshes(modelBin);
        }

        private static bool HasVisibleCarbinSourceMeshes(ModelBinNode modelBin)
        {
            return modelBin.Children.OfType<MeshNode>().Any(mesh => mesh.IsChecked == true && mesh.GeometryData != null);
        }

        private bool CarbinCacheMatchesVisibleSourceMeshes(CarbinModelNode carbinModel, ModelBinNode modelBin)
        {
            if (!_carbinRenderMap.TryGetValue(carbinModel, out var elements) || elements.Count == 0)
                return false;

            var cachedSources = new HashSet<MeshNode>();
            foreach (var element in elements)
            {
                if (!_carbinSourceMeshMap.TryGetValue(element, out var sourceMesh))
                    return false;

                cachedSources.Add(sourceMesh);
            }

            var visibleSources = modelBin.Children
                .OfType<MeshNode>()
                .Where(mesh => mesh.IsChecked == true && mesh.GeometryData != null)
                .ToHashSet();

            return cachedSources.SetEquals(visibleSources);
        }

        private ModelBinNode? FindMatchingModelBin(CarbinModelNode carbinModel)
        {
            if (_carbinModelBinCache.TryGetValue(carbinModel, out var cachedModelBin))
                return cachedModelBin;

            string carbinPath = NormalizeModelPath(carbinModel.Model?.Path);
            if (string.IsNullOrEmpty(carbinPath))
            {
                _carbinModelBinCache[carbinModel] = null;
                return null;
            }

            string carbinFileName = GetNormalizedFileName(carbinPath);
            var modelBins = EnumerateViewerNodes<ModelBinNode>(ViewModel.Roots).ToList();

            foreach (var modelBin in modelBins)
            {
                foreach (string candidate in GetModelBinCandidatePaths(modelBin))
                {
                    string normalizedCandidate = NormalizeModelPath(candidate);
                    if (string.IsNullOrEmpty(normalizedCandidate))
                        continue;

                    if (normalizedCandidate == carbinPath
                        || normalizedCandidate.EndsWith("/" + carbinPath, StringComparison.OrdinalIgnoreCase)
                    || carbinPath.EndsWith("/" + normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                    {
                        _carbinModelBinCache[carbinModel] = modelBin;
                        return modelBin;
                    }
                }
            }

            var fallback = modelBins.FirstOrDefault(modelBin => GetModelBinCandidatePaths(modelBin)
                .Select(NormalizeModelPath)
                .Select(GetNormalizedFileName)
                .Any(candidateFileName => !string.IsNullOrEmpty(candidateFileName)
                    && candidateFileName == carbinFileName));

            _carbinModelBinCache[carbinModel] = fallback;
            return fallback;
        }

        private bool TryResolveCarbinInstanceTransform(CarbinModelNode carbinModel, ModelBinNode modelBin, out Matrix4x4 instanceTransform)
        {
            instanceTransform = Matrix4x4.Identity;
            if (!carbinModel.UseTransforms)
                return false;

            Matrix4x4 modelTransform = Matrix4x4.Identity;
            if (IsFiniteMatrix(carbinModel.Model.Transform) && !IsIdentityMatrix(carbinModel.Model.Transform))
                modelTransform = carbinModel.Model.Transform;

            Matrix4x4 boneTransform = ResolveCarbinBoneTransform(carbinModel, modelBin);
            instanceTransform = IsFiniteMatrix(boneTransform) ? modelTransform * boneTransform : modelTransform;
            return IsFiniteMatrix(instanceTransform) && !IsIdentityMatrix(instanceTransform);
        }

        private Matrix4x4 ResolveCarbinBoneTransform(CarbinModelNode carbinModel, ModelBinNode modelBin)
        {
            var skeleton = modelBin.Bundle?.Blobs.OfType<SkeletonBlob>().FirstOrDefault();
            if (skeleton == null || skeleton.Bones.Count == 0)
                return Matrix4x4.Identity;

            if (!string.IsNullOrWhiteSpace(carbinModel.Model.BoneName))
            {
                var byName = skeleton.Bones.FirstOrDefault(bone =>
                    string.Equals(bone.Name, carbinModel.Model.BoneName, StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                    return ResolveSkeletonBoneWorldTransform(skeleton, skeleton.Bones.IndexOf(byName));
            }

            int boneId = carbinModel.Model.BoneId;
            if (boneId >= 0 && boneId < skeleton.Bones.Count)
                return ResolveSkeletonBoneWorldTransform(skeleton, boneId);

            return Matrix4x4.Identity;
        }

        private static Matrix4x4 ResolveSkeletonBoneWorldTransform(SkeletonBlob skeleton, int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= skeleton.Bones.Count)
                return Matrix4x4.Identity;

            var transforms = new Matrix4x4[skeleton.Bones.Count];
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                var bone = skeleton.Bones[i];
                var transform = bone.Matrix;
                if (bone.ParentId >= 0 && bone.ParentId < i)
                    transform *= transforms[bone.ParentId];
                transforms[i] = transform;
            }

            return transforms[boneIndex];
        }

        private ForzaGeometryData CreateCarbinInstanceGeometry(ForzaGeometryData source, Matrix4x4 instanceTransform)
        {
            if (!IsFiniteMatrix(instanceTransform))
                instanceTransform = Matrix4x4.Identity;

            var positions = BuildCurrentRenderPositions(source)
                .Select(position => SafeTransform(position, instanceTransform))
                .ToArray();

            Vector3[]? normals = null;
            if (source.Normals != null)
            {
                var rotation = source.GetRotationMatrix();
                bool hasRotation = rotation != Matrix4x4.Identity;
                normals = source.Normals
                    .Select(normal => hasRotation ? Vector3.TransformNormal(normal, rotation) : normal)
                    .Select(normal => NormalizeOrDefault(Vector3.TransformNormal(normal, instanceTransform), Vector3.UnitY))
                    .ToArray();
            }

            return new ForzaGeometryData
            {
                Name = source.Name ?? string.Empty,
                MaterialName = source.MaterialName ?? string.Empty,
                Positions = positions,
                InitialRenderPositions = positions,
                Normals = normals ?? Array.Empty<Vector3>(),
                UVs = source.UVs ?? Array.Empty<Vector2>(),
                UvChannels = source.UvChannels?.ToDictionary(kv => kv.Key, kv => (Vector2[])kv.Value.Clone())
                    ?? new Dictionary<int, Vector2[]>(),
                Indices = source.Indices ?? Array.Empty<int>(),
                SourceMesh = source.SourceMesh,
                BoneTransform = Matrix4x4.Identity,
                BoneIndex = source.BoneIndex,
                BoneName = source.BoneName ?? string.Empty,
                OriginalBoneTransform = source.OriginalBoneTransform
            };
        }

        private void RefreshSuppressedOriginalMeshesForAllModelBins()
        {
            foreach (var modelBin in EnumerateViewerNodes<ModelBinNode>(ViewModel.Roots))
                RefreshSuppressedOriginalMeshes(modelBin);
        }

        private void RefreshSuppressedOriginalMeshes(ModelBinNode? modelBin)
        {
            if (modelBin == null)
                return;

            bool suppress = ShouldAutoHideProxyModelBin(modelBin) || ShouldSuppressOriginalModelBin(modelBin);
            foreach (var mesh in EnumerateViewerNodes<MeshNode>(new IViewerNode[] { modelBin }))
            {
                if (suppress)
                {
                    HideMesh(mesh);
                }
                else if (mesh.IsChecked == true)
                {
                    RenderMesh(mesh);
                }
            }

            foreach (var damageMesh in EnumerateViewerNodes<DamageMeshNode>(new IViewerNode[] { modelBin }))
            {
                if (suppress)
                {
                    HideDamageMesh(damageMesh);
                }
                else if (damageMesh.IsChecked == true)
                {
                    RenderDamageMesh(damageMesh);
                }
            }
        }

        private bool ShouldSuppressOriginalModelBin(ModelBinNode? modelBin)
        {
            if (modelBin == null)
                return false;

            foreach (var carbinModel in EnumerateViewerNodes<CarbinModelNode>(ViewModel.Roots))
            {
                if (carbinModel.IsChecked != true || !carbinModel.UseTransforms)
                    continue;

                if (!_carbinRenderMap.ContainsKey(carbinModel))
                    continue;

                var matchedModelBin = FindMatchingModelBin(carbinModel);
                if (!ReferenceEquals(matchedModelBin, modelBin))
                    continue;

                if (TryResolveCarbinInstanceTransform(carbinModel, modelBin, out _))
                    return true;
            }

            return false;
        }

        private static bool ShouldAutoHideProxyModelBin(ModelBinNode? modelBin)
        {
            if (modelBin == null)
                return false;

            return ContainsProxyToken(modelBin.Name)
                || ContainsProxyToken(modelBin.FileName)
                || ContainsProxyToken(modelBin.FilePath)
                || ContainsProxyToken(modelBin.ZipEntryName);
        }

        private static bool ContainsProxyToken(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Contains("proxy", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<Vector3> BuildCurrentRenderPositions(ForzaGeometryData data)
        {
            var boneTransform = IsFiniteMatrix(data.BoneTransform) ? data.BoneTransform : Matrix4x4.Identity;

            if (data.RawPositions != null && data.SourceMesh != null)
            {
                var scale = data.SourceMesh.PositionScale;
                var trans = data.SourceMesh.PositionTranslate;
                var rotMatrix = data.GetRotationMatrix();
                bool hasRotation = rotMatrix != Matrix4x4.Identity;

                foreach (var raw in data.RawPositions)
                {
                    var scaled = new Vector3(raw.X * scale.X, raw.Y * scale.Y, raw.Z * scale.Z);
                    if (hasRotation)
                        scaled = Vector3.Transform(scaled, rotMatrix);

                    var local = new Vector3(scaled.X + trans.X, scaled.Y + trans.Y, scaled.Z + trans.Z);
                    var transformed = Vector3.Transform(local, boneTransform);

                    if (!IsFiniteVector(transformed))
                        transformed = IsFiniteVector(local) ? local : Vector3.Zero;

                    yield return transformed;
                }
                yield break;
            }

            if (data.InitialRenderPositions != null)
            {
                foreach (var position in data.InitialRenderPositions)
                    yield return IsFiniteVector(position) ? position : Vector3.Zero;
                yield break;
            }

            if (data.Positions != null)
            {
                foreach (var position in data.Positions)
                    yield return IsFiniteVector(position) ? position : Vector3.Zero;
            }
        }

        private static Vector3 SafeTransform(Vector3 value, Matrix4x4 transform)
        {
            var transformed = Vector3.Transform(value, transform);
            return IsFiniteVector(transformed) ? transformed : value;
        }

        private static IEnumerable<T> EnumerateViewerNodes<T>(IEnumerable<IViewerNode> roots) where T : class, IViewerNode
        {
            foreach (var root in roots)
            {
                if (root is T typed)
                    yield return typed;

                foreach (var child in EnumerateViewerNodes<T>(root.Children))
                    yield return child;
            }
        }

        private static IEnumerable<string> GetModelBinCandidatePaths(ModelBinNode modelBin)
        {
            if (!string.IsNullOrEmpty(modelBin.ZipEntryName)) yield return modelBin.ZipEntryName;
            if (!string.IsNullOrEmpty(modelBin.FilePath)) yield return modelBin.FilePath;
            if (!string.IsNullOrEmpty(modelBin.FileName)) yield return modelBin.FileName;
            if (!string.IsNullOrEmpty(modelBin.Name)) yield return modelBin.Name;
        }

        private static string NormalizeModelPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Replace('\\', '/')
                .Trim()
                .TrimStart('/')
                .ToLowerInvariant();
        }

        private static string GetNormalizedFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            int slashIndex = path.LastIndexOf('/');
            return slashIndex >= 0 ? path[(slashIndex + 1)..] : path;
        }

        private static bool IsRootCarbinBoneName(string? boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
                return false;

            string normalized = boneName.Trim();
            return string.Equals(normalized, "root", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "<root>", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIdentityMatrix(in Matrix4x4 matrix, float epsilon = 0.0001f)
        {
            return Math.Abs(matrix.M11 - 1f) <= epsilon
                && Math.Abs(matrix.M22 - 1f) <= epsilon
                && Math.Abs(matrix.M33 - 1f) <= epsilon
                && Math.Abs(matrix.M44 - 1f) <= epsilon
                && Math.Abs(matrix.M12) <= epsilon
                && Math.Abs(matrix.M13) <= epsilon
                && Math.Abs(matrix.M14) <= epsilon
                && Math.Abs(matrix.M21) <= epsilon
                && Math.Abs(matrix.M23) <= epsilon
                && Math.Abs(matrix.M24) <= epsilon
                && Math.Abs(matrix.M31) <= epsilon
                && Math.Abs(matrix.M32) <= epsilon
                && Math.Abs(matrix.M34) <= epsilon
                && Math.Abs(matrix.M41) <= epsilon
                && Math.Abs(matrix.M42) <= epsilon
                && Math.Abs(matrix.M43) <= epsilon;
        }
    }
}
