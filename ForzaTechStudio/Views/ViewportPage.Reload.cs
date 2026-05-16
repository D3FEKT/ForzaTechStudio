using ForzaTechStudio.ViewModels.ThreeDViewer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ForzaTechStudio.Views
{
    
    public sealed partial class ViewportPage : Page
    {
        // Click handler for "Reload All" in the toolbar flyout
        private async void ReloadAll_Click(object sender, RoutedEventArgs e)
        {
            CloseAnyOpenFlyout(sender);

            var paths = GetReloadablePathsForRoots(ViewModel.Roots.ToList());
            if (paths.Count == 0) return;

            // Properly clean up all roots (removes tree nodes, render objects, unsubscribes events)
            foreach (var root in ViewModel.Roots.ToList())
            {
                ViewModel_RequestCloseRoot(this, root);
                ViewModel.RemoveRoot(root);
            }

            await ProcessDroppedFilesAsync(paths);
        }

        // Click handler for a single file entry in the reload flyout
        private async void ReloadOne_Click(object sender, RoutedEventArgs e)
        {
            CloseAnyOpenFlyout(sender);

            if (sender is not Button btn || btn.Tag is not IViewerNode node) return;

            var path = GetReloadablePathForRoot(node);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            // Properly clean up this root before reloading
            ViewModel_RequestCloseRoot(this, node);
            ViewModel.RemoveRoot(node);
            await ProcessDroppedFilesAsync(new List<string> { path });
        }

         
        private static string GetReloadablePathForRoot(IViewerNode node)
        {
            return node switch
            {
                ZipNode z => z.FilePath,
                ModelBinNode m => m.FilePath,
                LightsBinNode l => l.FilePath,
                LocatorsXmlNode x => x.FilePath,
                GrannyFileNode g => g.FilePath,
                _ => null
            };
        }

        private static List<string> GetReloadablePathsForRoots(IEnumerable<IViewerNode> roots)
        {
            var list = new List<string>();
            foreach (var r in roots)
            {
                var p = GetReloadablePathForRoot(r);
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) list.Add(p);
            }
            return list;
        }

        private static void CloseAnyOpenFlyout(object sender)
        {
            if (sender is FrameworkElement fe)
            {
                var parent = fe.Parent;
                while (parent is FrameworkElement p && p is not FlyoutPresenter)
                    parent = p.Parent;
                if (parent is FlyoutPresenter fp && fp.Parent is Microsoft.UI.Xaml.Controls.Primitives.Popup popup)
                    popup.IsOpen = false;
            }
        }

        // Ctrl+A: select all visible meshes across all modelbins and highlight them
        private void SelectAllVisibleMeshes_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;

            var visibleMeshes = new List<MeshNode>();
            foreach (var root in ViewModel.Roots)
            {
                CollectVisibleMeshes(root, visibleMeshes);
            }

            if (visibleMeshes.Count == 0)
            {
                ClearMultiSelection();
                return;
            }

            _multiSelectedMeshes.Clear();
            _multiSelectedMeshes.AddRange(visibleMeshes);
            _isMultiSelectActive = true;
            SnapshotMultiSelectValues();
            UpdateHighlight(_multiSelectedMeshes);
            UpdateTransformUIForMultiSelect();
        }

        private static void CollectVisibleMeshes(IViewerNode node, List<MeshNode> result)
        {
            if (node == null) return;
            // Only traverse into nodes whose visibility is on
            if (node.IsChecked == false) return;
            if (node is MeshNode m && m.IsChecked == true)
            {
                result.Add(m);
                return;
            }
            foreach (var c in node.Children)
                CollectVisibleMeshes(c, result);
        }
    }
}
