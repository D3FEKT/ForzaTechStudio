using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.Linq;

namespace ForzaTechStudio.Views
{
    public sealed partial class BXMLEditorPage : Page
    {
        public BXMLEditorViewModel ViewModel { get; } = new BXMLEditorViewModel();

        public BXMLEditorPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BXMLEditorViewModel.BrowserTreeVersion))
                RebuildEntryTree();
            else if (e.PropertyName == nameof(BXMLEditorViewModel.ObjectModelTreeVersion))
                RebuildObjectModelTree();
        }

        private void RebuildEntryTree()
        {
            EntryTree.RootNodes.Clear();

            bool expandForSearch = !string.IsNullOrWhiteSpace(ViewModel.NodeSearchText);
            foreach (var item in ViewModel.FilteredBrowserRoots)
                EntryTree.RootNodes.Add(BuildEntryTreeNode(item, expandForSearch));
        }

        private static TreeViewNode BuildEntryTreeNode(BXMLBrowserItemViewModel item, bool expandForSearch)
        {
            var node = new TreeViewNode
            {
                Content = item,
                IsExpanded = expandForSearch || item.Depth < 2,
            };

            foreach (var child in item.Children)
                node.Children.Add(BuildEntryTreeNode(child, expandForSearch));

            return node;
        }

        private void EntryTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode treeNode && treeNode.Content is BXMLBrowserItemViewModel item)
            {
                ViewModel.SelectedBrowserItem = item;
                return;
            }

            if (args.InvokedItem is BXMLBrowserItemViewModel directItem)
                ViewModel.SelectedBrowserItem = directItem;
        }

        // ObjectModelGame navigator (groups / files) 

        private void RebuildObjectModelTree()
        {
            ObjectModelTree.RootNodes.Clear();

            bool expandForSearch = !string.IsNullOrWhiteSpace(ViewModel.ObjectModelSearchText);
            foreach (var root in ViewModel.FilteredObjectModelRoots)
                ObjectModelTree.RootNodes.Add(BuildObjectModelTreeNode(root, expandForSearch));
        }

        private static TreeViewNode BuildObjectModelTreeNode(ObjectModelNavNodeViewModel item, bool expandForSearch)
        {
            var node = new TreeViewNode
            {
                Content = item,
                IsExpanded = expandForSearch || (!item.IsFile && item.Depth < 2),
            };

            foreach (var child in item.Children)
                node.Children.Add(BuildObjectModelTreeNode(child, expandForSearch));

            return node;
        }

        private void ObjectModelTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode treeNode && treeNode.Content is ObjectModelNavNodeViewModel item)
            {
                ViewModel.SelectObjectModelNavNode(item);
                return;
            }

            if (args.InvokedItem is ObjectModelNavNodeViewModel directItem)
                ViewModel.SelectObjectModelNavNode(directItem);
        }

        // Attribute remove button (code-behind since template can't x:Bind
        //    back to the page ViewModel for the remove command) 

        private void RemoveAttribute_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BXMLAttributeViewModel attr)
                ViewModel.RemoveAttributeCommand.Execute(attr);
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
            var files = items.OfType<Windows.Storage.StorageFile>().ToList();

            // Check for ZIP first
            var zipFile = files.FirstOrDefault(f =>
                f.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zipFile != null)
            {
                await ViewModel.LoadZipAsync(zipFile.Path);
                return;
            }

            // Then BXML / XML
            var xmlFile = files.FirstOrDefault(f =>
                f.Path.EndsWith(".bxml", StringComparison.OrdinalIgnoreCase) ||
                f.Path.EndsWith(".xml",  StringComparison.OrdinalIgnoreCase));

            if (xmlFile != null)
                await ViewModel.LoadFileAsync(xmlFile.Path);
        }
    }
}
