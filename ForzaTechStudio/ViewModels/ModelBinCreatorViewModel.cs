using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using Windows.Storage.Pickers;
using ForzaTechStudio.Services;
using ForzaTechStudio.Views;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ForzaTools.Shared;
using System.Text.Json;

namespace ForzaTechStudio.ViewModels
{
    // NEW: Material cache entry that stores file path AND eagerly loads MaterialBlob when accessed
    public class CachedMaterialBlob
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public string ParentFolder { get; set; }
        public string RelativeZipPath { get; set; }
        
        // In-memory MaterialBlob created on-demand (NOT loaded from file)
        private MaterialBlob _blob;
        
        public MaterialBlob Blob
        {
            get
            {
                if (_blob == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[CachedMaterial] Creating in-memory MaterialBlob for: {Name}");
                    
                    // Create a fresh MaterialBlob with nested bundle structure
                    _blob = CreateMaterialBlobInMemory(Name, RelativeZipPath);
                    
                    System.Diagnostics.Debug.WriteLine($"[CachedMaterial] ? Created MaterialBlob with nested bundle");
                    if (_blob.Bundle != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CachedMaterial]   Nested bundle has {_blob.Bundle.Blobs.Count} blobs");
                        
                        var paramBlob = _blob.Bundle.Blobs.OfType<MaterialShaderParameterBlob>().FirstOrDefault();
                        if (paramBlob != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CachedMaterial]   ? MaterialShaderParameterBlob with {paramBlob.Parameters.Count} parameters");
                        }
                    }
                }
                return _blob;
            }
            set => _blob = value;
        }
        
        // Creates a MaterialBlob in memory with proper nested bundle structure
        private MaterialBlob CreateMaterialBlobInMemory(string materialName, string relativePath)
        {
            var materialBlob = new MaterialBlob
            {
                Tag = Bundle.TAG_BLOB_MaterialInstance,
                VersionMajor = 1,
                VersionMinor = 0
            };
            
            // Create nested bundle structure (version 1.1)
            var nestedBundle = new Bundle
            {
                VersionMajor = 1,
                VersionMinor = 1
            };

            // Create MATI blob with material path
            var matiBlob = new MaterialResourceBlob
            {
                Tag = Bundle.TAG_BLOB_MaterialResource, // 0x4D415449 'MATI'
                VersionMajor = 1,
                VersionMinor = 0
            };
            
            // Construct the material path using the relative path if available
            string materialPath;
            if (!string.IsNullOrEmpty(relativePath))
            {
                // Remove .materialbin extension if present
                string cleanPath = relativePath.EndsWith(".materialbin") 
                    ? relativePath.Substring(0, relativePath.Length - 12) 
                    : relativePath;
                materialPath = $"Game:\\Media\\cars\\_library\\materials\\{cleanPath}.materialbin";
            }
            else
            {
                // Fallback to simple path
                materialPath = $"Game:\\Media\\cars\\_library\\materials\\{materialName ?? "error"}.materialbin";
            }
            
            matiBlob.Path = materialPath;
            
            // Add Name metadata to MATI blob
            matiBlob.Metadatas.Add(new NameMetadata 
            { 
                Tag = BundleMetadata.TAG_METADATA_Name, 
                Name = materialName ?? "Default" 
            });
            
            // Add Atlas metadata (version 2, both bools false)
            matiBlob.Metadatas.Add(new AtlasMetadata
            {
                Tag = BundleMetadata.TAG_METADATA_Atlas,
                Version = 2,
                Unk = false,
                UnkV2 = false
            });
            
            nestedBundle.Blobs.Add(matiBlob);
            
            // Create MaterialShaderParameterBlob (MTPR) with version 2.0 and 0 parameters initially
            var shaderParamBlob = new MaterialShaderParameterBlob
            {
                Tag = Bundle.TAG_BLOB_MaterialShaderParameter, // 0x4D545052 'MTPR'
                VersionMajor = 2,
                VersionMinor = 0,
                // Initialize with empty parameters collection
                // Unk1, Unk2, Unk3 default to 0
            };
            
            nestedBundle.Blobs.Add(shaderParamBlob);
            
            // Assign the nested bundle to the MaterialBlob
            materialBlob.Bundle = nestedBundle;
            
            // Add Name metadata to the outer MaterialBlob
            materialBlob.Metadatas.Add(new NameMetadata 
            { 
                Tag = BundleMetadata.TAG_METADATA_Name, 
                Name = materialName ?? "Default" 
            });
            
            return materialBlob;
        }
        
        // Ensures the blob is created (no-op since we don't load from files)
        public void PreloadBlob()
        {
            _ = Blob; // Just access to trigger creation if needed
        }
    }

    public partial class ModelBinConfigViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _outputName;

        [ObservableProperty]
        private bool _isSelected; // Used for UI selection of the config itself

        public ObservableCollection<GroupViewModel> Groups { get; } = new();

        public Bundle ActiveBundle { get; set; }
        public bool IsDirty { get; set; } = true;

        public ModelBinConfigViewModel(string name)
        {
            OutputName = name;
        }

        [RelayCommand]
        private void Rename()
        {
            // Simple rename logic placeholder - could trigger a dialog or inline edit state
            // For now, relies on binding to OutputName textbox
        }
    }

    public partial class GroupViewModel : ObservableObject
    {
        public string Name { get; set; }
        public string TextureName { get; set; }
        public string OriginalMaterial { get; set; }
        public SceneGroup SourceGroup { get; set; }
        
        // Track which bin this group belongs to
        public ModelBinConfigViewModel AssignedBin { get; set; }

        [ObservableProperty]
        private bool _isSelected = true;

        private string _currentMaterial;
        public string SelectedMaterial
        {
            get => _currentMaterial;
            set
            {
                if (SetProperty(ref _currentMaterial, value))
                {
                    _onSelectionChanged?.Invoke(this);
                }
            }
        }

        [ObservableProperty]
        private string _selectedTexture; // Helper for display

        // Base Transform (calculated from geometry)
        public float BaseScaleX { get; set; } = 1f;
        public float BaseScaleY { get; set; } = 1f;
        public float BaseScaleZ { get; set; } = 1f;
        public float BaseTranslateX { get; set; } = 0f;
        public float BaseTranslateY { get; set; } = 0f;
        public float BaseTranslateZ { get; set; } = 0f;

        // Per-Object Transform Properties (user editable - these are the FINAL values)
        private float _objScaleX = 1f;
        public float ObjScaleX
        {
            get => _objScaleX;
            set
            {
                if (SetProperty(ref _objScaleX, value))
                {
                    _onSelectionChanged?.Invoke(this);
                }
            }
        }

        private float _objScaleY = 1f;
        public float ObjScaleY
        {
            get => _objScaleY;
            set
            {
                if (SetProperty(ref _objScaleY, value))
                {
                    _onSelectionChanged?.Invoke(this);
                }
            }
        }

        private float _objScaleZ = 1f;
        public float ObjScaleZ
        {
            get => _objScaleZ;
            set
            {
                if (SetProperty(ref _objScaleZ, value))
                {
                    _onSelectionChanged?.Invoke(this);
                }
            }
        }

        private float _objPosX = 0f;
        public float ObjPosX
        {
            get => _objPosX;
            set
            {
                if (SetProperty(ref _objPosX, value))
                {
                    _onSelectionChanged?.Invoke(this);
                }
            }
        }

        private float _objPosY = 0f;
        public float ObjPosY
        {
            get => _objPosY;
            set
            {
                if (SetProperty(ref _objPosY, value))
                {
                    _onSelectionChanged?.Invoke(this);
                }
            }
        }

        private float _objPosZ = 0f;
        public float ObjPosZ
        {
            get => _objPosZ;
            set
            {
                if (SetProperty(ref _objPosZ, value))
                {
                    _onSelectionChanged?.Invoke(this);
                }
            }
        }

        private Action<GroupViewModel> _onSelectionChanged;
        private ObservableCollection<string> _sharedMaterials;

        public ObservableCollection<string> PossibleMaterials => _sharedMaterials;

        public GroupViewModel(SceneGroup group, string texture, ObservableCollection<string> sharedMaterials, Action<GroupViewModel> onSelectionChanged)
        {
            Name = group.Name;
            TextureName = texture ?? "No Texture";
            OriginalMaterial = group.MaterialName ?? "Default";
            SourceGroup = group;
            _sharedMaterials = sharedMaterials;
            _onSelectionChanged = onSelectionChanged;

            if (sharedMaterials.Contains(OriginalMaterial))
            {
                SelectedMaterial = OriginalMaterial;
            }
            else
            {
                // Try fuzzy/case-insensitive match
                var match = sharedMaterials.FirstOrDefault(m => m.Equals(OriginalMaterial, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    SelectedMaterial = match;
                }
                else
                {
                    SelectedMaterial = sharedMaterials.FirstOrDefault();
                }
            }
        }

        // Sets the base transform values calculated from geometry processing.
        // Also initializes the user-editable values to match.
        public void SetBaseTransform(Vector4 scale, Vector4 translate)
        {
            BaseScaleX = scale.X;
            BaseScaleY = scale.Y;
            BaseScaleZ = scale.Z;
            BaseTranslateX = translate.X;
            BaseTranslateY = translate.Y;
            BaseTranslateZ = translate.Z;

            // Initialize user-editable values to the calculated base
            _objScaleX = scale.X;
            _objScaleY = scale.Y;
            _objScaleZ = scale.Z;
            _objPosX = translate.X;
            _objPosY = translate.Y;
            _objPosZ = translate.Z;

            // Notify UI of changes
            OnPropertyChanged(nameof(ObjScaleX));
            OnPropertyChanged(nameof(ObjScaleY));
            OnPropertyChanged(nameof(ObjScaleZ));
            OnPropertyChanged(nameof(ObjPosX));
            OnPropertyChanged(nameof(ObjPosY));
            OnPropertyChanged(nameof(ObjPosZ));
        }

        public void ApplyGlobalTransform(float scaleX, float scaleY, float scaleZ, float transX, float transY, float transZ)
        {
            float newScaleX = BaseScaleX * scaleX;
            float newScaleY = BaseScaleY * scaleY;
            float newScaleZ = BaseScaleZ * scaleZ;

            // Apply global scale to base position + global translation
            float newPosX = (BaseTranslateX * scaleX) + transX;
            float newPosY = (BaseTranslateY * scaleY) + transY;
            float newPosZ = (BaseTranslateZ * scaleZ) + transZ;

            if (_objScaleX != newScaleX || _objScaleY != newScaleY || _objScaleZ != newScaleZ)
            {
                _objScaleX = newScaleX;
                _objScaleY = newScaleY;
                _objScaleZ = newScaleZ;
                OnPropertyChanged(nameof(ObjScaleX));
                OnPropertyChanged(nameof(ObjScaleY));
                OnPropertyChanged(nameof(ObjScaleZ));
            }

            if (_objPosX != newPosX || _objPosY != newPosY || _objPosZ != newPosZ)
            {
                _objPosX = newPosX;
                _objPosY = newPosY;
                _objPosZ = newPosZ;
                OnPropertyChanged(nameof(ObjPosX));
                OnPropertyChanged(nameof(ObjPosY));
                OnPropertyChanged(nameof(ObjPosZ));
            }
        }

        // NEW: Helper for transform sorting/grouping
        public int SortIndex { get; set; }

        partial void OnIsSelectedChanged(bool value) => _onSelectionChanged?.Invoke(this);
    }

    public partial class ModelBinCreatorViewModel : ObservableObject
    {
        private readonly ModelBuilderService _builderService = new ModelBuilderService();
        private readonly ObjParserService _parserService = new ObjParserService();
        private readonly FbxParserService _fbxParser = new FbxParserService();
        private readonly SettingsService _settingsService = new SettingsService();
        private bool _isManualOverride;
        private bool _suppressZipSelectionChange;
        private bool _hasInitializedZip;
        private readonly Dictionary<string, string> _materialZipOptionPaths = new();

        // Abbreviation map used by fuzzy material auto-matching
        private static readonly Dictionary<string, string[]> _materialAbbreviations = new(StringComparer.OrdinalIgnoreCase)
        {
            { "glass",    new[] { "gls", "glss", "glaz" } },
            { "carbon",   new[] { "crbn", "carb", "crb" } },
            { "chrome",   new[] { "chr", "chrm" } },
            { "plastic",  new[] { "plstc", "plas", "plst" } },
            { "metal",    new[] { "mtl", "met", "metl" } },
            { "leather",  new[] { "lth", "lthr" } },
            { "rubber",   new[] { "rbr", "rubb" } },
            { "interior", new[] { "int", "intr" } },
            { "exterior", new[] { "ext" } },
            { "black",    new[] { "blk", "blck" } },
            { "white",    new[] { "wht", "wh" } },
            { "paint",    new[] { "pnt" } },
            { "body",     new[] { "bdy", "bd" } },
            { "window",   new[] { "wnd", "win", "wndw" } },
        };

        // NEW: Material Library Dictionary
        private Dictionary<string, CachedMaterialBlob> _materialLibrary = new();

        private Bundle _activeBundle;
        private SceneData _rawScene;

        [ObservableProperty] private ObservableCollection<ModelBinConfigViewModel> _binConfigs = new();
        [ObservableProperty] private ModelBinConfigViewModel _selectedBinConfig;
        private bool _isUpdatingSelection;

        [ObservableProperty] private string _statusMessage = "Select an OBJ or FBX file to begin.";
        [ObservableProperty] private string _mtlStatusMessage = "";
        [ObservableProperty] private string _inputFilePath;
        [ObservableProperty][NotifyPropertyChangedFor(nameof(IsNotBusy))] private bool _isBusy;
        public bool IsNotBusy => !IsBusy;
        [ObservableProperty] private bool _isFileSelected;

        // Game Target Selection
        public ObservableCollection<string> GameTargetOptions { get; } =
        [
            "Forza Horizon 6",
            "Forza Horizon 5",
            "Forza Motorsport (2023)",
            "Forza Horizon 4",
            "Forza Horizon 3",
            "Forza Motorsport 7",
            "Forza Motorsport 6",
            "Forza Horizon 2",
            "Forza Motorsport 5",
        ];

        [ObservableProperty] private string _selectedGameTarget = "Forza Horizon 5";

        // Returns the ForzaGameTarget enum for the currently selected game.
        public ForzaGameTarget CurrentGameTarget => SelectedGameTarget switch
        {
            "Forza Horizon 6" => ForzaGameTarget.FH6,
            "Forza Horizon 5" => ForzaGameTarget.FH5,
            "Forza Motorsport (2023)" => ForzaGameTarget.FM2023,
            "Forza Horizon 4" => ForzaGameTarget.FH4,
            "Forza Horizon 3" => ForzaGameTarget.FH3,
            "Forza Motorsport 7" => ForzaGameTarget.FM7,
            "Forza Motorsport 6" => ForzaGameTarget.FM6,
            "Forza Horizon 2" => ForzaGameTarget.FH2,
            "Forza Motorsport 5" => ForzaGameTarget.FM5,
            _ => ForzaGameTarget.FH5,
        };

        // Human-readable description of the VLay layout that will be generated for the selected game.
        [ObservableProperty] private string _vlayLayoutDescription = "";

        partial void OnSelectedGameTargetChanged(string value)
        {
            OnPropertyChanged(nameof(CurrentGameTarget));
            UpdateVlayLayoutDescription();
            _isManualOverride = false; // Reset manual override so new game auto-loads its zip
            _hasInitializedZip = false; // Force zip reload for the newly selected game target
            _ = TryAutoLoadMaterialZipAsync();
            RebuildBundle();
        }

        private void UpdateVlayLayoutDescription()
        {
            var info = ModelBuilderService.GetGameLayoutInfo(CurrentGameTarget);
            VlayLayoutDescription = $"Slot 1: {info.ElementCount} elements, stride={info.Stride}B ? " +
                                    $"NORMAL({info.NormalFormat}), TANGENT({info.TangentFormat}), " +
                                    $"TEXCOORDs: {info.TexcoordCount}, TANGENTs: {info.TangentCount}" +
                                    (info.HasColor ? ", COLOR0" : "");
        }

        // Zip and Material Assignment Properties
        [ObservableProperty] private string _materialZipPath;
        [ObservableProperty] private bool _isZipLoading;
        [ObservableProperty] private bool _isZipLoaded;
        [ObservableProperty] private string _zipLoadProgress = "";
        [ObservableProperty] private string _materialZipSourceLabel = "No zip loaded — Browse or configure a game path";
        [ObservableProperty] private bool _isAdvancedVlayBlobPatchEnabled;
        [ObservableProperty] private string _selectedMaterialZipOption;

        public ObservableCollection<string> MaterialZipOptions { get; } = new();
        public bool HasMaterialZipDropdown => MaterialZipOptions.Count > 1;

        partial void OnSelectedMaterialZipOptionChanged(string value)
        {
            if (_suppressZipSelectionChange || string.IsNullOrEmpty(value) || _isManualOverride) return;
            if (_materialZipOptionPaths.TryGetValue(value, out string fullPath))
                _ = LoadMaterialZipAsync(fullPath, isAutoLoad: true);
        }
        [ObservableProperty] private int _availableMaterialCount;
        [ObservableProperty] private string _materialSearchText = "";
        
        // NEW: Search Property for Groups
        [ObservableProperty] private string _groupSearchText = "";

        public ObservableCollection<string> AvailableMaterials { get; } = new();
        public ObservableCollection<string> FilteredAvailableMaterials { get; } = new();
        public ObservableCollection<MaterialAssignment> MaterialAssignments { get; } = new();
        
        // NEW: Filtered views and original material mappings
        public ObservableCollection<MaterialAssignment> FilteredMaterialAssignments { get; } = new();
        public ObservableCollection<OriginalMaterialMapping> OriginalMaterialMappings { get; } = new();
        
        // NEW: Selection tracking for material tab
        [ObservableProperty]
        private GroupViewModel _selectedGroupForMaterialAssignment;

        partial void OnSelectedGroupForMaterialAssignmentChanged(GroupViewModel value)
        {
            UpdateFilteredMaterialAssignments();
        }

        // NEW: Material Browser State
        [ObservableProperty] private bool _isMaterialBrowserOpen;
        [ObservableProperty] private string _browserSearchText = "";
        private object _browserContext; // Can be MaterialAssignment or OriginalMaterialMapping

        // Library browser: game library source
        [ObservableProperty] private bool _isBrowserLibraryMode;
        [ObservableProperty] private ForzaGameDefinition _selectedBrowserLibraryGame;
        public List<ForzaGameDefinition> BrowserLibraryGames => ForzaGameCatalog.MaterialLibraryGames.ToList();
        public ObservableCollection<string> BrowserActiveItems { get; } = new();

        // Cache loaded library entries so we don't reload on every filter keystroke
        private Dictionary<string, MaterialEntry> _currentLibraryEntries = new(StringComparer.OrdinalIgnoreCase);

        partial void OnBrowserSearchTextChanged(string value)
        {
            if (IsBrowserLibraryMode)
                RefreshBrowserItemsCore();
            else
                FilterBrowserMaterials();
        }

        partial void OnIsBrowserLibraryModeChanged(bool value)
        {
            if (!IsMaterialBrowserOpen) return;
            if (value)
                _ = RefreshBrowserItemsAsync();
            else
                FilterBrowserMaterials();
        }

        partial void OnSelectedBrowserLibraryGameChanged(ForzaGameDefinition value)
        {
            if (IsBrowserLibraryMode && IsMaterialBrowserOpen)
                _ = RefreshBrowserItemsAsync();
        }

        private async Task RefreshBrowserItemsAsync()
        {
            string gameId = SelectedBrowserLibraryGame?.GameId ?? ForzaGameCatalog.GetPreferredMaterialLibraryGameId();
            var library = await Task.Run(() => MaterialLibrary.LoadEntries(gameId, out _, includeLegacyFallback: true));
            _currentLibraryEntries = library;
            RefreshBrowserItemsCore();
        }

        private void RefreshBrowserItemsCore()
        {
            BrowserActiveItems.Clear();
            if (IsBrowserLibraryMode)
            {
                string q = BrowserSearchText?.Trim() ?? "";
                var keys = _currentLibraryEntries.Keys
                    .Where(k => string.IsNullOrWhiteSpace(q) || k.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
                foreach (var n in keys) BrowserActiveItems.Add(n);
            }
            else
            {
                string q = BrowserSearchText?.Trim() ?? "";
                var items = string.IsNullOrWhiteSpace(q)
                    ? AvailableMaterials.AsEnumerable()
                    : AvailableMaterials.Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase));
                foreach (var m in items) BrowserActiveItems.Add(m);
            }
        }

        private void FilterBrowserMaterials()
        {
            FilteredAvailableMaterials.Clear();
            if (string.IsNullOrWhiteSpace(BrowserSearchText))
            {
                foreach (var mat in AvailableMaterials) FilteredAvailableMaterials.Add(mat);
            }
            else
            {
                var query = BrowserSearchText.ToLowerInvariant();
                foreach (var mat in AvailableMaterials.Where(m => m.ToLowerInvariant().Contains(query)))
                    FilteredAvailableMaterials.Add(mat);
            }
            // Keep BrowserActiveItems in sync when in zip mode
            if (!IsBrowserLibraryMode)
                RefreshBrowserItemsCore();
        }

        // Axis Selection (Import Settings)
        public ObservableCollection<string> AxisOptions { get; } =
        [
            "+X", "-X", "+Y", "-Y", "+Z", "-Z"
        ];

        [ObservableProperty] private string _selectedForwardAxis = "-Z";
        [ObservableProperty] private string _selectedUpAxis = "+Y";

        // Live Edit Properties
        [ObservableProperty] private bool _isOpaque = true;
        [ObservableProperty] private bool _isDecal;
        [ObservableProperty] private bool _isTransparent;
        [ObservableProperty] private bool _isShadow = true;
        [ObservableProperty] private bool _isNotShadow;
        [ObservableProperty] private bool _isAlphaToCoverage;
        [ObservableProperty] private bool _isMorphDamage = true;

        // Per-Object Transform Selection (manual to avoid source gen conflicts)
        private GroupViewModel _currentTransformObject;
        public GroupViewModel SelectedTransformObject
        {
            get => _currentTransformObject;
            set
            {
                if (SetProperty(ref _currentTransformObject, value))
                {
                    OnPropertyChanged(nameof(CanEditTransform));
                    // Refresh transform display properties
                    OnPropertyChanged(nameof(TransformPosX));
                    OnPropertyChanged(nameof(TransformPosY));
                    OnPropertyChanged(nameof(TransformPosZ));
                    OnPropertyChanged(nameof(TransformScaleX));
                    OnPropertyChanged(nameof(TransformScaleY));
                    OnPropertyChanged(nameof(TransformScaleZ));
                }
            }
        }

        public bool CanEditTransform => _currentTransformObject != null;

        // Filtered list showing only selected/enabled groups for transform editing
        public ObservableCollection<GroupViewModel> TransformableGroups { get; } = new();

        // Transform Editing (bound to selected object)
        public float TransformPosX
        {
            get => _currentTransformObject?.ObjPosX ?? 0f;
            set
            {
                if (_currentTransformObject != null && _currentTransformObject.ObjPosX != value)
                {
                    _currentTransformObject.ObjPosX = value;
                    OnPropertyChanged();
                    UpdateTransformInMemory();
                }
            }
        }

        public float TransformPosY
        {
            get => _currentTransformObject?.ObjPosY ?? 0f;
            set
            {
                if (_currentTransformObject != null && _currentTransformObject.ObjPosY != value)
                {
                    _currentTransformObject.ObjPosY = value;
                    OnPropertyChanged();
                    UpdateTransformInMemory();
                }
            }
        }

        public float TransformPosZ
        {
            get => _currentTransformObject?.ObjPosZ ?? 0f;
            set
            {
                if (_currentTransformObject != null && _currentTransformObject.ObjPosZ != value)
                {
                    _currentTransformObject.ObjPosZ = value;
                    OnPropertyChanged();
                    UpdateTransformInMemory();
                }
            }
        }

        public float TransformScaleX
        {
            get => _currentTransformObject?.ObjScaleX ?? 1f;
            set
            {
                if (_currentTransformObject != null && _currentTransformObject.ObjScaleX != value)
                {
                    _currentTransformObject.ObjScaleX = value;
                    OnPropertyChanged();
                    UpdateTransformInMemory();
                }
            }
        }

        public float TransformScaleY
        {
            get => _currentTransformObject?.ObjScaleY ?? 1f;
            set
            {
                if (_currentTransformObject != null && _currentTransformObject.ObjScaleY != value)
                {
                    _currentTransformObject.ObjScaleY = value;
                    OnPropertyChanged();
                    UpdateTransformInMemory();
                }
            }
        }

        public float TransformScaleZ
        {
            get => _currentTransformObject?.ObjScaleZ ?? 1f;
            set
            {
                if (_currentTransformObject != null && _currentTransformObject.ObjScaleZ != value)
                {
                    _currentTransformObject.ObjScaleZ = value;
                    OnPropertyChanged();
                    UpdateTransformInMemory();
                }
            }
        }

        // Rotation
        [ObservableProperty] private float _rotationPitch;
        [ObservableProperty] private float _rotationYaw;
        [ObservableProperty] private float _rotationRoll;

        // Vertex Flip Options
        [ObservableProperty] private bool _flipVertexX;
        [ObservableProperty] private bool _flipVertexY;
        [ObservableProperty] private bool _flipVertexZ;

        // Normal Flip Options
        [ObservableProperty] private bool _flipNormalX;
        [ObservableProperty] private bool _flipNormalY;
        [ObservableProperty] private bool _flipNormalZ;

        // Face Winding
        [ObservableProperty] private bool _flipFaces;

        // Mirror
        [ObservableProperty] private bool _isMirrorEnabled;

        // Recalculate Normals
        [ObservableProperty] private bool _recalculateNormals;

        // Global Resize
        [ObservableProperty] private double _globalResizePercentage = 100;

        // Global Transform
        [ObservableProperty] private float _globalScaleX = 1f;
        [ObservableProperty] private float _globalScaleY = 1f;
        [ObservableProperty] private float _globalScaleZ = 1f;
        
        [ObservableProperty] private float _globalPosX = 0f;
        [ObservableProperty] private float _globalPosY = 0f;
        [ObservableProperty] private float _globalPosZ = 0f;

        // Material & Groups
        public ObservableCollection<string> Materials { get; } = new ObservableCollection<string>();

        public ObservableCollection<GroupViewModel> ModelGroups { get; } = new ObservableCollection<GroupViewModel>();
        
        // Filtered view for Materials Page
        public IEnumerable<GroupViewModel> EnabledGroups => ModelGroups.Where(g => g.IsSelected);

        [ObservableProperty]
        private string _selectAllButtonText = "Deselect All";

        [ObservableProperty]
        private string _sortMaterialButtonText = "Sort by Material";

        [ObservableProperty]
        private bool _canSortByMaterial;

        public string SaveButtonText => BinConfigs.Count > 1 ? "Batch Save ModelBins" : "Save ModelBin";

        public ModelBinCreatorViewModel()
        {
            UpdateVlayLayoutDescription();
        }

        // Axis Conversion Helper
        private CoordinateAxis ParseAxisString(string axis) => axis switch
        {
            "+X" => CoordinateAxis.PositiveX,
            "-X" => CoordinateAxis.NegativeX,
            "+Y" => CoordinateAxis.PositiveY,
            "-Y" => CoordinateAxis.NegativeY,
            "+Z" => CoordinateAxis.PositiveZ,
            "-Z" => CoordinateAxis.NegativeZ,
            _ => CoordinateAxis.PositiveY
        };

        // Change Handlers
        partial void OnFlipVertexXChanged(bool value) => RebuildBundle();
        partial void OnFlipVertexYChanged(bool value) => RebuildBundle();
        partial void OnFlipVertexZChanged(bool value) => RebuildBundle();
        partial void OnFlipNormalXChanged(bool value) => RebuildBundle();
        partial void OnFlipNormalYChanged(bool value) => RebuildBundle();
        partial void OnFlipNormalZChanged(bool value) => RebuildBundle();
        partial void OnFlipFacesChanged(bool value) => RebuildBundle();
        partial void OnIsMirrorEnabledChanged(bool value) => RebuildBundle();
        partial void OnRecalculateNormalsChanged(bool value) => RebuildBundle();
        
        partial void OnGlobalResizePercentageChanged(double value)
        {
            float factor = (float)(value / 100.0);
            
            // Set backing fields directly to avoid multiple triggers if possible, 
            // but we need the properties to update for UI binding.
            bool changed = false;
            
            if (_globalScaleX != factor) { _globalScaleX = factor; OnPropertyChanged(nameof(GlobalScaleX)); changed = true; }
            if (_globalScaleY != factor) { _globalScaleY = factor; OnPropertyChanged(nameof(GlobalScaleY)); changed = true; }
            if (_globalScaleZ != factor) { _globalScaleZ = factor; OnPropertyChanged(nameof(GlobalScaleZ)); changed = true; }
            
            if (changed)
            {
                ApplyGlobalTransforms();
            }
        }

        partial void OnGlobalScaleXChanged(float value) => ApplyGlobalTransforms();
        partial void OnGlobalScaleYChanged(float value) => ApplyGlobalTransforms();
        partial void OnGlobalScaleZChanged(float value) => ApplyGlobalTransforms();
        partial void OnGlobalPosXChanged(float value) => ApplyGlobalTransforms();
        partial void OnGlobalPosYChanged(float value) => ApplyGlobalTransforms();
        partial void OnGlobalPosZChanged(float value) => ApplyGlobalTransforms();

        private void ApplyGlobalTransforms()
        {
             foreach (var group in ModelGroups)
            {
                group.ApplyGlobalTransform(_globalScaleX, _globalScaleY, _globalScaleZ, _globalPosX, _globalPosY, _globalPosZ);
            }
            
            // Apply updates
            UpdateTransformInMemory();
            
            // Refresh current selection UI
            if (_currentTransformObject != null)
            {
                OnPropertyChanged(nameof(TransformPosX));
                OnPropertyChanged(nameof(TransformPosY));
                OnPropertyChanged(nameof(TransformPosZ));
                OnPropertyChanged(nameof(TransformScaleX));
                OnPropertyChanged(nameof(TransformScaleY));
                OnPropertyChanged(nameof(TransformScaleZ));
            }
        }

        partial void OnIsOpaqueChanged(bool value) => UpdateMeshFlags();
        partial void OnIsDecalChanged(bool value) => UpdateMeshFlags();
        partial void OnIsTransparentChanged(bool value) => UpdateMeshFlags();
        partial void OnIsShadowChanged(bool value) => UpdateMeshFlags();
        partial void OnIsNotShadowChanged(bool value) => UpdateMeshFlags();
        partial void OnIsAlphaToCoverageChanged(bool value) => UpdateMeshFlags();
        partial void OnIsMorphDamageChanged(bool value) => UpdateMeshFlags();

        private void UpdateMeshFlags()
        {
            if (_activeBundle == null) return;
            foreach (var mesh in _activeBundle.Blobs.OfType<MeshBlob>())
            {
                mesh.IsOpaque = IsOpaque;
                mesh.IsDecal = IsDecal;
                mesh.IsTransparent = IsTransparent;
                mesh.IsShadow = IsShadow;
                mesh.IsNotShadow = IsNotShadow;
                mesh.IsAlphaToCoverage = IsAlphaToCoverage;
                mesh.IsMorphDamage = IsMorphDamage;
            }
        }

        partial void OnGroupSearchTextChanged(string value)
        {

        }

        [RelayCommand]
        private void SearchGroups()
        {
            
            if (_filteredModelGroups == null) _filteredModelGroups = new ObservableCollection<GroupViewModel>(ModelGroups);
            
            _filteredModelGroups.Clear();
            if (string.IsNullOrWhiteSpace(GroupSearchText))
            {
                foreach(var g in ModelGroups) _filteredModelGroups.Add(g);
            }
            else
            {
                var query = GroupSearchText.ToLower();
                foreach(var g in ModelGroups.Where(x => x.Name.ToLower().Contains(query))) 
                    _filteredModelGroups.Add(g);
            }
        }

        // Need a filtered collection for the UI
        private ObservableCollection<GroupViewModel> _filteredModelGroups;
        public ObservableCollection<GroupViewModel> FilteredModelGroups => _filteredModelGroups ??= new ObservableCollection<GroupViewModel>(ModelGroups);

        private void RefillFilteredGroups() 
        {
             if (_filteredModelGroups == null) return;
             _filteredModelGroups.Clear();
             foreach(var g in ModelGroups) _filteredModelGroups.Add(g);
        }

        private async Task RebuildBundleInternalAsync()
        {
            if (SelectedBinConfig != null)
            {
                await BuildBinAsync(SelectedBinConfig);
            }
        }

        private void HandleMaterialAssignmentChanged(MaterialAssignment assignment)
        {
            // Find the group and update its material selection
            var group = ModelGroups.FirstOrDefault(g => g.Name == assignment.MeshName);
            if (group != null)
            {
                // Validate if material actually changed to avoid loop
                if (group.SelectedMaterial != assignment.SelectedMaterial)
                {
                    group.SelectedMaterial = assignment.SelectedMaterial;
                    // Group property change will trigger RebuildBundle via callback
                }
            }
        }

        private async Task BuildBinAsync(ModelBinConfigViewModel targetBin)
        {
            IsBusy = true;
            StatusMessage = $"Processing {targetBin.OutputName}...";

            try
            {
                // Capture the current game target on UI thread
                var gameTarget = CurrentGameTarget;

                // Capture enabled groups and their settings safely on UI thread
                var groupsToProcess = targetBin.Groups
                    .Select(g => new { 
                        Group = g.SourceGroup, 
                        MatName = g.SelectedMaterial,
                        ViewModel = g
                    })
                    .ToList();

                if (groupsToProcess.Count == 0) 
                {
                    targetBin.ActiveBundle = null;
                    if (targetBin == SelectedBinConfig) _activeBundle = null;
                    IsBusy = false;
                    return;
                }

                // Store transform updates to apply on UI thread
                var transformUpdates = new List<(GroupViewModel vm, Vector4 scale, Vector4 translate)>();

                await Task.Run(() =>
                {
                    var objectBuildList = new List<ObjectBuildData>();

                    foreach (var item in groupsToProcess)
                    {
                        var group = item.Group;
                        
                        // Compact geometry for this group
                        var usedVertexIndices = group.Indices.Distinct().OrderBy(i => i).ToList();
                        var oldToNewIndexMap = new Dictionary<int, int>();
                        for (int i = 0; i < usedVertexIndices.Count; i++) oldToNewIndexMap[usedVertexIndices[i]] = i;

                        var compactedPositions = new Vector3[usedVertexIndices.Count];
                        var compactedNormals = new Vector3[usedVertexIndices.Count];
                        var compactedUVs = new Vector2[usedVertexIndices.Count];
                        var compactedTangents = new Vector4[usedVertexIndices.Count];

                        for (int i = 0; i < usedVertexIndices.Count; i++)
                        {
                            int oldIdx = usedVertexIndices[i];
                            compactedPositions[i] = _rawScene.Positions[oldIdx];
                            compactedNormals[i] = oldIdx < _rawScene.Normals.Length ? _rawScene.Normals[oldIdx] : Vector3.UnitY;
                            compactedUVs[i] = oldIdx < _rawScene.UVs.Length ? _rawScene.UVs[oldIdx] : Vector2.Zero;
                            compactedTangents[i] = oldIdx < _rawScene.Tangents.Length ? _rawScene.Tangents[oldIdx] : new Vector4(1, 0, 0, 1);
                        }

                        var remappedIndices = group.Indices.Select(i => oldToNewIndexMap[i]).ToArray();

                        // Modifiers
                        if (FlipVertexX) FlipVector3Array(compactedPositions, true, false, false);
                        if (FlipVertexY) FlipVector3Array(compactedPositions, false, true, false);
                        if (FlipVertexZ) FlipVector3Array(compactedPositions, false, false, true);

                        // Tangents
                        if (FlipVertexX) FlipVector4Array(compactedTangents, true, false, false);
                        if (FlipVertexY) FlipVector4Array(compactedTangents, false, true, false);
                        if (FlipVertexZ) FlipVector4Array(compactedTangents, false, false, true);

                        if (IsMirrorEnabled)
                        {
                            FlipVector3Array(compactedPositions, true, false, false);
                            FlipVector4Array(compactedTangents, true, false, false);
                            for (int i = 0; i < compactedTangents.Length; i++) compactedTangents[i].W *= -1;
                        }

                        // Winding
                        bool shouldFlipWinding = FlipFaces;
                        if (IsMirrorEnabled) shouldFlipWinding = !shouldFlipWinding;
                        if (shouldFlipWinding) FlipFaceWinding(remappedIndices);

                        // Normals
                        Vector3[] finalNormals;
                        if (RecalculateNormals)
                        {
                            finalNormals = RecalculateMeshNormals(compactedPositions, remappedIndices);
                        }
                        else
                        {
                            finalNormals = compactedNormals;
                            if (FlipNormalX) FlipVector3Array(finalNormals, true, false, false);
                            if (FlipNormalY) FlipVector3Array(finalNormals, false, true, false);
                            if (FlipNormalZ) FlipVector3Array(finalNormals, false, false, true);
                            if (IsMirrorEnabled) FlipVector3Array(finalNormals, true, false, false);
                            
                            for (int i = 0; i < finalNormals.Length; i++) finalNormals[i] = Vector3.Normalize(finalNormals[i]);
                        }

                        var geoInput = new GeometryInput
                        {
                            Name = group.Name,
                            Positions = compactedPositions,
                            Normals = finalNormals,
                            UVs = compactedUVs,
                            Tangents = compactedTangents,
                            Indices = remappedIndices
                        };

                        var processedParam = _builderService.ProcessGeometry(geoInput, gameTarget);

                        // Check if we need to initialize the base transform (first time processing)
                        var vm = item.ViewModel;
                        bool isFirstTimeOrReset = (vm.BaseScaleX == 1f && vm.BaseScaleY == 1f && vm.BaseScaleZ == 1f &&
                                                   vm.BaseTranslateX == 0f && vm.BaseTranslateY == 0f && vm.BaseTranslateZ == 0f &&
                                                   vm.ObjScaleX == 1f && vm.ObjScaleY == 1f && vm.ObjScaleZ == 1f &&
                                                   vm.ObjPosX == 0f && vm.ObjPosY == 0f && vm.ObjPosZ == 0f);

                        if (isFirstTimeOrReset)
                        {
                            // Store calculated values to update UI later
                            transformUpdates.Add((vm, processedParam.PositionScale, processedParam.PositionTranslate));
                        }
                        else
                        {
                            // User has modified values - use them directly
                            processedParam.PositionScale = new Vector4(vm.ObjScaleX, vm.ObjScaleY, vm.ObjScaleZ, 1);
                            processedParam.PositionTranslate = new Vector4(vm.ObjPosX, vm.ObjPosY, vm.ObjPosZ, 0);
                        }

                        // Get MaterialBlob from library
                        MaterialBlob materialBlob = null;
                        string materialRelPath = null;
                        if (_materialLibrary.TryGetValue(item.MatName, out var cachedMat))
                        {
                            materialBlob = cachedMat.Blob;
                            materialRelPath = cachedMat.RelativeZipPath;
                        }

                        objectBuildList.Add(new ObjectBuildData 
                        { 
                            Geometry = processedParam, 
                            MaterialName = item.MatName,
                            MaterialBlob = materialBlob,
                            ObjectName = group.Name,
                            MaterialRelativePath = materialRelPath
                        });
                    }

                    _activeBundle = _builderService.CreateBundleFromObjects(objectBuildList, gameTarget);
                });
                
                targetBin.ActiveBundle = _activeBundle;
                if (targetBin != SelectedBinConfig)
                {
                     // If we built a background bin, don't set global _activeBundle unless we want to?
                }

                // Update UI with calculated base transforms (must be on UI thread)
                foreach (var (vm, scale, translate) in transformUpdates)
                {
                    vm.SetBaseTransform(scale, translate);
                }

                // Refresh the transform display if currently selected object was updated
                if (_currentTransformObject != null && transformUpdates.Any(t => t.vm == _currentTransformObject))
                {
                    OnPropertyChanged(nameof(TransformPosX));
                    OnPropertyChanged(nameof(TransformPosY));
                    OnPropertyChanged(nameof(TransformPosZ));
                    OnPropertyChanged(nameof(TransformScaleX));
                    OnPropertyChanged(nameof(TransformScaleY));
                    OnPropertyChanged(nameof(TransformScaleZ));
                }

                if (_activeBundle != null)
                {
                    UpdateMeshFlags();
                    StatusMessage = "Geometry updated.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Rebuild Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void UpdateTransformInMemory()
        {
            if (_activeBundle == null) return;

            // Build a map of object name -> transform from GroupViewModels
            var transformMap = ModelGroups
                .Where(g => g.IsSelected)
                .ToDictionary(g => g.Name, g => new
                {
                    Scale = new Vector4(g.ObjScaleX, g.ObjScaleY, g.ObjScaleZ, 1),
                    Translate = new Vector4(g.ObjPosX, g.ObjPosY, g.ObjPosZ, 0)
                });

            // Update each mesh's transform based on its name
            foreach (var mesh in _activeBundle.Blobs.OfType<MeshBlob>())
            {
                var nameMeta = mesh.Metadatas.OfType<ForzaTools.Bundles.Metadata.NameMetadata>().FirstOrDefault();
                if (nameMeta != null && transformMap.TryGetValue(nameMeta.Name, out var transform))
                {
                    mesh.PositionScale = transform.Scale;
                    mesh.PositionTranslate = transform.Translate;
                }
            }
        }

        private void UpdateTransformableGroups()
        {
            TransformableGroups.Clear();
            foreach (var group in ModelGroups.Where(g => g.IsSelected))
            {
                TransformableGroups.Add(group);
            }

            // Auto-select first if current selection is no longer valid
            if (_currentTransformObject == null || !TransformableGroups.Contains(_currentTransformObject))
            {
                SelectedTransformObject = TransformableGroups.FirstOrDefault();
            }

            OnPropertyChanged(nameof(EnabledGroups));
        }

        private void UpdateSelectAllButtonText()
        {
            bool allSelected = ModelGroups.Count > 0 && ModelGroups.All(g => g.IsSelected);
            SelectAllButtonText = allSelected ? "Deselect All" : "Select All";
            UpdateTransformableGroups();
        }

        [RelayCommand(CanExecute = nameof(CanSortByMaterial))
        ]
        private void SortByMaterial()
        {
            if (ModelGroups.Count == 0) return;

            var sorted = ModelGroups.OrderBy(g => g.OriginalMaterial ?? "")
                                    .ThenBy(g => g.Name)
                                    .ToList();
            
            ModelGroups.Clear();
            foreach (var g in sorted)
            {
                ModelGroups.Add(g);
            }
        }

        [RelayCommand]
        private void ToggleSelectAllGroups()
        {
            if (ModelGroups.Count == 0) return;

            bool allSelected = ModelGroups.All(g => g.IsSelected);
            bool newState = !allSelected;

            foreach (var group in ModelGroups)
            {
                group.IsSelected = newState;
            }

            UpdateSelectAllButtonText();
        }

        // NEW: Batch selection commands for Configuration Tab
        [RelayCommand]
        public void SelectAllInConfig()
        {
            BatchUpdateSelection(true);
        }

        [RelayCommand]
        public void DeselectAllInConfig()
        {
            BatchUpdateSelection(false);
        }

        private void BatchUpdateSelection(bool select)
        {
            if (SelectedBinConfig == null) return;
            // Use FilteredModelGroups so we only affect what the user likely sees or searches for
            // (though normally config tab shows all)
            var targets = FilteredModelGroups.ToList(); 
            if (targets.Count == 0) return;

            _isUpdatingSelection = true;
            try
            {
                foreach (var g in targets)
                {
                    // Only process if state is changing to avoid redundant logic
                    if (g.IsSelected != select)
                    {
                        g.IsSelected = select;
                        
                        // Replicate logic from HandleGroupSelectionChanged without triggering Rebuild per item
                        if (select)
                        {
                            // Move to current bin
                            if (g.AssignedBin != null)
                            {
                                g.AssignedBin.Groups.Remove(g);
                                g.AssignedBin.IsDirty = true;
                            }
                            g.AssignedBin = SelectedBinConfig;
                            SelectedBinConfig.Groups.Add(g);
                            SelectedBinConfig.IsDirty = true;
                        }
                        else
                        {
                            // Remove from current bin (if it was assigned to it)
                            if (g.AssignedBin == SelectedBinConfig)
                            {
                                g.AssignedBin = null;
                                SelectedBinConfig.Groups.Remove(g);
                                SelectedBinConfig.IsDirty = true;
                            }
                        }
                    }
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }

            RebuildBundle();
            UpdateSelectAllButtonText();
        }

        [RelayCommand]
        public async Task PickInputFileAsync()
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;

            picker.FileTypeFilter.Add(".obj");
            picker.FileTypeFilter.Add(".fbx");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                InputFilePath = file.Path;
                await InitializeConversionAsync();
            }
        }

        private async Task InitializeConversionAsync()
        {
            IsBusy = true;
            StatusMessage = "Parsing model file...";
            MtlStatusMessage = "";

            try
            {
                string ext = Path.GetExtension(InputFilePath).ToLower();
                Dictionary<string, string> matTextures = new Dictionary<string, string>();

                if (ext == ".obj")
                {
                    var importSettings = new ImportSettings
                    {
                        ForwardAxis = ParseAxisString(SelectedForwardAxis),
                        UpAxis = ParseAxisString(SelectedUpAxis)
                    };

                    _rawScene = await Task.Run(() => _parserService.ParseObj(InputFilePath, importSettings));

                    string mtlPath = null;

                    if (!string.IsNullOrEmpty(_rawScene.MaterialLib))
                    {
                        string dir = Path.GetDirectoryName(InputFilePath);
                        string checkPath = Path.Combine(dir, _rawScene.MaterialLib);
                        if (File.Exists(checkPath)) mtlPath = checkPath;
                    }

                    if (mtlPath == null)
                    {
                        string sameNameMtl = Path.ChangeExtension(InputFilePath, ".mtl");
                        if (File.Exists(sameNameMtl)) mtlPath = sameNameMtl;
                    }

                    if (mtlPath != null)
                    {
                        matTextures = await Task.Run(() => _parserService.ParseMtl(mtlPath));
                        MtlStatusMessage = $"Loaded MTL: {Path.GetFileName(mtlPath)}";
                    }
                    else
                    {
                        MtlStatusMessage = "No associated MTL file found.";
                    }
                }
                else if (ext == ".fbx")
                {
                    _rawScene = await Task.Run(() => _fbxParser.Parse(InputFilePath));
                    MtlStatusMessage = "Materials loaded from FBX.";
                }

                ModelGroups.Clear();
                TransformableGroups.Clear();
                // Reset filter
                if (_filteredModelGroups != null) _filteredModelGroups.Clear();
                
                SelectedTransformObject = null;
                BinConfigs.Clear();

                // Create default bin
                var defaultBin = new ModelBinConfigViewModel(Path.GetFileNameWithoutExtension(InputFilePath));
                BinConfigs.Add(defaultBin);
                
                if (_rawScene != null && _rawScene.Groups != null)
                {
                    bool isFirst = true;
                    foreach (var g in _rawScene.Groups)
                    {
                        if (g.Indices.Count > 0)
                        {
                            string tex = null;
                            if (!string.IsNullOrEmpty(g.MaterialName) && matTextures.ContainsKey(g.MaterialName))
                            {
                                tex = matTextures[g.MaterialName];
                            }
                            else if (ext == ".fbx")
                            {
                                tex = "Embed/FBX";
                            }

                            var newGroup = new GroupViewModel(g, tex, Materials, HandleGroupSelectionChanged);
                            newGroup.AssignedBin = defaultBin;
                            
                            // Only select the first model for better performance
                            if (isFirst)
                            {
                                newGroup.IsSelected = true;
                                defaultBin.Groups.Add(newGroup);
                                isFirst = false;
                            }
                            else
                            {
                                newGroup.IsSelected = false;
                            }
                            
                            ModelGroups.Add(newGroup);
                            _filteredModelGroups?.Add(newGroup); // Add to filter view
                        }
                    }
                }
                
                SelectedBinConfig = defaultBin;
                UpdateTransformableGroups();

                // Check materials for sort button
                bool hasMaterials = ModelGroups.Any(g => g.OriginalMaterial != "Default");
                CanSortByMaterial = hasMaterials;
                SortMaterialButtonText = hasMaterials ? "Sort by Material" : "No materials found";
                // Force command update
                SortByMaterialCommand.NotifyCanExecuteChanged();

                // Populate MaterialAssignments if zip is already loaded
                if (IsZipLoaded)
                {
                    PopulateMaterialAssignments();
                }

                await RebuildBundleInternalAsync();

                IsFileSelected = true;
                StatusMessage = "Ready.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                IsFileSelected = false;
            }
            finally { IsBusy = false; }
        }

        private void RebuildBundle()
        {
            if (_rawScene == null) return;
            if (IsBusy) return;
            _ = RebuildBundleInternalAsync();
        }

        private void HandleGroupSelectionChanged(GroupViewModel g)
        {
            if (_isUpdatingSelection) return;
            
            // Handle bin assignment changes
            if (SelectedBinConfig != null)
            {
                // Reference check for mismatch
                bool isAssignedToCurrent = g.AssignedBin == SelectedBinConfig;
                
                if (g.IsSelected && !isAssignedToCurrent)
                {
                    // User checked it -> Move to current bin
                    if (g.AssignedBin != null)
                    {
                        g.AssignedBin.Groups.Remove(g);
                        g.AssignedBin.IsDirty = true;
                    }
                    g.AssignedBin = SelectedBinConfig;
                    SelectedBinConfig.Groups.Add(g);
                    SelectedBinConfig.IsDirty = true;
                }
                else if (!g.IsSelected && isAssignedToCurrent)
                {
                    // User unchecked it -> Remove from current bin (become unassigned)
                    g.AssignedBin = null;
                    SelectedBinConfig.Groups.Remove(g);
                    SelectedBinConfig.IsDirty = true;
                }
            }

            RebuildBundle();
            UpdateSelectAllButtonText();
        }

        partial void OnSelectedBinConfigChanged(ModelBinConfigViewModel value)
        {
            if (value != null) UpdateGroupSelectionForActiveBin();
        }

        private void UpdateGroupSelectionForActiveBin()
        {
            if (SelectedBinConfig == null) return;
            _isUpdatingSelection = true;
            try
            {
                foreach (var g in ModelGroups)
                {
                    g.IsSelected = (g.AssignedBin == SelectedBinConfig);
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }
            
            // Switch active bundle for preview
            if (SelectedBinConfig.ActiveBundle != null)
            {
                _activeBundle = SelectedBinConfig.ActiveBundle;
                UpdateMeshFlags(); 
            }
            else
            {
                _ = RebuildBundleInternalAsync();
            }
            
            UpdateTransformableGroups();
            UpdateSelectAllButtonText();
        }

        [RelayCommand]
        private void AddBin()
        {
            var newBin = new ModelBinConfigViewModel($"ModelPart_{BinConfigs.Count + 1}");
            BinConfigs.Add(newBin);
            SelectedBinConfig = newBin;
            OnPropertyChanged(nameof(SaveButtonText));
        }

        [RelayCommand]
        private void RemoveBin(ModelBinConfigViewModel bin)
        {
            if (bin == null) return;
            if (BinConfigs.Count <= 1) return; // Prevent deleting last bin

            // Unassign groups
            foreach(var g in bin.Groups.ToList())
            {
                g.AssignedBin = null;
            }
            BinConfigs.Remove(bin);
            if (SelectedBinConfig == bin)
            {
                SelectedBinConfig = BinConfigs.FirstOrDefault();
            }
            OnPropertyChanged(nameof(SaveButtonText));
        }

        private Vector3[] RecalculateMeshNormals(Vector3[] positions, int[] indices)
        {
            Vector3[] newNormals = new Vector3[positions.Length];

            for (int i = 0; i < indices.Length; i += 3)
            {
                int i1 = indices[i];
                int i2 = indices[i + 1];
                int i3 = indices[i + 2];

                if (i1 >= positions.Length || i2 >= positions.Length || i3 >= positions.Length) continue;

                Vector3 v1 = positions[i1];
                Vector3 v2 = positions[i2];
                Vector3 v3 = positions[i3];

                Vector3 edge1 = v2 - v1;
                Vector3 edge2 = v3 - v1;
                Vector3 faceNormal = Vector3.Cross(edge1, edge2);

                newNormals[i1] += faceNormal;
                newNormals[i2] += faceNormal;
                newNormals[i3] += faceNormal;
            }

            for (int i = 0; i < newNormals.Length; i++)
            {
                if (newNormals[i].LengthSquared() > 0.000001f)
                {
                    newNormals[i] = Vector3.Normalize(newNormals[i]);
                }
                else
                {
                    newNormals[i] = Vector3.UnitY;
                }
            }

            return newNormals;
        }

        private void FlipVector3Array(Vector3[] arr, bool x, bool y, bool z)
        {
            if (!x && !y && !z) return;
            for (int i = 0; i < arr.Length; i++)
            {
                var v = arr[i];
                if (x) v.X = -v.X;
                if (y) v.Y = -v.Y;
                if (z) v.Z = -v.Z;
                arr[i] = v;
            }
        }

        private void FlipVector4Array(Vector4[] arr, bool x, bool y, bool z)
        {
            if (!x && !y && !z) return;
            for (int i = 0; i < arr.Length; i++)
            {
                var v = arr[i];
                if (x) v.X = -v.X;
                if (y) v.Y = -v.Y;
                if (z) v.Z = -v.Z;
                arr[i] = v;
            }
        }

        private void FlipFaceWinding(int[] indices)
        {
            for (int i = 0; i < indices.Length; i += 3)
            {
                if (i + 2 < indices.Length)
                {
                    (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                }
            }
        }

        // Model Processing and Conversion
        [RelayCommand]
        public async Task ConvertModelAsync()
        {
            if (BinConfigs.Count == 0)
            {
                StatusMessage = "No model configuration loaded.";
                return;
            }

            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

            if (BinConfigs.Count == 1)
            {
                var bin = BinConfigs[0];
                var savePicker = new FileSavePicker();
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);
                savePicker.SuggestedStartLocation = PickerLocationId.Desktop;
                savePicker.SuggestedFileName = bin.OutputName + ".modelbin";
                savePicker.FileTypeChoices.Add("Forza ModelBin", new[] { ".modelbin" });

                var outputFile = await savePicker.PickSaveFileAsync();
                if (outputFile == null) return;
                
                IsBusy = true;
                StatusMessage = "Saving...";
                try 
                {
                    // Ensure fresh build
                    UpdateTransformInMemory(); 
                    await BuildBinAsync(bin);
                    
                    if (bin.ActiveBundle != null)
                    {
                        if (IsAdvancedVlayBlobPatchEnabled)
                            await ApplyAdvancedVlayPatchAsync(bin.ActiveBundle);
                         await Task.Run(() => _builderService.SaveBundle(bin.ActiveBundle, outputFile.Path));
                         StatusMessage = $"Saved {outputFile.Name}";
                    }
                }
                catch(Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
                finally { IsBusy = false; }
                return;
            }

            // Multiple -> Folder Picker
            var folderPicker = new FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hWnd);
            folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder == null) return;

            IsBusy = true;
            StatusMessage = "Batch Saving...";
            int savedCount = 0;

            try
            {
                UpdateTransformInMemory();

                foreach (var bin in BinConfigs)
                {
                    if (bin.Groups.Count == 0) continue;

                    // Build
                    await BuildBinAsync(bin);
                    
                    if (bin.ActiveBundle != null)
                    {
                        if (IsAdvancedVlayBlobPatchEnabled)
                            await ApplyAdvancedVlayPatchAsync(bin.ActiveBundle);
                        var path = Path.Combine(folder.Path, bin.OutputName + ".modelbin");
                        await Task.Run(() => _builderService.SaveBundle(bin.ActiveBundle, path));
                        savedCount++;
                    }
                }
                StatusMessage = $"Success! Saved {savedCount} modelbins to {folder.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving batch: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                // Restore active bundle for UI
                if (SelectedBinConfig != null) _activeBundle = SelectedBinConfig.ActiveBundle;
            }
        }

        // Axis Import/Export
        private ImportSettings CreateImportSettings()
        {
            return new ImportSettings
            {
                ForwardAxis = ParseAxisString(SelectedForwardAxis),
                UpAxis = ParseAxisString(SelectedUpAxis)
            };
        }

        // Material Zip Loading
        [RelayCommand]
        public async Task PickMaterialZipAsync()
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".zip");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                _isManualOverride = true;
                _hasInitializedZip = true; // Manual pick counts as initialized — suppress auto-reload on navigation
                await LoadMaterialZipAsync(file.Path, isAutoLoad: false);
            }
        }

        private async Task LoadMaterialZipAsync(string zipPath, bool isAutoLoad = false)
        {
            // If the same zip is already fully loaded, skip the expensive reload to preserve
            // any material assignments the user has already set up.
            if (string.Equals(zipPath, MaterialZipPath, StringComparison.OrdinalIgnoreCase)
                && IsZipLoaded && _materialLibrary.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[MaterialZip] Same zip already loaded, skipping reload: {zipPath}");
                if (IsFileSelected && ModelGroups.Count > 0)
                    PopulateMaterialAssignments();
                return;
            }

            IsZipLoading = true;
            IsZipLoaded = false;
            ZipLoadProgress = "Scanning material zip for .materialbin files...";
            StatusMessage = "Loading material library...";
            
            try
            {
                await Task.Run(() =>
                {
                    _materialLibrary.Clear();
                    
                    // We're NOT extracting files - just cataloging material names from the zip
                    System.Diagnostics.Debug.WriteLine($"[MaterialZip] Cataloging materials from: {zipPath}");
                    
                    try
                    {
                        // Read zip file to get list of .materialbin files
                        using (var zip = new CustomZipFile(zipPath))
                        {
                            var allEntries = zip.GetEntries();
                            var materialFiles = allEntries
                                .Where(e => Path.GetExtension(e.Name).Equals(".materialbin", StringComparison.OrdinalIgnoreCase))
                                .Select(e => e.Name)
                                .ToList();
                            

                            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                            {
                                ZipLoadProgress = $"Found {materialFiles.Count} .materialbin files. Cataloging...";
                            });

                            System.Diagnostics.Debug.WriteLine($"[MaterialZip] Found {materialFiles.Count} material files");

                            // Track material names to handle duplicates
                            var materialNameCounts = new Dictionary<string, int>();

                            foreach (var relativePath in materialFiles)
                            {
                                try
                                {
                                    // Get material name from filename (NO FILE LOADING)
                                    string materialName = Path.GetFileNameWithoutExtension(relativePath);
                                    
                                    // Get parent folder name for duplicate handling
                                    string parentFolder = Path.GetFileName(Path.GetDirectoryName(relativePath));
                                    System.Diagnostics.Debug.WriteLine($"[MaterialZip]   Cataloging: {materialName} (path: {relativePath})");

                                    // Handle duplicates by prefixing with parent folder
                                    string uniqueName = materialName;
                                    if (materialNameCounts.ContainsKey(materialName))
                                    {
                                        uniqueName = $"{parentFolder}_{materialName}";
                                        
                                        // If this combination also exists, add a counter
                                        int counter = 1;
                                        string testName = uniqueName;
                                        while (_materialLibrary.ContainsKey(testName))
                                        {
                                            counter++;
                                            testName = $"{parentFolder}_{materialName}_{counter}";
                                        }
                                        uniqueName = testName;
                                        
                                        materialNameCounts[materialName]++;
                                    }
                                    else
                                    {
                                        materialNameCounts[materialName] = 1;
                                    }
                                    
                                    // Store ONLY the name and path reference (NO FILE DATA)
                                    if (!_materialLibrary.ContainsKey(uniqueName))
                                    {
                                        _materialLibrary[uniqueName] = new CachedMaterialBlob
                                        {
                                            Name = uniqueName,
                                            FilePath = null, // Not used - we create in-memory
                                            ParentFolder = parentFolder,
                                            RelativeZipPath = relativePath // Store for Game:\ path construction
                                        };

                                        // Add to UI lists (must be on UI thread)
                                        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                                        {
                                            // Add to AvailableMaterials for the zip loader UI
                                            if (!AvailableMaterials.Contains(uniqueName))
                                            {
                                                AvailableMaterials.Add(uniqueName);
                                            }
                                            

                                            // Add to Materials collection for group assignment
                                            if (!Materials.Contains(uniqueName))
                                            {
                                                Materials.Add(uniqueName);
                                            }
                                        });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[MaterialZip] Error cataloging {relativePath}: {ex.Message}");
                                }
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"[MaterialZip] ? Cataloged {_materialLibrary.Count} materials");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MaterialZip] Error reading zip: {ex.Message}");
                        throw;
                    }
                });

                MaterialZipPath = zipPath;
                AvailableMaterialCount = _materialLibrary.Count;
                IsZipLoaded = true;
                MaterialZipSourceLabel = isAutoLoad
                    ? $"Auto-loaded from configured {SelectedGameTarget} install"
                    : "Manually selected";
                ZipLoadProgress = $"Cataloged {AvailableMaterialCount} materials (ready for assignment)";
                StatusMessage = $"Material library loaded: {AvailableMaterialCount} materials available";
                
                // Init filtered view
                App.MainWindow.DispatcherQueue.TryEnqueue(() => 
                {
                    FilterBrowserMaterials();
                });

                // Populate MaterialAssignments if model is already loaded
                if (IsFileSelected && ModelGroups.Count > 0)
                {
                    PopulateMaterialAssignments();
                }
            }
            catch (Exception ex)
            {
                ZipLoadProgress = $"Error: {ex.Message}";
                StatusMessage = $"Error loading material zip: {ex.Message}";
                IsZipLoaded = false;
                System.Diagnostics.Debug.WriteLine($"[MaterialZip] LoadMaterialZipAsync error: {ex}");
            }
            finally
            {
                IsZipLoading = false;
            }
        }

        private void PopulateMaterialAssignments()
        {
            // Snapshot existing user assignments BEFORE clearing
            // This ensures assignments survive any reload (e.g. same zip re-checked on page Loaded).
            var meshSnapshot = MaterialAssignments
                .Where(a => !string.IsNullOrEmpty(a.SelectedMaterial))
                .ToDictionary(a => a.MeshName, a => a.SelectedMaterial, StringComparer.OrdinalIgnoreCase);
            var mappingSnapshot = OriginalMaterialMappings
                .Where(m => !string.IsNullOrEmpty(m.SelectedMaterial))
                .ToDictionary(m => m.OriginalName, m => m.SelectedMaterial, StringComparer.OrdinalIgnoreCase);

            MaterialAssignments.Clear();
            OriginalMaterialMappings.Clear();

            // Populate unique original materials
            var uniqueOriginals = ModelGroups.Select(g => g.OriginalMaterial).Distinct().OrderBy(n => n);
            foreach (var origMat in uniqueOriginals)
            {
                var mapping = new OriginalMaterialMapping(
                    origMat, 
                    AvailableMaterials, 
                    (m, newMat) => UpdateAssignmentsForOriginalMaterial(m, newMat),
                    OpenBrowserForMapping
                );

                // Priority: 1) explicit snapshot (user assigned), 2) group.SelectedMaterial,
                // 3) fuzzy auto-match for fresh assignments.
                if (mappingSnapshot.TryGetValue(origMat, out var snapshotMat)
                    && (AvailableMaterials.Contains(snapshotMat) || _materialLibrary.ContainsKey(snapshotMat)))
                {
                    mapping.SelectedMaterial = snapshotMat;
                }
                else
                {
                    var groupsForMat = ModelGroups.Where(g => g.OriginalMaterial == origMat).ToList();
                    var existingSelections = groupsForMat
                        .Select(g => g.SelectedMaterial)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (existingSelections.Count == 1
                        && (AvailableMaterials.Contains(existingSelections[0]) || _materialLibrary.ContainsKey(existingSelections[0])))
                    {
                        mapping.SelectedMaterial = existingSelections[0];
                    }
                    else
                    {
                        var autoMatch = FindBestMaterialMatch(origMat, AvailableMaterials);
                        if (autoMatch != null)
                            mapping.SelectedMaterial = autoMatch;
                    }
                }

                OriginalMaterialMappings.Add(mapping);
            }

            foreach (var group in ModelGroups)
            {
                var assignment = new MaterialAssignment(
                    group.Name,
                    group.OriginalMaterial,
                    AvailableMaterials,
                    HandleMaterialAssignmentChanged,
                    OpenBrowserForAssignment,
                    _materialLibrary
                );

                // Priority: 1) explicit mesh snapshot, 2) group.SelectedMaterial
                string matToAssign = null;
                if (meshSnapshot.TryGetValue(group.Name, out var snapMat)
                    && (AvailableMaterials.Contains(snapMat) || _materialLibrary.ContainsKey(snapMat)))
                {
                    matToAssign = snapMat;
                }
                else if (!string.IsNullOrEmpty(group.SelectedMaterial))
                {
                    matToAssign = group.SelectedMaterial;
                }

                if (matToAssign != null)
                    assignment.SelectedMaterial = matToAssign;

                MaterialAssignments.Add(assignment);
            }
            
            UpdateFilteredMaterialAssignments();
        }

        private void UpdateFilteredMaterialAssignments()
        {
            FilteredMaterialAssignments.Clear();
            if (SelectedGroupForMaterialAssignment != null)
            {
                // Find assignment for this group
                var assignment = MaterialAssignments.FirstOrDefault(a => a.MeshName == SelectedGroupForMaterialAssignment.Name);
                if (assignment != null)
                {
                    FilteredMaterialAssignments.Add(assignment);
                }
            }
            // If nothing selected, maybe show nothing? Or all? 
        }

        private void UpdateAssignmentsForOriginalMaterial(string originalMatName, string newForzaMat)
        {
            // Propagate to all individual assignments
            foreach (var assignment in MaterialAssignments.Where(a => a.SourceMaterialName == originalMatName))
            {
                assignment.SelectedMaterial = newForzaMat;
            }
            
            // Also update the groups directly to trigger rebuild if needed
            // (The assignment change handler usually does this, but we update ViewModel directly)
            foreach (var group in ModelGroups.Where(g => g.OriginalMaterial == originalMatName))
            {
                group.SelectedMaterial = newForzaMat;
            }
            
            RebuildBundle();
        }

        // NEW: Relay Command wrapper for AutoMatch
        [RelayCommand]
        private void RunAutoMatchWrapper()
        {
             RunAutoMatch();
        }
        
        // Missing methods stub implementations
        private void OpenBrowserForMapping(OriginalMaterialMapping mapping)
        {
            _browserContext = mapping;
            // Initialise browser items for whichever source is currently active
            if (IsBrowserLibraryMode)
                _ = RefreshBrowserItemsAsync();
            else
                FilterBrowserMaterials();
            IsMaterialBrowserOpen = true;
        }
        
        private void OpenBrowserForAssignment(MaterialAssignment assignment)
        {
            _browserContext = assignment;
            if (IsBrowserLibraryMode)
                _ = RefreshBrowserItemsAsync();
            else
                FilterBrowserMaterials();
            IsMaterialBrowserOpen = true;
        }
        
        private void RunAutoMatch()
        {
            // Auto-match materials based on name similarity
            if (!IsZipLoaded || MaterialAssignments.Count == 0) return;

            int matchedCount = 0;
            foreach (var assignment in MaterialAssignments)
            {
                var match = FindBestMaterialMatch(assignment.SourceMaterialName, AvailableMaterials);
                if (match != null)
                {
                    assignment.SelectedMaterial = match;
                    matchedCount++;
                }
            }

            // Also refresh OriginalMaterialMappings so the group-level view stays consistent
            foreach (var mapping in OriginalMaterialMappings)
            {
                var match = FindBestMaterialMatch(mapping.OriginalName, AvailableMaterials);
                if (match != null)
                    mapping.SelectedMaterial = match;
            }

            StatusMessage = $"Auto-matched {matchedCount} of {MaterialAssignments.Count} materials";
            RebuildBundle();
        }

        // Finds the best matching material name 
        private static string FindBestMaterialMatch(string sourceName, IEnumerable<string> available)
        {
            if (string.IsNullOrEmpty(sourceName)) return null;

            var list = available.ToList();
            if (list.Count == 0) return null;

            // 1. Exact (case-insensitive)
            var exact = list.FirstOrDefault(m => m.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // 2. Normalized — strip _ - . spaces and compare ("black_glass" == "blackglass")
            string normSource = NormalizeMaterialName(sourceName);
            var normalized = list.FirstOrDefault(m =>
                NormalizeMaterialName(m).Equals(normSource, StringComparison.OrdinalIgnoreCase));
            if (normalized != null) return normalized;

            // 3. Token-set match with abbreviation awareness
            //    e.g. "black_glass" tokens {black, glass} match "glassblack" tokens {glass, black}
            //         "glass" token {glass} matches "gls" via abbreviation map
            var sourceTokens = TokenizeMaterialName(sourceName);
            if (sourceTokens.Length > 0)
            {
                var sourceVariantSets = sourceTokens
                    .Select(t => GetMaterialTokenVariants(t))
                    .ToArray();

                var tokenMatch = list
                    .Select(m => new { Material = m, Tokens = TokenizeMaterialName(m) })
                    .Where(x => x.Tokens.Length > 0)
                    .FirstOrDefault(x => AllSourceTokensMatch(sourceVariantSets, x.Tokens));

                if (tokenMatch != null) return tokenMatch.Material;
            }

            // 4. Partial containment fallback
            return list.FirstOrDefault(m =>
                m.Contains(sourceName, StringComparison.OrdinalIgnoreCase) ||
                sourceName.Contains(m, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeMaterialName(string name)
            => System.Text.RegularExpressions.Regex.Replace(name, @"[_\-\s\.]", "").ToLowerInvariant();

        private static string[] TokenizeMaterialName(string name)
            => name.Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);

        private static HashSet<string> GetMaterialTokenVariants(string token)
        {
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { token };
            // Expand: token is a full word → add abbreviations
            if (_materialAbbreviations.TryGetValue(token, out var abbrevs))
                foreach (var a in abbrevs) variants.Add(a);
            // Reverse-expand: token is an abbreviation → add the full word
            foreach (var kvp in _materialAbbreviations)
                if (kvp.Value.Any(a => a.Equals(token, StringComparison.OrdinalIgnoreCase)))
                    variants.Add(kvp.Key);
            return variants;
        }

        private static bool AllSourceTokensMatch(HashSet<string>[] sourceVariantSets, string[] targetTokens)
        {
            foreach (var variantSet in sourceVariantSets)
            {
                if (!targetTokens.Any(t => variantSet.Contains(t)))
                    return false;
            }
            return true;
        }
        
        [RelayCommand]
        private void CloseMaterialBrowser()
        {
            IsMaterialBrowserOpen = false;
            _browserContext = null;
        }
        
        [RelayCommand]
        private async Task ApplyBrowserSelection(string selection)
        {
            var selectedMaterial = selection;
            
            if (string.IsNullOrEmpty(selectedMaterial))
                selectedMaterial = BrowserActiveItems.FirstOrDefault();
            
            if (string.IsNullOrEmpty(selectedMaterial))
            {
                StatusMessage = "No material selected";
                return;
            }

            // Close browser first so the UI feels responsive
            IsMaterialBrowserOpen = false;
            var context = _browserContext;
            _browserContext = null;

            if (IsBrowserLibraryMode && _currentLibraryEntries.TryGetValue(selectedMaterial, out var libEntry))
            {
                // Import from the game library JSON into the working material library
                await ImportLibraryMaterialAsync(selectedMaterial, libEntry);
            }
            else if (!string.IsNullOrEmpty(MaterialZipPath) && _materialLibrary.TryGetValue(selectedMaterial, out var cached))
            {
                // Swap in real materialbin bytes from zip so Edit Parameters shows correct params
                await TrySwapBlobFromZipAsync(cached);
            }

            if (context is OriginalMaterialMapping mapping)
                mapping.SelectedMaterial = selectedMaterial;
            else if (context is MaterialAssignment assignment)
                assignment.SelectedMaterial = selectedMaterial;
        }

        // Imports a material from the game materials.json library into the working material library
        // so it can be used in the model just like a zip-sourced material.
        private async Task ImportLibraryMaterialAsync(string materialName, MaterialEntry entry)
        {
            if (string.IsNullOrEmpty(entry?.MaterialBlob)) return;

            // Don't re-import if already present
            if (_materialLibrary.ContainsKey(materialName)) return;

            try
            {
                byte[] blobData = await Task.Run(() =>
                {
                    string hex = entry.MaterialBlob.Replace(" ", "").Replace("-", "");
                    byte[] data = new byte[hex.Length / 2];
                    for (int i = 0; i < data.Length; i++)
                        data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    return data;
                });

                var newBlob = new MaterialBlob
                {
                    Tag = Bundle.TAG_BLOB_MaterialInstance,
                    CustomBlobData = blobData,
                    UncompressedSize = (uint)blobData.Length,
                    CompressedSize = (uint)blobData.Length
                };
                newBlob.Metadatas.Add(new NameMetadata
                {
                    Tag = BundleMetadata.TAG_METADATA_Name,
                    Name = materialName
                });
                newBlob.Metadatas.Add(new AtlasMetadata
                {
                    Tag = BundleMetadata.TAG_METADATA_Atlas,
                    Version = 2,
                    Unk = false,
                    UnkV2 = false
                });

                // Try to parse the inner bundle so Edit Parameters works
                if (blobData.Length >= 4)
                {
                    try
                    {
                        using var ms = new MemoryStream(blobData);
                        uint magic = new BinaryReader(ms).ReadUInt32();
                        ms.Position = 0;
                        if (magic == Bundle.BundleTag)
                        {
                            var innerBundle = new Bundle();
                            innerBundle.Load(ms);
                            newBlob.Bundle = innerBundle;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LibraryImport] Inner bundle parse warning for '{materialName}': {ex.Message}");
                    }
                }

                var cachedBlob = new CachedMaterialBlob
                {
                    Name = materialName,
                    RelativeZipPath = string.Empty,
                    Blob = newBlob
                };

                _materialLibrary[materialName] = cachedBlob;

                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!AvailableMaterials.Contains(materialName))
                        AvailableMaterials.Insert(0, materialName);
                    if (!Materials.Contains(materialName))
                        Materials.Insert(0, materialName);
                });

                System.Diagnostics.Debug.WriteLine($"[LibraryImport] Imported '{materialName}' from game library");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LibraryImport] Failed to import '{materialName}': {ex.Message}");
            }
        }

        // Game-aware materials.zip auto-load


        public async Task InitializeAsync()
        {
            var settings = await _settingsService.LoadAsync();
            IsAdvancedVlayBlobPatchEnabled = settings.EnableAdvancedVlayBlobPatch;

            // Guard: only auto-load the zip once per ViewModel lifetime.
            if (!_hasInitializedZip)
            {
                _hasInitializedZip = true;
                await TryAutoLoadMaterialZipAsync();
            }
        }

        [RelayCommand]
        private async Task ReloadFromConfiguredGameAsync()
        {
            _isManualOverride = false;
            _hasInitializedZip = false; // Allow TryAutoLoadMaterialZipAsync to re-run
            await TryAutoLoadMaterialZipAsync();
        }

        private async Task TryAutoLoadMaterialZipAsync()
        {
            if (_isManualOverride) return;

            var allPaths = await TryGetAllConfiguredMaterialsZipPathsAsync(CurrentGameTarget);

            MaterialZipOptions.Clear();
            _materialZipOptionPaths.Clear();
            OnPropertyChanged(nameof(HasMaterialZipDropdown));

            if (allPaths.Count == 0)
            {
                MaterialZipSourceLabel = "No configured zip — use Browse Zip or configure a game path in Setup";
                return;
            }

            // Build display names relative to the game root
            string gameRoot = (await TryGetConfiguredGamePathAsync(CurrentGameTarget)) ?? "";
            foreach (var path in allPaths)
            {
                string display = path;
                if (!string.IsNullOrEmpty(gameRoot) && path.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase))
                    display = path.Substring(gameRoot.Length).TrimStart('\\', '/');
                MaterialZipOptions.Add(display);
                _materialZipOptionPaths[display] = path;
            }
            OnPropertyChanged(nameof(HasMaterialZipDropdown));

            // Select first without triggering a redundant load via the property-changed handler
            _suppressZipSelectionChange = true;
            SelectedMaterialZipOption = MaterialZipOptions[0];
            _suppressZipSelectionChange = false;

            await LoadMaterialZipAsync(allPaths[0], isAutoLoad: true);
        }

        private async Task<string> TryGetConfiguredGamePathAsync(ForzaGameTarget target)
        {
            var settings = await _settingsService.LoadAsync();
            string gameId = ConversionService.GetGameSettingsId(target);
            if (!settings.GamePaths.TryGetValue(gameId, out string gameRoot) || string.IsNullOrWhiteSpace(gameRoot))
                return null;
            return Directory.Exists(gameRoot) ? gameRoot : null;
        }

        private async Task<List<string>> TryGetAllConfiguredMaterialsZipPathsAsync(ForzaGameTarget target)
        {
            string gameRoot = await TryGetConfiguredGamePathAsync(target);
            if (string.IsNullOrEmpty(gameRoot)) return new List<string>();

            // Search media subfolder first, then fall back to root
            string mediaPath = Path.Combine(gameRoot, "media");
            if (!Directory.Exists(mediaPath)) mediaPath = gameRoot;

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] zipNames = ["materials.zip", "Materials.zip"];
            foreach (var name in zipNames)
            {
                try
                {
                    foreach (var found in Directory.GetFiles(mediaPath, name, SearchOption.AllDirectories))
                        if (seen.Add(found)) result.Add(found);
                }
                catch { /* keep searching */ }
            }
            return result;
        }

        // Reads the actual .materialbin bytes from the current zip for the given cached entry
        // and swaps in a real parsed inner bundle so Edit Parameters shows correct parameters.
        private async Task TrySwapBlobFromZipAsync(CachedMaterialBlob cached)
        {
            if (cached == null || string.IsNullOrEmpty(MaterialZipPath) || string.IsNullOrEmpty(cached.RelativeZipPath))
                return;
            try
            {
                byte[] bytes = await Task.Run(() =>
                {
                    using var zip = new CustomZipFile(MaterialZipPath);
                    var entries = zip.GetEntries();
                    var entry = entries.FirstOrDefault(e =>
                        string.Equals(e.Name, cached.RelativeZipPath, StringComparison.OrdinalIgnoreCase));
                    return entry != null ? zip.ExtractToMemory(entry) : null;
                });

                if (bytes == null || bytes.Length < 4) return;

                using var ms = new MemoryStream(bytes);
                uint magic = new System.IO.BinaryReader(ms).ReadUInt32();
                ms.Position = 0;
                if (magic == Bundle.BundleTag)
                {
                    var innerBundle = new Bundle();
                    innerBundle.Load(ms);
                    // Swap the inner bundle so EditParameters shows real shader parameters
                    cached.Blob.Bundle = innerBundle;
                    System.Diagnostics.Debug.WriteLine($"[BlobSwap] Swapped inner bundle for '{cached.Name}': {innerBundle.Blobs.Count} blobs");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BlobSwap] Failed for '{cached?.Name}': {ex.Message}");
            }
        }

        // Applies the VLay blob patch after the bundle is built but before it's written.
        private async Task ApplyAdvancedVlayPatchAsync(Bundle bundle)
        {
            string gamePath = await TryGetConfiguredGamePathAsync(CurrentGameTarget);
            if (string.IsNullOrEmpty(gamePath))
            {
                StatusMessage = "Warning: Advanced VLay patch enabled but no game path configured in Setup — using fallback layout.";
                return;
            }

            var result = new ConversionResult { Success = true };
            var options = new ConversionOptions
            {
                EnableAdvancedVlayBlobPatch = true,
                TargetGamePath = gamePath
            };
            var modelbinService = new ModelbinConversionService();
            var target = CurrentGameTarget;
            await Task.Run(() => modelbinService.ApplyAdvancedVlayBlobPatch(bundle, target, result, options));

            if (result.Warnings.Count > 0)
                StatusMessage = $"VLay patch: {result.Warnings[0]}";
            else if (result.Log.Any(l => l.StartsWith("  Advanced VLay blob patch updated")))
                StatusMessage += " [VLay patched]";
        }
    }

    public partial class MaterialAssignment : ObservableObject
    {
        [ObservableProperty]
        private string _selectedMaterial;

        private Action<MaterialAssignment> _onMaterialChanged;
        private Action<MaterialAssignment> _onBrowseRequested;

        public string MeshName { get; set; }
        public string SourceMaterialName { get; set; }
        public ObservableCollection<string> AvailableMaterials { get; }
        
        private readonly Dictionary<string, CachedMaterialBlob> _materialLibrary;

        public MaterialAssignment(
            string meshName,
            string sourceMaterialName,
            ObservableCollection<string> availableMaterials,
            Action<MaterialAssignment> onMaterialChanged,
            Action<MaterialAssignment> onBrowseRequested,
            Dictionary<string, CachedMaterialBlob>? materialLibrary = null)
        {
            MeshName = meshName;
            SourceMaterialName = sourceMaterialName;
            AvailableMaterials = availableMaterials;
            _onMaterialChanged = onMaterialChanged;
            _onBrowseRequested = onBrowseRequested;
            _materialLibrary = materialLibrary;
        }

        partial void OnSelectedMaterialChanged(string value)
        {
            // Preload the material blob into memory when selected
            if (!string.IsNullOrEmpty(value) && _materialLibrary != null && _materialLibrary.TryGetValue(value, out var cachedMaterial))
            {
                System.Diagnostics.Debug.WriteLine($"Material '{value}' selected, preloading blob...");
                cachedMaterial.PreloadBlob();
            }
            
            _onMaterialChanged?.Invoke(this);
        }

        [RelayCommand]
        private async Task ImportJsonAsync()
        {
            if (_materialLibrary == null)
            {
                 App.ShowErrorDialog("Material library not initialized.");
                 return;
            }

            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".json");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    string jsonString = await File.ReadAllTextAsync(file.Path);
                    var materials = JsonSerializer.Deserialize(jsonString, MaterialJsonContext.Default.DictionaryStringMaterialEntry);

                    if (materials == null || materials.Count == 0)
                    {
                        App.ShowErrorDialog("No materials found in JSON file.");
                        return;
                    }

                    // For now, take the first material found in the JSON
                    var entryPair = materials.First();
                    string jsonMatName = entryPair.Key;
                    var entry = entryPair.Value;

                    if (string.IsNullOrEmpty(entry.MaterialBlob))
                    {
                        App.ShowErrorDialog($"Material blob data is missing for '{jsonMatName}'.");
                        return;
                    }

                    // Parse Hex Data (Blob)
                    string hex = entry.MaterialBlob.Replace(" ", "").Replace("-", "");
                    byte[] blobData = new byte[hex.Length / 2];
                    for (int i = 0; i < blobData.Length; i++) blobData[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

                    // Parse Metadata (Name) from MaterialMetaData hex if available
                    string metaName = jsonMatName;
                    if (!string.IsNullOrEmpty(entry.MaterialMetaData))
                    {
                        try 
                        {
                            string hexMeta = entry.MaterialMetaData.Replace(" ", "").Replace("-", "");
                            byte[] metaBytes = new byte[hexMeta.Length / 2];
                            for (int i=0; i<metaBytes.Length; i++) metaBytes[i] = Convert.ToByte(hexMeta.Substring(i*2, 2), 16);
                            
                            // Extracts name based on CreateFormattedMetadataHex structure:
                            // Header (14 bytes) -> SizeByte (1 byte) -> 0x00 -> NameBytes
                            if (metaBytes.Length > 16)
                            {
                                int sizeByte = metaBytes[14];
                                int nameLen = sizeByte - 8;
                                if (nameLen > 0 && 16 + nameLen <= metaBytes.Length)
                                {
                                    metaName = System.Text.Encoding.UTF8.GetString(metaBytes, 16, nameLen).TrimEnd('\0');
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                             System.Diagnostics.Debug.WriteLine($"[ImportJson] Metadata parse error: {ex.Message}");
                             // Fallback to key
                        }
                    }

                    // Create MaterialBlob
                    var newBlob = new MaterialBlob
                    {
                        Tag = Bundle.TAG_BLOB_MaterialInstance, // 0x4D617449
                        CustomBlobData = blobData,
                        UncompressedSize = (uint)blobData.Length,
                        CompressedSize = (uint)blobData.Length
                    };
                    
                    // Add Name Metadata (extracted or fallback)
                    newBlob.Metadatas.Add(new NameMetadata 
                    { 
                        Tag = BundleMetadata.TAG_METADATA_Name, 
                        Name = metaName 
                    });

                    // Add Atlas Metadata (Standard default)
                    newBlob.Metadatas.Add(new AtlasMetadata
                    {
                        Tag = BundleMetadata.TAG_METADATA_Atlas,
                        Version = 2,
                        Unk = false,
                        UnkV2 = false
                    });

                    // Try to parse inner bundle (for editing parameters)
                    if (blobData.Length >= 4)
                    {
                        try 
                        {
                            using var ms = new MemoryStream(blobData);
                            uint magic = new BinaryReader(ms).ReadUInt32();
                            ms.Position = 0;
                            if (magic == Bundle.BundleTag)
                            {
                                var newBundle = new Bundle();
                                newBundle.Load(ms);
                                newBlob.Bundle = newBundle;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ImportJson] Inner bundle parse warning: {ex.Message}");
                        }
                   }

                    // Create unique name
                    string importedName = $"Custom: {metaName} ({Guid.NewGuid().ToString().Substring(0,4)})";

                    // Register in library
                    var cached = new CachedMaterialBlob
                    {
                        Name = importedName,
                        RelativeZipPath = "", 
                        Blob = newBlob // Set our custom blob directly
                    };
                    
                    _materialLibrary[importedName] = cached;

                    // Update UI collection
                    App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!AvailableMaterials.Contains(importedName))
                        {
                            AvailableMaterials.Insert(0, importedName); // Add to top
                        }
                        SelectedMaterial = importedName;
                    });
                }
                catch (Exception ex)
                {
                    App.ShowErrorDialog($"Failed to import material JSON: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private void Browse()
        {
            _onBrowseRequested?.Invoke(this);
        }

        [RelayCommand]
        private void EditParameters()
        {
            System.Diagnostics.Debug.WriteLine($"[EditParameters] Button clicked for material: {SelectedMaterial}");
            
            if (string.IsNullOrEmpty(SelectedMaterial))
            {
                App.ShowErrorDialog("No material selected.");
                return;
            }
            
            if (_materialLibrary == null)
            {
                System.Diagnostics.Debug.WriteLine($"[EditParameters] Material library is null");
                App.ShowErrorDialog($"Material library is not initialized.\n\nPlease load a material zip file first.");
                return;
            }
            
            if (!_materialLibrary.TryGetValue(SelectedMaterial, out var cachedMaterial))
            {
                System.Diagnostics.Debug.WriteLine($"[EditParameters] Material '{SelectedMaterial}' not found in library");
                System.Diagnostics.Debug.WriteLine($"[EditParameters] Available materials: {string.Join(", ", _materialLibrary.Keys.Take(10))}...");
                App.ShowErrorDialog($"Material '{SelectedMaterial}' not found in library.\n\nPlease ensure the material zip is loaded and the material exists.");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[EditParameters] Cached material found, creating in-memory blob...");
            
            // Get the in-memory created blob (NOT loaded from file)
            var materialBlob = cachedMaterial.Blob;
            
            if (materialBlob == null)
            {
                System.Diagnostics.Debug.WriteLine($"[EditParameters] Failed to create MaterialBlob in memory");
                App.ShowErrorDialog($"Failed to create material '{SelectedMaterial}' in memory.\n\nThis is an internal error.");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[EditParameters] MaterialBlob created successfully");
            
            if (materialBlob.Bundle == null)
            {
                System.Diagnostics.Debug.WriteLine($"[EditParameters] MaterialBlob.Bundle is null");
                App.ShowErrorDialog($"Material '{SelectedMaterial}' has no inner bundle.\n\nThis is an internal error - the material structure is invalid.");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[EditParameters] MaterialBlob.Bundle found with {materialBlob.Bundle.Blobs.Count} blobs");
            
            var paramBlob = materialBlob.Bundle.Blobs.OfType<MaterialShaderParameterBlob>().FirstOrDefault();
            
            if (paramBlob == null)
            {
                System.Diagnostics.Debug.WriteLine($"[EditParameters] No MaterialShaderParameterBlob found");
                System.Diagnostics.Debug.WriteLine($"[EditParameters] Available blob types:");
                foreach (var blob in materialBlob.Bundle.Blobs)
                {
                    System.Diagnostics.Debug.WriteLine($"[EditParameters]   - {blob.GetType().Name} (Tag: 0x{blob.Tag:X8})");
                }
                
                App.ShowErrorDialog($"Material '{SelectedMaterial}' has no shader parameters blob.\n\nThis is an internal error - expected MaterialShaderParameterBlob was not created.");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[EditParameters] ? Found MaterialShaderParameterBlob with {paramBlob.Parameters.Count} parameters");
            System.Diagnostics.Debug.WriteLine($"[EditParameters] Navigating to parameter editor...");
            
            // Successfully found parameters - navigate to editor
            var vm = new MaterialShaderParametersViewModel(paramBlob, SelectedMaterial);
            
            try
            {
                // Attempt to find the RootFrame from MainWindow structure
                Frame rootFrame = null;
                
                // MainWindow content is likely a Grid (or Panel) containing the Frame
                if (App.MainWindow?.Content is Panel rootPanel)
                {
                    // Try to find the frame by checking children
                    rootFrame = rootPanel.Children.OfType<Frame>().FirstOrDefault();
                }
                else if (App.MainWindow?.Content is Frame frame)
                {
                    // Direct frame content (fallback)
                    rootFrame = frame;
                }

                if (rootFrame == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[EditParameters] RootFrame is null");
                    System.Diagnostics.Debug.WriteLine($"[EditParameters] App.MainWindow type: {App.MainWindow?.GetType().Name}");
                    System.Diagnostics.Debug.WriteLine($"[EditParameters] App.MainWindow.Content type: {App.MainWindow?.Content?.GetType().Name}");
                    App.ShowErrorDialog("Failed to navigate to parameter editor.\n\nCould not locate RootFrame in MainWindow.");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"[EditParameters] RootFrame found: {rootFrame.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"[EditParameters] RootFrame.Content type: {rootFrame.Content?.GetType().Name}");
                
                // Get the ShellPage from the RootFrame
                if (rootFrame.Content is not Views.ShellPage shellPage)
                {
                    System.Diagnostics.Debug.WriteLine($"[EditParameters] ShellPage cast failed, actual type: {rootFrame.Content?.GetType().FullName}");
                    App.ShowErrorDialog($"Failed to navigate to parameter editor.\n\nShellPage not found.\n\nActual type: {rootFrame.Content?.GetType().Name}");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"[EditParameters] ShellPage found successfully");
                
                // Use the public NavigateToPage method
                bool navigated = shellPage.NavigateToPage(typeof(MaterialShaderParametersPage), vm);
                System.Diagnostics.Debug.WriteLine($"[EditParameters] Navigation result: {navigated}");
                
                if (!navigated)
                {
                    App.ShowErrorDialog("Failed to navigate to parameter editor.\n\nNavigation returned false.\n\nThe page type may not be registered properly.");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[EditParameters] ? Navigation successful!");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditParameters] Exception during navigation: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[EditParameters] Stack trace: {ex.StackTrace}");
                App.ShowErrorDialog($"Failed to navigate to parameter editor.\n\nException: {ex.Message}");
            }
        }
    }

    // NEW: Class for mapping original MTL materials to Forza materials globally
    public partial class OriginalMaterialMapping : ObservableObject
    {
        public string OriginalName { get; }

        [ObservableProperty]
        private string _selectedMaterial;

        public ObservableCollection<string> AvailableMaterials { get; }
        
        private Action<string, string> _onChanged;
        private Action<OriginalMaterialMapping> _onBrowseRequested;

        public OriginalMaterialMapping(string originalName, ObservableCollection<string> availableMaterials, Action<string, string> onChanged, Action<OriginalMaterialMapping> onBrowseRequested)
        {
            OriginalName = originalName;
            AvailableMaterials = availableMaterials;
            _onChanged = onChanged;
            _onBrowseRequested = onBrowseRequested;
        }

        partial void OnSelectedMaterialChanged(string value)
        {
            _onChanged?.Invoke(OriginalName, value);
        }

        [RelayCommand]
        private void Browse()
        {
            _onBrowseRequested?.Invoke(this);
        }
    }
}
