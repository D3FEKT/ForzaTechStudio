using ForzaTechStudio.Models;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ForzaTechStudio.Views
{
    // Export Options dialog: edits the shared _exportOptions used by all exports.
    public sealed partial class ViewportPage : Page
    {
        private readonly ExportOptions _exportOptions = new();

        private async void ExportOptions_Click(object sender, RoutedEventArgs e)
        {
            var partNames = CollectAllMeshNames(ViewModel.Roots);

            var draftUpAxis = _exportOptions.UpAxis;
            var draftScaleFactor = _exportOptions.ScaleFactor;
            var draftTextureFormat = _exportOptions.TextureFormat;
            var draftUvMode = _exportOptions.UvMode;
            var draftAllLods = _exportOptions.AllLods;
            var draftSelectedLods = new HashSet<int>(_exportOptions.SelectedLods);
            var draftIncludeBones = _exportOptions.IncludeBones;
            var draftIncludeVertexColors = _exportOptions.IncludeVertexColors;
            var draftMultiFileExport = _exportOptions.MultiFileExport;
            var draftExcludedParts = new HashSet<string>(_exportOptions.ExcludedParts, StringComparer.OrdinalIgnoreCase);

            while (true)
            {
                var upAxisCombo = new ComboBox
                {
                    Header = "Up-Axis",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    ItemsSource = new[] { "Y-Up", "Z-Up" },
                    SelectedIndex = draftUpAxis == ExportUpAxis.ZUp ? 1 : 0
                };

                var scaleBox = new NumberBox
                {
                    Header = "Scale Factor",
                    Value = draftScaleFactor,
                    SmallChange = 0.1,
                    LargeChange = 1,
                    Minimum = 0.0001,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
                };

                var texCombo = new ComboBox
                {
                    Header = "Texture Format",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    ItemsSource = new[] { "DDS", "PNG", "JPG", "TGA" },
                    SelectedIndex = (int)draftTextureFormat
                };

                var uvCombo = new ComboBox
                {
                    Header = "UV Channels",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    ItemsSource = new[] { "All Channels", "Primary Only", "None" },
                    SelectedIndex = (int)draftUvMode
                };

                var allLodsCheck = new CheckBox { Content = "All LODs", IsChecked = draftAllLods };
                var lodLabels = new[] { "LODS", "LOD0", "LOD1", "LOD2", "LOD3", "LOD4", "LOD5" };
                var lodValues = new[] { -1, 0, 1, 2, 3, 4, 5 };
                var lodChecks = new List<CheckBox>();

                var lodRow1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var lodRow2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                for (int i = 0; i < lodLabels.Length; i++)
                {
                    var cb = new CheckBox
                    {
                        Content = lodLabels[i],
                        Tag = lodValues[i],
                        MinWidth = 0,
                        IsChecked = draftSelectedLods.Contains(lodValues[i]),
                        IsEnabled = !draftAllLods
                    };
                    lodChecks.Add(cb);
                    (i < 4 ? lodRow1 : lodRow2).Children.Add(cb);
                }
                var lodPanel = new StackPanel { Spacing = 4 };
                lodPanel.Children.Add(lodRow1);
                lodPanel.Children.Add(lodRow2);
                allLodsCheck.Checked += (_, _) => lodChecks.ForEach(c => c.IsEnabled = false);
                allLodsCheck.Unchecked += (_, _) => lodChecks.ForEach(c => c.IsEnabled = true);

                var bonesCheck = new CheckBox { Content = "Include Bones (FBX only)", IsChecked = draftIncludeBones };
                var vertexColorCheck = new CheckBox { Content = "Include Vertex Colors", IsChecked = draftIncludeVertexColors };
                var multiFileCheck = new CheckBox { Content = "Multi-file export (one file per model)", IsChecked = draftMultiFileExport };

                void CaptureDraft()
                {
                    draftUpAxis = upAxisCombo.SelectedIndex == 1 ? ExportUpAxis.ZUp : ExportUpAxis.YUp;
                    draftScaleFactor = (float)(double.IsNaN(scaleBox.Value) ? 1.0 : scaleBox.Value);
                    draftTextureFormat = (ExportTextureFormat)Math.Max(0, texCombo.SelectedIndex);
                    draftUvMode = (ExportUvMode)Math.Max(0, uvCombo.SelectedIndex);
                    draftAllLods = allLodsCheck.IsChecked == true;

                    draftSelectedLods.Clear();
                    foreach (var cb in lodChecks.Where(c => c.IsChecked == true))
                        draftSelectedLods.Add((int)cb.Tag);

                    draftIncludeBones = bonesCheck.IsChecked == true;
                    draftIncludeVertexColors = vertexColorCheck.IsChecked == true;
                    draftMultiFileExport = multiFileCheck.IsChecked == true;
                }

                ContentDialog? dialog = null;
                bool openPartFilter = false;

                var partFilterBtn = new Button { HorizontalAlignment = HorizontalAlignment.Stretch };
                void UpdatePartButtonText() =>
                    partFilterBtn.Content = draftExcludedParts.Count == 0
                        ? $"Car Part Filter (all {partNames.Count} included)"
                        : $"Car Part Filter ({partNames.Count - draftExcludedParts.Count}/{partNames.Count} included)";
                UpdatePartButtonText();
                partFilterBtn.Click += (_, _) =>
                {
                    CaptureDraft();
                    openPartFilter = true;
                    dialog?.Hide();
                };

                var root = new StackPanel { Spacing = 12, MinWidth = 380 };
                root.Children.Add(upAxisCombo);
                root.Children.Add(scaleBox);
                root.Children.Add(texCombo);
                root.Children.Add(uvCombo);
                root.Children.Add(new TextBlock { Text = "LOD Selection", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                root.Children.Add(allLodsCheck);
                root.Children.Add(lodPanel);
                root.Children.Add(bonesCheck);
                root.Children.Add(vertexColorCheck);
                root.Children.Add(multiFileCheck);
                if (partNames.Count > 0)
                {
                    root.Children.Add(new TextBlock { Text = "Car Part Filter", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                    root.Children.Add(partFilterBtn);
                }

                dialog = new ContentDialog
                {
                    Title = "Export Options",
                    PrimaryButtonText = "Save",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot,
                    Content = new ScrollViewer
                    {
                        MaxHeight = 560,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = root
                    }
                };

                var result = await dialog.ShowAsync();

                if (openPartFilter)
                {
                    await ShowPartFilterDialog(partNames, draftExcludedParts);
                    continue;
                }

                if (result != ContentDialogResult.Primary)
                    return;

                CaptureDraft();

                _exportOptions.UpAxis = draftUpAxis;
                _exportOptions.ScaleFactor = draftScaleFactor;
                _exportOptions.TextureFormat = draftTextureFormat;
                _exportOptions.UvMode = draftUvMode;

                _exportOptions.AllLods = draftAllLods;
                _exportOptions.SelectedLods.Clear();
                if (!_exportOptions.AllLods)
                    foreach (var lod in draftSelectedLods)
                        _exportOptions.SelectedLods.Add(lod);

                _exportOptions.IncludeBones = draftIncludeBones;
                _exportOptions.IncludeVertexColors = draftIncludeVertexColors;
                _exportOptions.MultiFileExport = draftMultiFileExport;

                _exportOptions.ExcludedParts.Clear();
                foreach (var name in draftExcludedParts)
                    _exportOptions.ExcludedParts.Add(name);
                return;
            }
        }

        // Separate searchable, scrollable part picker. Mutates 'excluded' on OK.
        private async Task ShowPartFilterDialog(List<string> partNames, HashSet<string> excluded)
        {
            var orderedPartNames = partNames
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var workingExcluded = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);

            var searchBox = new TextBox
            {
                PlaceholderText = "Search parts...",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var helpText = new TextBlock
            {
                Text = "Checked parts are included in export.",
                TextWrapping = TextWrapping.WrapWholeWords
            };

            var statusText = new TextBlock();
            var listView = new ListView
            {
                Height = 360,
                SelectionMode = ListViewSelectionMode.Multiple,
                IsMultiSelectCheckBoxEnabled = true
            };

            bool rebuilding = false;

            void UpdateStatus()
            {
                statusText.Text = workingExcluded.Count == 0
                    ? $"All {orderedPartNames.Count} parts included"
                    : $"{orderedPartNames.Count - workingExcluded.Count}/{orderedPartNames.Count} included";
            }

            void Rebuild()
            {
                rebuilding = true;
                string q = searchBox.Text?.Trim() ?? string.Empty;
                var filtered = q.Length == 0
                    ? orderedPartNames
                    : orderedPartNames.Where(name => name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                listView.ItemsSource = filtered;
                listView.SelectedItems.Clear();
                foreach (var name in filtered)
                    if (!workingExcluded.Contains(name))
                        listView.SelectedItems.Add(name);

                rebuilding = false;
                UpdateStatus();
            }

            searchBox.TextChanged += (_, _) => Rebuild();
            listView.SelectionChanged += (_, e) =>
            {
                if (rebuilding) return;

                foreach (var name in e.AddedItems.OfType<string>())
                    workingExcluded.Remove(name);
                foreach (var name in e.RemovedItems.OfType<string>())
                    workingExcluded.Add(name);

                UpdateStatus();
            };

            var selectAllBtn = new Button { Content = "Select All" };
            var deselectAllBtn = new Button { Content = "Deselect All" };
            selectAllBtn.Click += (_, _) =>
            {
                workingExcluded.Clear();
                Rebuild();
            };
            deselectAllBtn.Click += (_, _) =>
            {
                workingExcluded.Clear();
                foreach (var name in orderedPartNames)
                    workingExcluded.Add(name);
                Rebuild();
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(selectAllBtn);
            buttons.Children.Add(deselectAllBtn);

            Rebuild();

            var panel = new StackPanel { Spacing = 8, MinWidth = 380 };
            panel.Children.Add(searchBox);
            panel.Children.Add(helpText);
            panel.Children.Add(statusText);
            panel.Children.Add(buttons);
            panel.Children.Add(listView);

            var dialog = new ContentDialog
            {
                Title = "Car Part Filter",
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Content = panel
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            excluded.Clear();
            foreach (var name in workingExcluded)
                excluded.Add(name);
        }

        private static List<string> CollectAllMeshNames(IEnumerable<IViewerNode> roots)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(IViewerNode node)
            {
                if (node is MeshNode mesh && mesh.GeometryData != null &&
                    !string.IsNullOrEmpty(mesh.Name) && seen.Add(mesh.Name))
                    names.Add(mesh.Name);

                foreach (var child in node.Children)
                    Visit(child);
            }

            foreach (var root in roots)
                Visit(root);
            return names;
        }
    }
}
