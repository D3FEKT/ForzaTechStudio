using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTechStudio.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.ViewModels;

//  View-models for individual list items (UI-friendly wrappers)

// Represents a hash entry loaded from a LightHashMap JSON file.
public class LightHashEntry
{
    public string Hash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RawBytes { get; set; } = string.Empty;

    public uint HashValue
    {
        get
        {
            if (Hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(Hash, 16);
            return 0;
        }
    }

    public string DisplayText => $"{Hash} - {Name}";
}

// Flat, bindable wrapper around a parsed <see cref="LightsBinParser.ModelEntry"/>.
public partial class ModelEntryViewModel : ObservableObject
{
    private LightsBinParser.ModelEntry _model;

    public ModelEntryViewModel(LightsBinParser.ModelEntry model, int index)
    {
        _model = model;
        Index = index;
    }

    public int Index { get; set; }

    public string FullPath
    {
        get => _model.FullPath;
        set { if (_model.FullPath != value) { _model.FullPath = value; OnPropertyChanged(); } }
    }

    public ushort BoneIndex
    {
        get => _model.BoneIndex;
        set { if (_model.BoneIndex != value) { _model.BoneIndex = value; OnPropertyChanged(); } }
    }

    public LightsBinParser.ModelEntry Model => _model;
}

// Flat, bindable wrapper around a parsed <see cref="LightsBinParser.LightGroup"/>.
public partial class LightEntryViewModel : ObservableObject
{
    private LightsBinParser.LightGroup _group;

    public LightEntryViewModel(LightsBinParser.LightGroup group, int index)
    {
        _group = group;
        Index = index;
    }

    public int Index { get; set; }

    public uint Id
    {
        get => _group.Id;
        set { if (_group.Id != value) { _group.Id = value; OnPropertyChanged(); OnPropertyChanged(nameof(IdHex)); OnPropertyChanged(nameof(DisplayName)); } }
    }

    public string IdHex
    {
        get => $"0x{_group.Id:X8}";
        set
        {
            string s = value?.Trim() ?? "0x00000000";
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s[2..];
            if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
            {
                if (_group.Id != parsed)
                {
                    _group.Id = parsed;
                    OnPropertyChanged(nameof(Id));
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }
    }

    public uint Flags
    {
        get => _group.Flags;
        set { if (_group.Flags != value) { _group.Flags = value; OnPropertyChanged(); OnPropertyChanged(nameof(FlagsDisplay)); OnPropertyChanged(nameof(FlagsHex)); } }
    }

    public string FlagsHex
    {
        get => $"0x{_group.Flags:X8}";
        set
        {
            string s = value?.Trim() ?? "0x00000000";
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s[2..];
            if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
            {
                if (_group.Flags != parsed)
                {
                    _group.Flags = parsed;
                    OnPropertyChanged(nameof(Flags));
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FlagsDisplay));
                }
            }
        }
    }

    public string FlagsDisplay => BuildFlagsDisplay();

    public uint AttachmentIndex
    {
        get => _group.AttachmentIndex;
        set { if (_group.AttachmentIndex != value) { _group.AttachmentIndex = value; OnPropertyChanged(); } }
    }

    public string ModelName => _group.ModelName ?? string.Empty;
    
    public string FullModelPath
    {
        get => _group.FullModelPath ?? string.Empty;
        set
        {
            if (_group.FullModelPath != value)
            {
                _group.FullModelPath = value;
                int lastSlash = (value ?? string.Empty).LastIndexOfAny(['/', '\\']);
                _group.ModelName = lastSlash >= 0 ? value[(lastSlash + 1)..] : value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ModelName));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string PresetName
    {
        get => _group.PresetName ?? string.Empty;
        set { if (_group.PresetName != value) { _group.PresetName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
    }

    public Guid V3Guid
    {
        get => _group.V3Guid;
        set { if (_group.V3Guid != value) { _group.V3Guid = value; OnPropertyChanged(); OnPropertyChanged(nameof(V3GuidDisplay)); } }
    }

    // Formatted as the standard registry/010-Editor style: XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX
    public string V3GuidDisplay => _group.V3Guid == Guid.Empty ? "(empty)" : _group.V3Guid.ToString("D").ToUpperInvariant();

    public string V3GuidHex
    {
        get => V3GuidDisplay;
        set
        {
            if (Guid.TryParse(value, out var parsed))
            {
                V3Guid = parsed;
            }
        }
    }

    public string DisplayName => !string.IsNullOrEmpty(ModelName) ? ModelName : IdHex;

    // Pos
    public float PosX { get => _group.Pos.X; set => SetPos(value, _group.Pos.Y, _group.Pos.Z, _group.Pos.W); }
    public float PosY { get => _group.Pos.Y; set => SetPos(_group.Pos.X, value, _group.Pos.Z, _group.Pos.W); }
    public float PosZ { get => _group.Pos.Z; set => SetPos(_group.Pos.X, _group.Pos.Y, value, _group.Pos.W); }
    public float PosW { get => _group.Pos.W; set => SetPos(_group.Pos.X, _group.Pos.Y, _group.Pos.Z, value); }
    private void SetPos(float x, float y, float z, float w)
    {
        _group.Pos = new Vector4(x, y, z, w);
        OnPropertyChanged(nameof(PosX)); OnPropertyChanged(nameof(PosY));
        OnPropertyChanged(nameof(PosZ)); OnPropertyChanged(nameof(PosW));
        OnPropertyChanged(nameof(PosDisplay));
    }
    public string PosDisplay => $"({_group.Pos.X:F3}, {_group.Pos.Y:F3}, {_group.Pos.Z:F3})";

    // Rot
    public float RotX { get => _group.Rot.X; set => SetRot(value, _group.Rot.Y, _group.Rot.Z, _group.Rot.W); }
    public float RotY { get => _group.Rot.Y; set => SetRot(_group.Rot.X, value, _group.Rot.Z, _group.Rot.W); }
    public float RotZ { get => _group.Rot.Z; set => SetRot(_group.Rot.X, _group.Rot.Y, value, _group.Rot.W); }
    public float RotW { get => _group.Rot.W; set => SetRot(_group.Rot.X, _group.Rot.Y, _group.Rot.Z, value); }
    private void SetRot(float x, float y, float z, float w)
    {
        _group.Rot = new Vector4(x, y, z, w);
        OnPropertyChanged(nameof(RotX)); OnPropertyChanged(nameof(RotY));
        OnPropertyChanged(nameof(RotZ)); OnPropertyChanged(nameof(RotW));
        OnPropertyChanged(nameof(RotDisplay));
    }
    public string RotDisplay => $"({_group.Rot.X:F3}, {_group.Rot.Y:F3}, {_group.Rot.Z:F3}, {_group.Rot.W:F3})";

    // DamagePos
    public float DmgPosX { get => _group.DamagePos.X; set => SetDmgPos(value, _group.DamagePos.Y, _group.DamagePos.Z, _group.DamagePos.W); }
    public float DmgPosY { get => _group.DamagePos.Y; set => SetDmgPos(_group.DamagePos.X, value, _group.DamagePos.Z, _group.DamagePos.W); }
    public float DmgPosZ { get => _group.DamagePos.Z; set => SetDmgPos(_group.DamagePos.X, _group.DamagePos.Y, value, _group.DamagePos.W); }
    public float DmgPosW { get => _group.DamagePos.W; set => SetDmgPos(_group.DamagePos.X, _group.DamagePos.Y, _group.DamagePos.Z, value); }
    private void SetDmgPos(float x, float y, float z, float w)
    {
        _group.DamagePos = new Vector4(x, y, z, w);
        OnPropertyChanged(nameof(DmgPosX)); OnPropertyChanged(nameof(DmgPosY));
        OnPropertyChanged(nameof(DmgPosZ)); OnPropertyChanged(nameof(DmgPosW));
        OnPropertyChanged(nameof(DmgPosDisplay));
    }
    public string DmgPosDisplay => $"({_group.DamagePos.X:F3}, {_group.DamagePos.Y:F3}, {_group.DamagePos.Z:F3})";

    // DamageRot
    public float DmgRotX { get => _group.DamageRot.X; set => SetDmgRot(value, _group.DamageRot.Y, _group.DamageRot.Z, _group.DamageRot.W); }
    public float DmgRotY { get => _group.DamageRot.Y; set => SetDmgRot(_group.DamageRot.X, value, _group.DamageRot.Z, _group.DamageRot.W); }
    public float DmgRotZ { get => _group.DamageRot.Z; set => SetDmgRot(_group.DamageRot.X, _group.DamageRot.Y, value, _group.DamageRot.W); }
    public float DmgRotW { get => _group.DamageRot.W; set => SetDmgRot(_group.DamageRot.X, _group.DamageRot.Y, _group.DamageRot.Z, value); }
    private void SetDmgRot(float x, float y, float z, float w)
    {
        _group.DamageRot = new Vector4(x, y, z, w);
        OnPropertyChanged(nameof(DmgRotX)); OnPropertyChanged(nameof(DmgRotY));
        OnPropertyChanged(nameof(DmgRotZ)); OnPropertyChanged(nameof(DmgRotW));
        OnPropertyChanged(nameof(DmgRotDisplay));
    }
    public string DmgRotDisplay => $"({_group.DamageRot.X:F3}, {_group.DamageRot.Y:F3}, {_group.DamageRot.Z:F3}, {_group.DamageRot.W:F3})";

    // Flags - each setter now also raises OnPropertyChanged for the raw Flags value
    public bool IsExterior
    {
        get => _group.IsExterior;
        set { _group.IsExterior = value; OnPropertyChanged(); OnPropertyChanged(nameof(Flags)); OnPropertyChanged(nameof(FlagsDisplay)); OnPropertyChanged(nameof(FlagsHex)); }
    }
    public bool IsCockpit
    {
        get => _group.IsCockpit;
        set { _group.IsCockpit = value; OnPropertyChanged(); OnPropertyChanged(nameof(Flags)); OnPropertyChanged(nameof(FlagsDisplay)); OnPropertyChanged(nameof(FlagsHex)); }
    }
    public bool CastsShadows
    {
        get => _group.CastsShadows;
        set { _group.CastsShadows = value; OnPropertyChanged(); OnPropertyChanged(nameof(Flags)); OnPropertyChanged(nameof(FlagsDisplay)); OnPropertyChanged(nameof(FlagsHex)); }
    }
    public bool IsHood
    {
        get => _group.IsHood;
        set { _group.IsHood = value; OnPropertyChanged(); OnPropertyChanged(nameof(Flags)); OnPropertyChanged(nameof(FlagsDisplay)); OnPropertyChanged(nameof(FlagsHex)); }
    }
    public bool IsWindshieldReflection
    {
        get => _group.IsWindshieldReflection;
        set { _group.IsWindshieldReflection = value; OnPropertyChanged(); OnPropertyChanged(nameof(Flags)); OnPropertyChanged(nameof(FlagsDisplay)); OnPropertyChanged(nameof(FlagsHex)); }
    }
    public bool IsDriverlessCockpit
    {
        get => _group.IsDriverlessCockpit;
        set { _group.IsDriverlessCockpit = value; OnPropertyChanged(); OnPropertyChanged(nameof(Flags)); OnPropertyChanged(nameof(FlagsDisplay)); OnPropertyChanged(nameof(FlagsHex)); }
    }
    public bool IsWindshieldReflectionDriverlessCockpit
    {
        get => _group.IsWindshieldReflectionDriverlessCockpit;
        set { _group.IsWindshieldReflectionDriverlessCockpit = value; OnPropertyChanged(); OnPropertyChanged(nameof(Flags)); OnPropertyChanged(nameof(FlagsDisplay)); OnPropertyChanged(nameof(FlagsHex)); }
    }
    public bool IsProxyLOD
    {
        get => _group.IsProxyLOD;
        set { _group.IsProxyLOD = value; OnPropertyChanged(); OnPropertyChanged(nameof(Flags)); OnPropertyChanged(nameof(FlagsDisplay)); OnPropertyChanged(nameof(FlagsHex)); }
    }

    // Direct access to the underlying group for serialization.
    public LightsBinParser.LightGroup Group => _group;

    private string BuildFlagsDisplay()
    {
        var parts = new List<string>();
        if (_group.IsExterior) parts.Add("Exterior");
        if (_group.IsCockpit) parts.Add("Cockpit");
        if (_group.CastsShadows) parts.Add("Shadows");
        if (_group.IsHood) parts.Add("Hood");
        if (_group.IsWindshieldReflection) parts.Add("WShield");
        if (_group.IsDriverlessCockpit) parts.Add("Driverless");
        if (_group.IsProxyLOD) parts.Add("ProxyLOD");
        return parts.Count > 0 ? string.Join(" | ", parts) : $"0x{_group.Flags:X8}";
    }
}

// Flat, bindable wrapper around a parsed <see cref="LightPresetsBinParser.LightPreset"/>.
public partial class PresetEntryViewModel : ObservableObject
{
    private LightPresetsBinParser.LightPreset _preset;

    public PresetEntryViewModel(LightPresetsBinParser.LightPreset preset, int index, bool isV2)
    {
        _preset = preset;
        Index = index;
        IsV2 = isV2;
        Parameters = preset.Parameters
            .Select(p => new PresetParameterViewModel(p))
            .ToObservableCollection();
    }

    public int Index { get; }
    public bool IsV2 { get; }

    public uint Id
    {
        get => _preset.Id;
        set
        {
            if (_preset.Id != value)
            {
                _preset.Id = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IdHex));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string IdHex
    {
        get => _preset.Id != 0 || IsV2 ? $"0x{_preset.Id:X8}" : "V1";
        set
        {
            if (!IsV2) return;
            string s = value?.Trim() ?? "0x00000000";
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s[2..];
            if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
            {
                Id = parsed;
            }
        }
    }

    public uint PresetSize => _preset.PresetSize;
    public int ParameterCount => _preset.Parameters.Count;

    // Friendly display name: derived from projected texture param if present.
    public string DisplayName
    {
        get
        {
            var texParam = _preset.Parameters.FirstOrDefault(p =>
                p.Id == (byte)LightPresetsBinParser.ParameterId.LightProjectedTexture);
            if (texParam != null)
            {
                string tex = texParam.AsString;
                if (!string.IsNullOrEmpty(tex))
                    return Path.GetFileNameWithoutExtension(tex);
            }
            return _preset.Id != 0 ? $"Preset {IdHex}" : $"Preset [{Index}]";
        }
    }

    public ObservableCollection<PresetParameterViewModel> Parameters { get; }

    public LightPresetsBinParser.LightPreset Preset => _preset;
}

// Flat, bindable wrapper around a single <see cref="LightPresetsBinParser.LightParameter"/>.
public partial class PresetParameterViewModel : ObservableObject
{
    private readonly LightPresetsBinParser.LightParameter _param;
    private bool _isUpdating;

    public PresetParameterViewModel(LightPresetsBinParser.LightParameter param)
    {
        _param = param;
    }

    public LightPresetsBinParser.LightParameter Parameter => _param;

    public string IdHex => $"0x{_param.Id:X2}";
    public string Name => _param.Name;
    public string TypeLabel => _param.DataType.ToString();
    public string Description => LightPresetsBinParser.GetParameterDescription(_param.Id);

    public string DisplayValue => _param.DisplayValue;

    public bool IsVec3 => _param.DataType == LightPresetsBinParser.ParameterDataType.Vec3;
    public bool IsBool => _param.DataType == LightPresetsBinParser.ParameterDataType.Bool;
    public bool IsFloat => _param.DataType == LightPresetsBinParser.ParameterDataType.Float;
    public bool IsUInt32 => _param.DataType == LightPresetsBinParser.ParameterDataType.UInt32;
    public bool IsString => _param.DataType == LightPresetsBinParser.ParameterDataType.String;
    public bool IsRaw => _param.DataType == LightPresetsBinParser.ParameterDataType.Raw;

    public bool IsLightType => _param.Id == 0x01;
    public bool IsStandardUInt32 => _param.DataType == LightPresetsBinParser.ParameterDataType.UInt32 && _param.Id != 0x01;

    public bool BoolValue
    {
        get => IsBool ? (_param.AsBool ?? false) : false;
        set { if (IsBool) { _param.SetBool(value); OnPropertyChanged(nameof(DisplayValue)); } }
    }

    public double FloatValue
    {
        get => IsFloat ? (_param.AsFloat ?? 0.0) : 0.0;
        set 
        { 
            if (!IsFloat) return;
            if (Math.Abs((_param.AsFloat ?? 0.0) - value) < float.Epsilon) return;
            _param.SetFloat((float)value); 
            OnPropertyChanged(nameof(DisplayValue)); 
        }
    }

    public string StringValue
    {
        get => IsString ? (_param.AsString ?? string.Empty) : string.Empty;
        set { if (IsString) { _param.SetString(value); OnPropertyChanged(nameof(DisplayValue)); } }
    }

    public string UInt32Value
    {
        get => _param.DisplayValue;
        set
        {
            if (!IsStandardUInt32) return;
            string numPart = value;
            int parenIdx = value.IndexOf('(');
            if (parenIdx >= 0)
            {
                int closeIdx = value.IndexOf(')', parenIdx);
                if (closeIdx > parenIdx)
                    numPart = value.Substring(parenIdx + 1, closeIdx - parenIdx - 1).Trim();
            }
            if (uint.TryParse(numPart, out uint uval))
            {
                _param.SetUInt32(uval);
                OnPropertyChanged(nameof(DisplayValue));
            }
        }
    }

    public int LightTypeValue
    {
        get => IsLightType ? (int)(_param.AsUInt32 ?? 0) : -1;
        set { if (IsLightType) { _param.SetUInt32((uint)value); OnPropertyChanged(nameof(DisplayValue)); } }
    }

    public Windows.UI.Color Vec3Color
    {
        get
        {
            if (!IsVec3) return Windows.UI.Color.FromArgb(255, 0, 0, 0);
            var v = _param.AsVec3 ?? Vector3.Zero;
            return Windows.UI.Color.FromArgb(255,
                (byte)Math.Clamp(v.X * 255f, 0, 255),
                (byte)Math.Clamp(v.Y * 255f, 0, 255),
                (byte)Math.Clamp(v.Z * 255f, 0, 255));
        }
        set
        {
            if (_isUpdating || !IsVec3) return;
            _param.SetVec3(new Vector3(value.R / 255f, value.G / 255f, value.B / 255f));
            _isUpdating = true;
            OnPropertyChanged(nameof(Vec3Color));
            OnPropertyChanged(nameof(DisplayValue));
            _isUpdating = false;
        }
    }

    private void Refresh()
    {
        _isUpdating = true;
        OnPropertyChanged(nameof(DisplayValue));
        _isUpdating = false;
    }

    public string TypeGlyph => _param.DataType switch
    {
        LightPresetsBinParser.ParameterDataType.Bool => "\uE73E",
        LightPresetsBinParser.ParameterDataType.Float => "\uE8EF",
        LightPresetsBinParser.ParameterDataType.UInt32 => "\uE8CB",
        LightPresetsBinParser.ParameterDataType.Vec3 => "\uE946",
        LightPresetsBinParser.ParameterDataType.String => "\uE8D2",
        _ => "\uE9CE",
    };
}

//  Main ViewModel

public partial class LightsViewModel : ObservableObject
{
    // Parsed data

    private LightsBinParser.LightsBinData _lightsData;
    private LightPresetsBinParser.LightPresetsData _presetsData;

    private string _lightsBinPath;
    private string _presetsBinPath;

    private string _sourceZipPath;
    private string _lightsZipEntryName;
    private string _presetsZipEntryName;

    // State

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Waiting..";

    [ObservableProperty]
    private bool _hasLightsData;

    [ObservableProperty]
    private bool _hasPresetsData;

    [ObservableProperty]
    private string _lightsFileInfo = string.Empty;

    [ObservableProperty]
    private string _presetsFileInfo = string.Empty;

    public bool IsContentVisible => HasLightsData || HasPresetsData;

    partial void OnHasLightsDataChanged(bool value)
    {
        OnPropertyChanged(nameof(IsContentVisible));
        SaveLightsBinCommand.NotifyCanExecuteChanged();
        SaveAsLightsBinCommand.NotifyCanExecuteChanged();
        JumpToPresetCommand.NotifyCanExecuteChanged();
        SaveLightsToZipCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasPresetsDataChanged(bool value)
    {
        OnPropertyChanged(nameof(IsContentVisible));
        SavePresetsBinCommand.NotifyCanExecuteChanged();
        SaveAsPresetsBinCommand.NotifyCanExecuteChanged();
        JumpToPresetCommand.NotifyCanExecuteChanged();
        SavePresetsToZipCommand.NotifyCanExecuteChanged();
    }

    // Lights tab

    public ObservableCollection<LightEntryViewModel> Lights { get; } = [];

    [ObservableProperty]
    private LightEntryViewModel _selectedLight;

    public ObservableCollection<ModelEntryViewModel> Attachments { get; } = [];

    [ObservableProperty]
    private ModelEntryViewModel _selectedAttachment;

    [ObservableProperty]
    private int _lightsVersion;

    [ObservableProperty]
    private int _lightCount;

    [ObservableProperty]
    private int _attachmentCount;

    [ObservableProperty]
    private int _lodOverrideCount;

    // Light ID source selection

    [ObservableProperty]
    private int _selectedIdSourceIndex; // 0=FH3, 1=FH4, 2=FH5

    public ObservableCollection<LightHashEntry> Fh5HashEntries { get; } = [];

    [ObservableProperty]
    private LightHashEntry _selectedFh5Hash;

    partial void OnSelectedIdSourceIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsFh5IdSource));
        OnPropertyChanged(nameof(IsFh3Fh4IdSource));
    }

    public bool IsFh5IdSource => SelectedIdSourceIndex == 2;
    public bool IsFh3Fh4IdSource => SelectedIdSourceIndex != 2;

    partial void OnLightsVersionChanged(int value)
    {
        OnPropertyChanged(nameof(IsVersion3));
    }

    public bool IsVersion3 => LightsVersion >= 3;

    partial void OnSelectedFh5HashChanged(LightHashEntry value)
    {
        if (value != null && SelectedLight != null)
        {
            SelectedLight.Id = value.HashValue;
        }
    }

    // Presets tab

    public ObservableCollection<PresetEntryViewModel> Presets { get; } = [];

    [ObservableProperty]
    private PresetEntryViewModel _selectedPreset;

    [ObservableProperty]
    private int _presetsVersion;

    [ObservableProperty]
    private int _presetCount;

    // Log

    public ObservableCollection<string> Log { get; } = [];

    // Constructor

    public LightsViewModel()
    {
        LoadFh5Hashes();
    }

    private void LoadFh5Hashes()
    {
        try
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string fh5Path = Path.Combine(basePath, "Presets", "LightHashMap_FH5.json");
            if (File.Exists(fh5Path))
            {
                string json = File.ReadAllText(fh5Path);
                var entries = JsonSerializer.Deserialize<List<LightHashEntry>>(json);
                if (entries != null)
                {
                    foreach (var entry in entries.DistinctBy(e => e.Hash).OrderBy(e => e.Name))
                        Fh5HashEntries.Add(entry);
                }
            }
        }
        catch { /* Non-critical */ }
    }

    //  Commands

    // Opens a zip archive and extracts + parses lights.bin and lightpresets.bin from it.
    [RelayCommand]
    public async Task OpenZipAsync()
    {
        var picker = BuildFilePicker();
        picker.FileTypeFilter.Add(".zip");
        picker.CommitButtonText = "Open Zip";

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        await LoadZipFileAsync(file.Path);
    }

    public async Task LoadZipFileAsync(string filePath)
    {
        IsBusy = true;
        StatusMessage = $"Loading {Path.GetFileName(filePath)}…";
        AddLog($"=== Opening zip: {Path.GetFileName(filePath)} ===");

        try
        {
            byte[] lightsBytes = null;
            byte[] presetsBytes = null;
            string lightsPath = null, presetsPath = null;

            await Task.Run(() =>
            {
                using var zip = new CustomZipFile(filePath);
                var entries = zip.GetEntries().Where(e => !e.IsDirectory).ToList();

                var lightsEntry = entries.FirstOrDefault(e =>
                    e.Name.EndsWith("lights.bin", StringComparison.OrdinalIgnoreCase));
                var presetsEntry = entries.FirstOrDefault(e =>
                    e.Name.EndsWith("lightpresets.bin", StringComparison.OrdinalIgnoreCase));

                if (lightsEntry != null)
                {
                    lightsBytes = zip.ExtractToMemory(lightsEntry);
                    lightsPath = lightsEntry.Name;
                    _sourceZipPath = filePath;
                    _lightsZipEntryName = lightsEntry.Name;
                    AddLog($"  lights.bin       {lightsEntry.Name}  ({lightsEntry.UncompressedSize:N0} B)");
                }
                else AddLog("  lights.bin        not found");

                if (presetsEntry != null)
                {
                    presetsBytes = zip.ExtractToMemory(presetsEntry);
                    presetsPath = presetsEntry.Name;
                    _sourceZipPath = filePath;
                    _presetsZipEntryName = presetsEntry.Name;
                    AddLog($"  lightpresets.bin  {presetsEntry.Name}  ({presetsEntry.UncompressedSize:N0} B)");
                }
                else AddLog("  lightpresets.bin  not found");
            });

            if (lightsBytes != null)
                LoadLightsFromBytes(lightsBytes, lightsPath ?? "lights.bin");

            if (presetsBytes != null)
                LoadPresetsFromBytes(presetsBytes, presetsPath ?? "lightpresets.bin");

            if (lightsBytes == null && presetsBytes == null)
                StatusMessage = "Neither lights.bin nor lightpresets.bin found in archive.";
            else
                StatusMessage = $"Loaded from {Path.GetFileName(filePath)} - {LightCount} lights, {PresetCount} presets.";
        }
        catch (Exception ex)
        {
            AddLog($"ERROR: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Opens a lights.bin file directly.
    [RelayCommand]
    public async Task OpenLightsBinAsync()
    {
        var picker = BuildFilePicker();
        picker.FileTypeFilter.Add(".bin");
        picker.CommitButtonText = "Open lights.bin";

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        await LoadLightsBinFileAsync(file.Path);
    }

    public async Task LoadLightsBinFileAsync(string filePath)
    {
        IsBusy = true;
        StatusMessage = $"Parsing {Path.GetFileName(filePath)}…";
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(filePath);
            _lightsBinPath = filePath;
            LoadLightsFromBytes(bytes, Path.GetFileName(filePath));
            StatusMessage = $"Loaded {Path.GetFileName(filePath)} - {LightCount} lights, version {LightsVersion}.";
        }
        catch (Exception ex)
        {
            AddLog($"ERROR: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Opens a lightpresets.bin file directly.
    [RelayCommand]
    public async Task OpenPresetsBinAsync()
    {
        var picker = BuildFilePicker();
        picker.FileTypeFilter.Add(".bin");
        picker.CommitButtonText = "Open lightpresets.bin";

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        await LoadPresetsBinFileAsync(file.Path);
    }

    public async Task LoadPresetsBinFileAsync(string filePath)
    {
        IsBusy = true;
        StatusMessage = $"Parsing {Path.GetFileName(filePath)}…";
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(filePath);
            _presetsBinPath = filePath;
            LoadPresetsFromBytes(bytes, Path.GetFileName(filePath));
            StatusMessage = $"Loaded {Path.GetFileName(filePath)} - {PresetCount} presets, version {PresetsVersion}.";
        }
        catch (Exception ex)
        {
            AddLog($"ERROR: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Saves modified lights.bin back to its source path (or prompts for a new one).
    [RelayCommand(CanExecute = nameof(CanSaveLightsBin))]
    public async Task SaveLightsBinAsync()
    {
        await InternalSaveLightsBinAsync(_lightsBinPath);
    }

    [RelayCommand(CanExecute = nameof(CanSaveLightsBin))]
    public async Task SaveAsLightsBinAsync()
    {
        await InternalSaveLightsBinAsync(null);
    }

    private async Task InternalSaveLightsBinAsync(string savePath)
    {
        if (_lightsData == null) return;

        if (string.IsNullOrEmpty(savePath))
        {
            var picker = new FileSavePicker();
            InitPicker(picker);
            picker.SuggestedFileName = "lights.bin";
            picker.FileTypeChoices.Add("Binary", [".bin"]);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            savePath = file.Path;
        }

        IsBusy = true;
        StatusMessage = "Saving lights.bin…";
        try
        {
            // Sync attachment indices from current view-model state
            SyncLightsToData();

            await Task.Run(() =>
            {
                using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write);
                new LightsBinParser().Serialize(fs, _lightsData);
            });

            _lightsBinPath = savePath;
            AddLog($"Saved lights.bin  {savePath}");
            StatusMessage = $"Saved: {Path.GetFileName(savePath)}";
        }
        catch (Exception ex)
        {
            AddLog($"ERROR saving: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSaveLightsBin() => HasLightsData;

    // Saves modified lightpresets.bin back to its source path (or prompts for a new one).
    [RelayCommand(CanExecute = nameof(CanSavePresetsBin))]
    public async Task SavePresetsBinAsync()
    {
        await InternalSavePresetsBinAsync(_presetsBinPath);
    }

    [RelayCommand(CanExecute = nameof(CanSavePresetsBin))]
    public async Task SaveAsPresetsBinAsync()
    {
        await InternalSavePresetsBinAsync(null);
    }

    private async Task InternalSavePresetsBinAsync(string savePath)
    {
        if (_presetsData == null) return;

        if (string.IsNullOrEmpty(savePath))
        {
            var picker = new FileSavePicker();
            InitPicker(picker);
            picker.SuggestedFileName = "lightpresets.bin";
            picker.FileTypeChoices.Add("Binary", [".bin"]);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            savePath = file.Path;
        }

        IsBusy = true;
        StatusMessage = "Saving lightpresets.bin…";
        try
        {
            // Sync preset parameters from ViewModels back to data
            SyncPresetsToData();

            await Task.Run(() =>
            {
                using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write);
                new LightPresetsBinParser().Serialize(fs, _presetsData);
            });

            _presetsBinPath = savePath;
            AddLog($"Saved lightpresets.bin  {savePath}");
            StatusMessage = $"Saved: {Path.GetFileName(savePath)}";
        }
        catch (Exception ex)
        {
            AddLog($"ERROR saving: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSavePresetsBin() => HasPresetsData;

    // Adds a new light entry with default values.
    [RelayCommand]
    public void AddLight()
    {
        if (_lightsData == null)
        {
            // Create default lights data structure
            _lightsData = new LightsBinParser.LightsBinData { Version = 2 };
            HasLightsData = true;
        }

        var group = new LightsBinParser.LightGroup
        {
            Id = 0,
            Flags = 1, // Exterior by default
            AttachmentIndex = 0,
            Pos = Vector4.Zero,
            Rot = new Vector4(0, 0, 0, 1),
            DamagePos = Vector4.Zero,
            DamageRot = new Vector4(0, 0, 0, 1),
            PresetName = string.Empty,
            FullModelPath = string.Empty,
            ModelName = string.Empty
        };

        _lightsData.Groups.Add(group);
        var vm = new LightEntryViewModel(group, Lights.Count);
        Lights.Add(vm);
        SelectedLight = vm;
        LightCount = Lights.Count;
        LightsFileInfo = $"v{_lightsData.Version} · {LightCount} lights · {_lightsData.Models.Count} attachments · {_lightsData.LODOverrides.Count} LOD overrides";
        AddLog($"Added new light at index {vm.Index}");
    }

    // Removes the currently selected light entry.
    [RelayCommand(CanExecute = nameof(CanRemoveLight))]
    public void RemoveLight()
    {
        if (SelectedLight == null || _lightsData == null) return;

        int idx = Lights.IndexOf(SelectedLight);
        _lightsData.Groups.Remove(SelectedLight.Group);
        Lights.RemoveAt(idx);

        // Reindex remaining lights
        for (int i = 0; i < Lights.Count; i++)
            Lights[i].Index = i;

        SelectedLight = Lights.Count > 0 ? Lights[Math.Min(idx, Lights.Count - 1)] : null;
        LightCount = Lights.Count;
        LightsFileInfo = $"v{_lightsData.Version} · {LightCount} lights · {_lightsData.Models.Count} attachments · {_lightsData.LODOverrides.Count} LOD overrides";
        AddLog($"Removed light at index {idx}");
    }

    private bool CanRemoveLight() => SelectedLight != null && HasLightsData;

    // Adds a new attachment entry.
    [RelayCommand]
    public void AddAttachment()
    {
        if (_lightsData == null) return;

        var model = new LightsBinParser.ModelEntry
        {
            FullPath = string.Empty,
            BoneIndex = 0
        };

        _lightsData.Models.Add(model);
        var vm = new ModelEntryViewModel(model, Attachments.Count);
        Attachments.Add(vm);
        SelectedAttachment = vm;
        AttachmentCount = Attachments.Count;
        LightsFileInfo = $"v{_lightsData.Version} · {LightCount} lights · {_lightsData.Models.Count} attachments · {_lightsData.LODOverrides.Count} LOD overrides";
        AddLog($"Added new attachment at index {vm.Index}");
    }

    // Removes the currently selected attachment entry.
    [RelayCommand(CanExecute = nameof(CanRemoveAttachment))]
    public void RemoveAttachment()
    {
        if (SelectedAttachment == null || _lightsData == null) return;

        int idx = Attachments.IndexOf(SelectedAttachment);
        _lightsData.Models.Remove(SelectedAttachment.Model);
        Attachments.RemoveAt(idx);

        // Reindex remaining attachments
        for (int i = 0; i < Attachments.Count; i++)
            Attachments[i].Index = i;

        SelectedAttachment = Attachments.Count > 0 ? Attachments[Math.Min(idx, Attachments.Count - 1)] : null;
        AttachmentCount = Attachments.Count;
        LightsFileInfo = $"v{_lightsData.Version} · {LightCount} lights · {_lightsData.Models.Count} attachments · {_lightsData.LODOverrides.Count} LOD overrides";
        AddLog($"Removed attachment at index {idx}");
    }

    private bool CanRemoveAttachment() => SelectedAttachment != null && HasLightsData;

    // Navigates the Presets tab to the preset matching the selected light's preset name/hash.
    [RelayCommand(CanExecute = nameof(CanJumpToPreset))]
    public void JumpToPreset()
    {
        if (SelectedLight == null || !HasPresetsData) return;

        // Try to match by hash (V2) first, then by PresetName string
        PresetEntryViewModel match = null;

        if (SelectedLight.Id != 0 && _presetsData?.Version >= 2)
        {
            match = Presets.FirstOrDefault(p => p.Id == SelectedLight.Id);
        }

        if (match == null && !string.IsNullOrEmpty(SelectedLight.PresetName))
        {
            match = Presets.FirstOrDefault(p =>
                p.DisplayName.Equals(SelectedLight.PresetName, StringComparison.OrdinalIgnoreCase));
        }

        if (match != null)
        {
            SelectedPreset = match;
            JumpedToPreset?.Invoke(match);
            AddLog($"Jumped to preset for light {SelectedLight.IdHex}: {match.DisplayName}");
        }
        else
        {
            StatusMessage = $"No matching preset found for light {SelectedLight.IdHex}.";
        }
    }

    private bool CanJumpToPreset() => SelectedLight != null && HasPresetsData;

    // Raised when <see cref="JumpToPreset"/> navigates to a preset — the view pivots to the Presets tab.
    public event Action<PresetEntryViewModel> JumpedToPreset;

    // Imports presets from a LightHashMap JSON file into the presets data.
    [RelayCommand]
    public async Task ImportPresetsFromJsonAsync()
    {
        var picker = BuildFilePicker();
        picker.FileTypeFilter.Add(".json");
        picker.CommitButtonText = "Import Presets JSON";

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        IsBusy = true;
        StatusMessage = $"Importing presets from {file.Name}…";
        try
        {
            string json = await File.ReadAllTextAsync(file.Path);
            var entries = JsonSerializer.Deserialize<List<LightHashEntry>>(json);
            if (entries == null || entries.Count == 0)
            {
                StatusMessage = "No entries found in JSON file.";
                return;
            }

            // If no presets data exists, create a new V2 container
            if (_presetsData == null)
            {
                _presetsData = new LightPresetsBinParser.LightPresetsData
                {
                    Version = 2,
                    PresetCount = 0
                };
            }

            int importedCount = 0;
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.RawBytes)) continue;

                // Parse raw bytes back into a preset
                byte[] rawBytes = ParseHexBytes(entry.RawBytes);
                if (rawBytes.Length < 8) continue;

                using var ms = new MemoryStream(rawBytes);
                using var bs = new Syroot.BinaryData.BinaryStream(ms, Syroot.BinaryData.ByteConverter.Little);

                uint presetSize = bs.ReadUInt32();
                uint paramCount = bs.ReadUInt32();

                var preset = new LightPresetsBinParser.LightPreset
                {
                    Id = entry.HashValue,
                    PresetSize = presetSize,
                    RawBlockBytes = rawBytes
                };

                for (uint p = 0; p < paramCount && ms.Position < ms.Length; p++)
                {
                    byte paramId = bs.Read1Byte();
                    byte paramLen = bs.Read1Byte();
                    byte[] paramValue = paramLen > 0 && ms.Position + paramLen <= ms.Length
                        ? bs.ReadBytes(paramLen) : [];

                    preset.Parameters.Add(new LightPresetsBinParser.LightParameter
                    {
                        Id = paramId,
                        Length = paramLen,
                        Value = paramValue
                    });
                }

                // Check for duplicate by ID
                bool exists = _presetsData.Presets.Any(p => p.Id == preset.Id && preset.Id != 0);
                if (!exists)
                {
                    _presetsData.Presets.Add(preset);
                    if (_presetsData.Version >= 2)
                        _presetsData.PresetIds.Add(preset.Id);
                    importedCount++;
                }
            }

            // Rebuild UI
            _presetsData.PresetCount = (uint)_presetsData.Presets.Count;
            Presets.Clear();
            bool isV2 = _presetsData.Version >= 2;
            for (int i = 0; i < _presetsData.Presets.Count; i++)
                Presets.Add(new PresetEntryViewModel(_presetsData.Presets[i], i, isV2));

            PresetsVersion = (int)_presetsData.Version;
            PresetCount = _presetsData.Presets.Count;
            HasPresetsData = true;
            PresetsFileInfo = $"v{_presetsData.Version} · {_presetsData.Presets.Count} presets";

            AddLog($"Imported {importedCount} presets from {file.Name} (total now: {PresetCount})");
            StatusMessage = $"Imported {importedCount} presets from {file.Name}.";
        }
        catch (Exception ex)
        {
            AddLog($"ERROR importing: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Clears all loaded data and resets the page.
    [RelayCommand]
    public void Clear()
    {
        _lightsData = null;
        _presetsData = null;
        _lightsBinPath = null;
        _presetsBinPath = null;

        Lights.Clear();
        Attachments.Clear();
        Presets.Clear();
        Log.Clear();
        SelectedLight = null;
        SelectedAttachment = null;
        SelectedPreset = null;

        HasLightsData = false;
        HasPresetsData = false;
        // OnHasLightsDataChanged and OnHasPresetsDataChanged will update IsContentVisible
        
        LightsFileInfo = string.Empty;
        PresetsFileInfo = string.Empty;
        LightCount = 0;
        AttachmentCount = 0;
        LodOverrideCount = 0;
        PresetCount = 0;
        LightsVersion = 0;
        PresetsVersion = 0;

        StatusMessage = "Cleared. Open a zip archive or individual .bin files to begin.";
    }

    //  Property-change wiring for command CanExecute

    partial void OnSelectedLightChanged(LightEntryViewModel value)
    {
        JumpToPresetCommand.NotifyCanExecuteChanged();
        RemoveLightCommand.NotifyCanExecuteChanged();

        if (_previousSelectedLight != null)
        {
            _previousSelectedLight.PropertyChanged -= SelectedLight_PropertyChanged;
        }

        if (value != null)
        {
            value.PropertyChanged += SelectedLight_PropertyChanged;
        }

        _previousSelectedLight = value;
    }

    private LightEntryViewModel _previousSelectedLight;

    private void SelectedLight_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LightEntryViewModel.AttachmentIndex))
        {
            if (SelectedLight != null && SelectedLight.AttachmentIndex < Attachments.Count)
            {
                SelectedLight.FullModelPath = Attachments[(int)SelectedLight.AttachmentIndex].FullPath;
            }
        }
    }

    partial void OnSelectedPresetChanged(PresetEntryViewModel value)
    {
        RemovePresetCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedAttachmentChanged(ModelEntryViewModel value)
    {
        RemoveAttachmentCommand.NotifyCanExecuteChanged();
    }

    //  Private helpers

    private void LoadLightsFromBytes(byte[] bytes, string displayName)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var parser = new LightsBinParser();
            _lightsData = parser.ParseToData(ms);

            Lights.Clear();
            for (int i = 0; i < _lightsData.Groups.Count; i++)
                Lights.Add(new LightEntryViewModel(_lightsData.Groups[i], i));

            Attachments.Clear();
            for (int i = 0; i < _lightsData.Models.Count; i++)
                Attachments.Add(new ModelEntryViewModel(_lightsData.Models[i], i));

            LightsVersion = (int)_lightsData.Version;
            LightCount = _lightsData.Groups.Count;
            AttachmentCount = _lightsData.Models.Count;
            LodOverrideCount = _lightsData.LODOverrides.Count;
            HasLightsData = true;
            LightsFileInfo = $"v{_lightsData.Version} · {LightCount} lights · {_lightsData.Models.Count} attachments · {_lightsData.LODOverrides.Count} LOD overrides";

            AddLog($"lights.bin: version={_lightsData.Version}, lights={_lightsData.Groups.Count}, attachments={_lightsData.Models.Count}, LODs={_lightsData.LODOverrides.Count}");
        }
        catch (Exception ex)
        {
            AddLog($"Failed to parse {displayName}: {ex.Message}");
            throw;
        }
    }

    private void LoadPresetsFromBytes(byte[] bytes, string displayName)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var parser = new LightPresetsBinParser();
            _presetsData = parser.Parse(ms);

            bool isV2 = _presetsData.Version >= 2;

            Presets.Clear();
            for (int i = 0; i < _presetsData.Presets.Count; i++)
                Presets.Add(new PresetEntryViewModel(_presetsData.Presets[i], i, isV2));

            PresetsVersion = (int)_presetsData.Version;
            PresetCount = _presetsData.Presets.Count;
            HasPresetsData = true;
            PresetsFileInfo = $"v{_presetsData.Version} · {_presetsData.Presets.Count} presets";

            AddLog($"lightpresets.bin: version={_presetsData.Version}, presets={_presetsData.Presets.Count}");
        }
        catch (Exception ex)
        {
            AddLog($"Failed to parse {displayName}: {ex.Message}");
            throw;
        }
    }

    private void SyncLightsToData()
    {
        if (_lightsData == null) return;
        // Groups are already mutable via LightEntryViewModel.Group property
        _lightsData.Groups.Clear();
        foreach (var vm in Lights)
            _lightsData.Groups.Add(vm.Group);

        _lightsData.Models.Clear();
        foreach (var vm in Attachments)
            _lightsData.Models.Add(vm.Model);
    }

    private void SyncPresetsToData()
    {
        if (_presetsData == null) return;
        
        _presetsData.Presets.Clear();
        if (_presetsData.Version >= 2)
            _presetsData.PresetIds.Clear();

        foreach (var vm in Presets)
        {
            _presetsData.Presets.Add(vm.Preset);
            if (_presetsData.Version >= 2)
                _presetsData.PresetIds.Add(vm.Id);
                
            vm.Preset.RawBlockBytes = [];
        }
    }

    private static byte[] ParseHexBytes(String hexString)
    {
        var parts = hexString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            bytes[i] = Convert.ToByte(parts[i], 16);
        return bytes;
    }

    private void AddLog(string message)
    {
        if (App.MainWindow?.DispatcherQueue != null)
            App.MainWindow.DispatcherQueue.TryEnqueue(() => Log.Add(message));
        else
            Log.Add(message);
    }

    private static FileOpenPicker BuildFilePicker()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return picker;
    }

    private static void InitPicker(FileSavePicker picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
    }

    public bool CanSaveLightsToZip => HasLightsData && !string.IsNullOrEmpty(_sourceZipPath) && !string.IsNullOrEmpty(_lightsZipEntryName);
    public bool CanSavePresetsToZip => HasPresetsData && !string.IsNullOrEmpty(_sourceZipPath) && !string.IsNullOrEmpty(_presetsZipEntryName);

    [RelayCommand(CanExecute = nameof(CanSaveLightsToZip))]
    public async Task SaveLightsToZipAsync()
    {
        if (_lightsData == null || string.IsNullOrEmpty(_sourceZipPath) || string.IsNullOrEmpty(_lightsZipEntryName)) return;

        IsBusy = true;
        StatusMessage = "Saving lights.bin to Zip…";
        try
        {
            SyncLightsToData();

            await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                new LightsBinParser().Serialize(ms, _lightsData);
                byte[] newBytes = ms.ToArray();
                ZipArchiveHelper.ReplaceEntry(_sourceZipPath, _lightsZipEntryName, newBytes);
            });

            AddLog($"Saved lights.bin to Zip: {_sourceZipPath}");
            StatusMessage = $"Saved lights.bin to Zip.";
        }
        catch (Exception ex)
        {
            AddLog($"ERROR saving to Zip: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSavePresetsToZip))]
    public async Task SavePresetsToZipAsync()
    {
        if (_presetsData == null || string.IsNullOrEmpty(_sourceZipPath) || string.IsNullOrEmpty(_presetsZipEntryName)) return;

        IsBusy = true;
        StatusMessage = "Saving lightpresets.bin to Zip…";
        try
        {
            SyncPresetsToData();

            await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                new LightPresetsBinParser().Serialize(ms, _presetsData);
                byte[] newBytes = ms.ToArray();
                ZipArchiveHelper.ReplaceEntry(_sourceZipPath, _presetsZipEntryName, newBytes);
            });

            AddLog($"Saved lightpresets.bin to Zip: {_sourceZipPath}");
            StatusMessage = $"Saved lightpresets.bin to Zip.";
        }
        catch (Exception ex)
        {
            AddLog($"ERROR saving to Zip: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Removes the currently selected preset entry.
    [RelayCommand(CanExecute = nameof(CanRemovePreset))]
    public void RemovePreset()
    {
        if (SelectedPreset == null || _presetsData == null) return;

        int idx = Presets.IndexOf(SelectedPreset);
        _presetsData.Presets.Remove(SelectedPreset.Preset);
        if (_presetsData.Version >= 2)
            _presetsData.PresetIds.Remove(SelectedPreset.Id);
        Presets.RemoveAt(idx);

        _presetsData.PresetCount = (uint)_presetsData.Presets.Count;
        PresetCount = _presetsData.Presets.Count;
        PresetsFileInfo = $"v{_presetsData.Version} · {_presetsData.Presets.Count} presets";

        SelectedPreset = Presets.Count > 0 ? Presets[Math.Min(idx, Presets.Count - 1)] : null;
        AddLog($"Removed preset at index {idx}");
    }

    private bool CanRemovePreset() => SelectedPreset != null && HasPresetsData;

    // Adds a new preset entry from the loaded JSON hashes.
    [RelayCommand]
    public async Task AddPresetAsync()
    {
        if (_presetsData == null)
        {
            _presetsData = new LightPresetsBinParser.LightPresetsData { Version = 2, PresetCount = 0 };
            HasPresetsData = true;
        }

        if (Fh5HashEntries.Count == 0)
        {
            StatusMessage = "No presets available to add. Ensure LightHashMap_FH5.json is present.";
            return;
        }

        var listView = new ListView
        {
            ItemsSource = Fh5HashEntries,
            DisplayMemberPath = "DisplayText",
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 400
        };

        var dialog = new ContentDialog
        {
            Title = "Add Preset",
            Content = listView,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && listView.SelectedItem is LightHashEntry entry)
        {
            if (string.IsNullOrEmpty(entry.RawBytes))
            {
                StatusMessage = "Selected preset has no raw bytes.";
                return;
            }

            byte[] rawBytes = ParseHexBytes(entry.RawBytes);
            if (rawBytes.Length < 8) return;

            using var ms = new MemoryStream(rawBytes);
            using var bs = new Syroot.BinaryData.BinaryStream(ms, Syroot.BinaryData.ByteConverter.Little);

            uint presetSize = bs.ReadUInt32();
            uint paramCount = bs.ReadUInt32();

            var preset = new LightPresetsBinParser.LightPreset
            {
                Id = entry.HashValue,
                PresetSize = presetSize,
                RawBlockBytes = rawBytes
            };

            for (uint p = 0; p < paramCount && ms.Position < ms.Length; p++)
            {
                byte paramId = bs.Read1Byte();
                byte paramLen = bs.Read1Byte();
                byte[] paramValue = paramLen > 0 && ms.Position + paramLen <= ms.Length
                    ? bs.ReadBytes(paramLen) : [];

                preset.Parameters.Add(new LightPresetsBinParser.LightParameter
                {
                    Id = paramId,
                    Length = paramLen,
                    Value = paramValue
                });
            }

            bool exists = _presetsData.Presets.Any(p => p.Id == preset.Id && preset.Id != 0);
            if (!exists)
            {
                _presetsData.Presets.Add(preset);
                if (_presetsData.Version >= 2)
                    _presetsData.PresetIds.Add(preset.Id);

                _presetsData.PresetCount = (uint)_presetsData.Presets.Count;
                var vm = new PresetEntryViewModel(preset, Presets.Count, _presetsData.Version >= 2);
                Presets.Add(vm);
                SelectedPreset = vm;
                PresetCount = _presetsData.Presets.Count;
                PresetsFileInfo = $"v{_presetsData.Version} · {_presetsData.Presets.Count} presets";

                AddLog($"Added preset {entry.DisplayText}");
            }
            else
            {
                StatusMessage = "Preset already exists.";
            }
        }
    }

    // Opens a single file — zip or bin — and auto-detects its type from magic bytes.
    [RelayCommand]
    public async Task OpenFileAsync()
    {
        var picker = BuildFilePicker();
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".bin");
        picker.CommitButtonText = "Open";

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        string ext = Path.GetExtension(file.Path).ToLowerInvariant();

        if (ext == ".zip")
        {
            await LoadZipFileAsync(file.Path);
            return;
        }

        // Read first 4 bytes to detect magic
        byte[] header = new byte[4];
        using (var fs = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int read = await fs.ReadAsync(header.AsMemory(0, 4));
            if (read < 4)
            {
                StatusMessage = "File is too small to identify.";
                return;
            }
        }

        uint magic = BitConverter.ToUInt32(header, 0);

        const uint MagicLightsBin   = 0xDEADBEEF;
        const uint MagicPresetsBin  = 0x4246444C; // "LDFB"

        if (magic == MagicLightsBin)
        {
            await LoadLightsBinFileAsync(file.Path);
        }
        else if (magic == MagicPresetsBin)
        {
            await LoadPresetsBinFileAsync(file.Path);
        }
        else
        {
            // No recognized magic — could be a version-0 lights.bin (no header).
            // Try lights first; fall back to presets.
            try
            {
                await LoadLightsBinFileAsync(file.Path);
            }
            catch
            {
                try
                {
                    await LoadPresetsBinFileAsync(file.Path);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Could not identify or parse file: {ex.Message}";
                }
            }
        }
    }
}

// Extension helper
file static class EnumerableExtensions
{
    public static ObservableCollection<T> ToObservableCollection<T>(this IEnumerable<T> source)
        => new(source);
}
