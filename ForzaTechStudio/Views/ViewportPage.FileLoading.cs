using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
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
using Windows.Storage.Pickers;
using SDX = SharpDX;
using Color = Windows.UI.Color;

namespace ForzaTechStudio.Views
{
    // File Loading and Parsing Methods
    public sealed partial class ViewportPage : Page
    {
        private static readonly RecyclableMemoryStreamManager _msManager = new();

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
                var paths = items.Select(i => i.Path).ToList();
                await ProcessDroppedFilesAsync(paths);
            }
        }

        private async Task ProcessDroppedFilesAsync(List<string> paths)
        {
            if (paths.Count == 0) return;

            IsLoading = true;
            LoadingStatus = "Processing dropped files...";

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

            // XML files must be loaded synchronously on the UI thread
            var xmlPaths = paths.Where(p => Path.GetExtension(p).Equals(".xml", StringComparison.OrdinalIgnoreCase)).ToList();
            var avpinsPaths = paths.Where(p => Path.GetExtension(p).Equals(".avpins", StringComparison.OrdinalIgnoreCase)).ToList();
            var grannyPaths = paths.Where(p => { var ext = Path.GetExtension(p).ToLowerInvariant(); return ext == ".gr2" || ext == ".gsf"; }).ToList();
            var otherPaths = paths.Where(p => !xmlPaths.Contains(p) && !avpinsPaths.Contains(p) && !grannyPaths.Contains(p)).ToList();

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
                var xmlText = File.ReadAllText(avpinsPath, Encoding.UTF8);
                var node = LoadAvPins(Path.GetFileName(avpinsPath), xmlText, avpinsPath);
                if (node != null)
                    loadedNodes.Add((node, avpinsPath));
            }

            // Granny files are loaded on the UI thread (no native DLL threading issues)
            foreach (var grannyPath in grannyPaths)
            {
                LoadingStatus = $"Loading {Path.GetFileName(grannyPath)}...";
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
                LoadingStatus = $"Auto-loading {Path.GetFileName(compPath)}...";
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

            // Now that the tree is built with PropertyChanged handlers attached,
            // apply filters to set IsChecked which triggers rendering.
            foreach (var (node, _) in loadedNodes)
            {
                ApplyLODFilterRecursive(node, l0, l1, l2, l3, l4, l5, shadows);
                ApplyViewTypeFilterRecursive(node, showLights, showLocators, showPhysics);
            }

            if (wasEmpty && ViewModel.Roots.Any())
                AutoFitCamera();

            UpdateAnimationExpanderVisibility();

            IsLoading = false;
            LoadingStatus = "";
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
                var xmlText = File.ReadAllText(avpinsFile.Path, Encoding.UTF8);
                var node = LoadAvPins(avpinsFile.Name, xmlText, avpinsFile.Path);
                if (node != null)
                    loadedNodes.Add((node, avpinsFile.Name));
            }

            foreach (var grannyFile in grannyFiles)
            {
                LoadingStatus = $"Loading {grannyFile.Name}...";
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

            // Now that the tree is built with PropertyChanged handlers attached,
            // apply filters to set IsChecked which triggers rendering.
            foreach (var (node, _) in loadedNodes)
            {
                ApplyLODFilterRecursive(node, l0, l1, l2, l3, l4, l5, shadows);
                ApplyViewTypeFilterRecursive(node, showLights, showLocators, showPhysics);
            }

            if (wasEmpty && ViewModel.Roots.Any())
                AutoFitCamera();

            UpdateAnimationExpanderVisibility();

            IsLoading = false;
            LoadingStatus = "";
        }
        
        private LightsBinNode LoadLightsBin(string name, byte[] data)
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

        private ZipNode LoadZip(string path)
        {
             var zipNode = new ZipNode { Name = Path.GetFileName(path), FilePath = path, IsChecked = true };
             
             try
             {
                 using (var zip = new CustomZipFile(path))
                 {
                     var entries = zip.GetEntries().Where(e => !e.IsDirectory && 
                         (e.Name.EndsWith(".modelbin", StringComparison.OrdinalIgnoreCase) ||
                          e.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
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
                                     if (IsLightsBinFile(bytes))
                                     {
                                         node = LoadLightsBin(fileName, bytes);
                                     }
                                     else if (fileName.Equals("physicsdefinition.bin", StringComparison.OrdinalIgnoreCase))
                                     {
                                         node = LoadPhysicsDefinition(fileName, bytes);
                                     }
                                     else if (fileName.Contains("lights", StringComparison.OrdinalIgnoreCase))
                                     {
                                         node = LoadLightsBin(fileName, bytes);
                                     }
                                 }
                                 else if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                                 {
                                     // Save to temp file to use existing XML parser
                                     string tempPath = Path.GetTempFileName();
                                     File.WriteAllBytes(tempPath, bytes);
                                     node = LoadLocatorsXml(tempPath);
                                     if (node != null)
                                     {
                                         node.Name = fileName;
                                         if (node is LocatorsXmlNode locNode)
                                         {
                                         locNode.FilePath = null!; // Don't save back to temp file
                                         }
                                     }
                                     File.Delete(tempPath);
                                 }
                             }
                             else if (fileName.EndsWith(".avpins", StringComparison.OrdinalIgnoreCase))
                             {
                                 var bytes = zip.ExtractToMemory(entry);
                                 var xmlText = Encoding.UTF8.GetString(bytes);
                                 node = LoadAvPins(fileName, xmlText, null!, path, entry.Name);
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
                         catch { }
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
                         currentParent.UpdateCheckStateFromChildren();
                     }
                 }
                 return zipNode;
             }
             catch
             {
                 return null;
             }
        }

        // Loads a Playground MiniZip (.minizip / PGZP) file, extracting all ModelBin entries
        // and any other supported content. Returns a <see cref="ZipNode"/> whose children
        // mirror the flat index-addressed structure of the archive.
        private ZipNode LoadMiniZip(string path)
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

        private ModelBinNode LoadModelBin(string name, Stream stream)
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
                
                var binNode = new ModelBinNode { Name = name, Bundle = bundle, IsChecked = true };

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

        private PhysicsDefinitionNode LoadPhysicsDefinition(string name, byte[] data)
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

                        ModelBinNode newTempNode;
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
                                ViewModel.SelectedNode = null!;
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

        private LocatorsXmlNode LoadLocatorsXml(string filePath)
        {
            try
            {
                var parser = new LocatorsXmlParser();
                var data = parser.Parse(filePath);

                if (data.Locators.Count == 0)
                {
                    // Surface a visible error so the user knows why nothing appeared
                    _ = ShowError($"No <Locator> entries found in:\n{System.IO.Path.GetFileName(filePath)}\n\nMake sure the root element contains <Locator> children with a <Name value=\"...\"/> element.");
                    return null;
                }

                var rootNode = new LocatorsXmlNode
                {
                    Name = System.IO.Path.GetFileName(filePath),
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
                _ = ShowError($"Failed to open locators XML:\n{System.IO.Path.GetFileName(filePath)}\n\n{ex.Message}");
                return null;
            }
        }

        private AvPinsFileNode LoadAvPins(
            string name,
            string xmlText,
            string filePath,
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

        private GrannyFileNode LoadGrannyFileFromBytes(string fileName, byte[] bytes, string? sourceZipPath = null)
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

        private GrannyFileNode LoadGrannyFile(string filePath)
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
