using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using ForzaTechStudio.Services;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.UI.Xaml;
using System.Buffers;

namespace ForzaTechStudio.ViewModels
{
    public class ZipItem
    {
        public string Name { get; set; }
        public string Type { get; set; } // "File" or "Folder"
        public string FullPath { get; set; } // Source path for new files, relative path for existing zip entries
        public string Icon { get; set; } // Glyph
        
        // New columns for 7-Zip style view
        public string SizeDisplay { get; set; }
        public string PackedSizeDisplay { get; set; }
        public string ModifiedDateDisplay { get; set; }
        public string AttributesDisplay { get; set; }
        public string CompressionMethodDisplay { get; set; } // New Property
        
        public long OriginalSize { get; set; }
        public long CompressedSize { get; set; }

        // Index of this entry in the source archive (zip or minizip). -1 for items not yet in an archive (build mode).
        public int SourceEntryIndex { get; set; } = -1;

        // Raw compression method code from the archive (0=Store, 8=Deflate, 21=LZX, etc.).
        public ushort CompressionMethodCode { get; set; }

        public ObservableCollection<ZipItem> Children { get; set; } = new ObservableCollection<ZipItem>();
        public bool IsExpanded { get; set; } = true;
    }

    public partial class CreateZipViewModel : ObservableObject
    {
        private ZipCreationService _zipService = new ZipCreationService();
        private string _loadedArchiveSourcePath;
        private bool _isMiniZip = false;

        [ObservableProperty]
        private string _zipName = "NewArchive";

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        private bool _isLoading = false;

        partial void OnIsLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsLoadingVisibility));
        }

        private bool _isArchiveLoaded = false;
        public bool IsArchiveLoaded
        {
            get => _isArchiveLoaded;
            set
            {
                if (SetProperty(ref _isArchiveLoaded, value))
                {
                    OnPropertyChanged(nameof(IsBuildMode));
                    OnPropertyChanged(nameof(BuildModeVisibility));
                    OnPropertyChanged(nameof(IsArchiveLoadedVisibility));
                }
            }
        }

        public bool IsBuildMode => !IsArchiveLoaded;
        
        public Visibility BuildModeVisibility => IsBuildMode ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsArchiveLoadedVisibility => IsArchiveLoaded ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsLoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;

        [ObservableProperty]
        private int _selectedFormatIndex = 0; // 0 = Standard, 1 = Forza

        public ObservableCollection<ZipItem> Items { get; } = new();

        public List<string> Formats { get; } = new() { "Standard Zip (Deflate)", "Forza Zip (Store)" };

        [RelayCommand]
        public async Task OpenZipAsync()
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.FileTypeFilter.Add(".zip");
            picker.FileTypeFilter.Add(".minizip");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await LoadZipFileAsync(file.Path, file.Name);
            }
        }

        public async Task LoadZipFileAsync(string filePath, string fileName)
        {
            try 
            {
                Items.Clear();
                _loadedArchiveSourcePath = filePath;
                ZipName = fileName;
                IsArchiveLoaded = true;
                IsLoading = true;
                StatusMessage = $"Loading {fileName}…";

                _isMiniZip = MiniZipService.IsMiniZipFile(filePath);

                if (_isMiniZip)
                {
                    await Task.Run(() =>
                    {
                        using var minizip = new MiniZipService(filePath);
                        var rootItems = BuildTreeFromMiniZipEntries(minizip.Entries);
                        int count = minizip.Entries.Count;
                        uint version = minizip.Version;
                        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                        {
                            foreach (var item in rootItems) Items.Add(item);
                            StatusMessage = $"Loaded {fileName} \u2014 {count} entries (MiniZip v{version})";
                            IsLoading = false;
                        });
                    });
                }
                else
                {
                    await Task.Run(() =>
                    {
                        using var customZip = new CustomZipFile(filePath);
                        var entries = customZip.GetEntries();
                        var rootItems = BuildTreeFromEntries(entries);
                        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                        {
                            foreach (var item in rootItems) Items.Add(item);
                            StatusMessage = $"Loaded {fileName}";
                            IsLoading = false;
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening zip: {ex.Message}";
                IsArchiveLoaded = false;
                IsLoading = false;
            }
        }

        private List<ZipItem> BuildTreeFromMiniZipEntries(List<MiniZipEntryInfo> entries)
        {
            var rootItems = new List<ZipItem>();
            var pathLookup = new Dictionary<string, ZipItem>();

            foreach (var entry in entries.OrderBy(e => e.Name))
            {
                string fullPath = entry.Name.Replace('\\', '/').TrimEnd('/');
                string[] parts = fullPath.Split('/');

                ZipItem currentParent = null;
                string currentPath = "";

                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i];
                    string partPath = string.IsNullOrEmpty(currentPath) ? part : currentPath + "/" + part;
                    bool isFile = i == parts.Length - 1;

                    if (!pathLookup.TryGetValue(partPath, out var existingItem))
                    {
                        var newItem = new ZipItem
                        {
                            Name = part,
                            FullPath = partPath,
                            Type = isFile ? entry.ResourceType.ToString() : "Folder",
                            Icon = isFile ? "\uE8A5" : "\uE8B7",
                            SizeDisplay = isFile ? FormatSize(entry.UncompressedSize) : "",
                            PackedSizeDisplay = isFile ? FormatSize(entry.CompressedSize) : "",
                            ModifiedDateDisplay = "",
                            AttributesDisplay = isFile ? "A" : "D",
                            CompressionMethodDisplay = isFile ? (entry.CompressMethod == 0 ? "Store" : entry.CompressMethod == 8 ? "Deflate" : $"Method {entry.CompressMethod}") : "",
                            OriginalSize = entry.UncompressedSize,
                            CompressedSize = entry.CompressedSize,
                            SourceEntryIndex = isFile ? entry.Index : -1,
                            CompressionMethodCode = isFile ? entry.CompressMethod : (ushort)0,
                        };

                        pathLookup[partPath] = newItem;

                        if (currentParent == null)
                            rootItems.Add(newItem);
                        else
                            currentParent.Children.Add(newItem);

                        existingItem = newItem;
                    }

                    currentParent = existingItem;
                    currentPath = partPath;
                }
            }

            return rootItems;
        }
        
        private List<ZipItem> BuildTreeFromEntries(List<CustomZipFile.ZipEntryInfo> entries)
        {
            var rootItems = new List<ZipItem>();
            var pathLookup = new Dictionary<string, ZipItem>();

            // Sort entries to ensure folders are processed before files if possible, or handle missing folders
            // Actually, zip entries for folders might not exist explicitly.
            
            // Normalize paths and ensure hierarchy
            // Track original indices before sorting so we can store them on leaf items
            var indexedEntries = entries.Select((e, i) => (Entry: e, OriginalIndex: i)).OrderBy(t => t.Entry.Name);

            foreach (var (entry, originalIndex) in indexedEntries)
            {
                string fullPath = entry.Name.Replace('\\', '/').TrimEnd('/');
                string[] parts = fullPath.Split('/');
                
                ZipItem currentParent = null; 
                string currentPath = "";

                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i];
                    string partPath = string.IsNullOrEmpty(currentPath) ? part : currentPath + "/" + part;
                    bool isFile = (i == parts.Length - 1) && !entry.IsDirectory;
                    
                    if (!pathLookup.TryGetValue(partPath, out var existingItem))
                    {
                        var newItem = new ZipItem
                        {
                            Name = part,
                            FullPath = partPath,
                            Type = isFile ? "File" : "Folder",
                            Icon = isFile ? "\uE8A5" : "\uE8B7",
                            SizeDisplay = isFile ? FormatSize(entry.UncompressedSize) : "",
                            PackedSizeDisplay = isFile ? FormatSize(entry.CompressedSize) : "",
                            ModifiedDateDisplay = entry.LastModified.ToString("yyyy-MM-dd HH:mm"),
                            AttributesDisplay = isFile ? "A" : "D",
                            CompressionMethodDisplay = isFile ? entry.CompressionMethod.ToString() : "",
                            OriginalSize = entry.UncompressedSize,
                            CompressedSize = entry.CompressedSize,
                            SourceEntryIndex = isFile ? originalIndex : -1,
                            CompressionMethodCode = isFile ? entry.CompressionMethod : (ushort)0,
                        };

                        pathLookup[partPath] = newItem;

                        if (currentParent == null)
                        {
                            rootItems.Add(newItem);
                        }
                        else
                        {
                            currentParent.Children.Add(newItem);
                        }
                        existingItem = newItem;
                    }
                    else
                    {
                        // If we encounter an explicit entry for a folder we auto-created, update details
                        if (i == parts.Length - 1 && entry.IsDirectory)
                        {
                            existingItem.ModifiedDateDisplay = entry.LastModified.ToString("yyyy-MM-dd HH:mm");
                        }
                    }

                    currentParent = existingItem;
                    currentPath = partPath;
                }
            }

            return rootItems;
        }

        [RelayCommand]
        public async Task ExtractZipAsync()
        {
            if (!IsArchiveLoaded || string.IsNullOrEmpty(_loadedArchiveSourcePath)) return;

            var picker = new FolderPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                StatusMessage = "Extracting...";
                try
                {
                    if (_isMiniZip)
                    {
                        await Task.Run(() =>
                        {
                            using var minizip = new MiniZipService(_loadedArchiveSourcePath);
                            minizip.ExtractAll(folder.Path);
                        });
                    }
                    else
                    {
                        await Task.Run(() =>
                        {
                            using (var customZip = new CustomZipFile(_loadedArchiveSourcePath))
                            {
                                customZip.ExtractToDirectory(folder.Path);
                            }
                        });
                    }
                    StatusMessage = "Extraction Complete.";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error extracting: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public void CreateNewSession()
        {
            Items.Clear();
            IsArchiveLoaded = false;
            _isMiniZip = false;
            ZipName = "NewArchive";
            StatusMessage = "Ready to build new archive.";
            _loadedArchiveSourcePath = null;
        }

        [RelayCommand]
        public async Task AddFilesAsync()
        {
            if (IsArchiveLoaded) return; // Disable adding to read-only loaded archives for now

            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.FileTypeFilter.Add("*");

            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files)
            {
                var props = await file.GetBasicPropertiesAsync();
                Items.Add(new ZipItem
                {
                    Name = file.Name,
                    Type = "File",
                    FullPath = file.Path,
                    Icon = "\uE8A5", // Document Icon
                    SizeDisplay = FormatSize((long)props.Size),
                    PackedSizeDisplay = "", // Unknown until compressed
                    ModifiedDateDisplay = props.DateModified.ToString("yyyy-MM-dd HH:mm"),
                    AttributesDisplay = "A"
                });
            }
        }

        [RelayCommand]
        public async Task AddFolderAsync()
        {
            if (IsArchiveLoaded) return;

            var picker = new FolderPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                await AddFolderItemAsync(folder.Name, folder.Path);
            }
        }

        public async Task AddFolderItemAsync(string folderName, string folderPath)
        {
            var rootItem = new ZipItem
            {
                Name = folderName,
                Type = "Folder",
                FullPath = folderPath,
                Icon = "\uE8B7", 
                SizeDisplay = "",
                PackedSizeDisplay = "",
                ModifiedDateDisplay = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                AttributesDisplay = "D"
            };

            // Recursively populate children
            await Task.Run(() => PopulateChildren(rootItem, folderPath));
            
            Items.Add(rootItem);
        }

        public async Task AddFileItemAsync(Windows.Storage.StorageFile file)
        {
            var props = await file.GetBasicPropertiesAsync();
            Items.Add(new ZipItem
            {
                Name = file.Name,
                Type = "File",
                FullPath = file.Path,
                Icon = "\uE8A5", // Document Icon
                SizeDisplay = FormatSize((long)props.Size),
                PackedSizeDisplay = "", // Unknown until compressed
                ModifiedDateDisplay = props.DateModified.ToString("yyyy-MM-dd HH:mm"),
                AttributesDisplay = "A"
            });
        }

        private void PopulateChildren(ZipItem parent, string dirPath)
        {
            try
            {
                var dirInfo = new DirectoryInfo(dirPath);

                // Directories
                foreach (var dir in dirInfo.GetDirectories())
                {
                    var folderItem = new ZipItem
                    {
                        Name = dir.Name,
                        Type = "Folder",
                        FullPath = dir.FullName,
                        Icon = "\uE8B7",
                        SizeDisplay = "",
                        PackedSizeDisplay = "",
                        ModifiedDateDisplay = dir.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        AttributesDisplay = "D"
                    };
                    parent.Children.Add(folderItem);
                    PopulateChildren(folderItem, dir.FullName);
                }

                // Files
                foreach (var file in dirInfo.GetFiles())
                {
                    var fileItem = new ZipItem
                    {
                        Name = file.Name,
                        Type = "File",
                        FullPath = file.FullName,
                        Icon = "\uE8A5",
                        SizeDisplay = FormatSize(file.Length),
                        PackedSizeDisplay = "",
                        ModifiedDateDisplay = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        AttributesDisplay = "A",
                        OriginalSize = file.Length
                    };
                    parent.Children.Add(fileItem);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error populating children for {dirPath}: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task CreateZipAsync()
        {
            if (Items.Count == 0)
            {
                StatusMessage = "No files selected.";
                return;
            }

            var picker = new FileSavePicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.SuggestedFileName = ZipName;

            if (SelectedFormatIndex == 0)
                picker.FileTypeChoices.Add("Zip Archive", new List<string>() { ".zip" });
            else
                picker.FileTypeChoices.Add("Forza Archive", new List<string>() { ".zip", ".minizip" });

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                StatusMessage = "Creating Zip...";
                try
                {
                    var fileList = new List<string>();
                    var folderList = new List<string>();

                    foreach (var item in Items)
                    {
                        // Top-level items only: files go to fileList, folders go to folderList.
                        if (item.Type == "File") fileList.Add(item.FullPath);
                        else folderList.Add(item.FullPath);
                    }

                    if (SelectedFormatIndex == 0)
                    {
                        await _zipService.CreateStandardZipAsync(file.Path, fileList, folderList);
                    }
                    else
                    {
                        await _zipService.CreateForzaZipAsync(file.Path, fileList, folderList);
                    }

                    StatusMessage = "Zip Created Successfully!";
                     // Optionally load it after creation?
                     // await OpenZipFromPath(file.Path); 
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public void ClearList()
        {
            Items.Clear();
            StatusMessage = "";
        }
        
        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        [RelayCommand]
        public async Task ExtractItemAsync(ZipItem item)
        {
            if (item == null || !IsArchiveLoaded || string.IsNullOrEmpty(_loadedArchiveSourcePath)) return;

            var picker = new FolderPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                StatusMessage = $"Extracting {item.Name}...";
                try
                {
                    if (_isMiniZip)
                    {
                        await Task.Run(() =>
                        {
                            using var minizip = new MiniZipService(_loadedArchiveSourcePath);
                            string normTarget = item.FullPath.Replace('\\', '/');
                            foreach (var entry in minizip.Entries)
                            {
                                string normEntry = entry.Name.Replace('\\', '/');
                                bool match = item.Type == "Folder"
                                    ? normEntry.StartsWith(normTarget + "/") || normEntry == normTarget
                                    : normEntry == normTarget;
                                if (match)
                                    minizip.ExtractEntry(entry.Index, folder.Path);
                            }
                        });
                    }
                    else
                    {
                        await Task.Run(() =>
                        {
                            using (var customZip = new CustomZipFile(_loadedArchiveSourcePath))
                            {
                                Func<string, bool> filter = (entryName) =>
                                {
                                    string normEntry = entryName.Replace('\\', '/');
                                    string normTarget = item.FullPath.Replace('\\', '/');
                                    if (item.Type == "Folder")
                                        return normEntry.StartsWith(normTarget + "/") || normEntry == normTarget;
                                    else
                                        return normEntry == normTarget;
                                };
                                customZip.ExtractToDirectory(folder.Path, filter);
                            }
                        });
                    }
                    StatusMessage = "Extraction Complete.";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error extracting item: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        public async Task ReplaceItemAsync(ZipItem item)
        {
            if (item == null || item.SourceEntryIndex < 0 || !IsArchiveLoaded || string.IsNullOrEmpty(_loadedArchiveSourcePath)) return;

            // Only file entries can be replaced
            if (item.Type == "Folder") return;

            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.ViewMode = PickerViewMode.List;
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            StatusMessage = $"Replacing {item.Name}…";
            try
            {
                int entryIndex = item.SourceEntryIndex;
                string sourcePath = _loadedArchiveSourcePath;
                string replacementPath = file.Path;

                await Task.Run(() =>
                {
                    if (_isMiniZip)
                        MiniZipService.ReplaceEntry(sourcePath, entryIndex, replacementPath, sourcePath);
                    else
                        CustomZipFile.ReplaceEntry(sourcePath, entryIndex, replacementPath, sourcePath);
                });

                // Reload the archive to reflect the changes
                StatusMessage = $"Replaced {item.Name}. Reloading…";
                await LoadZipFileAsync(sourcePath, ZipName);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error replacing {item.Name}: {ex.Message}";
            }
        }
    }
}
