using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using ForzaTools.CarScene;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Numerics;

namespace ForzaTechStudio.ViewModels.ThreeDViewer
{
    public interface IViewerNode
    {
        string Name { get; set; }
        bool? IsChecked { get; set; }
        bool IsExpanded { get; set; }
        ObservableCollection<IViewerNode> Children { get; }
        IViewerNode Parent { get; set; }
        NodeType Type { get; }
    }

    public enum NodeType
    {
        Zip,
        Folder,
        ModelBin,
        Mesh,
        PhysicsDefinition,
        LightsBin,
        LightGroup,
        LightRow,
        LocatorsXml,
        Locator,
        GrannyFile,
        Skeleton,
        Bone,
        AnimationClip,
        GsfInfo,
        AvPinsFile,
        AvPin,
        DamageMesh,
        CarbinFile,
        CarbinPart,
        CarbinModel
    }

    public abstract partial class ViewerNode : ObservableObject, IViewerNode
    {
        private bool _isSettingChildren = false;

        // Suppresses checkbox cascade updates during bulk child construction.
        public static bool SuppressCheckCascade { get; set; }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        private ObservableCollection<IViewerNode> _children = new();
        public ObservableCollection<IViewerNode> Children
        {
            get => _children;
            private set => SetProperty(ref _children, value);
        }

        public IViewerNode Parent { get; set; }

        public abstract NodeType Type { get; }

        private bool? _isChecked = true;
        public bool? IsChecked
        {
            get => _isChecked;
            set
            {

                var efficientValue = value;
                if (efficientValue == null) efficientValue = false;

                if (SetProperty(ref _isChecked, efficientValue))
                {
                    OnIsCheckedChanged();
                }
            }
        }

        protected virtual void OnIsCheckedChanged()
        {
            if (IsChecked.HasValue && !SuppressCheckCascade)
            {
                try
                {
                    _isSettingChildren = true;
                    if (IsChecked == false)
                    {
                        // Snapshot child states before unchecking so we can restore them
                        _childCheckSnapshot = new Dictionary<IViewerNode, bool?>(Children.Count);
                        foreach (var child in Children)
                        {
                            _childCheckSnapshot[child] = child.IsChecked;
                            if (child.IsChecked != false)
                            {
                                child.IsChecked = false;
                            }
                        }
                    }
                    else
                    {
                        // Restore prior child states if we have a snapshot, else cascade true
                        if (_childCheckSnapshot != null)
                        {
                            foreach (var child in Children)
                            {
                                bool? restored = _childCheckSnapshot.TryGetValue(child, out var s) ? s : true;
                                if (restored == null) restored = true;
                                if (child.IsChecked != restored)
                                {
                                    child.IsChecked = restored;
                                }
                            }
                            _childCheckSnapshot = null;
                        }
                        else
                        {
                            foreach (var child in Children)
                            {
                                if (child.IsChecked != true)
                                {
                                    child.IsChecked = true;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    _isSettingChildren = false;
                }
            }

            if (!SuppressCheckCascade && Parent is ViewerNode parentNode)
            {
                parentNode.UpdateCheckStateFromChildren();
            }
        }

        // Snapshot of children's check states captured when this node is unchecked
        private Dictionary<IViewerNode, bool?> _childCheckSnapshot;

        public void UpdateCheckStateFromChildren()
        {
            if (_isSettingChildren || SuppressCheckCascade) return;
            if (Children.Count == 0) return;

            bool allChecked = Children.All(c => c.IsChecked == true);
            bool allUnchecked = Children.All(c => c.IsChecked == false);

            // Mixed (some checked, some not) ? parent stays checked, not indeterminate
            bool? newState = allUnchecked ? false : true;

            if (_isChecked != newState)
            {
                SetProperty(ref _isChecked, newState, nameof(IsChecked));
                
                if (Parent is ViewerNode parentNode)
                {
                    parentNode.UpdateCheckStateFromChildren();
                }
            }
        }
        
        public ICommand CloseCommand { get; set; }
    }

    public class ZipNode : ViewerNode
    {
        public override NodeType Type => NodeType.Zip;
        public string FilePath { get; set; }
        public ManufacturerColorsBlob? ManufacturerColors { get; set; }
    }

    public class FolderNode : ViewerNode
    {
        public override NodeType Type => NodeType.Folder;
    }

    public class ModelBinNode : ViewerNode
    {
        public override NodeType Type => NodeType.ModelBin;
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public ForzaTools.Bundles.Bundle Bundle { get; set; }
        public string? SourceZipPath { get; set; }
        public string? ZipEntryName { get; set; }
        public bool IsDirty { get; set; }

        public string BoneStatusText
        {
            get
            {
                var meshesWithBones = Children.OfType<MeshNode>()
                    .Where(m => m.GeometryData?.SourceBone != null 
                             && BoneTransformService.IsSignificantBone(m.GeometryData.BoneIndex))
                    .ToList();
                
                if (meshesWithBones.Count == 0)
                    return " (No Bone)";
                
                var firstBone = meshesWithBones[0].GeometryData.BoneName;
                if (meshesWithBones.All(m => m.GeometryData.BoneName == firstBone))
                    return $" (Bone: {firstBone})";
                
                return $" ({meshesWithBones.Count} Bones)";
            }
        }
    }

    public class PhysicsDefinitionNode : ViewerNode
    {
        public override NodeType Type => NodeType.PhysicsDefinition;
        public List<PhysicsDefinitionParser.PhysicsDefinition> Definitions { get; set; }
    }

    public class LightsBinNode : ViewerNode
    {
        public override NodeType Type => NodeType.LightsBin;
        public string FilePath { get; set; }
        public LightsBinParser.LightsBinData OriginalData { get; set; }
        public bool IsDirty { get; set; }
        public string? SourceZipPath { get; set; }
        public string? ZipEntryName { get; set; }
    }

    public class LightGroupNode : ViewerNode
    {
        public override NodeType Type => NodeType.LightGroup;
        public LightsBinParser.LightGroup GroupData { get; set; }
    }

    public class LightRowNode : ViewerNode
    {
        public override NodeType Type => NodeType.LightRow;
        public int RowIndex { get; set; }
        public Vector4 RowData { get; set; }
    }

    public class MeshNode : ViewerNode
    {
        public override NodeType Type => NodeType.Mesh;
        
        public ForzaGeometryData GeometryData { get; set; }
        public int LODLevel { get; set; }
        public bool IsShadow { get; set; }
        
        public Vector4 OriginalPositionScale { get; set; }
        public Vector4 OriginalPositionTranslate { get; set; }
        public Vector3 OriginalRotationEulerDegrees { get; set; } = Vector3.Zero;
        
        public ModelBinNode ParentModelBin { get; set; }
    }

    public class DamageMeshNode : ViewerNode
    {
        public override NodeType Type => NodeType.DamageMesh;
        public ForzaGeometryData GeometryData { get; set; }
        public ModelBinNode ParentModelBin { get; set; }
        public bool IsShadow { get; set; }
    }

    public class LocatorsXmlNode : ViewerNode
    {
        public override NodeType Type => NodeType.LocatorsXml;
        public string FilePath { get; set; }
        public LocatorsData LocatorsData { get; set; }
        public bool IsDirty { get; set; }
        public string? SourceZipPath { get; set; }
        public string? ZipEntryName { get; set; }
    }

    public class LocatorNode : ViewerNode
    {
        public override NodeType Type => NodeType.Locator;
        public LocatorEntry LocatorEntry { get; set; }
    }

    public class GrannyFileNode : ViewerNode
    {
        public override NodeType Type => NodeType.GrannyFile;
        public string? FilePath { get; set; }
        public string? SourceZipPath { get; set; }
        public GrannyFileData? FileData { get; set; }
        public bool IsGsf { get; set; }
    }

    public class SkeletonNode : ViewerNode
    {
        public override NodeType Type => NodeType.Skeleton;
        public GrannySkeleton SkeletonData { get; set; }
    }

    public class BoneNode : ViewerNode
    {
        public override NodeType Type => NodeType.Bone;
        public GrannyBone BoneData { get; set; }
        public int BoneIndex { get; set; }
    }

    public class AnimationClipNode : ViewerNode
    {
        public override NodeType Type => NodeType.AnimationClip;
        public GrannyAnimation AnimationData { get; set; }
    }

    public class GsfInfoNode : ViewerNode
    {
        public override NodeType Type => NodeType.GsfInfo;
        public GsfCharacterInfo CharacterInfoData { get; set; }
    }

    public class AvPinsFileNode : ViewerNode
    {
        public override NodeType Type => NodeType.AvPinsFile;
        public string? FilePath { get; set; }
        public AvPinsData? AvPinsData { get; set; }
        public bool IsDirty { get; set; }
        public string? SourceZipPath { get; set; }
        public string? ZipEntryName { get; set; }
    }

    public class AvPinNode : ViewerNode
    {
        public override NodeType Type => NodeType.AvPin;
        public PointOfInterest PoiData { get; set; }
    }

    public class CarbinFileNode : ViewerNode
    {
        public override NodeType Type => NodeType.CarbinFile;
        public string? FilePath { get; set; }
        public string? SourceZipPath { get; set; }
        public string? ZipEntryName { get; set; }
        public CarbinFile? CarbinData { get; set; }
        public bool IsDirty { get; set; }
    }

    public class CarbinPartNode : ViewerNode
    {
        public override NodeType Type => NodeType.CarbinPart;
        public string PartCategory { get; set; } = string.Empty;
        public object? PartData { get; set; }
    }

    public class CarbinModelNode : ViewerNode
    {
        public override NodeType Type => NodeType.CarbinModel;
        public CarRenderModel Model { get; set; }
        public int ModelIndex { get; set; }
        public string PartName { get; set; } = string.Empty;

        private bool _useTransforms = true;
        public bool UseTransforms
        {
            get => _useTransforms;
            set => SetProperty(ref _useTransforms, value);
        }
    }

    // Represents a single bone's matching status across ModelBin skeleton, GR2 skeleton, and animation tracks.
    public class BoneMatchEntry
    {
        public int Index { get; set; }
        public string BoneName { get; set; }
        public bool InModelBinSkeleton { get; set; }
        public bool InGr2Skeleton { get; set; }
        public bool InAnimTrack { get; set; }
        public bool FullMatch => InModelBinSkeleton && InGr2Skeleton;
        public int LinkedMeshCount { get; set; }
        public List<string> LinkedMeshNames { get; set; } = new();
        public string StatusIcon => FullMatch ? "?" : (InModelBinSkeleton || InGr2Skeleton) ? "~" : "?";
        public string StatusColor => FullMatch ? "Green" : InModelBinSkeleton || InGr2Skeleton ? "Orange" : "Red";
    }

    // Lightweight material item used in the Viewport materials section
    public partial class ViewportMaterialItem : ObservableObject
    {
        public MaterialBlob Blob { get; }
        public string SourceModelName { get; }

        public string Name
        {
            get
            {
                var meta = Blob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
                return meta?.Name ?? "Unnamed";
            }
        }

        public uint MaterialId
        {
            get
            {
                var meta = Blob.Metadatas.OfType<IdentifierMetadata>().FirstOrDefault();
                return meta?.Id ?? Blob.Id;
            }
        }

        public string MaterialPath
        {
            get
            {
                if (Blob.Bundle != null)
                {
                    var mati = Blob.Bundle.Blobs.OfType<MaterialResourceBlob>().FirstOrDefault();
                    return mati?.Path ?? string.Empty;
                }
                return string.Empty;
            }
        }

        public ObservableCollection<ShaderParameter> Parameters { get; } = new();

        public ViewportMaterialItem(MaterialBlob blob, string sourceModelName)
        {
            Blob = blob;
            SourceModelName = sourceModelName;
            var paramBlob = blob.Bundle?.Blobs.OfType<MaterialShaderParameterBlob>().FirstOrDefault();
            if (paramBlob != null)
                foreach (var p in paramBlob.Parameters)
                    Parameters.Add(p);
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(MaterialId));
            OnPropertyChanged(nameof(MaterialPath));
            Parameters.Clear();
            var paramBlob = Blob.Bundle?.Blobs.OfType<MaterialShaderParameterBlob>().FirstOrDefault();
            if (paramBlob != null)
                foreach (var p in paramBlob.Parameters)
                    Parameters.Add(p);
        }
    }

    public partial class ViewportTab : ObservableObject
    {
        [ObservableProperty] private string _name;
        public List<IViewerNode> Roots { get; } = new();
        public ViewportTab(string name) => _name = name;
    }

    public partial class ViewportViewModel : ObservableObject
    {
        private ObservableCollection<IViewerNode> _roots = new();
        public ObservableCollection<IViewerNode> Roots
        {
            get => _roots;
            set => SetProperty(ref _roots, value);
        }

        private IViewerNode _selectedNode;
        public IViewerNode SelectedNode
        {
            get => _selectedNode;
            set => SetProperty(ref _selectedNode, value);
        }

        public ObservableCollection<ViewportTab> Tabs { get; } = new();

        private ViewportTab? _activeTab;
        public ViewportTab? ActiveTab
        {
            get => _activeTab;
            set => SetProperty(ref _activeTab, value);
        }

        public ObservableCollection<ViewportMaterialItem> ViewportMaterials { get; } = new();

        // Materials for the mesh-level material editor (swap list for selected mesh)
        public ObservableCollection<ViewportMaterialItem> MeshMaterials { get; } = new();

        private ViewportMaterialItem? _selectedMeshMaterial;
        public ViewportMaterialItem? SelectedMeshMaterial
        {
            get => _selectedMeshMaterial;
            set => SetProperty(ref _selectedMeshMaterial, value);
        }

        public void RefreshViewportMaterials()
        {
            ViewportMaterials.Clear();
            foreach (var root in Roots)
                CollectMaterialsFromNode(root);
        }

        private void CollectMaterialsFromNode(IViewerNode node)
        {
            if (node is ModelBinNode mb && mb.Bundle != null)
                foreach (var blob in mb.Bundle.Blobs.OfType<MaterialBlob>())
                    ViewportMaterials.Add(new ViewportMaterialItem(blob, mb.Name));
            foreach (var child in node.Children)
                CollectMaterialsFromNode(child);
        }

        public ViewportTab AddTab(string name)
        {
            var tab = new ViewportTab(name);
            Tabs.Add(tab);
            return tab;
        }

        public ViewportViewModel()
        {
        }

        public void AddRoot(IViewerNode root)
        {
            Roots.Add(root);
            ActiveTab?.Roots.Add(root);
        }

        public void RemoveRoot(IViewerNode root)
        {
            Roots.Remove(root);
            foreach (var tab in Tabs)
                tab.Roots.Remove(root);
        }

        private RelayCommand _closeSelectedCommand;
        public ICommand CloseSelectedCommand => _closeSelectedCommand ??= new RelayCommand(CloseSelected);

        private void CloseSelected()
        {
            if (SelectedNode == null) return;

            var root = FindRoot(SelectedNode);
            if (root != null)
            {
                RequestCloseRoot?.Invoke(this, root);
                RemoveRoot(root);
                // Also clear selection?
                if (SelectedNode != null && FindRoot(SelectedNode) == root)
                {
                    SelectedNode = null;
                }
            }
        }

        private RelayCommand _closeAllCommand;
        public ICommand CloseAllCommand => _closeAllCommand ??= new RelayCommand(CloseAll);

        private void CloseAll()
        {
            var roots = Roots.ToList();
            foreach (var root in roots)
                RequestCloseRoot?.Invoke(this, root);
            Roots.Clear();
            ActiveTab?.Roots.Clear();
            SelectedNode = null;
        }

        private IViewerNode FindRoot(IViewerNode node)
        {
            var current = node;
            while (current.Parent != null)
            {
                current = current.Parent;
            }
            return current;
        }

        public event EventHandler<IViewerNode> RequestCloseRoot;

        // Cross-file helpers 

        // Finds all GrannyFileNodes across every loaded root.
        public List<GrannyFileNode> GetAllGrannyFileNodes()
        {
            var result = new List<GrannyFileNode>();
            foreach (var root in Roots)
                CollectNodes(root, result);
            return result;
        }

        // Finds all ModelBinNodes across every loaded root.
        public List<ModelBinNode> GetAllModelBinNodes()
        {
            var result = new List<ModelBinNode>();
            foreach (var root in Roots)
                CollectNodes(root, result);
            return result;
        }

        // Finds all SkeletonNodes across every loaded root.
        public List<SkeletonNode> GetAllSkeletonNodes()
        {
            var result = new List<SkeletonNode>();
            foreach (var root in Roots)
                CollectNodes(root, result);
            return result;
        }

        // Finds the best-matching GR2 skeleton for a given ModelBin by comparing bone names.
        // Also considers animation track names as valid bone name sources.
        // Returns (skeleton, matchCount, totalGR2Bones).
        public (GrannySkeleton Skeleton, SkeletonNode Node, int MatchCount, int TotalBones)? FindBestSkeletonMatch(ModelBinNode modelBin)
        {
            var modelBinBoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mesh in modelBin.Children.OfType<MeshNode>())
            {
                if (!string.IsNullOrEmpty(mesh.GeometryData?.BoneName))
                    modelBinBoneNames.Add(mesh.GeometryData.BoneName);
            }

            // Also collect from the ModelBin skeleton blob if present
            if (modelBin.Bundle != null)
            {
                var skelBlob = modelBin.Bundle.Blobs.OfType<ForzaTools.Bundles.Blobs.SkeletonBlob>().FirstOrDefault();
                if (skelBlob != null)
                {
                    foreach (var bone in skelBlob.Bones)
                    {
                        if (!string.IsNullOrEmpty(bone.Name))
                            modelBinBoneNames.Add(bone.Name);
                    }
                }
            }

            if (modelBinBoneNames.Count == 0)
                return null;

            (GrannySkeleton Skeleton, SkeletonNode Node, int MatchCount, int TotalBones)? best = null;

            foreach (var skelNode in GetAllSkeletonNodes())
            {
                if (skelNode.SkeletonData == null) continue;
                var skel = skelNode.SkeletonData;

                // Build combined name set: skeleton bones + animation track names from same file
                var gr2Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var bone in skel.Bones)
                {
                    if (!string.IsNullOrEmpty(bone.Name))
                        gr2Names.Add(bone.Name);
                }

                // Also add track names from animations in the same GrannyFile
                if (skelNode.Parent is GrannyFileNode gfn && gfn.FileData != null)
                {
                    foreach (var anim in gfn.FileData.Animations)
                    {
                        foreach (var tg in anim.TrackGroups)
                        {
                            foreach (var tt in tg.TransformTracks)
                            {
                                if (!string.IsNullOrEmpty(tt.Name))
                                    gr2Names.Add(tt.Name);
                            }
                        }
                    }
                }

                int matched = 0;
                foreach (var name in gr2Names)
                {
                    if (modelBinBoneNames.Contains(name))
                        matched++;
                }

                if (matched > 0 && (best == null || matched > best.Value.MatchCount))
                {
                    best = (skel, skelNode, matched, skel.Bones.Count);
                }
            }

            return best;
        }

        // Finds the first skeleton from any GrannyFile that can be used for animation playback.
        // Prefers skeletons from GR2 files over GSF files.
        public (GrannySkeleton Skeleton, SkeletonNode Node, GrannyFileNode File)? FindFirstSkeleton()
        {
            // Prefer non-GSF skeletons first
            foreach (var gfn in GetAllGrannyFileNodes().OrderBy(g => g.IsGsf ? 1 : 0))
            {
                if (gfn.FileData?.Skeletons.Count > 0)
                {
                    var skelNode = gfn.Children.OfType<SkeletonNode>().FirstOrDefault();
                    if (skelNode != null)
                        return (gfn.FileData.Skeletons[0], skelNode, gfn);
                }
            }
            return null;
        }

        // Collects GSF source file reference filenames for auto-loading.
        // Returns the filenames (not full paths) referenced by all loaded GSF files.
        public List<string> GetGsfReferencedFilenames()
        {
            var result = new List<string>();
            foreach (var gfn in GetAllGrannyFileNodes())
            {
                if (gfn.FileData?.CharacterInfo != null)
                {
                    foreach (var set in gfn.FileData.CharacterInfo.AnimationSets)
                    {
                        foreach (var sfr in set.SourceFileReferences)
                        {
                            if (!string.IsNullOrEmpty(sfr.SourceFilename))
                                result.Add(sfr.SourceFilename);
                        }
                    }
                }
            }
            return result;
        }

        // Computes detailed bone matching info between a ModelBin and a GR2 skeleton,
        // including which meshes are linked to each bone via bone index.
        public List<BoneMatchEntry> ComputeBoneMatchEntries(ModelBinNode modelBin, GrannySkeleton gr2Skeleton)
        {
            var entries = new List<BoneMatchEntry>();
            if (modelBin == null) return entries;

            // Collect all unique bone names from both sources
            var allBoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ModelBin skeleton blob bones
            var mbSkelBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SkeletonBlob skelBlob = null;
            if (modelBin.Bundle != null)
            {
                skelBlob = modelBin.Bundle.Blobs.OfType<SkeletonBlob>().FirstOrDefault();
                if (skelBlob != null)
                {
                    foreach (var bone in skelBlob.Bones)
                    {
                        if (!string.IsNullOrEmpty(bone.Name))
                        {
                            mbSkelBones.Add(bone.Name);
                            allBoneNames.Add(bone.Name);
                        }
                    }
                }
            }

            // GR2 skeleton bones AND animation track names (both are valid GR2 bone sources)
            var gr2BoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (gr2Skeleton != null)
            {
                foreach (var bone in gr2Skeleton.Bones)
                {
                    if (!string.IsNullOrEmpty(bone.Name))
                    {
                        gr2BoneNames.Add(bone.Name);
                        allBoneNames.Add(bone.Name);
                    }
                }
            }

            // Build bone name -> mesh linkage map from MeshBlob.RigidBoneIndex
            var boneToMeshes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (skelBlob != null)
            {
                foreach (var mesh in modelBin.Children.OfType<MeshNode>())
                {
                    short boneIdx = mesh.GeometryData?.BoneIndex ?? -1;
                    if (boneIdx >= 0 && boneIdx < skelBlob.Bones.Count)
                    {
                        string boneName = skelBlob.Bones[boneIdx].Name;
                        if (!string.IsNullOrEmpty(boneName))
                        {
                            if (!boneToMeshes.ContainsKey(boneName))
                                boneToMeshes[boneName] = new List<string>();
                            boneToMeshes[boneName].Add(mesh.Name ?? mesh.GeometryData?.Name ?? $"Mesh#{boneIdx}");
                        }
                    }
                }
            }

            int idx = 0;
            foreach (var boneName in allBoneNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                boneToMeshes.TryGetValue(boneName, out var meshNames);
                entries.Add(new BoneMatchEntry
                {
                    Index = idx++,
                    BoneName = boneName,
                    InModelBinSkeleton = mbSkelBones.Contains(boneName),
                    InGr2Skeleton = gr2BoneNames.Contains(boneName),
                    LinkedMeshCount = meshNames?.Count ?? 0,
                    LinkedMeshNames = meshNames ?? new List<string>(),
                });
            }

            return entries;
        }

        // Computes bone match entries that also include animation track presence info.
        // Track names are also checked as a valid GR2 bone name source.
        public List<BoneMatchEntry> ComputeBoneMatchEntriesWithAnim(
            ModelBinNode modelBin, GrannySkeleton gr2Skeleton, GrannyAnimation animation)
        {
            var entries = ComputeBoneMatchEntries(modelBin, gr2Skeleton);

            if (animation != null)
            {
                var trackNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tg in animation.TrackGroups)
                {
                    foreach (var tt in tg.TransformTracks)
                    {
                        if (!string.IsNullOrEmpty(tt.Name))
                            trackNames.Add(tt.Name);
                    }
                }

                foreach (var entry in entries)
                {
                    entry.InAnimTrack = trackNames.Contains(entry.BoneName);
                    // If the bone is found in animation tracks, it should also be considered 
                    // as present in the GR2 data (tracks are GR2 bone references)
                    if (entry.InAnimTrack && !entry.InGr2Skeleton)
                        entry.InGr2Skeleton = true;
                }

                // Add any track-only bones not already in the list
                int idx = entries.Count;
                foreach (var trackName in trackNames)
                {
                    if (!entries.Any(e => string.Equals(e.BoneName, trackName, StringComparison.OrdinalIgnoreCase)))
                    {
                        entries.Add(new BoneMatchEntry
                        {
                            Index = idx++,
                            BoneName = trackName,
                            InModelBinSkeleton = false,
                            InGr2Skeleton = true, // Track names ARE GR2 bone references
                            InAnimTrack = true,
                            LinkedMeshCount = 0,
                        });
                    }
                }
            }

            return entries;
        }

        private static void CollectNodes<T>(IViewerNode node, List<T> results) where T : class, IViewerNode
        {
            if (node is T typed)
                results.Add(typed);
            foreach (var child in node.Children)
                CollectNodes(child, results);
        }
    }
}
