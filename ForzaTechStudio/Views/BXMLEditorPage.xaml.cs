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

        // ViewModel → TreeView sync

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BXMLEditorViewModel.TreeRoot))
                RebuildTreeView();

            if (e.PropertyName is nameof(BXMLEditorViewModel.SelectedNode)
                               or nameof(BXMLEditorViewModel.IsNodeSelected))
                UpdateNodeDetailPanel();
        }

        private void RebuildTreeView()
        {
            BXMLTree.RootNodes.Clear();

            if (ViewModel.TreeRoot is { } root)
            {
                var node = BuildTreeNode(root, depth: 0);
                BXMLTree.RootNodes.Add(node);
            }
        }

        private static TreeViewNode BuildTreeNode(BXMLNodeViewModel vm, int depth)
        {
            var node = new TreeViewNode
            {
                Content    = vm,
                IsExpanded = depth < 2,
            };
            foreach (var child in vm.Children)
                node.Children.Add(BuildTreeNode(child, depth + 1));
            return node;
        }

        private void UpdateNodeDetailPanel()
        {
            var node = ViewModel.SelectedNode;
            if (node == null)
            {
                // Hide the no-attrs placeholder and children info — the whole panel 
                // is already hidden via IsNodeNotSelected Visibility binding.
                return;
            }

            // Update the children info text (ChildrenInfoText is a named TextBlock)
            ChildrenInfoText.Text = node.Children.Count == 0
                ? "This element has no child elements."
                : node.Children.Count == 1
                    ? $"1 child element"
                    : $"{node.Children.Count} child elements";

            // Toggle the no-attrs placeholder visibility
            NoAttrsPlaceholder.Visibility = node.Attributes.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // TreeView item invoked

        private void BXMLTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode tvn && tvn.Content is BXMLNodeViewModel vm)
                ViewModel.SelectedNode = vm;
        }

        // Attribute remove button (code-behind since template can't x:Bind
        //    back to the page ViewModel for the remove command) ──────────────────

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
