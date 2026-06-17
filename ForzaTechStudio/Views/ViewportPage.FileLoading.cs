using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.CarScene;
using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using SDX = SharpDX;
using Color = Windows.UI.Color;

namespace ForzaTechStudio.Views
{
    // File Loading and Parsing Methods
    public sealed partial class ViewportPage : Page
    {
        private static readonly RecyclableMemoryStreamManager _msManager = new();
        private static readonly HashSet<string> ViewportLoadableDropExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".minizip", ".modelbin", ".bin", ".carbin", ".xml", ".avpins", ".gr2", ".gsf"
        };

        private static readonly HashSet<string> ViewportLooseTextureDropExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".swatchbin", ".pb"
        };

        private sealed record ViewportLooseTextureDropFile(string Path, string LogicalPath);
        private sealed record ViewportDroppedFiles(
            List<string> IndividualFiles,
            List<string> FolderPaths,
            List<ViewportLooseTextureDropFile> LooseTextureFiles);

        private void Page_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            
            // Show drag overlay
            if (DragDropOverlay != null)
            {
                DragDropOverlay.Visibility = Visibility.Visible;
            }
        }

        private void Page_DragLeave(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            // Hide drag overlay
            if (DragDropOverlay != null)
            {
                DragDropOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void Page_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            // Hide drag overlay
            if (DragDropOverlay != null)
            {
                DragDropOverlay.Visibility = Visibility.Collapsed;
            }
            
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var droppedFiles = await CollectViewportDropFilesAsync(items);
                RegisterLooseViewportTextureFiles(droppedFiles.LooseTextureFiles);

                var allLoadablePaths = new List<string>(droppedFiles.IndividualFiles);
                allLoadablePaths.AddRange(droppedFiles.FolderPaths);

                if (allLoadablePaths.Count > 0)
                {
                    await ProcessDroppedFilesAsync(allLoadablePaths);
                }
                else if (droppedFiles.LooseTextureFiles.Count > 0)
                {
                    InvalidateViewportTextureLookup();
                    RefreshViewportTextureLookupFromLoadedRoots();
                    _ = StartViewportTextureRefreshAsync();
                }
            }
        }

        private Task<ViewportDroppedFiles> CollectViewportDropFilesAsync(IEnumerable<IStorageItem> items)
        {
            var itemPaths = items
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => (item.Path, IsFolder: item is StorageFolder))
                .ToList();

            return Task.Run(() =>
            {
                var individualFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var folderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var looseTextureFiles = new Dictionary<string, ViewportLooseTextureDropFile>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in itemPaths)
                {
                    if (item.IsFolder)
                    {
                        if (Directory.Exists(item.Path))
                        {
                            folderPaths.Add(Path.GetFullPath(item.Path));
                            // Also collect loose texture files from the folder tree
                            CollectViewportFolderTextureFiles(item.Path, looseTextureFiles);
                        }
                    }
                    else
                    {
                        AddViewportDropFile(item.Path, individualFiles, looseTextureFiles);
                    }
                }

                return new ViewportDroppedFiles(
                    individualFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
                    folderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
                    looseTextureFiles.Values.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToList());
            });
        }

        private static void CollectViewportFolderTextureFiles(string folderPath, IDictionary<string, ViewportLooseTextureDropFile> looseTextureFiles)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            try
            {
                foreach (string filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(filePath);
                    if (ViewportLooseTextureDropExtensions.Contains(extension))
                    {
                        string fullPath = Path.GetFullPath(filePath);
                        string logicalPath = Path.GetRelativePath(folderPath, fullPath);
                        looseTextureFiles.TryAdd(fullPath, new ViewportLooseTextureDropFile(fullPath, logicalPath));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Viewport/Drop] {folderPath}: {ex.Message}");
            }
        }

        private static void AddViewportDropFile(string filePath, ISet<string> loadableFiles, IDictionary<string, ViewportLooseTextureDropFile> looseTextureFiles)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            string extension = Path.GetExtension(filePath);
            if (ViewportLoadableDropExtensions.Contains(extension))
            {
                loadableFiles.Add(Path.GetFullPath(filePath));
            }
            else if (ViewportLooseTextureDropExtensions.Contains(extension))
            {
                string fullPath = Path.GetFullPath(filePath);
                looseTextureFiles.TryAdd(fullPath, new ViewportLooseTextureDropFile(fullPath, Path.GetFileName(fullPath)));
            }
        }

        private async Task ProcessDroppedFilesAsync(List<string> paths)
        {
            if (paths.Count == 0) return;

            IsLoading = true;
            LoadingStatus = "Processing dropped files...";

            // Separate folder paths from individual file paths
            var folderPaths = paths.Where(p => Directory.Exists(p)).ToList();
            var filePaths = paths.Where(p => !Directory.Exists(p)).ToList();

            bool l0 = Lod0Item.IsChecked;
            bool l1 = Lod1Item.IsChecked;
            bool l2 = Lod2Item.IsChecked;
            bool l3 = Lod3Item.IsChecked;
            bool l4 = Lod4Item.IsChecked;
            bool l5 = Lod5Item.IsChecked;
            bool shadows = ShadowsItem.IsChecked;
            bool showLights = LightsItem.IsChecked;
            bool showLocators = LocatorsItem.IsChecked;
            bool showPhysics = PhysicsItem.IsChecked;

            var loadedNodes = new System.Collections.Generic.List<(ViewerNode Node, string Path)>();

            // Load folder trees first (each folder becomes a single FolderNode root with children)
            foreach (var folderPath in folderPaths)
            {
                LoadingDetail = Path.GetFileName(folderPath);
                var folderNode = await LoadFolderTree(folderPath);
                if (folderNode != null)
                {
                    loadedNodes.Add((folderNode, folderPath));
                }
            }

            // XML files must be loaded synchronously on the UI thread
            var xmlPaths = filePaths.Where(p => Path.GetExtension(p).Equals(".xml", StringComparison.OrdinalIgnoreCase)).ToList();
            var avpinsPaths = filePaths.Where(p => Path.GetExtension(p).Equals(".avpins", StringComparison.OrdinalIgnoreCase)).ToList();
            var grannyPaths = filePaths.Where(p => { var ext = Path.GetExtension(p).ToLowerInvariant(); return ext == ".gr2" || ext == ".gsf"; }).ToList();
            var otherPaths = filePaths.Where(p => !xmlPaths.Contains(p) && !avpinsPaths.Contains(p) && !grannyPaths.Contains(p)).ToList();

            foreach (var xmlPath in xmlPaths)
            {
                var node = LoadLocatorsXml(xmlPath);
                if (node != null)
                {
                    loadedNodes.Add((node, xmlPath));
                }
            }

            foreach (var avpinsPath in avpinsPaths)
            {
                var xmlText = DecodeXmlText(await File.ReadAllBytesAsync(avpinsPath));
                var node = LoadAvPins(Path.GetFileName(avpinsPath), xmlText, avpinsPath);
                if (node != null)
                    loadedNodes.Add((node, avpinsPath));
            }

            // Granny files are loaded on the UI thread (no native DLL threading issues)
            foreach (var grannyPath in grannyPaths)
            {
                LoadingDetail = Path.GetFileName(grannyPath);
                var node = LoadGrannyFile(grannyPath);
                if (node != null)
                    loadedNodes.Add((node, grannyPath));
            }

            // Auto-discover companion GR2/GSF files from the same directory
            var alreadyLoadedPaths = new HashSet<string>(
                grannyPaths.Select(p => Path.GetFullPath(p)),
                StringComparer.OrdinalIgnoreCase);

            var companionPaths = new List<string>();
            foreach (var (node, _) in loadedNodes)
            {
                if (node is GrannyFileNode gfn && gfn.FileData != null)
                {
                    // If GSF, try to load referenced GR2 files from the same directory
                    if (gfn.IsGsf && gfn.FileData.CharacterInfo != null)
                    {
                        string? gsfDir = Path.GetDirectoryName(gfn.FilePath);
                        foreach (var set in gfn.FileData.CharacterInfo.AnimationSets)
                        {
                            foreach (var sfr in set.SourceFileReferences)
                            {
                                if (string.IsNullOrEmpty(sfr.SourceFilename)) continue;
                                // Extract just the filename from the source reference (may have $(var) paths)
                                string refName = Path.GetFileName(sfr.SourceFilename);
                                if (gsfDir == null) continue;
                                string refPath = Path.Combine(gsfDir, refName);
                                string fullRefPath = Path.GetFullPath(refPath);
                                if (File.Exists(refPath) && !alreadyLoadedPaths.Contains(fullRefPath))
                                {
                                    companionPaths.Add(refPath);
                                    alreadyLoadedPaths.Add(fullRefPath);
                                }
                            }
                        }
                    }

                    // If GR2, try to load sibling GSF files from the same directory
                    if (!gfn.IsGsf)
                    {
                        string? gr2Dir = Path.GetDirectoryName(gfn.FilePath);
                        try
                        {
                            if (gr2Dir != null)
                            foreach (var gsfFile in Directory.GetFiles(gr2Dir, "*.gsf"))
                            {
                                string fullGsfPath = Path.GetFullPath(gsfFile);
                                if (!alreadyLoadedPaths.Contains(fullGsfPath))
                                {
                                    companionPaths.Add(gsfFile);
                                    alreadyLoadedPaths.Add(fullGsfPath);
                                }
                            }
                        }
                        catch { /* directory access error */ }
                    }
                }
            }

            // Load discovered companion files
            foreach (var compPath in companionPaths)
            {
                LoadingDetail = Path.GetFileName(compPath);
                var compNode = LoadGrannyFile(compPath);
                if (compNode != null)
                    loadedNodes.Add((compNode, compPath));
            }

            // Other files (modelbin, bin, zip) can be processed in parallel
            var parallelResults = new ConcurrentBag<(ViewerNode Node, string Path)>();
            var processTask = Task.Run(async () =>
            {
                ViewerNode.SuppressCheckCascade = true;
                try
                {
                    var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
                    await Parallel.ForEachAsync(otherPaths, parallelOptions, async (filePath, ct) =>
                    {
                        try
                        {
                            ViewerNode? rootNode = null;
                            string extension = Path.GetExtension(filePath).ToLowerInvariant();
                            string fileName = Path.GetFileName(filePath);
                            DispatcherQueue.TryEnqueue(() => LoadingDetail = fileName);

                            if (extension == ".zip")
                            {
                                rootNode = LoadZip(filePath);
                            }
                            else if (extension == ".minizip")
                            {
                                rootNode = LoadMiniZip(filePath);
                            }
                            else if (extension == ".modelbin")
                            {
                                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                rootNode = LoadModelBin(filePath, fs);
                                if (rootNode != null)
                                {
                                    rootNode.Name = fileName;
                                    if (rootNode is ModelBinNode modelBinNode)
                                        modelBinNode.FilePath = filePath;
                                }
                            }
                            else if (extension == ".bin")
                            {
                                var bytes = await File.ReadAllBytesAsync(filePath, ct);
                                if (IsLightsBinFile(bytes))
                                {
                                    rootNode = LoadLightsBin(fileName, bytes);
                                    if (rootNode is LightsBinNode lightsBinNode)
                                        lightsBinNode.FilePath = filePath;
                                }
                                else if (fileName.Equals("physicsdefinition.bin", StringComparison.OrdinalIgnoreCase))
                                {
                                    rootNode = LoadPhysicsDefinition(fileName, bytes);
                                }
                                else if (fileName.Contains("lights", StringComparison.OrdinalIgnoreCase))
                                {
                                    rootNode = LoadLightsBin(fileName, bytes);
                                    if (rootNode is LightsBinNode lightsBinNode)
                                        lightsBinNode.FilePath = filePath;
                                }
                            }
                            else if (extension == ".carbin")
                            {
                                var bytes = await File.ReadAllBytesAsync(filePath, ct);
                                rootNode = LoadCarbin(fileName, bytes, filePath: filePath);
                            }

                            if (rootNode != null)
                                parallelResults.Add((rootNode, filePath));
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading {filePath}: {ex}");
                        }
                    });
                }
                finally
                {
                    ViewerNode.SuppressCheckCascade = false;
                }
            });

            await processTask;

            foreach (var (node, path) in parallelResults)
            {
                loadedNodes.Add((node, path));
            }

            bool wasEmpty = ViewModel.Roots.Count == 0;

            foreach (var (node, _) in loadedNodes.OrderBy(x => x.Node.Name))
            {
                ViewModel.AddRoot(node);
                AddNodeToTree(node, null, deferRendering: true);
            }

            RefreshViewportTextureLookupFromLoadedRoots();

            // Bulk-load mode: defer geometry building so we can batch it on background threads.
            _isBulkLoading = true;
            foreach (var (node, _) in loadedNodes)
            {
                ApplyLODFilterRecursive(node, l0, l1, l2, l3, l4, l5, shadows);
                ApplyViewTypeFilterRecursive(node, showLights, showLocators, showPhysics);
            }
            _isBulkLoading = false;

            // Build all pending mesh geometries in parallel, then add them to the scene.
            LoadingStatus = "Building scene...";
            LoadingDetail = "";
            await BatchRenderPendingMeshesAsync();

            SyncViewDropdownItems();
            RefreshAllCarbinInstances();
            RefreshManufacturerColorsFromLoadedRoots();

            // Geometry is now rendered with color-only materials for instant display.
            // Kick off async texture loading so textures appear within a few seconds.
            _ = StartViewportTextureRefreshAsync();

            if (wasEmpty && ViewModel.Roots.Any())
                AutoFitCamera();

            UpdateAnimationExpanderVisibility();

            IsLoading = false;
            LoadingStatus = "";
            LoadingDetail = "";
        }

        private bool IsLightsBinFile(byte[] data)
        {
            // Check if file has at least 4 bytes for magic number
            if (data == null || data.Length < 4)
                return false;

            // Read first 4 bytes as little-endian uint32
            uint magic = BitConverter.ToUInt32(data, 0);
            
            // Check for 0xDEADBEEF magic number
            return magic == 0xDEADBEEF;
        }

        private static string DecodeXmlText(byte[] data)
        {
            using var stream = new MemoryStream(data);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        
        private async void OpenFiles_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".zip");
            picker.FileTypeFilter.Add(".minizip");
            picker.FileTypeFilter.Add(".modelbin");
            picker.FileTypeFilter.Add(".bin");
            picker.FileTypeFilter.Add(".carbin");
            picker.FileTypeFilter.Add(".carbin");
            picker.FileTypeFilter.Add(".xml");
            picker.FileTypeFilter.Add(".avpins");
            picker.FileTypeFilter.Add(".gr2");
            picker.FileTypeFilter.Add(".gsf");

            var files = await picker.PickMultipleFilesAsync();
            if (files.Count == 0) return;

            IsLoading = true;
            LoadingStatus = "Preparing files...";

            bool l0 = Lod0Item.IsChecked;
            bool l1 = Lod1Item.IsChecked;
            bool l2 = Lod2Item.IsChecked;
            bool l3 = Lod3Item.IsChecked;
            bool l4 = Lod4Item.IsChecked;
            bool l5 = Lod5Item.IsChecked;
            bool shadows = ShadowsItem.IsChecked;
            bool showLights = LightsItem.IsChecked;
            bool showLocators = LocatorsItem.IsChecked;
            bool showPhysics = PhysicsItem.IsChecked;

            var loadedNodes = new System.Collections.Generic.List<(ViewerNode Node, string Name)>();

            // XML and Granny files must be loaded on the UI thread
            var xmlFiles    = files.Where(f => Path.GetExtension(f.Path).Equals(".xml", StringComparison.OrdinalIgnoreCase)).ToList();
            var avpinsFiles = files.Where(f => Path.GetExtension(f.Path).Equals(".avpins", StringComparison.OrdinalIgnoreCase)).ToList();
            var grannyFiles = files.Where(f => { var ext = Path.GetExtension(f.Path).ToLowerInvariant(); return ext == ".gr2" || ext == ".gsf"; }).ToList();
            var otherFiles  = files.Where(f => !xmlFiles.Contains(f) && !avpinsFiles.Contains(f) && !grannyFiles.Contains(f)).ToList();

            foreach (var xmlFile in xmlFiles)
            {
                var node = LoadLocatorsXml(xmlFile.Path);
                if (node != null)
                {
                    loadedNodes.Add((node, xmlFile.Name));
                }
            }

            foreach (var avpinsFile in avpinsFiles)
            {
                var xmlText = DecodeXmlText(await File.ReadAllBytesAsync(avpinsFile.Path));
                var node = LoadAvPins(avpinsFile.Name, xmlText, avpinsFile.Path);
                if (node != null)
                    loadedNodes.Add((node, avpinsFile.Name));
            }

            foreach (var grannyFile in grannyFiles)
            {
                LoadingDetail = grannyFile.Name;
                var node = LoadGrannyFile(grannyFile.Path);
                if (node != null)
                    loadedNodes.Add((node, grannyFile.Name));
            }

            // Other files processed in background
            var parallelResults = new ConcurrentBag<(ViewerNode Node, string Name)>();
            var processTask = Task.Run(async () =>
            {
                ViewerNode.SuppressCheckCascade = true;
                try
                {
                    var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
                    await Parallel.ForEachAsync(otherFiles, parallelOptions, async (file, ct) =>
                    {
                        try
                        {
                            ViewerNode? rootNode = null;
                            string extension = Path.GetExtension(file.Path).ToLowerInvariant();
                            DispatcherQueue.TryEnqueue(() => LoadingDetail = file.Name);

                            if (extension == ".zip")
                            {
                                rootNode = LoadZip(file.Path);
                            }
                            else if (extension == ".minizip")
                            {
                                rootNode = LoadMiniZip(file.Path);
                            }
                            else if (extension == ".modelbin")
                            {
                                using var fs = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                                rootNode = LoadModelBin(file.Path, fs);
                                if (rootNode != null)
                                {
                                    rootNode.Name = file.Name;
                                    if (rootNode is ModelBinNode modelBinNode)
                                        modelBinNode.FilePath = file.Path;
                                }
                            }
                            else if (extension == ".bin")
                            {
                                var bytes = await File.ReadAllBytesAsync(file.Path, ct);
                                if (IsLightsBinFile(bytes))
                                {
                                    rootNode = LoadLightsBin(file.Name, bytes);
                                    if (rootNode is LightsBinNode lightsBinNode)
                                        lightsBinNode.FilePath = file.Path;
                                }
                                else if (file.Name.Equals("physicsdefinition.bin", StringComparison.OrdinalIgnoreCase))
                                {
                                    rootNode = LoadPhysicsDefinition(file.Name, bytes);
                                }
                                else if (file.Name.Equals("lights.bin", StringComparison.OrdinalIgnoreCase))
                                {
                                    rootNode = LoadLightsBin(file.Name, bytes);
                                    if (rootNode is LightsBinNode lightsBinNode)
                                        lightsBinNode.FilePath = file.Path;
                                }
                            }
                            else if (extension == ".carbin")
                            {
                                var bytes = await File.ReadAllBytesAsync(file.Path, ct);
                                rootNode = LoadCarbin(file.Name, bytes, filePath: file.Path);
                            }

                            if (rootNode != null)
                                parallelResults.Add((rootNode, file.Name));
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading {file.Name}: {ex}");
                        }
                    });
                }
                finally
                {
                    ViewerNode.SuppressCheckCascade = false;
                }
            });

            await processTask;

            foreach (var (node, name) in parallelResults)
            {
                loadedNodes.Add((node, name));
            }

            bool wasEmpty = ViewModel.Roots.Count == 0;

            foreach (var (node, _) in loadedNodes.OrderBy(x => x.Node.Name))
            {
                ViewModel.AddRoot(node);
                AddNodeToTree(node, null, deferRendering: true);
            }

            RefreshViewportTextureLookupFromLoadedRoots();

            // Bulk-load mode: defer geometry building so we can batch it on background threads.
            _isBulkLoading = true;
            foreach (var (node, _) in loadedNodes)
            {
                ApplyLODFilterRecursive(node, l0, l1, l2, l3, l4, l5, shadows);
                ApplyViewTypeFilterRecursive(node, showLights, showLocators, showPhysics);
            }
            _isBulkLoading = false;

            // Build all pending mesh geometries in parallel, then add them to the scene.
            LoadingStatus = "Building scene...";
            LoadingDetail = "";
            await BatchRenderPendingMeshesAsync();

            SyncViewDropdownItems();
            RefreshAllCarbinInstances();
            RefreshManufacturerColorsFromLoadedRoots();

            // Geometry is now rendered with color-only materials for instant display.
            // Kick off async texture loading so textures appear within a few seconds.
            _ = StartViewportTextureRefreshAsync();

            if (wasEmpty && ViewModel.Roots.Any())
                AutoFitCamera();

            UpdateAnimationExpanderVisibility();

            IsLoading = false;
            LoadingStatus = "";
            LoadingDetail = "";
        }

        private async void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null || string.IsNullOrWhiteSpace(folder.Path))
                return;

            // Treat as a single folder drop — reuse ProcessDroppedFilesAsync
            await ProcessDroppedFilesAsync(new List<string> { folder.Path });
        }
        
        private LightsBinNode? LoadLightsBin(string name, byte[] data)
        {
            try
            {
                using var stream = new MemoryStream(data);
                var parser = new LightsBinParser();
                var lightsBinData = parser.ParseToData(stream);

                var node = new LightsBinNode 
                { 
                    Name = name, 
                    FilePath = name, 
                    IsChecked = false, // Default to unchecked
                    OriginalData = lightsBinData
                };

                foreach (var group in lightsBinData.Groups)
                {
                    string displayName = string.IsNullOrEmpty(group.ModelName)
                        ? $"Light {group.Id:X8}"
                        : group.ModelName;
                    if (!string.IsNullOrEmpty(group.PresetName))
                        displayName += $" | {group.PresetName}";

                    var lightNode = new LightGroupNode
                    {
                        Name = displayName,
                        GroupData = group,
                        Parent = node,
                        IsChecked = false // Default to unchecked
                    };

                    // RowIndex 0 = Pos, 1 = Rot, 2 = DamagePos, 3 = DamageRot
                    lightNode.Children.Add(new LightRowNode { Name = $"Pos: {group.Pos.X:F5}, {group.Pos.Y:F5}, {group.Pos.Z:F5}, {group.Pos.W:F5}", Parent = lightNode, RowIndex = 0, RowData = group.Pos, IsChecked = false });
                    lightNode.Children.Add(new LightRowNode { Name = $"Rot: {group.Rot.X:F5}, {group.Rot.Y:F5}, {group.Rot.Z:F5}, {group.Rot.W:F5}", Parent = lightNode, RowIndex = 1, RowData = group.Rot, IsChecked = false });
                    lightNode.Children.Add(new LightRowNode { Name = $"DmgPos: {group.DamagePos.X:F5}, {group.DamagePos.Y:F5}, {group.DamagePos.Z:F5}, {group.DamagePos.W:F5}", Parent = lightNode, RowIndex = 2, RowData = group.DamagePos, IsChecked = false });
                    lightNode.Children.Add(new LightRowNode { Name = $"DmgRot: {group.DamageRot.X:F5}, {group.DamageRot.Y:F5}, {group.DamageRot.Z:F5}, {group.DamageRot.W:F5}", Parent = lightNode, RowIndex = 3, RowData = group.DamageRot, IsChecked = false });

                    node.Children.Add(lightNode);
                }

                node.UpdateCheckStateFromChildren();
                return node;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading lights: {ex}");
                return null;
            }
        }


        private async Task<FolderNode?> LoadFolderTree(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                return null;

            var folderNode = new FolderNode { Name = Path.GetFileName(folderPath), FolderPath = folderPath, IsChecked = true };
            var allFiles = new List<(string FilePath, string RelativeDir)>();

            try
            {
                string normalizedRoot = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                foreach (string filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(filePath);
                    if (!ViewportLoadableDropExtensions.Contains(extension))
                    {
                        // Loose texture files are already collected separately via CollectViewportFolderTextureFiles
                        continue;
                    }
                    string fullPath = Path.GetFullPath(filePath);
                    string relativePath = fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                        ? fullPath.Substring(normalizedRoot.Length)
                        : Path.GetRelativePath(folderPath, fullPath);
                    string? relativeDir = Path.GetDirectoryName(relativePath);
                    allFiles.Add((fullPath, relativeDir ?? string.Empty));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Viewport/LoadFolder] {folderPath}: {ex.Message}");
                return null;
            }

            if (allFiles.Count == 0)
            {
                // No loadable files found — still return the folder node so the user sees it
                return folderNode;
            }

            // Load files in parallel (XML/Granny on UI thread, others in background)
            var loadedEntries = new ConcurrentBag<(string FilePath, string RelativeDir, ViewerNode Node)>();

            // XML and Granny/AvPins files must be loaded on the UI thread
            var uiThreadFiles = allFiles.Where(f =>
            {
                var ext = Path.GetExtension(f.FilePath).ToLowerInvariant();
                return ext == ".xml" || ext == ".avpins" || ext == ".gr2" || ext == ".gsf";
            }).ToList();

            var bgFiles = allFiles.Except(uiThreadFiles).ToList();

            // Load UI-thread files
            foreach (var file in uiThreadFiles)
            {
                try
                {
                    string ext = Path.GetExtension(file.FilePath).ToLowerInvariant();
                    string fileName = Path.GetFileName(file.FilePath);
                    ViewerNode? node = null;

                    if (ext == ".xml")
                    {
                        node = LoadLocatorsXml(file.FilePath);
                    }
                    else if (ext == ".avpins")
                    {
                        var data = await File.ReadAllBytesAsync(file.FilePath);
                        var xmlText = DecodeXmlText(data);
                        node = LoadAvPins(fileName, xmlText, file.FilePath);
                    }
                    else if (ext == ".gr2" || ext == ".gsf")
                    {
                        node = LoadGrannyFile(file.FilePath);
                    }

                    if (node != null)
                        loadedEntries.Add((file.FilePath, file.RelativeDir, node));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Viewport/LoadFolder] {file.FilePath}: {ex}");
                }
            }

            // Load background-thread files in parallel
            await Task.Run(async () =>
            {
                ViewerNode.SuppressCheckCascade = true;
                try
                {
                    var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
                    await Parallel.ForEachAsync(bgFiles, parallelOptions, async (file, ct) =>
                    {
                        try
                        {
                            string ext = Path.GetExtension(file.FilePath).ToLowerInvariant();
                            string fileName = Path.GetFileName(file.FilePath);
                            ViewerNode? node = null;
                            DispatcherQueue.TryEnqueue(() => LoadingDetail = fileName);

                            if (ext == ".zip")
                            {
                                node = LoadZip(file.FilePath);
                            }
                            else if (ext == ".minizip")
                            {
                                node = LoadMiniZip(file.FilePath);
                            }
                            else if (ext == ".modelbin")
                            {
                                using var fs = new FileStream(file.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                node = LoadModelBin(file.FilePath, fs);
                                if (node != null)
                                {
                                    node.Name = fileName;
                                    if (node is ModelBinNode modelBinNode)
                                        modelBinNode.FilePath = file.FilePath;
                                }
                            }
                            else if (ext == ".bin")
                            {
                                var bytes = await File.ReadAllBytesAsync(file.FilePath, ct);
                                if (fileName.Equals("manufacturercolors.bin", StringComparison.OrdinalIgnoreCase))
                                {
                                    var mfgColors = TryLoadManufacturerColors(bytes);
                                    if (mfgColors != null)
                                        folderNode.ManufacturerColors = mfgColors;
                                }
                                else if (IsLightsBinFile(bytes))
                                {
                                    node = LoadLightsBin(fileName, bytes);
                                    if (node is LightsBinNode lightsBinNode)
                                        lightsBinNode.FilePath = file.FilePath;
                                }
                                else if (fileName.Equals("physicsdefinition.bin", StringComparison.OrdinalIgnoreCase))
                                {
                                    node = LoadPhysicsDefinition(fileName, bytes);
                                }
                                else if (fileName.Contains("lights", StringComparison.OrdinalIgnoreCase))
                                {
                                    node = LoadLightsBin(fileName, bytes);
                                    if (node is LightsBinNode lightsBinNode)
                                        lightsBinNode.FilePath = file.FilePath;
                                }
                            }
                            else if (ext == ".carbin")
                            {
                                var bytes = await File.ReadAllBytesAsync(file.FilePath, ct);
                                node = LoadCarbin(fileName, bytes, filePath: file.FilePath);
                            }

                            if (node != null)
                                loadedEntries.Add((file.FilePath, file.RelativeDir, node));
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Viewport/LoadFolder] {file.FilePath}: {ex}");
                        }
                    });
                }
                finally
                {
                    ViewerNode.SuppressCheckCascade = false;
                }
            });

            // Build folder hierarchy using the same folderDict pattern as LoadZip
            var folderDict = new Dictionary<string, ViewerNode>(StringComparer.OrdinalIgnoreCase)
            {
                ["."] = folderNode
            };

            // Sort entries by path for deterministic ordering
            var sortedEntries = loadedEntries
                .OrderBy(e => e.RelativeDir, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => Path.GetFileName(e.FilePath), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in sortedEntries)
            {
                string relDir = string.IsNullOrEmpty(entry.RelativeDir) ? "." : entry.RelativeDir.Replace('\\', '/');
                var parts = relDir.Split('/', StringSplitOptions.RemoveEmptyEntries);

                ViewerNode currentParent = folderNode;
                string currentPath = ".";

                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i];
                    string newPath = currentPath + "/" + part;

                    if (!folderDict.TryGetValue(newPath, out var existingFolder))
                    {
                        existingFolder = new FolderNode { Name = part, Parent = currentParent, IsChecked = true };
                        folderDict[newPath] = existingFolder;
                        currentParent.Children.Add(existingFolder);
                    }

                    currentParent = existingFolder;
                    currentPath = newPath;
                }

                entry.Node.Parent = currentParent;
                currentParent.Children.Add(entry.Node);
            }

            folderNode.UpdateCheckStateFromChildren();
            return folderNode;
        }

        private ZipNode? LoadZip(string path)
        {
             var zipNode = new ZipNode { Name = Path.GetFileName(path), FilePath = path, IsChecked = true };
             
             try
             {
                 using (var zip = new CustomZipFile(path))
                 {
                     var entries = zip.GetEntries().Where(e => !e.IsDirectory && 
                         (e.Name.EndsWith(".modelbin", StringComparison.OrdinalIgnoreCase) ||
                          e.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                          e.Name.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase) ||
                          e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                          e.Name.EndsWith(".avpins", StringComparison.OrdinalIgnoreCase) ||
                          e.Name.EndsWith(".gr2", StringComparison.OrdinalIgnoreCase) ||
                          e.Name.EndsWith(".gsf", StringComparison.OrdinalIgnoreCase))).ToList();
                          
                     var results = new List<(CustomZipFile.ZipEntryInfo Entry, ViewerNode Node)>();

                     // Process entries sequentially ? CustomZipFile uses a lock for stream access,
                     // so Parallel.ForEach would serialize on the lock anyway while adding thread overhead.
                     foreach (var entry in entries)
                     {
                         try
                         {
                             string fileName = Path.GetFileName(entry.Name);
                             ViewerNode? node = null;
                             DispatcherQueue.TryEnqueue(() => LoadingDetail = fileName);

                             if (fileName.EndsWith(".modelbin", StringComparison.OrdinalIgnoreCase))
                             {
                                 // Use RecyclableMemoryStream to avoid LOH allocations for large modelbin entries
                                 using var rms = _msManager.GetStream("ZipModelBin", (int)entry.UncompressedSize);
                                 zip.ExtractToStream(entry, rms);
                                 rms.Position = 0;
                                 var binNode = LoadModelBin(fileName, rms);
                                 if (binNode != null)
                                 {
                                     binNode.SourceZipPath = path;
                                     binNode.ZipEntryName = entry.Name;
                                     node = binNode;
                                 }
                             }
                             else if (fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                                      fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                             {
                                 // .bin and .xml entries are typically small ? ExtractToMemory is fine
                                 var bytes = zip.ExtractToMemory(entry);

                                 if (fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                                 {
                                     if (fileName.Equals("manufacturercolors.bin", StringComparison.OrdinalIgnoreCase))
                                     {
                                         zipNode.ManufacturerColors = TryLoadManufacturerColors(bytes);
                                     }
                                     else if (IsLightsBinFile(bytes))
                                     {
                                         node = LoadLightsBin(fileName, bytes);
                                         if (node is LightsBinNode lightsNode)
                                         {
                                             lightsNode.FilePath = null;
                                             lightsNode.SourceZipPath = path;
                                             lightsNode.ZipEntryName = entry.Name;
                                         }
                                     }
                                     else if (fileName.Equals("physicsdefinition.bin", StringComparison.OrdinalIgnoreCase))
                                     {
                                         node = LoadPhysicsDefinition(fileName, bytes);
                                     }
                                     else if (fileName.Contains("lights", StringComparison.OrdinalIgnoreCase))
                                     {
                                         node = LoadLightsBin(fileName, bytes);
                                         if (node is LightsBinNode lightsNode)
                                         {
                                             lightsNode.FilePath = null;
                                             lightsNode.SourceZipPath = path;
                                             lightsNode.ZipEntryName = entry.Name;
                                         }
                                     }
                                 }
                                 else if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                                 {
                                     // Save to temp file to use existing XML parser
                                     string tempPath = Path.GetTempFileName();
                                     File.WriteAllBytes(tempPath, bytes);
                                     node = LoadLocatorsXml(tempPath, fileName);
                                     if (node != null)
                                     {
                                         node.Name = fileName;
                                         if (node is LocatorsXmlNode locNode)
                                         {
                                             locNode.FilePath = null; // Don't save back to temp file
                                             locNode.SourceZipPath = path;
                                             locNode.ZipEntryName = entry.Name;
                                         }
                                     }
                                     File.Delete(tempPath);
                                 }
                             }
                             else if (fileName.EndsWith(".avpins", StringComparison.OrdinalIgnoreCase))
                             {
                                 var bytes = zip.ExtractToMemory(entry);
                                 var xmlText = DecodeXmlText(bytes);
                                node = LoadAvPins(fileName, xmlText, null, path, entry.Name);
                             }
                             else if (fileName.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                             {
                                 var bytes = zip.ExtractToMemory(entry);
                                 node = LoadCarbin(fileName, bytes, sourceZipPath: path, zipEntryName: entry.Name);
                             }
                             else if (fileName.EndsWith(".gr2", StringComparison.OrdinalIgnoreCase) ||
                                      fileName.EndsWith(".gsf", StringComparison.OrdinalIgnoreCase))
                             {
                                 var bytes = zip.ExtractToMemory(entry);
                                 var gr2Node = LoadGrannyFileFromBytes(fileName, bytes, sourceZipPath: path);
                                 if (gr2Node != null)
                                     node = gr2Node;
                             }

                             if (node != null)
                             {
                                 results.Add((entry, node));
                             }
                         }
                         catch (Exception ex)
                         {
                             System.Diagnostics.Debug.WriteLine($"Error loading ZIP entry {entry.Name}: {ex}");
                         }
                     }
                     
                     var folderDict = new Dictionary<string, ViewerNode>();
                     folderDict[""] = zipNode;
                     
                     foreach (var res in results.OrderBy(r => r.Entry.Name))
                     {
                         var entry = res.Entry;
                         var node = res.Node;

                         string relativePath = entry.Name.Replace("\\", "/");
                         var parts = relativePath.Split('/');
                         ViewerNode currentParent = zipNode;
                         string currentPath = "";
                         
                         for (int i = 0; i < parts.Length - 1; i++)
                         {
                             string part = parts[i];
                             string newPath = currentPath + part + "/";
                             
                             if (!folderDict.ContainsKey(newPath))
                             {
                                 var folder = new FolderNode { Name = part, Parent = currentParent, IsChecked = true };
                                 folderDict[newPath] = folder;
                                 currentParent.Children.Add(folder);
                             }
                             
                             currentParent = folderDict[newPath];
                             currentPath = newPath;
                         }
                         
                         node.Parent = currentParent;
                         currentParent.Children.Add(node);
                     }

                     zipNode.UpdateCheckStateFromChildren();
                 }
                 return zipNode;
             }
             catch
             {
                 return null;
             }
        }

        private static ManufacturerColorsBlob? TryLoadManufacturerColors(byte[] bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                var bundle = new Bundle();
                bundle.Load(stream);
                return bundle.Blobs.OfType<ManufacturerColorsBlob>().FirstOrDefault();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading manufacturer colors: {ex}");
                return null;
            }
        }

        // Loads a Playground MiniZip (.minizip / PGZP) file, extracting all ModelBin entries
        // and any other supported content. Returns a <see cref="ZipNode"/> whose children
        // mirror the flat index-addressed structure of the archive.
        private ZipNode? LoadMiniZip(string path)
        {
            var zipNode = new ZipNode { Name = Path.GetFileName(path), FilePath = path, IsChecked = true };

            try
            {
                using var minizip = new MiniZipService(path);
                var results = new List<(MiniZipEntryInfo Entry, ViewerNode Node)>();

                foreach (var entry in minizip.Entries)
                {
                        try
                        {
                            ViewerNode? node = null;

                            if (entry.ResourceType == MiniZipResourceType.ModelBin)
                        {
                            // Extract the compressed/stored payload directly into a memory stream
                            using var ms = new MemoryStream((int)entry.UncompressedSize);
                            minizip.ExtractEntryToStream(entry.Index, ms);
                            ms.Position = 0;

                            string entryName = string.IsNullOrEmpty(entry.Name) || entry.Name == $"{entry.Index}.{entry.Extension}"
                                ? $"{entry.Index}.modelbin"
                                : entry.Name;

                            var binNode = LoadModelBin(entryName, ms);
                            if (binNode != null)
                            {
                                binNode.Name = Path.GetFileName(entryName);
                                binNode.SourceZipPath = path;
                                binNode.ZipEntryName = entryName;
                                node = binNode;
                            }
                        }

                        if (node != null)
                            results.Add((entry, node));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MiniZip] Error loading entry {entry.Index}: {ex.Message}");
                    }
                }

                // MiniZip is a flat archive � all entries hang directly under the root ZipNode
                foreach (var (entry, node) in results.OrderBy(r => r.Entry.Index))
                {
                    node.Parent = zipNode;
                    zipNode.Children.Add(node);
                }

                zipNode.UpdateCheckStateFromChildren();
                return zipNode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MiniZip] Failed to load {path}: {ex.Message}");
                return null;
            }
        }

        private ModelBinNode? LoadModelBin(string name, Stream stream)
        {
            try 
            {
                var bundle = new Bundle();
                if (stream.CanSeek)
                {
                    bundle.Load(stream);
                }
                else
                {
                    // Non-seekable streams (rare) need buffering; use pooled memory
                    using var rms = _msManager.GetStream("LoadModelBin");
                    stream.CopyTo(rms);
                    rms.Position = 0;
                    bundle.Load(rms);
                }
                
                var binNode = new ModelBinNode
                {
                    Name = name,
                    FileName = Path.GetFileName(name),
                    Bundle = bundle,
                    IsChecked = !ContainsProxyToken(name)
                };

                var importer = new ModelImporter();
                var result = importer.ExtractModels(bundle);
                
                // Get skeleton blob for bone references
                var skeleton = bundle.Blobs.OfType<SkeletonBlob>().FirstOrDefault();

                foreach (var meshData in result.Meshes)
                {

                    
                    // Attach direct bone reference if skeleton exists
                    if (skeleton != null && meshData.BoneIndex >= 0 && meshData.BoneIndex < skeleton.Bones.Count)
                    {
                        meshData.SourceBone = skeleton.Bones[meshData.BoneIndex];
                        
                        // Store original mesh translate (preserve as-is, no migration)
                        meshData.OriginalMeshTranslateRelativeToBone = 
                            meshData.SourceMesh?.PositionTranslate ?? Vector4.Zero;
                    }

                    bool isShadow = meshData.Name.Contains("Shadow", StringComparison.OrdinalIgnoreCase);
                    
                    var meshNode = new MeshNode
                    {
                        Name = string.IsNullOrEmpty(meshData.MaterialName) ? meshData.Name : $"{meshData.Name} [{meshData.MaterialName}]",
                        GeometryData = meshData,
                        LODLevel = 0,
                        IsShadow = isShadow,
                        Parent = binNode,
                        IsChecked = false,
                        OriginalPositionScale = meshData.SourceMesh?.PositionScale ?? Vector4.One,
                        OriginalPositionTranslate = meshData.SourceMesh?.PositionTranslate ?? Vector4.Zero,
                        ParentModelBin = binNode
                    };
                    
                    binNode.Children.Add(meshNode);

                    // Add damage mesh node if morph buffer decoded successfully
                    if (meshData.HasDamageModel)
                    {
                        var dmgGeo = new ForzaGeometryData
                        {
                            Name         = meshData.Name,
                            MaterialName = meshData.MaterialName,
                            Indices      = meshData.Indices,
                            Normals      = meshData.Normals,
                            UVs          = meshData.UVs,
                            RawPositions = meshData.DamageRawPositions,
                            SourceMesh   = meshData.SourceMesh,
                            BoneTransform = meshData.BoneTransform,
                            BoneIndex    = meshData.BoneIndex,
                            BoneName     = meshData.BoneName,
                            OriginalBoneTransform = meshData.OriginalBoneTransform
                        };
                        var dmgNode = new DamageMeshNode
                        {
                            Name           = $"[Damage] {meshData.Name}",
                            GeometryData   = dmgGeo,
                            Parent         = binNode,
                            IsChecked      = false,
                            ParentModelBin = binNode,
                            IsShadow       = isShadow
                        };
                        binNode.Children.Add(dmgNode);
                    }
                }
                
                binNode.UpdateCheckStateFromChildren();
                return binNode;
            }
            catch
            {
                return null;
            }
        }

        private CarbinFileNode? LoadCarbin(string name, byte[] data, string? filePath = null, string? sourceZipPath = null, string? zipEntryName = null)
        {
            try
            {
                using var stream = new MemoryStream(data);
                var carbinFile = new CarbinFile();
                carbinFile.Load(stream);

                var node = new CarbinFileNode
                {
                    Name = name,
                    FilePath = filePath,
                    SourceZipPath = sourceZipPath,
                    ZipEntryName = zipEntryName,
                    CarbinData = carbinFile,
                    IsChecked = true
                };

                PopulateCarbinNodes(node);
                node.UpdateCheckStateFromChildren();
                return node;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading carbin: {ex}");
                return null;
            }
        }

        private static void PopulateCarbinNodes(CarbinFileNode fileNode)
        {
            var scene = fileNode.CarbinData?.Scene;
            if (scene == null)
                return;

            foreach (var entry in scene.NonUpgradableParts)
            {
                string partName = entry.Type.ToString();
                var partNode = new CarbinPartNode
                {
                    Name = $"{partName} ({entry.Part.Models.Count})",
                    Parent = fileNode,
                    PartCategory = "Non-Upgradable",
                    PartData = entry,
                    IsChecked = true
                };
                AddCarbinModelNodes(partNode, entry.Part.Models, partName);
                fileNode.Children.Add(partNode);
            }

            foreach (var part in scene.UpgradableParts)
            {
                int modelCount = part.Upgrades.Sum(upgrade => upgrade.Models.Count) + part.SharedModels.Count;
                string partName = part.Type.ToString();
                var partNode = new CarbinPartNode
                {
                    Name = $"{partName} upgrades ({modelCount})",
                    Parent = fileNode,
                    PartCategory = "Upgradable",
                    PartData = part,
                    IsChecked = true
                };

                foreach (var upgrade in part.Upgrades)
                    AddCarbinModelNodes(partNode, upgrade.Models, $"{partName} upgrade {upgrade.Id}");

                foreach (var sharedModel in part.SharedModels)
                    AddCarbinModelNodes(partNode, new[] { sharedModel.Model }, $"{partName} shared");

                fileNode.Children.Add(partNode);
            }
        }

        private static void AddCarbinModelNodes(CarbinPartNode partNode, IEnumerable<CarRenderModel> models, string partName)
        {
            int index = 0;
            foreach (var model in models)
            {
                var modelNode = new CarbinModelNode
                {
                    Name = BuildCarbinModelDisplayName(model),
                    Parent = partNode,
                    Model = model,
                    ModelIndex = index,
                    PartName = partName,
                    UseTransforms = !IsRootCarbinBoneName(model.BoneName),
                    IsChecked = true
                };
                partNode.Children.Add(modelNode);
                index++;
            }
        }

        private static string BuildCarbinModelDisplayName(CarRenderModel model)
        {
            string modelName = string.IsNullOrEmpty(model.Path) ? "model" : Path.GetFileName(model.Path.Replace('\\', '/'));
            string boneText = string.IsNullOrEmpty(model.BoneName) ? $"bone {model.BoneId}" : model.BoneName;
            return $"{modelName} [{boneText}]";
        }

        private PhysicsDefinitionNode? LoadPhysicsDefinition(string name, byte[] data)
        {
            try
            {
                using var stream = new MemoryStream(data);
                var parser = new PhysicsDefinitionParser();
                var definitionsList = parser.Parse(stream);
                
                var node = new PhysicsDefinitionNode { Name = name, Definitions = definitionsList.Definitions, IsChecked = false }; // Default to unchecked
                
                int count = 0;
                foreach (var def in definitionsList.Definitions)
                {
                    foreach (var shape in def.Shapes)
                    {
                        if (shape.PointCloud != null)
                        {
                            var meshNode = new MeshNode
                            {
                                Name = $"PointCloud Shape {count}",
                                Parent = node,
                                IsChecked = false, // Default to unchecked
                                LODLevel = 0,
                                GeometryData = new ForzaGeometryData 
                                { 
                                    Name = $"PointCloud {count}",
                                    Positions = shape.PointCloud.Points.ToArray(),
                                }
                            };
                            node.Children.Add(meshNode);
                            count++;
                        }
                    }
                }
                
                node.UpdateCheckStateFromChildren();
                return node;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading physics: {ex}");
                return null;
            }
        }

        private async Task ReplaceModelBinInZip(ModelBinNode binNode)
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".modelbin");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    IsLoading = true;
                    LoadingStatus = "Replacing file in ZIP...";

                    var newBytes = await File.ReadAllBytesAsync(file.Path);
                    
                    if (binNode.SourceZipPath != null && binNode.ZipEntryName != null)
                    {
                        foreach (var child in binNode.Children.ToList())
                        {
                            CleanupNodeRecusrive(child);
                        }

                        if (_treeNodeMap.TryGetValue(binNode, out var existingTreeNode))
                        {
                            existingTreeNode.Children.Clear();
                        }

                        binNode.Children.Clear();

                        await Task.Run(() => ZipArchiveHelper.ReplaceEntry(binNode.SourceZipPath, binNode.ZipEntryName, newBytes));

                        ModelBinNode? newTempNode;
                        using (var ms = new MemoryStream(newBytes))
                        {
                            newTempNode = LoadModelBin(file.Name, ms);
                        }
                        if (newTempNode != null)
                        {
                            binNode.Bundle = newTempNode.Bundle;
                            foreach (var child in newTempNode.Children)
                            {
                                child.Parent = binNode;
                                binNode.Children.Add(child);
                            }

                            if (_treeNodeMap.TryGetValue(binNode, out var treeNode))
                            {
                                treeNode.Children.Clear();
                                foreach (var child in binNode.Children)
                                {
                                    AddNodeToTree(child, treeNode);
                                }
                            }

                            RefreshModelList();
                            if (ViewModel.SelectedNode == binNode)
                            {
                                ViewModel.SelectedNode = null;
                                ViewModel.SelectedNode = binNode;
                            }

                            LoadingStatus = "File replaced and reloaded.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    await ShowError($"Failed to replace file in ZIP: {ex.Message}");
                }
                finally
                {
                    IsLoading = false;
                    LoadingStatus = "";
                }
            }
        }

        private LocatorsXmlNode? LoadLocatorsXml(string filePath, string? displayName = null)
        {
            string xmlName = string.IsNullOrWhiteSpace(displayName)
                ? System.IO.Path.GetFileName(filePath)
                : displayName;

            try
            {
                var parser = new LocatorsXmlParser();
                var data = parser.Parse(filePath);

                if (data.Locators.Count == 0)
                {
                    if (IsLikelyLocatorXmlFile(xmlName))
                    {
                        _ = ShowError($"No <Locator> entries found in:\n{xmlName}\n\nMake sure the root element contains <Locator> children with a <Name value=\"...\"/> element.");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[Viewport/XML] Skipping unsupported XML: {xmlName}");
                    }

                    return null;
                }

                var rootNode = new LocatorsXmlNode
                {
                    Name = xmlName,
                    FilePath = filePath,
                    LocatorsData = data,
                    IsChecked = false, // Default to unchecked
                    IsExpanded = false
                };

                foreach (var entry in data.Locators)
                {
                    var locNode = new LocatorNode
                    {
                        Name = string.IsNullOrEmpty(entry.Name) ? $"(unnamed {data.Locators.IndexOf(entry)})" : entry.Name,
                        LocatorEntry = entry,
                        Parent = rootNode,
                        IsChecked = false // Default to unchecked
                    };
                    rootNode.Children.Add(locNode);
                }

                rootNode.UpdateCheckStateFromChildren();
                return rootNode;
            }
            catch (Exception ex)
            {
                if (IsLikelyLocatorXmlFile(xmlName))
                {
                    _ = ShowError($"Failed to open locators XML:\n{xmlName}\n\n{ex.Message}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Viewport/XML] Skipping unsupported XML {xmlName}: {ex.Message}");
                }

                return null;
            }
        }

        private static bool IsLikelyLocatorXmlFile(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            return fileName.Contains("locator", StringComparison.OrdinalIgnoreCase);
        }

        private AvPinsFileNode? LoadAvPins(
            string name,
            string xmlText,
            string? filePath,
            string? sourceZipPath = null,
            string? zipEntryName = null)
        {
            try
            {
                var data = AvPinsParser.Parse(xmlText);

                var rootNode = new AvPinsFileNode
                {
                    Name         = name,
                    FilePath     = filePath,
                    AvPinsData   = data,
                    SourceZipPath = sourceZipPath,
                    ZipEntryName  = zipEntryName,
                    IsChecked    = false,
                    IsExpanded   = false
                };

                int idx = 0;
                foreach (var poi in data.POIs)
                {
                    // Skip removed entries or POIs without a visibility position
                    if (poi.OverrideMode == "Remove" || poi.Visibility == null)
                    {
                        idx++;
                        continue;
                    }

                    var pinNode = new AvPinNode
                    {
                        Name    = string.IsNullOrEmpty(poi.Name) ? $"(unnamed {idx})" : poi.Name,
                        PoiData = poi,
                        Parent  = rootNode,
                        IsChecked = false
                    };
                    rootNode.Children.Add(pinNode);
                    idx++;
                }

                rootNode.UpdateCheckStateFromChildren();
                return rootNode;
            }
            catch (Exception ex)
            {
                _ = ShowError($"Failed to parse .avpins:\n{name}\n\n{ex.Message}");
                return null;
            }
        }

        private GrannyFileNode? LoadGrannyFileFromBytes(string fileName, byte[] bytes, string? sourceZipPath = null)
        {
            GrannyFileData? data = null;
            string? parseError = null;
            try
            {
                var parser = new GrannyParserService();
                data = parser.Parse(bytes, fileName);
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
            }

            if (data == null)
            {
                return new GrannyFileNode
                {
                    Name = $"{fileName} [LOAD ERROR: {parseError ?? "null"}]",
                    FilePath = fileName,
                    SourceZipPath = sourceZipPath,
                    FileData = new GrannyFileData { IsValid = false, StatusMessage = parseError ?? "null result" },
                    IsGsf = false,
                    IsChecked = true,
                    IsExpanded = false
                };
            }

            if (!data.IsValid)
            {
                return new GrannyFileNode
                {
                    Name = $"{fileName} [PARSE ERROR: {data.StatusMessage}]",
                    FilePath = fileName,
                    SourceZipPath = sourceZipPath,
                    FileData = data,
                    IsGsf = false,
                    IsChecked = true,
                    IsExpanded = false
                };
            }

            // Delegate to the full node-building path via a temp-style call reusing the same logic.
            // Build a GrannyFileNode with the parsed data (identical to LoadGrannyFile logic).
            var labelParts = new List<string>();
            if (data.IsGsf) labelParts.Add("GSF"); else labelParts.Add("GR2");
            if (data.Skeletons.Count > 0) labelParts.Add($"{data.Skeletons.Count} skel");
            if (data.Animations.Count > 0) labelParts.Add($"{data.Animations.Count} anim");
            if (data.TrackGroups.Count > 0) labelParts.Add($"{data.TrackGroups.Count} tracks");
            if (data.CharacterInfo != null) labelParts.Add($"{data.CharacterInfo.AnimationSetCount} sets");

            string label = $"{fileName} [{string.Join(", ", labelParts)}]";

            var rootNode = new GrannyFileNode
            {
                Name = label,
                FilePath = fileName,
                SourceZipPath = sourceZipPath,
                FileData = data,
                IsGsf = data.IsGsf,
                IsChecked = true,
                IsExpanded = true
            };

            foreach (var skeleton in data.Skeletons)
            {
                var skelNode = new SkeletonNode
                {
                    Name = $"Skeleton: {skeleton.Name ?? "unnamed"} ({skeleton.Bones.Count} bones)",
                    SkeletonData = skeleton,
                    Parent = rootNode,
                    IsChecked = true,
                    IsExpanded = false
                };
                var boneNodes = new Dictionary<int, BoneNode>();
                for (int i = 0; i < skeleton.Bones.Count; i++)
                {
                    var bone = skeleton.Bones[i];
                    boneNodes[i] = new BoneNode
                    {
                        Name = $"{bone.Name ?? $"bone_{i}"} (idx:{i}, parent:{bone.ParentIndex})",
                        BoneData = bone, BoneIndex = i, IsChecked = true
                    };
                }
                foreach (var kvp in boneNodes)
                {
                    var bone = skeleton.Bones[kvp.Key];
                    if (bone.ParentIndex >= 0 && boneNodes.TryGetValue(bone.ParentIndex, out var pb))
                    { kvp.Value.Parent = pb; pb.Children.Add(kvp.Value); }
                    else
                    { kvp.Value.Parent = skelNode; skelNode.Children.Add(kvp.Value); }
                }
                rootNode.Children.Add(skelNode);
            }

            foreach (var anim in data.Animations)
            {
                int trackCount = anim.TrackGroups.Sum(tg => tg.TransformTracks.Count);
                var animNode = new AnimationClipNode
                {
                    Name = $"Anim: {anim.Name ?? "unnamed"} ({anim.Duration:F2}s, {trackCount} tracks)",
                    AnimationData = anim, Parent = rootNode, IsChecked = false
                };
                foreach (var tg in anim.TrackGroups)
                {
                    var tgFolder = new FolderNode
                    {
                        Name = $"TrackGroup: {tg.Name ?? "unnamed"} ({tg.TransformTracks.Count} tracks)",
                        Parent = animNode, IsChecked = false
                    };
                    foreach (var tt in tg.TransformTracks)
                        tgFolder.Children.Add(new FolderNode { Name = $"Track: {tt.Name ?? "unnamed"}", Parent = tgFolder, IsChecked = false });
                    animNode.Children.Add(tgFolder);
                }
                rootNode.Children.Add(animNode);
            }

            if (data.CharacterInfo != null)
            {
                var gsfNode = new GsfInfoNode
                {
                    Name = $"State Machine ({data.CharacterInfo.AnimationSlotCount} slots, {data.CharacterInfo.AnimationSetCount} sets)",
                    CharacterInfoData = data.CharacterInfo, Parent = rootNode, IsChecked = false, IsExpanded = true
                };
                foreach (var slot in data.CharacterInfo.AnimationSlots)
                    gsfNode.Children.Add(new FolderNode { Name = $"Slot [{slot.Index}]: {slot.Name}", Parent = gsfNode, IsChecked = false });
                foreach (var set in data.CharacterInfo.AnimationSets)
                {
                    var setFolder = new FolderNode
                    {
                        Name = $"Set: {set.Name ?? "unnamed"} ({set.SourceFileReferences.Count} refs, {set.AnimationSpecs.Count} specs)",
                        Parent = gsfNode, IsChecked = false, IsExpanded = true
                    };
                    foreach (var sfr in set.SourceFileReferences)
                    {
                        string refFileName = Path.GetFileName(sfr.SourceFilename ?? "");
                        setFolder.Children.Add(new FolderNode { Name = $"?? {refFileName} ({sfr.ExpectedAnimCount} anims, CRC:0x{sfr.AnimCRC:X8})", Parent = setFolder, IsChecked = false });
                    }
                    foreach (var spec in set.AnimationSpecs)
                        setFolder.Children.Add(new FolderNode { Name = $"Spec [{spec.AnimationIndex}]: {spec.ExpectedName}", Parent = setFolder, IsChecked = false });
                    gsfNode.Children.Add(setFolder);
                }
                rootNode.Children.Add(gsfNode);
            }

            if (!string.IsNullOrEmpty(data.StatusMessage) &&
                data.Skeletons.Count == 0 && data.Animations.Count == 0 && data.CharacterInfo == null)
                rootNode.Children.Add(new FolderNode { Name = $"? {data.StatusMessage}", Parent = rootNode, IsChecked = false });

            rootNode.UpdateCheckStateFromChildren();
            return rootNode;
        }

        private GrannyFileNode? LoadGrannyFile(string filePath)
        {
            GrannyFileData? data = null;
            string? parseError = null;
            try
            {
                var parser = new GrannyParserService();
                data = parser.Parse(filePath);
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
            }

            if (data == null)
            {
                return new GrannyFileNode
                {
                    Name = $"{Path.GetFileName(filePath)} [LOAD ERROR: {parseError ?? "null"}]",
                    FilePath = filePath,
                    FileData = new GrannyFileData { IsValid = false, StatusMessage = parseError ?? "null result" },
                    IsGsf = false,
                    IsChecked = true,
                    IsExpanded = false
                };
            }

            if (!data.IsValid)
            {
                return new GrannyFileNode
                {
                    Name = $"{Path.GetFileName(filePath)} [PARSE ERROR: {data.StatusMessage}]",
                    FilePath = filePath,
                    FileData = data,
                    IsGsf = false,
                    IsChecked = true,
                    IsExpanded = false
                };
            }

            string fileName = Path.GetFileName(filePath);

            // Build descriptive label
            var labelParts = new List<string>();
            if (data.IsGsf) labelParts.Add("GSF");
            else labelParts.Add("GR2");

            if (data.Skeletons.Count > 0)
                labelParts.Add($"{data.Skeletons.Count} skel");
            if (data.Animations.Count > 0)
                labelParts.Add($"{data.Animations.Count} anim");
            if (data.TrackGroups.Count > 0)
                labelParts.Add($"{data.TrackGroups.Count} tracks");
            if (data.CharacterInfo != null)
                labelParts.Add($"{data.CharacterInfo.AnimationSetCount} sets");

            string label = $"{fileName} [{string.Join(", ", labelParts)}]";

            var rootNode = new GrannyFileNode
            {
                Name = label,
                FilePath = filePath,
                FileData = data,
                IsGsf = data.IsGsf,
                IsChecked = true,
                IsExpanded = true
            };

            // Add skeletons
            foreach (var skeleton in data.Skeletons)
            {
                var skelNode = new SkeletonNode
                {
                    Name = $"Skeleton: {skeleton.Name ?? "unnamed"} ({skeleton.Bones.Count} bones)",
                    SkeletonData = skeleton,
                    Parent = rootNode,
                    IsChecked = true,
                    IsExpanded = false
                };

                // Build bone hierarchy via parent indices
                var boneNodes = new Dictionary<int, BoneNode>();
                for (int i = 0; i < skeleton.Bones.Count; i++)
                {
                    var bone = skeleton.Bones[i];
                    var boneNode = new BoneNode
                    {
                        Name = $"{bone.Name ?? $"bone_{i}"} (idx:{i}, parent:{bone.ParentIndex})",
                        BoneData = bone,
                        BoneIndex = i,
                        IsChecked = true
                    };
                    boneNodes[i] = boneNode;
                }

                foreach (var kvp in boneNodes)
                {
                    var bone = skeleton.Bones[kvp.Key];
                    if (bone.ParentIndex >= 0 && boneNodes.TryGetValue(bone.ParentIndex, out var parentBoneNode))
                    {
                        kvp.Value.Parent = parentBoneNode;
                        parentBoneNode.Children.Add(kvp.Value);
                    }
                    else
                    {
                        kvp.Value.Parent = skelNode;
                        skelNode.Children.Add(kvp.Value);
                    }
                }

                rootNode.Children.Add(skelNode);
            }

            // Add animations
            foreach (var anim in data.Animations)
            {
                int trackCount = anim.TrackGroups.Sum(tg => tg.TransformTracks.Count);
                var animNode = new AnimationClipNode
                {
                    Name = $"Anim: {anim.Name ?? "unnamed"} ({anim.Duration:F2}s, {trackCount} tracks)",
                    AnimationData = anim,
                    Parent = rootNode,
                    IsChecked = false
                };

                foreach (var tg in anim.TrackGroups)
                {
                    var tgFolder = new FolderNode
                    {
                        Name = $"TrackGroup: {tg.Name ?? "unnamed"} ({tg.TransformTracks.Count} tracks)",
                        Parent = animNode,
                        IsChecked = false
                    };
                    foreach (var tt in tg.TransformTracks)
                    {
                        tgFolder.Children.Add(new FolderNode
                        {
                            Name = $"Track: {tt.Name ?? "unnamed"}",
                            Parent = tgFolder,
                            IsChecked = false
                        });
                    }
                    animNode.Children.Add(tgFolder);
                }

                rootNode.Children.Add(animNode);
            }

            // Add GSF state machine info
            if (data.CharacterInfo != null)
            {
                var gsfNode = new GsfInfoNode
                {
                    Name = $"State Machine ({data.CharacterInfo.AnimationSlotCount} slots, {data.CharacterInfo.AnimationSetCount} sets)",
                    CharacterInfoData = data.CharacterInfo,
                    Parent = rootNode,
                    IsChecked = false,
                    IsExpanded = true
                };

                foreach (var slot in data.CharacterInfo.AnimationSlots)
                {
                    gsfNode.Children.Add(new FolderNode
                    {
                        Name = $"Slot [{slot.Index}]: {slot.Name}",
                        Parent = gsfNode,
                        IsChecked = false
                    });
                }

                foreach (var set in data.CharacterInfo.AnimationSets)
                {
                    var setFolder = new FolderNode
                    {
                        Name = $"Set: {set.Name ?? "unnamed"} ({set.SourceFileReferences.Count} refs, {set.AnimationSpecs.Count} specs)",
                        Parent = gsfNode,
                        IsChecked = false,
                        IsExpanded = true
                    };
                    foreach (var sfr in set.SourceFileReferences)
                    {
                        string refFileName = Path.GetFileName(sfr.SourceFilename ?? "");
                        setFolder.Children.Add(new FolderNode
                        {
                            Name = $"?? {refFileName} ({sfr.ExpectedAnimCount} anims, CRC:0x{sfr.AnimCRC:X8})",
                            Parent = setFolder,
                            IsChecked = false
                        });
                    }
                    foreach (var spec in set.AnimationSpecs)
                    {
                        setFolder.Children.Add(new FolderNode
                        {
                            Name = $"Spec [{spec.AnimationIndex}]: {spec.ExpectedName}",
                            Parent = setFolder,
                            IsChecked = false
                        });
                    }
                    gsfNode.Children.Add(setFolder);
                }

                rootNode.Children.Add(gsfNode);
            }

            // Show status if partial
            if (!string.IsNullOrEmpty(data.StatusMessage) &&
                (data.Skeletons.Count == 0 && data.Animations.Count == 0 && data.CharacterInfo == null))
            {
                rootNode.Children.Add(new FolderNode
                {
                    Name = $"? {data.StatusMessage}",
                    Parent = rootNode,
                    IsChecked = false
                });
            }

            rootNode.UpdateCheckStateFromChildren();
            return rootNode;
        }
    }
}
