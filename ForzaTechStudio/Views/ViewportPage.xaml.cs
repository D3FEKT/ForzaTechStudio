using CommunityToolkit.Mvvm.Input;
using ForzaTools.Bundles.Blobs;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Windows.Input;
using SDX = SharpDX;
using Color = Windows.UI.Color;

namespace ForzaTechStudio.Views
{
    // Simple wrapper for combo box items
    public class MeshScopeItem : INotifyPropertyChanged
    {
        public string? Name { get; set; }
        public MeshNode? Node { get; set; }
        public string? MaterialGroup { get; set; }

        private bool? _isChecked = true;
        public bool? IsChecked
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

    // Main initialization and lifecycle management
    public sealed partial class ViewportPage : Page
    {
        public ViewportViewModel ViewModel { get; } = new();
        public ICommand RemoveMeshMaterialParameterCommand { get; }

        private bool _isUpdatingUi = false;
        private Viewport3DX? _viewport;
        private LineGeometryModel3D? _gridLines;
        private GroupModel3D? _modelGroup;
        private DirectionalLight3D? _sceneLight;
        private DirectionalLight3D? _fillLight;
        private DirectionalLight3D? _backLight;
        private AmbientLight3D? _ambientLight;
        private DirectionalLight3D[]? _fullbrightLights;
        private MeshGeometryModel3D? _highlightModel;
        private LineGeometryModel3D? _lightHighlightModel;
        private Dictionary<IViewerNode, GeometryModel3D> _renderMap = new();
        private Dictionary<LightGroupNode, GeometryModel3D> _lightDamageRenderMap = new();
        private Dictionary<GeometryModel3D, IViewerNode> _hitTestMap = new();
        private Dictionary<IViewerNode, List<GeometryModel3D>> _hitProxyMap = new();
        private List<MeshNode> _currentHighlightTargets = new List<MeshNode>();
        private ViewportTransformGizmo? _transformGizmo;
        private ViewportGizmoMode _selectedGizmoMode = ViewportGizmoMode.None;
        private bool _hasTransformSelection = false;
        private bool _isGizmoDragging = false;
        private ViewportGizmoHandle _activeGizmoHandle;
        private ViewportTransformSession? _gizmoTransformSession;
        private readonly List<ViewportTransformTarget> _gizmoTransformTargets = new();
        private Vector3 _gizmoPivotWorld;
        private Vector3 _gizmoDragAxis;
        private Vector3 _gizmoDragPlaneNormal;
        private Vector3 _gizmoDragStartPlanePoint;
        private Vector3 _gizmoDragStartVector;
        private float _gizmoDragStartAxisParameter;
        private float _gizmoVisualScale = 1f;

        // Multi-selection state (meshes)
        private List<MeshNode> _multiSelectedMeshes = new();
        private bool _isMultiSelectActive = false;
        // Snapshot of original scale/translate/rotation at the time multi-select editing began
        private Dictionary<MeshNode, (Vector4 Scale, Vector4 Translate, Vector3 Rotation)> _multiSelectSnapshots = new();

        // Multi-selection state (light groups)
        private List<LightGroupNode> _multiSelectedLightGroups = new();
        private bool _isMultiLightSelectActive = false;
        private Dictionary<LightGroupNode, (Vector4 Pos, Vector4 Rot, Vector4 DmgPos, Vector4 DmgRot)> _multiSelectLightSnapshots = new();
        private List<LightGroupNode> _currentLightHighlightTargets = new List<LightGroupNode>();
        
        private SDX.Vector3? _savedCameraPosition;
        private SDX.Vector3? _savedCameraLookDirection;
        private SDX.Vector3? _savedCameraUpDirection;
        private double _savedFarPlane = 50000;
        private double _savedNearPlane = 0.1;
        private HelixToolkit.SharpDX.Core.CameraRotationMode _savedCameraRotationMode = HelixToolkit.SharpDX.Core.CameraRotationMode.Turntable;
        private HelixToolkit.SharpDX.Core.CameraMode _savedCameraMode = HelixToolkit.SharpDX.Core.CameraMode.Inspect;
        private bool _savedRotateAroundMouseDownPoint = false;
        private bool _savedZoomAroundMouseDownPoint = false;
        private bool _savedIsInertiaEnabled = true;
        private bool _savedOrthographicMode = false;
        private double _savedZoomSensitivity = 1.0;
        private double _savedRotationSensitivity = 1.0;
        private double _savedPanSensitivity = 1.0;
        private double _savedCameraInertiaFactor = 0.93;
        private double _savedFieldOfView = 45.0;
        private double _savedZoomDistanceLimitNear = 0.01;
        private double _savedOrthographicWidth = 100.0;
        private bool _isSyncingCameraSettingsUi = false;
        private bool _isModelScopeDeltaTransformActive = false;
        private bool _isSyncingSelection = false;
        private readonly List<MeshNode> _modelScopeDeltaMeshes = new();
        private readonly Dictionary<MeshNode, (Vector4 Scale, Vector4 Translate, Vector3 Rotation)> _modelScopeDeltaSnapshots = new();
        
        private Dictionary<IViewerNode, TreeViewNode> _treeNodeMap = new();

        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }
        public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register("IsLoading", typeof(bool), typeof(ViewportPage), new PropertyMetadata(false));

        public string LoadingStatus
        {
            get => (string)GetValue(LoadingStatusProperty);
            set => SetValue(LoadingStatusProperty, value);
        }
        public static readonly DependencyProperty LoadingStatusProperty = DependencyProperty.Register("LoadingStatus", typeof(string), typeof(ViewportPage), new PropertyMetadata(""));

        public string LoadingDetail
        {
            get => (string)GetValue(LoadingDetailProperty);
            set => SetValue(LoadingDetailProperty, value);
        }
        public static readonly DependencyProperty LoadingDetailProperty = DependencyProperty.Register("LoadingDetail", typeof(string), typeof(ViewportPage), new PropertyMetadata(""));

        public ViewportPage()
        {
            RemoveMeshMaterialParameterCommand = new RelayCommand<ShaderParameter>(RemoveMeshMaterialParameter);
            this.InitializeComponent();
            UpdateSelectionModeToggleIcon();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            this.KeyDown += ViewportPage_KeyDown;
            this.Loaded += Page_Loaded;
            this.Unloaded += Page_Unloaded;
            ViewModel.RequestCloseRoot += ViewModel_RequestCloseRoot;
            ViewModel.RequestResetScene += ViewModel_RequestResetScene;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.Roots.CollectionChanged += Roots_CollectionChanged;
        }

        private void ViewModel_RequestResetScene(object? sender, EventArgs e)
        {
            ClearSelection(syncTree: true);
            ResetViewportScene();
            RefreshModelList();
            RefreshManufacturerColorsFromLoadedRoots();
        }

        private void SelectionModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            UpdateSelectionModeToggleIcon();
        }

        private void SelectionModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateSelectionModeToggleIcon();
        }

        private void UpdateSelectionModeToggleIcon()
        {
            if (SelectionModeToggleIcon == null)
                return;

            SelectionModeToggleIcon.Glyph = SelectionModeToggle?.IsChecked == true
                ? "\uE762"
                : "\uE73E";
        }

        private void Roots_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshModelList();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            this.ActualThemeChanged -= ViewportPage_ActualThemeChanged;

            CancelViewportTransformForUnload();
            CancelActiveGizmoDrag();
            DisposeFileWatch();
            StopStatsOverlay();
            StopAnimTimer();
            ClearAllOverlays();

            if (_viewport != null)
            {
                SaveCameraStateFromViewport();

                var effectsManager = _viewport.EffectsManager;

                ViewportContainer.Children.Remove(_viewport);
                _viewport.Items.Clear();

                if (_viewport is IDisposable disposableViewport)
                    disposableViewport.Dispose();

                if (effectsManager != null)
                    effectsManager.Dispose();

                _viewport     = null;
                _gridLines    = null;
                _transformAxisGuide = null;
                _transformGizmo = null;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewportViewModel.SelectedNode))
            {
                SyncSelectionToUI();
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            Initialize3DView();
            RefreshTransformSelectionAvailability();
            InitFileWatch();
            
            this.ActualThemeChanged += ViewportPage_ActualThemeChanged;
            UpdateViewportTheme();
            
            RestoreSavedCameraView();
            ApplySavedCameraSettingsToViewport();
            SyncCameraSettingsUiFromState();

            InitializeStatsOverlay();
            UpdateUndoRedoButtons();
            _ = RefreshViewportGameTextureSourcesAsync();

            // Create initial tab on first load
            if (ViewModel.Tabs.Count == 0)
            {
                _isSwitchingTabs = true;
                var tab = ViewModel.AddTab("Tab 1");
                ViewModel.ActiveTab = tab;
                TabListView.SelectedItem = tab;
                _isSwitchingTabs = false;
            }
            else if (TabListView.SelectedItem == null && ViewModel.ActiveTab != null)
            {
                // Re-sync selection after navigation cache restores the page
                _isSwitchingTabs = true;
                TabListView.SelectedItem = ViewModel.ActiveTab;
                _isSwitchingTabs = false;
            }
        }

        private void ViewportPage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            UpdateViewportTheme();
        }

        private void UpdateViewportTheme()
        {
            if (_viewport != null && ViewportContainer.Background is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
            {
                _viewport.BackgroundColor = brush.Color;
            }
        }

        private void Initialize3DView()
        {
            if (_viewport != null) return;

            _viewport = new Viewport3DX
            {
                BackgroundColor = Color.FromArgb(255, 30, 30, 30),
                ShowCoordinateSystem = true,
                ShowViewCube = true,
                EffectsManager = new DefaultEffectsManager(),
                IsTabStop = false
            };
            
            //had to use custom cube texture to get the L/R directions to display correct due to lefthandsystem
            try
            {
                string cubePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "customcube.png");
                if (System.IO.File.Exists(cubePath))
                {
                    var bytes = System.IO.File.ReadAllBytes(cubePath);
                    _viewport.ViewCubeTexture = new System.IO.MemoryStream(bytes);
                }
            }
            catch { }

            _viewport.OnMouse3DDown += Viewport_OnMouse3DDown;
            _viewport.KeyDown += ViewportPage_KeyDown;
            _viewport.PointerMoved += Viewport_PointerMovedForTransform;
            _viewport.PointerPressed += Viewport_PointerPressedForTransform;
            _viewport.PointerReleased += Viewport_PointerReleasedForTransform;
            _viewport.PointerCanceled += Viewport_PointerReleasedForTransform;

            _viewport.Camera = new PerspectiveCamera
            {
                Position = new SDX.Vector3(50, 50, 50),
                LookDirection = new SDX.Vector3(-50, -50, -50),
                UpDirection = new SDX.Vector3(0, 1, 0),
                FarPlaneDistance = _savedFarPlane,
                NearPlaneDistance = _savedNearPlane,
                FieldOfView = _savedFieldOfView,
                CreateLeftHandSystem = true
            };

            if (_savedCameraPosition.HasValue && _viewport.Camera is PerspectiveCamera camera)
            {
                camera.Position = _savedCameraPosition.Value;
                if (_savedCameraLookDirection.HasValue) camera.LookDirection = _savedCameraLookDirection.Value;
                if (_savedCameraUpDirection.HasValue) camera.UpDirection = _savedCameraUpDirection.Value;
                camera.FarPlaneDistance = _savedFarPlane;
                camera.NearPlaneDistance = _savedNearPlane;
            }

            ApplySavedCameraSettingsToViewport();

            if (_modelGroup == null)
            {
                _modelGroup = new GroupModel3D();
            }
            
            RefreshModelList();
            
            _gridLines = CreateGrid();
            _viewport.Items.Add(_gridLines);
            
            _sceneLight = new DirectionalLight3D { Direction = new SDX.Vector3(-1, -1, -1), Color = Microsoft.UI.Colors.White };
            _fillLight = new DirectionalLight3D { Direction = new SDX.Vector3(1, -0.5f, -1), Color = Color.FromArgb(255, 128, 128, 128) };
            _backLight = new DirectionalLight3D { Direction = new SDX.Vector3(0, 1, 1), Color = Color.FromArgb(255, 64, 64, 64) };
            _ambientLight = new AmbientLight3D { Color = Color.FromArgb(255, 80, 80, 80) };
            // 6-axis directional lights used by the "Fully Lit Scene" toggle.
            // Each axis pair ensures every polygon face receives direct diffuse light.
            _fullbrightLights = new[]
            {
                new DirectionalLight3D { Direction = new SDX.Vector3( 1,  0,  0), Color = Microsoft.UI.Colors.White, IsRendering = false },
                new DirectionalLight3D { Direction = new SDX.Vector3(-1,  0,  0), Color = Microsoft.UI.Colors.White, IsRendering = false },
                new DirectionalLight3D { Direction = new SDX.Vector3( 0,  1,  0), Color = Microsoft.UI.Colors.White, IsRendering = false },
                new DirectionalLight3D { Direction = new SDX.Vector3( 0, -1,  0), Color = Microsoft.UI.Colors.White, IsRendering = false },
                new DirectionalLight3D { Direction = new SDX.Vector3( 0,  0,  1), Color = Microsoft.UI.Colors.White, IsRendering = false },
                new DirectionalLight3D { Direction = new SDX.Vector3( 0,  0, -1), Color = Microsoft.UI.Colors.White, IsRendering = false },
            };
            _viewport.Items.Add(_sceneLight);
            _viewport.Items.Add(_fillLight);
            _viewport.Items.Add(_backLight);
            _viewport.Items.Add(_ambientLight);
            foreach (var fl in _fullbrightLights) _viewport.Items.Add(fl);
            _viewport.Items.Add(_modelGroup);

            _highlightModel = new MeshGeometryModel3D
            {
                Material = new PhongMaterial 
                { 
                    DiffuseColor = new SDX.Color4(1f, 1f, 0f, 0.4f),
                    EmissiveColor = new SDX.Color4(1f, 0.5f, 0f, 1.0f)
                },
                Visibility = Visibility.Collapsed,
                CullMode = SDX.Direct3D11.CullMode.None
            };
            _viewport.Items.Add(_highlightModel);

            _lightHighlightModel = new LineGeometryModel3D
            {
                Color = Color.FromArgb(255, 255, 128, 0),
                Thickness = 3.0,
                Visibility = Visibility.Collapsed
            };
            _viewport.Items.Add(_lightHighlightModel);

            _viewport.EnableRenderOrder = true;
            _transformGizmo = new ViewportTransformGizmo();
            _transformGizmo.SetMode(_selectedGizmoMode);
            _viewport.Items.Add(_transformGizmo.Root);

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                if (_viewport != null && !ViewportContainer.Children.Contains(_viewport))
                    ViewportContainer.Children.Add(_viewport);
            });
        }

        private void Viewport_OnMouse3DDown(object? sender, MouseDown3DEventArgs e)
        {
            if (TryHandleActiveTransformMouse3DDown(e))
                return;

            if (TryBeginGizmoDrag(e))
                return;

            if (IsGizmoSelectionModeActive())
            {
                if (e.OriginalInputEventArgs is Microsoft.UI.Xaml.Input.PointerRoutedEventArgs routedArgs)
                    routedArgs.Handled = true;

                return;
            }

            bool isCtrlHeld = false;
            if (e.OriginalInputEventArgs is Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
            {
                var point = args.GetCurrentPoint(sender as UIElement);
                bool isLeft  = point.Properties.IsLeftButtonPressed;
                bool isRight = point.Properties.IsRightButtonPressed;
                if (!isLeft && !isRight) return;
                if (isRight) return;

                isCtrlHeld = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            }

            if (e.HitTestResult != null && e.HitTestResult.ModelHit is GeometryModel3D modelHit)
            {
                IViewerNode? node = null;
                if (!_hitTestMap.TryGetValue(modelHit, out node))
                    node = _renderMap.FirstOrDefault(x => x.Value == modelHit).Key;
                // Also resolve damage-cone hits to their LightGroupNode
                if (node == null)
                {
                    var dmgEntry = _lightDamageRenderMap.FirstOrDefault(x => (GeometryModel3D)x.Value == modelHit);
                    if (dmgEntry.Key != null) node = dmgEntry.Key;
                }
                // Resolve morph damage mesh hits to their DamageMeshNode
                if (node == null)
                {
                    var morphDmgEntry = _damageRenderMap.FirstOrDefault(x => x.Value == modelHit);
                    if (morphDmgEntry.Key != null) node = morphDmgEntry.Key;
                }
                if (node == null && _carbinHitMap.TryGetValue(modelHit, out var carbinNode))
                {
                    node = carbinNode;
                }
                if (node != null && !IsNodeInLoadedRoots(node))
                {
                    ClearSelection(syncTree: true);
                    return;
                }
                if (node != null)
                {
                    // Handle locator selection ? never multi-select
                    if (node is LocatorNode locatorNode)
                    {
                        ApplySingleSelection(locatorNode, syncTree: true);
                        return;
                    }

                    // Handle light group selection
                    if (node is LightGroupNode lightGroupNode)
                    {
                        if (isCtrlHeld)
                        {
                            // Ctrl+click: toggle light group in multi-select.
                            // Only clear the mesh multi-select so existing light selections survive.
                            if (_isMultiSelectActive)
                            {
                                _multiSelectedMeshes.Clear();
                                _multiSelectSnapshots.Clear();
                                _isMultiSelectActive = false;
                                RefreshModelList();
                            }
                            if (_multiSelectedLightGroups.Contains(lightGroupNode))
                                _multiSelectedLightGroups.Remove(lightGroupNode);
                            else
                                _multiSelectedLightGroups.Add(lightGroupNode);

                            if (_multiSelectedLightGroups.Count > 0)
                            {
                                ApplyLightMultiSelection(_multiSelectedLightGroups, primaryNode: lightGroupNode, syncTree: true);
                            }
                            else
                            {
                                ClearMultiLightSelection();
                                ClearSelectionState();
                            }
                        }
                        else
                        {
                            // Normal click: single select + highlight via SelectedNode
                            ApplySingleSelection(lightGroupNode, syncTree: true);
                        }
                        return;
                    }

                    // Resolve the clicked mesh node
                    MeshNode? clickedMesh = node as MeshNode;

                    // Handle damage mesh click — select node and sync properties panel
                    if (node is DamageMeshNode damageMeshNode)
                    {
                        ApplySingleSelection(damageMeshNode, syncTree: true);
                        return;
                    }

                    if (node is CarbinModelNode carbinModelNode)
                    {
                        ApplySingleSelection(carbinModelNode, syncTree: true);
                        return;
                    }

                    if (isCtrlHeld && clickedMesh != null)
                    {
                        if (SelectionModeToggle.IsChecked == true)
                        {
                            // ?? Ctrl+click in ModelBin mode: toggle all meshes of the parent ModelBin ??
                            var parent = clickedMesh.Parent;
                            while (parent != null && parent is not ModelBinNode)
                                parent = parent.Parent;

                            if (parent is ModelBinNode parentBin)
                            {
                                var binMeshes = new List<MeshNode>();
                                CollectMeshNodes(parentBin, binMeshes);

                                bool allAlreadySelected = binMeshes.All(m => _multiSelectedMeshes.Contains(m));
                                if (allAlreadySelected)
                                {
                                    foreach (var m in binMeshes)
                                        _multiSelectedMeshes.Remove(m);
                                }
                                else
                                {
                                    foreach (var m in binMeshes)
                                    {
                                        if (!_multiSelectedMeshes.Contains(m))
                                            _multiSelectedMeshes.Add(m);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // ?? Ctrl+click in Mesh mode: toggle single mesh in multi-selection ??
                            if (_multiSelectedMeshes.Contains(clickedMesh))
                                _multiSelectedMeshes.Remove(clickedMesh);
                            else
                                _multiSelectedMeshes.Add(clickedMesh);
                        }

                        if (_multiSelectedMeshes.Count > 0)
                        {
                            ApplyMeshMultiSelection(_multiSelectedMeshes, primaryNode: clickedMesh, syncTree: true);
                        }
                        else
                        {
                            ClearMultiSelection();
                            ClearSelectionState();
                        }
                        return;
                    }

                    // ?? Normal click (no Ctrl) ??
                    ClearMultiSelection();

                    if (SelectionModeToggle.IsChecked == true && clickedMesh != null)
                    {
                        // ModelBin selection mode: select parent ModelBinNode
                        var parent = clickedMesh.Parent;
                        while (parent != null && parent is not ModelBinNode)
                            parent = parent.Parent;

                        if (parent != null)
                        {
                            ApplySingleSelection(parent, syncTree: true);
                        }
                        else
                        {
                            ApplySingleSelection(node, syncTree: true);
                        }
                    }
                    else if (clickedMesh != null)
                    {
                        // Mesh selection mode: select individual mesh
                        ApplySingleSelection(clickedMesh, syncTree: true);
                    }
                    else
                    {
                        ApplySingleSelection(node, syncTree: true);
                    }
                }
            }
            else
            {
                ClearSelection(syncTree: true);
            }
        }

        private bool IsNodeInLoadedRoots(IViewerNode node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (ViewModel.Roots.Contains(current))
                    return true;
            }

            return false;
        }

        private void ClearMultiSelection()
        {
            bool wasActive = _isMultiSelectActive || _isMultiLightSelectActive;
            _multiSelectedMeshes.Clear();
            _multiSelectSnapshots.Clear();
            ClearModelScopeDeltaTransformState();
            _isMultiSelectActive = false;
            _multiSelectedLightGroups.Clear();
            _multiSelectLightSnapshots.Clear();
            _isMultiLightSelectActive = false;
            MultiSelectText.Visibility = Visibility.Collapsed;
            if (wasActive)
            {
                // Restore the real model/mesh selector lists
                RefreshModelList();
            }
        }

        private void ClearMultiLightSelection()
        {
            bool wasActive = _isMultiLightSelectActive;
            _multiSelectedLightGroups.Clear();
            _multiSelectLightSnapshots.Clear();
            _isMultiLightSelectActive = false;
            if (LightMultiSelectText != null)
                LightMultiSelectText.Visibility = Visibility.Collapsed;
            if (wasActive)
            {
                if (ViewModel.SelectedNode is LightGroupNode lg)
                {
                    _isUpdatingUi = true;
                    LightPartSelector.SelectedItem = lg;
                    _isUpdatingUi = false;
                    UpdateLightTransformUI(lg);
                }
                else
                {
                    RefreshModelList();
                }
            }
        }

        private void SnapshotMultiLightSelectValues()
        {
            _multiSelectLightSnapshots.Clear();
            foreach (var lg in _multiSelectedLightGroups)
                _multiSelectLightSnapshots[lg] = (lg.GroupData.Pos, lg.GroupData.Rot, lg.GroupData.DamagePos, lg.GroupData.DamageRot);
        }

        private void SnapshotMultiSelectValues()
        {
            _multiSelectSnapshots.Clear();
            foreach (var mesh in _multiSelectedMeshes)
            {
                if (mesh.GeometryData?.SourceMesh != null)
                {
                    var blob = mesh.GeometryData.SourceMesh;
                    _multiSelectSnapshots[mesh] = (blob.PositionScale, blob.PositionTranslate, mesh.GeometryData.RotationEulerDegrees);
                }
            }
        }
    }
}
