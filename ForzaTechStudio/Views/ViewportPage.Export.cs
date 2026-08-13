using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using FbxExportFormat = ForzaTechStudio.Services.FbxExportFormat;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ForzaTechStudio.Views
{
    // OBJ / MTL export handlers
    public sealed partial class ViewportPage : Page
    {
        // Button click handlers

        private async void ExportObjScene_Click(object sender, RoutedEventArgs e)
        {
            var models = CollectModelBinExportData(ViewModel.Roots).ToList();

            if (models.Count == 0 || models.All(m => m.Meshes.Count == 0))
            {
                await ShowError("No meshes are currently loaded.");
                return;
            }

            await PerformObjExport(models, "scene");
        }

        private async void ExportObjSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ModelBinSelector.SelectedItem is not ModelBinNode selectedModel)
            {
                await ShowError("No model is selected.\n\nSelect a model from the Model dropdown first.");
                return;
            }

            var meshes = selectedModel.Children
                .OfType<MeshNode>()
                .Where(m => m.GeometryData != null)
                .Select(m => (Name: m.Name, Data: m.GeometryData!))
                .Where(m => _exportOptions.ShouldExportMesh(m.Name, m.Data.SourceMesh))
                .ToList();

            if (meshes.Count == 0)
            {
                await ShowError("The selected model contains no exportable meshes.");
                return;
            }

            var models = new List<ModelBinExportData>
            {
                new ModelBinExportData(
                    selectedModel.Name ?? selectedModel.FileName ?? "model",
                    selectedModel.Bundle,
                    meshes)
            };

            await PerformObjExport(models, selectedModel.Name ?? selectedModel.FileName ?? "model");
        }

        // Core export logic

        private async Task PerformObjExport(
            IEnumerable<ModelBinExportData> models,
            string suggestedBaseName)
        {
            var allModels = models.ToList();
            if (_exportOptions.MultiFileExport && allModels.Count > 1)
            {
                await PerformObjExportMultiFile(allModels);
                return;
            }

            // Show the picker first — the saving overlay must not appear behind it.
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);
            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            savePicker.SuggestedFileName = suggestedBaseName;
            savePicker.FileTypeChoices.Add("Wavefront OBJ", new[] { ".obj" });

            var outFile = await savePicker.PickSaveFileAsync();
            if (outFile == null)
                return;

            // File chosen — now show the saving overlay.
            IsLoading = true;
            LoadingStatus = "Building geometry...";
            await Task.Yield(); // let the UI paint before doing CPU work

            try
            {
                string mtlFileName = Path.GetFileNameWithoutExtension(outFile.Name) + ".mtl";
                var modelList = models.ToList();

                // Collect zip paths on the UI thread before handing off to the background.
                string outputDir = Path.GetDirectoryName(outFile.Path) ?? string.Empty;
                var zipPathList = CollectSourceZipPaths(ViewModel.Roots).ToList();

                // Build text content on a background thread.
                // The texture path map is built here too (zip central-directory scan only,
                // no extraction) so that map_Kd lines are included in the MTL.
                string? objContent = null;
                string? mtlContent = null;
                await Task.Run(() =>
                {
                    var texMap = BuildTexturePathMap(modelList, zipPathList, _exportOptions.TextureFormat);
                    objContent = ObjExportService.BuildObjContent(modelList, mtlFileName, _exportOptions);
                    mtlContent = ObjExportService.BuildMtlContent(modelList, texMap);
                });

                LoadingStatus = "Saving files...";
                await Task.Yield();

                // Write OBJ via WinRT — guaranteed to work in packaged apps.
                await Windows.Storage.FileIO.WriteTextAsync(outFile, objContent);

                // Write MTL via the parent StorageFolder (required for packaged apps;
                // the broker only grants access to the picker-chosen file, not siblings).
                var folder = await outFile.GetParentAsync();
                if (folder != null)
                {
                    var mtlFile = await folder.CreateFileAsync(
                        mtlFileName,
                        Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    await Windows.Storage.FileIO.WriteTextAsync(mtlFile, mtlContent);
                }

                // Export textures from any source zip(s).
                LoadingStatus = "Exporting textures...";
                await Task.Yield();
                string? texSummary = await ExportZipTextures(zipPathList, outputDir, _exportOptions.TextureFormat);

                // Hide the overlay before showing the success dialog.
                IsLoading = false;
                LoadingStatus = "";

                int meshCount = modelList.Sum(m => m.Meshes.Count);
                string content = $"Written {meshCount} mesh(es) across {modelList.Count} model(s).\n\n" +
                                 $"{outFile.Name}\n{mtlFileName}";
                if (texSummary != null)
                    content += $"\n\nTextures:\n{texSummary}";

                var dlg = new ContentDialog
                {
                    Title = "Export Complete",
                    Content = content,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dlg.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowError($"OBJ export failed.\n\nError: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadingStatus = "";
            }
        }

        // FBX export handlers

        private async void ExportFbxAsciiScene_Click(object sender, RoutedEventArgs e)
            => await ExportFbxScene(FbxExportFormat.Ascii);

        private async void ExportFbxAsciiSelected_Click(object sender, RoutedEventArgs e)
            => await ExportFbxSelected(FbxExportFormat.Ascii);

        private async void ExportFbxBinaryScene_Click(object sender, RoutedEventArgs e)
            => await ExportFbxScene(FbxExportFormat.Binary);

        private async void ExportFbxBinarySelected_Click(object sender, RoutedEventArgs e)
            => await ExportFbxSelected(FbxExportFormat.Binary);

        private async Task ExportFbxScene(FbxExportFormat format)
        {
            var models = CollectModelBinExportData(ViewModel.Roots).ToList();

            if (models.Count == 0 || models.All(m => m.Meshes.Count == 0))
            {
                await ShowError("No meshes are currently loaded.");
                return;
            }

            await PerformFbxExport(models, "scene", format);
        }

        private async Task ExportFbxSelected(FbxExportFormat format)
        {
            if (ModelBinSelector.SelectedItem is not ModelBinNode selectedModel)
            {
                await ShowError("No model is selected.\n\nSelect a model from the Model dropdown first.");
                return;
            }

            var meshes = selectedModel.Children
                .OfType<MeshNode>()
                .Where(m => m.GeometryData != null)
                .Select(m => (Name: m.Name, Data: m.GeometryData!))
                .Where(m => _exportOptions.ShouldExportMesh(m.Name, m.Data.SourceMesh))
                .ToList();

            if (meshes.Count == 0)
            {
                await ShowError("The selected model contains no exportable meshes.");
                return;
            }

            var models = new List<ModelBinExportData>
            {
                new ModelBinExportData(
                    selectedModel.Name ?? selectedModel.FileName ?? "model",
                    selectedModel.Bundle,
                    meshes)
            };

            await PerformFbxExport(models, selectedModel.Name ?? selectedModel.FileName ?? "model", format);
        }

        private async Task PerformFbxExport(
            IEnumerable<ModelBinExportData> models,
            string suggestedBaseName,
            FbxExportFormat format)
        {
            var allModels = models.ToList();
            if (_exportOptions.MultiFileExport && allModels.Count > 1)
            {
                await PerformFbxExportMultiFile(allModels, format);
                return;
            }

            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);
            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            savePicker.SuggestedFileName = suggestedBaseName;
            savePicker.FileTypeChoices.Add("Autodesk FBX", new[] { ".fbx" });

            var outFile = await savePicker.PickSaveFileAsync();
            if (outFile == null)
                return;

            IsLoading = true;
            LoadingStatus = format == FbxExportFormat.Binary
                ? "Building FBX Binary geometry..."
                : "Building FBX ASCII geometry...";
            await Task.Yield();

            try
            {
                var modelList = models.ToList();
                string outPath = outFile.Path;
                string outputDir = Path.GetDirectoryName(outPath) ?? string.Empty;
                var zipPathList = CollectSourceZipPaths(ViewModel.Roots).ToList();
                var texMap = BuildTexturePathMap(modelList, zipPathList, _exportOptions.TextureFormat);

                await Task.Run(() => FbxExportService.Export(modelList, outPath, format, texMap, _exportOptions));

                // Export textures from any source zip(s).
                LoadingStatus = "Exporting textures...";
                await Task.Yield();
                string? texSummary = await ExportZipTextures(zipPathList, outputDir, _exportOptions.TextureFormat);

                IsLoading = false;
                LoadingStatus = "";

                int meshCount = modelList.Sum(m => m.Meshes.Count);
                string formatLabel = format == FbxExportFormat.Binary ? "Binary" : "ASCII";
                string content = $"Written {meshCount} mesh(es) across {modelList.Count} model(s).\n\nFormat: FBX {formatLabel}\n{outFile.Name}";
                if (texSummary != null)
                    content += $"\n\nTextures:\n{texSummary}";

                var dlg = new ContentDialog
                {
                    Title = "Export Complete",
                    Content = content,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dlg.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowError($"FBX export failed.\n\nError: {ex.Message}\n\n{ex.InnerException?.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadingStatus = "";
            }
        }

        // Helpers


        private async Task PerformObjExportMultiFile(List<ModelBinExportData> models)
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hWnd);
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder == null)
                return;

            IsLoading = true;
            LoadingStatus = "Building geometry...";
            await Task.Yield();

            try
            {
                string outputDir = folder.Path;
                var zipPathList = CollectSourceZipPaths(ViewModel.Roots).ToList();
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int fileCount = 0;

                foreach (var model in models)
                {
                    if (model.Meshes.Count == 0)
                        continue;

                    string baseName = UniqueFileName(usedNames, model.ModelBinName);
                    string mtlFileName = baseName + ".mtl";
                    var single = new List<ModelBinExportData> { model };

                    string objContent = string.Empty;
                    string mtlContent = string.Empty;
                    await Task.Run(() =>
                    {
                        var texMap = BuildTexturePathMap(single, zipPathList, _exportOptions.TextureFormat);
                        objContent = ObjExportService.BuildObjContent(single, mtlFileName, _exportOptions);
                        mtlContent = ObjExportService.BuildMtlContent(single, texMap);
                    });

                    var objFile = await folder.CreateFileAsync(baseName + ".obj",
                        Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    await Windows.Storage.FileIO.WriteTextAsync(objFile, objContent);

                    var mtlFile = await folder.CreateFileAsync(mtlFileName,
                        Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    await Windows.Storage.FileIO.WriteTextAsync(mtlFile, mtlContent);
                    fileCount++;
                }

                LoadingStatus = "Exporting textures...";
                await Task.Yield();
                string? texSummary = await ExportZipTextures(zipPathList, outputDir, _exportOptions.TextureFormat);

                IsLoading = false;
                LoadingStatus = "";

                string content = $"Written {fileCount} OBJ file(s) to:\n{outputDir}";
                if (texSummary != null)
                    content += $"\n\nTextures:\n{texSummary}";

                await new ContentDialog
                {
                    Title = "Export Complete",
                    Content = content,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowError($"OBJ export failed.\n\nError: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadingStatus = "";
            }
        }

        private async Task PerformFbxExportMultiFile(List<ModelBinExportData> models, FbxExportFormat format)
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hWnd);
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder == null)
                return;

            IsLoading = true;
            LoadingStatus = format == FbxExportFormat.Binary
                ? "Building FBX Binary geometry..."
                : "Building FBX ASCII geometry...";
            await Task.Yield();

            try
            {
                string outputDir = folder.Path;
                var zipPathList = CollectSourceZipPaths(ViewModel.Roots).ToList();
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int fileCount = 0;

                foreach (var model in models)
                {
                    if (model.Meshes.Count == 0)
                        continue;

                    string baseName = UniqueFileName(usedNames, model.ModelBinName);
                    string outPath = Path.Combine(outputDir, baseName + ".fbx");
                    var single = new List<ModelBinExportData> { model };
                    var texMap = BuildTexturePathMap(single, zipPathList, _exportOptions.TextureFormat);

                    await Task.Run(() => FbxExportService.Export(single, outPath, format, texMap, _exportOptions));
                    fileCount++;
                }

                LoadingStatus = "Exporting textures...";
                await Task.Yield();
                string? texSummary = await ExportZipTextures(zipPathList, outputDir, _exportOptions.TextureFormat);

                IsLoading = false;
                LoadingStatus = "";

                string formatLabel = format == FbxExportFormat.Binary ? "Binary" : "ASCII";
                string content = $"Written {fileCount} FBX {formatLabel} file(s) to:\n{outputDir}";
                if (texSummary != null)
                    content += $"\n\nTextures:\n{texSummary}";

                await new ContentDialog
                {
                    Title = "Export Complete",
                    Content = content,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowError($"FBX export failed.\n\nError: {ex.Message}\n\n{ex.InnerException?.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadingStatus = "";
            }
        }

        // Produces a filesystem-safe, unique base file name.
        private static string UniqueFileName(HashSet<string> used, string? name)
        {
            string baseName = string.IsNullOrWhiteSpace(name) ? "model" : name!;
            foreach (char c in Path.GetInvalidFileNameChars())
                baseName = baseName.Replace(c, '_');

            string candidate = baseName;
            int n = 1;
            while (!used.Add(candidate))
                candidate = $"{baseName}_{n++}";
            return candidate;
        }

        private IEnumerable<ModelBinExportData> CollectModelBinExportData(
            IEnumerable<IViewerNode> roots)
        {
            foreach (var root in roots)
                foreach (var data in CollectModelBinExportDataRecursive(root))
                    yield return data;
        }

        private IEnumerable<ModelBinExportData> CollectModelBinExportDataRecursive(
            IViewerNode node)
        {
            if (node is ModelBinNode modelBin)
            {
                var meshes = modelBin.Children
                    .OfType<MeshNode>()
                    .Where(m => m.GeometryData != null)
                    .Select(m => (Name: m.Name, Data: m.GeometryData!))
                    .Where(m => _exportOptions.ShouldExportMesh(m.Name, m.Data.SourceMesh))
                    .ToList();

                yield return new ModelBinExportData(
                    modelBin.Name ?? modelBin.FileName ?? "model",
                    modelBin.Bundle,
                    meshes);
            }

            foreach (var child in node.Children)
                foreach (var data in CollectModelBinExportDataRecursive(child))
                    yield return data;
        }
    }

}

