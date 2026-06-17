using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using SDX = SharpDX;

namespace ForzaTechStudio.Views
{
    public sealed partial class ViewportPage : Page
    {
        private const double UvDefaultDisplaySize = 600;
        private const int UvMaxOverlayPixels = 16 * 1024 * 1024;
        private Window? _uvMapsWindow;

        private ModelBinNode? _uvModelBin;
        private MeshNode? _uvMeshNode;
        private ForzaGeometryData? _uvGeo;
        private Grid? _uvEditorRoot;
        private Image? _uvTextureImage;
        private Image? _uvOverlayImage;
        private Canvas? _uvSelectionCanvas;
        private TextBlock? _uvStatusText;
        private MeshGeometryModel3D? _uvFaceHighlight;
        private bool _uvDragging;
        private bool _uvDragMoved;
        private bool _uvClosingHandled;
        private Vector2 _uvDragLast;
        private readonly HashSet<int> _uvSelectedFaces = new();
        private readonly HashSet<UvEditKey> _uvDirtyChannels = new();
        private readonly Dictionary<UvEditKey, Vector2[]> _uvBackups = new();
        private ScrollViewer? _uvViewerScroll;
        private Slider? _uvZoomSlider;
        private TextBlock? _uvZoomPctText;
        private bool _uvZoomFromCode;
        private float _uvZoomCurrent = 1f;
        private double _uvSurfaceWidth = UvDefaultDisplaySize;
        private double _uvSurfaceHeight = UvDefaultDisplaySize;
        private int _uvLastOverlayWidth;
        private int _uvLastOverlayHeight;
        private int _uvOverlayRenderVersion;
        private bool _uvPanning;
        private bool _uvPanMoved;
        private Windows.Foundation.Point _uvPanLast;
        private ComboBox? _uvLibraryCombo;
        private ComboBox? _uvChannelCombo;
        private int _uvChannelIndex;
        private bool _uvSyncingChannelCombo;

        private readonly record struct UvEditKey(MeshNode Mesh, int Channel);

        private sealed class UvMeshItem
        {
            public string Name { get; init; } = string.Empty;
            public MeshNode? Node { get; init; }
            public ForzaGeometryData? Geometry { get; init; }
            public override string ToString() => Name;
        }

        private sealed class UvTextureItem
        {
            public string Name { get; init; } = string.Empty;
            public SwatchbinArchiveEntry? Entry { get; init; }
            public override string ToString() => Name;
        }

        private sealed class UvChannelItem
        {
            public int ChannelIndex { get; init; }
            public string Name => $"UV {ChannelIndex}";
            public override string ToString() => Name;
        }

        private sealed class UvTexturePreview
        {
            public BitmapImage? Image { get; init; }
            public int PixelWidth { get; init; }
            public int PixelHeight { get; init; }
        }

        private void UvMaps_Click(object sender, RoutedEventArgs e)
        {
            if (_uvMapsWindow != null)
            {
                _uvMapsWindow.Activate();
                return;
            }

            var modelBins = ViewModel.GetAllModelBinNodes();

            var modelBinCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Select modelbin" };
            foreach (var mb in modelBins)
                modelBinCombo.Items.Add(new ComboBoxItem { Content = string.IsNullOrWhiteSpace(mb.FileName) ? mb.Name : mb.FileName, Tag = mb });

            var meshCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Select mesh", IsEnabled = false };
            var channelLabel = new TextBlock
            {
                Text = "UV Channel",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Visibility = Visibility.Collapsed
            };
            var channelCombo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = "UV channel",
                IsEnabled = false,
                Visibility = Visibility.Collapsed
            };
            _uvChannelCombo = channelCombo;

            var libraryTextures = CollectUvLibraryTextures();
            var libraryCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Library texture", IsEnabled = libraryTextures.Count > 0 };
            foreach (var item in libraryTextures)
                libraryCombo.Items.Add(item);
            _uvLibraryCombo = libraryCombo;

            var browseBtn = new Button { Content = "Browse .swatchbin...", HorizontalAlignment = HorizontalAlignment.Stretch };
            var clearTextureBtn = new Button { Content = "Clear Texture", HorizontalAlignment = HorizontalAlignment.Stretch };
            var statusText = new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Text = modelBins.Count == 0 ? "No modelbins loaded." : "Select a modelbin and mesh." };
            _uvStatusText = statusText;

            var background = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var textureImage = new Image
            {
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var overlayImage = new Image
            {
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };
            var selectionCanvas = new Canvas { Width = _uvSurfaceWidth, Height = _uvSurfaceHeight, IsHitTestVisible = false };
            _uvTextureImage = textureImage;
            _uvOverlayImage = overlayImage;
            _uvSelectionCanvas = selectionCanvas;

            var editorRoot = new Grid
            {
                Width = _uvSurfaceWidth,
                Height = _uvSurfaceHeight,
                Background = new SolidColorBrush(Colors.Transparent)
            };
            editorRoot.Children.Add(background);
            editorRoot.Children.Add(textureImage);
            editorRoot.Children.Add(overlayImage);
            editorRoot.Children.Add(selectionCanvas);
            editorRoot.PointerPressed += UvEditor_PointerPressed;
            editorRoot.PointerMoved += UvEditor_PointerMoved;
            editorRoot.PointerReleased += UvEditor_PointerReleased;
            editorRoot.PointerWheelChanged += UvEditor_PointerWheelChanged;
            _uvEditorRoot = editorRoot;
            UpdateUvSurfaceLayout();

            var uvScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Auto,
                VerticalScrollMode = ScrollMode.Auto,
                ZoomMode = ZoomMode.Enabled,
                MinZoomFactor = 0.1f,
                MaxZoomFactor = 8f,
                Content = editorRoot
            };
            uvScroll.ViewChanged += UvViewerScroll_ViewChanged;
            _uvViewerScroll = uvScroll;

            var zoomOutBtn = new Button { Content = "\u2212", Width = 32, FontSize = 14 };
            var zoomSlider = new Slider { Minimum = 0.1, Maximum = 8, Value = 1, StepFrequency = 0.05, Width = 130, VerticalAlignment = VerticalAlignment.Center };
            var zoomInBtn = new Button { Content = "+", Width = 32, FontSize = 14 };
            var zoomPctText = new TextBlock { Text = "100%", VerticalAlignment = VerticalAlignment.Center, Width = 46, TextAlignment = TextAlignment.Right };
            var fitBtn = new Button { Content = "Fit", FontSize = 12 };
            var oneToOneBtn = new Button { Content = "1:1", FontSize = 12 };
            _uvZoomSlider = zoomSlider;
            _uvZoomPctText = zoomPctText;

            zoomOutBtn.Click += (_, _) => UvSetZoom(Math.Max(0.1f, (_uvViewerScroll?.ZoomFactor ?? 1f) - 0.1f));
            zoomInBtn.Click += (_, _) => UvSetZoom(Math.Min(8f, (_uvViewerScroll?.ZoomFactor ?? 1f) + 0.1f));
            zoomSlider.ValueChanged += (_, ea) => { if (!_uvZoomFromCode) UvSetZoom((float)ea.NewValue); };

            fitBtn.Click += (_, _) =>
            {
                if (_uvViewerScroll == null) return;
                double vw = _uvViewerScroll.ViewportWidth;
                double vh = _uvViewerScroll.ViewportHeight;
                if (vw <= 0 || vh <= 0) return;
                UvSetZoom((float)Math.Max(0.1, Math.Min(vw / _uvSurfaceWidth, vh / _uvSurfaceHeight) * 0.95));
                _uvViewerScroll.ChangeView(0, 0, null);
            };
            oneToOneBtn.Click += (_, _) => { UvSetZoom(1f); _uvViewerScroll?.ChangeView(0, 0, null); };

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
            toolbar.Children.Add(zoomOutBtn);
            toolbar.Children.Add(zoomSlider);
            toolbar.Children.Add(zoomInBtn);
            toolbar.Children.Add(zoomPctText);
            toolbar.Children.Add(new Border { Width = 1, Margin = new Thickness(4, 2, 4, 2), Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)) });
            toolbar.Children.Add(fitBtn);
            toolbar.Children.Add(oneToOneBtn);

            var uvDisplayBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = uvScroll
            };

            var editorPanel = new Grid();
            editorPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editorPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(toolbar, 0);
            Grid.SetRow(uvDisplayBorder, 1);
            editorPanel.Children.Add(toolbar);
            editorPanel.Children.Add(uvDisplayBorder);

            var saveBtn = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Stretch };
            var okBtn = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Stretch, Style = Application.Current.Resources["AccentButtonStyle"] as Style };
            var cancelBtn = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Stretch };
            saveBtn.Click += async (_, _) => await UvSaveAsync();
            okBtn.Click += (_, _) => UvCommitAndClose();
            cancelBtn.Click += (_, _) => UvRevertAndClose();

            var controls = new StackPanel { Spacing = 10 };
            controls.Children.Add(new TextBlock { Text = "Modelbin", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            controls.Children.Add(modelBinCombo);
            controls.Children.Add(new TextBlock { Text = "Mesh", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            controls.Children.Add(meshCombo);
            controls.Children.Add(channelLabel);
            controls.Children.Add(channelCombo);
            controls.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)) });
            controls.Children.Add(new TextBlock { Text = "Background Texture", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            controls.Children.Add(libraryCombo);
            controls.Children.Add(browseBtn);
            controls.Children.Add(clearTextureBtn);
            controls.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)) });
            controls.Children.Add(new TextBlock
            {
                FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap,
                Text = "Click face to select. Ctrl+click to toggle. Drag face to move UVs. Drag empty area to pan."
            });
            controls.Children.Add(statusText);
            controls.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)) });
            controls.Children.Add(saveBtn);
            controls.Children.Add(okBtn);
            controls.Children.Add(cancelBtn);

            var leftScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new Border { Padding = new Thickness(0, 0, 16, 0), Child = controls }
            };

            var contentGrid = new Grid { ColumnSpacing = 12 };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(leftScroll, 0);
            Grid.SetColumn(editorPanel, 1);
            contentGrid.Children.Add(leftScroll);
            contentGrid.Children.Add(editorPanel);

            modelBinCombo.SelectionChanged += (_, _) =>
            {
                meshCombo.Items.Clear();
                UpdateUvChannelCombo(null, channelCombo, channelLabel);
                LoadMeshIntoEditor(null, null);
                var mb = (modelBinCombo.SelectedItem as ComboBoxItem)?.Tag as ModelBinNode;
                _uvModelBin = mb;
                if (mb == null) { meshCombo.IsEnabled = false; return; }

                foreach (var mesh in mb.Children.OfType<MeshNode>())
                    meshCombo.Items.Add(new UvMeshItem { Name = mesh.Name, Node = mesh, Geometry = mesh.GeometryData });

                meshCombo.IsEnabled = meshCombo.Items.Count > 0;
                statusText.Text = meshCombo.Items.Count > 0 ? "Select a mesh." : "Modelbin has no meshes.";
            };

            meshCombo.SelectionChanged += async (_, _) =>
            {
                var item = meshCombo.SelectedItem as UvMeshItem;
                UpdateUvChannelCombo(item?.Geometry, channelCombo, channelLabel);
                await LoadMeshIntoEditorAsync(item?.Node, item?.Geometry);
            };

            channelCombo.SelectionChanged += async (_, _) =>
            {
                if (_uvSyncingChannelCombo)
                    return;

                if (channelCombo.SelectedItem is UvChannelItem channelItem)
                    _uvChannelIndex = channelItem.ChannelIndex;

                _uvSelectedFaces.Clear();
                _uvLastOverlayWidth = 0;
                _uvLastOverlayHeight = 0;
                await LoadMeshIntoEditorAsync(_uvMeshNode, _uvGeo);
            };

            libraryCombo.SelectionChanged += async (_, _) =>
            {
                var entry = (libraryCombo.SelectedItem as UvTextureItem)?.Entry;
                if (entry == null) return;
                ApplyUvTexturePreview(await LoadUvTextureFromEntryAsync(entry));
            };

            browseBtn.Click += async (_, _) =>
            {
                var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
                picker.FileTypeFilter.Add(".swatchbin");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, GetUvMapsWindowHandle());
                StorageFile? file = await picker.PickSingleFileAsync();
                if (file == null) return;
                ApplyUvTexturePreview(await LoadUvTextureFromFileAsync(file.Path));
            };

            clearTextureBtn.Click += (_, _) => ApplyUvTexturePreview(null);

            ShowUvMapsWindow(contentGrid);
        }

        private void UvSetZoom(float z)
        {
            z = Math.Clamp(z, 0.1f, 8f);
            if (_uvViewerScroll != null)
            {
                _uvViewerScroll.ChangeView(null, null, z);
                return;
            }

            _uvZoomCurrent = z;
            if (_uvZoomSlider != null) { _uvZoomFromCode = true; _uvZoomSlider.Value = z; _uvZoomFromCode = false; }
            if (_uvZoomPctText != null) _uvZoomPctText.Text = $"{(int)(z * 100)}%";
        }

        private void UpdateUvSurfaceLayout()
        {
            if (_uvEditorRoot != null)
            {
                _uvEditorRoot.Width = _uvSurfaceWidth;
                _uvEditorRoot.Height = _uvSurfaceHeight;
            }

            if (_uvTextureImage != null)
            {
                _uvTextureImage.Width = _uvSurfaceWidth;
                _uvTextureImage.Height = _uvSurfaceHeight;
            }

            if (_uvOverlayImage != null)
            {
                _uvOverlayImage.Width = _uvSurfaceWidth;
                _uvOverlayImage.Height = _uvSurfaceHeight;
            }

            if (_uvSelectionCanvas != null)
            {
                _uvSelectionCanvas.Width = _uvSurfaceWidth;
                _uvSelectionCanvas.Height = _uvSurfaceHeight;
            }
        }

        private void ApplyUvSurfaceSize(double width, double height)
        {
            _uvSurfaceWidth = Math.Max(1d, width);
            _uvSurfaceHeight = Math.Max(1d, height);
            _uvLastOverlayWidth = 0;
            _uvLastOverlayHeight = 0;
            UpdateUvSurfaceLayout();
            RebuildSelectionPolygons();
        }

        private void ApplyUvTexturePreview(UvTexturePreview? preview)
        {
            if (_uvTextureImage != null)
                _uvTextureImage.Source = preview?.Image;

            if (preview != null && preview.PixelWidth > 0 && preview.PixelHeight > 0)
                ApplyUvSurfaceSize(preview.PixelWidth, preview.PixelHeight);
            else
                ApplyUvSurfaceSize(UvDefaultDisplaySize, UvDefaultDisplaySize);

            _ = RenderWireframeAsync(force: true);
        }

        private void UpdateUvChannelCombo(ForzaGeometryData? geo, ComboBox channelCombo, TextBlock channelLabel)
        {
            var channels = GetAvailableUvChannels(geo).ToList();
            _uvSyncingChannelCombo = true;
            channelCombo.Items.Clear();

            foreach (int channel in channels)
                channelCombo.Items.Add(new UvChannelItem { ChannelIndex = channel });

            bool showPicker = channels.Count > 1;
            channelCombo.Visibility = showPicker ? Visibility.Visible : Visibility.Collapsed;
            channelLabel.Visibility = showPicker ? Visibility.Visible : Visibility.Collapsed;
            channelCombo.IsEnabled = showPicker;

            _uvChannelIndex = channels.Count > 0 ? channels[0] : 0;
            if (channelCombo.Items.Count > 0)
                channelCombo.SelectedIndex = 0;

            _uvSyncingChannelCombo = false;
        }

        private static IEnumerable<int> GetAvailableUvChannels(ForzaGeometryData? geo)
        {
            if (geo?.UvChannels != null)
            {
                foreach (var channel in geo.UvChannels
                    .Where(kv => kv.Value != null && kv.Value.Length > 0)
                    .Select(kv => kv.Key)
                    .OrderBy(index => index))
                {
                    yield return channel;
                }
                yield break;
            }

            if (geo?.UVs != null && geo.UVs.Length > 0)
                yield return 0;
        }

        private static Vector2[]? GetUvArray(ForzaGeometryData? geo, int channelIndex)
        {
            if (geo?.UvChannels != null && geo.UvChannels.TryGetValue(channelIndex, out var channelUvs))
                return channelUvs;

            return channelIndex == 0 ? geo?.UVs : null;
        }

        private Vector2[]? GetActiveUvArray()
        {
            return GetUvArray(_uvGeo, _uvChannelIndex);
        }

        private UvEditKey? GetActiveUvEditKey()
        {
            return _uvMeshNode != null ? new UvEditKey(_uvMeshNode, _uvChannelIndex) : null;
        }

        private void EnsureActiveUvBackup()
        {
            var activeUvs = GetActiveUvArray();
            var key = GetActiveUvEditKey();
            if (activeUvs != null && key.HasValue && !_uvBackups.ContainsKey(key.Value))
                _uvBackups[key.Value] = (Vector2[])activeUvs.Clone();
        }

        private void UvViewerScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_uvViewerScroll == null)
                return;

            float previousZoom = _uvZoomCurrent;
            _uvZoomCurrent = _uvViewerScroll.ZoomFactor;

            if (_uvZoomSlider != null)
            {
                _uvZoomFromCode = true;
                _uvZoomSlider.Value = _uvZoomCurrent;
                _uvZoomFromCode = false;
            }

            if (_uvZoomPctText != null)
                _uvZoomPctText.Text = $"{(int)(_uvZoomCurrent * 100)}%";

            if (Math.Abs(_uvZoomCurrent - previousZoom) > 0.001f)
                _ = RenderWireframeAsync();
        }

        private (int Width, int Height) GetUvOverlayRenderSize()
        {
            double zoomScale = Math.Max(1d, _uvZoomCurrent);
            double width = Math.Max(1d, _uvSurfaceWidth * zoomScale);
            double height = Math.Max(1d, _uvSurfaceHeight * zoomScale);
            double pixelCount = width * height;

            if (pixelCount > UvMaxOverlayPixels)
            {
                double shrink = Math.Sqrt(UvMaxOverlayPixels / pixelCount);
                width *= shrink;
                height *= shrink;
            }

            return ((int)Math.Max(1, Math.Ceiling(width)), (int)Math.Max(1, Math.Ceiling(height)));
        }

        private void LoadMeshIntoEditor(MeshNode? meshNode, ForzaGeometryData? geo)
        {
            _uvMeshNode = meshNode;
            _uvGeo = geo;
            _uvSelectedFaces.Clear();
            _uvDragging = false;

            EnsureActiveUvBackup();

            RebuildSelectionPolygons();
            UpdateUvFaceHighlight3D();
        }

        private async Task LoadMeshIntoEditorAsync(MeshNode? meshNode, ForzaGeometryData? geo)
        {
            LoadMeshIntoEditor(meshNode, geo);

            if (_uvOverlayImage == null) return;

            var activeUvs = GetActiveUvArray();
            if (activeUvs == null || activeUvs.Length == 0)
            {
                _uvOverlayImage.Source = null;
                if (_uvStatusText != null && geo != null) _uvStatusText.Text = "Selected mesh has no UV data.";
                return;
            }

            await RenderWireframeAsync(force: true);
            UpdateUvStatus();
            await AutoSelectUvTextureAsync(meshNode);
        }

        private async Task RenderWireframeAsync(bool force = false)
        {
            var activeUvs = GetActiveUvArray();
            if (_uvOverlayImage == null || _uvGeo == null || activeUvs == null) return;
            var geo = _uvGeo;
            int channelIndex = _uvChannelIndex;
            var (renderWidth, renderHeight) = GetUvOverlayRenderSize();
            if (!force && renderWidth == _uvLastOverlayWidth && renderHeight == _uvLastOverlayHeight && _uvOverlayImage.Source != null)
                return;

            int renderVersion = ++_uvOverlayRenderVersion;
            byte[] rgba = await Task.Run(() => BuildUvOverlayRgba(geo, channelIndex, renderWidth, renderHeight));
            BitmapImage? bitmap = await RgbaToBitmapImageAsync(rgba, renderWidth, renderHeight);

            if (_uvOverlayImage == null || renderVersion != _uvOverlayRenderVersion || !ReferenceEquals(geo, _uvGeo) || channelIndex != _uvChannelIndex)
                return;

            _uvLastOverlayWidth = renderWidth;
            _uvLastOverlayHeight = renderHeight;
            _uvOverlayImage.Source = bitmap;
        }

        private void UpdateUvStatus()
        {
            if (_uvStatusText == null || _uvGeo == null) return;
            var activeUvs = GetActiveUvArray();
            int tris = _uvGeo.Indices != null ? _uvGeo.Indices.Length / 3 : 0;
            var key = GetActiveUvEditKey();
            string dirty = key.HasValue && _uvDirtyChannels.Contains(key.Value) ? " (modified)" : string.Empty;
            _uvStatusText.Text = $"UV {_uvChannelIndex}: {activeUvs?.Length ?? 0} UVs, {tris} triangles, {_uvSelectedFaces.Count} selected.{dirty}";
        }

        private void ShowUvMapsWindow(UIElement content)
        {
            var window = new Window
            {
                Title = "UV Maps",
                SystemBackdrop = new MicaBackdrop()
            };

            var outerBorder = new Border { Padding = new Thickness(24), Child = content };
            window.Content = outerBorder;

            if (outerBorder is FrameworkElement root &&
                App.MainWindow?.Content is FrameworkElement mainRoot)
            {
                root.RequestedTheme = mainRoot.RequestedTheme;
            }

            window.Closed += (_, _) =>
            {
                if (!ReferenceEquals(_uvMapsWindow, window))
                    return;

                if (!_uvClosingHandled)
                    RevertAllUvChanges();

                CleanupUvEditorState();
                _uvMapsWindow = null;
            };

            _uvClosingHandled = false;
            _uvMapsWindow = window;
            window.Activate();

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            if (appWindow.Presenter is OverlappedPresenter uvPresenter)
                uvPresenter.IsAlwaysOnTop = true;

            var size = new Windows.Graphics.SizeInt32(1320, 960);
            appWindow.Resize(size);

            var area = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
            if (area != null)
            {
                int x = area.WorkArea.X + (area.WorkArea.Width - size.Width) / 2;
                int y = area.WorkArea.Y + (area.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(x, y));
            }
        }

        private IntPtr GetUvMapsWindowHandle()
        {
            return _uvMapsWindow != null
                ? WinRT.Interop.WindowNative.GetWindowHandle(_uvMapsWindow)
                : App.MainWindowHandle;
        }

        private void UvEditor_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_uvEditorRoot == null) return;
            var pt = e.GetCurrentPoint(_uvEditorRoot);
            if (!pt.Properties.IsLeftButtonPressed) return;

            if (GetActiveUvArray() != null && _uvGeo?.Indices != null)
            {
                var uv = new Vector2((float)(pt.Position.X / _uvSurfaceWidth), (float)(pt.Position.Y / _uvSurfaceHeight));
                int face = HitTestFace(uv);
                bool ctrl = IsCtrlDown();

                if (face >= 0)
                {
                    if (ctrl)
                    {
                        if (!_uvSelectedFaces.Add(face)) _uvSelectedFaces.Remove(face);
                        RebuildSelectionPolygons(); UpdateUvFaceHighlight3D(); UpdateUvStatus();
                        return;
                    }
                    if (!_uvSelectedFaces.Contains(face))
                    {
                        _uvSelectedFaces.Clear(); _uvSelectedFaces.Add(face);
                        RebuildSelectionPolygons(); UpdateUvFaceHighlight3D(); UpdateUvStatus();
                    }
                    _uvDragging = true;
                    _uvDragMoved = false;
                    _uvDragLast = uv;
                    EnsureActiveUvBackup();
                    _uvEditorRoot.CapturePointer(e.Pointer);
                    return;
                }
            }

            _uvPanning = true;
            _uvPanMoved = false;
            _uvPanLast = e.GetCurrentPoint(null).Position;
            _uvEditorRoot.CapturePointer(e.Pointer);
        }

        private void UvEditor_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var activeUvs = GetActiveUvArray();
            if (_uvDragging && activeUvs != null && _uvEditorRoot != null)
            {
                var pt = e.GetCurrentPoint(_uvEditorRoot);
                if (!pt.Properties.IsLeftButtonPressed) return;
                var uv = new Vector2((float)(pt.Position.X / _uvSurfaceWidth), (float)(pt.Position.Y / _uvSurfaceHeight));
                var delta = uv - _uvDragLast;
                _uvDragLast = uv;
                if (delta == Vector2.Zero) return;
                foreach (var vi in SelectedVertexIndices())
                {
                    if ((uint)vi < (uint)activeUvs.Length)
                        activeUvs[vi] += delta;
                }
                _uvDragMoved = true;
                RebuildSelectionPolygons();
                if (_uvMeshNode != null && _uvChannelIndex == 0) RefreshMesh3DTextureCoords(_uvMeshNode);
                return;
            }

            if (_uvPanning && _uvViewerScroll != null)
            {
                var cur = e.GetCurrentPoint(null).Position;
                double dx = cur.X - _uvPanLast.X;
                double dy = cur.Y - _uvPanLast.Y;
                double nx = Math.Clamp(_uvViewerScroll.HorizontalOffset - dx, 0, _uvViewerScroll.ScrollableWidth);
                double ny = Math.Clamp(_uvViewerScroll.VerticalOffset - dy, 0, _uvViewerScroll.ScrollableHeight);
                _uvViewerScroll.ChangeView(nx, ny, null, true);
                _uvPanLast = cur;
                _uvPanMoved = true;
                e.Handled = true;
            }
        }

        private async void UvEditor_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_uvDragging)
            {
                _uvDragging = false;
                _uvEditorRoot?.ReleasePointerCapture(e.Pointer);
                if (_uvDragMoved && _uvMeshNode != null)
                {
                    _uvDirtyChannels.Add(new UvEditKey(_uvMeshNode, _uvChannelIndex));
                    await RenderWireframeAsync(force: true);
                    UpdateUvStatus();
                }
                return;
            }

            if (_uvPanning)
            {
                _uvPanning = false;
                _uvEditorRoot?.ReleasePointerCapture(e.Pointer);
                if (!_uvPanMoved)
                {
                    _uvSelectedFaces.Clear();
                    RebuildSelectionPolygons();
                    UpdateUvFaceHighlight3D();
                    UpdateUvStatus();
                }
            }
        }

        private void UvEditor_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            int delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
            float newZ = Math.Clamp(_uvZoomCurrent * (delta > 0 ? 1.15f : 1f / 1.15f), 0.1f, 8f);
            UvSetZoom(newZ);
            e.Handled = true;
        }

        private Task AutoSelectUvTextureAsync(MeshNode? meshNode)
        {
            if (meshNode?.GeometryData == null || _uvModelBin == null || _uvLibraryCombo == null || _uvLibraryCombo.Items.Count == 0)
                return Task.CompletedTask;
            var mat = ResolveAssignedMaterial(meshNode.GeometryData, _uvModelBin);
            if (mat?.Bundle == null) return Task.CompletedTask;

            string? targetPath = null;
            foreach (var paramBlob in mat.Bundle.Blobs.OfType<MaterialShaderParameterBlob>())
            {
                if (!IsViewportMaterialShaderParameterBlob(paramBlob)) continue;
                foreach (var param in paramBlob.Parameters)
                {
                    if (param.Type != ShaderParameterType.Texture2D || param.Value is not TextureParameter tp) continue;
                    if (string.IsNullOrEmpty(tp.Path)) continue;
                    if (ClassifyViewportTextureParameter(param, tp) == ViewportTextureSlot.Diffuse)
                    { targetPath = tp.Path; break; }
                }
                if (targetPath != null) break;
            }

            if (targetPath == null)
            {
                foreach (var paramBlob in mat.Bundle.Blobs.OfType<MaterialShaderParameterBlob>())
                    foreach (var param in paramBlob.Parameters)
                        if (param.Type == ShaderParameterType.Texture2D && param.Value is TextureParameter tp2
                            && !string.IsNullOrEmpty(tp2.Path) && tp2.Path.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase))
                        { targetPath = tp2.Path; break; }
            }

            if (targetPath == null) return Task.CompletedTask;
            string targetFile = System.IO.Path.GetFileName(targetPath);
            if (string.IsNullOrEmpty(targetFile)) return Task.CompletedTask;

            for (int i = 0; i < _uvLibraryCombo.Items.Count; i++)
            {
                if (_uvLibraryCombo.Items[i] is UvTextureItem item &&
                    System.IO.Path.GetFileName(item.Name).Equals(targetFile, StringComparison.OrdinalIgnoreCase))
                {
                    if (_uvLibraryCombo.SelectedIndex != i) _uvLibraryCombo.SelectedIndex = i;
                    return Task.CompletedTask;
                }
            }
            return Task.CompletedTask;
        }

        private void RefreshMesh3DTextureCoords(MeshNode meshNode)
        {
            var geo = meshNode.GeometryData;
            if (geo == null || !_renderMap.TryGetValue(meshNode, out var model)
                || model is not MeshGeometryModel3D meshModel || meshModel.Geometry is not MeshGeometry3D mesh3d) return;

            int activeChannel = GetViewportActiveRenderUvChannel(geo, meshNode.ParentModelBin);
            var activeUvs = GetGeometryUvArray(geo, activeChannel);
            if (activeUvs == null) return;

            var uvTiling = ResolveViewportMaterialUvTiling(geo, meshNode.ParentModelBin);
            var uvCol = new Vector2Collection(activeUvs.Length);
            foreach (var u in activeUvs)
            {
                var t = ApplyViewportUvTransform(geo, activeChannel, u, uvTiling);
                uvCol.Add(new SDX.Vector2(t.X, t.Y));
            }
            mesh3d.TextureCoordinates = uvCol;
        }

        private int HitTestFace(Vector2 p)
        {
            var rawUvs = GetActiveUvArray()!;
            var indices = _uvGeo.Indices!;
            // Use display-transformed UVs so hit testing matches visible wireframe
            var displayUvs = BuildDisplayTransformedUvs(_uvGeo, _uvChannelIndex, rawUvs);
            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                if (a >= displayUvs.Length || b >= displayUvs.Length || c >= displayUvs.Length) continue;
                if (PointInTriangle(p, displayUvs[a], displayUvs[b], displayUvs[c]))
                    return t / 3;
            }
            return -1;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
            float d2 = (p.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - c.Y);
            float d3 = (p.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - a.Y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private IEnumerable<int> SelectedVertexIndices()
        {
            var set = new HashSet<int>();
            var indices = _uvGeo!.Indices!;
            foreach (var f in _uvSelectedFaces)
            {
                int b = f * 3;
                if (b + 2 >= indices.Length) continue;
                set.Add(indices[b]);
                set.Add(indices[b + 1]);
                set.Add(indices[b + 2]);
            }
            return set;
        }

        private void RebuildSelectionPolygons()
        {
            if (_uvSelectionCanvas == null) return;
            _uvSelectionCanvas.Children.Clear();

            var rawUvs = GetActiveUvArray();
            if (rawUvs == null || _uvGeo?.Indices == null || _uvSelectedFaces.Count == 0)
                return;

            var indices = _uvGeo.Indices;
            // Use display-transformed UVs so selection polygons align with visible wireframe
            var displayUvs = BuildDisplayTransformedUvs(_uvGeo, _uvChannelIndex, rawUvs);
            var fill = new SolidColorBrush(Color.FromArgb(110, 255, 140, 0));
            var stroke = new SolidColorBrush(Color.FromArgb(255, 255, 140, 0));

            foreach (var f in _uvSelectedFaces)
            {
                int b = f * 3;
                if (b + 2 >= indices.Length) continue;

                var poly = new Microsoft.UI.Xaml.Shapes.Polygon { Fill = fill, Stroke = stroke, StrokeThickness = 1.5 };
                var pts = new Microsoft.UI.Xaml.Media.PointCollection();
                bool isValidFace = true;
                for (int k = 0; k < 3; k++)
                {
                    int vi = indices[b + k];
                    if (vi >= displayUvs.Length) { isValidFace = false; break; }
                    pts.Add(new Windows.Foundation.Point(displayUvs[vi].X * _uvSurfaceWidth, displayUvs[vi].Y * _uvSurfaceHeight));
                }
                if (!isValidFace) continue;
                poly.Points = pts;
                _uvSelectionCanvas.Children.Add(poly);
            }
        }

        private void EnsureUvFaceHighlight()
        {
            if (_uvFaceHighlight != null || _viewport == null) return;

            _uvFaceHighlight = new MeshGeometryModel3D
            {
                Material = new PhongMaterial
                {
                    DiffuseColor = new SDX.Color4(1f, 0.5f, 0f, 0.55f),
                    EmissiveColor = new SDX.Color4(1f, 0.45f, 0f, 1f)
                },
                Visibility = Visibility.Collapsed,
                CullMode = SDX.Direct3D11.CullMode.None
            };
            _viewport.Items.Add(_uvFaceHighlight);
        }

        private void UpdateUvFaceHighlight3D()
        {
            if (_uvMeshNode == null || _uvGeo?.Indices == null || _uvSelectedFaces.Count == 0 ||
                !_renderMap.TryGetValue(_uvMeshNode, out var model) || model is not MeshGeometryModel3D meshModel || meshModel.Geometry is not MeshGeometry3D src)
            {
                if (_uvFaceHighlight != null) _uvFaceHighlight.Visibility = Visibility.Collapsed;
                return;
            }

            EnsureUvFaceHighlight();
            if (_uvFaceHighlight == null) return;

            var indices = _uvGeo.Indices;
            var ind = new IntCollection();
            foreach (var f in _uvSelectedFaces)
            {
                int b = f * 3;
                if (b + 2 >= indices.Length) continue;
                ind.Add(indices[b]);
                ind.Add(indices[b + 1]);
                ind.Add(indices[b + 2]);
            }

            if (ind.Count == 0)
            {
                _uvFaceHighlight.Visibility = Visibility.Collapsed;
                return;
            }

            _uvFaceHighlight.Geometry = new MeshGeometry3D
            {
                Positions = src.Positions,
                Normals = src.Normals,
                TriangleIndices = ind
            };
            _uvFaceHighlight.Visibility = Visibility.Visible;
        }

        private void CommitDirtyToBundles()
        {
            foreach (var key in _uvDirtyChannels)
            {
                var meshNode = key.Mesh;
                var geo = meshNode.GeometryData;
                var bin = meshNode.ParentModelBin;
                var uvs = GetUvArray(geo, key.Channel);
                if (uvs == null || geo?.SourceMesh == null || bin?.Bundle == null) continue;

                WriteUvsToBundle(bin.Bundle, geo.SourceMesh, uvs, geo.MinVertexIndex, key.Channel);
                bin.IsDirty = true;

                if (key.Channel == 0)
                    RefreshMesh3DTextureCoords(meshNode);
            }
        }

        private void UvCommitAndClose()
        {
            _uvClosingHandled = true;
            CommitDirtyToBundles();
            _uvBackups.Clear();
            _uvDirtyChannels.Clear();
            _uvMapsWindow?.Close();
        }

        private void UvRevertAndClose()
        {
            _uvClosingHandled = true;
            RevertAllUvChanges();
            _uvMapsWindow?.Close();
        }

        private async Task UvSaveAsync()
        {
            if (_uvDirtyChannels.Count == 0)
            {
                if (_uvStatusText != null) _uvStatusText.Text = "No UV changes to save.";
                return;
            }

            var bins = _uvDirtyChannels
                .Select(key => key.Mesh.ParentModelBin)
                .Where(b => b != null)
                .Distinct()
                .Cast<IViewerNode>()
                .ToList();

            CommitDirtyToBundles();
            _uvDirtyChannels.Clear();
            _uvBackups.Clear();

            await SaveSelectedFileNodesAsync(bins, saveAsFolder: false);

            if (_uvStatusText != null) _uvStatusText.Text = "Saved UV changes to file.";
            UpdateUvStatus();
        }

        private void RevertAllUvChanges()
        {
            foreach (var kv in _uvBackups)
            {
                var geo = kv.Key.Mesh.GeometryData;
                var uvs = GetUvArray(geo, kv.Key.Channel);
                if (uvs == null) continue;
                Array.Copy(kv.Value, uvs, Math.Min(kv.Value.Length, uvs.Length));
                if (kv.Key.Channel == 0)
                    RefreshMesh3DTextureCoords(kv.Key.Mesh);
            }
            _uvBackups.Clear();
            _uvDirtyChannels.Clear();
        }

        private void CleanupUvEditorState()
        {
            _uvSelectedFaces.Clear();
            _uvModelBin = null;
            _uvMeshNode = null;
            _uvGeo = null;
            _uvEditorRoot = null;
            _uvTextureImage = null;
            _uvOverlayImage = null;
            _uvSelectionCanvas = null;
            _uvStatusText = null;
            _uvLibraryCombo = null;
            _uvChannelCombo = null;
            _uvChannelIndex = 0;
            _uvSyncingChannelCombo = false;
            _uvViewerScroll = null;
            _uvZoomSlider = null;
            _uvZoomPctText = null;
            _uvZoomFromCode = false;
            _uvZoomCurrent = 1f;
            _uvSurfaceWidth = UvDefaultDisplaySize;
            _uvSurfaceHeight = UvDefaultDisplaySize;
            _uvLastOverlayWidth = 0;
            _uvLastOverlayHeight = 0;
            _uvOverlayRenderVersion = 0;
            _uvDragging = false;
            _uvPanning = false;
            _uvPanMoved = false;
            if (_uvFaceHighlight != null)
                _uvFaceHighlight.Visibility = Visibility.Collapsed;
        }

        private static bool IsCtrlDown() =>
            Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        private static void WriteUvsToBundle(Bundle bundle, MeshBlob mesh, Vector2[] uvs, int minIndex, int semanticIndex)
        {
            var layout = bundle.Blobs.OfType<VertexLayoutBlob>()
                .FirstOrDefault(l => l.Metadatas.OfType<IdentifierMetadata>().Any(m => (int)m.Id == mesh.VertexLayoutIndex));
            if (layout == null)
            {
                var arr = bundle.Blobs.OfType<VertexLayoutBlob>().ToArray();
                if (mesh.VertexLayoutIndex >= 0 && mesh.VertexLayoutIndex < arr.Length) layout = arr[mesh.VertexLayoutIndex];
                else if (arr.Length > 0) layout = arr[0];
            }
            if (layout == null) return;

            var slotOffsets = new Dictionary<int, int>();
            D3D12_INPUT_LAYOUT_DESC? texEl = null;
            int texOffset = 0;
            foreach (var el in layout.Elements)
            {
                int slot = el.InputSlot;
                if (!slotOffsets.ContainsKey(slot)) slotOffsets[slot] = 0;
                int off = slotOffsets[slot];
                string sem = el.SemanticNameIndex >= 0 && el.SemanticNameIndex < layout.SemanticNames.Count
                    ? layout.SemanticNames[el.SemanticNameIndex] : string.Empty;
                if (sem == "TEXCOORD" && el.SemanticIndex == semanticIndex)
                {
                    texEl = el;
                    texOffset = off;
                }
                slotOffsets[slot] = off + UvFormatSize((int)el.Format);
            }
            if (texEl == null) return;

            int format = (int)texEl.Format;
            int writeSize = format == 16 ? 8 : 4;

            var vbMap = new Dictionary<int, VertexBufferBlob>();
            foreach (var vb in bundle.Blobs.OfType<VertexBufferBlob>())
            {
                var idMeta = vb.Metadatas.OfType<IdentifierMetadata>().FirstOrDefault();
                if (idMeta != null) vbMap[(int)idMeta.Id] = vb;
            }

            var usage = mesh.VertexBuffers.FirstOrDefault(v => v.InputSlot == texEl.InputSlot);
            if (usage == null) return;

            VertexBufferBlob? buf = null;
            if (vbMap.TryGetValue(usage.Index, out var byId)) buf = byId;
            else if (usage.Index >= 0 && usage.Index < bundle.Blobs.Count) buf = bundle.Blobs[usage.Index] as VertexBufferBlob;
            if (buf?.Header == null) return;

            byte[] raw = buf.Header.GetRawData();
            if (raw == null) return;

            long stride = buf.Header.Stride > 0 ? buf.Header.Stride : usage.Stride;
            if (stride == 0) return;

            for (int i = 0; i < uvs.Length; i++)
            {
                long vertexId = minIndex + i + mesh.IndexedVertexOffset;
                long addr = usage.Offset + vertexId * stride + texOffset;
                if (addr < 0 || addr + writeSize > raw.Length) continue;
                WriteUvBytes(raw, (int)addr, format, uvs[i]);
            }
        }

        private static void WriteUvBytes(byte[] buffer, int addr, int format, Vector2 uv)
        {
            if (format == 35)
            {
                ushort u = (ushort)Math.Clamp((int)MathF.Round(uv.X * 65535f), 0, 65535);
                ushort v = (ushort)Math.Clamp((int)MathF.Round((1f - uv.Y) * 65535f), 0, 65535);
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(addr), u);
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(addr + 2), v);
            }
            else if (format == 16)
            {
                BitConverter.GetBytes(uv.X).CopyTo(buffer, addr);
                BitConverter.GetBytes(1f - uv.Y).CopyTo(buffer, addr + 4);
            }
        }

        private static int UvFormatSize(int fmt) => fmt switch
        {
            6 => 12,
            10 => 8,
            13 => 8,
            16 => 8,
            24 => 4,
            28 => 4,
            35 => 4,
            37 => 4,
            _ => 4
        };


        private List<UvTextureItem> CollectUvLibraryTextures()
        {
            var result = new List<UvTextureItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var zipNode in EnumerateViewerNodes<ZipNode>(ViewModel.Roots))
            {
                if (string.IsNullOrWhiteSpace(zipNode.FilePath) || !File.Exists(zipNode.FilePath))
                    continue;

                try
                {
                    foreach (var entry in _swatchbinArchiveService.IndexZipTextureEntries(zipNode.FilePath))
                    {
                        if (seen.Add(entry.DisplayName))
                            result.Add(new UvTextureItem { Name = entry.DisplayName, Entry = entry });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Viewport/UvMaps] {zipNode.FilePath}: {ex.Message}");
                }
            }

            return result;
        }

        private async Task<UvTexturePreview?> LoadUvTextureFromEntryAsync(SwatchbinArchiveEntry entry)
        {
            try
            {
                SwatchbinInfo info = await Task.Run(() =>
                {
                    using var stream = new MemoryStream(entry.SwatchbinData);
                    return _viewportSwatchbinService.LoadSwatchbin(stream);
                });
                BitmapImage? preview = await _viewportSwatchbinPreviewService.CreatePreviewImageAsync(info, 0);
                if (preview == null)
                    return null;

                return new UvTexturePreview
                {
                    Image = preview,
                    PixelWidth = (int)Math.Max(1, info.Width),
                    PixelHeight = (int)Math.Max(1, info.Height)
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Viewport/UvMaps] LoadUvTextureFromEntry {entry.DisplayName}: {ex.Message}");
                return null;
            }
        }

        private async Task<UvTexturePreview?> LoadUvTextureFromFileAsync(string filePath)
        {
            try
            {
                SwatchbinInfo info = await _viewportSwatchbinService.LoadSwatchbinAsync(filePath);
                BitmapImage? preview = await _viewportSwatchbinPreviewService.CreatePreviewImageAsync(info, 0);
                if (preview == null)
                    return null;

                return new UvTexturePreview
                {
                    Image = preview,
                    PixelWidth = (int)Math.Max(1, info.Width),
                    PixelHeight = (int)Math.Max(1, info.Height)
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Viewport/UvMaps] LoadUvTextureFromFile {filePath}: {ex.Message}");
                return null;
            }
        }

        private static byte[] BuildUvOverlayRgba(ForzaGeometryData geo, int channelIndex, int width, int height)
        {
            byte[] buffer = new byte[width * height * 4];

            // grid
            const int divisions = 8;
            for (int i = 0; i <= divisions; i++)
            {
                int x = (int)Math.Round(i / (double)divisions * (width - 1));
                int y = (int)Math.Round(i / (double)divisions * (height - 1));
                DrawLine(buffer, width, height, x, 0, x, height - 1, 160, 160, 160, 130);
                DrawLine(buffer, width, height, 0, y, width - 1, y, 160, 160, 160, 130);
            }

            var rawUvs = GetUvArray(geo, channelIndex);
            var indices = geo.Indices;
            if (rawUvs == null || rawUvs.Length == 0)
                return buffer;

            // Build display-transformed UVs (read-only copy with channel-specific transform applied)
            var displayUvs = BuildDisplayTransformedUvs(geo, channelIndex, rawUvs);

            void DrawUvEdge(int a, int b)
            {
                if (a < 0 || b < 0 || a >= displayUvs.Length || b >= displayUvs.Length) return;
                int x0 = (int)Math.Round(displayUvs[a].X * (width - 1));
                int y0 = (int)Math.Round(displayUvs[a].Y * (height - 1));
                int x1 = (int)Math.Round(displayUvs[b].X * (width - 1));
                int y1 = (int)Math.Round(displayUvs[b].Y * (height - 1));
                DrawLine(buffer, width, height, x0, y0, x1, y1, 60, 220, 110, 255);
            }

            if (indices != null && indices.Length >= 3)
            {
                var drawn = new HashSet<long>();
                void Edge(int a, int b)
                {
                    int lo = Math.Min(a, b), hi = Math.Max(a, b);
                    if (drawn.Add(((long)lo << 32) | (uint)hi))
                        DrawUvEdge(a, b);
                }

                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                    Edge(a, b);
                    Edge(b, c);
                    Edge(c, a);
                }
            }

            return buffer;
        }


        private static Vector2[] BuildDisplayTransformedUvs(ForzaGeometryData geo, int channelIndex, Vector2[] rawUvs)
        {
            var transforms = geo?.SourceMesh?.TexCoordTransforms;
            bool hasTransform = transforms != null && channelIndex >= 0 && channelIndex < transforms.Length
                && transforms[channelIndex] != default;

            if (!hasTransform)
            {
                // Only apply V-flip for display
                var flipped = new Vector2[rawUvs.Length];
                for (int i = 0; i < rawUvs.Length; i++)
                    flipped[i] = new Vector2(rawUvs[i].X, 1f - rawUvs[i].Y);
                return flipped;
            }

            var transform = transforms![channelIndex];
            // Sanitize
            if (!float.IsFinite(transform.X) || !float.IsFinite(transform.Y)
                || !float.IsFinite(transform.Z) || !float.IsFinite(transform.W)
                || Math.Abs(transform.X) >= 1e6f || Math.Abs(transform.Y) >= 1e6f
                || Math.Abs(transform.Z) >= 1e6f || Math.Abs(transform.W) >= 1e6f)
            {
                var flipped = new Vector2[rawUvs.Length];
                for (int i = 0; i < rawUvs.Length; i++)
                    flipped[i] = new Vector2(rawUvs[i].X, 1f - rawUvs[i].Y);
                return flipped;
            }

            var transformed = new Vector2[rawUvs.Length];
            for (int i = 0; i < rawUvs.Length; i++)
            {
                float sourceU = rawUvs[i].X;
                float sourceV = 1f - rawUvs[i].Y;
                float u = sourceU * transform.Y + transform.X;
                float v = sourceV * transform.W + transform.Z;
                transformed[i] = float.IsFinite(u) && float.IsFinite(v)
                    ? new Vector2(u, v)
                    : new Vector2(rawUvs[i].X, 1f - rawUvs[i].Y);
            }

            return transformed;
        }

        private static void DrawLine(byte[] buffer, int width, int height, int x0, int y0, int x1, int y1, byte r, byte g, byte b, byte a)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if ((uint)x0 < (uint)width && (uint)y0 < (uint)height)
                {
                    int idx = (y0 * width + x0) * 4;
                    buffer[idx] = r;
                    buffer[idx + 1] = g;
                    buffer[idx + 2] = b;
                    buffer[idx + 3] = a;
                }

                if (x0 == x1 && y0 == y1) break;
                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        private static async Task<BitmapImage?> RgbaToBitmapImageAsync(byte[] rgba, int width, int height)
        {
            try
            {
                using var ms = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
                encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight,
                    (uint)width, (uint)height, 96, 96, rgba);
                await encoder.FlushAsync();
                ms.Seek(0);

                var image = new BitmapImage();
                await image.SetSourceAsync(ms);
                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}
