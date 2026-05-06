using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace ForzaTechStudio.ViewModels
{
    public partial class MaterialIndexEntry : ObservableObject
    {
        private string _key = "";
        public string Key
        {
            get => _key;
            set => SetProperty(ref _key, value);
        }

        private ulong _value = 0;
        public ulong Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }


        public double ValueAsDouble
        {
            get => _value;
            set
            {
                var newVal = (ulong)value;
                if (_value != newVal)
                {
                    _value = newVal;
                    OnPropertyChanged(nameof(Value));
                    OnPropertyChanged(nameof(ValueAsDouble));
                }
            }
        }

        public MaterialIndexEntry() { }

        public MaterialIndexEntry(string key, ulong value)
        {
            Key = key;
            Value = value;
        }
    }


    // Represents an upgrade entry in an upgradable part.

    public partial class UpgradeEntry : ObservableObject
    {
        private ushort _version = 3;
        public ushort Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        private byte _level = 0;
        public byte Level
        {
            get => _level;
            set => SetProperty(ref _level, value);
        }

        private bool _isStock = false;
        public bool IsStock
        {
            get => _isStock;
            set => SetProperty(ref _isStock, value);
        }

        private int _id = 0;
        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private int _carBodyId = 0;
        public int CarBodyId
        {
            get => _carBodyId;
            set => SetProperty(ref _carBodyId, value);
        }

        private bool _parentIsStock = true;
        public bool ParentIsStock
        {
            get => _parentIsStock;
            set => SetProperty(ref _parentIsStock, value);
        }

        // AABB Bounds 
        private float _boundsMinX = -1f;
        public float BoundsMinX
        {
            get => _boundsMinX;
            set => SetProperty(ref _boundsMinX, value);
        }

        private float _boundsMinY = -1f;
        public float BoundsMinY
        {
            get => _boundsMinY;
            set => SetProperty(ref _boundsMinY, value);
        }

        private float _boundsMinZ = -1f;
        public float BoundsMinZ
        {
            get => _boundsMinZ;
            set => SetProperty(ref _boundsMinZ, value);
        }

        private float _boundsMaxX = 1f;
        public float BoundsMaxX
        {
            get => _boundsMaxX;
            set => SetProperty(ref _boundsMaxX, value);
        }

        private float _boundsMaxY = 1f;
        public float BoundsMaxY
        {
            get => _boundsMaxY;
            set => SetProperty(ref _boundsMaxY, value);
        }

        private float _boundsMaxZ = 1f;
        public float BoundsMaxZ
        {
            get => _boundsMaxZ;
            set => SetProperty(ref _boundsMaxZ, value);
        }

        // AABB W components for byte-parity 
        private float _boundsMinW = 0f;
        public float BoundsMinW
        {
            get => _boundsMinW;
            set => SetProperty(ref _boundsMinW, value);
        }

        private float _boundsMaxW = 0f;
        public float BoundsMaxW
        {
            get => _boundsMaxW;
            set => SetProperty(ref _boundsMaxW, value);
        }

        public UpgradeEntry()
        {
        }

        public UpgradeEntry(int id, byte level, bool isStock)
        {
            Id = id;
            Level = level;
            IsStock = isStock;
        }
    }


    // Represents an AO map info entry with all its data preserved

    public partial class AOMapInfoEntry : ObservableObject
    {
        private ushort _version = 3;
        public ushort Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        private string _path = "";
        public string Path
        {
            get => _path;
            set => SetProperty(ref _path, value);
        }

        private uint _partType = 0xFFFFFFFF; // Default to -1
        public uint PartType
        {
            get => _partType;
            set => SetProperty(ref _partType, value);
        }

        private int _partId = -1; // Default to -1
        public int PartId
        {
            get => _partId;
            set => SetProperty(ref _partId, value);
        }

        // For version >= 2
        private Guid _droppedModelInstanceGuid = Guid.Empty;
        public Guid DroppedModelInstanceGuid
        {
            get => _droppedModelInstanceGuid;
            set => SetProperty(ref _droppedModelInstanceGuid, value);
        }

        // For version < 2
        private short _boneIndex = 0;
        public short BoneIndex
        {
            get => _boneIndex;
            set => SetProperty(ref _boneIndex, value);
        }

        private bool _isDropped = false;
        public bool IsDropped
        {
            get => _isDropped;
            set => SetProperty(ref _isDropped, value);
        }

        private bool _isDefault = true;
        public bool IsDefault
        {
            get => _isDefault;
            set => SetProperty(ref _isDefault, value);
        }

        // For version >= 3
        private sbyte _lodTest = 0;
        public sbyte LodTest
        {
            get => _lodTest;
            set => SetProperty(ref _lodTest, value);
        }

        private sbyte _lodValue = 31;
        public sbyte LodValue
        {
            get => _lodValue;
            set => SetProperty(ref _lodValue, value);
        }

        public AOMapInfoEntry() { }

        public AOMapInfoEntry(string path)
        {
            Path = path;
            Version = 3;
            DroppedModelInstanceGuid = Guid.Empty;
            IsDefault = true;
            LodTest = 0;
            LodValue = 31;
        }
    }


    // Represents a model entry in the carbin structure with its AO map information

    public partial class CarbinModelEntry : ObservableObject
    {
        private string _modelFileName = "";
        public string ModelFileName
        {
            get => _modelFileName;
            set => SetProperty(ref _modelFileName, value);
        }

        private string _modelFullPath = "";
        public string ModelFullPath
        {
            get => _modelFullPath;
            set => SetProperty(ref _modelFullPath, value);
        }

        private string _aoSwatchbinFileName = "";
        public string AoSwatchbinFileName
        {
            get => _aoSwatchbinFileName;
            set => SetProperty(ref _aoSwatchbinFileName, value);
        }

        private string _aoSwatchbinFullPath = "";
        public string AoSwatchbinFullPath
        {
            get => _aoSwatchbinFullPath;
            set => SetProperty(ref _aoSwatchbinFullPath, value);
        }

        // Game paths - stored exactly as read from file
        private string _modelGamePath = "";
        public string ModelGamePath
        {
            get => _modelGamePath;
            set => SetProperty(ref _modelGamePath, value);
        }

        private string _aoSwatchbinGamePath = "";
        public string AoSwatchbinGamePath
        {
            get => _aoSwatchbinGamePath;
            set => SetProperty(ref _aoSwatchbinGamePath, value);
        }


        public ObservableCollection<MaterialIndexEntry> MaterialIndexes { get; } = new();

        //  AO Map Infos (for version >= 9) 
        public ObservableCollection<AOMapInfoEntry> AoMapInfos { get; } = new();

        // Bone Attachment
        private string _boneName = "<root>";
        public string BoneName
        {
            get => _boneName;
            set => SetProperty(ref _boneName, value);
        }

        private short _boneId = 0;
        public short BoneId
        {
            get => _boneId;
            set => SetProperty(ref _boneId, value);
        }

        private bool _snapToParent = false;
        public bool SnapToParent
        {
            get => _snapToParent;
            set => SetProperty(ref _snapToParent, value);
        }

        // Draw Groups 
        private bool _drawGroupExterior = true;
        public bool DrawGroupExterior
        {
            get => _drawGroupExterior;
            set => SetProperty(ref _drawGroupExterior, value);
        }

        private bool _drawGroupCockpit = false;
        public bool DrawGroupCockpit
        {
            get => _drawGroupCockpit;
            set => SetProperty(ref _drawGroupCockpit, value);
        }

        private bool _drawGroupShadow = true;
        public bool DrawGroupShadow
        {
            get => _drawGroupShadow;
            set => SetProperty(ref _drawGroupShadow, value);
        }

        private bool _drawGroupHood = false;
        public bool DrawGroupHood
        {
            get => _drawGroupHood;
            set => SetProperty(ref _drawGroupHood, value);
        }

        private bool _drawGroupWindshieldReflection = false;
        public bool DrawGroupWindshieldReflection
        {
            get => _drawGroupWindshieldReflection;
            set => SetProperty(ref _drawGroupWindshieldReflection, value);
        }

        private bool _drawGroupDriverlessCockpit = false;
        public bool DrawGroupDriverlessCockpit
        {
            get => _drawGroupDriverlessCockpit;
            set => SetProperty(ref _drawGroupDriverlessCockpit, value);
        }

        private bool _drawGroupWindshieldReflectionDriverlessCockpit = false;
        public bool DrawGroupWindshieldReflectionDriverlessCockpit
        {
            get => _drawGroupWindshieldReflectionDriverlessCockpit;
            set => SetProperty(ref _drawGroupWindshieldReflectionDriverlessCockpit, value);
        }

        private bool _drawGroupProxyLOD = false;
        public bool DrawGroupProxyLOD
        {
            get => _drawGroupProxyLOD;
            set => SetProperty(ref _drawGroupProxyLOD, value);
        }

        // Droppable Part Settings, crank it 
        private bool _isDroppable = false;
        public bool IsDroppable
        {
            get => _isDroppable;
            set => SetProperty(ref _isDroppable, value);
        }

        private float _dropValue = 0.0f;
        public float DropValue
        {
            get => _dropValue;
            set => SetProperty(ref _dropValue, value);
        }

        private uint _dropPartId = 0;
        public uint DropPartId
        {
            get => _dropPartId;
            set
            {
                if (SetProperty(ref _dropPartId, value))
                    OnPropertyChanged(nameof(DropPartIdHexString));
            }
        }

        public string DropPartIdHexString
        {
            get => $"0x{DropPartId:X8}";
            set
            {
                var s = (value ?? "").Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    s = s.Substring(2);
                if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
                    DropPartId = parsed;
            }
        }

        // Break Amount
        private float _breakAmount = 0.0f;
        public float BreakAmount
        {
            get => _breakAmount;
            set => SetProperty(ref _breakAmount, value);
        }

        // Render Parameters 
        private bool _isInteriorWindshield = false;
        public bool IsInteriorWindshield
        {
            get => _isInteriorWindshield;
            set => SetProperty(ref _isInteriorWindshield, value);
        }

        private bool _receivesImpactMask = false;
        public bool ReceivesImpactMask
        {
            get => _receivesImpactMask;
            set => SetProperty(ref _receivesImpactMask, value);
        }

        private bool _receivesSplatterMask = false;
        public bool ReceivesSplatterMask
        {
            get => _receivesSplatterMask;
            set => SetProperty(ref _receivesSplatterMask, value);
        }

        private bool _receivesDamage = true;
        public bool ReceivesDamage
        {
            get => _receivesDamage;
            set => SetProperty(ref _receivesDamage, value);
        }

        private bool _receivesDirt = true;
        public bool ReceivesDirt
        {
            get => _receivesDirt;
            set => SetProperty(ref _receivesDirt, value);
        }

        private bool _receivesOil = false;
        public bool ReceivesOil
        {
            get => _receivesOil;
            set => SetProperty(ref _receivesOil, value);
        }

        private bool _receivesRubber = false;
        public bool ReceivesRubber
        {
            get => _receivesRubber;
            set => SetProperty(ref _receivesRubber, value);
        }

        private bool _receivesRain = false;
        public bool ReceivesRain
        {
            get => _receivesRain;
            set => SetProperty(ref _receivesRain, value);
        }

        // Assembly Name (v12+)
        private string _assemblyName = "";
        public string AssemblyName
        {
            get => _assemblyName;
            set => SetProperty(ref _assemblyName, value);
        }

        // GUIDs for preserving when reading/writing
        private Guid _guidV13 = Guid.Empty;
        public Guid GuidV13
        {
            get => _guidV13;
            set => SetProperty(ref _guidV13, value);
        }

        private Guid _dropGuidV14 = Guid.Empty;
        public Guid DropGuidV14
        {
            get => _dropGuidV14;
            set => SetProperty(ref _dropGuidV14, value);
        }

        private uint _aoMapInfoIdV14 = 0;
        public uint AoMapInfoIdV14
        {
            get => _aoMapInfoIdV14;
            set => SetProperty(ref _aoMapInfoIdV14, value);
        }

        // Damage GUIDs 
        public ObservableCollection<Guid> DamageGuids { get; } = new();

        // FM2023 specific (v20+) 
        private bool _isInterior = false;
        public bool IsInterior
        {
            get => _isInterior;
            set => SetProperty(ref _isInterior, value);
        }

        private bool _isLeftSideWindow = false;
        public bool IsLeftSideWindow
        {
            get => _isLeftSideWindow;
            set => SetProperty(ref _isLeftSideWindow, value);
        }

        private bool _isRightSideWindow = false;
        public bool IsRightSideWindow
        {
            get => _isRightSideWindow;
            set => SetProperty(ref _isRightSideWindow, value);
        }

        private bool _isNascarWiper = false;
        public bool IsNascarWiper
        {
            get => _isNascarWiper;
            set => SetProperty(ref _isNascarWiper, value);
        }

        private bool _isLicensePlate = false;
        public bool IsLicensePlate
        {
            get => _isLicensePlate;
            set => SetProperty(ref _isLicensePlate, value);
        }

        // Per-model LOD Flags 
        private bool _modelLodFlagLODS = true;
        public bool ModelLodFlagLODS
        {
            get => _modelLodFlagLODS;
            set => SetProperty(ref _modelLodFlagLODS, value);
        }

        private bool _modelLodFlagLOD0 = true;
        public bool ModelLodFlagLOD0
        {
            get => _modelLodFlagLOD0;
            set => SetProperty(ref _modelLodFlagLOD0, value);
        }

        private bool _modelLodFlagLOD1 = true;
        public bool ModelLodFlagLOD1
        {
            get => _modelLodFlagLOD1;
            set => SetProperty(ref _modelLodFlagLOD1, value);
        }

        private bool _modelLodFlagLOD2 = true;
        public bool ModelLodFlagLOD2
        {
            get => _modelLodFlagLOD2;
            set => SetProperty(ref _modelLodFlagLOD2, value);
        }

        private bool _modelLodFlagLOD3 = true;
        public bool ModelLodFlagLOD3
        {
            get => _modelLodFlagLOD3;
            set => SetProperty(ref _modelLodFlagLOD3, value);
        }

        private bool _modelLodFlagLOD4 = true;
        public bool ModelLodFlagLOD4
        {
            get => _modelLodFlagLOD4;
            set => SetProperty(ref _modelLodFlagLOD4, value);
        }

        private bool _modelLodFlagLOD5 = false;
        public bool ModelLodFlagLOD5
        {
            get => _modelLodFlagLOD5;
            set => SetProperty(ref _modelLodFlagLOD5, value);
        }

        // Transform Matrix (Full 4x4 matrix for preserving rotation) 
        private Matrix4x4 _transformMatrix = Matrix4x4.Identity;
        public Matrix4x4 TransformMatrix
        {
            get => _transformMatrix;
            set
            {
                if (SetProperty(ref _transformMatrix, value))
                {
                    // Update individual position/scale properties from matrix for UI binding
                    _positionX = value.M41;
                    _positionY = value.M42;
                    _positionZ = value.M43;
                    _scale = value.M11; // Assuming uniform scale

                    OnPropertyChanged(nameof(PositionX));
                    OnPropertyChanged(nameof(PositionY));
                    OnPropertyChanged(nameof(PositionZ));
                    OnPropertyChanged(nameof(Scale));
                }
            }
        }

        // Transform (Position/Scale) - UI binding properties 
        private float _positionX = 0f;
        public float PositionX
        {
            get => _positionX;
            set
            {
                if (SetProperty(ref _positionX, value))
                {
                    // Update matrix when individual property changes
                    _transformMatrix.M41 = value;
                    OnPropertyChanged(nameof(TransformMatrix));
                }
            }
        }

        private float _positionY = 0f;
        public float PositionY
        {
            get => _positionY;
            set
            {
                if (SetProperty(ref _positionY, value))
                {
                    _transformMatrix.M42 = value;
                    OnPropertyChanged(nameof(TransformMatrix));
                }
            }
        }

        private float _positionZ = 0f;
        public float PositionZ
        {
            get => _positionZ;
            set
            {
                if (SetProperty(ref _positionZ, value))
                {
                    _transformMatrix.M43 = value;
                    OnPropertyChanged(nameof(TransformMatrix));
                }
            }
        }

        private float _scale = 1f;
        public float Scale
        {
            get => _scale;
            set
            {
                if (SetProperty(ref _scale, value))
                {
                    // Update matrix diagonal (assuming uniform scale)
                    _transformMatrix.M11 = value;
                    _transformMatrix.M22 = value;
                    _transformMatrix.M33 = value;
                    OnPropertyChanged(nameof(TransformMatrix));
                }
            }
        }

        // Horizon-specific fields 
        private byte _horizonId = 0;
        public byte HorizonId
        {
            get => _horizonId;
            set => SetProperty(ref _horizonId, value);
        }

        private uint _horizonUnkV18 = 1;
        public uint HorizonUnkV18
        {
            get => _horizonUnkV18;
            set => SetProperty(ref _horizonUnkV18, value);
        }

        // Horizon v15 field - bunch of unknown flags, oh well
        private int _horizonUnkV15 = 0;
        public int HorizonUnkV15
        {
            get => _horizonUnkV15;
            set => SetProperty(ref _horizonUnkV15, value);
        }

        // Motorsport v18/v19 string fields - more unknown data, preserved for round trip but not editable in UI since we don't know what they do type shit
        private string _motorsportUnkV18 = "";
        public string MotorsportUnkV18
        {
            get => _motorsportUnkV18;
            set => SetProperty(ref _motorsportUnkV18, value);
        }

        private string _motorsportUnkV19 = "";
        public string MotorsportUnkV19
        {
            get => _motorsportUnkV19;
            set => SetProperty(ref _motorsportUnkV19, value);
        }


        private MaterialIndexEntry? _selectedMaterialIndex;
        public MaterialIndexEntry? SelectedMaterialIndex
        {
            get => _selectedMaterialIndex;
            set => SetProperty(ref _selectedMaterialIndex, value);
        }

        // Upgrade IDs (for shared models in upgradable parts) 
        public ObservableCollection<int> UpgradeIds { get; } = new();
        public ObservableCollection<UpgradeIdWrapper> UpgradeIdWrappers { get; } = new();

        private UpgradeIdWrapper? _selectedUpgradeIdWrapper;
        public UpgradeIdWrapper? SelectedUpgradeIdWrapper
        {
            get => _selectedUpgradeIdWrapper;
            set => SetProperty(ref _selectedUpgradeIdWrapper, value);
        }

        public ulong InternalId { get; set; }

        // Material Overrides (preserved for byte-parity, version >= 2)
        // Key = material name, Value = raw binary blob
        public System.Collections.Generic.Dictionary<string, byte[]> MaterialOverrides { get; } = new();

        // Motorsport Proxy LOD ID (v17+) 
        private byte _proxyLodId = 0;
        public byte ProxyLodId
        {
            get => _proxyLodId;
            set => SetProperty(ref _proxyLodId, value);
        }

        // Raw Draw Groups value for byte-parity
        private uint _rawDrawGroupsValue = 0;
        public uint RawDrawGroupsValue
        {
            get => _rawDrawGroupsValue;
            set => SetProperty(ref _rawDrawGroupsValue, value);
        }

        // Original model version (preserved for round-trip) 
        private ushort _originalModelVersion = 0;
        public ushort OriginalModelVersion
        {
            get => _originalModelVersion;
            set => SetProperty(ref _originalModelVersion, value);
        }

        public CarbinModelEntry()
        {
        }

        public CarbinModelEntry(string modelPath, string sceneName)
        {
            ModelFullPath = modelPath;
            ModelFileName = System.IO.Path.GetFileName(modelPath);
            UpdateGamePaths(sceneName);
        }

        public CarbinModelEntry(string path, string defaultName, bool isFromViewer)
        {
            ModelGamePath = path;
            ModelFileName = System.IO.Path.GetFileName(path.Replace("game:\\", "").Replace("game:/", ""));

            // Default assembly name based on file name
            AssemblyName = !string.IsNullOrEmpty(ModelFileName) ? System.IO.Path.GetFileNameWithoutExtension(ModelFileName) : defaultName;
        }

        public void UpdateGamePaths(string sceneName)
        {
            // Only update paths if they're empty (new model being added)
            // Don't overwrite paths that were loaded from file
            if (string.IsNullOrEmpty(ModelGamePath) && !string.IsNullOrEmpty(ModelFileName) && !string.IsNullOrEmpty(sceneName))
            {
                ModelGamePath = $@"game:\media\cars\{sceneName}\scene\{ModelFileName}";
            }

            if (string.IsNullOrEmpty(AoSwatchbinGamePath) && !string.IsNullOrEmpty(AoSwatchbinFileName) && !string.IsNullOrEmpty(sceneName))
            {
                AoSwatchbinGamePath = $@"game:\media\cars\{sceneName}\textures\ao\swatches\{AoSwatchbinFileName}";
            }
        }

        public DrawGroupFlags GetDrawGroupFlags()
        {
            DrawGroupFlags flags = DrawGroupFlags.None;
            if (DrawGroupExterior) flags |= DrawGroupFlags.Exterior;
            if (DrawGroupCockpit) flags |= DrawGroupFlags.Cockpit;
            if (DrawGroupShadow) flags |= DrawGroupFlags.Shadow;
            if (DrawGroupHood) flags |= DrawGroupFlags.Hood;
            if (DrawGroupWindshieldReflection) flags |= DrawGroupFlags.WindshieldReflection;
            if (DrawGroupDriverlessCockpit) flags |= DrawGroupFlags.DriverlessCockpit;
            if (DrawGroupWindshieldReflectionDriverlessCockpit) flags |= DrawGroupFlags.WindshieldReflectionDriverlessCockpit;
            if (DrawGroupProxyLOD) flags |= DrawGroupFlags.ProxyLOD;
            return flags;
        }

        public LODFlags GetModelLODFlags()
        {
            LODFlags flags = LODFlags.None;
            if (ModelLodFlagLODS) flags |= LODFlags.LODS;
            if (ModelLodFlagLOD0) flags |= LODFlags.LOD0;
            if (ModelLodFlagLOD1) flags |= LODFlags.LOD1;
            if (ModelLodFlagLOD2) flags |= LODFlags.LOD2;
            if (ModelLodFlagLOD3) flags |= LODFlags.LOD3;
            if (ModelLodFlagLOD4) flags |= LODFlags.LOD4;
            if (ModelLodFlagLOD5) flags |= LODFlags.LOD5;
            return flags;
        }

        public void AddMaterialIndex()
        {
            MaterialIndexes.Add(new MaterialIndexEntry("material_name", 0));
        }

        public void RemoveMaterialIndex()
        {
            if (SelectedMaterialIndex != null)
            {
                MaterialIndexes.Remove(SelectedMaterialIndex);
                SelectedMaterialIndex = MaterialIndexes.FirstOrDefault();
            }
        }
    }


    // Represents a part in the carbin structure (Non-Upgradable or Upgradable)

    public partial class CarbinPartEntry : ObservableObject
    {
        private string _partTypeName = "CCarParts_CarBody";
        public string PartTypeName
        {
            get => _partTypeName;
            set
            {
                if (SetProperty(ref _partTypeName, value))
                {
                    // Update the enum when the name changes
                    if (Enum.TryParse<CCarPartsEnum>(value, out var enumValue))
                    {
                        _partType = enumValue;
                        OnPropertyChanged(nameof(PartType));
                    }
                }
            }
        }

        private CCarPartsEnum _partType = CCarPartsEnum.CCarParts_CarBody;
        public CCarPartsEnum PartType
        {
            get => _partType;
            set => SetProperty(ref _partType, value);
        }

        // AABB Bounds 
        private float _boundsMinX = -1f;
        public float BoundsMinX
        {
            get => _boundsMinX;
            set => SetProperty(ref _boundsMinX, value);
        }

        private float _boundsMinY = -1f;
        public float BoundsMinY
        {
            get => _boundsMinY;
            set => SetProperty(ref _boundsMinY, value);
        }

        private float _boundsMinZ = -1f;
        public float BoundsMinZ
        {
            get => _boundsMinZ;
            set => SetProperty(ref _boundsMinZ, value);
        }

        private float _boundsMaxX = 1f;
        public float BoundsMaxX
        {
            get => _boundsMaxX;
            set => SetProperty(ref _boundsMaxX, value);
        }

        private float _boundsMaxY = 1f;
        public float BoundsMaxY
        {
            get => _boundsMaxY;
            set => SetProperty(ref _boundsMaxY, value);
        }

        private float _boundsMaxZ = 1f;
        public float BoundsMaxZ
        {
            get => _boundsMaxZ;
            set => SetProperty(ref _boundsMaxZ, value);
        }

        // AABB W components for byte-parity
        private float _boundsMinW = 0f;
        public float BoundsMinW
        {
            get => _boundsMinW;
            set => SetProperty(ref _boundsMinW, value);
        }

        private float _boundsMaxW = 0f;
        public float BoundsMaxW
        {
            get => _boundsMaxW;
            set => SetProperty(ref _boundsMaxW, value);
        }

        public ObservableCollection<CarbinModelEntry> Models { get; } = new();

        // Upgrades collection - only used for upgradable parts
        public ObservableCollection<UpgradeEntry> Upgrades { get; } = new();

        // Original version info preserved for byte-parity round-trip 
        private ushort _originalPartVersion = 0;
        public ushort OriginalPartVersion
        {
            get => _originalPartVersion;
            set => SetProperty(ref _originalPartVersion, value);
        }

        private ushort _originalUpgradablePartVersion = 0;
        public ushort OriginalUpgradablePartVersion
        {
            get => _originalUpgradablePartVersion;
            set => SetProperty(ref _originalUpgradablePartVersion, value);
        }

        private uint _originalPartTypeUint = 0;
        public uint OriginalPartTypeUint
        {
            get => _originalPartTypeUint;
            set => SetProperty(ref _originalPartTypeUint, value);
        }

        private readonly Action<string> _onSceneNameChanged;

        public CarbinPartEntry(Action<string> onSceneNameChanged)
        {
            _onSceneNameChanged = onSceneNameChanged;
        }

        public void UpdateModelGamePaths(string sceneName)
        {
            foreach (var model in Models)
            {
                model.UpdateGamePaths(sceneName);
            }
        }
    }


    // Wrapper class for UpgradeId to enable two-way binding in WinUI 3 ItemsControl

    public partial class UpgradeIdWrapper : ObservableObject
    {
        [ObservableProperty]
        private int _value;

        // For NumberBox binding - converts to/from double
        public double ValueAsDouble
        {
            get => _value;
            set
            {
                var newVal = (int)value;
                if (_value != newVal)
                {
                    _value = newVal;
                    OnPropertyChanged(nameof(Value));
                    OnPropertyChanged(nameof(ValueAsDouble));
                }
            }
        }
        
        public UpgradeIdWrapper(int value)
        {
            _value = value;
        }
    }


    // Car parts enumeration matching the carbin format.

    public enum CCarPartsEnum : uint
    {
        CCarParts_Engine = 0,
        CCarParts_Drivetrain = 1,
        CCarParts_CarBody = 2,
        CCarParts_Motor = 3,
        CCarParts_Brakes = 4,
        CCarParts_SpringDamper = 5,
        CCarParts_AntiSwayFront = 6,
        CCarParts_AntiSwayRear = 7,
        CCarParts_TireCompound = 8,
        CCarParts_RearWing = 9,
        CCarParts_RimSizeFront = 10,
        CCarParts_RimSizeRear = 11,
        CCarParts_Camshaft = 12,
        CCarParts_Valves = 13,
        CCarParts_Displacement = 14,
        CCarParts_PistonsCompression = 15,
        CCarParts_FuelSystem = 16,
        CCarParts_Ignition = 17,
        CCarParts_Exhaust = 18,
        CCarParts_Intake = 19,
        CCarParts_Flywheel = 20,
        CCarParts_Manifold = 21,
        CCarParts_RestrictorPlate = 22,
        CCarParts_OilCooling = 23,
        CCarParts_SingleTurbo = 24,
        CCarParts_TwinTurbo = 25,
        CCarParts_QuadTurbo = 26,
        CCarParts_SuperchargerCSC = 27,
        CCarParts_SuperchargerDSC = 28,
        CCarParts_Intercooler = 29,
        CCarParts_Clutch = 30,
        CCarParts_Transmission = 31,
        CCarParts_Driveline = 32,
        CCarParts_Differential = 33,
        CCarParts_FrontBumper = 34,
        CCarParts_RearBumper = 35,
        CCarParts_Hood = 36,
        CCarParts_SideSkirts = 37,
        CCarParts_TireWidthFront = 38,
        CCarParts_TireWidthRear = 39,
        CCarParts_WeightReduction = 40,
        CCarParts_ChassisStiffness = 41,
        CCarParts_Ballast = 42,
        CCarParts_MotorParts = 43,
        CCarParts_WheelStyle = 44,
        CCarParts_Aspiration = 45,
    }

 
    // Game series

    public enum GameSeries
    {
        ForzaMotorsport5 = 0,
        ForzaMotorsport6 = 1,
        ForzaMotorsport7 = 2,
        ForzaMotorsport2023 = 3,
        ForzaHorizon2 = 4,
        ForzaHorizon3 = 5,
        ForzaHorizon4 = 6,
        ForzaHorizon5 = 7,
        ForzaHorizon6 = 8, // Future game, not released yet but included for completeness
    }


    // Draw group flags for model rendering.

    [Flags]
    public enum DrawGroupFlags : uint
    {
        None = 0,
        Exterior = 1 << 0,
        Cockpit = 1 << 1,
        Shadow = 1 << 2,
        Hood = 1 << 3,
        WindshieldReflection = 1 << 4,
        DriverlessCockpit = 1 << 5,
        WindshieldReflectionDriverlessCockpit = 1 << 6,
        ProxyLOD = 1 << 7,
    }


    // LOD flags for level of detail selection.

    [Flags]
    public enum LODFlags : ushort
    {
        None = 0,
        LODS = 1 << 0,
        LOD0 = 1 << 1,
        LOD1 = 1 << 2,
        LOD2 = 1 << 3,
        LOD3 = 1 << 4,
        LOD4 = 1 << 5,
        LOD5 = 1 << 6,
    }
}
