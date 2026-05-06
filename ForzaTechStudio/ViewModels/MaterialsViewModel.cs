using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using ForzaTechStudio.Services;

namespace ForzaTechStudio.ViewModels
{
    public partial class MaterialsViewModel : ObservableObject
    {
        private readonly MaterialExtractionService _extractionService = new();

        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _selectedGameId = ForzaGameCatalog.GetPreferredMaterialLibraryGameId();

        [ObservableProperty]
        private string _selectedLibraryFileName = string.Empty;

        public ObservableCollection<MaterialDisplayItem> Materials { get; } = new();
        public ObservableCollection<ForzaGameDefinition> AvailableGames { get; } = new();

        public MaterialsViewModel()
        {
            foreach (var game in ForzaGameCatalog.MaterialLibraryGames)
            {
                AvailableGames.Add(game);
            }

            LoadExistingMaterials();
        }

        partial void OnSelectedGameIdChanged(string value)
        {
            LoadExistingMaterials();
        }

        private void LoadExistingMaterials()
        {
            try
            {
                var data = MaterialLibrary.LoadEntries(SelectedGameId, out string resolvedPath, includeLegacyFallback: true);
                SelectedLibraryFileName = Path.GetFileName(resolvedPath);

                Materials.Clear();
                foreach (var kvp in data.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Materials.Add(new MaterialDisplayItem
                    {
                        Name = kvp.Key,
                        DataSize = kvp.Value?.MaterialBlob?.Length / 3 + " bytes"
                    });
                }

                StatusMessage = data.Count > 0
                    ? $"Loaded {data.Count} materials from {SelectedLibraryFileName}."
                    : $"No materials saved for {ForzaGameCatalog.GetDisplayName(SelectedGameId)} yet.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load error: {ex.Message}");
                StatusMessage = $"Failed to load materials: {ex.Message}";
            }
        }

        [RelayCommand]
        public void RefreshMaterials()
        {
            StatusMessage = $"Refreshing {ForzaGameCatalog.GetDisplayName(SelectedGameId)} library...";
            LoadExistingMaterials();
        }

        [RelayCommand]
        public async Task ExtractMaterialsAsync()
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.FileTypeFilter.Add(".modelbin");
            picker.FileTypeFilter.Add(".zip");

            var files = await picker.PickMultipleFilesAsync();
            if (files.Count > 0)
            {
                IsBusy = true;
                StatusMessage = "Extracting Materials...";

                var filePaths = new System.Collections.Generic.List<string>();
                foreach (var f in files) filePaths.Add(f.Path);

                int count = await _extractionService.ExtractMaterialsAsync(filePaths, SelectedGameId);

                StatusMessage = $"Extracted {count} materials into {ForzaGameCatalog.GetMaterialLibraryFileName(SelectedGameId)}.";
                LoadExistingMaterials();
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void OpenMaterialsFolder()
        {
            var folderPath = MaterialLibrary.MaterialsDirectoryPath;
            MaterialLibrary.LoadEntries(SelectedGameId, out string filePath, includeLegacyFallback: true);

            if (File.Exists(filePath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else if (Directory.Exists(folderPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", folderPath);
            }
        }
    }

    public class MaterialDisplayItem
    {
        public string Name { get; set; }
        public string DataSize { get; set; }
    }
}