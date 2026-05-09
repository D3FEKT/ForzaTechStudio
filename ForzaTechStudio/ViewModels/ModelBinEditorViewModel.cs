using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTechStudio.Services;
using ForzaTechStudio.Views;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using ForzaTechStudio.Converters;
using Microsoft.UI.Xaml.Controls;
using System.Numerics;
using System.Windows.Input;

namespace ForzaTechStudio.ViewModels
{
    public partial class ModelBinEditorViewModel : ObservableObject
    {
        private readonly FileService _fileService;

        public ObservableCollection<ObjectNode> RootNodes { get; } = new();
        public ObservableCollection<FileViewModel> LoadedFiles { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedFileTitle))]
        private FileViewModel _selectedFile;

        public string SelectedFileTitle => SelectedFile?.FileName ?? "No File Selected";

        // Simple View Collections
        public ObservableCollection<SimpleMaterialViewModel> SimpleMaterials { get; } = new();
        public ObservableCollection<SimpleMeshViewModel> SimpleMeshes { get; } = new();
        public ObservableCollection<SimpleSkeletonViewModel> SimpleSkeletons { get; } = new();
        public ObservableCollection<VLayBlobViewModel> SimpleVertexLayouts { get; } = new();

        [ObservableProperty]
        private ObjectNode? _selectedNode;

        [ObservableProperty]
        private VLayBlobViewModel? _selectedVLayWrapper;

        [ObservableProperty]
        private bool _isVLayNodeSelected;

        partial void OnSelectedNodeChanged(ObjectNode? value)
        {
            IsVLayNodeSelected = value?.Data is VertexLayoutBlob;
            SelectedVLayWrapper = IsVLayNodeSelected
                ? new VLayBlobViewModel((VertexLayoutBlob)value!.Data, value.OwnerBundle, value.Title)
                : null;
        }

        private int _selectedViewIndex = 0;
        public int SelectedViewIndex
        {
            get => _selectedViewIndex;
            set
            {
                if (SetProperty(ref _selectedViewIndex, value))
                {
                    OnPropertyChanged(nameof(IsSimpleViewVisible));
                    OnPropertyChanged(nameof(IsAdvancedViewVisible));
                }
            }
        }

        public bool IsSimpleViewVisible => SelectedViewIndex == 0;
        public bool IsAdvancedViewVisible => SelectedViewIndex == 1;

        public bool IsContentVisible => LoadedFiles.Count > 0;
        public bool IsContentEmpty => LoadedFiles.Count == 0;

        public ModelBinEditorViewModel(FileService fileService)
        {
            _fileService = fileService;
            LoadedFiles.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(IsContentVisible));
                OnPropertyChanged(nameof(IsContentEmpty));
            };
        }

        [RelayCommand]
        public async Task OpenFilesAsync()
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".modelbin");

            var files = await picker.PickMultipleFilesAsync();
            if (files != null)
            {
                foreach (var file in files)
                {
                    await LoadFileAsync(file.Name, file.Path);
                }
            }
        }

        public async Task LoadFileAsync(string name, string path)
        {
            foreach (var node in RootNodes)
            {
                if (node.Title == name) return;
            }

            var fileVm = new FileViewModel(name, path, _fileService);
            await fileVm.EnsureLoadedAsync();
            LoadedFiles.Add(fileVm);

            // Populate tree items
            var fileNode = new ObjectNode(name, null, true);
            foreach (var node in fileVm.Nodes)
            {
                fileNode.Children.Add(node);
            }
            RootNodes.Add(fileNode);
            fileNode.IsExpanded = true;
            
            // Default to the last loaded file for simple view
            SelectedFile = fileVm;
        }

        partial void OnSelectedFileChanged(FileViewModel value)
        {
            RefreshSimpleView();
        }

        private void RefreshSimpleView()
        {
            SimpleMaterials.Clear();
            SimpleMeshes.Clear();
            SimpleSkeletons.Clear();
            SimpleVertexLayouts.Clear();
            
            if (SelectedFile?.ParsedObject is Bundle bundle)
            {
                PopulateSimpleView(bundle);
            }
        }

        private void PopulateSimpleView(Bundle bundle)
        {
            int vLayIdx = 0;
            foreach (var blob in bundle.Blobs)
            {
                if (blob is MaterialBlob matBlob)
                {
                    SimpleMaterials.Add(new SimpleMaterialViewModel(matBlob, this));
                }
                else if (blob is SkeletonBlob skelBlob)
                {
                    SimpleSkeletons.Add(new SimpleSkeletonViewModel(skelBlob));
                }
                else if (blob is MeshBlob meshBlob)
                {
                    SimpleMeshes.Add(new SimpleMeshViewModel(meshBlob));
                }
                else if (blob is VertexLayoutBlob vlayBlob)
                {
                    SimpleVertexLayouts.Add(new VLayBlobViewModel(vlayBlob, bundle, $"VLay {vLayIdx++}"));
                }
            }
        }

        [RelayCommand]
        public async Task SaveFileAsync()
        {
            var (bundleToSave, filePath) = ResolveSelectedBundle();

            if (bundleToSave == null)
            {
                App.ShowErrorDialog("No Modelbin file loaded to save.");
                return;
            }

            if (string.IsNullOrEmpty(filePath))
            {
                App.ShowErrorDialog("Cannot determine the original file path. Use Save As instead.");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write);
                    bundleToSave.Serialize(stream);
                });

                App.ShowInfoDialog($"File saved successfully to:\n{filePath}", "Save Complete");
            }
            catch (Exception ex)
            {
                App.ShowErrorDialog($"Save Error: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task SaveFileAsAsync()
        {
            var (bundleToSave, fileName) = ResolveSelectedBundle();

            if (bundleToSave == null)
            {
                App.ShowErrorDialog("No Modelbin file loaded to save.");
                return;
            }

            var picker = new FileSavePicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeChoices.Add("Forza Modelbin", new List<string>() { ".modelbin" });
            picker.SuggestedFileName = System.IO.Path.GetFileName(fileName) ?? "modified.modelbin";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    using var stream = await file.OpenStreamForWriteAsync();
                    stream.SetLength(0);
                    bundleToSave.Serialize(stream);

                    App.ShowInfoDialog($"File saved successfully to:\n{file.Path}", "Save Complete");
                }
                catch (Exception ex)
                {
                    App.ShowErrorDialog($"Save Error: {ex.Message}");
                }
            }
        }


        private (Bundle bundle, string filePath) ResolveSelectedBundle()
        {
            //  selected node
            if (SelectedNode != null)
            {
                var current = SelectedNode;
                while (current != null)
                {
                    if (current.Data is Bundle bundle)
                    {
                        var match = LoadedFiles.FirstOrDefault(f => f.ParsedObject == bundle);
                        return (bundle, match?.FilePath);
                    }
                    ObjectNode parent = null;
                    foreach (var root in RootNodes)
                    {
                        if (FindParent(root, current, out parent))
                            break;
                    }
                    current = parent;
                }
            }

            // fall back to the currently selected file
            if (SelectedFile?.ParsedObject is Bundle selectedBundle)
                return (selectedBundle, SelectedFile.FilePath);

            // first loaded file
            if (LoadedFiles.Count > 0 && LoadedFiles[0].ParsedObject is Bundle firstBundle)
                return (firstBundle, LoadedFiles[0].FilePath);

            return (null, null);
        }

        [RelayCommand]
        public async Task SaveMaterialToJsonAsync(object parameter)
        {
            MaterialBlob materialBlob = null;
            if (parameter is ObjectNode node && node.Data is MaterialBlob mb)
                materialBlob = mb;
            else if (parameter is MaterialBlob mb2)
                materialBlob = mb2;

            if (materialBlob == null) return;

            var picker = new FileSavePicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            string materialName = MaterialExtractionService.GetMaterialName(materialBlob);
            if (string.IsNullOrEmpty(materialName))
                materialName = "unnamed_material";

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("JSON File", new List<string>() { ".json" });
            picker.SuggestedFileName = $"{materialName}.json";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    var materialEntry = new Dictionary<string, MaterialEntry>();
                    
                    byte[] blobData = materialBlob.GetContents();
                    string blobHex = BitConverter.ToString(blobData).Replace("-", " ");
                    string metadataHex = CreateFormattedMetadataHex(materialName);

                    materialEntry[materialName] = new MaterialEntry
                    {
                        MaterialMetaData = metadataHex,
                        MaterialBlob = blobHex
                    };

                    var jsonOptions = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        TypeInfoResolver = MaterialJsonContext.Default
                    };

                    string jsonString = JsonSerializer.Serialize(materialEntry, typeof(Dictionary<string, MaterialEntry>), jsonOptions);
                    await File.WriteAllTextAsync(file.Path, jsonString);

                    App.ShowInfoDialog($"Material '{materialName}' saved successfully!", "Save Complete");
                }
                catch (Exception ex)
                {
                    App.ShowErrorDialog($"Failed to save material: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        public async Task ReplaceMaterialFromJsonAsync(object parameter)
        {
            MaterialBlob materialBlob = null;
            if (parameter is ObjectNode node && node.Data is MaterialBlob mb)
                materialBlob = mb;
            else if (parameter is MaterialBlob mb2)
                materialBlob = mb2;

            if (materialBlob == null) return;
            
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

                    string selectedMaterial = null;
                    
                    if (materials.Count == 1)
                    {
                        selectedMaterial = materials.Keys.First();
                    }
                    else
                    {
                        selectedMaterial = await ShowMaterialSelectorDialogAsync(materials.Keys);
                    }

                    if (selectedMaterial != null && materials.TryGetValue(selectedMaterial, out var entry))
                    {
                        if (string.IsNullOrEmpty(entry.MaterialBlob))
                        {
                            App.ShowErrorDialog($"Material blob data is missing for '{selectedMaterial}'.");
                            return;
                        }

                        byte[] blobData = HexStringToByteArray(entry.MaterialBlob);
                        
                        materialBlob.CustomBlobData = blobData;
                        materialBlob.UncompressedSize = (uint)blobData.Length;
                        materialBlob.CompressedSize = (uint)blobData.Length;

                        string newName = selectedMaterial;

                        var nameMeta = materialBlob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
                        if (nameMeta != null)
                        {
                            nameMeta.Name = newName;
                        }
                        else
                        {
                            materialBlob.Metadatas.Add(new NameMetadata 
                            { 
                                Tag = BundleMetadata.TAG_METADATA_Name, 
                                Name = newName 
                            });
                        }

                        if (blobData.Length > 0)
                        {
                            try
                            {
                                using var ms = new MemoryStream(blobData);
                                if (blobData.Length >= 4)
                                {
                                    uint magic = BitConverter.ToUInt32(blobData, 0);
                                    if (magic == Bundle.BundleTag)
                                    {
                                        var newBundle = new Bundle();
                                        newBundle.Load(ms);
                                        materialBlob.Bundle = newBundle;
                                        materialBlob.CustomBlobData = null;
                                    }
                                }
                            }
                            catch (Exception innerEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Warning: Could not parse inner bundle: {innerEx.Message}");
                                // Keep CustomBlobData as fallback since Bundle parsing failed
                            }
                        }

                        // Refresh UI if tree node
                        if (parameter is ObjectNode nodeObj)
                        {
                            await RefreshNodeAsync(nodeObj);
                        }
                        
                        // Also refresh simple view model
                        var simple = SimpleMaterials.FirstOrDefault(m => m.Blob == materialBlob);
                        simple?.Refresh();

                        App.ShowInfoDialog($"Material replaced with '{selectedMaterial}' successfully!\n\nRemember to save the file to persist changes.", "Replace Complete");
                    }
                    else
                    {
                        App.ShowErrorDialog("Selected material not found in the file.");
                    }
                }
                catch (Exception ex)
                {
                    App.ShowErrorDialog($"Failed to replace material: {ex.Message}");
                }
            }
        }

        public class MaterialLibraryDisplayItem
        {
            public string Name { get; set; }
            public string DisplayText { get; set; }
        }

        private string GetPreferredMaterialLibraryGameId()
        {
            if (!string.IsNullOrWhiteSpace(SelectedFile?.FilePath))
            {
                try
                {
                    var analysis = new ConversionService().AnalyzeFile(SelectedFile.FilePath);
                    foreach (string gameId in ForzaGameCatalog.GetMatchingGameIds(analysis.DetectedGame))
                    {
                        if (ForzaGameCatalog.TryGetGame(gameId, out _))
                            return gameId;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to detect material library game: {ex.Message}");
                }
            }

            return ForzaGameCatalog.GetPreferredMaterialLibraryGameId();
        }

        [RelayCommand]
        public async Task BrowseMaterialLibraryAsync(object parameter)
        {
            MaterialBlob materialBlob = null;
            if (parameter is ObjectNode node && node.Data is MaterialBlob mb)
                materialBlob = mb;
            else if (parameter is MaterialBlob mb2)
                materialBlob = mb2;

            if (materialBlob == null) return;

            try
            {
                var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    XamlRoot = App.MainWindow.Content.XamlRoot,
                    Title = "Browse Material Library",
                    PrimaryButtonText = "Select",
                    CloseButtonText = "Cancel",
                    DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
                    IsPrimaryButtonEnabled = false
                };

                var selectedGameComboBox = new ComboBox
                {
                    Header = "Game Library",
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                    DisplayMemberPath = nameof(ForzaGameDefinition.DisplayName),
                    SelectedValuePath = nameof(ForzaGameDefinition.GameId),
                    ItemsSource = ForzaGameCatalog.MaterialLibraryGames.ToList(),
                };

                var libraryInfoText = new TextBlock
                {
                    FontSize = 11,
                    Opacity = 0.7,
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                };

                var listBox = new Microsoft.UI.Xaml.Controls.ListBox();
                listBox.SelectionMode = Microsoft.UI.Xaml.Controls.SelectionMode.Single;
                listBox.DisplayMemberPath = "DisplayText";

                var searchBox = new AutoSuggestBox
                {
                    PlaceholderText = "Search materials...",
                    Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 12)
                };

                var displayItems = new List<MaterialLibraryDisplayItem>();
                var currentLibrary = new Dictionary<string, MaterialEntry>(StringComparer.OrdinalIgnoreCase);

                void ApplyFilter()
                {
                    string query = searchBox.Text?.Trim() ?? string.Empty;
                    var filteredItems = string.IsNullOrWhiteSpace(query)
                        ? displayItems
                        : displayItems
                            .Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                    listBox.ItemsSource = filteredItems;
                    dialog.IsPrimaryButtonEnabled = filteredItems.Count > 0;
                    if (filteredItems.Count > 0)
                        listBox.SelectedIndex = 0;
                }

                void LoadLibraryForSelectedGame()
                {
                    string selectedGameId = selectedGameComboBox.SelectedValue as string ?? GetPreferredMaterialLibraryGameId();
                    currentLibrary = MaterialLibrary.LoadEntries(selectedGameId, out string resolvedPath, includeLegacyFallback: true);

                    bool isFallback = !string.Equals(
                        resolvedPath,
                        MaterialLibrary.GetLibraryPath(selectedGameId),
                        StringComparison.OrdinalIgnoreCase);

                    libraryInfoText.Text = currentLibrary.Count == 0
                        ? $"No saved materials found for {ForzaGameCatalog.GetDisplayName(selectedGameId)}."
                        : isFallback
                            ? $"Using {Path.GetFileName(resolvedPath)} fallback for {ForzaGameCatalog.GetDisplayName(selectedGameId)}."
                            : $"Using {Path.GetFileName(resolvedPath)} for {ForzaGameCatalog.GetDisplayName(selectedGameId)}.";

                    displayItems = currentLibrary
                        .Select(kvp => new MaterialLibraryDisplayItem
                        {
                            Name = kvp.Key,
                            DisplayText = $"{kvp.Key} ({kvp.Value.MaterialBlob.Length / 3} bytes)"
                        })
                        .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    ApplyFilter();
                }

                searchBox.TextChanged += (s, e) =>
                {
                    if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                        ApplyFilter();
                };

                selectedGameComboBox.SelectionChanged += (_, _) => LoadLibraryForSelectedGame();

                var stackPanel = new StackPanel { Spacing = 8 };
                stackPanel.Children.Add(selectedGameComboBox);
                stackPanel.Children.Add(libraryInfoText);
                stackPanel.Children.Add(searchBox);
                stackPanel.Children.Add(new ScrollViewer { Content = listBox, MaxHeight = 400 });

                dialog.Content = stackPanel;

                selectedGameComboBox.SelectedValue = GetPreferredMaterialLibraryGameId();
                LoadLibraryForSelectedGame();

                var result = await dialog.ShowAsync();
                
                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary && listBox.SelectedItem is MaterialLibraryDisplayItem selectedItem)
                {
                    string selectedMaterial = selectedItem.Name;

                    var entry = currentLibrary[selectedMaterial];
                    if (string.IsNullOrEmpty(entry.MaterialBlob))
                    {
                        App.ShowErrorDialog($"Material blob data is missing for '{selectedMaterial}'.");
                        return;
                    }

                    byte[] blobData = HexStringToByteArray(entry.MaterialBlob);
                    
                    materialBlob.CustomBlobData = blobData;
                    materialBlob.UncompressedSize = (uint)blobData.Length;
                    materialBlob.CompressedSize = (uint)blobData.Length;

                    string newName = selectedMaterial;

                    var nameMeta = materialBlob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
                    if (nameMeta != null)
                    {
                        nameMeta.Name = newName;
                    }
                    else
                    {
                        materialBlob.Metadatas.Add(new NameMetadata 
                        { 
                            Tag = BundleMetadata.TAG_METADATA_Name, 
                            Name = newName 
                        });
                    }

                    if (blobData.Length > 0)
                    {
                        try
                        {
                            using var ms = new MemoryStream(blobData);
                            if (blobData.Length >= 4)
                            {
                                uint magic = BitConverter.ToUInt32(blobData, 0);
                                if (magic == Bundle.BundleTag)
                                {
                                    var newBundle = new Bundle();
                                    newBundle.Load(ms);
                                    materialBlob.Bundle = newBundle;
                                    materialBlob.CustomBlobData = null;
                                }
                            }
                        }
                        catch (Exception innerEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Warning: Could not parse inner bundle: {innerEx.Message}");
                        }
                    }

                    // Refresh UI if tree node
                    if (parameter is ObjectNode nodeObj)
                    {
                        await RefreshNodeAsync(nodeObj);
                    }
                    
                    // Also refresh simple view model
                    var simple = SimpleMaterials.FirstOrDefault(m => m.Blob == materialBlob);
                    simple?.Refresh();

                    App.ShowInfoDialog($"Material replaced with '{selectedMaterial}' from library successfully!\n\nRemember to save the file to persist changes.", "Replace Complete");
                }
            }
            catch (Exception ex)
            {
                App.ShowErrorDialog($"Failed to browse or replace material from library: {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task RemoveShaderParameterSimpleAsync(object parameter)
        {
            if (parameter is Tuple<MaterialShaderParameterBlob, ShaderParameter> tuple)
            {
                var blob = tuple.Item1;
                var param = tuple.Item2;
                blob.Parameters.Remove(param);
                
                // Trigger refresh if needed
            }
        }

        [RelayCommand]
        public async Task AddShaderParameterAsync(object parameter)
        {
            MaterialShaderParameterBlob blob = null;
            ObjectNode refreshNode = null;

            if (parameter is ObjectNode node && node.Data is MaterialShaderParameterBlob nodeBlob)
            {
                blob = nodeBlob;
                refreshNode = node;
            }
            else if (parameter is ObjectNode matNode && matNode.Data is MaterialBlob mbWrapped)
            {
                if (mbWrapped.Bundle != null)
                    blob = mbWrapped.Bundle.Blobs.OfType<MaterialShaderParameterBlob>().FirstOrDefault();
                refreshNode = matNode;
            }
            else if (parameter is MaterialBlob matBlob)
            {
                if (matBlob.Bundle != null)
                {
                    blob = matBlob.Bundle.Blobs.OfType<MaterialShaderParameterBlob>().FirstOrDefault();
                }
            }

            if (blob == null) return;

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Add Shader Parameter",
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var stackPanel = new StackPanel { Spacing = 12 };

            var nameBox = new AutoSuggestBox
            {
                Header = "Parameter Name (or Hash)",
                PlaceholderText = "Type to search known names...",
                ItemsSource = NameHashService.Instance.GetAll().Values.OrderBy(x => x).ToList()
            };
            
            nameBox.TextChanged += (s, e) => 
            {
                if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    var query = s.Text.ToLower();
                    s.ItemsSource = NameHashService.Instance.GetAll().Values
                        .Where(x => x.ToLower().Contains(query))
                        .OrderBy(x => x)
                        .ToList();
                }
            };
            
            var typeBox = new ComboBox
            {
                Header = "Parameter Type",
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                ItemsSource = Enum.GetValues<ShaderParameterType>()
            };
            typeBox.SelectedIndex = 0;

            stackPanel.Children.Add(nameBox);
            stackPanel.Children.Add(typeBox);
            dialog.Content = stackPanel;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                uint hash = 0;
                string inputName = nameBox.Text;
                
                var knownHash = NameHashService.Instance.GetHash(inputName);
                if (knownHash.HasValue)
                {
                    hash = knownHash.Value;
                }
                else
                {
                    string hexText = inputName.StartsWith("0x") ? inputName.Substring(2) : inputName;
                    if (!uint.TryParse(hexText, System.Globalization.NumberStyles.HexNumber, null, out hash))
                    {
                         App.ShowErrorDialog($"Invalid hash or unknown name: {inputName}");
                         return;
                    }
                }

                var type = (ShaderParameterType)typeBox.SelectedItem;
                
                var newParam = new ShaderParameter
                {
                    VersionMajor = 2,
                    VersionMinor = 0,
                    NameHash = hash,
                    Type = type,
                    Value = GetDefaultValueForType(type)
                };

                blob.Parameters.Add(newParam);
                
                if (refreshNode != null)
                {
                    refreshNode.MaterialBlobWrapper?.Refresh();
                    _ = RefreshNodeAsync(refreshNode);
                }
                
                // If it came from a MaterialBlob (simple view), we might want to refresh the SimpleView
                if (parameter is MaterialBlob mat)
                {
                    var simple = SimpleMaterials.FirstOrDefault(m => m.Blob == mat);
                    simple?.Refresh();
                }
            }
        }

        [RelayCommand]
        public async Task RemoveShaderParameterAsync(object parameter)
        {
             if (SelectedNode?.Data is MaterialShaderParameterBlob blob && parameter is ShaderParameter param)
             {
                 blob.Parameters.Remove(param);
                 _ = RefreshNodeAsync(SelectedNode);
             }
        }

        [RelayCommand]
        public async Task ViewKnownParametersAsync()
        {
            var listBox = new ListBox();
            
            var allParams = NameHashService.Instance.GetAll()
                                .OrderBy(kv => kv.Value)
                                .Select(kv => $"{kv.Value} (0x{kv.Key:X8})")
                                .ToList();

            foreach (var p in allParams)
            {
                listBox.Items.Add(p);
            }

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Known Parameters Reference",
                Content = new ScrollViewer { Content = listBox, Height = 400 },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close
            };

            await dialog.ShowAsync();
        }

        [RelayCommand]
        public void CloseAllFiles()
        {
            RootNodes.Clear();
            LoadedFiles.Clear();
            SelectedNode = null;
            SelectedFile = null;
        }

        [RelayCommand]
        public void CloseSimpleFile(FileViewModel file)
        {
             if (file == null) return;
             
             file.Unload();
             LoadedFiles.Remove(file);

             // Also remove from Tree Nodes
             var node = RootNodes.FirstOrDefault(n => n.Title == file.FileName);
             if (node != null) RootNodes.Remove(node);
             
             if (SelectedFile == file)
             {
                 SelectedFile = LoadedFiles.FirstOrDefault();
             }
        }

        [RelayCommand]
        public async Task EditShaderParameterAsync(object parameter)
        {
            if (parameter is not ShaderParameter param) return;

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Edit Parameter Value",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var stackPanel = new StackPanel { Spacing = 12 };
            
            stackPanel.Children.Add(new TextBlock { Text = $"Type: {param.Type}", Opacity = 0.6 });

            TextBox valueBox = new TextBox();
            
            if (param.Value is Vector4 v4)
                valueBox.Text = $"{v4.X}, {v4.Y}, {v4.Z}, {v4.W}";
            else if (param.Value is Vector2 v2)
                valueBox.Text = $"{v2.X}, {v2.Y}";
            else
                valueBox.Text = param.Value?.ToString() ?? "";

            stackPanel.Children.Add(valueBox);
            
            if (param.Type == ShaderParameterType.Vector || param.Type == ShaderParameterType.Color || param.Type == ShaderParameterType.Vector2)
            {
                stackPanel.Children.Add(new TextBlock { Text = "Format: X, Y, Z, W  (comma separated)", FontSize = 12, Opacity = 0.5 });
            }

            dialog.Content = stackPanel;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    string text = valueBox.Text;
                    object newValue = null;

                    switch (param.Type)
                    {
                         case ShaderParameterType.Vector:
                         case ShaderParameterType.Color:
                         case ShaderParameterType.Swizzle:
                         case ShaderParameterType.FunctionRange:
                            var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => float.Parse(s.Trim())).ToArray();
                            if (parts.Length >= 4) newValue = new Vector4(parts[0], parts[1], parts[2], parts[3]);
                            else if (parts.Length == 3) newValue = new Vector4(parts[0], parts[1], parts[2], 1.0f);
                            else throw new FormatException("Vector4 requires 3 or 4 values");
                            break;
                         case ShaderParameterType.Vector2:
                            var parts2 = text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => float.Parse(s.Trim())).ToArray();
                            if (parts2.Length >= 2) newValue = new Vector2(parts2[0], parts2[1]);
                            else throw new FormatException("Vector2 requires 2 values");
                            break;
                        case ShaderParameterType.Float:
                            newValue = float.Parse(text);
                            break;
                        case ShaderParameterType.Int:
                            newValue = int.Parse(text);
                            break;
                        case ShaderParameterType.Bool:
                            newValue = bool.Parse(text);
                            break;
                         case ShaderParameterType.Texture2D:
                             if (param.Value is TextureParameter tp) tp.Path = text;
                             else newValue = new TextureParameter { Path = text };
                             break;
                    }

                    if (newValue != null)
                        param.Value = newValue;

                    if (SelectedNode != null)
                        _ = RefreshNodeAsync(SelectedNode);
                }
                catch (Exception ex)
                {
                    App.ShowErrorDialog($"Invalid format: {ex.Message}");
                }
            }
        }

        private object GetDefaultValueForType(ShaderParameterType type)
        {
            return type switch
            {
                ShaderParameterType.Vector => new Vector4(0, 0, 0, 0),
                ShaderParameterType.Color => new Vector4(1, 1, 1, 1),
                ShaderParameterType.Float => 0.0f,
                ShaderParameterType.Bool => false,
                ShaderParameterType.Int => 0,
                ShaderParameterType.Texture2D => new TextureParameter { Path = "" },
                ShaderParameterType.Sampler => new SamplerParameter(),
                ShaderParameterType.Vector2 => new Vector2(0, 0),
                _ => null
            };
        }

        private async Task<string> ShowMaterialSelectorDialogAsync(IEnumerable<string> materialNames)
        {
            var listBox = new Microsoft.UI.Xaml.Controls.ListBox();
            listBox.SelectionMode = Microsoft.UI.Xaml.Controls.SelectionMode.Single;
            
            foreach (var name in materialNames)
            {
                listBox.Items.Add(name);
            }
            
            if (listBox.Items.Count > 0)
            {
                listBox.SelectedIndex = 0;
            }

            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Select Material",
                Content = listBox,
                PrimaryButtonText = "Select",
                CloseButtonText = "Cancel",
                DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            
            if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary && listBox.SelectedItem != null)
            {
                return listBox.SelectedItem as string;
            }
            
            return null;
        }

        private bool FindParent(ObjectNode root, ObjectNode target, out ObjectNode parent)
        {
            parent = null;
            foreach (var child in root.Children)
            {
                if (child == target)
                {
                    parent = root;
                    return true;
                }
                if (FindParent(child, target, out parent))
                    return true;
            }
            return false;
        }

        private async Task RefreshNodeAsync(ObjectNode node)
        {
            if (node == null) return;
            node.Refresh();
            await Task.Delay(50);
        }

        private string CreateFormattedMetadataHex(string materialName)
        {
            byte[] nameTag = new byte[] { 0x65, 0x6D, 0x61, 0x4E };
            byte[] idTag = new byte[] { 0x20, 0x20, 0x64, 0x49 };
            byte[] nameBytes = Encoding.UTF8.GetBytes(materialName);
            byte stringLengthByte = (byte)(nameBytes.Length);
            byte sizeByteWithOffset = (byte)(nameBytes.Length + 8);

            byte firstByte, secondByte;
            CalculateSpecialSizeFormat(stringLengthByte, out firstByte, out secondByte);

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(nameTag, 0, nameTag.Length);
                ms.WriteByte(firstByte);
                ms.WriteByte(secondByte);
                ms.WriteByte(0x10);
                ms.WriteByte(0x00);

                ms.Write(idTag, 0, idTag.Length);
                ms.WriteByte(0x40);
                ms.WriteByte(0x00);

                ms.WriteByte(sizeByteWithOffset);
                ms.WriteByte(0x00);

                ms.Write(nameBytes, 0, nameBytes.Length);

                ms.WriteByte(0x00);
                ms.WriteByte(0x00);
                ms.WriteByte(0x00);
                ms.WriteByte(0x00);

                return BitConverter.ToString(ms.ToArray()).Replace("-", " ");
            }
        }

        private void CalculateSpecialSizeFormat(byte size, out byte firstByte, out byte secondByte)
        {
            byte highNibble = (byte)((size & 0xF0) >> 4);
            byte lowNibble = (byte)(size & 0x0F);
            firstByte = (byte)(lowNibble << 4);
            secondByte = highNibble;
        }

        private byte[] HexStringToByteArray(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            int length = hex.Length;
            byte[] bytes = new byte[length / 2];
            for (int i = 0; i < length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }
    }

    public partial class SimpleMaterialViewModel : ObservableObject
    {
        public MaterialBlob Blob { get; }
        private readonly ModelBinEditorViewModel _parentVm;
        
        public uint MaterialId
        {
            get
            {
                var idMeta = Blob.Metadatas.OfType<IdentifierMetadata>().FirstOrDefault();
                return idMeta?.Id ?? Blob.Id;
            }
            set
            {
                var idMeta = Blob.Metadatas.OfType<IdentifierMetadata>().FirstOrDefault();
                if (idMeta != null)
                {
                    if (idMeta.Id != value)
                    {
                        idMeta.Id = value;
                        OnPropertyChanged(nameof(MaterialId));
                    }
                }
                else if (Blob.Id != value)
                {
                    Blob.Id = value;
                    OnPropertyChanged(nameof(MaterialId));
                }
            }
        }
        
        public string Name
        {
            get
            {
                var nameMeta = Blob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
                return nameMeta?.Name ?? "Unnamed Material";
            }
        }

        // New Property for Material Path
        public string MaterialPath
        {
            get
            {
                // Material path is stored in the nested MATI (MaterialResourceBlob)
                if (Blob.Bundle != null)
                {
                    var matiBlob = Blob.Bundle.Blobs.OfType<MaterialResourceBlob>().FirstOrDefault();
                    if (matiBlob != null)
                    {
                        return matiBlob.Path;
                    }
                }
                return string.Empty;
            }
            set
            {
                if (Blob.Bundle != null)
                {
                    var matiBlob = Blob.Bundle.Blobs.OfType<MaterialResourceBlob>().FirstOrDefault();
                    if (matiBlob != null && matiBlob.Path != value)
                    {
                        matiBlob.Path = value;
                        OnPropertyChanged(nameof(MaterialPath));
                    }
                }
            }
        }

        public MaterialShaderParameterBlob ParameterBlob { get; private set; }
        public ObservableCollection<ShaderParameter> Parameters { get; } = new();

        public ICommand SaveJsonCommand => _parentVm?.SaveMaterialToJsonCommand;
        public ICommand ReplaceJsonCommand => _parentVm?.ReplaceMaterialFromJsonCommand;
        public ICommand BrowseLibraryCommand => _parentVm?.BrowseMaterialLibraryCommand;
        public ICommand AddParameterCommand => _parentVm?.AddShaderParameterCommand;

        public SimpleMaterialViewModel(MaterialBlob blob, ModelBinEditorViewModel parentVm)
        {
            Blob = blob;
            _parentVm = parentVm;
            LoadParameters();
        }

        private void LoadParameters()
        {
            Parameters.Clear();
            if (Blob.Bundle != null)
            {
                ParameterBlob = Blob.Bundle.Blobs.OfType<MaterialShaderParameterBlob>().FirstOrDefault();
                if (ParameterBlob != null)
                {
                    foreach (var param in ParameterBlob.Parameters)
                    {
                        Parameters.Add(param);
                    }
                }
            }
        }
        
        public void Refresh()
        {
             OnPropertyChanged(nameof(Name));
             OnPropertyChanged(nameof(MaterialPath));
             LoadParameters();
             OnPropertyChanged(nameof(Parameters));
        }

        [RelayCommand]
        public void RemoveParameter(ShaderParameter param)
        {
            if (ParameterBlob != null && param != null)
            {
                ParameterBlob.Parameters.Remove(param);
                Parameters.Remove(param);
            }
        }

        [RelayCommand]
        public void EditShaderParameters()
        {
            if (ParameterBlob != null)
            {
                var vm = new MaterialShaderParametersViewModel(ParameterBlob, Name, () => Refresh());
                var frame = (App.MainWindow.Content as Microsoft.UI.Xaml.Controls.Frame);
                if (frame != null)
                {
                    frame.Navigate(typeof(MaterialShaderParametersPage), vm);
                }
            }
        }
    }

    public partial class SimpleMeshViewModel : ObservableObject
    {
        public MeshBlob Blob { get; }
        
        public string Name
        {
             get
             {
                 var nameMeta = Blob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
                 return !string.IsNullOrEmpty(nameMeta?.Name) ? nameMeta.Name : $"Mesh {Blob.IndexBufferIndex}"; 
             }
        }

        // Properties for binding
        public short MaterialId
        {
            get
            {
                // For version 1.9+, MaterialIds array is used (4 shorts). 
                // The primary material ID is usually at index 1.
                if (Blob.MaterialIds != null && Blob.MaterialIds.Length > 1)
                {
                    return Blob.MaterialIds[1];
                }
                return Blob.MaterialId;
            }
            set
            {
                bool changed = false;
                if (Blob.MaterialIds != null && Blob.MaterialIds.Length > 1)
                {
                    if (Blob.MaterialIds[1] != value)
                    {
                        Blob.MaterialIds[1] = value;
                        // Keep legacy property in sync just in case
                        Blob.MaterialId = value;
                        changed = true;
                    }
                }
                else
                {
                    if (Blob.MaterialId != value)
                    {
                        Blob.MaterialId = value;
                        changed = true;
                    }
                }

                if (changed)
                {
                    OnPropertyChanged(nameof(MaterialId));
                }
            }
        }

        // Flags
        public bool IsOpaque
        {
            get => Blob.IsOpaque;
            set { if (Blob.IsOpaque != value) { Blob.IsOpaque = value; OnFlagChanged(); } }
        }
        public bool IsDecal
        {
            get => Blob.IsDecal;
            set { if (Blob.IsDecal != value) { Blob.IsDecal = value; OnFlagChanged(); } }
        }
        public bool IsTransparent
        {
            get => Blob.IsTransparent;
            set { if (Blob.IsTransparent != value) { Blob.IsTransparent = value; OnFlagChanged(); } }
        }
        public bool IsShadow
        {
            get => Blob.IsShadow;
            set { if (Blob.IsShadow != value) { Blob.IsShadow = value; OnFlagChanged(); } }
        }
        public bool IsNotShadow
        {
            get => Blob.IsNotShadow;
            set { if (Blob.IsNotShadow != value) { Blob.IsNotShadow = value; OnFlagChanged(); } }
        }
        public bool IsAlphaToCoverage
        {
            get => Blob.IsAlphaToCoverage;
            set { if (Blob.IsAlphaToCoverage != value) { Blob.IsAlphaToCoverage = value; OnFlagChanged(); } }
        }
        
        private void OnFlagChanged()
        {
            OnPropertyChanged(nameof(IsOpaque));
            OnPropertyChanged(nameof(IsDecal));
            OnPropertyChanged(nameof(IsTransparent));
            OnPropertyChanged(nameof(IsShadow));
            OnPropertyChanged(nameof(IsNotShadow));
            OnPropertyChanged(nameof(IsAlphaToCoverage));
        }

        // Transforms (Scale)
        public float ScaleX
        {
            get => Blob.PositionScale.X;
            set { if (Blob.PositionScale.X != value) { var v = Blob.PositionScale; v.X = value; Blob.PositionScale = v; OnPropertyChanged(); } }
        }
        public float ScaleY
        {
            get => Blob.PositionScale.Y;
            set { if (Blob.PositionScale.Y != value) { var v = Blob.PositionScale; v.Y = value; Blob.PositionScale = v; OnPropertyChanged(); } }
        }
        public float ScaleZ
        {
            get => Blob.PositionScale.Z;
            set { if (Blob.PositionScale.Z != value) { var v = Blob.PositionScale; v.Z = value; Blob.PositionScale = v; OnPropertyChanged(); } }
        }
        public float ScaleW
        {
            get => Blob.PositionScale.W;
            set { if (Blob.PositionScale.W != value) { var v = Blob.PositionScale; v.W = value; Blob.PositionScale = v; OnPropertyChanged(); } }
        }

        // Transforms (Position/Translate)
        public float TransX
        {
            get => Blob.PositionTranslate.X;
            set { if (Blob.PositionTranslate.X != value) { var v = Blob.PositionTranslate; v.X = value; Blob.PositionTranslate = v; OnPropertyChanged(); } }
        }
        public float TransY
        {
            get => Blob.PositionTranslate.Y;
            set { if (Blob.PositionTranslate.Y != value) { var v = Blob.PositionTranslate; v.Y = value; Blob.PositionTranslate = v; OnPropertyChanged(); } }
        }
        public float TransZ
        {
            get => Blob.PositionTranslate.Z;
            set { if (Blob.PositionTranslate.Z != value) { var v = Blob.PositionTranslate; v.Z = value; Blob.PositionTranslate = v; OnPropertyChanged(); } }
        }
        public float TransW
        {
            get => Blob.PositionTranslate.W;
            set { if (Blob.PositionTranslate.W != value) { var v = Blob.PositionTranslate; v.W = value; Blob.PositionTranslate = v; OnPropertyChanged(); } }
        }
        
        public ObservableCollection<TexCoordTransformViewModel> TexCoordTransforms { get; } = new();
        public bool HasTexCoordTransforms => TexCoordTransforms.Count > 0;

        public SimpleMeshViewModel(MeshBlob blob)
        {
            Blob = blob;
            if (blob.TexCoordTransforms != null)
            {
                for (int i = 0; i < blob.TexCoordTransforms.Length; i++)
                    TexCoordTransforms.Add(new TexCoordTransformViewModel(blob.TexCoordTransforms, i));
            }
        }
    }

    public class BoneViewModel : ObservableObject
    {
        private Bone _bone;

        public BoneViewModel(Bone bone)
        {
            _bone = bone;
            CopyMatrixCommand = new RelayCommand(CopyMatrix);
            PasteMatrixCommand = new RelayCommand(PasteMatrix);
            BoneMatrixClipboard.ClipboardChanged += OnClipboardChanged;
        }

        private void OnClipboardChanged(object sender, EventArgs e)
            => OnPropertyChanged(nameof(HasCopiedMatrix));

        // Updates the underlying bone reference without replacing this VM instance, preventing selection-change race conditions.
        public void SetBone(Bone bone)
        {
            _bone = bone;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(ParentId));
            OnPropertyChanged(nameof(FirstChildIndex));
            OnPropertyChanged(nameof(NextIndex));
            OnPropertyChanged(nameof(M11)); OnPropertyChanged(nameof(M12)); OnPropertyChanged(nameof(M13)); OnPropertyChanged(nameof(M14));
            OnPropertyChanged(nameof(M21)); OnPropertyChanged(nameof(M22)); OnPropertyChanged(nameof(M23)); OnPropertyChanged(nameof(M24));
            OnPropertyChanged(nameof(M31)); OnPropertyChanged(nameof(M32)); OnPropertyChanged(nameof(M33)); OnPropertyChanged(nameof(M34));
            OnPropertyChanged(nameof(M41)); OnPropertyChanged(nameof(M42)); OnPropertyChanged(nameof(M43)); OnPropertyChanged(nameof(M44));
        }

        public ICommand CopyMatrixCommand { get; }
        public ICommand PasteMatrixCommand { get; }

        public bool HasCopiedMatrix => BoneMatrixClipboard.HasData;

        private void CopyMatrix()
        {
            BoneMatrixClipboard.Set(_bone.Matrix);
        }

        private void PasteMatrix()
        {
            if (!BoneMatrixClipboard.HasData) return;
            var m = BoneMatrixClipboard.Matrix;
            var mat = _bone.Matrix;
            mat.M11 = m.M11; mat.M12 = m.M12; mat.M13 = m.M13; mat.M14 = m.M14;
            mat.M21 = m.M21; mat.M22 = m.M22; mat.M23 = m.M23; mat.M24 = m.M24;
            mat.M31 = m.M31; mat.M32 = m.M32; mat.M33 = m.M33; mat.M34 = m.M34;
            mat.M41 = m.M41; mat.M42 = m.M42; mat.M43 = m.M43; mat.M44 = m.M44;
            _bone.Matrix = mat;
            SetBone(_bone);
        }

        public string Name
        {
            get => _bone.Name;
            set
            {
                if (_bone.Name != value)
                {
                    _bone.Name = value;
                    OnPropertyChanged();
                }
            }
        }

        public short ParentId
        {
            get => _bone.ParentId;
            set
            {
                if (_bone.ParentId != value)
                {
                    _bone.ParentId = value;
                    OnPropertyChanged();
                }
            }
        }

        public short FirstChildIndex
        {
            get => _bone.FirstChildIndex;
            set
            {
                if (_bone.FirstChildIndex != value)
                {
                    _bone.FirstChildIndex = value;
                    OnPropertyChanged();
                }
            }
        }

        public short NextIndex
        {
            get => _bone.NextIndex;
            set
            {
                if (_bone.NextIndex != value)
                {
                    _bone.NextIndex = value;
                    OnPropertyChanged();
                }
            }
        }

        public float M11 { get => _bone.Matrix.M11; set { var m = _bone.Matrix; m.M11 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M12 { get => _bone.Matrix.M12; set { var m = _bone.Matrix; m.M12 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M13 { get => _bone.Matrix.M13; set { var m = _bone.Matrix; m.M13 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M14 { get => _bone.Matrix.M14; set { var m = _bone.Matrix; m.M14 = value; _bone.Matrix = m; OnPropertyChanged(); } }

        public float M21 { get => _bone.Matrix.M21; set { var m = _bone.Matrix; m.M21 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M22 { get => _bone.Matrix.M22; set { var m = _bone.Matrix; m.M22 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M23 { get => _bone.Matrix.M23; set { var m = _bone.Matrix; m.M23 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M24 { get => _bone.Matrix.M24; set { var m = _bone.Matrix; m.M24 = value; _bone.Matrix = m; OnPropertyChanged(); } }

        public float M31 { get => _bone.Matrix.M31; set { var m = _bone.Matrix; m.M31 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M32 { get => _bone.Matrix.M32; set { var m = _bone.Matrix; m.M32 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M33 { get => _bone.Matrix.M33; set { var m = _bone.Matrix; m.M33 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M34 { get => _bone.Matrix.M34; set { var m = _bone.Matrix; m.M34 = value; _bone.Matrix = m; OnPropertyChanged(); } }

        public float M41 { get => _bone.Matrix.M41; set { var m = _bone.Matrix; m.M41 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M42 { get => _bone.Matrix.M42; set { var m = _bone.Matrix; m.M42 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M43 { get => _bone.Matrix.M43; set { var m = _bone.Matrix; m.M43 = value; _bone.Matrix = m; OnPropertyChanged(); } }
        public float M44 { get => _bone.Matrix.M44; set { var m = _bone.Matrix; m.M44 = value; _bone.Matrix = m; OnPropertyChanged(); } }
    }

    public partial class SimpleSkeletonViewModel : ObservableObject
    {
        public SkeletonBlob Blob { get; }
        public ObservableCollection<Bone> Bones { get; } = new();

        [ObservableProperty]
        private Bone _selectedBone;

        [ObservableProperty]
        private BoneViewModel _selectedBoneVm;

        public bool HasCopiedMatrix => BoneMatrixClipboard.HasData;

        partial void OnSelectedBoneChanged(Bone value)
        {
            if (value == null)
                SelectedBoneVm = null;
            else if (SelectedBoneVm == null)
                SelectedBoneVm = new BoneViewModel(value);
            else
                SelectedBoneVm.SetBone(value);
        }

        [RelayCommand]
        public void CopyBoneMatrix()
        {
            if (SelectedBoneVm == null) return;
            BoneMatrixClipboard.Set(new Matrix4x4(
                SelectedBoneVm.M11, SelectedBoneVm.M12, SelectedBoneVm.M13, SelectedBoneVm.M14,
                SelectedBoneVm.M21, SelectedBoneVm.M22, SelectedBoneVm.M23, SelectedBoneVm.M24,
                SelectedBoneVm.M31, SelectedBoneVm.M32, SelectedBoneVm.M33, SelectedBoneVm.M34,
                SelectedBoneVm.M41, SelectedBoneVm.M42, SelectedBoneVm.M43, SelectedBoneVm.M44));
            OnPropertyChanged(nameof(HasCopiedMatrix));
        }

        [RelayCommand]
        public void PasteBoneMatrix()
        {
            if (SelectedBoneVm == null || !BoneMatrixClipboard.HasData) return;
            var m = BoneMatrixClipboard.Matrix;
            SelectedBoneVm.M11 = m.M11; SelectedBoneVm.M12 = m.M12; SelectedBoneVm.M13 = m.M13; SelectedBoneVm.M14 = m.M14;
            SelectedBoneVm.M21 = m.M21; SelectedBoneVm.M22 = m.M22; SelectedBoneVm.M23 = m.M23; SelectedBoneVm.M24 = m.M24;
            SelectedBoneVm.M31 = m.M31; SelectedBoneVm.M32 = m.M32; SelectedBoneVm.M33 = m.M33; SelectedBoneVm.M34 = m.M34;
            SelectedBoneVm.M41 = m.M41; SelectedBoneVm.M42 = m.M42; SelectedBoneVm.M43 = m.M43; SelectedBoneVm.M44 = m.M44;
        }

        public SimpleSkeletonViewModel(SkeletonBlob blob)
        {
            Blob = blob;
            foreach (var b in blob.Bones) Bones.Add(b);
            BoneMatrixClipboard.ClipboardChanged += (_, _) => OnPropertyChanged(nameof(HasCopiedMatrix));
        }

        [RelayCommand]
        public void AddBone()
        {
            var newBone = new Bone
            {
                Name = "New_Bone",
                ParentId = -1,
                FirstChildIndex = -1,
                NextIndex = -1,
                Matrix = Matrix4x4.Identity
            };
            Blob.Bones.Add(newBone);
            Bones.Add(newBone);
            SelectedBone = newBone;
        }

        [RelayCommand]
        public void DeleteBone()
        {
            if (SelectedBone == null) return;
            int idx = Bones.IndexOf(SelectedBone);
            Blob.Bones.Remove(SelectedBone);
            Bones.Remove(SelectedBone);
            if (Bones.Count > 0)
                SelectedBone = Bones[Math.Clamp(idx, 0, Bones.Count - 1)];
        }
    }

    public class TexCoordTransformViewModel : ObservableObject
    {
        private readonly Vector4[] _source;
        private readonly int _index;

        public string Label => $"UV #{_index}";

        public TexCoordTransformViewModel(Vector4[] source, int index)
        {
            _source = source;
            _index = index;
        }

        public float X
        {
            get => _source[_index].X;
            set { var v = _source[_index]; if (v.X != value) { v.X = value; _source[_index] = v; OnPropertyChanged(); } }
        }

        public float Y
        {
            get => _source[_index].Y;
            set { var v = _source[_index]; if (v.Y != value) { v.Y = value; _source[_index] = v; OnPropertyChanged(); } }
        }

        public float Z
        {
            get => _source[_index].Z;
            set { var v = _source[_index]; if (v.Z != value) { v.Z = value; _source[_index] = v; OnPropertyChanged(); } }
        }

        public float W
        {
            get => _source[_index].W;
            set { var v = _source[_index]; if (v.W != value) { v.W = value; _source[_index] = v; OnPropertyChanged(); } }
        }
    }

    // Shared static clipboard for copying bone transform matrices across panels and view modes.
    public static class BoneMatrixClipboard
    {
        private static Matrix4x4 _matrix;
        public static Matrix4x4 Matrix => _matrix;
        public static bool HasData { get; private set; }

        public static event EventHandler ClipboardChanged;

        public static void Set(Matrix4x4 matrix)
        {
            _matrix = matrix;
            HasData = true;
            ClipboardChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    //  VLay Blob ViewModels

    public partial class VLayBlobViewModel : ObservableObject
    {
        public VertexLayoutBlob Blob { get; }

        public ObservableCollection<VLaySemanticNameViewModel> SemanticNames { get; } = new();
        public ObservableCollection<VLayElementViewModel> Elements { get; } = new();

        // Common DXGI formats users will choose from
        public static readonly IReadOnlyList<ForzaTools.Shared.DXGI_FORMAT> CommonFormats = new[]
        {
            ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
            ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
            ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT,
            ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT,
            ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT,
            ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT,
            ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM,
            ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM,
            ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM,
            ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
            ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SNORM,
        };

        public uint RawFlags
        {
            get => Blob.Flags;
            set
            {
                if (Blob.Flags != value)
                {
                    Blob.Flags = value;
                    NotifyFlagsChanged();
                }
            }
        }

        public string FlagsHex => $"0x{Blob.Flags:X8}";

        // Per-bit flag display helpers (read-only, refreshed via NotifyFlagsChanged)
        public bool FlagTexCoord0 => (Blob.Flags & 0x001) != 0;
        public bool FlagTexCoord1 => (Blob.Flags & 0x002) != 0;
        public bool FlagTexCoord2 => (Blob.Flags & 0x004) != 0;
        public bool FlagTexCoord3 => (Blob.Flags & 0x008) != 0;
        public bool FlagTexCoord4 => (Blob.Flags & 0x010) != 0;
        public bool FlagTangent0OrTC5 => (Blob.Flags & 0x020) != 0;  // shared bit
        public bool FlagTangent1 => (Blob.Flags & 0x040) != 0;
        public bool FlagTangent2 => (Blob.Flags & 0x080) != 0;
        public bool FlagColor0 => (Blob.Flags & 0x400) != 0;

        public bool CompatFH2FM5 => (Blob.Flags & 0x6Fu) == 0x6Fu;
        public bool CompatFH3FH4FH5 => (Blob.Flags & 0x4FFu) == 0x4FFu;
        public bool CompatFM2023 => (Blob.Flags & 0xFFu) == 0xFFu;

        public string FlagsDescription
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (FlagTexCoord0) parts.Add("TC0");
                if (FlagTexCoord1) parts.Add("TC1");
                if (FlagTexCoord2) parts.Add("TC2");
                if (FlagTexCoord3) parts.Add("TC3");
                if (FlagTexCoord4) parts.Add("TC4");
                if (FlagTangent0OrTC5) parts.Add("TAN0");
                if (FlagTangent1) parts.Add("TAN1");
                if (FlagTangent2) parts.Add("TAN2");
                if (FlagColor0) parts.Add("COLOR0");
                return parts.Count > 0 ? string.Join(" | ", parts) : "(none)";
            }
        }

        // Mesh+material name strings for every mesh that references this vertex layout.
        public IReadOnlyList<string> LinkedMaterials { get; }

        // True when at least one mesh references this vertex layout.
        public bool HasLinkedMaterials => LinkedMaterials.Count > 0;

        private static IReadOnlyList<string> BuildLinkedMaterialsList(VertexLayoutBlob blob, Bundle? bundle)
        {
            if (bundle == null) return Array.Empty<string>();


            var vLayIdMeta = blob.Metadatas.OfType<IdentifierMetadata>().FirstOrDefault();
            int vLayMatchKey;
            if (vLayIdMeta != null)
            {
                vLayMatchKey = (int)vLayIdMeta.Id;
            }
            else
            {
                int ord = 0, found = -1;
                foreach (var b in bundle.Blobs)
                {
                    if (b is VertexLayoutBlob vlb)
                    {
                        if (ReferenceEquals(vlb, blob)) { found = ord; break; }
                        ord++;
                    }
                }
                if (found < 0) return Array.Empty<string>();
                vLayMatchKey = found;
            }

            // MaterialId in MeshBlob is a 0-based ordinal into the MaterialBlob subsequence
            var materials = bundle.Blobs.OfType<MaterialBlob>().ToArray();

            var seen   = new HashSet<string>();
            var result = new List<string>();

            foreach (var b in bundle.Blobs)
            {
                if (b is not MeshBlob mesh || mesh.VertexLayoutIndex != vLayMatchKey) continue;

                string meshName = mesh.Metadatas.OfType<NameMetadata>().FirstOrDefault()?.Name ?? "Mesh";
                int    matId    = mesh.MaterialId;
                string matName  = (matId >= 0 && matId < materials.Length)
                                  ? (materials[matId].Metadatas.OfType<NameMetadata>().FirstOrDefault()?.Name
                                     ?? $"Material #{matId}")
                                  : $"Material #{matId} (not found)";

                string entry = $"{meshName}  \u2192  {matName}";
                if (seen.Add(entry))
                    result.Add(entry);
            }

            return result;
        }

        private void NotifyFlagsChanged()
        {
            OnPropertyChanged(nameof(RawFlags));
            OnPropertyChanged(nameof(FlagsHex));
            OnPropertyChanged(nameof(FlagTexCoord0));
            OnPropertyChanged(nameof(FlagTexCoord1));
            OnPropertyChanged(nameof(FlagTexCoord2));
            OnPropertyChanged(nameof(FlagTexCoord3));
            OnPropertyChanged(nameof(FlagTexCoord4));
            OnPropertyChanged(nameof(FlagTangent0OrTC5));
            OnPropertyChanged(nameof(FlagTangent1));
            OnPropertyChanged(nameof(FlagTangent2));
            OnPropertyChanged(nameof(FlagColor0));
            OnPropertyChanged(nameof(CompatFH2FM5));
            OnPropertyChanged(nameof(CompatFH3FH4FH5));
            OnPropertyChanged(nameof(CompatFM2023));
            OnPropertyChanged(nameof(FlagsDescription));
        }

        [RelayCommand]
        public void RecomputeFlags()
        {
            uint flags = 0;
            foreach (var elem in Elements)
            {
                string name = elem.SemanticName;
                int si = elem.SemanticIndex;
                if (name.Equals("TEXCOORD", StringComparison.OrdinalIgnoreCase) && si <= 5)
                    flags |= (1u << si);
                else if (name.Equals("TANGENT", StringComparison.OrdinalIgnoreCase) && si <= 4)
                    flags |= (1u << (si + 5));
                else if (name.Equals("COLOR", StringComparison.OrdinalIgnoreCase) && si == 0)
                    flags |= 0x400u;
            }
            Blob.Flags = flags;
            NotifyFlagsChanged();
        }

        [RelayCommand]
        public async Task AddSemanticNameAsync()
        {
            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Add Semantic Name",
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var quickPick = new ComboBox
            {
                Header = "Quick Pick",
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                ItemsSource = new[] { "POSITION", "NORMAL", "BINORMAL", "TANGENT", "TEXCOORD", "COLOR", "BLENDINDICES", "BLENDWEIGHT" }
            };
            var customBox = new TextBox { Header = "Or enter custom name", PlaceholderText = "e.g. TEXCOORD" };
            quickPick.SelectionChanged += (s, e) => { if (quickPick.SelectedItem is string v) customBox.Text = v; };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(quickPick);
            panel.Children.Add(customBox);
            dialog.Content = panel;

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var name = customBox.Text.Trim().ToUpper();
                if (string.IsNullOrEmpty(name)) return;
                Blob.SemanticNames.Add(name);
                SemanticNames.Add(new VLaySemanticNameViewModel(Blob, Blob.SemanticNames.Count - 1, this));
            }
        }

        public void RemoveSemanticName(VLaySemanticNameViewModel svm)
        {
            int idx = svm.Index;
            // Check if any element references this index
            if (Elements.Any(e => e.Element.SemanticNameIndex == idx))
            {
                App.ShowErrorDialog("Cannot remove: one or more elements reference this semantic name.");
                return;
            }
            Blob.SemanticNames.RemoveAt(idx);
            SemanticNames.RemoveAt(idx);
            // Rebuild SemanticNames collection references (indices shift down)
            for (int i = idx; i < SemanticNames.Count; i++)
                SemanticNames[i].Index = i;
            // Adjust element indices that pointed above the removed item
            foreach (var e in Elements)
            {
                if (e.Element.SemanticNameIndex > idx)
                {
                    e.Element.SemanticNameIndex--;
                    e.RefreshSemanticName();
                }
            }
        }

        [RelayCommand]
        public async Task AddElementAsync()
        {
            if (SemanticNames.Count == 0)
            {
                App.ShowErrorDialog("Add at least one semantic name first.");
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Add Vertex Element",
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var semanticBox = new ComboBox
            {
                Header = "Semantic Name",
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                ItemsSource = Blob.SemanticNames,
                SelectedIndex = 0
            };
            var siBox = new NumberBox { Header = "Semantic Index", Value = 0, Minimum = 0, Maximum = 15, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact };
            var slotBox = new NumberBox { Header = "Input Slot (0=pos, 1=norm/UV)", Value = 1, Minimum = 0, Maximum = 7, SpinButtonPlacementMode = Microsoft.UI.Xaml.Controls.NumberBoxSpinButtonPlacementMode.Compact };
            var fmtBox = new ComboBox
            {
                Header = "DXGI Format",
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                ItemsSource = CommonFormats,
                SelectedItem = ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(semanticBox);
            panel.Children.Add(siBox);
            panel.Children.Add(slotBox);
            panel.Children.Add(fmtBox);
            dialog.Content = new ScrollViewer { Content = panel };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                int nameIdx = semanticBox.SelectedIndex < 0 ? 0 : semanticBox.SelectedIndex;
                var fmt = fmtBox.SelectedItem is ForzaTools.Shared.DXGI_FORMAT f ? f : ForzaTools.Shared.DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;

                var elem = new D3D12_INPUT_LAYOUT_DESC
                {
                    SemanticNameIndex = (short)nameIdx,
                    SemanticIndex = (short)(int)siBox.Value,
                    InputSlot = (short)(int)slotBox.Value,
                    InputSlotClass = 0,
                    Format = fmt,
                    AlignedByteOffset = -1,  // D3D12_APPEND_ALIGNED_ELEMENT
                    InstanceDataStepRate = 0
                };
                Blob.Elements.Add(elem);
                Blob.PackedFormats.Add(fmt);
                Elements.Add(new VLayElementViewModel(Blob, Blob.Elements.Count - 1, this));
                RecomputeFlags();
            }
        }

        public void RemoveElement(VLayElementViewModel evm)
        {
            int idx = evm.Index;
            Blob.Elements.RemoveAt(idx);
            Blob.PackedFormats.RemoveAt(idx);
            Elements.RemoveAt(idx);
            // Re-number remaining element VMs
            for (int i = idx; i < Elements.Count; i++)
                Elements[i].Index = i;
            RecomputeFlags();
        }

        private readonly Bundle? _bundle;
        private readonly string _contextName;
        public string ContextName => _contextName;

        [RelayCommand]
        public async Task SaveToLibraryAsync()
        {
            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Save VLay to Library",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var nameBox = new TextBox { Header = "Library entry name", Text = _contextName, PlaceholderText = "e.g. StandardMesh_VLay" };
            var descBox = new TextBox { Header = "Description (optional)", PlaceholderText = "e.g. 3 TC slots + tangent" };
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(nameBox);
            panel.Children.Add(descBox);
            dialog.Content = panel;

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var name = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var entry = new VLayLibraryEntry
            {
                SemanticNames = new List<string>(Blob.SemanticNames),
                Elements = Blob.Elements.Select(e => new VLayElementData
                {
                    SemanticNameIndex = e.SemanticNameIndex,
                    SemanticIndex = e.SemanticIndex,
                    InputSlot = e.InputSlot,
                    InputSlotClass = e.InputSlotClass,
                    Format = (int)e.Format,
                    AlignedByteOffset = e.AlignedByteOffset,
                    InstanceDataStepRate = e.InstanceDataStepRate
                }).ToList(),
                PackedFormats = Blob.PackedFormats.Select(f => (int)f).ToList(),
                Flags = Blob.Flags,
                Description = descBox.Text.Trim(),
                SavedAt = DateTime.UtcNow
            };
            VLayLibrary.AddOrUpdate(name, entry);
        }

        [RelayCommand]
        public async Task LoadFromLibraryAsync()
        {
            var all = VLayLibrary.GetAllEntries();
            if (all.Count == 0)
            {
                App.ShowErrorDialog("The VLay library is empty. Save an entry first.");
                return;
            }

            var displayItems = all.Select(kvp => new
            {
                Key = kvp.Key,
                Label = $"{kvp.Key}  [{kvp.Value.Elements.Count} elem, flags 0x{kvp.Value.Flags:X}]  {kvp.Value.Description}"
            }).ToList();

            var listBox = new Microsoft.UI.Xaml.Controls.ListBox();
            listBox.SelectionMode = Microsoft.UI.Xaml.Controls.SelectionMode.Single;
            listBox.DisplayMemberPath = "Label";
            listBox.ItemsSource = displayItems;
            if (listBox.Items.Count > 0) listBox.SelectedIndex = 0;

            var searchBox = new AutoSuggestBox { PlaceholderText = "Search...", Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 8) };
            searchBox.TextChanged += (s, e) =>
            {
                if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    var q = s.Text.ToLower();
                    listBox.ItemsSource = displayItems.Where(x => x.Key.ToLower().Contains(q) || x.Label.ToLower().Contains(q)).ToList();
                }
            };

            var sp = new StackPanel();
            sp.Children.Add(searchBox);
            sp.Children.Add(new ScrollViewer { Content = listBox, MaxHeight = 350 });

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Load VLay from Library",
                Content = sp,
                PrimaryButtonText = "Load",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (listBox.SelectedItem == null) return;

            // Retrieve selected key via anonymous type
            var selectedKey = ((dynamic)listBox.SelectedItem).Key as string;
            if (selectedKey == null || !all.TryGetValue(selectedKey, out var entry)) return;

            // Warn if structure differs
            if (entry.Elements.Count != Blob.Elements.Count || entry.SemanticNames.Count != Blob.SemanticNames.Count)
            {
                var warnDialog = new ContentDialog
                {
                    XamlRoot = App.MainWindow.Content.XamlRoot,
                    Title = "Count Mismatch",
                    Content = $"Entry has {entry.SemanticNames.Count} semantic names and {entry.Elements.Count} elements.\n" +
                              $"Current blob has {Blob.SemanticNames.Count} semantic names and {Blob.Elements.Count} elements.\n\n" +
                              "Applying will replace all data and update vertex buffer strides. Continue?",
                    PrimaryButtonText = "Apply",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await warnDialog.ShowAsync() != ContentDialogResult.Primary) return;
            }

            var (updateResults, skippedBufs, diagLines) = ApplyLibraryEntry(entry);

            // Build result summary � always include diagnostics so the user can see why
            // buffers were or were not updated.
            var sb = new System.Text.StringBuilder();

            if (updateResults.Count > 0)
            {
                sb.AppendLine($"\u2705 Updated {updateResults.Count} vertex buffer(s):");
                foreach (var (meshName, vbufBlobIdx) in updateResults)
                    sb.AppendLine($"  Mesh: {meshName}  \u2192  blob #{vbufBlobIdx}");
            }
            else
            {
                sb.AppendLine($"\u26A0 No vertex buffers were resized.");
            }

            if (skippedBufs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"\u23E9 Skipped {skippedBufs.Count} buffer(s):");
                foreach (var skip in skippedBufs)
                    sb.AppendLine(skip);
            }

            sb.AppendLine();
            sb.AppendLine("\u2500\u2500 Diagnostics \u2500\u2500");
            foreach (var line in diagLines)
                sb.AppendLine(line);

            var resultDialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Vertex Buffers Updated",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = sb.ToString().TrimEnd(),
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
                        FontSize = 11
                    },
                    MaxHeight = 380
                },
                CloseButtonText = "OK"
            };
            await resultDialog.ShowAsync();
        }

        private (List<(string meshName, int vbufBlobIdx)> Updated, List<string> Skipped, List<string> Diagnostics) ApplyLibraryEntry(VLayLibraryEntry entry)
        {
            Blob.SemanticNames.Clear();
            foreach (var s in entry.SemanticNames) Blob.SemanticNames.Add(s);

            Blob.Elements.Clear();
            foreach (var e in entry.Elements)
                Blob.Elements.Add(new D3D12_INPUT_LAYOUT_DESC
                {
                    SemanticNameIndex = (short)e.SemanticNameIndex,
                    SemanticIndex = (short)e.SemanticIndex,
                    InputSlot = (short)e.InputSlot,
                    InputSlotClass = (short)e.InputSlotClass,
                    Format = (ForzaTools.Shared.DXGI_FORMAT)e.Format,
                    AlignedByteOffset = e.AlignedByteOffset,
                    InstanceDataStepRate = e.InstanceDataStepRate
                });

            Blob.PackedFormats.Clear();
            foreach (var f in entry.PackedFormats) Blob.PackedFormats.Add((ForzaTools.Shared.DXGI_FORMAT)f);
            Blob.Flags = entry.Flags;

            SemanticNames.Clear();
            for (int i = 0; i < Blob.SemanticNames.Count; i++)
                SemanticNames.Add(new VLaySemanticNameViewModel(Blob, i, this));
            Elements.Clear();
            for (int i = 0; i < Blob.Elements.Count; i++)
                Elements.Add(new VLayElementViewModel(Blob, i, this));
            NotifyFlagsChanged();

            return UpdateVertexBuffers();
        }

        private (List<(string meshName, int vbufBlobIdx)> Updated, List<string> Skipped, List<string> Diagnostics) UpdateVertexBuffers()
        {
            var updated     = new List<(string meshName, int vbufBlobIdx)>();
            var skipped     = new List<string>();
            var diagnostics = new List<string>();

            if (_bundle == null)
            {
                diagnostics.Add("No bundle attached � cannot update vertex buffers.");
                return (updated, skipped, diagnostics);
            }

            // -- Step 1: resolve the integer key that MeshBlob.VertexLayoutIndex uses to point here.

            var vLayIdMeta = Blob.Metadatas.OfType<IdentifierMetadata>().FirstOrDefault();
            int vLayMatchKey;

            if (vLayIdMeta != null)
            {
                vLayMatchKey = (int)vLayIdMeta.Id;
                diagnostics.Add($"VLay match mode: ID={vLayMatchKey} (IdentifierMetadata)");
            }
            else
            {
                int ord = 0, found = -1;
                foreach (var b in _bundle.Blobs)
                {
                    if (b is VertexLayoutBlob vlb)
                    {
                        if (vlb == Blob) { found = ord; break; }
                        ord++;
                    }
                }
                if (found < 0)
                {
                    diagnostics.Add("VLay blob not found in _bundle.Blobs � cannot match.");
                    return (updated, skipped, diagnostics);
                }
                vLayMatchKey = found;
                diagnostics.Add($"VLay match mode: ordinal={vLayMatchKey} (no IdentifierMetadata)");
            }

            // -- Step 2: build a fast lookup for VertexBufferBlobs.

            var vbById = new Dictionary<int, (VertexBufferBlob blob, int arrayIdx)>();
            int vbWithId = 0;
            for (int i = 0; i < _bundle.Blobs.Count; i++)
            {
                if (_bundle.Blobs[i] is VertexBufferBlob vbb)
                {
                    var idMeta = vbb.Metadatas.OfType<IdentifierMetadata>().FirstOrDefault();
                    if (idMeta != null)
                    {
                        vbById[(int)idMeta.Id] = (vbb, i);
                        vbWithId++;
                    }
                }
            }
            diagnostics.Add($"VBuf lookup: {vbById.Count} blobs indexed by ID, {vbWithId} have IdentifierMetadata");

            // -- Step 3: walk every MeshBlob and update matching VBufs.
            int meshesScanned = 0, meshesMatched = 0;
            foreach (var blobItem in _bundle.Blobs)
            {
                if (blobItem is not MeshBlob mesh) continue;
                meshesScanned++;

                if (mesh.VertexLayoutIndex != vLayMatchKey) continue;
                meshesMatched++;

                string meshName = mesh.Metadatas.OfType<NameMetadata>().FirstOrDefault()?.Name
                                  ?? $"Mesh[blob#{_bundle.Blobs.IndexOf(blobItem)}]";

                int vbuProcessed = 0;
                foreach (var vbu in mesh.VertexBuffers)
                {
                    uint newStride = ComputeStride(vbu.InputSlot);

                    // 'newStride == 0' means the new layout has no elements for this input slot.
                    if (newStride == 0)
                    {
                        skipped.Add($"  '{meshName}' VBuf idx={vbu.Index} slot={vbu.InputSlot}: new layout has no elements for this slot � skipped");
                        continue;
                    }

                    // Resolve the VertexBufferBlob: ID match first, then direct array-index fallback.
                    VertexBufferBlob vbBlob = null;
                    int vbBlobArrayIdx = -1;
                    if (vbById.TryGetValue(vbu.Index, out var byId))
                    {
                        vbBlob = byId.blob;
                        vbBlobArrayIdx = byId.arrayIdx;
                        diagnostics.Add($"  '{meshName}' VBuf idx={vbu.Index}: resolved by ID ? blob #{vbBlobArrayIdx}");
                    }
                    else if (vbu.Index >= 0 && vbu.Index < _bundle.Blobs.Count
                             && _bundle.Blobs[vbu.Index] is VertexBufferBlob directVb)
                    {
                        vbBlob = directVb;
                        vbBlobArrayIdx = vbu.Index;
                        diagnostics.Add($"  '{meshName}' VBuf idx={vbu.Index}: resolved by array index ? blob #{vbBlobArrayIdx}");
                    }
                    else
                    {
                        diagnostics.Add($"  '{meshName}' VBuf idx={vbu.Index}: NOT FOUND (no ID match, index out of range or wrong type)");
                        skipped.Add($"  '{meshName}' VBuf idx={vbu.Index}: blob not found � skipped");
                        continue;
                    }


                    uint oldStride = vbBlob.Header.Stride;
                    if (oldStride == 0)
                    {
                        diagnostics.Add($"    ? Header.Stride=0 � skipping (uninitialised buffer)");
                        skipped.Add($"  '{meshName}' VBuf blob #{vbBlobArrayIdx}: Header.Stride=0 � skipped");
                        continue;
                    }

                    if (oldStride == newStride)
                    {
                        // Always sync the mesh-side field even when the stride hasn't changed.
                        vbu.Stride = newStride;
                        diagnostics.Add($"    ? stride unchanged ({oldStride}B) � mesh field synced, buffer not resized");
                        continue;
                    }

                    // Resize vertex buffer raw data: copy per-vertex rows, zero-pad or truncate as needed.
                    byte[] rawData    = vbBlob.Header.GetRawData();
                    int    vertexCount = vbBlob.Header.Length;

                    if (rawData != null && rawData.Length > 0 && vertexCount > 0)
                    {
                        var newRaw = new byte[vertexCount * (int)newStride];
                        int oldStrideI = (int)oldStride;
                        for (int vi = 0; vi < vertexCount; vi++)
                        {
                            int srcStart = vi * oldStrideI;
                            int dstStart = vi * (int)newStride;
                            int copyLen  = Math.Min(oldStrideI, (int)newStride);
                            if (srcStart + copyLen <= rawData.Length)
                                Buffer.BlockCopy(rawData, srcStart, newRaw, dstStart, copyLen);
                        }
                        vbBlob.Header.SetRawData(newRaw, vertexCount, (ushort)newStride);
                    }
                    else
                    {
                        vbBlob.Header.Stride = (ushort)newStride;
                    }

                    // Sync the mesh-side stride field.
                    vbu.Stride = newStride;

                    diagnostics.Add($"    ? resized {oldStride}B ? {newStride}B ({vertexCount} vertices)");
                    updated.Add((meshName, vbBlobArrayIdx));
                    vbuProcessed++;
                }

                diagnostics.Add($"Mesh '{meshName}': {mesh.VertexBuffers.Count} VBUs, {vbuProcessed} VBufs resized");
            }

            diagnostics.Add($"Scan complete: {meshesScanned} meshes scanned, {meshesMatched} matched VLay key={vLayMatchKey}");
            return (updated, skipped, diagnostics);
        }

        private uint ComputeStride(uint inputSlot)
        {
            uint stride = 0;
            foreach (var elem in Blob.Elements)
            {
                if ((uint)elem.InputSlot != inputSlot) continue;
                stride += ForzaTools.Shared.DxgiUtils.BitsPerPixel(elem.Format) / 8u;
            }
            return stride;
        }

        public VLayBlobViewModel(VertexLayoutBlob blob, Bundle? bundle = null, string contextName = "")
        {
            Blob = blob;
            _bundle = bundle;
            _contextName = contextName;
            LinkedMaterials = BuildLinkedMaterialsList(blob, bundle);
            for (int i = 0; i < blob.SemanticNames.Count; i++)
                SemanticNames.Add(new VLaySemanticNameViewModel(blob, i, this));
            for (int i = 0; i < blob.Elements.Count; i++)
                Elements.Add(new VLayElementViewModel(blob, i, this));
        }
    }

    public partial class VLaySemanticNameViewModel : ObservableObject
    {
        private readonly VertexLayoutBlob _blob;
        private readonly VLayBlobViewModel _parent;
        public int Index { get; set; }

        public string Name
        {
            get => Index < _blob.SemanticNames.Count ? _blob.SemanticNames[Index] : "";
            set
            {
                if (Index < _blob.SemanticNames.Count && _blob.SemanticNames[Index] != value)
                {
                    _blob.SemanticNames[Index] = value.ToUpper().Trim();
                    OnPropertyChanged();
                    // Refresh element display labels
                    foreach (var e in _parent.Elements.Where(e => e.Element.SemanticNameIndex == Index))
                        e.RefreshSemanticName();
                }
            }
        }

        public int UsageCount => _parent.Elements.Count(e => e.Element.SemanticNameIndex == Index);

        [RelayCommand]
        public void Remove() => _parent.RemoveSemanticName(this);

        public VLaySemanticNameViewModel(VertexLayoutBlob blob, int index, VLayBlobViewModel parent)
        {
            _blob = blob;
            Index = index;
            _parent = parent;
        }
    }

    public partial class VLayElementViewModel : ObservableObject
    {
        private readonly VertexLayoutBlob _blob;
        private readonly VLayBlobViewModel _parent;
        public int Index { get; set; }
        public D3D12_INPUT_LAYOUT_DESC Element => _blob.Elements[Index];

        public string SemanticName
        {
            get
            {
                var si = Element.SemanticNameIndex;
                return si >= 0 && si < _blob.SemanticNames.Count ? _blob.SemanticNames[si] : $"#{si}";
            }
        }

        public string DisplayLabel => $"{SemanticName}{Element.SemanticIndex}  [slot {Element.InputSlot}]  {Element.Format}";

        public int SemanticNameIndex
        {
            get => Element.SemanticNameIndex;
            set { if (Element.SemanticNameIndex != (short)value) { Element.SemanticNameIndex = (short)value; RefreshDisplay(); } }
        }

        public int SemanticIndex
        {
            get => Element.SemanticIndex;
            set { if (Element.SemanticIndex != (short)value) { Element.SemanticIndex = (short)value; RefreshDisplay(); _parent.RecomputeFlags(); } }
        }

        public int InputSlot
        {
            get => Element.InputSlot;
            set { if (Element.InputSlot != (short)value) { Element.InputSlot = (short)value; OnPropertyChanged(); } }
        }

        public int InputSlotClass
        {
            get => Element.InputSlotClass;
            set { if (Element.InputSlotClass != (short)value) { Element.InputSlotClass = (short)value; OnPropertyChanged(); } }
        }

        public ForzaTools.Shared.DXGI_FORMAT Format
        {
            get => Element.Format;
            set { if (Element.Format != value) { Element.Format = value; if (Index < _blob.PackedFormats.Count) _blob.PackedFormats[Index] = value; OnPropertyChanged(); } }
        }

        public int AlignedByteOffset
        {
            get => Element.AlignedByteOffset;
            set { if (Element.AlignedByteOffset != value) { Element.AlignedByteOffset = value; OnPropertyChanged(nameof(AlignedByteOffset)); OnPropertyChanged(nameof(IsAutoAligned)); } }
        }

        public int InstanceDataStepRate
        {
            get => Element.InstanceDataStepRate;
            set { if (Element.InstanceDataStepRate != value) { Element.InstanceDataStepRate = value; OnPropertyChanged(); } }
        }

        public bool IsAutoAligned
        {
            get => Element.AlignedByteOffset == -1;
            set { AlignedByteOffset = value ? -1 : 0; }
        }

        public string FlagBit
        {
            get
            {
                string name = SemanticName;
                int si = Element.SemanticIndex;
                if (name.Equals("TEXCOORD", StringComparison.OrdinalIgnoreCase) && si <= 5)
                    return $"bit {si}  (0x{(1u << si):X3})";
                if (name.Equals("TANGENT", StringComparison.OrdinalIgnoreCase) && si <= 4)
                    return $"bit {si + 5}  (0x{(1u << (si + 5)):X3})";
                if (name.Equals("COLOR", StringComparison.OrdinalIgnoreCase) && si == 0)
                    return "bit 10  (0x400)";
                return "�";
            }
        }

        public void RefreshSemanticName()
        {
            OnPropertyChanged(nameof(SemanticName));
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(FlagBit));
        }

        private void RefreshDisplay()
        {
            OnPropertyChanged(nameof(SemanticNameIndex));
            OnPropertyChanged(nameof(SemanticIndex));
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(FlagBit));
        }

        [RelayCommand]
        public void Remove() => _parent.RemoveElement(this);

        public VLayElementViewModel(VertexLayoutBlob blob, int index, VLayBlobViewModel parent)
        {
            _blob = blob;
            Index = index;
            _parent = parent;
        }
    }
}
