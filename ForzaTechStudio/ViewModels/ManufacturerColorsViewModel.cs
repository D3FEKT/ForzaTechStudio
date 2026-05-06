using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using Microsoft.UI;
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
    }

    public override string DisplayName => string.IsNullOrEmpty(Path) ? "[New Entry]" : System.IO.Path.GetFileName(Path);
    public override IEnumerable Children => null;

    public string Path
    {
        get => _entry.Path;
        set
        {
            if (_entry.Path != value)
            {
                _entry.Path = value;
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
        get => Color.FromArgb(255, 
            (byte)Math.Clamp(_entry.PreviewColor.X * 255f, 0, 255),
            (byte)Math.Clamp(_entry.PreviewColor.Y * 255f, 0, 255),
            (byte)Math.Clamp(_entry.PreviewColor.Z * 255f, 0, 255));
        set
        {
            var newVector = new Vector3(value.R / 255f, value.G / 255f, value.B / 255f);
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

    public ManufacturerColorEntry GetEntry() => _entry;
}

public partial class ManufacturerColorGroupViewModel : ManufacturerColorNode
{
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

    public ObservableCollection<ManufacturerColorEntryViewModel> Entries { get; } = new();

    public ManufacturerColorGroupViewModel(string name, ManufacturerColorsViewModel owner)
    {
        _groupName = name;
        Owner = owner;
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

    public ObservableCollection<ManufacturerColorGroupViewModel> Groups { get; } = new();

    private ManufacturerColorEntryViewModel _selectedEntry;
    public ManufacturerColorEntryViewModel SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                OnPropertyChanged(nameof(IsEntrySelected));
            }
        }
    }

    public bool IsEntrySelected => SelectedEntry != null;

    [RelayCommand]
    private void NewFile()
    {
        Groups.Clear();
        _currentBundle = new Bundle();
        _colorsBlob = new ManufacturerColorsBlob();
        _colorsBlob.VersionMajor = 1;
        _colorsBlob.VersionMinor = 1;
        _currentBundle.Blobs.Add(_colorsBlob);
        FileName = "New ManufacturerColors.bin";
        OnPropertyChanged(nameof(IsFileLoaded));
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
            OnPropertyChanged(nameof(IsFileLoaded));

            Groups.Clear();
            for (int i = 0; i < blob.Groups.Count; i++)
            {
                var groupVm = new ManufacturerColorGroupViewModel($"Group {i}", this);
                foreach (var entry in blob.Groups[i].Entries)
                {
                    groupVm.Entries.Add(new ManufacturerColorEntryViewModel(entry, this));
                }
                Groups.Add(groupVm);
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
        SelectedEntry = null;
        OnPropertyChanged(nameof(IsFileLoaded));
    }

    private void SyncBlobFromViewModel()
    {
        _colorsBlob.Groups.Clear();
        foreach (var groupVm in Groups)
        {
            var group = new ManufacturerColorGroup();
            foreach (var entryVm in groupVm.Entries)
            {
                group.Entries.Add(entryVm.GetEntry());
            }
            _colorsBlob.Groups.Add(group);
        }
    }

    [RelayCommand]
    private void AddGroup()
    {
        Groups.Add(new ManufacturerColorGroupViewModel($"Group {Groups.Count}", this));
    }

    [RelayCommand]
    private void RemoveGroup(ManufacturerColorNode node)
    {
        if (node is ManufacturerColorGroupViewModel group)
        {
            Groups.Remove(group);
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
                break;
            }
        }
        SelectedEntry = null;
    }
}
