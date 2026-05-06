using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using ForzaTools.CarScene;
using ForzaTechStudio.Services;

namespace ForzaTechStudio.ViewModels
{
    public partial class FileViewModel : ObservableObject
    {
        private readonly string _filePath;
        private readonly FileService _fileService;
        private bool _isLoaded;
        private bool _isLoading;

        [ObservableProperty]
        private string _fileName;
        
        [ObservableProperty]
        private bool _keepRendered;

        [ObservableProperty]
        private string _fileType;

        [ObservableProperty]
        private ObjectNode _selectedNode;

        private object _parsedObject;
        public object ParsedObject
        {
            get => _parsedObject;
            private set => SetProperty(ref _parsedObject, value);
        }

        public string FilePath => _filePath;

        public ObservableCollection<ObjectNode> Nodes { get; } = new();

        public FileViewModel(string fileName, string filePath, FileService fileService)
        {
            _fileName = fileName;
            _filePath = filePath;
            _fileService = fileService;

            _fileType = "Not Loaded";
        }

        public async Task EnsureLoadedAsync()
        {
            if (_isLoaded || _isLoading) return;

            try
            {
                _isLoading = true;
                FileType = "Loading...";

                // Load file directly since FileService no longer has LoadFileAsync
                var obj = await Task.Run<object>(() =>
                {
                    try
                    {
                        using var stream = File.OpenRead(_filePath);

                        if (_filePath.EndsWith(".modelbin", StringComparison.OrdinalIgnoreCase))
                        {
                            var bundle = new Bundle();
                            bundle.Load(stream);
                            return bundle;
                        }
                        else if (_filePath.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                        {
                            var carbin = new CarbinFile();
                            carbin.Load(stream);
                            return carbin.Scene;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse {_filePath}: {ex.Message}");
                    }
                    return null;
                });

                if (obj != null)
                {
                    ParsedObject = obj;
                    FileType = obj.GetType().Name;
                    BuildTree(obj);
                    _isLoaded = true;
                }
                else
                {
                    FileType = "Load Failed";
                    App.ShowErrorDialog($"Failed to parse file: {FileName}\n\nUnknown error (returned null).");
                }
            }
            catch (Exception ex)
            {
                FileType = $"Error: {ex.Message}";
                App.ShowErrorDialog($"Failed to parse file: {FileName}\n\nError: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        public void Unload()
        {
            if (!_isLoaded) return;

            ParsedObject = null;
            Nodes.Clear();
            SelectedNode = null;

            FileType = "Not Loaded";
            _isLoaded = false;
            _isLoading = false;

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        [RelayCommand]
        public async Task SaveFileAsync()
        {
            if (ParsedObject is Bundle bundle)
            {
                var picker = new FileSavePicker();
                var window = App.MainWindow;
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

                picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                picker.FileTypeChoices.Add("Forza ModelBin", new List<string>() { ".modelbin" });
                picker.SuggestedFileName = FileName;

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    try
                    {
                        using var stream = await file.OpenStreamForWriteAsync();
                        stream.SetLength(0);
                        bundle.Serialize(stream);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Save Error: {ex.Message}");
                    }
                }
            }
        }

        private void BuildTree(object obj)
        {
            Nodes.Clear();

            if (obj is Bundle bundle)
            {
                var root = new ObjectNode("Bundle Root", bundle);
                Nodes.Add(root);
                root.Children.Clear(); // Remove DummyNode added by constructor; we populate synchronously

                var headerNode = new ObjectNode("header", bundle, p => 
                    p.Name == nameof(Bundle.VersionMajor) || 
                    p.Name == nameof(Bundle.VersionMinor) || 
                    p.Name == "HeaderSize" || 
                    p.Name == "TotalSize" ||
                    p.Name == "BlobCount");
                root.Children.Add(headerNode);

                int index = 0;
                foreach (var blob in bundle.Blobs)
                {
                    string blobTitle = $"[{index}] {blob.GetType().Name.Replace("Blob", "")}";
                    var nameMeta = blob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
                    if (nameMeta != null && !string.IsNullOrWhiteSpace(nameMeta.Name))
                    {
                        blobTitle += $" ({nameMeta.Name})";
                    }
                    else if (blob is GenericBlob generic)
                    {
                        blobTitle += $" (Tag: {blob.Tag:X})";
                    }

                    // For MeshBlob, exclude fields already shown in dedicated card sections of the template
                    // to prevent duplicate rows in the "OTHER PROPERTIES" ItemsControl.
                    var meshExclusions = blob is MeshBlob ? new System.Collections.Generic.HashSet<string>
                    {
                        nameof(MeshBlob.MaterialId), nameof(MeshBlob.MaterialIds),
                        nameof(MeshBlob.RigidBoneIndex),
                        nameof(MeshBlob.LODFlags), nameof(MeshBlob.LODLevel1), nameof(MeshBlob.LODLevel2),
                        nameof(MeshBlob.LOD_LODS), nameof(MeshBlob.LOD_LOD0), nameof(MeshBlob.LOD_LOD1),
                        nameof(MeshBlob.LOD_LOD2), nameof(MeshBlob.LOD_LOD3), nameof(MeshBlob.LOD_LOD4), nameof(MeshBlob.LOD_LOD5),
                        nameof(MeshBlob.IsOpaque), nameof(MeshBlob.IsDecal), nameof(MeshBlob.IsTransparent),
                        nameof(MeshBlob.IsShadow), nameof(MeshBlob.IsNotShadow), nameof(MeshBlob.IsAlphaToCoverage),
                        nameof(MeshBlob.BucketOrder),
                        nameof(MeshBlob.PositionScale), nameof(MeshBlob.PositionTranslate),
                        nameof(MeshBlob.TexCoordTransforms),
                        nameof(MeshBlob.NameSuffix),
                        // Collections that render poorly in the generic property list
                        nameof(MeshBlob.VertexBuffers),
                        nameof(MeshBlob.ConstantBufferIndices),
                    } : null;

                    // ModelBlob: exclude properties already shown in the dedicated card sections
                    var modelExclusions = blob is ModelBlob ? new System.Collections.Generic.HashSet<string>
                    {
                        nameof(ModelBlob.MeshCount), nameof(ModelBlob.BuffersCount),
                        nameof(ModelBlob.VertexLayoutCount), nameof(ModelBlob.MaterialCount),
                        nameof(ModelBlob.HasLOD), nameof(ModelBlob.MinLOD), nameof(ModelBlob.MaxLOD),
                        nameof(ModelBlob.LODFlags),
                        nameof(ModelBlob.BoundingBoxMin), nameof(ModelBlob.BoundingBoxMax),
                    } : null;

                    // Collapse: blob node IS the data node (no header/metadata/data sub-nodes).
                    // SkeletonBlob still exposes its Bones list as lazy-loaded children.
                    // For VertexLayoutBlob, exclude all collections (handled by VLayBlobTemplate).
                    Func<PropertyInfo, bool> dataFilter = p =>
                        p.Name != nameof(BundleBlob.Tag) &&
                        p.Name != nameof(BundleBlob.VersionMajor) &&
                        p.Name != nameof(BundleBlob.VersionMinor) &&
                        p.Name != nameof(BundleBlob.CompressedSize) &&
                        p.Name != nameof(BundleBlob.UncompressedSize) &&
                        p.Name != nameof(BundleBlob.FileOffset) &&
                        p.Name != nameof(BundleBlob.Id) &&
                        p.Name != nameof(BundleBlob.Metadatas) &&
                        p.Name != nameof(BundleBlob.Data) &&
                        p.Name != "MetadataCount" &&
                        (meshExclusions  == null || !meshExclusions.Contains(p.Name)) &&
                        (modelExclusions == null || !modelExclusions.Contains(p.Name));

                    var blobNode = new ObjectNode(blobTitle, blob, dataFilter);
                    blobNode.OwnerBundle = bundle;
                    root.Children.Add(blobNode);

                    // For blobs that have a rich template (non-skeleton), suppress auto-generated
                    // collection children — their templates show everything inline.
                    if (blob is not SkeletonBlob)
                        blobNode.MarkAsLoaded();

                    index++;
                }
                root.MarkAsLoaded(); // Lock root: EnsureChildrenLoaded must not overwrite OwnerBundle-bearing nodes
            }
            else if (obj is Scene scene)
            {
                var root = new ObjectNode("Scene Root", scene);
                Nodes.Add(root);
                root.PopulateChildrenFromProperties(scene);
            }
        }

        private string GetBlobDataName(BundleBlob blob)
        {
            return blob switch
            {
                SkeletonBlob => "skeleton_blob",
                MorphBlob => "morph_blob",
                MaterialBlob => "material_instance_bundle",
                MaterialResourceBlob => "material_instance_blob",
                MatLBlob => "material_blob",
                MaterialShaderParameterBlob => "shader_parameter_blob",
                MeshBlob => "mesh_blob",
                VertexLayoutBlob => "vertex_layout_blob",
                IndexBufferBlob => "buffer_blob",
                VertexBufferBlob => "buffer_blob",
                MorphBufferBlob => "buffer_blob",
                SkinBufferBlob => "buffer_blob",
                ModelBlob => "model_blob",
                LightScenarioBlob => "light_scenario_blob",
                ShaderParameterMappingBlob => "shader_parameters",
                TextureContentBlob => "data",
                ManufacturerColorsBlob => "manufacturer_colors",
                RenderTargetBlob => "render_target_blob",
                STexBlob => "texture_bundle",
                ParticleBlob => "particle_blob",
                _ => "blob_data"
            };
        }
    }

    public partial class ObjectNode : ObservableObject
    {
        public string Title { get; }
        public object Data { get; }
        public Bundle? OwnerBundle { get; set; }
        public ObservableCollection<ObjectNode> Children { get; } = new();
        public ObservableCollection<PropertyItem> Properties { get; } = new();

        private SimpleSkeletonViewModel _skeletonBlobWrapper;
        // Non-null when Data is a SkeletonBlob; used by the skeleton detail template.
        public SimpleSkeletonViewModel SkeletonBlobWrapper
        {
            get
            {
                if (_skeletonBlobWrapper == null && Data is SkeletonBlob skel)
                    _skeletonBlobWrapper = new SimpleSkeletonViewModel(skel);
                return _skeletonBlobWrapper;
            }
        }

        private SimpleMeshViewModel _meshWrapper;
        // Non-null when Data is a MeshBlob; used by the mesh detail template for editable transforms.
        public SimpleMeshViewModel MeshWrapper
        {
            get
            {
                if (_meshWrapper == null && Data is MeshBlob mesh)
                    _meshWrapper = new SimpleMeshViewModel(mesh);
                return _meshWrapper;
            }
        }

        private BoneViewModel _boneWrapper;
        // Non-null when Data is a Bone; used by the bone node detail template.
        public BoneViewModel BoneWrapper
        {
            get
            {
                if (_boneWrapper == null && Data is Bone bone)
                    _boneWrapper = new BoneViewModel(bone);
                return _boneWrapper;
            }
        }

        private SimpleMaterialViewModel _materialBlobWrapper;
        // Non-null when Data is a MaterialBlob; used by the material blob detail template.
        public SimpleMaterialViewModel MaterialBlobWrapper
        {
            get
            {
                if (_materialBlobWrapper == null && Data is MaterialBlob mat)
                    _materialBlobWrapper = new SimpleMaterialViewModel(mat, null);
                return _materialBlobWrapper;
            }
        }

        private VLayBlobViewModel _vLayBlobWrapper;
        // Non-null when Data is a VertexLayoutBlob; used by the VLay detail template.
        public VLayBlobViewModel VLayBlobWrapper
        {
            get
            {
                if (_vLayBlobWrapper == null && Data is VertexLayoutBlob vlay)
                    _vLayBlobWrapper = new VLayBlobViewModel(vlay, OwnerBundle, Title);
                return _vLayBlobWrapper;
            }
        }

        private readonly Func<PropertyInfo, bool> _propertyFilter;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value)
                {
                    App.MainWindow.DispatcherQueue.TryEnqueue(() => EnsureChildrenLoaded());
                }
            }
        }

        private bool _childrenLoaded = false;
        private static readonly ObjectNode DummyNode = new ObjectNode("Loading...", null, isDummy: true);

        public ObjectNode(string title, object data, bool isDummy = false)
        {
            Title = title;
            Data = data;
            if (isDummy)
            {
                _childrenLoaded = true;
            }
            else
            {
                GenerateProperties();
                CheckAndAddDummyChild(data);
            }
        }

        public ObjectNode(string title, object data, Func<PropertyInfo, bool> propertyFilter)
        {
            Title = title;
            Data = data;
            _propertyFilter = propertyFilter;
            GenerateProperties();
            CheckAndAddDummyChild(data);
        }

        private void CheckAndAddDummyChild(object obj)
        {
            if (obj == null) return;

            var props = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            bool hasPotentialChildren = false;

            foreach (var p in props)
            {
                if (_propertyFilter != null && !_propertyFilter(p)) continue;

                if (typeof(Bundle).IsAssignableFrom(p.PropertyType))
                {
                    hasPotentialChildren = true;
                    break;
                }

                if (p.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(p.PropertyType))
                {
                    var val = p.GetValue(obj) as IEnumerable;
                    if (val != null)
                    {
                        var enumerator = val.GetEnumerator();
                        if (enumerator.MoveNext()) 
                        {
                            hasPotentialChildren = true;
                            break;
                        }
                    }
                }
            }

            if (hasPotentialChildren)
            {
                Children.Add(DummyNode);
            }
            else
            {
                _childrenLoaded = true;
            }
        }

        private void EnsureChildrenLoaded()
        {
            if (_childrenLoaded) return;

            Children.Clear();
            PopulateChildrenFromProperties(Data);
            _childrenLoaded = true;
        }

        public void MarkAsLoaded()
        {
            _childrenLoaded = true;
        }

        private void GenerateProperties()
        {
            if (Data == null) return;

            var props = Data.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var p in props)
            {
                if (_propertyFilter != null && !_propertyFilter(p)) continue;

                // Skip collections (non-string IEnumerable) — those become child nodes
                if (p.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(p.PropertyType)) continue;

                // Skip complex struct types that get their own dedicated template sections
                if (p.PropertyType == typeof(BufferHeader)) continue;
                if (p.PropertyType == typeof(Matrix4x4)) continue;

                // Skip nullable Vector3? — written explicitly in ModelBlob template via special nodes
                if (p.PropertyType == typeof(Vector3?)) continue;

                try
                {
                    Properties.Add(new PropertyItem(p.Name, p, Data));
                }
                catch { }
            }
        }

        public void PopulateChildrenFromProperties(object obj)
        {
            if (obj == null) return;
            
            var objType = obj.GetType();
            if (objType == typeof(BufferHeader) || 
                objType == typeof(IndexBufferBlob) || 
                objType == typeof(VertexBufferBlob) ||
                objType == typeof(MorphBufferBlob) ||
                objType == typeof(SkinBufferBlob))
            {
                return;
            }

            var props = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var p in props)
            {
                if (_propertyFilter != null && !_propertyFilter(p)) continue;

                if (p.Name == "Data" || p.Name == "Indices" || p.Name == "Vertices")
                    continue;

                if (typeof(Bundle).IsAssignableFrom(p.PropertyType))
                {
                    var val = p.GetValue(obj) as Bundle;
                    if (val != null && val.Blobs.Count > 0)
                    {
                        var headerNode = new ObjectNode("header", val, prop => 
                            prop.Name == nameof(Bundle.VersionMajor) || 
                            prop.Name == nameof(Bundle.VersionMinor) || 
                            prop.Name == "HeaderSize" || 
                            prop.Name == "TotalSize" ||
                            prop.Name == "BlobCount");

                        Children.Add(headerNode);

                        int i = 0;
                        foreach (var blob in val.Blobs)
                        {
                            string blobTitle = $"[{i}] {blob.GetType().Name.Replace("Blob", "")}";
                            var nameMeta = blob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
                            if (nameMeta != null && !string.IsNullOrWhiteSpace(nameMeta.Name))
                            {
                                blobTitle += $" ({nameMeta.Name})";
                            }
                            
                            var blobNode = new ObjectNode(blobTitle, null);
                            Children.Add(blobNode);

                            var blobHeader = new ObjectNode("header", blob, prop => 
                                prop.Name == nameof(BundleBlob.Tag) ||
                                prop.Name == nameof(BundleBlob.VersionMajor) ||
                                prop.Name == nameof(BundleBlob.VersionMinor) ||
                                prop.Name == nameof(BundleBlob.CompressedSize) ||
                                prop.Name == nameof(BundleBlob.UncompressedSize) ||
                                prop.Name == nameof(BundleBlob.FileOffset) ||
                                prop.Name == nameof(BundleBlob.Id));
                            blobNode.Children.Add(blobHeader);

                            if (blob.Metadatas.Count > 0)
                            {
                                var metadataNode = new ObjectNode("metadata", null);
                                blobNode.Children.Add(metadataNode);

                                int metaIndex = 0;
                                foreach (var meta in blob.Metadatas)
                                {
                                    var metaName = $"[{metaIndex}] {meta.GetType().Name.Replace("Metadata", "")}";
                                    metadataNode.Children.Add(new ObjectNode(metaName, meta));
                                    metaIndex++;
                                }
                            }

                            var dataNode = new ObjectNode("blob_data", blob, prop => 
                                prop.Name != nameof(BundleBlob.Tag) &&
                                prop.Name != nameof(BundleBlob.VersionMajor) &&
                                prop.Name != nameof(BundleBlob.VersionMinor) &&
                                prop.Name != nameof(BundleBlob.CompressedSize) &&
                                prop.Name != nameof(BundleBlob.UncompressedSize) &&
                                prop.Name != nameof(BundleBlob.FileOffset) &&
                                prop.Name != nameof(BundleBlob.Id) &&
                                prop.Name != nameof(BundleBlob.Metadatas) &&
                                prop.Name != nameof(BundleBlob.Data));
                            blobNode.Children.Add(dataNode);

                            i++;
                        }
                    }
                    continue;
                }

                if (p.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(p.PropertyType))
                {
                    var val = p.GetValue(obj) as IEnumerable;
                    if (val != null)
                    {
                        if (val is byte[] || val is int[] || val is float[])
                            continue;

                        var collectionNode = new ObjectNode(p.Name, val);
                        Children.Add(collectionNode);
                        
                        collectionNode.Children.Clear();
                        
                        int i = 0;
                        int maxItems = 1000;
                        foreach (var item in val)
                        {
                            if (i >= maxItems)
                            {
                                collectionNode.Children.Add(new ObjectNode($"... (truncated {i} items)", null));
                                break;
                            }

                            string itemTitle = $"[{i}] {item?.GetType().Name ?? "Null"}";
                            if (item is Bone bone && !string.IsNullOrWhiteSpace(bone.Name))
                            {
                                itemTitle = $"Bone: {bone.Name}";
                            }
                            
                            if (item is BundleBlob blob)
                            {
                                var nameMeta = blob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
                                if (nameMeta != null && !string.IsNullOrWhiteSpace(nameMeta.Name))
                                {
                                    itemTitle += $" ({nameMeta.Name})";
                                }
                            }
                            
                            var itemNode = new ObjectNode(itemTitle, item);
                            collectionNode.Children.Add(itemNode);
                            i++;
                        }
                        
                        collectionNode.MarkAsLoaded();
                    }
                }
            }
        }

        public void Refresh()
        {
            if (Data == null) return;

            Children.Clear();
            PopulateChildrenFromProperties(Data);

            Properties.Clear();
            GenerateProperties();
        }
    }

    // Discriminates data kind for typed display in the tree detail panel.
    public enum PropertyDataType
    {
        Bool,
        Float,
        Int,
        UInt,
        String,
        Tag4CC,    // uint read as FourCC (e.g. "Mesh" / 0x4D657368)
        Enum,
        Vector4,
        Vector3,
        Vector2,
        HexUInt,   // uint displayed as 0xXXXXXXXX (read-only)
        ReadOnly,  // everything else — selectable text
    }

    public partial class PropertyItem : ObservableObject
    {
        private readonly PropertyInfo _propInfo;
        private readonly object _target;

        // Original (raw) property name, used in bindings that need the exact name.
        public string Name { get; }

        // Human-readable label: "RigidBoneIndex" → "Rigid Bone Index".
        public string DisplayName { get; }

        public PropertyDataType DataType { get; }

        // Typed accessors

        public bool BoolValue
        {
            get => _propInfo.GetValue(_target) is bool b && b;
            set { if (_propInfo.CanWrite) { _propInfo.SetValue(_target, value); OnPropertyChanged(); } }
        }

        public double FloatValue
        {
            get
            {
                var v = _propInfo.GetValue(_target);
                return v switch { float f => f, double d => d, _ => 0.0 };
            }
            set
            {
                if (!_propInfo.CanWrite) return;
                var t = _propInfo.PropertyType;
                if (t == typeof(float)) _propInfo.SetValue(_target, (float)value);
                else if (t == typeof(double)) _propInfo.SetValue(_target, value);
                OnPropertyChanged();
            }
        }

        public long IntValue
        {
            get
            {
                var v = _propInfo.GetValue(_target);
                return v switch
                {
                    int i => i, uint u => u, short s => s, ushort us => us,
                    byte b => b, sbyte sb => sb, long l => l, ulong ul => (long)ul, _ => 0L,
                };
            }
            set
            {
                if (!_propInfo.CanWrite) return;
                var t = _propInfo.PropertyType;
                try
                {
                    object converted = t switch
                    {
                        _ when t == typeof(int)    => (int)value,
                        _ when t == typeof(uint)   => (uint)value,
                        _ when t == typeof(short)  => (short)value,
                        _ when t == typeof(ushort) => (ushort)value,
                        _ when t == typeof(byte)   => (byte)value,
                        _ when t == typeof(sbyte)  => (sbyte)value,
                        _ when t == typeof(long)   => value,
                        _ => value
                    };
                    _propInfo.SetValue(_target, converted);
                }
                catch { }
                OnPropertyChanged();
            }
        }

        public System.Collections.IList EnumItems => DataType == PropertyDataType.Enum
            ? (System.Collections.IList)Enum.GetValues(_propInfo.PropertyType)
            : null;

        public object EnumSelectedValue
        {
            get => _propInfo.GetValue(_target);
            set { if (_propInfo.CanWrite && value != null) { try { _propInfo.SetValue(_target, value); } catch { } OnPropertyChanged(); } }
        }

        // Vector components (back a Vector4, Vector3, or Vector2)
        public double VecX { get => GetVecComponent(0); set => SetVecComponent(0, value); }
        public double VecY { get => GetVecComponent(1); set => SetVecComponent(1, value); }
        public double VecZ { get => GetVecComponent(2); set => SetVecComponent(2, value); }
        public double VecW { get => GetVecComponent(3); set => SetVecComponent(3, value); }

        private double GetVecComponent(int idx)
        {
            var v = _propInfo.GetValue(_target);
            return v switch
            {
                Vector4 v4 => idx switch { 0 => v4.X, 1 => v4.Y, 2 => v4.Z, 3 => v4.W, _ => 0 },
                Vector3 v3 => idx switch { 0 => v3.X, 1 => v3.Y, 2 => v3.Z, _ => 0 },
                Vector2 v2 => idx switch { 0 => v2.X, 1 => v2.Y, _ => 0 },
                _ => 0
            };
        }

        private void SetVecComponent(int idx, double val)
        {
            if (!_propInfo.CanWrite) return;
            var v = _propInfo.GetValue(_target);
            if (v is Vector4 v4)
            {
                var n = idx switch { 0 => new Vector4((float)val, v4.Y, v4.Z, v4.W), 1 => new Vector4(v4.X, (float)val, v4.Z, v4.W), 2 => new Vector4(v4.X, v4.Y, (float)val, v4.W), 3 => new Vector4(v4.X, v4.Y, v4.Z, (float)val), _ => v4 };
                _propInfo.SetValue(_target, n);
            }
            else if (v is Vector3 v3)
            {
                var n = idx switch { 0 => new Vector3((float)val, v3.Y, v3.Z), 1 => new Vector3(v3.X, (float)val, v3.Z), 2 => new Vector3(v3.X, v3.Y, (float)val), _ => v3 };
                _propInfo.SetValue(_target, n);
            }
            else if (v is Vector2 v2)
            {
                var n = idx switch { 0 => new Vector2((float)val, v2.Y), 1 => new Vector2(v2.X, (float)val), _ => v2 };
                _propInfo.SetValue(_target, n);
            }
            OnPropertyChanged(nameof(VecX)); OnPropertyChanged(nameof(VecY));
            OnPropertyChanged(nameof(VecZ)); OnPropertyChanged(nameof(VecW));
        }

        // FourCC string for Tag4CC properties, e.g. "Mesh".
        public string TagString
        {
            get
            {
                if (DataType != PropertyDataType.Tag4CC) return "";
                try
                {
                    uint raw = Convert.ToUInt32(_propInfo.GetValue(_target));
                    return new string(new[]
                    {
                        (char)((raw >> 24) & 0xFF),
                        (char)((raw >> 16) & 0xFF),
                        (char)((raw >>  8) & 0xFF),
                        (char)( raw        & 0xFF),
                    });
                }
                catch { return ""; }
            }
        }

        // Legacy string accessor (fallback / string type)

        public string ValueAsString
        {
            get
            {
                object currentValue = null;
                try { currentValue = _propInfo.GetValue(_target); } catch { }
                if (currentValue is Vector4 v4) return $"{v4.X}, {v4.Y}, {v4.Z}, {v4.W}";
                if (currentValue is Vector3 v3) return $"{v3.X}, {v3.Y}, {v3.Z}";
                if (currentValue is Vector2 v2) return $"{v2.X}, {v2.Y}";
                return currentValue?.ToString() ?? "";
            }
            set
            {
                if (!_propInfo.CanWrite || _target == null) return;
                try
                {
                    var targetType = _propInfo.PropertyType;
                    object converted = null;
                    if (targetType == typeof(Vector4))
                    {
                        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => float.Parse(s.Trim())).ToArray();
                        if (parts.Length >= 4) converted = new Vector4(parts[0], parts[1], parts[2], parts[3]);
                        else if (parts.Length == 3) converted = new Vector4(parts[0], parts[1], parts[2], 1.0f);
                        else return;
                    }
                    else { converted = Convert.ChangeType(value, targetType); }
                    _propInfo.SetValue(_target, converted);
                    OnPropertyChanged(nameof(ValueAsString));
                }
                catch { }
            }
        }

        public bool IsReadOnly => !_propInfo.CanWrite;

        public PropertyItem(string name, PropertyInfo propInfo, object target)
        {
            Name = name;
            _propInfo = propInfo;
            _target = target;
            DisplayName = MakePretty(name);
            DataType = InferDataType(propInfo, target);
        }

        private static PropertyDataType InferDataType(PropertyInfo p, object target)
        {
            // Explicit: "Tag" uint is always treated as FourCC
            if (p.Name == "Tag" && (p.PropertyType == typeof(uint) || p.PropertyType == typeof(int)))
                return PropertyDataType.Tag4CC;

            var t = p.PropertyType;
            if (t == typeof(bool)) return PropertyDataType.Bool;
            if (t == typeof(float) || t == typeof(double)) return PropertyDataType.Float;
            if (t == typeof(int) || t == typeof(short) || t == typeof(sbyte)) return PropertyDataType.Int;
            if (t == typeof(ushort) || t == typeof(byte)) return PropertyDataType.UInt;
            if (t == typeof(uint) && p.Name.Contains("Offset", StringComparison.OrdinalIgnoreCase))
                return PropertyDataType.HexUInt;
            if (t == typeof(uint) || t == typeof(ulong)) return PropertyDataType.UInt;
            if (t == typeof(string)) return PropertyDataType.String;
            if (t.IsEnum) return PropertyDataType.Enum;
            if (t == typeof(Vector4)) return PropertyDataType.Vector4;
            if (t == typeof(Vector3)) return PropertyDataType.Vector3;
            if (t == typeof(Vector2)) return PropertyDataType.Vector2;
            if (!p.CanWrite) return PropertyDataType.ReadOnly;
            return PropertyDataType.ReadOnly;
        }

        private static string MakePretty(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                    sb.Append(' ');
                else if (i > 0 && char.IsUpper(name[i]) && i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))
                    sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(ValueAsString));
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(FloatValue));
            OnPropertyChanged(nameof(IntValue));
            OnPropertyChanged(nameof(EnumSelectedValue));
            OnPropertyChanged(nameof(VecX)); OnPropertyChanged(nameof(VecY));
            OnPropertyChanged(nameof(VecZ)); OnPropertyChanged(nameof(VecW));
            OnPropertyChanged(nameof(TagString));
        }
    }
}
