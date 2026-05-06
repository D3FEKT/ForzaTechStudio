using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace ForzaTechStudio.ViewModels
{
    public partial class CarbinEditorViewModel : ObservableObject
    {
        // Content Visibility
        private bool _isContentVisible = false;
        public bool IsContentVisible
        {
            get => _isContentVisible;
            set => SetProperty(ref _isContentVisible, value);
        }

        // Status
        private string _statusMessage = "Waiting..";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isBusy = false;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsNotBusy));
                }
            }
        }

        public bool IsNotBusy => !IsBusy;

        // Loaded File Info
        private string _loadedFilePath = "";
        public string LoadedFilePath
        {
            get => _loadedFilePath;
            set => SetProperty(ref _loadedFilePath, value);
        }

        private string _loadedFileName = "";
        public string LoadedFileName
        {
            get => _loadedFileName;
            set => SetProperty(ref _loadedFileName, value);
        }

        // Detected Version Info
        private ushort _detectedSceneVersion = 0;
        public ushort DetectedSceneVersion
        {
            get => _detectedSceneVersion;
            set => SetProperty(ref _detectedSceneVersion, value);
        }

        private ushort _detectedModelVersion = 0;
        public ushort DetectedModelVersion
        {
            get => _detectedModelVersion;
            set => SetProperty(ref _detectedModelVersion, value);
        }

        private bool _isHorizon = true;
        public bool IsHorizon
        {
            get => _isHorizon;
            set => SetProperty(ref _isHorizon, value);
        }

        // Version Selection
        public ObservableCollection<string> GameVersions { get; } = new()
        {
            "Forza Motorsport 5 (Scene v5, Model v14)",
            "Forza Motorsport 6/7 (Scene v5, Model v17)",
            "Forza Motorsport 2023 (Scene v10-11, Model v21)",
            "Forza Horizon 2 (Scene v5, Model v15)",
            "Forza Horizon 3/4 (Scene v5, Model v16)",
            "Forza Horizon 5 (Scene v6, Model v18)",
        };

        private int _selectedVersionIndex = 5;
        public int SelectedVersionIndex
        {
            get => _selectedVersionIndex;
            set => SetProperty(ref _selectedVersionIndex, value);
        }

        // Scene Properties
        private string _sceneName = "";
        public string SceneName
        {
            get => _sceneName;
            set => SetProperty(ref _sceneName, value);
        }

        private string _mediaName = "";
        public string MediaName
        {
            get => _mediaName;
            set => SetProperty(ref _mediaName, value);
        }

        private string _skeletonPath = "";
        public string SkeletonPath
        {
            get => _skeletonPath;
            set => SetProperty(ref _skeletonPath, value);
        }

        private uint _ordinal = 0;
        public uint Ordinal
        {
            get => _ordinal;
            set => SetProperty(ref _ordinal, value);
        }

        // Build Options
        private bool _buildStrict = false;
        public bool BuildStrict
        {
            get => _buildStrict;
            set => SetProperty(ref _buildStrict, value);
        }

        private Guid _buildGuid = Guid.Empty;
        public Guid BuildGuid
        {
            get => _buildGuid;
            set => SetProperty(ref _buildGuid, value);
        }

        // LOD Flags (Scene-level)
        private bool _lodFlagLODS = true;
        public bool LodFlagLODS
        {
            get => _lodFlagLODS;
            set => SetProperty(ref _lodFlagLODS, value);
        }

        private bool _lodFlagLOD0 = true;
        public bool LodFlagLOD0
        {
            get => _lodFlagLOD0;
            set => SetProperty(ref _lodFlagLOD0, value);
        }

        private bool _lodFlagLOD1 = true;
        public bool LodFlagLOD1
        {
            get => _lodFlagLOD1;
            set => SetProperty(ref _lodFlagLOD1, value);
        }

        private bool _lodFlagLOD2 = true;
        public bool LodFlagLOD2
        {
            get => _lodFlagLOD2;
            set => SetProperty(ref _lodFlagLOD2, value);
        }

        private bool _lodFlagLOD3 = true;
        public bool LodFlagLOD3
        {
            get => _lodFlagLOD3;
            set => SetProperty(ref _lodFlagLOD3, value);
        }

        private bool _lodFlagLOD4 = true;
        public bool LodFlagLOD4
        {
            get => _lodFlagLOD4;
            set => SetProperty(ref _lodFlagLOD4, value);
        }

        private bool _lodFlagLOD5 = false;
        public bool LodFlagLOD5
        {
            get => _lodFlagLOD5;
            set => SetProperty(ref _lodFlagLOD5, value);
        }

        // Non-Upgradable Parts
        public ObservableCollection<CarbinPartEntry> NonUpgradableParts { get; } = new();

        private CarbinPartEntry? _selectedNonUpgradablePart;
        public CarbinPartEntry? SelectedNonUpgradablePart
        {
            get => _selectedNonUpgradablePart;
            set
            {
                if (SetProperty(ref _selectedNonUpgradablePart, value))
                {
                    OnPropertyChanged(nameof(HasSelectedNonUpgradablePart));
                    SelectedNonUpgradableModel = value?.Models.FirstOrDefault();
                }
            }
        }

        public bool HasSelectedNonUpgradablePart => SelectedNonUpgradablePart != null;

        private CarbinModelEntry? _selectedNonUpgradableModel;
        public CarbinModelEntry? SelectedNonUpgradableModel
        {
            get => _selectedNonUpgradableModel;
            set
            {
                if (SetProperty(ref _selectedNonUpgradableModel, value))
                {
                    OnPropertyChanged(nameof(HasSelectedNonUpgradableModel));
                }
            }
        }

        public bool HasSelectedNonUpgradableModel => SelectedNonUpgradableModel != null;

        // Upgradable Parts
        public ObservableCollection<CarbinPartEntry> UpgradableParts { get; } = new();

        private CarbinPartEntry? _selectedUpgradablePart;
        public CarbinPartEntry? SelectedUpgradablePart
        {
            get => _selectedUpgradablePart;
            set
            {
                if (SetProperty(ref _selectedUpgradablePart, value))
                {
                    OnPropertyChanged(nameof(HasSelectedUpgradablePart));
                    SelectedUpgradableModel = value?.Models.FirstOrDefault();
                    SelectedUpgrade = value?.Upgrades.FirstOrDefault();
                }
            }
        }

        public bool HasSelectedUpgradablePart => SelectedUpgradablePart != null;

        private CarbinModelEntry? _selectedUpgradableModel;
        public CarbinModelEntry? SelectedUpgradableModel
        {
            get => _selectedUpgradableModel;
            set
            {
                if (SetProperty(ref _selectedUpgradableModel, value))
                {
                    OnPropertyChanged(nameof(HasSelectedUpgradableModel));
                }
            }
        }

        public bool HasSelectedUpgradableModel => SelectedUpgradableModel != null;

        // Upgrades
        private UpgradeEntry? _selectedUpgrade;
        public UpgradeEntry? SelectedUpgrade
        {
            get => _selectedUpgrade;
            set
            {
                if (SetProperty(ref _selectedUpgrade, value))
                {
                    OnPropertyChanged(nameof(HasSelectedUpgrade));
                }
            }
        }

        public bool HasSelectedUpgrade => SelectedUpgrade != null;

        // Part Types for ComboBox
        public ObservableCollection<string> PartTypeNames { get; } = new();

        // Track current parsing state for error reporting
        private long _lastFilePosition = 0;
        private string _lastParsingContext = "";

        public CarbinEditorViewModel()
        {
            foreach (var partType in Enum.GetValues<CCarPartsEnum>())
            {
                PartTypeNames.Add(partType.ToString());
            }
        }

        private void ClearAll()
        {
            IsContentVisible = false;

            SelectedNonUpgradablePart = null;
            SelectedUpgradablePart = null;
            SelectedNonUpgradableModel = null;
            SelectedUpgradableModel = null;
            SelectedUpgrade = null;

            foreach (var part in NonUpgradableParts)
            {
                part.Models.Clear();
            }

            foreach (var part in UpgradableParts)
            {
                part.Models.Clear();
                part.Upgrades.Clear();
            }

            NonUpgradableParts.Clear();
            UpgradableParts.Clear();

            LoadedFilePath = "";
            LoadedFileName = "";
            DetectedSceneVersion = 0;
            DetectedModelVersion = 0;
            IsHorizon = true;
            SceneName = "";
            MediaName = "";
            SkeletonPath = "";
            Ordinal = 0;
            BuildStrict = false;
            BuildGuid = Guid.Empty;

            LodFlagLODS = false;
            LodFlagLOD0 = false;
            LodFlagLOD1 = false;
            LodFlagLOD2 = false;
            LodFlagLOD3 = false;
            LodFlagLOD4 = false;
            LodFlagLOD5 = false;

            _lastParsingContext = "";
            _lastFilePosition = 0;
        }

        private LODFlags GetLODFlags()
        {
            LODFlags flags = LODFlags.None;
            if (LodFlagLODS) flags |= LODFlags.LODS;
            if (LodFlagLOD0) flags |= LODFlags.LOD0;
            if (LodFlagLOD1) flags |= LODFlags.LOD1;
            if (LodFlagLOD2) flags |= LODFlags.LOD2;
            if (LodFlagLOD3) flags |= LODFlags.LOD3;
            if (LodFlagLOD4) flags |= LODFlags.LOD4;
            if (LodFlagLOD5) flags |= LODFlags.LOD5;
            return flags;
        }

        private (ushort sceneVersion, ushort modelVersion, bool isHorizon) GetVersionInfo()
        {
            return SelectedVersionIndex switch
            {
                0 => (5, 14, false),
                1 => (5, 17, false),
                2 => (10, 21, false),
                3 => (5, 15, true),
                4 => (5, 16, true),
                5 => (6, 18, true),
                _ => (6, 18, true),
            };
        }
    }
}
