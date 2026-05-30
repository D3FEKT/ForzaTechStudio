using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.Views
{
    // Material editing for the selected mesh inside the 3D viewer
    public sealed partial class ViewportPage : Page
    {
        // Tracks if we're suppressing UI events during programmatic updates
        private bool _isUpdatingMaterialUi = false;

        // The MeshNode whose material is currently being edited
        private MeshNode? _activeMaterialMeshNode = null;

        /// <summary>
        /// Called from MeshSelector_SelectionChanged to refresh the mesh material expander.
        /// Shows the expander only when a specific single mesh is selected (not "All" or group).
        /// </summary>
        internal void RefreshMeshMaterialSection()
        {
            if (ModelBinSelector.SelectedItem is not ModelBinNode modelBin)
            {
                MeshMaterialExpander.Visibility = Visibility.Collapsed;
                _activeMaterialMeshNode = null;
                return;
            }

            var meshScope = MeshSelector.SelectedItem as MeshScopeItem;

            // Only show when a single specific mesh is selected
            if (meshScope?.Node == null)
            {
                MeshMaterialExpander.Visibility = Visibility.Collapsed;
                _activeMaterialMeshNode = null;
                return;
            }

            var meshNode = meshScope.Node;
            _activeMaterialMeshNode = meshNode;

            // Gather all materials from this modelbin
            ViewModel.MeshMaterials.Clear();
            if (modelBin.Bundle != null)
            {
                foreach (var blob in modelBin.Bundle.Blobs.OfType<MaterialBlob>())
                    ViewModel.MeshMaterials.Add(new ViewportMaterialItem(blob, modelBin.Name));
            }

            // Find which material is currently assigned to this mesh
            var meshBlob = meshNode.GeometryData?.SourceMesh;
            ViewportMaterialItem? currentMaterial = null;

            if (meshBlob != null && ViewModel.MeshMaterials.Count > 0)
            {
                short assignedId = meshBlob.MaterialIds != null && meshBlob.MaterialIds.Length > 1
                    ? meshBlob.MaterialIds[1]
                    : meshBlob.MaterialId;

                currentMaterial = ViewModel.MeshMaterials.FirstOrDefault(m => (short)m.MaterialId == assignedId)
                                  ?? ViewModel.MeshMaterials.FirstOrDefault();
            }
            else if (ViewModel.MeshMaterials.Count > 0)
            {
                currentMaterial = ViewModel.MeshMaterials.FirstOrDefault();
            }

            _isUpdatingMaterialUi = true;
            MaterialPickerCombo.ItemsSource = ViewModel.MeshMaterials;
            MaterialPickerCombo.SelectedItem = currentMaterial;
            _isUpdatingMaterialUi = false;

            ViewModel.SelectedMeshMaterial = currentMaterial;
            PopulateMeshMaterialDetails(currentMaterial);

            // Hide assign button initially (the right material is already selected)
            AssignMaterialBtn.Visibility = currentMaterial != null && meshBlob != null
                ? (IsSelectedMaterialAssigned(meshBlob, currentMaterial) ? Visibility.Collapsed : Visibility.Visible)
                : Visibility.Collapsed;

            MeshMaterialExpander.Visibility = Visibility.Visible;
        }

        private bool IsSelectedMaterialAssigned(MeshBlob meshBlob, ViewportMaterialItem material)
        {
            short assignedId = meshBlob.MaterialIds != null && meshBlob.MaterialIds.Length > 1
                ? meshBlob.MaterialIds[1]
                : meshBlob.MaterialId;
            return (short)material.MaterialId == assignedId;
        }

        private void PopulateMeshMaterialDetails(ViewportMaterialItem? material)
        {
            if (material == null)
            {
                MeshMaterialDetailsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            _isUpdatingMaterialUi = true;

            MeshMatIdBox.Value = material.MaterialId;
            MeshMatPathBox.Text = material.MaterialPath;
            MeshMaterialParamEditor.ParametersSource = material.Parameters;

            MeshMaterialDetailsPanel.Visibility = Visibility.Visible;
            _isUpdatingMaterialUi = false;
        }

        private void MaterialPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingMaterialUi) return;

            var selected = MaterialPickerCombo.SelectedItem as ViewportMaterialItem;
            ViewModel.SelectedMeshMaterial = selected;
            PopulateMeshMaterialDetails(selected);

            // Show/hide Assign button depending on whether this is already the mesh's material
            var meshBlob = _activeMaterialMeshNode?.GeometryData?.SourceMesh;
            if (selected != null && meshBlob != null)
            {
                AssignMaterialBtn.Visibility = IsSelectedMaterialAssigned(meshBlob, selected)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            else
            {
                AssignMaterialBtn.Visibility = Visibility.Collapsed;
            }
        }

        private void AssignMaterial_Click(object sender, RoutedEventArgs e)
        {
            var material = ViewModel.SelectedMeshMaterial;
            var meshBlob = _activeMaterialMeshNode?.GeometryData?.SourceMesh;
            if (material == null || meshBlob == null) return;

            short newId = (short)material.MaterialId;

            if (meshBlob.MaterialIds != null && meshBlob.MaterialIds.Length > 1)
            {
                meshBlob.MaterialIds[1] = newId;
                meshBlob.MaterialId = newId;
            }
            else
            {
                meshBlob.MaterialId = newId;
            }

            AssignMaterialBtn.Visibility = Visibility.Collapsed;
        }

        private void MeshMatId_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isUpdatingMaterialUi) return;
            var material = ViewModel.SelectedMeshMaterial;
            if (material == null) return;

            uint newId = (uint)args.NewValue;
            var idMeta = material.Blob.Metadatas.OfType<IdentifierMetadata>().FirstOrDefault();
            if (idMeta != null)
                idMeta.Id = newId;
            else
                material.Blob.Id = newId;
        }

        private async void AddMaterialParam_Click(object sender, RoutedEventArgs e)
        {
            var material = ViewModel.SelectedMeshMaterial;
            if (material?.Blob == null) return;

            var paramBlob = material.Blob.Bundle?.Blobs.OfType<MaterialShaderParameterBlob>().FirstOrDefault();
            if (paramBlob == null) return;

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Add Shader Parameter",
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var panel = new StackPanel { Spacing = 12 };
            var nameBox = new AutoSuggestBox
            {
                Header = "Parameter Name (or Hash)",
                PlaceholderText = "Type to search known names...",
                ItemsSource = NameHashService.Instance.GetAll().Values.OrderBy(x => x).ToList()
            };
            nameBox.TextChanged += (s, ev) =>
            {
                if (ev.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    var q = s.Text.ToLower();
                    s.ItemsSource = NameHashService.Instance.GetAll().Values
                        .Where(x => x.ToLower().Contains(q)).OrderBy(x => x).ToList();
                }
            };
            var typeBox = new ComboBox
            {
                Header = "Parameter Type",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = Enum.GetValues<ShaderParameterType>()
            };
            typeBox.SelectedIndex = 0;
            panel.Children.Add(nameBox);
            panel.Children.Add(typeBox);
            dialog.Content = panel;

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

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
                Value = GetMaterialDefaultValueForType(type)
            };

            paramBlob.Parameters.Add(newParam);
            material.Parameters.Add(newParam);
            MeshMaterialParamEditor.ParametersSource = material.Parameters;
        }

        private void RemoveMeshMaterialParameter(ShaderParameter? parameter)
        {
            var material = ViewModel.SelectedMeshMaterial;
            if (parameter == null || material?.Blob == null) return;

            var paramBlob = material.Blob.Bundle?.Blobs.OfType<MaterialShaderParameterBlob>().FirstOrDefault();
            if (paramBlob == null) return;

            paramBlob.Parameters.Remove(parameter);
            material.Parameters.Remove(parameter);
        }

        private async void SaveMaterialJson_Click(object sender, RoutedEventArgs e)
        {
            var material = ViewModel.SelectedMeshMaterial;
            if (material?.Blob == null) return;

            var picker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("JSON File", new List<string>() { ".json" });

            string materialName = MaterialExtractionService.GetMaterialName(material.Blob);
            if (string.IsNullOrEmpty(materialName)) materialName = "unnamed_material";
            picker.SuggestedFileName = $"{materialName}.json";

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            try
            {
                var entry = new Dictionary<string, MaterialEntry>();
                byte[] blobData = material.Blob.GetContents();
                string blobHex = BitConverter.ToString(blobData).Replace("-", " ");
                string metadataHex = CreateMaterialMetadataHex(materialName);
                entry[materialName] = new MaterialEntry { MaterialMetaData = metadataHex, MaterialBlob = blobHex };

                var jsonOptions = new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = MaterialJsonContext.Default };
                string jsonString = JsonSerializer.Serialize(entry, typeof(Dictionary<string, MaterialEntry>), jsonOptions);
                await File.WriteAllTextAsync(file.Path, jsonString);
                App.ShowInfoDialog($"Material '{materialName}' saved successfully!", "Save Complete");
            }
            catch (Exception ex)
            {
                App.ShowErrorDialog($"Failed to save material: {ex.Message}");
            }
        }

        private async void ReplaceMaterialJson_Click(object sender, RoutedEventArgs e)
        {
            var material = ViewModel.SelectedMeshMaterial;
            if (material?.Blob == null) return;

            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".json");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                string jsonString = await File.ReadAllTextAsync(file.Path);
                var materials = JsonSerializer.Deserialize(jsonString, MaterialJsonContext.Default.DictionaryStringMaterialEntry);
                if (materials == null || materials.Count == 0)
                {
                    App.ShowErrorDialog("No materials found in JSON file.");
                    return;
                }

                string selectedName = materials.Count == 1
                    ? materials.Keys.First()
                    : await ShowMaterialPickerDialog(materials.Keys);

                if (selectedName == null || !materials.TryGetValue(selectedName, out var entry)) return;
                if (string.IsNullOrEmpty(entry.MaterialBlob))
                {
                    App.ShowErrorDialog($"Material blob data is missing for '{selectedName}'.");
                    return;
                }

                byte[] blobData = HexToBytes(entry.MaterialBlob);
                ApplyMaterialBlobData(material.Blob, blobData, selectedName);
                material.Refresh();
                PopulateMeshMaterialDetails(material);
                App.ShowInfoDialog($"Material replaced with '{selectedName}' successfully!\n\nRemember to save the modelbin.", "Replace Complete");
            }
            catch (Exception ex)
            {
                App.ShowErrorDialog($"Failed to replace material: {ex.Message}");
            }
        }

        private async void BrowseMaterialLibrary_Click(object sender, RoutedEventArgs e)
        {
            var material = ViewModel.SelectedMeshMaterial;
            if (material?.Blob == null) return;

            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = App.MainWindow.Content.XamlRoot,
                    Title = "Browse Material Library",
                    PrimaryButtonText = "Select",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    IsPrimaryButtonEnabled = false
                };

                var gameCombo = new ComboBox
                {
                    Header = "Game Library",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    DisplayMemberPath = nameof(ForzaGameDefinition.DisplayName),
                    SelectedValuePath = nameof(ForzaGameDefinition.GameId),
                    ItemsSource = ForzaGameCatalog.MaterialLibraryGames.ToList()
                };
                var infoText = new TextBlock { FontSize = 11, Opacity = 0.7, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
                var listBox = new ListBox { SelectionMode = SelectionMode.Single, DisplayMemberPath = "DisplayText" };
                var searchBox = new AutoSuggestBox { PlaceholderText = "Search materials...", Margin = new Thickness(0, 0, 0, 12) };

                var displayItems = new List<ModelBinEditorViewModel.MaterialLibraryDisplayItem>();
                var currentLibrary = new Dictionary<string, MaterialEntry>(StringComparer.OrdinalIgnoreCase);

                void ApplyFilter()
                {
                    string q = searchBox.Text?.Trim() ?? string.Empty;
                    var filtered = string.IsNullOrWhiteSpace(q) ? displayItems
                        : displayItems.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                    listBox.ItemsSource = filtered;
                    dialog.IsPrimaryButtonEnabled = filtered.Count > 0;
                    if (filtered.Count > 0) listBox.SelectedIndex = 0;
                }

                void LoadLibrary()
                {
                    string gameId = gameCombo.SelectedValue as string ?? ForzaGameCatalog.GetPreferredMaterialLibraryGameId();
                    currentLibrary = MaterialLibrary.LoadEntries(gameId, out string resolvedPath, includeLegacyFallback: true);
                    bool isFallback = !string.Equals(resolvedPath, MaterialLibrary.GetLibraryPath(gameId), StringComparison.OrdinalIgnoreCase);
                    infoText.Text = currentLibrary.Count == 0
                        ? $"No saved materials found for {ForzaGameCatalog.GetDisplayName(gameId)}."
                        : isFallback
                            ? $"Using {Path.GetFileName(resolvedPath)} fallback for {ForzaGameCatalog.GetDisplayName(gameId)}."
                            : $"Using {Path.GetFileName(resolvedPath)} for {ForzaGameCatalog.GetDisplayName(gameId)}.";
                    displayItems = currentLibrary
                        .Select(kvp => new ModelBinEditorViewModel.MaterialLibraryDisplayItem
                        {
                            Name = kvp.Key,
                            DisplayText = $"{kvp.Key} ({kvp.Value.MaterialBlob.Length / 3} bytes)"
                        })
                        .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    ApplyFilter();
                }

                searchBox.TextChanged += (s, ev) => { if (ev.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ApplyFilter(); };
                gameCombo.SelectionChanged += (_, _) => LoadLibrary();

                var panel = new StackPanel { Spacing = 8 };
                panel.Children.Add(gameCombo);
                panel.Children.Add(infoText);
                panel.Children.Add(searchBox);
                panel.Children.Add(new ScrollViewer { Content = listBox, MaxHeight = 400 });
                dialog.Content = panel;

                gameCombo.SelectedValue = ForzaGameCatalog.GetPreferredMaterialLibraryGameId();
                LoadLibrary();

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && listBox.SelectedItem is ModelBinEditorViewModel.MaterialLibraryDisplayItem selectedItem)
                {
                    var entry = currentLibrary[selectedItem.Name];
                    if (string.IsNullOrEmpty(entry.MaterialBlob))
                    {
                        App.ShowErrorDialog($"Material blob data is missing for '{selectedItem.Name}'.");
                        return;
                    }

                    byte[] blobData = HexToBytes(entry.MaterialBlob);
                    ApplyMaterialBlobData(material.Blob, blobData, selectedItem.Name);
                    material.Refresh();
                    PopulateMeshMaterialDetails(material);
                    App.ShowInfoDialog($"Material replaced with '{selectedItem.Name}' from library.\n\nRemember to save the modelbin.", "Replace Complete");
                }
            }
            catch (Exception ex)
            {
                App.ShowErrorDialog($"Failed to browse material library: {ex.Message}");
            }
        }

        private async void SaveModelBinFromMaterial_Click(object sender, RoutedEventArgs e)
        {
            if (ModelBinSelector.SelectedItem is not ModelBinNode modelBin) return;
            if (modelBin.Bundle == null || string.IsNullOrEmpty(modelBin.FilePath)) return;

            try
            {
                await Task.Run(() =>
                {
                    using var stream = File.Open(modelBin.FilePath, FileMode.Create, FileAccess.Write);
                    modelBin.Bundle.Serialize(stream);
                });
                App.ShowInfoDialog($"Saved {modelBin.FileName}", "Save Complete");
            }
            catch (Exception ex)
            {
                App.ShowErrorDialog($"Save Error: {ex.Message}");
            }
        }

        // --- Helpers ---

        private void ApplyMaterialBlobData(MaterialBlob blob, byte[] blobData, string newName)
        {
            blob.CustomBlobData = blobData;
            blob.UncompressedSize = (uint)blobData.Length;
            blob.CompressedSize = (uint)blobData.Length;

            var nameMeta = blob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
            if (nameMeta != null)
                nameMeta.Name = newName;
            else
                blob.Metadatas.Add(new NameMetadata { Tag = BundleMetadata.TAG_METADATA_Name, Name = newName });

            if (blobData.Length >= 4)
            {
                try
                {
                    using var ms = new MemoryStream(blobData);
                    uint magic = BitConverter.ToUInt32(blobData, 0);
                    if (magic == ForzaTools.Bundles.Bundle.BundleTag)
                    {
                        var newBundle = new ForzaTools.Bundles.Bundle();
                        newBundle.Load(ms);
                        blob.Bundle = newBundle;
                        blob.CustomBlobData = null;
                    }
                }
                catch { /* keep CustomBlobData as fallback */ }
            }
        }

        private async Task<string?> ShowMaterialPickerDialog(IEnumerable<string> names)
        {
            var listBox = new ListBox { SelectionMode = SelectionMode.Single };
            foreach (var n in names) listBox.Items.Add(n);
            if (listBox.Items.Count > 0) listBox.SelectedIndex = 0;

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Select Material",
                Content = listBox,
                PrimaryButtonText = "Select",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? listBox.SelectedItem as string : null;
        }

        private static byte[] HexToBytes(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private static string CreateMaterialMetadataHex(string materialName)
        {
            byte[] nameTag = { 0x65, 0x6D, 0x61, 0x4E };
            byte[] idTag = { 0x20, 0x20, 0x64, 0x49 };
            byte[] nameBytes = Encoding.UTF8.GetBytes(materialName);
            byte strLen = (byte)nameBytes.Length;
            byte sizeWithOffset = (byte)(strLen + 8);

            // special nibble format
            byte firstByte = (byte)((strLen & 0x0F) << 4);
            byte secondByte = (byte)((strLen & 0xF0) >> 4);

            using var ms = new MemoryStream();
            ms.Write(nameTag);
            ms.WriteByte(firstByte);
            ms.WriteByte(secondByte);
            ms.WriteByte(0x10);
            ms.WriteByte(0x00);
            ms.Write(idTag);
            ms.WriteByte(0x40);
            ms.WriteByte(0x00);
            ms.WriteByte(sizeWithOffset);
            ms.WriteByte(0x00);
            ms.Write(nameBytes);
            ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
            return BitConverter.ToString(ms.ToArray()).Replace("-", " ");
        }

        private static object? GetMaterialDefaultValueForType(ShaderParameterType type) => type switch
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
}
