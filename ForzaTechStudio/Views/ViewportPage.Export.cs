using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
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

                // Build text content on a background thread.
                string? objContent = null;
                string? mtlContent = null;
                await Task.Run(() =>
                {
                    objContent = ObjExportService.BuildObjContent(modelList, mtlFileName);
                    mtlContent = ObjExportService.BuildMtlContent(modelList);
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

                // Hide the overlay before showing the success dialog.
                IsLoading = false;
                LoadingStatus = "";

                int meshCount = modelList.Sum(m => m.Meshes.Count);
                var dlg = new ContentDialog
                {
                    Title = "Export Complete",
                    Content = $"Written {meshCount} mesh(es) across {modelList.Count} model(s).\n\n" +
                              $"{outFile.Name}\n{mtlFileName}",
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

        // Helpers

        // Walks the viewer node tree and returns one <see cref="ModelBinExportData"/>
        // per <see cref="ModelBinNode"/> found (including those nested inside ZipNodes).
        private static IEnumerable<ModelBinExportData> CollectModelBinExportData(
            IEnumerable<IViewerNode> roots)
        {
            foreach (var root in roots)
                foreach (var data in CollectModelBinExportDataRecursive(root))
                    yield return data;
        }

        private static IEnumerable<ModelBinExportData> CollectModelBinExportDataRecursive(
            IViewerNode node)
        {
            if (node is ModelBinNode modelBin)
            {
                var meshes = modelBin.Children
                    .OfType<MeshNode>()
                    .Where(m => m.GeometryData != null)
                    .Select(m => (Name: m.Name, Data: m.GeometryData!))
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
