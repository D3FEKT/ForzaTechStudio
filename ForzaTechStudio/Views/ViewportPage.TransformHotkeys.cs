using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using SDX = SharpDX;
using Color = Windows.UI.Color;

namespace ForzaTechStudio.Views
{
    public sealed partial class ViewportPage : Page
    {
        private enum ViewportTransformMode
        {
            None,
            Translate,
            Rotate,
            Scale
        }

        private enum ViewportTransformAxis
        {
            None,
            X,
            Y,
            Z
        }

        private enum ViewportTransformAxisSpace
        {
            Global,
            Local
        }

        private enum ViewportTransformTargetKind
        {
            Mesh,
            Locator,
            AvPin,
            LightGroup
        }

        private sealed class ViewportTransformSession
        {
            public string Description { get; }
            public List<ViewportTransformTarget> Targets { get; }

            public ViewportTransformSession(string description, List<ViewportTransformTarget> targets)
            {
                Description = description;
                Targets = targets;
            }
        }

        private sealed class ViewportTransformTarget
        {
            public ViewportTransformTargetKind Kind { get; init; }
            public IViewerNode Node { get; init; } = default!;
            public MeshNode? Mesh { get; init; }
            public LocatorNode? Locator { get; init; }
            public AvPinNode? AvPin { get; init; }
            public LightGroupNode? LightGroup { get; init; }
            public bool CanScale { get; init; }
            public Vector3 OldPosition { get; init; }
            public Vector3 OldRotation { get; init; }
            public Vector3 OldScale { get; init; }
            public Vector4 OldMeshScale { get; init; }
            public Vector4 OldMeshTranslate { get; init; }
            public Vector3 OldMeshRotation { get; init; }
            public Matrix4x4 OldLocatorTransform { get; init; }
            public double OldAvPinPosX { get; init; }
            public double OldAvPinPosY { get; init; }
            public double OldAvPinPosZ { get; init; }
            public double OldAvPinAxisYaw { get; init; }
            public double OldAvPinAxisPitch { get; init; }
            public Vector4 OldLightPos { get; init; }
            public Vector4 OldLightRot { get; init; }
            public Vector4 OldLightDamagePos { get; init; }
            public Vector4 OldLightDamageRot { get; init; }
        }

        private ViewportTransformMode _activeTransformMode = ViewportTransformMode.None;
        private ViewportTransformAxis _activeTransformAxis = ViewportTransformAxis.None;
        private ViewportTransformAxisSpace _activeTransformAxisSpace = ViewportTransformAxisSpace.Global;
        private ViewportTransformSession? _activeTransformSession;
        private readonly List<ViewportTransformTarget> _activeTransformTargets = new();
        private Point _transformMouseAnchor;
        private Point _transformPivotScreen;
        private Vector3 _transformPivotWorld;
        private double _transformInitialMouseRadius;
        private double _transformInitialMouseAngle;
        private Point _lastViewportPointerPosition;
        private bool _hasLastViewportPointerPosition;
        private bool _suppressNextTransformMouse3DDown;
        private LineGeometryModel3D? _transformAxisGuide;
        private bool _savedTransformPanEnabled;
        private bool _savedTransformRotationEnabled;
        private bool _savedTransformZoomEnabled;
        private bool _savedTransformMoveEnabled;

        private bool IsViewportTransformActive => _activeTransformMode != ViewportTransformMode.None;

        private void ViewportPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (TryHandleTransformHotkey(e.Key, e.OriginalSource))
                e.Handled = true;
        }

        private void TransformKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            var focused = XamlRoot != null ? FocusManager.GetFocusedElement(XamlRoot) : null;
            if (TryHandleTransformHotkey(sender.Key, focused))
                args.Handled = true;
        }

        private bool TryHandleTransformHotkey(VirtualKey key, object? originalSource)
        {
            if (_isGizmoDragging)
            {
                if (key == VirtualKey.Escape)
                {
                    CancelActiveGizmoDrag();
                    return true;
                }

                if (key == VirtualKey.Enter)
                {
                    ConfirmActiveGizmoDrag();
                    return true;
                }
            }

            if (!IsViewportTransformActive && IsEditableKeyboardSource(originalSource))
                return false;

            if (IsControlOrAltDown())
                return false;

            if (IsViewportTransformActive)
            {
                switch (key)
                {
                    case VirtualKey.Escape:
                        CancelActiveViewportTransform();
                        return true;
                    case VirtualKey.Enter:
                        ConfirmActiveViewportTransform();
                        return true;
                    case VirtualKey.X:
                        CycleTransformAxis(ViewportTransformAxis.X);
                        return true;
                    case VirtualKey.Y:
                        CycleTransformAxis(ViewportTransformAxis.Y);
                        return true;
                    case VirtualKey.Z:
                        CycleTransformAxis(ViewportTransformAxis.Z);
                        return true;
                }

                return false;
            }

            switch (key)
            {
                case VirtualKey.G:
                    BeginViewportTransform(ViewportTransformMode.Translate);
                    return IsViewportTransformActive;
                case VirtualKey.R:
                    BeginViewportTransform(ViewportTransformMode.Rotate);
                    return IsViewportTransformActive;
                case VirtualKey.S:
                    BeginViewportTransform(ViewportTransformMode.Scale);
                    return IsViewportTransformActive;
            }

            return false;
        }

        private void Viewport_PointerMovedForTransform(object sender, PointerRoutedEventArgs e)
        {
            var position = GetViewportPointerPosition(e, sender as UIElement);
            _lastViewportPointerPosition = position;
            _hasLastViewportPointerPosition = true;

            if (_isGizmoDragging)
            {
                UpdateActiveGizmoDrag(position);
                e.Handled = true;
                return;
            }

            if (!IsViewportTransformActive)
            {
                if (_transformGizmo?.IsVisible == true)
                    UpdateTransformGizmoVisual(_gizmoPivotWorld);
                return;
            }

            UpdateActiveViewportTransform(position);
            e.Handled = true;
        }

        private void Viewport_PointerPressedForTransform(object sender, PointerRoutedEventArgs e)
        {
            var position = GetViewportPointerPosition(e, sender as UIElement);
            _lastViewportPointerPosition = position;
            _hasLastViewportPointerPosition = true;
            _viewport?.Focus(FocusState.Pointer);

            if (_isGizmoDragging)
            {
                var gizmoPointerTarget = sender as UIElement ?? (_viewport as UIElement);
                var gizmoPointerProperties = e.GetCurrentPoint(gizmoPointerTarget).Properties;
                if (gizmoPointerProperties.IsRightButtonPressed)
                {
                    CancelActiveGizmoDrag();
                    e.Handled = true;
                }
                return;
            }

            if (!IsViewportTransformActive)
                return;

            var pointerTarget = sender as UIElement ?? (_viewport as UIElement);
            var properties = e.GetCurrentPoint(pointerTarget).Properties;
            if (properties.IsLeftButtonPressed)
            {
                ConfirmActiveViewportTransform();
                _suppressNextTransformMouse3DDown = true;
                e.Handled = true;
            }
            else if (properties.IsRightButtonPressed)
            {
                CancelActiveViewportTransform();
                _suppressNextTransformMouse3DDown = true;
                e.Handled = true;
            }
        }

        private void Viewport_PointerReleasedForTransform(object sender, PointerRoutedEventArgs e)
        {
            if (!_isGizmoDragging)
                return;

            ConfirmActiveGizmoDrag();
            e.Handled = true;
        }

        private bool TryHandleActiveTransformMouse3DDown(MouseDown3DEventArgs e)
        {
            if (_suppressNextTransformMouse3DDown)
            {
                _suppressNextTransformMouse3DDown = false;
                return true;
            }

            if (!IsViewportTransformActive)
                return false;

            if (e.OriginalInputEventArgs is PointerRoutedEventArgs args)
            {
                var point = args.GetCurrentPoint(_viewport);
                if (point.Properties.IsLeftButtonPressed)
                {
                    ConfirmActiveViewportTransform();
                    args.Handled = true;
                    return true;
                }

                if (point.Properties.IsRightButtonPressed)
                {
                    CancelActiveViewportTransform();
                    args.Handled = true;
                    return true;
                }
            }

            return true;
        }

        private void CancelViewportTransformForUnload()
        {
            if (IsViewportTransformActive)
                CancelActiveViewportTransform();

            ClearTransformAxisGuide(removeFromViewport: true);
        }

        private void BeginViewportTransform(ViewportTransformMode mode)
        {
            var targets = GetViewportTransformTargets(mode);
            if (targets.Count == 0)
            {
                UpdateTransformHotkeyStatus();
                return;
            }

            var session = BeginViewportTransformSession(targets, GetTransformDescription(mode));
            if (session.Targets.Count == 0)
            {
                UpdateTransformHotkeyStatus();
                return;
            }

            _activeTransformMode = mode;
            _activeTransformAxis = ViewportTransformAxis.None;
            _activeTransformAxisSpace = ViewportTransformAxisSpace.Global;
            _activeTransformSession = session;
            _activeTransformTargets.Clear();
            _activeTransformTargets.AddRange(session.Targets);

            _transformPivotWorld = CalculateTransformPivot(_activeTransformTargets);
            _transformMouseAnchor = _hasLastViewportPointerPosition
                ? _lastViewportPointerPosition
                : GetFallbackViewportAnchor(_transformPivotWorld);

            if (!TryProjectWorldToScreen(_transformPivotWorld, out _transformPivotScreen))
                _transformPivotScreen = _transformMouseAnchor;

            _transformInitialMouseRadius = Math.Max(8.0, Distance(_transformMouseAnchor, _transformPivotScreen));
            _transformInitialMouseAngle = Math.Atan2(
                _transformMouseAnchor.Y - _transformPivotScreen.Y,
                _transformMouseAnchor.X - _transformPivotScreen.X);

            DisableViewportCameraInputForTransform();
            ClearTransformAxisGuide(removeFromViewport: false);
            _viewport?.Focus(FocusState.Programmatic);
            _transformGizmo?.SetVisible(false);
            UpdateTransformHotkeyStatus();
        }

        private void UpdateActiveViewportTransform(Point currentMouse)
        {
            if (_activeTransformSession == null)
                return;

            switch (_activeTransformMode)
            {
                case ViewportTransformMode.Translate:
                    ApplyTranslationPreview(CalculateTranslationDelta(currentMouse));
                    break;
                case ViewportTransformMode.Rotate:
                    ApplyRotationPreview(CalculateRotationDeltaDegrees(currentMouse));
                    break;
                case ViewportTransformMode.Scale:
                    ApplyScalePreview(CalculateScaleFactor(currentMouse));
                    break;
            }

                    RefreshViewportTransformSessionUI(_activeTransformSession);
            UpdateTransformAxisGuide();
            RefreshHighlight();
            RefreshOverlays();
        }

        private void ConfirmActiveViewportTransform()
        {
            var session = _activeTransformSession;
            if (session != null)
                CommitViewportTransformSession(session);

            RefreshViewportTransformSessionUI(session);
            ClearActiveViewportTransformState();
            RefreshHighlight();
            RefreshOverlays();
        }

        private void CancelActiveViewportTransform()
        {
            var session = _activeTransformSession;
            RestoreActiveTransformSnapshot();
            RefreshViewportTransformSessionUI(session);
            ClearActiveViewportTransformState();
            RefreshHighlight();
            RefreshOverlays();
        }

        private void ClearActiveViewportTransformState()
        {
            _activeTransformMode = ViewportTransformMode.None;
            _activeTransformAxis = ViewportTransformAxis.None;
            _activeTransformAxisSpace = ViewportTransformAxisSpace.Global;
            _activeTransformSession = null;
            _activeTransformTargets.Clear();
            ClearTransformAxisGuide(removeFromViewport: false);
            RestoreViewportCameraInputAfterTransform();
            UpdateTransformHotkeyStatus();
            UpdateTransformGizmoForSelection();
        }

        private void MoveTransformButton_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedGizmoMode(ViewportGizmoMode.Translate);
        }

        private void RotateTransformButton_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedGizmoMode(ViewportGizmoMode.Rotate);
        }

        private void ScaleTransformButton_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedGizmoMode(ViewportGizmoMode.Scale);
        }

        private void SetSelectedGizmoMode(ViewportGizmoMode mode)
        {
            if (mode == ViewportGizmoMode.None)
            {
                _selectedGizmoMode = ViewportGizmoMode.None;
                _transformGizmo?.SetVisible(false);
                _transformGizmo?.SetMode(ViewportGizmoMode.None);
                UpdateTransformOverlayButtons();
                return;
            }

            if (!RefreshTransformSelectionAvailability())
            {
                _selectedGizmoMode = ViewportGizmoMode.None;
                _transformGizmo?.SetMode(ViewportGizmoMode.None);
                _transformGizmo?.SetVisible(false);
                UpdateTransformOverlayButtons();
                return;
            }

            _selectedGizmoMode = _selectedGizmoMode == mode ? ViewportGizmoMode.None : mode;
            _transformGizmo?.SetMode(_selectedGizmoMode);
            UpdateTransformOverlayButtons();
            UpdateTransformGizmoForSelection();
        }

        private void UpdateTransformOverlayButtons()
        {
            var targets = GetViewportTransformTargets();
            var canTransform = targets.Count > 0;
            var canScale = targets.Any(target => target.CanScale);

            _hasTransformSelection = canTransform;
            if (!canTransform || (_selectedGizmoMode == ViewportGizmoMode.Scale && !canScale))
            {
                _selectedGizmoMode = ViewportGizmoMode.None;
                _transformGizmo?.SetVisible(false);
                _transformGizmo?.SetMode(ViewportGizmoMode.None);
            }

            SetTransformOverlayButton(MoveTransformButton, canTransform, canTransform && _selectedGizmoMode == ViewportGizmoMode.Translate);
            SetTransformOverlayButton(RotateTransformButton, canTransform, canTransform && _selectedGizmoMode == ViewportGizmoMode.Rotate);
            SetTransformOverlayButton(ScaleTransformButton, canScale, canScale && _selectedGizmoMode == ViewportGizmoMode.Scale);
        }

        private void SetTransformOverlayButton(Button button, bool enabled, bool active)
        {
            if (TryGetResourceStyle(active ? "AccentButtonStyle" : "SubtleButtonStyle", out var style))
                button.Style = style;

            button.IsEnabled = enabled;
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
            button.ClearValue(Control.BorderBrushProperty);

            if (!active)
                return;

            var accent = new SolidColorBrush(GetSystemAccentColor());
            button.Background = accent;
            button.BorderBrush = accent;
            button.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        }

        private bool HasTransformSelection()
        {
            return RefreshTransformSelectionAvailability();
        }

        private bool RefreshTransformSelectionAvailability(IReadOnlyList<ViewportTransformTarget>? knownTargets = null)
        {
            var targets = knownTargets ?? GetViewportTransformTargets();
            _hasTransformSelection = targets.Count > 0;
            if (!_hasTransformSelection)
            {
                _selectedGizmoMode = ViewportGizmoMode.None;
                _transformGizmo?.SetVisible(false);
                _transformGizmo?.SetMode(ViewportGizmoMode.None);
            }
            else if (_selectedGizmoMode == ViewportGizmoMode.Scale && !targets.Any(target => target.CanScale))
            {
                _selectedGizmoMode = ViewportGizmoMode.None;
                _transformGizmo?.SetVisible(false);
                _transformGizmo?.SetMode(ViewportGizmoMode.None);
            }

            UpdateTransformOverlayButtons();
            return _hasTransformSelection;
        }

        private bool IsGizmoSelectionModeActive()
        {
            return _selectedGizmoMode != ViewportGizmoMode.None && (_hasTransformSelection || RefreshTransformSelectionAvailability());
        }

        private bool TryGetResourceStyle(string key, out Style? style)
        {
            style = null;

            if (Resources.TryGetValue(key, out var localValue) && localValue is Style localStyle)
            {
                style = localStyle;
                return true;
            }

            if (Application.Current?.Resources.TryGetValue(key, out var appValue) == true && appValue is Style appStyle)
            {
                style = appStyle;
                return true;
            }

            return false;
        }

        private static Color GetSystemAccentColor()
        {
            if (Application.Current?.Resources.TryGetValue("SystemAccentColor", out var value) == true && value is Color color)
                return color;

            return Color.FromArgb(255, 0, 120, 215);
        }

        private bool TryBeginGizmoDrag(MouseDown3DEventArgs e)
        {
            if (_transformGizmo == null || !_transformGizmo.IsVisible || IsViewportTransformActive)
                return false;

            if (e.OriginalInputEventArgs is not PointerRoutedEventArgs args)
                return false;

            var point = args.GetCurrentPoint(_viewport);
            if (!point.Properties.IsLeftButtonPressed)
                return false;

            var position = point.Position;
            ViewportGizmoHandle handle;
            if (TryPickGizmoHandleFromScreen(position, out var screenHandle))
            {
                handle = screenHandle;
            }
            else if (e.HitTestResult?.ModelHit is GeometryModel3D hitModel &&
                _transformGizmo.TryResolveHandle(hitModel, out var hitHandle))
            {
                handle = hitHandle;
            }
            else
            {
                return false;
            }

            if (handle.Mode != _selectedGizmoMode)
                return false;

            if (!TryCreatePointerRay(position, out var rayOrigin, out var rayDirection))
                return false;

            var targets = GetViewportTransformTargets(ToTransformMode(_selectedGizmoMode));
            if (targets.Count == 0)
                return false;

            var session = BeginViewportTransformSession(targets, $"Gizmo {handle.Mode}");
            if (session.Targets.Count == 0)
                return false;

            _isGizmoDragging = true;
            _activeGizmoHandle = handle;
            _gizmoTransformSession = session;
            _gizmoTransformTargets.Clear();
            _gizmoTransformTargets.AddRange(session.Targets);
            _gizmoPivotWorld = _transformGizmo.Pivot;
            _transformMouseAnchor = position;

            InitializeGizmoDragMath(handle, rayOrigin, rayDirection);
            DisableViewportCameraInputForTransform();
            _viewport?.Focus(FocusState.Pointer);
            _viewport?.CapturePointer(args.Pointer);
            args.Handled = true;
            return true;
        }

        private void InitializeGizmoDragMath(ViewportGizmoHandle handle, Vector3 rayOrigin, Vector3 rayDirection)
        {
            _gizmoDragAxis = NormalizeOrDefault(handle.Axis, Vector3.UnitX);
            _gizmoDragPlaneNormal = NormalizeOrDefault(handle.PlaneNormal, Vector3.UnitY);
            _gizmoDragStartPlanePoint = _gizmoPivotWorld;
            _gizmoDragStartVector = Vector3.UnitX;
            _gizmoDragStartAxisParameter = 0f;

            if (handle.IsAxis)
            {
                _gizmoDragStartAxisParameter = ClosestParameterOnLineToRay(_gizmoPivotWorld, _gizmoDragAxis, rayOrigin, rayDirection);
            }
            else if (handle.IsPlane)
            {
                if (TryIntersectRayPlane(rayOrigin, rayDirection, _gizmoPivotWorld, _gizmoDragPlaneNormal, out var planePoint))
                    _gizmoDragStartPlanePoint = planePoint;
            }
            else if (handle.IsView || handle.Mode == ViewportGizmoMode.Rotate)
            {
                var axis = handle.IsView && TryGetCameraFrame(out _, out var forward, out _, out _)
                    ? forward
                    : _gizmoDragAxis;
                _gizmoDragAxis = NormalizeOrDefault(axis, Vector3.UnitZ);
                if (TryIntersectRayPlane(rayOrigin, rayDirection, _gizmoPivotWorld, _gizmoDragAxis, out var planePoint))
                    _gizmoDragStartVector = NormalizeOrDefault(planePoint - _gizmoPivotWorld, Vector3.UnitX);
            }
            else if (handle.IsUniform && TryGetCameraFrame(out _, out _, out var right, out _))
            {
                _gizmoDragAxis = right;
                _gizmoDragStartAxisParameter = ClosestParameterOnLineToRay(_gizmoPivotWorld, _gizmoDragAxis, rayOrigin, rayDirection);
            }
        }

        private bool TryPickGizmoHandleFromScreen(Point screenPoint, out ViewportGizmoHandle handle)
        {
            handle = default;
            if (_selectedGizmoMode == ViewportGizmoMode.None || _transformGizmo == null)
                return false;

            var pivot = _transformGizmo.Pivot;
            var scale = Math.Max(_gizmoVisualScale, 0.001f);
            var bestScore = double.MaxValue;
            var bestHandle = default(ViewportGizmoHandle);

            void Consider(ViewportGizmoHandle candidate, double score, double threshold)
            {
                if (score <= threshold && score < bestScore)
                {
                    bestScore = score;
                    bestHandle = candidate;
                }
            }

            double DistanceToProjectedSegment(Vector3 a, Vector3 b)
            {
                if (!TryProjectWorldToScreen(a, out var pa) || !TryProjectWorldToScreen(b, out var pb))
                    return double.MaxValue;

                return DistanceToSegment(screenPoint, pa, pb);
            }

            double DistanceToProjectedPoint(Vector3 p)
            {
                if (!TryProjectWorldToScreen(p, out var projected))
                    return double.MaxValue;

                return Distance(screenPoint, projected);
            }

            switch (_selectedGizmoMode)
            {
                case ViewportGizmoMode.Translate:
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Translate, ViewportGizmoHandleKind.AxisX, Vector3.UnitX, Vector3.Zero),
                        Math.Min(DistanceToProjectedSegment(pivot, pivot + Vector3.UnitX * (scale * 0.74f)), DistanceToProjectedPoint(pivot + Vector3.UnitX * (scale * 0.74f))), 32);
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Translate, ViewportGizmoHandleKind.AxisY, Vector3.UnitY, Vector3.Zero),
                        Math.Min(DistanceToProjectedSegment(pivot, pivot + Vector3.UnitY * (scale * 0.74f)), DistanceToProjectedPoint(pivot + Vector3.UnitY * (scale * 0.74f))), 32);
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Translate, ViewportGizmoHandleKind.AxisZ, Vector3.UnitZ, Vector3.Zero),
                        Math.Min(DistanceToProjectedSegment(pivot, pivot + Vector3.UnitZ * (scale * 0.74f)), DistanceToProjectedPoint(pivot + Vector3.UnitZ * (scale * 0.74f))), 32);

                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Translate, ViewportGizmoHandleKind.PlaneXY, Vector3.Zero, Vector3.UnitZ),
                        DistanceToProjectedPoint(pivot + (Vector3.UnitX + Vector3.UnitY) * (scale * 0.22f)), 34);
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Translate, ViewportGizmoHandleKind.PlaneXZ, Vector3.Zero, Vector3.UnitY),
                        DistanceToProjectedPoint(pivot + (Vector3.UnitX + Vector3.UnitZ) * (scale * 0.22f)), 34);
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Translate, ViewportGizmoHandleKind.PlaneYZ, Vector3.Zero, Vector3.UnitX),
                        DistanceToProjectedPoint(pivot + (Vector3.UnitY + Vector3.UnitZ) * (scale * 0.22f)), 34);
                    break;

                case ViewportGizmoMode.Scale:
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Scale, ViewportGizmoHandleKind.AxisX, Vector3.UnitX, Vector3.Zero),
                        Math.Min(DistanceToProjectedSegment(pivot, pivot + Vector3.UnitX * (scale * 0.72f)), DistanceToProjectedPoint(pivot + Vector3.UnitX * (scale * 0.72f))), 32);
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Scale, ViewportGizmoHandleKind.AxisY, Vector3.UnitY, Vector3.Zero),
                        Math.Min(DistanceToProjectedSegment(pivot, pivot + Vector3.UnitY * (scale * 0.72f)), DistanceToProjectedPoint(pivot + Vector3.UnitY * (scale * 0.72f))), 32);
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Scale, ViewportGizmoHandleKind.AxisZ, Vector3.UnitZ, Vector3.Zero),
                        Math.Min(DistanceToProjectedSegment(pivot, pivot + Vector3.UnitZ * (scale * 0.72f)), DistanceToProjectedPoint(pivot + Vector3.UnitZ * (scale * 0.72f))), 32);
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Scale, ViewportGizmoHandleKind.Uniform, Vector3.Zero, Vector3.Zero),
                        DistanceToProjectedPoint(pivot), 28);
                    break;

                case ViewportGizmoMode.Rotate:
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Rotate, ViewportGizmoHandleKind.AxisX, Vector3.UnitX, Vector3.Zero),
                        DistanceToProjectedRing(screenPoint, pivot, Vector3.UnitX, scale * 0.62f), 28);
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Rotate, ViewportGizmoHandleKind.AxisY, Vector3.UnitY, Vector3.Zero),
                        DistanceToProjectedRing(screenPoint, pivot, Vector3.UnitY, scale * 0.68f), 28);
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Rotate, ViewportGizmoHandleKind.AxisZ, Vector3.UnitZ, Vector3.Zero),
                        DistanceToProjectedRing(screenPoint, pivot, Vector3.UnitZ, scale * 0.74f), 28);
                    var viewAxis = TryGetCameraFrame(out _, out var forward, out _, out _) ? forward : Vector3.UnitZ;
                    Consider(new ViewportGizmoHandle(ViewportGizmoMode.Rotate, ViewportGizmoHandleKind.View, viewAxis, Vector3.Zero),
                        DistanceToProjectedRing(screenPoint, pivot, viewAxis, scale * 0.82f), 24);
                    break;
            }

            if (bestScore >= double.MaxValue)
                return false;

            handle = bestHandle;
            return true;
        }

        private double DistanceToProjectedRing(Point screenPoint, Vector3 center, Vector3 normal, float radius)
        {
            normal = NormalizeOrDefault(normal, Vector3.UnitZ);
            CreateScreenPickBasis(normal, out var u, out var v);

            var best = double.MaxValue;
            const int segments = 72;
            var previous = center + u * radius;
            for (int i = 1; i <= segments; i++)
            {
                var angle = (MathF.PI * 2f) * i / segments;
                var current = center + ((u * MathF.Cos(angle)) + (v * MathF.Sin(angle))) * radius;
                if (TryProjectWorldToScreen(previous, out var pa) && TryProjectWorldToScreen(current, out var pb))
                    best = Math.Min(best, DistanceToSegment(screenPoint, pa, pb));
                previous = current;
            }

            return best;
        }

        private static void CreateScreenPickBasis(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            normal = NormalizeOrDefault(normal, Vector3.UnitZ);
            var reference = MathF.Abs(Vector3.Dot(normal, Vector3.UnitY)) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
            u = NormalizeOrDefault(Vector3.Cross(reference, normal), Vector3.UnitX);
            v = NormalizeOrDefault(Vector3.Cross(normal, u), Vector3.UnitY);
        }

        private static double DistanceToSegment(Point point, Point a, Point b)
        {
            var abX = b.X - a.X;
            var abY = b.Y - a.Y;
            var lengthSquared = (abX * abX) + (abY * abY);
            if (lengthSquared <= 0.000001)
                return Distance(point, a);

            var t = (((point.X - a.X) * abX) + ((point.Y - a.Y) * abY)) / lengthSquared;
            t = Math.Clamp(t, 0.0, 1.0);
            var closest = new Point(a.X + (abX * t), a.Y + (abY * t));
            return Distance(point, closest);
        }

        private void UpdateActiveGizmoDrag(Point currentMouse)
        {
            if (!_isGizmoDragging || _gizmoTransformSession == null ||
                !TryCreatePointerRay(currentMouse, out var rayOrigin, out var rayDirection))
            {
                return;
            }

            switch (_activeGizmoHandle.Mode)
            {
                case ViewportGizmoMode.Translate:
                    UpdateGizmoTranslationDrag(rayOrigin, rayDirection);
                    break;
                case ViewportGizmoMode.Rotate:
                    UpdateGizmoRotationDrag(rayOrigin, rayDirection);
                    break;
                case ViewportGizmoMode.Scale:
                    UpdateGizmoScaleDrag(currentMouse, rayOrigin, rayDirection);
                    break;
            }

                    RefreshViewportTransformSessionUI(_gizmoTransformSession);
            RefreshHighlight();
            RefreshOverlays();
        }

        private void UpdateGizmoTranslationDrag(Vector3 rayOrigin, Vector3 rayDirection)
        {
            Vector3 delta = Vector3.Zero;

            if (_activeGizmoHandle.IsAxis)
            {
                var current = ClosestParameterOnLineToRay(_gizmoPivotWorld, _gizmoDragAxis, rayOrigin, rayDirection);
                delta = _gizmoDragAxis * (current - _gizmoDragStartAxisParameter);
            }
            else if (_activeGizmoHandle.IsPlane &&
                     TryIntersectRayPlane(rayOrigin, rayDirection, _gizmoPivotWorld, _gizmoDragPlaneNormal, out var point))
            {
                delta = point - _gizmoDragStartPlanePoint;
            }

            ApplyGizmoTranslationPreview(delta);
            UpdateTransformGizmoVisual(_gizmoPivotWorld + delta);
        }

        private void UpdateGizmoRotationDrag(Vector3 rayOrigin, Vector3 rayDirection)
        {
            if (!TryIntersectRayPlane(rayOrigin, rayDirection, _gizmoPivotWorld, _gizmoDragAxis, out var point))
                return;

            var currentVector = NormalizeOrDefault(point - _gizmoPivotWorld, _gizmoDragStartVector);
            var cross = Vector3.Cross(_gizmoDragStartVector, currentVector);
            var radians = MathF.Atan2(Vector3.Dot(cross, _gizmoDragAxis), Vector3.Dot(_gizmoDragStartVector, currentVector));
            ApplyGizmoRotationPreview(_gizmoDragAxis, radians * 180f / MathF.PI);
            UpdateTransformGizmoVisual(_gizmoPivotWorld);
        }

        private void UpdateGizmoScaleDrag(Point currentMouse, Vector3 rayOrigin, Vector3 rayDirection)
        {
            float factor;
            if (_activeGizmoHandle.IsUniform)
            {
                factor = (float)Math.Clamp(1.0 + ((currentMouse.X - _transformMouseAnchor.X) * 0.01), 0.001, 1000.0);
            }
            else
            {
                var current = ClosestParameterOnLineToRay(_gizmoPivotWorld, _gizmoDragAxis, rayOrigin, rayDirection);
                var delta = current - _gizmoDragStartAxisParameter;
                factor = (float)Math.Clamp(1.0 + (delta / Math.Max(_gizmoVisualScale * 0.7f, 0.001f)), 0.001, 1000.0);
            }

            ApplyGizmoScalePreview(factor);
            UpdateTransformGizmoVisual(_gizmoPivotWorld);
        }

        private void ConfirmActiveGizmoDrag()
        {
            if (!_isGizmoDragging)
                return;

            var session = _gizmoTransformSession;
            if (session != null)
                CommitViewportTransformSession(session);

            RefreshViewportTransformSessionUI(session);
            ClearActiveGizmoDragState();
            RefreshHighlight();
            RefreshOverlays();
            UpdateTransformGizmoForSelection();
        }

        private void CancelActiveGizmoDrag()
        {
            if (!_isGizmoDragging)
                return;

            RestoreGizmoTransformSnapshot();
            RefreshViewportTransformSessionUI(_gizmoTransformSession);
            ClearActiveGizmoDragState();
            RefreshHighlight();
            RefreshOverlays();
            UpdateTransformGizmoForSelection();
        }

        private void ClearActiveGizmoDragState()
        {
            _isGizmoDragging = false;
            _activeGizmoHandle = default;
            _gizmoTransformSession = null;
            _gizmoTransformTargets.Clear();
            _viewport?.ReleasePointerCaptures();
            RestoreViewportCameraInputAfterTransform();
        }

        private void RestoreGizmoTransformSnapshot()
        {
            if (_gizmoTransformSession == null)
                return;

            RestoreTransformSnapshot(_gizmoTransformSession);
        }

        private void ApplyGizmoTranslationPreview(Vector3 delta)
        {
            if (_gizmoTransformSession == null)
                return;

            foreach (var target in _gizmoTransformSession.Targets)
                ApplyTargetTranslationPreview(target, delta);
        }

        private void ApplyGizmoRotationPreview(Vector3 axis, float deltaDegrees)
        {
            if (_gizmoTransformSession == null)
                return;

            foreach (var target in _gizmoTransformSession.Targets)
                ApplyTargetRotationPreview(target, ComposeTargetRotationEulerAroundAxis(target, axis, deltaDegrees));
        }

        private void ApplyGizmoScalePreview(float factor)
        {
            if (_gizmoTransformSession == null)
                return;

            foreach (var target in _gizmoTransformSession.Targets)
            {
                if (!target.CanScale)
                    continue;

                var scale = target.OldScale;
                if (_activeGizmoHandle.IsUniform)
                {
                    scale.X *= factor;
                    scale.Y *= factor;
                    scale.Z *= factor;
                }
                else
                {
                    switch (_activeGizmoHandle.Kind)
                    {
                        case ViewportGizmoHandleKind.AxisX:
                            scale.X *= factor;
                            break;
                        case ViewportGizmoHandleKind.AxisY:
                            scale.Y *= factor;
                            break;
                        case ViewportGizmoHandleKind.AxisZ:
                            scale.Z *= factor;
                            break;
                    }
                }

                ApplyTargetScalePreview(target, scale);
            }
        }

        private Vector3 ComposeTargetRotationEulerAroundAxis(ViewportTransformTarget target, Vector3 axis, float deltaDegrees)
        {
            var initial = GetTargetRotationMatrix(target);
            var radians = deltaDegrees * MathF.PI / 180f;
            var axisRotation = Matrix4x4.CreateFromAxisAngle(NormalizeOrDefault(axis, Vector3.UnitZ), radians);
            return ExtractRenderEulerDegrees(initial * axisRotation);
        }

        private void UpdateTransformGizmoForSelection()
        {
            var targets = GetViewportTransformTargets(ToTransformMode(_selectedGizmoMode));
            UpdateTransformGizmoForTargets(targets);
        }

        private void UpdateTransformGizmoForTargets(IReadOnlyList<ViewportTransformTarget> targets)
        {
            if (_transformGizmo == null)
                return;

            _hasTransformSelection = targets.Count > 0;
            if (!_hasTransformSelection)
                _selectedGizmoMode = ViewportGizmoMode.None;

            if (IsViewportTransformActive || _isGizmoDragging || targets.Count == 0 || _selectedGizmoMode == ViewportGizmoMode.None)
            {
                _transformGizmo.SetVisible(false);
                if (_selectedGizmoMode == ViewportGizmoMode.None)
                    _transformGizmo.SetMode(ViewportGizmoMode.None);
                UpdateTransformOverlayButtons();
                return;
            }

            var session = BeginViewportTransformSession(targets, "Gizmo Pivot");
            if (session.Targets.Count == 0)
            {
                _hasTransformSelection = false;
                _selectedGizmoMode = ViewportGizmoMode.None;
                _transformGizmo.SetVisible(false);
                _transformGizmo.SetMode(ViewportGizmoMode.None);
                UpdateTransformOverlayButtons();
                return;
            }

            var pivot = CalculateTransformPivot(session.Targets);
            _gizmoPivotWorld = pivot;
            _transformGizmo.SetMode(_selectedGizmoMode);
            UpdateTransformGizmoVisual(pivot);
            _transformGizmo.SetVisible(true);
            UpdateTransformOverlayButtons();
        }

        private void HideTransformGizmo()
        {
            _hasTransformSelection = false;
            _selectedGizmoMode = ViewportGizmoMode.None;
            _transformGizmo?.SetVisible(false);
            _transformGizmo?.SetMode(ViewportGizmoMode.None);
            UpdateTransformOverlayButtons();
        }

        private void UpdateTransformGizmoVisual(Vector3 pivot)
        {
            if (_transformGizmo == null)
                return;

            var scale = CalculateGizmoVisualScale(pivot);
            _gizmoVisualScale = scale;
            var forward = TryGetCameraFrame(out _, out var cameraForward, out _, out _)
                ? cameraForward
                : Vector3.UnitZ;
            _transformGizmo.Update(pivot, scale, forward);
        }

        private float CalculateGizmoVisualScale(Vector3 pivot)
        {
            if (TryGetWorldUnitsPerPixelAt(pivot, out var unitsX, out var unitsY))
                return (float)Math.Max(0.01, Math.Max(unitsX, unitsY) * 72.0);

            if (TryGetCameraFrame(out var cameraPosition, out _, out _, out _))
                return Math.Max(0.01f, Vector3.Distance(cameraPosition, pivot) * 0.06f);

            return 1f;
        }

        private void UpdateTransformHotkeyStatus()
        {
            if (TransformHotkeyStatusBadge == null || TransformHotkeyStatusText == null)
                return;

            if (!IsViewportTransformActive)
            {
                TransformHotkeyStatusBadge.Visibility = Visibility.Collapsed;
                TransformHotkeyStatusText.Text = string.Empty;
                return;
            }

            var modeText = _activeTransformMode switch
            {
                ViewportTransformMode.Translate => "Position",
                ViewportTransformMode.Rotate => "Rotation",
                ViewportTransformMode.Scale => "Scale",
                _ => string.Empty
            };

            var axisText = _activeTransformAxis switch
            {
                ViewportTransformAxis.X => " X",
                ViewportTransformAxis.Y => " Y",
                ViewportTransformAxis.Z => " Z",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(axisText) && _activeTransformAxisSpace == ViewportTransformAxisSpace.Local)
                axisText += " Local";

            TransformHotkeyStatusText.Text = $"{modeText}{axisText}";
            TransformHotkeyStatusBadge.Visibility = Visibility.Visible;
        }

        private void RestoreActiveTransformSnapshot()
        {
            if (_activeTransformSession == null)
                return;

            RestoreTransformSnapshot(_activeTransformSession);
        }

        private void CycleTransformAxis(ViewportTransformAxis axis)
        {
            if (_activeTransformAxis != axis)
            {
                _activeTransformAxis = axis;
                _activeTransformAxisSpace = ViewportTransformAxisSpace.Global;
            }
            else if (_activeTransformAxisSpace == ViewportTransformAxisSpace.Global)
            {
                _activeTransformAxisSpace = ViewportTransformAxisSpace.Local;
            }
            else
            {
                _activeTransformAxis = ViewportTransformAxis.None;
                _activeTransformAxisSpace = ViewportTransformAxisSpace.Global;
            }

            UpdateTransformAxisGuide();
            UpdateTransformHotkeyStatus();

            if (_activeTransformSession != null && _hasLastViewportPointerPosition)
                UpdateActiveViewportTransform(_lastViewportPointerPosition);
        }

        private void ApplyTranslationPreview(Vector3 delta)
        {
            if (_activeTransformSession == null)
                return;

            foreach (var target in _activeTransformSession.Targets)
                ApplyTargetTranslationPreview(target, delta);
        }

        private void ApplyRotationPreview(float deltaDegrees)
        {
            if (_activeTransformSession == null)
                return;

            foreach (var target in _activeTransformSession.Targets)
                ApplyTargetRotationPreview(target, ComposeRotationEuler(target, deltaDegrees));
        }

        private void ApplyScalePreview(float scaleFactor)
        {
            if (_activeTransformSession == null)
                return;

            foreach (var target in _activeTransformSession.Targets)
            {
                if (!target.CanScale)
                    continue;

                var newScale = target.OldScale;

                if (_activeTransformAxis == ViewportTransformAxis.None)
                {
                    newScale.X *= scaleFactor;
                    newScale.Y *= scaleFactor;
                    newScale.Z *= scaleFactor;
                }
                else
                {
                    switch (_activeTransformAxis)
                    {
                        case ViewportTransformAxis.X:
                            newScale.X *= scaleFactor;
                            break;
                        case ViewportTransformAxis.Y:
                            newScale.Y *= scaleFactor;
                            break;
                        case ViewportTransformAxis.Z:
                            newScale.Z *= scaleFactor;
                            break;
                    }
                }

                ApplyTargetScalePreview(target, newScale);
            }
        }

        private void RefreshViewportTransformSessionUI(ViewportTransformSession? session)
        {
            if (session == null || session.Targets.Count == 0)
                return;

            RefreshViewportTransformTargetUI(session.Targets);
        }

        private void RefreshViewportTransformTargetUI(IReadOnlyList<ViewportTransformTarget> targets)
        {
            if (targets.Count == 0)
                return;

            if (targets.Count == 1)
            {
                RefreshViewportTransformTargetUI(targets[0]);
                return;
            }

            if (targets.All(target => target.Kind == ViewportTransformTargetKind.Mesh))
            {
                UpdateTransformUI();
            }
            else if (targets.All(target => target.Kind == ViewportTransformTargetKind.LightGroup) && _isMultiLightSelectActive)
            {
                UpdateTransformUIForMultiLightSelect();
            }
        }

        private void RefreshViewportTransformTargetUI(ViewportTransformTarget target)
        {
            switch (target.Kind)
            {
                case ViewportTransformTargetKind.Mesh:
                    UpdateTransformUI();
                    break;

                case ViewportTransformTargetKind.Locator when target.Locator != null:
                    PopulateMatrixFields(target.Locator.LocatorEntry.SceneTransform);
                    break;

                case ViewportTransformTargetKind.AvPin when target.AvPin?.PoiData?.Visibility != null:
                    PopulateAvPinFields(target.AvPin.PoiData.Visibility);
                    break;

                case ViewportTransformTargetKind.LightGroup when target.LightGroup != null:
                    UpdateLightTransformUI(target.LightGroup);
                    break;
            }
        }

        private Vector3 CalculateTranslationDelta(Point currentMouse)
        {
            if (_activeTransformAxis != ViewportTransformAxis.None)
                return CalculateAxisTranslationDelta(currentMouse);

            if (!TryGetCameraFrame(out _, out _, out var right, out var up))
                return Vector3.Zero;

            if (!TryGetWorldUnitsPerPixelAt(_transformPivotWorld, out var unitsPerPixelX, out var unitsPerPixelY))
                return Vector3.Zero;

            var dx = currentMouse.X - _transformMouseAnchor.X;
            var dy = currentMouse.Y - _transformMouseAnchor.Y;
            return (right * (float)(dx * unitsPerPixelX)) - (up * (float)(dy * unitsPerPixelY));
        }

        private Vector3 CalculateAxisTranslationDelta(Point currentMouse)
        {
            if (_activeTransformSession == null || _activeTransformSession.Targets.Count == 0)
                return Vector3.Zero;

            var axis = GetActiveAxisDirection(_activeTransformSession.Targets[0]);
            if (!TryProjectWorldToScreen(_transformPivotWorld, out var pivotScreen) ||
                !TryProjectWorldToScreen(_transformPivotWorld + axis, out var axisScreen))
            {
                return Vector3.Zero;
            }

            var screenAxis = new Vector2(
                (float)(axisScreen.X - pivotScreen.X),
                (float)(axisScreen.Y - pivotScreen.Y));

            var pixelsPerWorld = screenAxis.Length();
            if (pixelsPerWorld <= 0.001f)
                return Vector3.Zero;

            screenAxis /= pixelsPerWorld;

            var mouseDelta = new Vector2(
                (float)(currentMouse.X - _transformMouseAnchor.X),
                (float)(currentMouse.Y - _transformMouseAnchor.Y));

            var signedPixels = Vector2.Dot(mouseDelta, screenAxis);
            return axis * (signedPixels / pixelsPerWorld);
        }

        private float CalculateRotationDeltaDegrees(Point currentMouse)
        {
            if (_activeTransformAxis != ViewportTransformAxis.None)
                return (float)((currentMouse.X - _transformMouseAnchor.X) * 0.5);

            var radius = Distance(currentMouse, _transformPivotScreen);
            if (_transformInitialMouseRadius < 12.0 || radius < 4.0)
                return (float)((currentMouse.X - _transformMouseAnchor.X) * 0.5);

            var angle = Math.Atan2(
                currentMouse.Y - _transformPivotScreen.Y,
                currentMouse.X - _transformPivotScreen.X);

            return NormalizeAngleDegrees((float)((angle - _transformInitialMouseAngle) * 180.0 / Math.PI));
        }

        private float CalculateScaleFactor(Point currentMouse)
        {
            var radius = Distance(currentMouse, _transformPivotScreen);
            double factor;

            if (_transformInitialMouseRadius < 12.0)
            {
                factor = 1.0 + ((currentMouse.X - _transformMouseAnchor.X) * 0.01);
            }
            else
            {
                factor = radius / _transformInitialMouseRadius;
            }

            return (float)Math.Clamp(factor, 0.001, 1000.0);
        }

        private Vector3 ComposeRotationEuler(ViewportTransformTarget target, float deltaDegrees)
        {
            var initial = GetTargetRotationMatrix(target);
            var radians = deltaDegrees * MathF.PI / 180f;

            Matrix4x4 next;
            if (_activeTransformAxis == ViewportTransformAxis.None)
            {
                var viewAxis = TryGetCameraFrame(out _, out var forward, out _, out _)
                    ? forward
                    : Vector3.UnitZ;
                var viewRotation = Matrix4x4.CreateFromAxisAngle(NormalizeOrDefault(viewAxis, Vector3.UnitZ), radians);
                next = initial * viewRotation;
            }
            else
            {
                var axis = GetAxisUnitVector(_activeTransformAxis);
                var axisRotation = Matrix4x4.CreateFromAxisAngle(axis, radians);
                next = _activeTransformAxisSpace == ViewportTransformAxisSpace.Local
                    ? axisRotation * initial
                    : initial * axisRotation;
            }

            return ExtractRenderEulerDegrees(next);
        }

        private void RefreshModalTransformMesh(MeshNode mesh)
        {
            var geometry = mesh.GeometryData;
            if (geometry?.SourceMesh == null)
                return;

            bool hasBone = geometry.SourceBone != null &&
                           BoneTransformService.IsSignificantBone(geometry.BoneIndex);

            if (hasBone)
            {
                UpdateMeshRenderingWithBoneTransform(mesh, geometry.BoneTransform);
            }
            else
            {
                var meshBlob = geometry.SourceMesh;
                UpdateMeshRendering(mesh,
                    meshBlob.PositionScale.X,
                    meshBlob.PositionScale.Y,
                    meshBlob.PositionScale.Z,
                    meshBlob.PositionTranslate.X,
                    meshBlob.PositionTranslate.Y,
                    meshBlob.PositionTranslate.Z);
            }
        }

        private ViewportTransformSession BeginViewportTransformSession(IReadOnlyList<ViewportTransformTarget> targets, string description)
        {
            return new ViewportTransformSession(description, targets.ToList());
        }

        private ViewportTransformTarget? CreateViewportTransformTarget(IViewerNode node)
        {
            switch (node)
            {
                case MeshNode mesh when mesh.GeometryData?.SourceMesh != null:
                    return new ViewportTransformTarget
                    {
                        Kind = ViewportTransformTargetKind.Mesh,
                        Node = mesh,
                        Mesh = mesh,
                        CanScale = true,
                        OldPosition = ToVector3(mesh.GeometryData.SourceMesh.PositionTranslate),
                        OldRotation = mesh.GeometryData.RotationEulerDegrees,
                        OldScale = ToVector3(mesh.GeometryData.SourceMesh.PositionScale),
                        OldMeshScale = mesh.GeometryData.SourceMesh.PositionScale,
                        OldMeshTranslate = mesh.GeometryData.SourceMesh.PositionTranslate,
                        OldMeshRotation = mesh.GeometryData.RotationEulerDegrees
                    };

                case LocatorNode locator:
                    DecomposeTransformMatrix(locator.LocatorEntry.SceneTransform, out var locatorPosition, out var locatorRotation, out var locatorScale);
                    return new ViewportTransformTarget
                    {
                        Kind = ViewportTransformTargetKind.Locator,
                        Node = locator,
                        Locator = locator,
                        CanScale = true,
                        OldPosition = locatorPosition,
                        OldRotation = locatorRotation,
                        OldScale = locatorScale,
                        OldLocatorTransform = locator.LocatorEntry.SceneTransform
                    };

                case AvPinNode avPin when avPin.PoiData?.Visibility != null:
                    var visibility = avPin.PoiData.Visibility;
                    return new ViewportTransformTarget
                    {
                        Kind = ViewportTransformTargetKind.AvPin,
                        Node = avPin,
                        AvPin = avPin,
                        CanScale = false,
                        OldPosition = new Vector3((float)visibility.PosX, (float)visibility.PosY, (float)visibility.PosZ),
                        OldRotation = new Vector3((float)visibility.AxisPitch, (float)visibility.AxisYaw, 0f),
                        OldScale = Vector3.One,
                        OldAvPinPosX = visibility.PosX,
                        OldAvPinPosY = visibility.PosY,
                        OldAvPinPosZ = visibility.PosZ,
                        OldAvPinAxisYaw = visibility.AxisYaw,
                        OldAvPinAxisPitch = visibility.AxisPitch
                    };

                case LightGroupNode lightGroup when lightGroup.GroupData != null:
                    return new ViewportTransformTarget
                    {
                        Kind = ViewportTransformTargetKind.LightGroup,
                        Node = lightGroup,
                        LightGroup = lightGroup,
                        CanScale = false,
                        OldPosition = ToVector3(lightGroup.GroupData.Pos),
                        OldRotation = QuaternionToEulerDegrees(lightGroup.GroupData.Rotation),
                        OldScale = Vector3.One,
                        OldLightPos = lightGroup.GroupData.Pos,
                        OldLightRot = lightGroup.GroupData.Rot,
                        OldLightDamagePos = lightGroup.GroupData.DamagePos,
                        OldLightDamageRot = lightGroup.GroupData.DamageRot
                    };
            }

            return null;
        }

        private void ApplyTargetTranslationPreview(ViewportTransformTarget target, Vector3 delta)
        {
            switch (target.Kind)
            {
                case ViewportTransformTargetKind.Mesh when target.Mesh?.GeometryData?.SourceMesh != null:
                    target.Mesh.GeometryData.SourceMesh.PositionTranslate = ApplyWorldTranslationDelta(target.Mesh, target.OldMeshTranslate, delta);
                    RefreshModalTransformMesh(target.Mesh);
                    break;

                case ViewportTransformTargetKind.Locator when target.Locator != null:
                    target.Locator.LocatorEntry.SceneTransform = ComposeTransformMatrix(
                        target.OldPosition + delta,
                        target.OldRotation,
                        target.OldScale,
                        target.OldLocatorTransform);
                    RefreshLocatorCone(target.Locator);
                    break;

                case ViewportTransformTargetKind.AvPin when target.AvPin?.PoiData?.Visibility != null:
                    var visibility = target.AvPin.PoiData.Visibility;
                    visibility.PosX = target.OldAvPinPosX + delta.X;
                    visibility.PosY = target.OldAvPinPosY + delta.Y;
                    visibility.PosZ = target.OldAvPinPosZ + delta.Z;
                    RefreshAvPinCone(target.AvPin);
                    break;

                case ViewportTransformTargetKind.LightGroup when target.LightGroup?.GroupData != null:
                    var group = target.LightGroup.GroupData;
                    group.Pos = AddVector3(target.OldLightPos, delta);
                    group.DamagePos = AddVector3(target.OldLightDamagePos, delta);
                    RefreshLightTransformTarget(target.LightGroup);
                    break;
            }
        }

        private void ApplyTargetRotationPreview(ViewportTransformTarget target, Vector3 rotationDegrees)
        {
            switch (target.Kind)
            {
                case ViewportTransformTargetKind.Mesh when target.Mesh?.GeometryData?.SourceMesh != null:
                    target.Mesh.GeometryData.RotationEulerDegrees = rotationDegrees;
                    RefreshModalTransformMesh(target.Mesh);
                    break;

                case ViewportTransformTargetKind.Locator when target.Locator != null:
                    target.Locator.LocatorEntry.SceneTransform = ComposeTransformMatrix(
                        target.OldPosition,
                        rotationDegrees,
                        target.OldScale,
                        target.OldLocatorTransform);
                    RefreshLocatorCone(target.Locator);
                    break;

                case ViewportTransformTargetKind.AvPin when target.AvPin?.PoiData?.Visibility != null:
                    var visibility = target.AvPin.PoiData.Visibility;
                    visibility.AxisPitch = rotationDegrees.X;
                    visibility.AxisYaw = rotationDegrees.Y;
                    RefreshAvPinCone(target.AvPin);
                    break;

                case ViewportTransformTargetKind.LightGroup when target.LightGroup?.GroupData != null:
                    var rotation = EulerDegreesToQuaternion(rotationDegrees);
                    var rotationVector = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W);
                    target.LightGroup.GroupData.Rot = rotationVector;
                    target.LightGroup.GroupData.DamageRot = rotationVector;
                    RefreshLightTransformTarget(target.LightGroup);
                    break;
            }
        }

        private void ApplyTargetScalePreview(ViewportTransformTarget target, Vector3 scale)
        {
            switch (target.Kind)
            {
                case ViewportTransformTargetKind.Mesh when target.Mesh?.GeometryData?.SourceMesh != null:
                    target.Mesh.GeometryData.SourceMesh.PositionScale = new Vector4(scale, target.OldMeshScale.W);
                    RefreshModalTransformMesh(target.Mesh);
                    break;

                case ViewportTransformTargetKind.Locator when target.Locator != null:
                    target.Locator.LocatorEntry.SceneTransform = ComposeTransformMatrix(
                        target.OldPosition,
                        target.OldRotation,
                        scale,
                        target.OldLocatorTransform);
                    RefreshLocatorCone(target.Locator);
                    break;
            }
        }

        private void RestoreTransformSnapshot(ViewportTransformSession session)
        {
            foreach (var target in session.Targets)
            {
                switch (target.Kind)
                {
                    case ViewportTransformTargetKind.Mesh when target.Mesh?.GeometryData?.SourceMesh != null:
                        target.Mesh.GeometryData.SourceMesh.PositionScale = target.OldMeshScale;
                        target.Mesh.GeometryData.SourceMesh.PositionTranslate = target.OldMeshTranslate;
                        target.Mesh.GeometryData.RotationEulerDegrees = target.OldMeshRotation;
                        RefreshModalTransformMesh(target.Mesh);
                        break;

                    case ViewportTransformTargetKind.Locator when target.Locator != null:
                        target.Locator.LocatorEntry.SceneTransform = target.OldLocatorTransform;
                        RefreshLocatorCone(target.Locator);
                        break;

                    case ViewportTransformTargetKind.AvPin when target.AvPin?.PoiData?.Visibility != null:
                        var visibility = target.AvPin.PoiData.Visibility;
                        visibility.PosX = target.OldAvPinPosX;
                        visibility.PosY = target.OldAvPinPosY;
                        visibility.PosZ = target.OldAvPinPosZ;
                        visibility.AxisYaw = target.OldAvPinAxisYaw;
                        visibility.AxisPitch = target.OldAvPinAxisPitch;
                        RefreshAvPinCone(target.AvPin);
                        break;

                    case ViewportTransformTargetKind.LightGroup when target.LightGroup?.GroupData != null:
                        var group = target.LightGroup.GroupData;
                        group.Pos = target.OldLightPos;
                        group.Rot = target.OldLightRot;
                        group.DamagePos = target.OldLightDamagePos;
                        group.DamageRot = target.OldLightDamageRot;
                        RefreshLightTransformTarget(target.LightGroup);
                        break;
                }
            }
        }

        private void CommitViewportTransformSession(ViewportTransformSession session)
        {
            CommitMeshTransformTargets(session);
            CommitLocatorTransformTargets(session);
            CommitAvPinTransformTargets(session);
            CommitLightTransformTargets(session);
        }

        private void CommitMeshTransformTargets(ViewportTransformSession session)
        {
            var entries = new List<MeshTransformEntry>();
            foreach (var target in session.Targets.Where(target => target.Kind == ViewportTransformTargetKind.Mesh))
            {
                if (target.Mesh?.GeometryData?.SourceMesh == null)
                    continue;

                var source = target.Mesh.GeometryData.SourceMesh;
                entries.Add(new MeshTransformEntry
                {
                    Mesh = target.Mesh,
                    OldScale = target.OldMeshScale,
                    OldTranslate = target.OldMeshTranslate,
                    OldRotation = target.OldMeshRotation,
                    NewScale = source.PositionScale,
                    NewTranslate = source.PositionTranslate,
                    NewRotation = target.Mesh.GeometryData.RotationEulerDegrees
                });
            }

            entries = entries
                .Where(entry => entry.OldScale != entry.NewScale || entry.OldTranslate != entry.NewTranslate || entry.OldRotation != entry.NewRotation)
                .ToList();

            if (entries.Count == 0)
                return;

            UpdateModelBinDirtyState(entries.Select(entry => entry.Mesh!).Where(mesh => mesh != null));
            PushUndo(new MeshTransformAction(session.Description, entries));
        }

        private void CommitLocatorTransformTargets(ViewportTransformSession session)
        {
            foreach (var target in session.Targets.Where(target => target.Kind == ViewportTransformTargetKind.Locator))
            {
                if (target.Locator == null)
                    continue;

                var current = target.Locator.LocatorEntry.SceneTransform;
                if (current == target.OldLocatorTransform)
                    continue;

                if (target.Locator.Parent is LocatorsXmlNode xmlRoot)
                    xmlRoot.IsDirty = true;

                var action = new LocatorTransformAction(session.Description, target.Locator, target.OldLocatorTransform)
                {
                    NewTransform = current
                };
                PushUndo(action);
            }
        }

        private void CommitAvPinTransformTargets(ViewportTransformSession session)
        {
            foreach (var target in session.Targets.Where(target => target.Kind == ViewportTransformTargetKind.AvPin))
            {
                if (target.AvPin?.PoiData?.Visibility == null)
                    continue;

                var visibility = target.AvPin.PoiData.Visibility;
                var action = new AvPinTransformAction(
                    session.Description,
                    target.AvPin,
                    target.OldAvPinPosX,
                    target.OldAvPinPosY,
                    target.OldAvPinPosZ,
                    target.OldAvPinAxisYaw,
                    target.OldAvPinAxisPitch)
                {
                    NewPosX = visibility.PosX,
                    NewPosY = visibility.PosY,
                    NewPosZ = visibility.PosZ,
                    NewAxisYaw = visibility.AxisYaw,
                    NewAxisPitch = visibility.AxisPitch
                };

                if (!action.HasChanges)
                    continue;

                if (target.AvPin.Parent is AvPinsFileNode fileNode)
                    fileNode.IsDirty = true;

                PushUndo(action);
            }
        }

        private void CommitLightTransformTargets(ViewportTransformSession session)
        {
            var entries = new List<LightGroupTransformEntry>();
            foreach (var target in session.Targets.Where(target => target.Kind == ViewportTransformTargetKind.LightGroup))
            {
                if (target.LightGroup?.GroupData == null)
                    continue;

                var group = target.LightGroup.GroupData;
                var entry = new LightGroupTransformEntry
                {
                    Group = target.LightGroup,
                    OldPos = target.OldLightPos,
                    OldRot = target.OldLightRot,
                    OldDamagePos = target.OldLightDamagePos,
                    OldDamageRot = target.OldLightDamageRot,
                    NewPos = group.Pos,
                    NewRot = group.Rot,
                    NewDamagePos = group.DamagePos,
                    NewDamageRot = group.DamageRot
                };

                if (entry.HasChanges)
                    entries.Add(entry);
            }

            if (entries.Count == 0)
                return;

            foreach (var lightsBin in entries.Select(entry => entry.Group?.Parent).OfType<LightsBinNode>().Distinct())
                lightsBin.IsDirty = true;

            PushUndo(new LightGroupTransformAction(session.Description, entries));
        }

        private Matrix4x4 GetTargetRotationMatrix(ViewportTransformTarget target)
        {
            return target.Kind switch
            {
                ViewportTransformTargetKind.Locator => Matrix4x4.CreateFromQuaternion(EulerDegreesToQuaternion(target.OldRotation)),
                ViewportTransformTargetKind.LightGroup => Matrix4x4.CreateFromQuaternion(new Quaternion(
                    target.OldLightRot.X,
                    target.OldLightRot.Y,
                    target.OldLightRot.Z,
                    target.OldLightRot.W)),
                _ => CreateRenderRotationMatrix(target.OldRotation)
            };
        }

        private void RefreshLightTransformTarget(LightGroupNode group)
        {
            UpdateLightRowNodeLabels(group);
            if (group.IsChecked != false)
            {
                HideLight(group);
                RenderLight(group);
                if (_currentLightHighlightTargets.Contains(group))
                    UpdateHighlightForLightGroups(_currentLightHighlightTargets);
            }
        }

        private static Vector3 ToVector3(Vector4 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static Vector4 AddVector3(Vector4 value, Vector3 delta)
        {
            return new Vector4(value.X + delta.X, value.Y + delta.Y, value.Z + delta.Z, value.W);
        }

        private List<ViewportTransformTarget> GetViewportTransformTargets(ViewportTransformMode mode = ViewportTransformMode.Translate)
        {
            var nodes = GetViewportTransformTargetNodes();
            return GetViewportTransformTargets(nodes, mode);
        }

        private List<ViewportTransformTarget> GetViewportTransformTargets(IEnumerable<IViewerNode> nodes, ViewportTransformMode mode = ViewportTransformMode.Translate)
        {
            return nodes
                .Select(CreateViewportTransformTarget)
                .Where(target => target != null)
                .Cast<ViewportTransformTarget>()
                .Where(target => mode != ViewportTransformMode.Scale || target.CanScale)
                .GroupBy(target => target.Node)
                .Select(group => group.First())
                .ToList();
        }

        private List<IViewerNode> GetViewportTransformTargetNodes()
        {
            IEnumerable<IViewerNode> targets = Enumerable.Empty<IViewerNode>();

            if (_isMultiSelectActive && _multiSelectedMeshes.Count > 0)
            {
                targets = _multiSelectedMeshes;
            }
            else if (_isMultiLightSelectActive && _multiSelectedLightGroups.Count > 0)
            {
                targets = _multiSelectedLightGroups;
            }
            else if (ViewModel.SelectedNode is MeshNode meshNode)
            {
                targets = new[] { meshNode };
            }
            else if (ViewModel.SelectedNode is LocatorNode locatorNode)
            {
                targets = new[] { locatorNode };
            }
            else if (ViewModel.SelectedNode is AvPinNode avPinNode)
            {
                targets = new[] { avPinNode };
            }
            else if (ViewModel.SelectedNode is LightGroupNode lightGroupNode)
            {
                targets = new[] { lightGroupNode };
            }
            else if (ViewModel.SelectedNode is LightRowNode lightRowNode && lightRowNode.Parent is LightGroupNode parentLightGroup)
            {
                targets = new[] { parentLightGroup };
            }
            else if (ViewModel.SelectedNode is ModelBinNode modelBinNode)
            {
                var meshes = new List<MeshNode>();
                CollectMeshNodes(modelBinNode, meshes);
                targets = meshes;
            }
            else if (ModelBinSelector?.SelectedItem is ModelBinNode selectedModelBin &&
                     MeshSelector?.SelectedItem is MeshScopeItem meshScope)
            {
                if (meshScope.Node != null)
                {
                    targets = new[] { meshScope.Node };
                }
                else if (!string.IsNullOrEmpty(meshScope.MaterialGroup))
                {
                    targets = selectedModelBin.Children
                        .OfType<MeshNode>()
                        .Where(mesh => mesh.GeometryData?.MaterialName == meshScope.MaterialGroup);
                }
                else
                {
                    targets = selectedModelBin.Children.OfType<MeshNode>();
                }
            }

            return targets
                .Distinct()
                .ToList();
        }

        private Vector3 CalculateTransformPivot(IReadOnlyList<ViewportTransformTarget> targets)
        {
            var hasBounds = false;
            var bounds = new SDX.BoundingBox();

            foreach (var target in targets)
            {
                if (_renderMap.TryGetValue(target.Node, out var model) &&
                    model is MeshGeometryModel3D meshModel &&
                    meshModel.Geometry != null &&
                    meshModel.Visibility != Visibility.Collapsed)
                {
                    if (!hasBounds)
                    {
                        bounds = meshModel.Bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds = SDX.BoundingBox.Merge(bounds, meshModel.Bounds);
                    }
                }
            }

            if (hasBounds && bounds.Minimum != bounds.Maximum)
            {
                var center = (bounds.Minimum + bounds.Maximum) / 2f;
                return new Vector3(center.X, center.Y, center.Z);
            }

            if (targets.Count == 0)
                return Vector3.Zero;

            var total = Vector3.Zero;
            foreach (var target in targets)
                total += target.OldPosition;

            return total / targets.Count;
        }

        private void DisableViewportCameraInputForTransform()
        {
            if (_viewport == null)
                return;

            _savedTransformPanEnabled = _viewport.IsPanEnabled;
            _savedTransformRotationEnabled = _viewport.IsRotationEnabled;
            _savedTransformZoomEnabled = _viewport.IsZoomEnabled;
            _savedTransformMoveEnabled = _viewport.IsMoveEnabled;

            _viewport.IsPanEnabled = false;
            _viewport.IsRotationEnabled = false;
            _viewport.IsZoomEnabled = false;
            _viewport.IsMoveEnabled = false;
        }

        private void RestoreViewportCameraInputAfterTransform()
        {
            if (_viewport == null)
                return;

            _viewport.IsPanEnabled = _savedTransformPanEnabled;
            _viewport.IsRotationEnabled = _savedTransformRotationEnabled;
            _viewport.IsZoomEnabled = _savedTransformZoomEnabled;
            _viewport.IsMoveEnabled = _savedTransformMoveEnabled;
        }

        private void UpdateTransformAxisGuide()
        {
            if (_activeTransformAxis == ViewportTransformAxis.None || _activeTransformSession == null)
            {
                ClearTransformAxisGuide(removeFromViewport: false);
                return;
            }

            EnsureTransformAxisGuide();
            if (_transformAxisGuide == null)
                return;

            var axis = GetActiveAxisDirection(_activeTransformSession.Targets[0]);
            var length = GetAxisGuideLength();
            var start = _transformPivotWorld - (axis * length);
            var end = _transformPivotWorld + (axis * length);

            var builder = new LineBuilder();
            builder.AddLine(ToSharpDx(start), ToSharpDx(end));

            _transformAxisGuide.Geometry = builder.ToLineGeometry3D();
            _transformAxisGuide.Color = GetAxisGuideColor(_activeTransformAxis);
            _transformAxisGuide.Thickness = _activeTransformAxisSpace == ViewportTransformAxisSpace.Local ? 3.5 : 2.5;
            _transformAxisGuide.Visibility = Visibility.Visible;
        }

        private void EnsureTransformAxisGuide()
        {
            if (_transformAxisGuide != null || _viewport == null)
                return;

            _transformAxisGuide = new LineGeometryModel3D
            {
                Thickness = 2.5,
                HitTestThickness = 24,
                DepthBias = -100000,
                IsThrowingShadow = false,
                RenderOrder = 10001,
                EnableViewFrustumCheck = false,
                Visibility = Visibility.Collapsed
            };
            _viewport.Items.Add(_transformAxisGuide);
        }

        private void ClearTransformAxisGuide(bool removeFromViewport)
        {
            if (_transformAxisGuide == null)
                return;

            _transformAxisGuide.Visibility = Visibility.Collapsed;

            if (removeFromViewport)
            {
                _viewport?.Items.Remove(_transformAxisGuide);
                _transformAxisGuide = null;
            }
        }

        private float GetAxisGuideLength()
        {
            if (TryGetCameraFrame(out var position, out _, out _, out _))
            {
                var distance = Vector3.Distance(position, _transformPivotWorld);
                return Math.Max(1000f, distance * 3f);
            }

            return 1000f;
        }

        private Vector3 GetActiveAxisDirection(ViewportTransformTarget referenceTarget)
        {
            var axis = GetAxisUnitVector(_activeTransformAxis);
            if (_activeTransformAxisSpace == ViewportTransformAxisSpace.Local)
            {
                axis = Vector3.TransformNormal(axis, GetTargetRotationMatrix(referenceTarget));
            }

            return NormalizeOrDefault(axis, GetAxisUnitVector(_activeTransformAxis));
        }

        private static Vector3 GetAxisUnitVector(ViewportTransformAxis axis)
        {
            return axis switch
            {
                ViewportTransformAxis.X => Vector3.UnitX,
                ViewportTransformAxis.Y => Vector3.UnitY,
                ViewportTransformAxis.Z => Vector3.UnitZ,
                _ => Vector3.Zero
            };
        }

        private static Color GetAxisGuideColor(ViewportTransformAxis axis)
        {
            return axis switch
            {
                ViewportTransformAxis.X => Color.FromArgb(255, 255, 64, 64),
                ViewportTransformAxis.Y => Color.FromArgb(255, 64, 220, 96),
                ViewportTransformAxis.Z => Color.FromArgb(255, 64, 128, 255),
                _ => Color.FromArgb(255, 255, 255, 255)
            };
        }

        private static Matrix4x4 CreateRenderRotationMatrix(Vector3 eulerDegrees)
        {
            if (eulerDegrees == Vector3.Zero)
                return Matrix4x4.Identity;

            var rx = eulerDegrees.X * MathF.PI / 180f;
            var ry = eulerDegrees.Y * MathF.PI / 180f;
            var rz = eulerDegrees.Z * MathF.PI / 180f;
            return Matrix4x4.CreateRotationY(rx) *
                   Matrix4x4.CreateRotationX(ry) *
                   Matrix4x4.CreateRotationZ(rz);
        }

        private static Vector3 ExtractRenderEulerDegrees(Matrix4x4 matrix)
        {
            var sinY = Math.Clamp(matrix.M23, -1f, 1f);
            var y = MathF.Asin(sinY);
            var cosY = MathF.Cos(y);

            float x;
            float z;

            if (MathF.Abs(cosY) > 1e-5f)
            {
                z = MathF.Atan2(-matrix.M21, matrix.M22);
                x = MathF.Atan2(-matrix.M13, matrix.M33);
            }
            else
            {
                z = 0f;
                x = MathF.Atan2(matrix.M31, matrix.M11);
            }

            return new Vector3(
                NormalizeAngleDegrees(x * 180f / MathF.PI),
                NormalizeAngleDegrees(y * 180f / MathF.PI),
                NormalizeAngleDegrees(z * 180f / MathF.PI));
        }

        private bool TryGetCameraFrame(out Vector3 position, out Vector3 forward, out Vector3 right, out Vector3 up)
        {
            position = Vector3.Zero;
            forward = Vector3.UnitZ;
            right = Vector3.UnitX;
            up = Vector3.UnitY;

            if (_viewport?.Camera is not ProjectionCamera camera)
                return false;

            position = ToNumerics(camera.Position);
            forward = NormalizeOrDefault(ToNumerics(camera.LookDirection), Vector3.UnitZ);
            up = NormalizeOrDefault(ToNumerics(camera.UpDirection), Vector3.UnitY);
            right = NormalizeOrDefault(Vector3.Cross(up, forward), Vector3.UnitX);
            up = NormalizeOrDefault(Vector3.Cross(forward, right), Vector3.UnitY);
            return true;
        }

        private bool TryGetWorldUnitsPerPixelAt(Vector3 worldPoint, out double unitsPerPixelX, out double unitsPerPixelY)
        {
            unitsPerPixelX = 0.0;
            unitsPerPixelY = 0.0;

            if (!TryGetViewportSize(out var width, out var height) ||
                !TryGetCameraFrame(out var position, out var forward, out _, out _))
            {
                return false;
            }

            var aspect = width / height;

            if (_viewport?.Camera is PerspectiveCamera perspective)
            {
                var depth = Math.Max(0.001, Vector3.Dot(worldPoint - position, forward));
                var fov = Math.Clamp(perspective.FieldOfView, 1.0, 179.0) * Math.PI / 180.0;
                var worldHeight = 2.0 * depth * Math.Tan(fov * 0.5);
                var worldWidth = worldHeight * aspect;
                unitsPerPixelX = worldWidth / width;
                unitsPerPixelY = worldHeight / height;
                return true;
            }

            if (_viewport?.Camera is OrthographicCamera orthographic)
            {
                var worldWidth = Math.Max(0.001, orthographic.Width);
                var worldHeight = worldWidth / aspect;
                unitsPerPixelX = worldWidth / width;
                unitsPerPixelY = worldHeight / height;
                return true;
            }

            return false;
        }

        private bool TryCreatePointerRay(Point screenPoint, out Vector3 origin, out Vector3 direction)
        {
            origin = Vector3.Zero;
            direction = Vector3.UnitZ;

            if (!TryGetViewportSize(out var width, out var height) ||
                !TryGetCameraFrame(out var cameraPosition, out var forward, out var right, out var up))
            {
                return false;
            }

            var ndcX = ((screenPoint.X / width) * 2.0) - 1.0;
            var ndcY = 1.0 - ((screenPoint.Y / height) * 2.0);
            var aspect = width / height;

            if (_viewport?.Camera is PerspectiveCamera perspective)
            {
                var fov = Math.Clamp(perspective.FieldOfView, 1.0, 179.0) * Math.PI / 180.0;
                var tanHalfFov = Math.Tan(fov * 0.5);
                origin = cameraPosition;
                direction = NormalizeOrDefault(
                    forward +
                    (right * (float)(ndcX * tanHalfFov * aspect)) +
                    (up * (float)(ndcY * tanHalfFov)),
                    forward);
                return true;
            }

            if (_viewport?.Camera is OrthographicCamera orthographic)
            {
                var worldWidth = Math.Max(0.001, orthographic.Width);
                var worldHeight = worldWidth / aspect;
                origin = cameraPosition +
                         (right * (float)(ndcX * worldWidth * 0.5)) +
                         (up * (float)(ndcY * worldHeight * 0.5));
                direction = forward;
                return true;
            }

            return false;
        }

        private static bool TryIntersectRayPlane(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3 planePoint,
            Vector3 planeNormal,
            out Vector3 intersection)
        {
            intersection = Vector3.Zero;
            planeNormal = NormalizeOrDefault(planeNormal, Vector3.UnitY);
            rayDirection = NormalizeOrDefault(rayDirection, Vector3.UnitZ);
            var denominator = Vector3.Dot(planeNormal, rayDirection);
            if (MathF.Abs(denominator) < 0.000001f)
                return false;

            var t = Vector3.Dot(planePoint - rayOrigin, planeNormal) / denominator;
            if (!float.IsFinite(t))
                return false;

            intersection = rayOrigin + (rayDirection * t);
            return IsFiniteVector(intersection);
        }

        private static float ClosestParameterOnLineToRay(Vector3 linePoint, Vector3 lineDirection, Vector3 rayOrigin, Vector3 rayDirection)
        {
            lineDirection = NormalizeOrDefault(lineDirection, Vector3.UnitX);
            rayDirection = NormalizeOrDefault(rayDirection, Vector3.UnitZ);

            var w = linePoint - rayOrigin;
            var a = Vector3.Dot(lineDirection, lineDirection);
            var b = Vector3.Dot(lineDirection, rayDirection);
            var c = Vector3.Dot(rayDirection, rayDirection);
            var d = Vector3.Dot(lineDirection, w);
            var e = Vector3.Dot(rayDirection, w);
            var denominator = (a * c) - (b * b);

            if (MathF.Abs(denominator) < 0.000001f)
                return -d / Math.Max(a, 0.000001f);

            var parameter = ((b * e) - (c * d)) / denominator;
            return float.IsFinite(parameter) ? parameter : 0f;
        }

        private bool TryProjectWorldToScreen(Vector3 worldPoint, out Point screenPoint)
        {
            screenPoint = default;

            if (!TryGetViewportSize(out var width, out var height) ||
                !TryGetCameraFrame(out var position, out var forward, out var right, out var up))
            {
                return false;
            }

            var rel = worldPoint - position;
            var x = Vector3.Dot(rel, right);
            var y = Vector3.Dot(rel, up);
            var z = Vector3.Dot(rel, forward);
            var aspect = width / height;

            if (_viewport?.Camera is PerspectiveCamera perspective)
            {
                if (z <= 0.001f)
                    return false;

                var fov = Math.Clamp(perspective.FieldOfView, 1.0, 179.0) * Math.PI / 180.0;
                var tanHalfFov = Math.Tan(fov * 0.5);
                var ndcX = x / (z * tanHalfFov * aspect);
                var ndcY = y / (z * tanHalfFov);

                screenPoint = new Point(
                    (width * 0.5) + (ndcX * width * 0.5),
                    (height * 0.5) - (ndcY * height * 0.5));
                return IsFinitePoint(screenPoint);
            }

            if (_viewport?.Camera is OrthographicCamera orthographic)
            {
                var worldWidth = Math.Max(0.001, orthographic.Width);
                var worldHeight = worldWidth / aspect;

                screenPoint = new Point(
                    (width * 0.5) + (x / worldWidth * width),
                    (height * 0.5) - (y / worldHeight * height));
                return IsFinitePoint(screenPoint);
            }

            return false;
        }

        private bool TryGetViewportSize(out double width, out double height)
        {
            width = _viewport?.ActualWidth ?? 0.0;
            height = _viewport?.ActualHeight ?? 0.0;

            if (width < 1.0)
                width = ViewportContainer?.ActualWidth ?? 0.0;
            if (height < 1.0)
                height = ViewportContainer?.ActualHeight ?? 0.0;

            return width >= 1.0 && height >= 1.0;
        }

        private Point GetFallbackViewportAnchor(Vector3 pivotWorld)
        {
            if (TryProjectWorldToScreen(pivotWorld, out var projected))
                return projected;

            if (TryGetViewportSize(out var width, out var height))
                return new Point(width * 0.5, height * 0.5);

            return new Point(0.0, 0.0);
        }

        private Point GetViewportPointerPosition(PointerRoutedEventArgs e, UIElement? relativeTo)
        {
            var viewportElement = _viewport as UIElement;
            var target = relativeTo ?? viewportElement ?? this;
            return e.GetCurrentPoint(target).Position;
        }

        private static bool IsEditableKeyboardSource(object? source)
        {
            var current = source as DependencyObject;
            while (current != null)
            {
                if (current is TextBox ||
                    current is PasswordBox ||
                    current is RichEditBox ||
                    current is NumberBox ||
                    current is ComboBox ||
                    current is AutoSuggestBox)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static bool IsControlOrAltDown()
        {
            return IsKeyDown(VirtualKey.Control) ||
                   IsKeyDown(VirtualKey.LeftControl) ||
                   IsKeyDown(VirtualKey.RightControl) ||
                   IsKeyDown(VirtualKey.Menu) ||
                   IsKeyDown(VirtualKey.LeftMenu) ||
                   IsKeyDown(VirtualKey.RightMenu);
        }

        private static bool IsKeyDown(VirtualKey key)
        {
            return InputKeyboardSource
                .GetKeyStateForCurrentThread(key)
                .HasFlag(CoreVirtualKeyStates.Down);
        }

        private static string GetTransformDescription(ViewportTransformMode mode)
        {
            return mode switch
            {
                ViewportTransformMode.Translate => "Viewport Grab/Move",
                ViewportTransformMode.Rotate => "Viewport Rotate",
                ViewportTransformMode.Scale => "Viewport Scale",
                _ => "Viewport Transform"
            };
        }

        private static ViewportTransformMode ToTransformMode(ViewportGizmoMode mode)
        {
            return mode switch
            {
                ViewportGizmoMode.Translate => ViewportTransformMode.Translate,
                ViewportGizmoMode.Rotate => ViewportTransformMode.Rotate,
                ViewportGizmoMode.Scale => ViewportTransformMode.Scale,
                _ => ViewportTransformMode.None
            };
        }

        private static float NormalizeAngleDegrees(float degrees)
        {
            while (degrees > 180f)
                degrees -= 360f;
            while (degrees < -180f)
                degrees += 360f;
            return degrees;
        }

        private static double Distance(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static bool IsFinitePoint(Point point)
        {
            return double.IsFinite(point.X) && double.IsFinite(point.Y);
        }

        private static Vector3 ToNumerics(SDX.Vector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static SDX.Vector3 ToSharpDx(Vector3 value)
        {
            return new SDX.Vector3(value.X, value.Y, value.Z);
        }
    }
}
