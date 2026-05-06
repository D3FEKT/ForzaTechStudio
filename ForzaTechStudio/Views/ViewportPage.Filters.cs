using ForzaTechStudio.ViewModels.ThreeDViewer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace ForzaTechStudio.Views
{
    // Filter state item for the Materials popup
    public class MaterialFilterItem : INotifyPropertyChanged
    {
        public string? MaterialName { get; set; }

        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // Filter state item for the Objects popup
    public class ObjectFilterItem : INotifyPropertyChanged
    {
        public string? Name { get; set; }
        public string? TypeLabel { get; set; }
        public IViewerNode? Node { get; set; }

        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed partial class ViewportPage
    {
        // Session-persistent filter state
        private readonly List<MaterialFilterItem> _materialFilters = new();
        private readonly List<ObjectFilterItem> _objectFilters = new();

        // Click handlers wired up from XAML

        private async void Materials_Click(object sender, RoutedEventArgs e)
            => await ShowMaterialFilterAsync();

        private async void Objects_Click(object sender, RoutedEventArgs e)
            => await ShowObjectFilterAsync();

        // Materials filter dialog

        private async Task ShowMaterialFilterAsync()
        {
            RefreshMaterialFilters();

            if (_materialFilters.Count == 0)
            {
                var empty = new ContentDialog
                {
                    Title = "Materials",
                    Content = "No materials are currently loaded.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };
                await empty.ShowAsync();
                return;
            }

            // Search bar
            var searchBox = new TextBox
            {
                PlaceholderText = "Search materials�",
                Margin = new Thickness(0, 0, 0, 6)
            };

            // Virtualized list � only visible rows are realized, keeping memory low for large files
            var listView = new ListView
            {
                ItemsSource = _materialFilters,
                ItemTemplate = (DataTemplate)Resources["MaterialFilterItemTemplate"],
                SelectionMode = ListViewSelectionMode.None,
                IsItemClickEnabled = false,
                MaxHeight = 450,
                MinWidth = 320
            };

            List<MaterialFilterItem> CurrentMaterialView() =>
                string.IsNullOrWhiteSpace(searchBox.Text)
                    ? _materialFilters
                    : _materialFilters.Where(m => m.MaterialName?.Contains(searchBox.Text, StringComparison.OrdinalIgnoreCase) == true).ToList();

            searchBox.TextChanged += (s, e) => listView.ItemsSource = CurrentMaterialView();

            // Select All / Deselect All act on the current search view
            var selectAllBtn = new HyperlinkButton { Content = "Select All" };
            var deselectAllBtn = new HyperlinkButton { Content = "Deselect All" };
            selectAllBtn.Click += (s, e) => { foreach (var item in CurrentMaterialView()) item.IsChecked = true; };
            deselectAllBtn.Click += (s, e) => { foreach (var item in CurrentMaterialView()) item.IsChecked = false; };

            var headerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(0, 0, 0, 4)
            };
            headerRow.Children.Add(selectAllBtn);
            headerRow.Children.Add(deselectAllBtn);

            var contentPanel = new StackPanel { MinWidth = 320 };
            contentPanel.Children.Add(searchBox);
            contentPanel.Children.Add(headerRow);
            contentPanel.Children.Add(listView);

            var dialog = new ContentDialog
            {
                Title = "Materials",
                Content = contentPanel,
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // IsChecked values are already updated via two-way binding
                ApplyMaterialFilter();
            }
        }

        private void RefreshMaterialFilters()
        {
            var found = new HashSet<string>();
            foreach (var root in ViewModel.Roots)
                CollectMaterialNamesRecursive(root, found);

            // Remove entries that no longer have corresponding loaded nodes
            _materialFilters.RemoveAll(f => !found.Contains(f.MaterialName!));

            // Add newly discovered materials (default: visible)
            var existing = _materialFilters.Select(f => f.MaterialName).ToHashSet();
            foreach (var mat in found.Where(m => !existing.Contains(m)).OrderBy(m => m))
                _materialFilters.Add(new MaterialFilterItem { MaterialName = mat, IsChecked = true });

            // Keep list sorted alphabetically
            _materialFilters.Sort((a, b) =>
                string.Compare(a.MaterialName, b.MaterialName, System.StringComparison.OrdinalIgnoreCase));
        }

        private static void CollectMaterialNamesRecursive(IViewerNode node, HashSet<string> names)
        {
            if (node is MeshNode mesh && !string.IsNullOrEmpty(mesh.GeometryData?.MaterialName))
                names.Add(mesh.GeometryData.MaterialName);

            foreach (var child in node.Children)
                CollectMaterialNamesRecursive(child, names);
        }

        private void ApplyMaterialFilter()
        {
            // Reset visibility to LOD-based state first so re-enabling a material restores meshes
            UpdateLODVisibility();

            // Mask out meshes whose material is unchecked
            foreach (var root in ViewModel.Roots)
                ApplyMaterialFilterRecursive(root);
        }

        private void ApplyMaterialFilterRecursive(IViewerNode node)
        {
            if (node is MeshNode mesh && mesh.IsChecked == true)
            {
                var matName = mesh.GeometryData?.MaterialName ?? string.Empty;
                var filter = _materialFilters.FirstOrDefault(f => f.MaterialName == matName);
                if (filter != null && !filter.IsChecked)
                    mesh.IsChecked = false;
            }

            foreach (var child in node.Children)
                ApplyMaterialFilterRecursive(child);
        }

        // Objects filter dialog

        private async Task ShowObjectFilterAsync()
        {
            RefreshObjectFilters();

            if (_objectFilters.Count == 0)
            {
                var empty = new ContentDialog
                {
                    Title = "Objects",
                    Content = "No objects are currently loaded.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };
                await empty.ShowAsync();
                return;
            }

            // Search bar
            var searchBox = new TextBox
            {
                PlaceholderText = "Search objects�",
                Margin = new Thickness(0, 0, 0, 6)
            };

            // Virtualized list 
            var listView = new ListView
            {
                ItemsSource = _objectFilters,
                ItemTemplate = (DataTemplate)Resources["ObjectFilterItemTemplate"],
                SelectionMode = ListViewSelectionMode.None,
                IsItemClickEnabled = false,
                MaxHeight = 450,
                MinWidth = 320
            };

            List<ObjectFilterItem> CurrentObjectView() =>
                string.IsNullOrWhiteSpace(searchBox.Text)
                    ? _objectFilters
                    : _objectFilters.Where(o => o.Name?.Contains(searchBox.Text, StringComparison.OrdinalIgnoreCase) == true
                                             || o.TypeLabel?.Contains(searchBox.Text, StringComparison.OrdinalIgnoreCase) == true).ToList();

            searchBox.TextChanged += (s, e) => listView.ItemsSource = CurrentObjectView();

            // Select All / Deselect All act on the current search view
            var selectAllBtn = new HyperlinkButton { Content = "Select All" };
            var deselectAllBtn = new HyperlinkButton { Content = "Deselect All" };
            selectAllBtn.Click += (s, e) => { foreach (var item in CurrentObjectView()) item.IsChecked = true; };
            deselectAllBtn.Click += (s, e) => { foreach (var item in CurrentObjectView()) item.IsChecked = false; };

            var headerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(0, 0, 0, 4)
            };
            headerRow.Children.Add(selectAllBtn);
            headerRow.Children.Add(deselectAllBtn);

            var contentPanel = new StackPanel { MinWidth = 320 };
            contentPanel.Children.Add(searchBox);
            contentPanel.Children.Add(headerRow);
            contentPanel.Children.Add(listView);

            var dialog = new ContentDialog
            {
                Title = "Objects",
                Content = contentPanel,
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // IsChecked values are already updated via two-way binding
                ApplyObjectFilter();
            }
        }

        private void RefreshObjectFilters()
        {
            var found = new List<(IViewerNode Node, string TypeLabel)>();
            foreach (var root in ViewModel.Roots)
                CollectObjectNodesRecursive(root, found);

            // Remove stale entries
            var activeSet = found.Select(x => x.Node).ToHashSet();
            _objectFilters.RemoveAll(f => !activeSet.Contains(f.Node!));

            // Add newly discovered nodes (default: match current visibility)
            var existingNodes = _objectFilters.Select(f => f.Node).ToHashSet();
            foreach (var (node, typeLabel) in found)
            {
                if (!existingNodes.Contains(node))
                {
                    _objectFilters.Add(new ObjectFilterItem
                    {
                        Name = node.Name ?? "Unnamed",
                        TypeLabel = typeLabel,
                        Node = node,
                        IsChecked = node.IsChecked != false
                    });
                }
            }
        }

        private static void CollectObjectNodesRecursive(IViewerNode node, List<(IViewerNode, string)> results)
        {
            switch (node)
            {
                case MeshNode:
                    results.Add((node, "Mesh"));
                    break;
                case LightGroupNode:
                    results.Add((node, "Light"));
                    break;
                case LocatorNode:
                    results.Add((node, "Locator"));
                    break;
                case AvPinNode:
                    results.Add((node, "AvPin"));
                    break;
            }

            foreach (var child in node.Children)
                CollectObjectNodesRecursive(child, results);
        }

        private void ApplyObjectFilter()
        {
            foreach (var item in _objectFilters)
            {
                if (item.Node != null)
                    item.Node.IsChecked = item.IsChecked;
            }
        }
    }
}
