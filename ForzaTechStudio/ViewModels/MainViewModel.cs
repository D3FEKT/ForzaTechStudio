using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTechStudio.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace ForzaTechStudio.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private FileService _fileService;
        private nint _windowHandle;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage;

        public bool IsInitialized => _fileService != null;

        public event Action<Type> NavigationRequested;

        public void Initialize(nint windowHandle)
        {
            _windowHandle = windowHandle;
            _fileService = new FileService(windowHandle);
        }

        [RelayCommand]
        public void CreateZip()
        {
            NavigationRequested?.Invoke(typeof(Views.CreateZipPage));
        }

        [RelayCommand]
        public void CreateModelBin()
        {
            NavigationRequested?.Invoke(typeof(Views.ModelBinCreatorPage));
        }

        [RelayCommand]
        private void NavigateTo(string pageName)
        {
            Type pageType = pageName switch
            {
                "ModelViewerPage" => typeof(Views.ViewportPage),
                "CreateModelBinPage" => typeof(Views.ModelBinCreatorPage),
                "ModelBinEditorPage" => typeof(Views.ModelBinEditorPage),
                "CarbinEditorPage" => typeof(Views.CarbinEditorPage),
                "CreateZipPage" => typeof(Views.CreateZipPage),
                "MaterialsPage" => typeof(Views.MaterialsPage),
                "MaterialsAndShadersPage" => typeof(Views.MaterialsAndShadersPage),
                "SwatchbinEditorPage" => typeof(Views.SwatchbinEditorPage),
                "ManufacturerColorsPage" => typeof(Views.ManufacturerColorsPage),
                "PhysicsDefinitionPage" => typeof(Views.PhysicsDefinitionPage),
                "ConversionToolPage" => typeof(Views.ConversionToolPage),
                "SetupPage" => typeof(Views.SetupPage),
                "LightsPage" => typeof(Views.LightsPage),
                "FxbEditorPage" => typeof(Views.FxbEditorPage),
                "BXMLEditorPage" => typeof(Views.BXMLEditorPage),
                "StringTablesPage" => typeof(Views.StringTablesPage),
                "DocumentationPage" => typeof(Views.DocumentationPage),
                _ => null
            };

            if (pageType != null)
            {
                NavigationRequested?.Invoke(pageType);
            }
        }

        private async Task ShowErrorDialog(string message)
        {
            if (App.MainWindow?.Content?.XamlRoot == null) return;

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Notification",
                Content = message,
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }
    }
}