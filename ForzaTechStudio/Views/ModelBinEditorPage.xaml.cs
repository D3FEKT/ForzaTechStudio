using ForzaTechStudio.Helpers;
using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.UI.Xaml;
using System;

namespace ForzaTechStudio.Views
{
    public sealed partial class ModelBinEditorPage : Page
    {
        public ModelBinEditorViewModel ViewModel { get; private set; }

        private FileChangeWatcher? _fileWatcher;
        private bool _changeDialogShowing;

        public ModelBinEditorPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var fileService = new FileService(hWnd);
            ViewModel = new ModelBinEditorViewModel(fileService);

            _fileWatcher = new FileChangeWatcher(this.DispatcherQueue);
            _fileWatcher.FileChanged += OnFileChangedExternally;

            ViewModel.LoadedFiles.CollectionChanged += LoadedFiles_CollectionChanged;
            ViewModel.FileSaved += path => _fileWatcher.Suppress(path);

            this.Unloaded += (_, _) => _fileWatcher.UnwatchAll();
        }

        private void LoadedFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (FileViewModel f in e.NewItems)
                    _fileWatcher?.Watch(f.FilePath);

            if (e.OldItems != null)
                foreach (FileViewModel f in e.OldItems)
                    _fileWatcher?.Unwatch(f.FilePath);
        }

        private async void OnFileChangedExternally(string path)
        {
            if (_changeDialogShowing) return;
            _changeDialogShowing = true;
            try
            {
                var fileName = System.IO.Path.GetFileName(path);
                var dialog = new ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = "File Changed",
                    Content = $"\"{fileName}\" was modified outside of ForzaTechStudio. Reload it?",
                    PrimaryButtonText = "Reload",
                    CloseButtonText = "Ignore"
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                    await ViewModel.ReloadFileByPathAsync(path);
            }
            finally
            {
                _changeDialogShowing = false;
            }
        }

        private void ModelBinTabClose_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is FileViewModel file)
                ViewModel.CloseSimpleFileCommand.Execute(file);
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
