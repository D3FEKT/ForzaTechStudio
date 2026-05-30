using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.UI;

namespace ForzaTechStudio.ViewModels;

public abstract partial class ManufacturerColorNode : ObservableObject
{
    public abstract string DisplayName { get; }
    public abstract IEnumerable Children { get; }

    [ObservableProperty]
    private ManufacturerColorsViewModel _owner;

    public Symbol Icon => Children != null ? Symbol.Folder : Symbol.Document;
    public Visibility GroupVisibility => Children != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EntryVisibility => Children == null ? Visibility.Visible : Visibility.Collapsed;
}

public partial class ManufacturerColorEntryViewModel : ManufacturerColorNode
{
    private readonly ManufacturerColorEntry _entry;

    public ManufacturerColorEntryViewModel(ManufacturerColorEntry entry, ManufacturerColorsViewModel owner)
    {
        _entry = entry;
        Owner = owner;

        foreach (var name in entry.MaterialNames)
            Parts.Add(new PartNameEntry { Name = name });
    }

    public override string DisplayName => string.IsNullOrEmpty(Path) ? "[New Entry]" : System.IO.Path.GetFileName(Path);
    public override IEnumerable Children => null;
    public bool UsesMaterialNames => Owner?.IsFh6VersionLoaded == true;
    public bool UsesMaterialIndexMask => !UsesMaterialNames;

    public ObservableCollection<PartNameEntry> Parts { get; } = new();

    private PartNameEntry _selectedPart;
    public PartNameEntry SelectedPart
    {
        get => _selectedPart;
        set => SetProperty(ref _selectedPart, value);
    }

    [RelayCommand]
    private void AddPart()
    {
        var entry = new PartNameEntry();
        Parts.Add(entry);
        SelectedPart = entry;
    }

    [RelayCommand]
    private void RemovePart(PartNameEntry part)
    {
        if (part != null)
            Parts.Remove(part);
    }

    public string Path
    {
        get => _entry.Path;
        set
        {
            string newValue = value ?? string.Empty;
            if (_entry.Path != newValue)
            {
                _entry.Path = newValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public uint MaterialIndexMask
    {
        get => _entry.MaterialIndexMask;
        set
        {
            if (_entry.MaterialIndexMask != value)
            {
                _entry.MaterialIndexMask = value;
                OnPropertyChanged();
            }
        }
    }

    public Color UiColor
    {
        get => ToUiColor(_entry.PreviewColor);
        set
        {
            var newVector = ToVector3(value);
            if (_entry.PreviewColor != newVector)
            {
                _entry.PreviewColor = newVector;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewColorX));
                OnPropertyChanged(nameof(PreviewColorY));
                OnPropertyChanged(nameof(PreviewColorZ));
            }
        }
    }

    public float PreviewColorX
    {
        get => _entry.PreviewColor.X;
        set
        {
            if (_entry.PreviewColor.X != value)
            {
                _entry.PreviewColor = new Vector3(value, _entry.PreviewColor.Y, _entry.PreviewColor.Z);
                OnPropertyChanged();
                OnPropertyChanged(nameof(UiColor));
            }
        }
    }

    public float PreviewColorY
    {
        get => _entry.PreviewColor.Y;
        set
        {
            if (_entry.PreviewColor.Y != value)
            {
                _entry.PreviewColor = new Vector3(_entry.PreviewColor.X, value, _entry.PreviewColor.Z);
                OnPropertyChanged();
                OnPropertyChanged(nameof(UiColor));
            }
        }
    }

    public float PreviewColorZ
    {
        get => _entry.PreviewColor.Z;
        set
        {
            if (_entry.PreviewColor.Z != value)
            {
                _entry.PreviewColor = new Vector3(_entry.PreviewColor.X, _entry.PreviewColor.Y, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(UiColor));
            }
        }
    }

    private static Color ToUiColor(Vector3 value)
    {
        return Color.FromArgb(255,
            (byte)Math.Clamp(value.X * 255f, 0, 255),
            (byte)Math.Clamp(value.Y * 255f, 0, 255),
            (byte)Math.Clamp(value.Z * 255f, 0, 255));
    }

    private static Vector3 ToVector3(Color value)
    {
        return new Vector3(value.R / 255f, value.G / 255f, value.B / 255f);
    }

    public ManufacturerColorEntry GetEntry()
    {
        if (UsesMaterialNames)
            _entry.MaterialNames = Parts.Select(p => p.Name).ToList();
        return _entry;
    }
}

public partial class PartNameEntry : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;
}

public partial class ManufacturerColorGroupViewModel : ManufacturerColorNode
{
    private readonly ManufacturerColorGroup _group;
    private string _groupName;

    public string GroupName
    {
        get => _groupName;
        set
        {
            if (SetProperty(ref _groupName, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public override string DisplayName => GroupName;
    public override IEnumerable Children => Entries;
    public int EntryCount => Entries.Count;
    public bool UsesFh6Fields => Owner?.IsFh6VersionLoaded == true;

    public ObservableCollection<ManufacturerColorEntryViewModel> Entries { get; } = new();

    public ManufacturerColorGroupViewModel(ManufacturerColorGroup group, string name, ManufacturerColorsViewModel owner)
    {
        _group = group;
        _groupName = name;
        Owner = owner;

        foreach (var entry in group.Entries)
            Entries.Add(new ManufacturerColorEntryViewModel(entry, owner));

        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(EntryCount));
    }

    public int PrimaryGroupPreviewPresent
    {
        get => _group.PrimaryGroupPreviewPresent;
        set => SetByteProperty(_group.PrimaryGroupPreviewPresent, value, newValue => _group.PrimaryGroupPreviewPresent = newValue);
    }

    public int GroupPreviewZero
    {
        get => _group.GroupPreviewZero;
        set => SetByteProperty(_group.GroupPreviewZero, value, newValue => _group.GroupPreviewZero = newValue);
    }

    public int SecondaryGroupPreviewPresent
    {
        get => _group.SecondaryGroupPreviewPresent;
        set => SetByteProperty(_group.SecondaryGroupPreviewPresent, value, newValue => _group.SecondaryGroupPreviewPresent = newValue);
    }

    public Color PrimaryGroupUiColor
    {
        get => ToUiColor(_group.PrimaryGroupPreviewColor);
        set
        {
            Vector3 newValue = ToVector3(value);
            if (_group.PrimaryGroupPreviewColor != newValue)
            {
                _group.PrimaryGroupPreviewColor = newValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PrimaryGroupPreviewColorX));
                OnPropertyChanged(nameof(PrimaryGroupPreviewColorY));
                OnPropertyChanged(nameof(PrimaryGroupPreviewColorZ));
            }
        }
    }

    public float PrimaryGroupPreviewColorX
    {
        get => _group.PrimaryGroupPreviewColor.X;
        set => SetVectorComponent(_group.PrimaryGroupPreviewColor, value, static (vector, component) => new Vector3(component, vector.Y, vector.Z), newValue => _group.PrimaryGroupPreviewColor = newValue, nameof(PrimaryGroupUiColor));
    }

    public float PrimaryGroupPreviewColorY
    {
        get => _group.PrimaryGroupPreviewColor.Y;
        set => SetVectorComponent(_group.PrimaryGroupPreviewColor, value, static (vector, component) => new Vector3(vector.X, component, vector.Z), newValue => _group.PrimaryGroupPreviewColor = newValue, nameof(PrimaryGroupUiColor));
    }

    public float PrimaryGroupPreviewColorZ
    {
        get => _group.PrimaryGroupPreviewColor.Z;
        set => SetVectorComponent(_group.PrimaryGroupPreviewColor, value, static (vector, component) => new Vector3(vector.X, vector.Y, component), newValue => _group.PrimaryGroupPreviewColor = newValue, nameof(PrimaryGroupUiColor));
    }

    public Color SecondaryGroupUiColor
    {
        get => ToUiColor(_group.SecondaryGroupPreviewColor);
        set
        {
            Vector3 newValue = ToVector3(value);
            if (_group.SecondaryGroupPreviewColor != newValue)
            {
                _group.SecondaryGroupPreviewColor = newValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SecondaryGroupPreviewColorX));
                OnPropertyChanged(nameof(SecondaryGroupPreviewColorY));
                OnPropertyChanged(nameof(SecondaryGroupPreviewColorZ));
            }
        }
    }

    public float SecondaryGroupPreviewColorX
    {
        get => _group.SecondaryGroupPreviewColor.X;
        set => SetVectorComponent(_group.SecondaryGroupPreviewColor, value, static (vector, component) => new Vector3(component, vector.Y, vector.Z), newValue => _group.SecondaryGroupPreviewColor = newValue, nameof(SecondaryGroupUiColor));
    }

    public float SecondaryGroupPreviewColorY
    {
        get => _group.SecondaryGroupPreviewColor.Y;
        set => SetVectorComponent(_group.SecondaryGroupPreviewColor, value, static (vector, component) => new Vector3(vector.X, component, vector.Z), newValue => _group.SecondaryGroupPreviewColor = newValue, nameof(SecondaryGroupUiColor));
    }

    public float SecondaryGroupPreviewColorZ
    {
        get => _group.SecondaryGroupPreviewColor.Z;
        set => SetVectorComponent(_group.SecondaryGroupPreviewColor, value, static (vector, component) => new Vector3(vector.X, vector.Y, component), newValue => _group.SecondaryGroupPreviewColor = newValue, nameof(SecondaryGroupUiColor));
    }

    public ManufacturerColorGroup GetGroup()
    {
        _group.Entries = Entries.Select(entry => entry.GetEntry()).ToList();
        return _group;
    }

    private void SetByteProperty(byte currentValue, int requestedValue, Action<byte> setValue)
    {
        byte newValue = (byte)Math.Clamp(requestedValue, 0, byte.MaxValue);
        if (currentValue == newValue)
            return;

        setValue(newValue);
        OnPropertyChanged();
    }

    private void SetVectorComponent(Vector3 currentValue, float componentValue, Func<Vector3, float, Vector3> update, Action<Vector3> setValue, string colorPropertyName)
    {
        Vector3 newValue = update(currentValue, componentValue);
        if (currentValue == newValue)
            return;

        setValue(newValue);
        OnPropertyChanged();
        OnPropertyChanged(colorPropertyName);
    }

    private static Color ToUiColor(Vector3 value)
    {
        return Color.FromArgb(255,
            (byte)Math.Clamp(value.X * 255f, 0, 255),
            (byte)Math.Clamp(value.Y * 255f, 0, 255),
            (byte)Math.Clamp(value.Z * 255f, 0, 255));
    }

    private static Vector3 ToVector3(Color value)
    {
        return new Vector3(value.R / 255f, value.G / 255f, value.B / 255f);
    }
}

public partial class ManufacturerColorsViewModel : ObservableObject
{
    private Bundle _currentBundle;
    private ManufacturerColorsBlob _colorsBlob;

    private string _fileName;
    public string FileName
    {
        get => _fileName;
        set
        {
            if (SetProperty(ref _fileName, value))
                OnPropertyChanged(nameof(IsFileLoaded));
        }
    }

    public bool IsFileLoaded => _colorsBlob != null;
    public bool IsFh6VersionLoaded => _colorsBlob?.IsAtLeastVersion(2, 0) == true;
    public string BlobVersionText => _colorsBlob == null ? string.Empty : $"Blob v{_colorsBlob.VersionMajor}.{_colorsBlob.VersionMinor}";

    public ObservableCollection<ManufacturerColorGroupViewModel> Groups { get; } = new();

    private ManufacturerColorGroupViewModel _selectedGroup;
    public ManufacturerColorGroupViewModel SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
                NotifySelectionStateChanged();
        }
    }

    private ManufacturerColorEntryViewModel _selectedEntry;
    public ManufacturerColorEntryViewModel SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
                NotifySelectionStateChanged();
        }
    }

    public bool IsEntrySelected => SelectedEntry != null;
    public bool IsGroupSelected => SelectedGroup != null;
    public bool ShowGroupDetails => SelectedGroup != null;
    public bool ShowEmptyDetailState => SelectedEntry == null && SelectedGroup == null;

    // Called from code-behind after the user picks a version.
    public void NewFileWithVersion(bool isFh6)
    {
        Groups.Clear();
        _currentBundle = new Bundle();
        _colorsBlob = new ManufacturerColorsBlob();
        _colorsBlob.VersionMajor = (byte)(isFh6 ? 2 : 1);
        _colorsBlob.VersionMinor = 0;
        _currentBundle.Blobs.Add(_colorsBlob);
        FileName = "New ManufacturerColors.bin";
        SelectedEntry = null;
        SelectedGroup = null;
        NotifyLoadedStateChanged();
        AddGroup();
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var picker = new FileOpenPicker();
        var hWnd = App.MainWindowHandle;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add(".bin");

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        await LoadManufacturerColorsFileAsync(file.Path);
    }

    public async Task LoadManufacturerColorsFileAsync(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var bundle = new Bundle();
            bundle.Load(stream);

            var blob = bundle.Blobs.OfType<ManufacturerColorsBlob>().FirstOrDefault();
            if (blob == null)
            {
                App.ShowErrorDialog("File does not contain Manufacturer Colors data.");
                return;
            }

            _currentBundle = bundle;
            _colorsBlob = blob;
            FileName = Path.GetFileName(filePath);
            SelectedEntry = null;
            SelectedGroup = null;
            NotifyLoadedStateChanged();

            Groups.Clear();
            for (int i = 0; i < blob.Groups.Count; i++)
            {
                Groups.Add(new ManufacturerColorGroupViewModel(blob.Groups[i], $"Group {i}", this));
            }
        }
        catch (Exception ex)
        {
            App.ShowErrorDialog($"Error opening file: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveFileAsync()
    {
        if (_colorsBlob == null) return;

        var picker = new FileSavePicker();
        var hWnd = App.MainWindowHandle;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeChoices.Add("Binary File", new List<string> { ".bin" });
        picker.SuggestedFileName = FileName;

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        try
        {
            SyncBlobFromViewModel();
            using var stream = await file.OpenStreamForWriteAsync();
            stream.SetLength(0);
            _currentBundle.Serialize(stream);
            FileName = file.Name;
        }
        catch (Exception ex)
        {
            App.ShowErrorDialog($"Error saving file: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CloseFile()
    {
        Groups.Clear();
        _currentBundle = null;
        _colorsBlob = null;
        FileName = null;
        SelectedGroup = null;
        SelectedEntry = null;
        NotifyLoadedStateChanged();
    }

    private void SyncBlobFromViewModel()
    {
        _colorsBlob.Groups.Clear();
        foreach (var groupVm in Groups)
            _colorsBlob.Groups.Add(groupVm.GetGroup());
    }

    [RelayCommand]
    private void AddGroup()
    {
        var group = new ManufacturerColorGroup();
        var groupVm = new ManufacturerColorGroupViewModel(group, $"Group {Groups.Count}", this);
        Groups.Add(groupVm);
        SelectedGroup = groupVm;
        SelectedEntry = null;
    }

    [RelayCommand]
    private void RemoveGroup(ManufacturerColorNode node)
    {
        if (node is ManufacturerColorGroupViewModel group)
        {
            Groups.Remove(group);
            if (SelectedGroup == group)
                SelectedGroup = null;
            // Re-index names
            for (int i = 0; i < Groups.Count; i++)
            {
                Groups[i].GroupName = $"Group {i}";
            }
        }
    }

    [RelayCommand]
    private async Task AddEntry(ManufacturerColorNode node)
    {
        if (node is not ManufacturerColorGroupViewModel group) return;

        var picker = new FileOpenPicker();
        var hWnd = App.MainWindowHandle;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add(".materialbin");

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        var entry = new ManufacturerColorEntry { Path = file.Path };
        var entryVm = new ManufacturerColorEntryViewModel(entry, this);
        group.Entries.Add(entryVm);
        SelectedGroup = null;
        SelectedEntry = entryVm;
    }

    [RelayCommand]
    private void RemoveEntry(ManufacturerColorNode node)
    {
        if (node is not ManufacturerColorEntryViewModel entry) return;
        
        foreach (var group in Groups)
        {
            if (group.Entries.Contains(entry))
            {
                group.Entries.Remove(entry);
                SelectedGroup = group;
                break;
            }
        }
        SelectedEntry = null;
    }

    private void NotifyLoadedStateChanged()
    {
        OnPropertyChanged(nameof(IsFileLoaded));
        OnPropertyChanged(nameof(IsFh6VersionLoaded));
        OnPropertyChanged(nameof(BlobVersionText));
    }

    private void NotifySelectionStateChanged()
    {
        OnPropertyChanged(nameof(IsEntrySelected));
        OnPropertyChanged(nameof(IsGroupSelected));
        OnPropertyChanged(nameof(ShowGroupDetails));
        OnPropertyChanged(nameof(ShowEmptyDetailState));
    }
}
