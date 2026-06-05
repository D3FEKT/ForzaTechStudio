using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;

namespace ForzaTechStudio.Views
{
    // Tab management for the 3D Viewport
    public sealed partial class ViewportPage : Page
    {
        private bool _isSwitchingTabs;

        // Switch visible content from one tab to another
        private void SwitchTab(ViewportTab? oldTab, ViewportTab? newTab)
        {
            // Hide old tab geometry and remove its tree nodes
            if (oldTab != null)
            {
                foreach (var root in oldTab.Roots)
                    SetTabNodeVisible(root, false);
                foreach (var root in oldTab.Roots)
                    if (_treeNodeMap.TryGetValue(root, out var tv))
                        FileTree.RootNodes.Remove(tv);
            }

            ViewModel.ActiveTab = newTab;
            ViewModel.Roots.Clear();

            // Show new tab geometry and add its tree nodes
            if (newTab != null)
            {
                foreach (var root in newTab.Roots)
                    ViewModel.Roots.Add(root);
                foreach (var root in newTab.Roots)
                {
                    if (_treeNodeMap.TryGetValue(root, out var tv))
                        FileTree.RootNodes.Add(tv);
                    SetTabNodeVisible(root, true);
                }
            }
        }

        // Toggle 3D visibility for a node tree without destroying render objects
        private void SetTabNodeVisible(IViewerNode node, bool tabVisible)
        {
            var vis = tabVisible && node.IsChecked != false ? Visibility.Visible : Visibility.Collapsed;
            bool isVisible = vis == Visibility.Visible;

            if (_renderMap.TryGetValue(node, out var model))
                SetRenderElementVisible(model, isVisible);

            if (node is DamageMeshNode dmg && _damageRenderMap.TryGetValue(dmg, out var dmgModel))
                SetRenderElementVisible(dmgModel, isVisible);

            if (node is LightGroupNode lg && _lightDamageRenderMap.TryGetValue(lg, out var lgDmg))
                SetRenderElementVisible(lgDmg, isVisible);

            if (node is SkeletonNode skel && _skeletonRenderMap.TryGetValue(skel, out var skelElems))
                foreach (var el in skelElems)
                    SetRenderElementVisible(el, isVisible);

            if (node is CarbinModelNode carbinModel && _carbinRenderMap.TryGetValue(carbinModel, out var carbinElems))
                foreach (var el in carbinElems)
                    SetRenderElementVisible(el, isVisible);

            foreach (var child in node.Children)
                SetTabNodeVisible(child, tabVisible);
        }

        private void TabListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSwitchingTabs) return;
            var oldTab = e.RemovedItems.Count > 0 ? e.RemovedItems[0] as ViewportTab : null;
            var newTab = e.AddedItems.Count > 0 ? e.AddedItems[0] as ViewportTab : null;
            if (newTab == null) return;
            SwitchTab(oldTab, newTab);
        }

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            int num = ViewModel.Tabs.Count + 1;
            var tab = ViewModel.AddTab($"Tab {num}");
            var prevActive = ViewModel.ActiveTab;
            _isSwitchingTabs = true;
            TabListView.SelectedItem = tab;
            _isSwitchingTabs = false;
            SwitchTab(prevActive, tab);
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ViewportTab tab) return;

            bool wasActive = tab == ViewModel.ActiveTab;
            int idx = ViewModel.Tabs.IndexOf(tab);

            _isSwitchingTabs = true;
            try
            {
                // If closing active tab, switch to adjacent tab first
                if (wasActive)
                {
                    ViewportTab? nextTab = ViewModel.Tabs.Count > 1
                        ? ViewModel.Tabs[idx > 0 ? idx - 1 : 1]
                        : null;

                    if (nextTab != null)
                        SwitchTab(tab, nextTab);
                    else
                    {
                        // Last tab: hide geometry and clear tree
                        foreach (var root in tab.Roots)
                        {
                            SetTabNodeVisible(root, false);
                            if (_treeNodeMap.TryGetValue(root, out var tv))
                                FileTree.RootNodes.Remove(tv);
                        }
                        ViewModel.ActiveTab = null;
                        ViewModel.Roots.Clear();
                    }
                }

                // Dispose all roots in the closing tab
                foreach (var root in tab.Roots.ToList())
                {
                    ViewModel_RequestCloseRoot(this, root);
                    ViewModel.RemoveRoot(root);
                }

                ViewModel.Tabs.Remove(tab);

                // Always keep at least one tab
                if (ViewModel.Tabs.Count == 0)
                {
                    var newTab = ViewModel.AddTab("Tab 1");
                    ViewModel.ActiveTab = newTab;
                }

                TabListView.SelectedItem = ViewModel.ActiveTab;
            }
            finally
            {
                _isSwitchingTabs = false;
            }
        }
    }
}
