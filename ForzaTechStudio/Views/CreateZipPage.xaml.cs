using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ForzaTechStudio.Views
{
    public class ExplorerItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate FolderTemplate { get; set; } = null!;
        public DataTemplate FileTemplate { get; set; } = null!;

        protected override DataTemplate SelectTemplateCore(object item)
        {
            // In RootNodes mode, WinUI passes the TreeViewNode — unwrap to get the ZipItem
            if (item is TreeViewNode node)
                item = node.Content;

            if (item is ZipItem explorerItem)
                return explorerItem.Type == "Folder" ? FolderTemplate : FileTemplate;

            return base.SelectTemplateCore(item);
        }
    }

    public sealed partial class CreateZipPage : Page
    {
        public CreateZipViewModel ViewModel { get; } = new CreateZipViewModel();

        public CreateZipPage()
        {
            this.InitializeComponent();
            this.DataContext = ViewModel;

            // Handles build-mode individual adds and session resets
            ViewModel.Items.CollectionChanged += Items_CollectionChanged;

            // When loading finishes, batch-build all tree nodes in one pass with layout suppressed
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.IsLoading) && !ViewModel.IsLoading)
                    BatchRebuildTreeView();
            };
        }

        // Tree management

        // Rebuilds the entire tree from ViewModel.Items in one pass.
        // The TreeView is hidden during the rebuild to suppress per-node layout passes,
        // which is critical for large archives with thousands of entries.
        private void BatchRebuildTreeView()
        {
            FileTreeView.Visibility = Visibility.Collapsed;
            FileTreeView.RootNodes.Clear();

            var nodes = new List<TreeViewNode>(ViewModel.Items.Count);
            foreach (var item in ViewModel.Items)
                nodes.Add(CreateTreeNode(item));

            foreach (var node in nodes)
                FileTreeView.RootNodes.Add(node);

            FileTreeView.Visibility = Visibility.Visible;
        }

        private void Items_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                // Items.Clear() called (e.g. CreateNewSession or start of LoadZipFileAsync)
                FileTreeView.RootNodes.Clear();
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
                     && !ViewModel.IsLoading)
            {
                // Build-mode individual file/folder adds — IsLoading is false so we add directly.
                // During archive loading IsLoading is true, so we skip here and let
                // BatchRebuildTreeView (triggered by IsLoading→false) handle it instead.
                foreach (ZipItem item in e.NewItems ?? Array.Empty<object>())
                    FileTreeView.RootNodes.Add(CreateTreeNode(item));
            }
        }

        private TreeViewNode CreateTreeNode(ZipItem item)
        {
            var node = new TreeViewNode { Content = item, IsExpanded = item.IsExpanded };
            foreach (var child in item.Children)
                node.Children.Add(CreateTreeNode(child));
            return node;
        }

        // Drag & drop

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();

            // Single zip/minizip dropped → open it
            if (items.Count == 1 && items[0] is Windows.Storage.StorageFile singleFile &&
                (singleFile.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                 singleFile.Path.EndsWith(".minizip", StringComparison.OrdinalIgnoreCase)))
            {
                await ViewModel.LoadZipFileAsync(singleFile.Path, singleFile.Name);
                return;
            }

            if (!ViewModel.IsBuildMode) return;

            foreach (var item in items)
            {
                if (item is Windows.Storage.StorageFolder folder)
                    await ViewModel.AddFolderItemAsync(folder.Name, folder.Path);
                else if (item is Windows.Storage.StorageFile file)
                    await ViewModel.AddFileItemAsync(file);
            }
        }

        // Context menu handlers

        private async void ExtractItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem &&
                menuItem.DataContext is TreeViewNode node &&
                node.Content is ZipItem zipItem)
            {
                await ViewModel.ExtractItemCommand.ExecuteAsync(zipItem);
            }
        }

        private async void ReplaceItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem &&
                menuItem.DataContext is TreeViewNode node &&
                node.Content is ZipItem zipItem)
            {
                await ViewModel.ReplaceItemCommand.ExecuteAsync(zipItem);
            }
        }
    }
}
