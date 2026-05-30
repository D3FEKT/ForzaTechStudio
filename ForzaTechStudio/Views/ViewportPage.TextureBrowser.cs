using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using ForzaTools.Bundles.Blobs;
using HelixToolkit.SharpDX.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ForzaTechStudio.Views
{
    public sealed partial class ViewportPage : Page
    {
        private readonly SwatchbinArchiveService _swatchbinArchiveService = new();
        private readonly SwatchbinService _viewportSwatchbinService = new();
        private readonly SwatchbinPreviewService _viewportSwatchbinPreviewService = new();
        private readonly Dictionary<string, SwatchbinArchiveEntry> _viewportTextureLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, SwatchbinArchiveEntry> _viewportTextureHashLookup = new();
        private readonly Dictionary<string, TextureModel?> _viewportTextureModelCache = new(StringComparer.OrdinalIgnoreCase);
        private bool _viewportTextureLookupDirty = true;

        private void InvalidateViewportTextureLookup()
        {
            _viewportTextureLookupDirty = true;
            _viewportTextureLookup.Clear();
            _viewportTextureHashLookup.Clear();
            _viewportTextureModelCache.Clear();
        }

        private void RefreshViewportTextureLookupFromLoadedRoots()
        {
            _viewportTextureLookupDirty = false;
            _viewportTextureLookup.Clear();
            _viewportTextureHashLookup.Clear();
            _viewportTextureModelCache.Clear();

            foreach (var zipNode in EnumerateViewerNodes<ZipNode>(ViewModel.Roots))
            {
                if (string.IsNullOrWhiteSpace(zipNode.FilePath) || !File.Exists(zipNode.FilePath))
                    continue;

                try
                {
                    foreach (var entry in _swatchbinArchiveService.LoadZipTextureEntries(zipNode.FilePath))
                        AddViewportTextureLookupEntry(entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Viewport/Textures] {zipNode.FilePath}: {ex.Message}");
                }
            }
        }

        private void AddViewportTextureLookupEntry(SwatchbinArchiveEntry entry)
        {
            foreach (string key in BuildViewportTextureLookupKeys(entry.LogicalPath))
            {
                _viewportTextureLookup.TryAdd(key, entry);
                _viewportTextureHashLookup.TryAdd(SwatchbinArchiveService.ComputeCrc32(key), entry);
                _viewportTextureHashLookup.TryAdd(SwatchbinArchiveService.ComputeCrc32(key.ToLowerInvariant()), entry);
            }
        }

        private static IEnumerable<string> BuildViewportTextureLookupKeys(string texturePath)
        {
            string normalized = NormalizeViewportTexturePath(texturePath);
            if (string.IsNullOrWhiteSpace(normalized))
                yield break;

            foreach (string path in ExpandViewportTexturePathCandidates(normalized))
            {
                yield return path;
                yield return path.Replace('/', '\\');
                yield return path.Replace('\\', '/');
            }
        }

        private static IEnumerable<string> ExpandViewportTexturePathCandidates(string normalizedPath)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            static void Add(HashSet<string> set, string value)
            {
                value = NormalizeViewportTexturePath(value);
                if (!string.IsNullOrWhiteSpace(value))
                    set.Add(value);
            }

            Add(candidates, normalizedPath);

            string withoutExtension = Path.ChangeExtension(normalizedPath, null) ?? normalizedPath;
            Add(candidates, withoutExtension);
            Add(candidates, withoutExtension + ".swatchbin");

            string fileName = Path.GetFileName(normalizedPath.Replace('/', '\\'));
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                Add(candidates, fileName);
                string fileNameWithoutExtension = Path.ChangeExtension(fileName, null) ?? fileName;
                Add(candidates, fileNameWithoutExtension);
                Add(candidates, fileNameWithoutExtension + ".swatchbin");
            }

            foreach (string candidate in candidates)
                yield return candidate;
        }

        private static string NormalizeViewportTexturePath(string texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
                return string.Empty;

            string normalized = texturePath.Trim().Replace('/', '\\');
            if (normalized.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Game:/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[6..];

            return normalized.TrimStart('\\', '/');
        }

        private SwatchbinArchiveEntry? ResolveViewportTextureEntry(TextureParameter textureParameter)
        {
            if (_viewportTextureLookupDirty)
                RefreshViewportTextureLookupFromLoadedRoots();

            foreach (string key in BuildViewportTextureLookupKeys(textureParameter.Path ?? string.Empty))
            {
                if (_viewportTextureLookup.TryGetValue(key, out var entry))
                    return entry;
            }

            if (textureParameter.PathHash != 0 && _viewportTextureHashLookup.TryGetValue(textureParameter.PathHash, out var hashedEntry))
                return hashedEntry;

            return null;
        }

        private TextureModel? ResolveViewportTextureModel(TextureParameter textureParameter)
        {
            var entry = ResolveViewportTextureEntry(textureParameter);
            if (entry == null)
                return null;

            string cacheKey = $"{entry.SourceArchivePath}|{entry.LogicalPath}";
            if (_viewportTextureModelCache.TryGetValue(cacheKey, out var cachedModel))
                return cachedModel;

            try
            {
                using var stream = new MemoryStream(entry.SwatchbinData);
                SwatchbinInfo info = _viewportSwatchbinService.LoadSwatchbin(stream);
                if (info.DdsData == null || info.DdsData.Length == 0)
                    return _viewportTextureModelCache[cacheKey] = null;

                var textureStream = new MemoryStream(info.DdsData, writable: false);
                var textureModel = new TextureModel(textureStream, autoCloseStream: true);

                _viewportTextureModelCache[cacheKey] = textureModel;
                return textureModel;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Viewport/Textures] Failed to resolve {entry.DisplayName}: {ex.Message}");
                _viewportTextureModelCache[cacheKey] = null;
                return null;
            }
        }

        private async void ShowZipTextures_Click(object sender, RoutedEventArgs e)
        {
            var zipNodes = EnumerateViewerNodes<ZipNode>(ViewModel.Roots)
                .Where(node => !string.IsNullOrWhiteSpace(node.FilePath) && File.Exists(node.FilePath))
                .ToList();

            if (zipNodes.Count == 0)
            {
                await ShowTextureInfoDialogAsync("No loaded car zip is available for texture browsing.");
                return;
            }

            ZipTexturesBtn.IsEnabled = false;

            try
            {
                var textureItems = new List<ViewportZipTextureItem>();

                foreach (var zipNode in zipNodes)
                {
                    var entries = await _swatchbinArchiveService.LoadZipTextureEntriesAsync(zipNode.FilePath);
                    foreach (var entry in entries)
                    {
                        textureItems.Add(await CreateTextureItemAsync(zipNode.Name, entry));
                    }
                }

                if (textureItems.Count == 0)
                {
                    await ShowTextureInfoDialogAsync("No .swatchbin or .pb texture bundles were found in the loaded car zip.");
                    return;
                }

                await ShowTextureBrowserDialogAsync(textureItems);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await ShowTextureInfoDialogAsync($"Failed to load zip textures: {ex.Message}");
            }
            finally
            {
                ZipTexturesBtn.IsEnabled = true;
            }
        }

        private async Task<ViewportZipTextureItem> CreateTextureItemAsync(string zipName, SwatchbinArchiveEntry entry)
        {
            try
            {
                using var stream = new MemoryStream(entry.SwatchbinData);
                SwatchbinInfo info = _viewportSwatchbinService.LoadSwatchbin(stream);
                info.FileName = Path.GetFileName(entry.LogicalPath);
                var preview = await _viewportSwatchbinPreviewService.CreatePreviewImageAsync(info, 144);

                return new ViewportZipTextureItem
                {
                    DisplayName = entry.DisplayName,
                    SourceText = zipName,
                    DetailText = $"{info.Width}x{info.Height} - {info.DxgiFormatName} - {info.MipLevels} mip(s)",
                    PreviewImage = preview
                };
            }
            catch (Exception ex)
            {
                return new ViewportZipTextureItem
                {
                    DisplayName = entry.DisplayName,
                    SourceText = zipName,
                    DetailText = $"Preview unavailable - {ex.Message}"
                };
            }
        }

        private async Task ShowTextureBrowserDialogAsync(IReadOnlyList<ViewportZipTextureItem> textureItems)
        {
            var texturePanel = new StackPanel
            {
                Spacing = 8,
                Padding = new Thickness(2)
            };

            foreach (var item in textureItems)
                texturePanel.Children.Add(CreateTextureRow(item));

            var scrollViewer = new ScrollViewer
            {
                Content = texturePanel,
                MaxHeight = 560,
                MinWidth = 640,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = $"Zip Textures ({textureItems.Count})",
                CloseButtonText = "Close",
                Content = scrollViewer
            };

            await dialog.ShowAsync();
        }

        private UIElement CreateTextureRow(ViewportZipTextureItem item)
        {
            var grid = new Grid
            {
                ColumnSpacing = 12,
                Padding = new Thickness(8),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var previewHost = new Border
            {
                Width = 96,
                Height = 96,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"]
            };

            if (item.PreviewImage != null)
            {
                previewHost.Child = new Image
                {
                    Source = item.PreviewImage,
                    Stretch = Stretch.Uniform
                };
            }
            else
            {
                previewHost.Child = new FontIcon
                {
                    Glyph = "\uEB9F",
                    FontSize = 28,
                    Opacity = 0.55
                };
            }

            grid.Children.Add(previewHost);

            var textPanel = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };

            textPanel.Children.Add(new TextBlock
            {
                Text = item.DisplayName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.WrapWholeWords
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = item.DetailText,
                Opacity = 0.75,
                TextWrapping = TextWrapping.WrapWholeWords
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = item.SourceText,
                FontSize = 11,
                Opacity = 0.6,
                TextWrapping = TextWrapping.WrapWholeWords
            });

            Grid.SetColumn(textPanel, 1);
            grid.Children.Add(textPanel);

            return grid;
        }

        private async Task ShowTextureInfoDialogAsync(string message)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Zip Textures",
                CloseButtonText = "Close",
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 380
                }
            };

            await dialog.ShowAsync();
        }
    }

    public sealed class ViewportZipTextureItem
    {
        public string DisplayName { get; init; } = string.Empty;
        public string SourceText { get; init; } = string.Empty;
        public string DetailText { get; init; } = string.Empty;
        public BitmapImage? PreviewImage { get; init; }
    }
}