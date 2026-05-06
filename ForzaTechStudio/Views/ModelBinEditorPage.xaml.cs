using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Linq;
using Microsoft.UI.Xaml;
using System;

namespace ForzaTechStudio.Views
{
    public sealed partial class ModelBinEditorPage : Page
    {
        public ModelBinEditorViewModel ViewModel { get; private set; }

        public ModelBinEditorPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            
            // Initialize ViewModel here so bindings work immediately
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var fileService = new FileService(hWnd);
            ViewModel = new ModelBinEditorViewModel(fileService);
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
                foreach (var item in items)
                {
                    if (item is Windows.Storage.StorageFile file && file.Path.EndsWith(".modelbin", StringComparison.OrdinalIgnoreCase))
                    {
                        await ViewModel.LoadFileAsync(file.Name, file.Path);
                    }
                }
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Handle file passed via navigation parameter
            if (e.Parameter is string filePath && !string.IsNullOrEmpty(filePath))
            {
                string name = System.IO.Path.GetFileName(filePath);
                await ViewModel.LoadFileAsync(name, filePath);
            }
        }
    }
}
