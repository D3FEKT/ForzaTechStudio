using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.Foundation;
using Microsoft.UI.Input;
using DurangoTypes;

namespace ForzaTechStudio.Views;

public sealed partial class SwatchbinEditorPage : Page
{
    private readonly SwatchbinService _swatchbinService = new();
    private readonly Services.SwatchbinConversionService _swatchbinConversionService = new();
    private SwatchbinInfo? _currentSwatchbin;
    private string? _lastDecodeError;
    private bool _isZoomingFromCode = false;
    
    // Panning variables
    private Point _lastMousePosition;
    private bool _isDragging = false;

    // Multi-file support
    private readonly List<SwatchbinFileEntry> _loadedEntries = new();
    private readonly HashSet<string> _tempExtractionDirs = new(StringComparer.OrdinalIgnoreCase);
    private bool _isChangingSelection = false;
    private CancellationTokenSource? _loadCts;

    // Loaded image dimensions
    private double _loadedImageWidth;
    private double _loadedImageHeight;

    // Preview display toggles
    private bool _showAlpha = false;
    private bool _showMipMaps = false;

    // Cached linear (post-detile) texture data for re-rendering on toggle
    private byte[]? _lastLinearData = null;

    public SwatchbinEditorPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not string filePath || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        await LoadMultipleSwatchbinFilesAsync([filePath]);
    }

    // Represents a loaded texture entry in the ComboBox list.
    private class SwatchbinFileEntry
    {
        public string DisplayName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;

        public override string ToString() => DisplayName;
    }

    private string CreateTempExtractionDir()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ForzaSwatchbin_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _tempExtractionDirs.Add(tempDir);
        return tempDir;
    }

    private void CleanupTempDir(string tempDir)
    {
        _tempExtractionDirs.Remove(tempDir);

        if (!Directory.Exists(tempDir))
            return;

        try { Directory.Delete(tempDir, true); } catch { }
    }

    private void CleanupTempDirs()
    {
        foreach (var tempDir in _tempExtractionDirs.ToList())
            CleanupTempDir(tempDir);
    }

    private static string SanitizeFileStem(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] sanitized = value
            .Select(ch => invalidChars.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch)
            .ToArray();

        string result = new string(sanitized).Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "texture" : result;
    }

    private static string CreateBundleFileStem(string sourceName)
    {
        string withoutExtension = Path.ChangeExtension(sourceName, null) ?? sourceName;
        return SanitizeFileStem(withoutExtension);
    }

    private static string MakeUniqueName(string baseName, ISet<string> usedNames)
    {
        string candidate = baseName;
        int suffix = 2;

        while (!usedNames.Add(candidate))
            candidate = $"{baseName}_{suffix++}";

        return candidate;
    }

    private static string GetTextureEntryName(TextureContentBlob textureBlob, int index)
    {
        var idMetadata = textureBlob.GetMetadataByTag<IdentifierMetadata>(BundleMetadata.TAG_METADATA_Identifier);
        if (idMetadata != null)
            return $"tex_0x{idMetadata.Id:X8}";

        var txchMetadata = textureBlob.GetMetadataByTag<TextureContentHeaderMetadata>(BundleMetadata.TAG_METADATA_TextureContentHeader);
        if (txchMetadata != null)
        {
            txchMetadata.ParseWithBlobVersion(textureBlob.VersionMajor, textureBlob.VersionMinor);

            Guid textureId = txchMetadata.PCHeader?.Id ?? txchMetadata.DurangoHeader?.Id ?? Guid.Empty;
            if (textureId != Guid.Empty)
                return $"tex_{textureId:N}";
        }

        return $"tex{index:D2}";
    }

    private static List<SwatchbinFileEntry> ExtractTextureBundleEntries(Stream bundleStream, string tempDir, string displayPrefix, string sourceName)
    {
        var bundle = new Bundle();
        bundle.Load(bundleStream);
        return ExtractTextureBundleEntries(bundle, tempDir, displayPrefix, sourceName);
    }

    private static List<SwatchbinFileEntry> ExtractTextureBundleEntries(Bundle bundle, string tempDir, string displayPrefix, string sourceName)
    {
        var textureBlobs = bundle.Blobs.OfType<TextureContentBlob>().ToList();
        var extractedEntries = new List<SwatchbinFileEntry>(textureBlobs.Count);

        string fileStem = CreateBundleFileStem(sourceName);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < textureBlobs.Count; index++)
        {
            var textureBlob = textureBlobs[index];
            string textureName = MakeUniqueName(GetTextureEntryName(textureBlob, index), usedNames);
            string outputFileName = $"{fileStem}_{SanitizeFileStem(textureName)}.swatchbin";
            string outputPath = Path.Combine(tempDir, outputFileName);

            var singleTextureBundle = new Bundle
            {
                VersionMajor = bundle.VersionMajor,
                VersionMinor = bundle.VersionMinor
            };
            singleTextureBundle.Blobs.Add(textureBlob);

            using var outputStream = File.Create(outputPath);
            singleTextureBundle.SerializeConverted(outputStream);

            extractedEntries.Add(new SwatchbinFileEntry
            {
                DisplayName = $"{displayPrefix} {textureName}",
                FilePath = outputPath
            });
        }

        return extractedEntries;
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var files = items.OfType<Windows.Storage.StorageFile>().ToList();

            var swatchbinFiles = files.Where(f => f.Path.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase)).ToList();
            var pbFiles = files.Where(f => f.Path.EndsWith(".pb", StringComparison.OrdinalIgnoreCase)).ToList();
            var zipFiles = files.Where(f => f.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
            var miniZipFiles = files.Where(f => f.Path.EndsWith(".minizip", StringComparison.OrdinalIgnoreCase)).ToList();

            if (zipFiles.Count > 0)
            {
                foreach (var zipFile in zipFiles)
                    await LoadZipFileAsync(zipFile.Path);
            }

            if (miniZipFiles.Count > 0)
            {
                foreach (var mzFile in miniZipFiles)
                    await LoadMiniZipFileAsync(mzFile.Path);
            }

            if (pbFiles.Count > 0)
            {
                foreach (var pbFile in pbFiles)
                    await LoadPbFileAsync(pbFile.Path);
            }

            if (swatchbinFiles.Count > 0)
            {
                await LoadMultipleSwatchbinFilesAsync(swatchbinFiles.Select(f => f.Path).ToList());
            }
        }
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        
        var window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add(".swatchbin");
        picker.FileTypeFilter.Add(".pb");
        picker.FileTypeFilter.Add(".zip");
        picker.FileTypeFilter.Add(".minizip");

        var files = await picker.PickMultipleFilesAsync();
        if (files == null || files.Count == 0) return;

        var swatchbinFiles = files.Where(f => f.Path.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase)).ToList();
    var pbFiles = files.Where(f => f.Path.EndsWith(".pb", StringComparison.OrdinalIgnoreCase)).ToList();
        var zipFiles = files.Where(f => f.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        var miniZipFiles = files.Where(f => f.Path.EndsWith(".minizip", StringComparison.OrdinalIgnoreCase)).ToList();

        // Process zip files first
        foreach (var zipFile in zipFiles)
            await LoadZipFileAsync(zipFile.Path);

        // Process minizip files
        foreach (var mzFile in miniZipFiles)
            await LoadMiniZipFileAsync(mzFile.Path);

        // Process loose .pb bundle files
        foreach (var pbFile in pbFiles)
            await LoadPbFileAsync(pbFile.Path);

        // Then process individual swatchbin files
        if (swatchbinFiles.Count > 0)
        {
            await LoadMultipleSwatchbinFilesAsync(swatchbinFiles.Select(f => f.Path).ToList());
        }
    }

    private async Task LoadPbFileAsync(string pbPath)
    {
        LoadingRing.IsActive = true;
        string? tempDir = null;

        try
        {
            tempDir = CreateTempExtractionDir();
            string pbName = Path.GetFileName(pbPath);

            var extractedEntries = await Task.Run(() =>
            {
                using var stream = File.OpenRead(pbPath);
                return ExtractTextureBundleEntries(stream, tempDir, $"[{pbName}]", pbName);
            });

            if (extractedEntries.Count == 0)
            {
                CleanupTempDir(tempDir);
                await ShowInfoDialogAsync("No texture blobs found in the .pb file.");
                return;
            }

            int countBefore = _loadedEntries.Count;
            _loadedEntries.AddRange(extractedEntries);

            UpdateFileSelector();

            if (_loadedEntries.Count > countBefore)
                SwatchbinSelector.SelectedItem = _loadedEntries[countBefore];
        }
        catch (Exception ex)
        {
            if (tempDir != null)
                CleanupTempDir(tempDir);

            await ShowErrorDialogAsync($"Failed to open .pb file: {ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private async Task LoadZipFileAsync(string zipPath)
    {
        LoadingRing.IsActive = true;
        string? tempDir = null;

        try
        {
            tempDir = CreateTempExtractionDir();
            string zipName = Path.GetFileName(zipPath);

            var extractedEntries = await Task.Run(() =>
            {
                var entriesToLoad = new List<SwatchbinFileEntry>();

                using var zip = new CustomZipFile(zipPath);
                var entries = zip.GetEntries()
                    .Where(e => !e.IsDirectory &&
                        (e.Name.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase) ||
                         e.Name.EndsWith(".pb", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                foreach (var entry in entries)
                {
                    if (entry.Name.EndsWith(".pb", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var data = zip.ExtractToMemory(entry);
                            using var stream = new MemoryStream(data);
                            entriesToLoad.AddRange(ExtractTextureBundleEntries(stream, tempDir, $"[{zipName}] {entry.Name}", entry.Name));
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Zip/PB] {entry.Name}: {ex.Message}");
                        }

                        continue;
                    }

                    string relativePath = entry.Name.Replace('/', Path.DirectorySeparatorChar);
                    string relativeName = Path.GetFileName(relativePath);
                    string entryDir = Path.GetDirectoryName(relativePath) ?? string.Empty;
                    string outputDir = Path.Combine(tempDir, entryDir);
                    Directory.CreateDirectory(outputDir);

                    string outputPath = Path.Combine(outputDir, relativeName);
                    var swatchbinData = zip.ExtractToMemory(entry);
                    File.WriteAllBytes(outputPath, swatchbinData);

                    string displayName = $"[{zipName}] {Path.GetFileName(outputPath)}";
                    if (entries.Count(e =>
                        e.Name.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(Path.GetFileName(e.Name), Path.GetFileName(entry.Name), StringComparison.OrdinalIgnoreCase)) > 1)
                    {
                        displayName = $"[{zipName}] {entry.Name}";
                    }

                    entriesToLoad.Add(new SwatchbinFileEntry
                    {
                        DisplayName = displayName,
                        FilePath = outputPath
                    });
                }

                return entriesToLoad;
            });

            if (extractedEntries.Count == 0)
            {
                CleanupTempDir(tempDir);
                await ShowInfoDialogAsync("No .swatchbin or .pb texture bundles found in the zip archive.");
                return;
            }

            int countBefore = _loadedEntries.Count;
            _loadedEntries.AddRange(extractedEntries);

            UpdateFileSelector();

            if (_loadedEntries.Count > countBefore)
                SwatchbinSelector.SelectedItem = _loadedEntries[countBefore];
        }
        catch (Exception ex)
        {
            if (tempDir != null)
                CleanupTempDir(tempDir);

            await ShowErrorDialogAsync($"Failed to open zip file: {ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    // Opens a Playground MiniZip and adds swatchbin/texture bundle entries to the viewer.
    private async Task LoadMiniZipFileAsync(string miniZipPath)
    {
        LoadingRing.IsActive = true;
        string? tempDir = null;

        try
        {
            tempDir = CreateTempExtractionDir();
            string miniZipName = Path.GetFileName(miniZipPath);

            var extractedEntries = await Task.Run(() =>
            {
                var entriesToLoad = new List<SwatchbinFileEntry>();

                using var minizip = new ForzaTechStudio.Services.MiniZipService(miniZipPath);

                foreach (var entry in minizip.Entries)
                {
                    try
                    {
                        bool isSwatchbinEntry = entry.Name.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase);
                        bool isPbEntry = entry.Name.EndsWith(".pb", StringComparison.OrdinalIgnoreCase);

                        if (!isSwatchbinEntry && !isPbEntry)
                        {
                            bool hasGrubMagic = minizip.ProbeEntryMagic(entry.Index, out uint magic)
                                && magic == Bundle.BundleTag;

                            if (!hasGrubMagic)
                                continue;
                        }

                        using var ms = new MemoryStream((int)entry.UncompressedSize);
                        minizip.ExtractEntryToStream(entry.Index, ms);
                        ms.Position = 0;

                        var bundle = new Bundle();
                        bundle.Load(ms);

                        int textureBlobCount = bundle.Blobs.OfType<TextureContentBlob>().Count();
                        if (textureBlobCount == 0)
                            continue;

                        string logicalEntryName;
                        string uniqueSourceName;

                        if (!string.IsNullOrWhiteSpace(entry.Name))
                        {
                            logicalEntryName = entry.Name;
                            uniqueSourceName = $"{entry.Index:D4}_{entry.Name}";
                        }
                        else if (textureBlobCount > 1 || isPbEntry)
                        {
                            logicalEntryName = $"entry_{entry.Index:D4}.pb";
                            uniqueSourceName = logicalEntryName;
                        }
                        else
                        {
                            logicalEntryName = $"entry_{entry.Index:D4}.swatchbin";
                            uniqueSourceName = logicalEntryName;
                        }

                        if (!isPbEntry && textureBlobCount == 1)
                        {
                            string outputFileName = $"{CreateBundleFileStem(uniqueSourceName)}.swatchbin";
                            string outputPath = Path.Combine(tempDir, outputFileName);

                            ms.Position = 0;
                            File.WriteAllBytes(outputPath, ms.ToArray());

                            entriesToLoad.Add(new SwatchbinFileEntry
                            {
                                DisplayName = $"[{miniZipName}] {logicalEntryName}",
                                FilePath = outputPath
                            });

                            continue;
                        }

                        entriesToLoad.AddRange(ExtractTextureBundleEntries(bundle, tempDir, $"[{miniZipName}] {logicalEntryName}", uniqueSourceName));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MiniZip/Textures] Entry {entry.Index}: {ex.Message}");
                    }
                }

                return entriesToLoad;
            });

            if (extractedEntries.Count == 0)
            {
                CleanupTempDir(tempDir);
                await ShowInfoDialogAsync("No .swatchbin or .pb texture bundles found in the minizip archive.");
                return;
            }

            int countBefore = _loadedEntries.Count;
            _loadedEntries.AddRange(extractedEntries);

            UpdateFileSelector();

            if (_loadedEntries.Count > countBefore)
                SwatchbinSelector.SelectedItem = _loadedEntries[countBefore];
        }
        catch (Exception ex)
        {
            if (tempDir != null)
                CleanupTempDir(tempDir);

            await ShowErrorDialogAsync($"Failed to open minizip file: {ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private async Task LoadMultipleSwatchbinFilesAsync(List<string> filePaths)
    {
        if (filePaths.Count == 0) return;

        LoadingRing.IsActive = true;

        try
        {
            foreach (var path in filePaths)
            {
                // Avoid adding duplicates
                if (_loadedEntries.Any(e => string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _loadedEntries.Add(new SwatchbinFileEntry
                {
                    DisplayName = Path.GetFileName(path),
                    FilePath = path
                });
            }

            UpdateFileSelector();

            // Select the first newly added file
            var firstNew = _loadedEntries.First(e => filePaths.Contains(e.FilePath, StringComparer.OrdinalIgnoreCase));
            SwatchbinSelector.SelectedItem = firstNew;
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync($"Failed to load swatchbin files: {ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private void UpdateFileSelector()
    {
        _isChangingSelection = true;
        SwatchbinSelector.ItemsSource = null;
        SwatchbinSelector.ItemsSource = _loadedEntries;
        _isChangingSelection = false;

        // Show the selector when we have entries
        FileSelectionPanel.Visibility = _loadedEntries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SwatchbinSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isChangingSelection) return;
        if (SwatchbinSelector.SelectedItem is not SwatchbinFileEntry entry) return;

        // Cancel any in-flight load from a previous selection
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var cts = _loadCts;

        // Unload current swatchbin from memory
        _currentSwatchbin = null;
        TextureImage.Source = null;
        TextureScrollViewer.Visibility = Visibility.Collapsed;

        // Load the selected swatchbin
        await LoadSwatchbinFileAsync(entry.FilePath, cts.Token);
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var openPicker = new FileOpenPicker();
        var window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);
        }
        
        openPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        openPicker.FileTypeFilter.Add(".dds");
        openPicker.FileTypeFilter.Add(".png");
        openPicker.FileTypeFilter.Add(".jpg");
        openPicker.FileTypeFilter.Add(".jpeg");
        
        var sourceFile = await openPicker.PickSingleFileAsync();
        if (sourceFile == null) return;

        bool isDds = sourceFile.Path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase);

        var savePicker = new FileSavePicker();
        if (window != null)
        {
             var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
             WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
        }
        savePicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        savePicker.FileTypeChoices.Add("Swatchbin Texture", [".swatchbin"]);
        savePicker.SuggestedFileName = Path.GetFileNameWithoutExtension(sourceFile.Name);
        
        var saveFile = await savePicker.PickSaveFileAsync();
        if (saveFile == null) return;

        try
        {
            if (!isDds)
            {
                var dialog = new SwatchbinCreationDialog();
                dialog.XamlRoot = this.XamlRoot;
                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    return;
                }
                
                LoadingRing.IsActive = true;
                await _swatchbinService.CreateSwatchbinFromImageAsync(
                    sourceFile.Path, 
                    saveFile.Path,
                    dialog.SelectedEncoding,
                    dialog.SelectedColorProfile,
                    dialog.SelectedTranscoding,
                    dialog.GenerateMipMaps,
                    dialog.IsCubeMap,
                    dialog.Is3D,
                    dialog.IsPremultipliedAlpha,
                    dialog.TextureGuid
                );
            }
            else
            {
                LoadingRing.IsActive = true;
                await _swatchbinService.CreateSwatchbinAsync(sourceFile.Path, saveFile.Path);
            }

            await ShowInfoDialogAsync($"Swatchbin created successfully.\nSaved to: {saveFile.Name}");
            
            // Load the new file into the list
            await LoadMultipleSwatchbinFilesAsync([saveFile.Path]);
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync($"Failed to create swatchbin: {ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private async Task LoadSwatchbinFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        LoadingRing.IsActive = true;
        PlaceholderPanel.Visibility = Visibility.Collapsed;
        TextureScrollViewer.Visibility = Visibility.Collapsed;
        _lastDecodeError = null;
        _lastLinearData = null;

        try
        {
            _currentSwatchbin = await _swatchbinService.LoadSwatchbinAsync(filePath);

            if (cancellationToken.IsCancellationRequested) return;

            // Update info panel
            UpdateInfoPanel(_currentSwatchbin);

            // Try to display the texture
            await DisplayTextureAsync(_currentSwatchbin, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            SaveAsButton.IsEnabled = true;
            ReplaceButton.IsEnabled = true;

            // Enable PC Swatchbin conversion only for Durango/Xbox textures
            SaveAsPcSwatchbinMenuItem.IsEnabled = _currentSwatchbin.IsDurangoFormat;

            InfoPanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            InfoColumnBorder.Visibility = Visibility.Visible;
            PreviewColumnBorder.Visibility = Visibility.Visible;

        }
        catch (OperationCanceledException)
        {
            // A newer selection was made ? silently discard this result
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested) return;
            await ShowErrorDialogAsync($"Failed to load swatchbin file: {ex.Message}");
            PlaceholderPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                LoadingRing.IsActive = false;
        }
    }

    private void UpdateInfoPanel(SwatchbinInfo info)
    {
        FileNameText.Text = info.FileName;
        WidthText.Text = info.Width.ToString();
        HeightText.Text = info.Height.ToString();
        DepthText.Text = info.Depth.ToString();
        MipLevelsText.Text = info.MipLevels.ToString();
        FormatText.Text = info.DxgiFormatName;
        EncodingText.Text = info.Encoding.ToString();
        TranscodingText.Text = info.Transcoding.ToString();
        ColorProfileText.Text = info.ColorProfile.ToString();
        IsCubeCheck.IsChecked = info.IsTextureCube;
        Is3DCheck.IsChecked = info.IsTexture3D;
        IsPremultCheck.IsChecked = info.IsPremultipliedAlpha;
        BundleVersionText.Text = $"{info.BundleVersionMajor}.{info.BundleVersionMinor}";
        BlobVersionText.Text = $"{info.BlobVersionMajor}.{info.BlobVersionMinor}";
        GuidText.Text = info.TextureId.ToString();
        
        // Platform detection
        PlatformText.Text = info.IsDurangoFormat ? "Xbox One (Durango)" : "PC";
        
        if (info.IsDurangoFormat)
        {
            XboxInfoPanel.Visibility = Visibility.Visible;
            TileModeText.Text = info.TileMode?.ToString() ?? "Unknown";
        }
        else
        {
            XboxInfoPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async Task DisplayTextureAsync(SwatchbinInfo info, CancellationToken cancellationToken = default)
    {
        if (info.RawTextureData == null || info.RawTextureData.Length == 0)
        {
            PlaceholderPanel.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            byte[]? processedData = null;
            Exception? processingException = null;
            string? errorDetails = null;

            // First, detile Xbox textures if needed
            await Task.Run(() =>
            {
                try
                {
                    if (info.IsDurangoFormat)
                    {
                        System.Diagnostics.Debug.WriteLine($"Processing Xbox texture: {info.Width}x{info.Height}, TileMode: {info.TileMode}");

                        if (info.TileMode == XG_TILE_MODE.XG_TILE_MODE_2D_THIN ||
                            info.TileMode == XG_TILE_MODE.XG_TILE_MODE_1D_THIN)
                        {
                            var detiledData = DurangoDetile(info, info.RawTextureData);
                            if (detiledData != null)
                            {
                                var format = (XG_FORMAT)info.DxgiFormat;
                                processedData = DealignDurangoTextureData(info, format, detiledData);
                                System.Diagnostics.Debug.WriteLine($"Detiling successful, output size: {processedData.Length} bytes");
                            }
                            else
                            {
                                errorDetails = "DurangoDetile returned null. Check debug output for XG library errors.";
                                throw new Exception("Failed to detile Xbox texture data");
                            }
                        }
                        else
                        {
                            errorDetails = $"Tile mode {info.TileMode} is not currently supported. Only XG_TILE_MODE_2D_THIN and XG_TILE_MODE_1D_THIN are implemented.";
                            throw new NotSupportedException(errorDetails);
                        }
                    }
                    else
                    {
                        processedData = info.RawTextureData;
                    }
                }
                catch (Exception ex)
                {
                    processingException = ex;
                    if (errorDetails == null)
                        errorDetails = ex.Message;
                }
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            if (processingException != null)
            {
                _lastDecodeError = processingException.Message;
                PlaceholderPanel.Visibility = Visibility.Visible;
                System.Diagnostics.Debug.WriteLine($"Texture processing failed: {errorDetails}");
                await ShowErrorDialogAsync($"Failed to process texture:\n\n{errorDetails}");
                return;
            }

            if (processedData == null || processedData.Length == 0)
            {
                PlaceholderPanel.Visibility = Visibility.Visible;
                await ShowErrorDialogAsync("Failed to process texture: No data after processing");
                return;
            }

            // Decode processed data to RGBA
            byte[]? rgbaData = null;
            Exception? decodeException = null;

            await Task.Run(() =>
            {
                try
                {
                    rgbaData = DecodeTextureToRgba(info, processedData);
                }
                catch (Exception ex)
                {
                    decodeException = ex;
                }
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            if (decodeException != null)
            {
                _lastDecodeError = decodeException.Message;
                PlaceholderPanel.Visibility = Visibility.Visible;
                await ShowErrorDialogAsync($"Failed to decode texture:\n\n{decodeException.Message}");
                return;
            }

            if (rgbaData != null && rgbaData.Length > 0)
            {

                _lastLinearData = processedData;

                await RenderPreviewAsync(info, processedData, cancellationToken);
                return;
            }

            // If we can't decode, show placeholder
            PlaceholderPanel.Visibility = Visibility.Visible;
            await ShowErrorDialogAsync("Failed to create bitmap from decoded data");
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection ? discard silently
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested) return;
            _lastDecodeError = ex.Message;
            PlaceholderPanel.Visibility = Visibility.Visible;
            System.Diagnostics.Debug.WriteLine($"DisplayTextureAsync exception: {ex.Message}\n{ex.StackTrace}");
            await ShowErrorDialogAsync($"Failed to display texture:\n\n{ex.Message}");
        }
    }

    private byte[]? DecodeTextureToRgba(SwatchbinInfo info, byte[] linearData)
    {
        if (linearData == null || linearData.Length == 0) 
            return null;

        // Get the compression format
        CompressionFormat format = GetBcnFormat(info.DxgiFormat);
        
        if (format == CompressionFormat.Unknown)
        {
            // Try uncompressed formats
            return TryDecodeUncompressed(info, linearData);
        }

        // Use BCnEncoder to decode compressed textures
        var decoder = new BcDecoder();
        
        // Decode the raw texture data (first mip level only)
        var decoded = decoder.DecodeRaw(linearData, (int)info.Width, (int)info.Height, format);
        
        if (decoded == null || decoded.Length == 0)
            return null;
        
        // Convert ColorRgba32 array to byte array (RGBA)
        byte[] result = new byte[decoded.Length * 4];
        for (int i = 0; i < decoded.Length; i++)
        {
            result[i * 4 + 0] = decoded[i].r;
            result[i * 4 + 1] = decoded[i].g;
            result[i * 4 + 2] = decoded[i].b;
            result[i * 4 + 3] = decoded[i].a;
        }
        
        return result;
    }

    private byte[]? TryDecodeUncompressed(SwatchbinInfo info, byte[] linearData)
    {
        if (linearData == null) return null;
        
        int width = (int)info.Width;
        int height = (int)info.Height;
        int expectedSize = width * height * 4;
        
        // R8G8B8A8 format
        if (info.DxgiFormat == 28 || info.DxgiFormat == 29)
        {
            if (linearData.Length >= expectedSize)
            {
                byte[] result = new byte[expectedSize];
                Array.Copy(linearData, 0, result, 0, expectedSize);
                return result;
            }
        }
        
        // B8G8R8A8 format (swap R and B)
        if (info.DxgiFormat == 87)
        {
            if (linearData.Length >= expectedSize)
            {
                byte[] result = new byte[expectedSize];
                for (int i = 0; i < width * height; i++)
                {
                    int idx = i * 4;
                    result[idx + 0] = linearData[idx + 2]; // R <- B
                    result[idx + 1] = linearData[idx + 1]; // G
                    result[idx + 2] = linearData[idx + 0]; // B <- R
                    result[idx + 3] = linearData[idx + 3]; // A
                }
                return result;
            }
        }
        
        // R8 format (grayscale)
        if (info.DxgiFormat == 61)
        {
            int r8Size = width * height;
            if (linearData.Length >= r8Size)
            {
                byte[] result = new byte[expectedSize];
                for (int i = 0; i < width * height; i++)
                {
                    byte gray = linearData[i];
                    result[i * 4 + 0] = gray;
                    result[i * 4 + 1] = gray;
                    result[i * 4 + 2] = gray;
                    result[i * 4 + 3] = 255;
                }
                return result;
            }
        }
        
        // A8 format (alpha only, display as grayscale)
        if (info.DxgiFormat == 65)
        {
            int a8Size = width * height;
            if (linearData.Length >= a8Size)
            {
                byte[] result = new byte[expectedSize];
                for (int i = 0; i < width * height; i++)
                {
                    byte alpha = linearData[i];
                    result[i * 4 + 0] = alpha;
                    result[i * 4 + 1] = alpha;
                    result[i * 4 + 2] = alpha;
                    result[i * 4 + 3] = 255;
                }
                return result;
            }
        }
        
        // R8G8 format (two channel)
        if (info.DxgiFormat == 49)
        {
            int rg8Size = width * height * 2;
            if (linearData.Length >= rg8Size)
            {
                byte[] result = new byte[expectedSize];
                for (int i = 0; i < width * height; i++)
                {
                    result[i * 4 + 0] = linearData[i * 2 + 0]; // R
                    result[i * 4 + 1] = linearData[i * 2 + 1]; // G
                    result[i * 4 + 2] = 0;                               // B
                    result[i * 4 + 3] = 255;                             // A
                }
                return result;
            }
        }
        
        return null;
    }

    private CompressionFormat GetBcnFormat(uint dxgiFormat)
    {
        return dxgiFormat switch
        {
            71 or 72 => CompressionFormat.Bc1,       // BC1_UNORM / BC1_UNORM_SRGB
            74 or 75 => CompressionFormat.Bc2,       // BC2_UNORM / BC2_UNORM_SRGB
            77 or 78 => CompressionFormat.Bc3,       // BC3_UNORM / BC3_UNORM_SRGB
            80 => CompressionFormat.Bc4,             // BC4_UNORM
            81 => CompressionFormat.Bc4,             // BC4_SNORM (treat as UNORM for display)
            83 => CompressionFormat.Bc5,             // BC5_UNORM
            84 => CompressionFormat.Bc5,             // BC5_SNORM (treat as UNORM for display)
            // BC6H not supported by BCnEncoder.Net
            // 95 or 96 => CompressionFormat.Bc6,
            98 or 99 => CompressionFormat.Bc7,       // BC7_UNORM / BC7_UNORM_SRGB
            _ => CompressionFormat.Unknown
        };
    }

    private async Task<BitmapImage?> CreateBitmapImageFromRgbaAsync(byte[] rgbaData, int width, int height)
    {
        if (rgbaData == null || rgbaData.Length == 0 || width <= 0 || height <= 0)
            return null;
            
        int expectedSize = width * height * 4;
        if (rgbaData.Length < expectedSize)
            return null;

        try
        {
            using var ms = new InMemoryRandomAccessStream();
            
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
            
            encoder.SetPixelData(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Straight,
                (uint)width,
                (uint)height,
                96, 96,
                rgbaData);

            await encoder.FlushAsync();
            ms.Seek(0);

            var bitmapImage = new BitmapImage();
            await bitmapImage.SetSourceAsync(ms);
            
            return bitmapImage;
        }
        catch
        {
            return null;
        }
    }

    // Re-renders the preview using cached linear data when a toggle changes.
    private async void ShowAlphaToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _showAlpha = ShowAlphaToggle.IsOn;
        if (_currentSwatchbin != null && _lastLinearData != null)
            await RenderPreviewAsync(_currentSwatchbin, _lastLinearData, CancellationToken.None);
    }

    private async void ShowMipMapsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _showMipMaps = ShowMipMapsToggle.IsOn;
        if (_currentSwatchbin != null && _lastLinearData != null)
            await RenderPreviewAsync(_currentSwatchbin, _lastLinearData, CancellationToken.None);
    }

    // Decodes and displays the texture according to current toggle states.
    private async Task RenderPreviewAsync(SwatchbinInfo info, byte[] linearData, CancellationToken cancellationToken)
    {
        byte[]? rgbaData = null;
        int displayWidth = (int)info.Width;
        int displayHeight = (int)info.Height;

        await Task.Run(() =>
        {
            if (_showMipMaps && info.MipLevels > 1)
            {
                (rgbaData, displayWidth, displayHeight) = BuildMipAtlas(info, linearData);
            }
            else
            {
                rgbaData = DecodeTextureToRgba(info, linearData);
            }

            if (rgbaData != null && _showAlpha)
                rgbaData = ExtractAlphaChannel(rgbaData);
        }, cancellationToken);

        if (cancellationToken.IsCancellationRequested) return;
        if (rgbaData == null || rgbaData.Length == 0) return;

        var bitmapImage = await CreateBitmapImageFromRgbaAsync(rgbaData, displayWidth, displayHeight);
        if (cancellationToken.IsCancellationRequested) return;

        if (bitmapImage != null)
        {
            _loadedImageWidth = displayWidth;
            _loadedImageHeight = displayHeight;
            TextureImage.Width = _loadedImageWidth;
            TextureImage.Height = _loadedImageHeight;
            ImageContainer.Width = _loadedImageWidth;
            ImageContainer.Height = _loadedImageHeight;
            TextureImage.Source = bitmapImage;
            TextureScrollViewer.Visibility = Visibility.Visible;
            PlaceholderPanel.Visibility = Visibility.Collapsed;

            TextureScrollViewer.UpdateLayout();
            TextureScrollViewer.ChangeView(0, 0, 1.0f, true);
        }
    }

    // Builds a horizontal mip atlas
    // Returns (rgba bytes, atlasWidth, atlasHeight).
    private (byte[] rgba, int width, int height) BuildMipAtlas(SwatchbinInfo info, byte[] linearData)
    {
        int mipCount = Math.Max(1, (int)info.MipLevels);
        uint blockSize = GetBlockSize(info.DxgiFormat);
        uint bpp = blockSize == 0 ? GetBitsPerPixel(info.DxgiFormat) : 0;

        var mips = new List<(int w, int h, int offset, int size)>(mipCount);
        int offset = 0;
        int w = (int)info.Width;
        int h = (int)info.Height;

        for (int m = 0; m < mipCount; m++)
        {
            int size;
            if (blockSize > 0)
            {
                int blocksW = Math.Max(1, (w + 3) / 4);
                int blocksH = Math.Max(1, (h + 3) / 4);
                size = blocksW * blocksH * (int)blockSize;
            }
            else
            {
                size = Math.Max(1, w) * Math.Max(1, h) * (int)bpp / 8;
            }

            if (offset + size <= linearData.Length)
                mips.Add((Math.Max(1, w), Math.Max(1, h), offset, size));

            offset += size;
            w = Math.Max(1, w >> 1);
            h = Math.Max(1, h >> 1);
        }

        if (mips.Count == 0)
            return (DecodeTextureToRgba(info, linearData) ?? [], (int)info.Width, (int)info.Height);

        // Atlas dimensions: all mips side by side, height = mip 0 height
        int atlasW = mips.Sum(m => m.w + 2); 
        int atlasH = mips[0].h;
        byte[] atlas = new byte[atlasW * atlasH * 4]; // pre-filled black/transparent

        int xCursor = 0;
        foreach (var (mw, mh, mOffset, mSize) in mips)
        {
            var mipSlice = new byte[mSize];
            Array.Copy(linearData, mOffset, mipSlice, 0, mSize);

            // Temporarily adjust info dimensions for this mip level
            var mipInfo = new SwatchbinInfo
            {
                Width = (uint)mw,
                Height = (uint)mh,
                DxgiFormat = info.DxgiFormat,
                IsDurangoFormat = false 
            };

            byte[]? mipRgba = DecodeTextureToRgba(mipInfo, mipSlice);
            if (mipRgba == null) { xCursor += mw + 2; continue; }

            int yOffset = (atlasH - mh) / 2;
            for (int row = 0; row < mh; row++)
            {
                int atlasRow = yOffset + row;
                if (atlasRow < 0 || atlasRow >= atlasH) continue;
                int srcBase = row * mw * 4;
                int dstBase = (atlasRow * atlasW + xCursor) * 4;
                int copyLen = Math.Min(mw * 4, (atlasW - xCursor) * 4);
                if (copyLen > 0 && srcBase + copyLen <= mipRgba.Length)
                    Array.Copy(mipRgba, srcBase, atlas, dstBase, copyLen);
            }

            xCursor += mw + 2;
        }

        return (atlas, atlasW, atlasH);
    }

    // Converts RGBA data to a greyscale alpha-channel view (R=G=B=A, A=255).
    private static byte[] ExtractAlphaChannel(byte[] rgba)
    {
        byte[] result = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length / 4; i++)
        {
            byte a = rgba[i * 4 + 3];
            result[i * 4 + 0] = a;
            result[i * 4 + 1] = a;
            result[i * 4 + 2] = a;
            result[i * 4 + 3] = 255;
        }
        return result;
    }

    private async void SaveAsDds_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSwatchbin?.DdsData == null) return;

        var picker = new FileSavePicker();
        
        var window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("DDS Texture", [".dds"]);
        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_currentSwatchbin.FileName);

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            try
            {
                await FileIO.WriteBytesAsync(file, _currentSwatchbin.DdsData);
                await ShowInfoDialogAsync($"DDS file saved successfully to:\n{file.Path}");
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"Failed to save DDS file: {ex.Message}");
            }
        }
    }

    private async void SaveAsPcSwatchbin_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSwatchbin == null || !_currentSwatchbin.IsDurangoFormat) return;

        var picker = new FileSavePicker();
        
        var window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("PC Swatchbin Texture", [".swatchbin"]);
        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_currentSwatchbin.FileName);

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            try
            {
                LoadingRing.IsActive = true;
                await ConvertDurangoToPcSwatchbinAsync(_currentSwatchbin, file.Path);
                await ShowInfoDialogAsync($"Xbox (durango) swatchbin converted to PC format successfully.\nSaved to: {file.Path}");
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync($"Failed to convert swatchbin: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }
    }

    private async void SaveAsPng_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsImageAsync(BitmapEncoder.PngEncoderId, ".png", "PNG Image");
    }

    private async void SaveAsJpg_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsImageAsync(BitmapEncoder.JpegEncoderId, ".jpg", "JPEG Image");
    }

    private async Task SaveAsImageAsync(Guid encoderId, string extension, string fileTypeDescription)
    {
        if (_currentSwatchbin == null) return;

        // We need the decoded RGBA data ? re-decode from the current swatchbin.
        byte[]? rgbaData = null;
        Exception? decodeError = null;

        LoadingRing.IsActive = true;
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    byte[] linearData;
                    if (_currentSwatchbin.IsDurangoFormat)
                    {
                        var detiledData = DurangoDetile(_currentSwatchbin, _currentSwatchbin.RawTextureData);
                        if (detiledData == null)
                            throw new InvalidOperationException("Failed to detile Xbox texture data.");
                        var fmt = (DurangoTypes.XG_FORMAT)_currentSwatchbin.DxgiFormat;
                        linearData = DealignDurangoTextureData(_currentSwatchbin, fmt, detiledData);
                    }
                    else
                    {
                        linearData = _currentSwatchbin.RawTextureData;
                    }

                    rgbaData = DecodeTextureToRgba(_currentSwatchbin, linearData);
                }
                catch (Exception ex)
                {
                    decodeError = ex;
                }
            });

            if (decodeError != null)
            {
                await ShowErrorDialogAsync($"Failed to decode texture: {decodeError.Message}");
                return;
            }

            if (rgbaData == null || rgbaData.Length == 0)
            {
                await ShowErrorDialogAsync("No decoded texture data available.");
                return;
            }

            var picker = new FileSavePicker();
            var window = App.MainWindow;
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add(fileTypeDescription, [extension]);
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_currentSwatchbin.FileName);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);

            // PNG keeps alpha; JPEG has no alpha channel so use Ignore to flatten correctly.
            var alphaMode = encoderId == BitmapEncoder.PngEncoderId
                ? BitmapAlphaMode.Straight
                : BitmapAlphaMode.Ignore;

            encoder.SetPixelData(
                BitmapPixelFormat.Rgba8,
                alphaMode,
                (uint)_currentSwatchbin.Width,
                (uint)_currentSwatchbin.Height,
                96, 96,
                rgbaData);

            await encoder.FlushAsync();
            await ShowInfoDialogAsync($"Saved successfully to:\n{file.Path}");
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync($"Failed to save image: {ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    // Converts a Durango/Xbox swatchbin to a PC swatchbin by detiling the texture
    // and creating a new PC-format swatchbin with the linear DDS data.
    private async Task ConvertDurangoToPcSwatchbinAsync(SwatchbinInfo durangoInfo, string outputPath)
    {
        await Task.Run(() =>
        {
            System.Diagnostics.Debug.WriteLine($"Converting Durango swatchbin to PC: {durangoInfo.FileName}");
            _swatchbinConversionService.ConvertDurangoToPc(durangoInfo.FilePath, outputPath);
            System.Diagnostics.Debug.WriteLine($"PC swatchbin created successfully: {outputPath}");
        });
    }

    // Creates a complete DDS file (with header) from linear texture data.
    private byte[] CreateDdsFromLinearData(byte[] linearData, int width, int height, byte mipLevels, 
        uint dxgiFormat, bool isCube, bool is3D, uint depth)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Calculate linear size for the first mip
        uint linearSize = CalculateLinearSize(dxgiFormat, (uint)width, (uint)height);

        // DDS Magic
        writer.Write(0x20534444); // 'DDS '

        // DDS_HEADER - 124 bytes
        writer.Write(124); // dwSize
        writer.Write(0x000A1007); // dwFlags: CAPS | HEIGHT | WIDTH | PIXELFORMAT | MIPMAPCOUNT | LINEARSIZE
        writer.Write((uint)height);
        writer.Write((uint)width);
        writer.Write(linearSize); // dwPitchOrLinearSize
        writer.Write(depth > 1 ? depth : 1u); // dwDepth
        writer.Write((uint)mipLevels); // dwMipMapCount

        // dwReserved1[11]
        for (int i = 0; i < 11; i++)
            writer.Write(0);

        // DDS_PIXELFORMAT - 32 bytes
        writer.Write(32); // dwSize
        writer.Write(0x4); // dwFlags: FOURCC
        writer.Write(0x30315844); // dwFourCC: 'DX10'
        writer.Write(0); // dwRGBBitCount
        writer.Write(0); // dwRBitMask
        writer.Write(0); // dwGBitMask
        writer.Write(0); // dwBBitMask
        writer.Write(0); // dwABitMask

        // dwCaps
        uint caps = 0x1000; // DDSCAPS_TEXTURE
        if (mipLevels > 1)
            caps |= 0x400008; // DDSCAPS_COMPLEX | DDSCAPS_MIPMAP
        writer.Write(caps);
        
        // dwCaps2
        uint caps2 = 0;
        if (isCube)
            caps2 = 0xFE00; // All cubemap faces
        writer.Write(caps2);
        
        // dwCaps3, dwCaps4, dwReserved2
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        // DDS_HEADER_DXT10 - 20 bytes
        writer.Write(dxgiFormat); // dxgiFormat
        
        // resourceDimension
        uint resourceDimension = 3; // D3D10_RESOURCE_DIMENSION_TEXTURE2D
        if (is3D)
            resourceDimension = 4; // D3D10_RESOURCE_DIMENSION_TEXTURE3D
        writer.Write(resourceDimension);
        
        // miscFlag
        uint miscFlag = 0;
        if (isCube)
            miscFlag = 0x4; // D3D11_RESOURCE_MISC_TEXTURECUBE
        writer.Write(miscFlag);
        
        writer.Write(1u); // arraySize
        writer.Write(0u); // miscFlags2: DDS_ALPHA_MODE_UNKNOWN

        // Texture data
        writer.Write(linearData);

        return ms.ToArray();
    }

    private uint CalculateLinearSize(uint dxgiFormat, uint width, uint height)
    {
        uint blockSize = GetBlockSize(dxgiFormat);
        if (blockSize > 0)
        {
            // Block compressed format
            uint blocksWide = Math.Max(1, (width + 3) / 4);
            uint blocksHigh = Math.Max(1, (height + 3) / 4);
            return blocksWide * blocksHigh * blockSize;
        }
        else
        {
            // Uncompressed - calculate based on format
            uint bpp = GetBitsPerPixel(dxgiFormat);
            return (width * bpp + 7) / 8 * height;
        }
    }

    private uint GetBlockSize(uint dxgiFormat)
    {
        return dxgiFormat switch
        {
            71 or 72 => 8,  // BC1
            74 or 75 => 16, // BC2
            77 or 78 => 16, // BC3
            80 or 81 => 8,  // BC4
            83 or 84 => 16, // BC5
            95 or 96 => 16, // BC6H
            98 or 99 => 16, // BC7
            _ => 0
        };
    }

    private uint GetBitsPerPixel(uint dxgiFormat)
    {
        return dxgiFormat switch
        {
            2 => 128,  // R32G32B32A32_FLOAT
            10 => 64,  // R16G16B16A16_FLOAT
            11 => 64,  // R16G16B16A16_UNORM
            28 or 29 => 32, // R8G8B8A8_UNORM / R8G8B8A8_UNORM_SRGB
            49 => 16,  // R8G8_UNORM
            61 => 8,   // R8_UNORM
            65 => 8,   // A8_UNORM
            85 => 16,  // B5G6R5_UNORM
            86 => 16,  // B5G5R5A1_UNORM
            87 => 32,  // B8G8R8A8_UNORM
            _ => 32    // Default to 32bpp
        };
    }

    private void CloseFileButton_Click(object sender, RoutedEventArgs e)
    {
        // Unload current swatchbin
        _currentSwatchbin = null;
        _loadedImageWidth = 0;
        _loadedImageHeight = 0;
        _isDragging = false;
        TextureImage.Source = null;
        TextureImage.Width = double.NaN;
        TextureImage.Height = double.NaN;
        ImageContainer.Width = double.NaN;
        ImageContainer.Height = double.NaN;
        TextureScrollViewer.Visibility = Visibility.Collapsed;
        PlaceholderPanel.Visibility = Visibility.Visible;
        InfoPanel.Visibility = Visibility.Collapsed;
        InfoColumnBorder.Visibility = Visibility.Collapsed;
        PreviewColumnBorder.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Visible;
        SaveAsButton.IsEnabled = false;
        SaveAsPcSwatchbinMenuItem.IsEnabled = false;
        ReplaceButton.IsEnabled = false;
        this.ProtectedCursor = null;
        
        FileNameText.Text = "";
        WidthText.Text = "";
        HeightText.Text = "";

        // Clear all loaded entries
        _isChangingSelection = true;
        _loadedEntries.Clear();
        SwatchbinSelector.ItemsSource = null;
        FileSelectionPanel.Visibility = Visibility.Collapsed;
        _isChangingSelection = false;

        // Clean up extracted temporary files
        CleanupTempDirs();
    }

    private async void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSwatchbin == null) return;

        var openPicker = new FileOpenPicker();
        var window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);
        }
        
        openPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        openPicker.FileTypeFilter.Add(".dds");
        openPicker.FileTypeFilter.Add(".png");
        openPicker.FileTypeFilter.Add(".jpg");
        openPicker.FileTypeFilter.Add(".jpeg");

        var sourceFile = await openPicker.PickSingleFileAsync();
        if (sourceFile == null) return;

        bool isDds = sourceFile.Path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase);

        // For PNG/JPG, ask for encoding settings before proceeding
        ForzaTechStudio.Models.TextureEncoding imgEncoding = default;
        ForzaTechStudio.Models.ColorProfile imgColorProfile = default;
        ForzaTechStudio.Models.TextureTranscoding imgTranscoding = default;
        bool imgGenerateMipMaps = false;
        bool imgIsPremultiplied = false;

        if (!isDds)
        {
            var dialog = new SwatchbinCreationDialog();
            dialog.XamlRoot = this.XamlRoot;
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            imgEncoding = dialog.SelectedEncoding;
            imgColorProfile = dialog.SelectedColorProfile;
            imgTranscoding = dialog.SelectedTranscoding;
            imgGenerateMipMaps = dialog.GenerateMipMaps;
            imgIsPremultiplied = dialog.IsPremultipliedAlpha;
        }

        var savePicker = new FileSavePicker();
        if (window != null)
        {
             var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
             WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
        }
        savePicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        savePicker.FileTypeChoices.Add("Swatchbin Texture", [".swatchbin"]);
        savePicker.SuggestedFileName = _currentSwatchbin.FileName;
        
        var saveFile = await savePicker.PickSaveFileAsync();
        if (saveFile == null) return;

        try
        {
            LoadingRing.IsActive = true;
            if (isDds)
            {
                await _swatchbinService.ReplaceSwatchbinAsync(_currentSwatchbin.FilePath, sourceFile.Path, saveFile.Path);
            }
            else
            {
                await _swatchbinService.ReplaceSwatchbinFromImageAsync(
                    _currentSwatchbin.FilePath,
                    sourceFile.Path,
                    saveFile.Path,
                    imgEncoding,
                    imgColorProfile,
                    imgTranscoding,
                    imgGenerateMipMaps,
                    imgIsPremultiplied);
            }
            await ShowInfoDialogAsync($"Texture replaced successfully.\nSaved to: {saveFile.Name}");
            
            // Reload the new file
            await LoadSwatchbinFileAsync(saveFile.Path);
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync($"Failed to replace texture: {ex.Message}");
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private async Task ShowErrorDialogAsync(string message)
    {
        if (this.XamlRoot == null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Error",
            Content = message,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }

    private async Task ShowInfoDialogAsync(string message)
    {
        if (this.XamlRoot == null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Success",
            Content = message,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        var newZoom = TextureScrollViewer.ZoomFactor - 0.1f;
        if (newZoom < TextureScrollViewer.MinZoomFactor) newZoom = TextureScrollViewer.MinZoomFactor;
        TextureScrollViewer.ChangeView(null, null, newZoom);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        var newZoom = TextureScrollViewer.ZoomFactor + 0.1f;
        if (newZoom > TextureScrollViewer.MaxZoomFactor) newZoom = TextureScrollViewer.MaxZoomFactor;
        TextureScrollViewer.ChangeView(null, null, newZoom);
    }

    private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isZoomingFromCode) return;
        if (TextureScrollViewer == null) return;
        
        TextureScrollViewer.ChangeView(null, null, (float)e.NewValue);
    }

    private void TextureScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (ZoomSlider == null || ZoomPercentText == null) return;

        _isZoomingFromCode = true;
        ZoomSlider.Value = TextureScrollViewer.ZoomFactor;
        ZoomPercentText.Text = $"{(int)(TextureScrollViewer.ZoomFactor * 100)}%";
        _isZoomingFromCode = false;
    }

    private void FitZoomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSwatchbin == null) return;
        if (TextureScrollViewer == null) return;
        
        var viewportWidth = TextureScrollViewer.ViewportWidth;
        var viewportHeight = TextureScrollViewer.ViewportHeight;
        
        if (viewportWidth == 0 || viewportHeight == 0) return;
        
        var imageWidth = _currentSwatchbin.Width;
        var imageHeight = _currentSwatchbin.Height;
        
        if (imageWidth == 0 || imageHeight == 0) return;

        var zoomX = viewportWidth / imageWidth;
        var zoomY = viewportHeight / imageHeight;
        
        var newZoom = (float)Math.Min(zoomX, zoomY);

        if (newZoom < TextureScrollViewer.MinZoomFactor) newZoom = TextureScrollViewer.MinZoomFactor;
        if (newZoom > TextureScrollViewer.MaxZoomFactor) newZoom = TextureScrollViewer.MaxZoomFactor;
        
        newZoom *= 0.95f; 

        TextureScrollViewer.ChangeView(0, 0, newZoom);
    }

    private void ActualSizeButton_Click(object sender, RoutedEventArgs e)
    {
        TextureScrollViewer?.ChangeView(0, 0, 1.0f);
    }

    private void ImageContainer_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not Grid grid || TextureImage.Source == null) return;

        var properties = e.GetCurrentPoint(grid).Properties;
        if (properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastMousePosition = e.GetCurrentPoint(this).Position;
            grid.CapturePointer(e.Pointer);
            this.ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        }
    }

    private void ImageContainer_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDragging)
            return;

        var currentPosition = e.GetCurrentPoint(this).Position;
        double deltaX = currentPosition.X - _lastMousePosition.X;
        double deltaY = currentPosition.Y - _lastMousePosition.Y;

        double newOffsetX = Math.Clamp(TextureScrollViewer.HorizontalOffset - deltaX, 0, TextureScrollViewer.ScrollableWidth);
        double newOffsetY = Math.Clamp(TextureScrollViewer.VerticalOffset - deltaY, 0, TextureScrollViewer.ScrollableHeight);

        TextureScrollViewer.ChangeView(newOffsetX, newOffsetY, null, true);
        _lastMousePosition = currentPosition;
        e.Handled = true;
    }

    private void ImageContainer_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isDragging && sender is Grid grid)
        {
            _isDragging = false;
            grid.ReleasePointerCapture(e.Pointer);
            this.ProtectedCursor = null;
            e.Handled = true;
        }
    }
}
