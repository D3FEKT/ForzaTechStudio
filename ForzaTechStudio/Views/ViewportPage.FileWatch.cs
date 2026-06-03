using ForzaTechStudio.Helpers;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Specialized;
using System.IO;

namespace ForzaTechStudio.Views
{
    // External file change detection for loaded viewport files.
    public sealed partial class ViewportPage : Page
    {
        private FileChangeWatcher? _fileWatcher;
        private bool _changeDialogShowing;

        private void InitFileWatch()
        {
            if (_fileWatcher != null) return;
            _fileWatcher = new FileChangeWatcher(this.DispatcherQueue);
            _fileWatcher.FileChanged += OnFileChangedExternally;
            ViewModel.Roots.CollectionChanged += FileWatch_RootsCollectionChanged;
        }

        private void DisposeFileWatch()
        {
            ViewModel.Roots.CollectionChanged -= FileWatch_RootsCollectionChanged;
            _fileWatcher?.UnwatchAll();
        }

        private void FileWatch_RootsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (IViewerNode n in e.NewItems)
                {
                    var p = GetReloadablePathForRoot(n);
                    if (!string.IsNullOrEmpty(p)) _fileWatcher?.Watch(p);
                }

            if (e.OldItems != null)
                foreach (IViewerNode n in e.OldItems)
                {
                    var p = GetReloadablePathForRoot(n);
                    if (!string.IsNullOrEmpty(p)) _fileWatcher?.Unwatch(p);
                }
        }

        private async void OnFileChangedExternally(string path)
        {

            if (_changeDialogShowing || IsLoading) return;
            _changeDialogShowing = true;
            try
            {
                var fileName = Path.GetFileName(path);
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
                    await ReloadRootByPathAsync(path);
            }
            finally
            {
                _changeDialogShowing = false;
            }
        }

        // Reload a specific root identified by its file path.
        private async System.Threading.Tasks.Task ReloadRootByPathAsync(string path)
        {
            // Find the matching root
            IViewerNode? target = null;
            foreach (var r in ViewModel.Roots)
            {
                if (string.Equals(GetReloadablePathForRoot(r), path, StringComparison.OrdinalIgnoreCase))
                {
                    target = r;
                    break;
                }
            }
            if (target == null || string.IsNullOrEmpty(path) || !File.Exists(path)) return;


            ViewModel_RequestCloseRoot(this, target);
            ViewModel.RemoveRoot(target);
            await ProcessDroppedFilesAsync(new System.Collections.Generic.List<string> { path });
        }
    }
}
