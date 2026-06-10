using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.Views
{
    public sealed partial class ViewportPage : Page
    {
        private sealed record ViewportSavePayload(
            IViewerNode Node,
            string DisplayName,
            string SuggestedRelativePath,
            string? FilePath,
            string? SourceZipPath,
            string? ZipEntryName,
            byte[] Bytes);

        private async Task SaveSelectedFileNodesAsync(IReadOnlyList<IViewerNode> nodes, bool saveAsFolder)
        {
            if (nodes.Count == 0)
                return;

            try
            {
                IsLoading = true;
                LoadingStatus = saveAsFolder ? "Saving selected files..." : "Saving selected files...";

                var payloads = new List<ViewportSavePayload>(nodes.Count);
                foreach (var node in nodes)
                    payloads.Add(await BuildSavePayloadAsync(node));

                if (saveAsFolder)
                {
                    var folderPath = await PickBatchSaveFolderAsync();
                    if (string.IsNullOrEmpty(folderPath))
                        return;

                    await Task.Run(() =>
                    {
                        foreach (var payload in payloads)
                        {
                            string outputPath = BuildBatchOutputPath(folderPath, payload.SuggestedRelativePath);
                            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                            File.WriteAllBytes(outputPath, payload.Bytes);
                        }
                    });

                    foreach (var payload in payloads)
                        MarkNodeSaved(payload.Node);

                    await ShowSaveSummary("Success", $"Saved {payloads.Count} selected file(s) to:\n{folderPath}");
                    return;
                }

                var missingTargets = payloads
                    .Where(p => !HasDirectSaveTarget(p))
                    .ToList();

                if (missingTargets.Count > 0)
                {
                    string names = string.Join("\n", missingTargets.Select(p => p.DisplayName));
                    await ShowError($"The following selected file(s) do not have a save target. Use Save As instead:\n\n{names}");
                    return;
                }

                var zipGroups = payloads
                    .Where(p => !string.IsNullOrEmpty(p.SourceZipPath) && !string.IsNullOrEmpty(p.ZipEntryName))
                    .GroupBy(p => p.SourceZipPath!, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                await Task.Run(() =>
                {
                    foreach (var payload in payloads.Where(p => !HasZipSaveTarget(p)))
                    {
                        File.WriteAllBytes(payload.FilePath!, payload.Bytes);
                    }

                    foreach (var group in zipGroups)
                    {
                        var replacements = group.ToDictionary(
                            p => p.ZipEntryName!,
                            p => p.Bytes,
                            StringComparer.OrdinalIgnoreCase);
                        ZipArchiveHelper.ReplaceEntries(group.Key, replacements);
                    }
                });

                foreach (var payload in payloads)
                    MarkNodeSaved(payload.Node);

                await ShowSaveSummary("Success", $"Saved {payloads.Count} selected file(s).");
            }
            catch (Exception ex)
            {
                await ShowError($"Failed to save selected file(s).\n\nError: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadingStatus = "";
            }
        }

        private async Task SaveCurrentAsync(bool saveAs, bool preferViewportMultiSelection = false)
        {
            if (saveAs)
            {
                var dirty = CollectDirtySaveFileNodes();
                if (dirty.Count > 1)
                {
                    await SaveSelectedFileNodesAsync(dirty, saveAsFolder: true);
                    return;
                }
            }

            var nodes = preferViewportMultiSelection
                ? GetViewportMultiSelectionSaveFileNodes()
                : GetCurrentSaveFileNodes();

            if (nodes.Count == 0 && saveAs)
            {
                var dirty = CollectDirtySaveFileNodes();
                if (dirty.Count == 1)
                    nodes.Add(dirty[0]);
            }

            if (nodes.Count == 0)
            {
                await ShowError("No saveable viewport file is currently selected.");
                return;
            }

            if (nodes.Count > 1)
            {
                await SaveSelectedFileNodesAsync(nodes, saveAsFolder: saveAs);
                return;
            }

            var node = nodes[0];
            if (saveAs || !HasDirectSaveTarget(node))
            {
                await SaveSingleFileNodeAsAsync(node);
                return;
            }

            await SaveSelectedFileNodesAsync(nodes, saveAsFolder: false);
        }

        private void ShowSaveCurrentOrSelectedFlyout(IReadOnlyList<IViewerNode> selectedSaveNodes)
        {
            if (SaveBtn == null)
                return;

            var flyout = new MenuFlyout();

            var saveCurrentItem = new MenuFlyoutItem
            {
                Text = "Save Current",
                Icon = new FontIcon { Glyph = "\uE74E", FontSize = 13 }
            };
            saveCurrentItem.Click += async (_, _) => await SaveCurrentAsync(saveAs: false);
            flyout.Items.Add(saveCurrentItem);

            var saveSelectedItem = new MenuFlyoutItem
            {
                Text = "Save Selected",
                Icon = new FontIcon { Glyph = "\uE8B7", FontSize = 13 },
                IsEnabled = selectedSaveNodes.Count > 0
            };
            saveSelectedItem.Click += async (_, _) => await SaveSelectedFileNodesAsync(selectedSaveNodes, saveAsFolder: false);
            flyout.Items.Add(saveSelectedItem);

            flyout.ShowAt(SaveBtn);
        }

        private bool ShouldShowViewportSaveFlyout()
        {
            return (_isMultiSelectActive && _multiSelectedMeshes.Count > 1)
                || (_isMultiLightSelectActive && _multiSelectedLightGroups.Count > 1);
        }

        private List<IViewerNode> GetViewportMultiSelectionSaveFileNodes()
        {
            var result = new List<IViewerNode>();
            var seen = new HashSet<IViewerNode>();

            if (_isMultiSelectActive)
            {
                foreach (var mesh in _multiSelectedMeshes)
                    AddSaveFileNodes(mesh, result, seen);
            }

            if (_isMultiLightSelectActive)
            {
                foreach (var group in _multiSelectedLightGroups)
                    AddSaveFileNodes(group, result, seen);
            }

            return result;
        }

        private List<IViewerNode> GetCurrentSaveFileNodes()
        {
            var result = GetSelectedSaveFileNodes();
            if (result.Count > 0)
                return result;

            var seen = new HashSet<IViewerNode>();
            void AddFromNode(IViewerNode? node)
            {
                if (node != null)
                    AddSaveFileNodes(node, result, seen);
            }

            AddFromNode(ViewModel.SelectedNode);

            if (ModelBinSelector?.SelectedItem is IViewerNode modelSelection)
                AddFromNode(modelSelection);

            if (MeshSelector?.SelectedItem is MeshScopeItem meshScope)
            {
                if (meshScope.Node != null)
                    AddFromNode(meshScope.Node);
                else if (ModelBinSelector?.SelectedItem is ModelBinNode modelBin)
                    AddFromNode(modelBin);
            }

            if (DamageMeshSelector?.SelectedItem is IViewerNode damageSelection)
                AddFromNode(damageSelection);
            if (LightPartSelector?.SelectedItem is IViewerNode lightSelection)
                AddFromNode(lightSelection);
            if (LocatorPartSelector?.SelectedItem is IViewerNode locatorSelection)
                AddFromNode(locatorSelection);
            if (CarbinModelSelector?.SelectedItem is IViewerNode carbinSelection)
                AddFromNode(carbinSelection);
            if (AvPinSelector?.SelectedItem is IViewerNode avPinSelection)
                AddFromNode(avPinSelection);

            return result;
        }

        private async Task SaveSingleFileNodeAsAsync(IViewerNode node)
        {
            var savePicker = new FileSavePicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);

            string extension = GetSaveExtension(node);
            savePicker.SuggestedStartLocation = PickerLocationId.Desktop;
            savePicker.SuggestedFileName = GetSuggestedSaveAsName(node, extension);
            savePicker.FileTypeChoices.Add(GetSavePickerLabel(node), new[] { extension });

            var outputFile = await savePicker.PickSaveFileAsync();
            if (outputFile == null)
                return;

            try
            {
                IsLoading = true;
                LoadingStatus = "Saving file...";

                var payload = await BuildSavePayloadAsync(node);
                await Task.Run(() => File.WriteAllBytes(outputFile.Path, payload.Bytes));

                ApplyStandaloneSaveTarget(node, outputFile.Path);
                MarkNodeSaved(node);

                await ShowSaveSummary("Success", $"Saved {payload.DisplayName} to:\n{outputFile.Name}");
            }
            catch (Exception ex)
            {
                await ShowError($"Failed to save file.\n\nError: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadingStatus = "";
            }
        }

        private static bool HasDirectSaveTarget(IViewerNode node)
        {
            return node switch
            {
                ModelBinNode modelBin => HasZipSaveTarget(modelBin.SourceZipPath, modelBin.ZipEntryName)
                    || HasStandaloneSaveTarget(modelBin.FilePath),
                LightsBinNode lightsBin => HasZipSaveTarget(lightsBin.SourceZipPath, lightsBin.ZipEntryName)
                    || HasStandaloneSaveTarget(lightsBin.FilePath),
                LocatorsXmlNode locatorsXml => HasZipSaveTarget(locatorsXml.SourceZipPath, locatorsXml.ZipEntryName)
                    || HasStandaloneSaveTarget(locatorsXml.FilePath),
                AvPinsFileNode avPins => HasZipSaveTarget(avPins.SourceZipPath, avPins.ZipEntryName)
                    || HasStandaloneSaveTarget(avPins.FilePath),
                CarbinFileNode carbin => HasZipSaveTarget(carbin.SourceZipPath, carbin.ZipEntryName)
                    || HasStandaloneSaveTarget(carbin.FilePath),
                _ => false
            };
        }

        private static bool HasDirectSaveTarget(ViewportSavePayload payload)
        {
            return HasZipSaveTarget(payload) || HasStandaloneSaveTarget(payload.FilePath);
        }

        private static bool HasZipSaveTarget(ViewportSavePayload payload)
        {
            return HasZipSaveTarget(payload.SourceZipPath, payload.ZipEntryName);
        }

        private static bool HasZipSaveTarget(string? sourceZipPath, string? zipEntryName)
        {
            return !string.IsNullOrEmpty(sourceZipPath)
                && !string.IsNullOrEmpty(zipEntryName)
                && File.Exists(sourceZipPath);
        }

        private static bool HasStandaloneSaveTarget(string? filePath)
        {
            return !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
        }

        private static void ApplyStandaloneSaveTarget(IViewerNode node, string filePath)
        {
            switch (node)
            {
                case ModelBinNode modelBin:
                    modelBin.FilePath = filePath;
                    modelBin.FileName = Path.GetFileName(filePath);
                    modelBin.SourceZipPath = null;
                    modelBin.ZipEntryName = null;
                    break;
                case LightsBinNode lightsBin:
                    lightsBin.FilePath = filePath;
                    lightsBin.SourceZipPath = null;
                    lightsBin.ZipEntryName = null;
                    break;
                case LocatorsXmlNode locatorsXml:
                    locatorsXml.FilePath = filePath;
                    locatorsXml.SourceZipPath = null;
                    locatorsXml.ZipEntryName = null;
                    break;
                case AvPinsFileNode avPins:
                    avPins.FilePath = filePath;
                    avPins.SourceZipPath = null;
                    avPins.ZipEntryName = null;
                    break;
                case CarbinFileNode carbin:
                    carbin.FilePath = filePath;
                    carbin.SourceZipPath = null;
                    carbin.ZipEntryName = null;
                    break;
            }
        }

        private static string GetSaveExtension(IViewerNode node) => node switch
        {
            ModelBinNode => ".modelbin",
            LightsBinNode => ".bin",
            LocatorsXmlNode => ".xml",
            AvPinsFileNode => ".avpins",
            CarbinFileNode => ".carbin",
            _ => ".bin"
        };

        private static string GetSavePickerLabel(IViewerNode node) => node switch
        {
            ModelBinNode => "Forza Modelbin",
            LightsBinNode => "Lights Binary",
            LocatorsXmlNode => "Locator XML",
            AvPinsFileNode => "Autovista POI File",
            CarbinFileNode => "Carbin File",
            _ => "Forza File"
        };

        private static string GetSuggestedSaveAsName(IViewerNode node, string extension)
        {
            string baseName = node switch
            {
                ModelBinNode modelBin when !string.IsNullOrWhiteSpace(modelBin.FilePath) => Path.GetFileNameWithoutExtension(modelBin.FilePath),
                ModelBinNode modelBin when !string.IsNullOrWhiteSpace(modelBin.FileName) => Path.GetFileNameWithoutExtension(modelBin.FileName),
                LightsBinNode lightsBin when !string.IsNullOrWhiteSpace(lightsBin.FilePath) => Path.GetFileNameWithoutExtension(lightsBin.FilePath),
                LocatorsXmlNode locatorsXml when !string.IsNullOrWhiteSpace(locatorsXml.FilePath) => Path.GetFileNameWithoutExtension(locatorsXml.FilePath),
                AvPinsFileNode avPins when !string.IsNullOrWhiteSpace(avPins.FilePath) => Path.GetFileNameWithoutExtension(avPins.FilePath),
                CarbinFileNode carbin when !string.IsNullOrWhiteSpace(carbin.FilePath) => Path.GetFileNameWithoutExtension(carbin.FilePath),
                _ => Path.GetFileNameWithoutExtension(node.Name ?? "file")
            };

            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "file";

            return EnsureExtension(baseName + "_modified", extension);
        }

        private async Task<ViewportSavePayload> BuildSavePayloadAsync(IViewerNode node)
        {
            return node switch
            {
                ModelBinNode modelBin => await BuildModelBinSavePayloadAsync(modelBin),
                LightsBinNode lightsBin => await BuildLightsBinSavePayloadAsync(lightsBin),
                LocatorsXmlNode locatorsXml => await BuildLocatorsSavePayloadAsync(locatorsXml),
                AvPinsFileNode avPins => await BuildAvPinsSavePayloadAsync(avPins),
                CarbinFileNode carbin => await BuildCarbinSavePayloadAsync(carbin),
                _ => throw new InvalidOperationException($"{node.Name} is not a saveable file node.")
            };
        }

        private async Task<ViewportSavePayload> BuildModelBinSavePayloadAsync(ModelBinNode modelBin)
        {
            if (modelBin.Bundle == null)
                throw new InvalidOperationException($"{modelBin.Name} has no model data to save.");

            BakeRotationsIntoVertexData(modelBin);
            bool convertLods = ConvertLodsToLod0Toggle?.IsChecked == true;
            byte[] bytes = await Task.Run(() =>
            {
                var lodOriginals = PatchLodConversionForSave(modelBin.Bundle, convertLods);
                try
                {
                    using var ms = new MemoryStream();
                    modelBin.Bundle.SerializeConverted(ms);
                    return ms.ToArray();
                }
                finally
                {
                    RevertLodConversionAfterSave(lodOriginals);
                }
            });

            return CreatePayload(modelBin, modelBin.FilePath, modelBin.SourceZipPath, modelBin.ZipEntryName, ".modelbin", bytes);
        }

        private async Task<ViewportSavePayload> BuildLightsBinSavePayloadAsync(LightsBinNode lightsBin)
        {
            if (lightsBin.OriginalData == null)
                throw new InvalidOperationException($"{lightsBin.Name} has no lights data to save.");

            byte[] bytes = await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                var parser = new LightsBinParser();
                parser.Serialize(ms, lightsBin.OriginalData);
                return ms.ToArray();
            });

            return CreatePayload(lightsBin, lightsBin.FilePath, lightsBin.SourceZipPath, lightsBin.ZipEntryName, ".bin", bytes);
        }

        private async Task<ViewportSavePayload> BuildLocatorsSavePayloadAsync(LocatorsXmlNode locatorsXml)
        {
            if (locatorsXml.LocatorsData == null)
                throw new InvalidOperationException($"{locatorsXml.Name} has no locators data to save.");

            byte[] bytes = await Task.Run(() =>
            {
                var parser = new LocatorsXmlParser();
                return Encoding.UTF8.GetBytes(parser.Serialize(locatorsXml.LocatorsData));
            });

            return CreatePayload(locatorsXml, locatorsXml.FilePath, locatorsXml.SourceZipPath, locatorsXml.ZipEntryName, ".xml", bytes);
        }

        private async Task<ViewportSavePayload> BuildAvPinsSavePayloadAsync(AvPinsFileNode avPins)
        {
            if (avPins.AvPinsData == null)
                throw new InvalidOperationException($"{avPins.Name} has no .avpins data to save.");

            byte[] bytes = await Task.Run(() => Encoding.UTF8.GetBytes(AvPinsParser.Serialize(avPins.AvPinsData)));
            return CreatePayload(avPins, avPins.FilePath, avPins.SourceZipPath, avPins.ZipEntryName, ".avpins", bytes);
        }

        private async Task<ViewportSavePayload> BuildCarbinSavePayloadAsync(CarbinFileNode carbin)
        {
            if (carbin.CarbinData == null)
                throw new InvalidOperationException($"{carbin.Name} has no .carbin data to save.");

            byte[] bytes = await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                carbin.CarbinData.Save(ms);
                return ms.ToArray();
            });

            return CreatePayload(carbin, carbin.FilePath, carbin.SourceZipPath, carbin.ZipEntryName, ".carbin", bytes);
        }

        private ViewportSavePayload CreatePayload(
            IViewerNode node,
            string? filePath,
            string? sourceZipPath,
            string? zipEntryName,
            string fallbackExtension,
            byte[] bytes)
        {
            string relativePath = !string.IsNullOrEmpty(zipEntryName)
                ? zipEntryName.Replace('\\', '/')
                : !string.IsNullOrEmpty(filePath)
                    ? Path.GetFileName(filePath)
                    : EnsureExtension(node.Name ?? "file", fallbackExtension);

            return new ViewportSavePayload(
                node,
                node.Name ?? Path.GetFileName(relativePath),
                relativePath,
                filePath,
                sourceZipPath,
                zipEntryName,
                bytes);
        }

        private List<IViewerNode> GetSelectedSaveFileNodes()
        {
            var result = new List<IViewerNode>();
            var seen = new HashSet<IViewerNode>();

            IEnumerable<object> selectedItems = FileTree.SelectedItems?.Cast<object>() ?? Enumerable.Empty<object>();
            if (!selectedItems.Any() && FileTree.SelectedItem != null)
                selectedItems = new[] { FileTree.SelectedItem };

            foreach (var selectedItem in selectedItems)
            {
                if (TryGetViewerNode(selectedItem, out var node))
                    AddSaveFileNodes(node, result, seen);
            }

            return result;
        }

        private static bool TryGetViewerNode(object item, [NotNullWhen(true)] out IViewerNode? node)
        {
            if (item is TreeViewNode treeNode && treeNode.Content is IViewerNode treeContent)
            {
                node = treeContent;
                return true;
            }

            if (item is IViewerNode viewerNode)
            {
                node = viewerNode;
                return true;
            }

            node = null;
            return false;
        }

        private static void AddSaveFileNodes(IViewerNode node, List<IViewerNode> result, HashSet<IViewerNode> seen)
        {
            var saveNode = ResolveSaveFileNode(node);
            if (saveNode != null)
            {
                if (seen.Add(saveNode))
                    result.Add(saveNode);
                return;
            }

            foreach (var child in node.Children)
                AddSaveFileNodes(child, result, seen);
        }

        private static IViewerNode? ResolveSaveFileNode(IViewerNode node)
        {
            return node switch
            {
                ModelBinNode or LightsBinNode or LocatorsXmlNode or AvPinsFileNode or CarbinFileNode => node,
                MeshNode mesh => mesh.ParentModelBin ?? FindAncestor<ModelBinNode>(mesh),
                DamageMeshNode damageMesh => damageMesh.ParentModelBin ?? FindAncestor<ModelBinNode>(damageMesh),
                LightGroupNode lightGroup => FindAncestor<LightsBinNode>(lightGroup),
                LightRowNode lightRow => FindAncestor<LightsBinNode>(lightRow),
                LocatorNode locator => FindAncestor<LocatorsXmlNode>(locator),
                AvPinNode avPin => FindAncestor<AvPinsFileNode>(avPin),
                CarbinModelNode or CarbinPartNode => FindAncestor<CarbinFileNode>(node),
                _ => null
            };
        }

        private static T? FindAncestor<T>(IViewerNode node) where T : class, IViewerNode
        {
            var current = node.Parent;
            while (current != null)
            {
                if (current is T typed)
                    return typed;
                current = current.Parent;
            }
            return null;
        }

        private async Task<string?> PickBatchSaveFolderAsync()
        {
            var folderPicker = new FolderPicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hWnd);
            folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");
            var folder = await folderPicker.PickSingleFolderAsync();
            return folder?.Path;
        }

        private static string BuildBatchOutputPath(string folderPath, string relativePath)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p != "." && p != "..")
                .Select(p => new string(p.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()))
                .ToArray();

            if (parts.Length == 0)
                parts = new[] { "file.bin" };

            string outputPath = folderPath;
            foreach (var part in parts)
                outputPath = Path.Combine(outputPath, part);
            return outputPath;
        }

        private static string EnsureExtension(string name, string extension)
        {
            return name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension;
        }

        private static void MarkNodeSaved(IViewerNode node)
        {
            switch (node)
            {
                case ModelBinNode modelBin:
                    MarkModelBinSaved(modelBin);
                    break;
                case LightsBinNode lightsBin:
                    lightsBin.IsDirty = false;
                    break;
                case LocatorsXmlNode locatorsXml:
                    locatorsXml.IsDirty = false;
                    break;
                case AvPinsFileNode avPins:
                    avPins.IsDirty = false;
                    break;
                case CarbinFileNode carbin:
                    carbin.IsDirty = false;
                    break;
            }
        }

        private static void MarkModelBinSaved(ModelBinNode modelBin)
        {
            foreach (var mesh in modelBin.Children.OfType<MeshNode>())
            {
                if (mesh.GeometryData?.SourceMesh == null)
                    continue;

                mesh.OriginalPositionScale = mesh.GeometryData.SourceMesh.PositionScale;
                mesh.OriginalPositionTranslate = mesh.GeometryData.SourceMesh.PositionTranslate;
                mesh.OriginalRotationEulerDegrees = mesh.GeometryData.RotationEulerDegrees;
            }

            modelBin.IsDirty = false;
        }

        private async void SaveAllModified_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var dirty = CollectDirtySaveFileNodes();
            if (dirty.Count == 0)
            {
                await ShowSaveSummary("Save All", "No modified files to save.");
                return;
            }

            await SaveSelectedFileNodesAsync(dirty, saveAsFolder: false);
        }

        private List<IViewerNode> CollectDirtySaveFileNodes()
        {
            var result = new List<IViewerNode>();
            var seen = new HashSet<IViewerNode>();

            foreach (var node in EnumerateAllViewerNodes(ViewModel.Roots))
            {
                if (!IsSaveNodeDirty(node) || !seen.Add(node))
                    continue;
                result.Add(node);
            }

            return result;
        }

        private static bool IsSaveNodeDirty(IViewerNode node) => node switch
        {
            ModelBinNode modelBin => modelBin.IsDirty,
            LightsBinNode lightsBin => lightsBin.IsDirty,
            LocatorsXmlNode locatorsXml => locatorsXml.IsDirty,
            AvPinsFileNode avPins => avPins.IsDirty,
            CarbinFileNode carbin => carbin.IsDirty,
            _ => false
        };

        private static IEnumerable<IViewerNode> EnumerateAllViewerNodes(IEnumerable<IViewerNode> roots)
        {
            foreach (var root in roots)
            {
                yield return root;
                foreach (var child in EnumerateAllViewerNodes(root.Children))
                    yield return child;
            }
        }

        private async Task ShowSaveSummary(string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
