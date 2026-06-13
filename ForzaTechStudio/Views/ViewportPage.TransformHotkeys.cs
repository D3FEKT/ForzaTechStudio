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

        private ViewportTransformMode _activeTransformMode = ViewportTransformMode.None;
        private ViewportTransformAxis _activeTransformAxis = ViewportTransformAxis.None;
        private ViewportTransformAxisSpace _activeTransformAxisSpace = ViewportTransformAxisSpace.Global;
        private MeshTransformAction? _activeTransformAction;
        private readonly List<MeshNode> _activeTransformTargets = new();
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
            var targets = GetViewportTransformTargets();
            if (targets.Count == 0)
            {
                UpdateTransformHotkeyStatus();
                return;
            }

            var action = BeginTransformAction(targets, GetTransformDescription(mode));
            if (action.Entries.Count == 0)
            {
                UpdateTransformHotkeyStatus();
                return;
            }

            _activeTransformMode = mode;
            _activeTransformAxis = ViewportTransformAxis.None;
            _activeTransformAxisSpace = ViewportTransformAxisSpace.Global;
            _activeTransformAction = action;
            _activeTransformTargets.Clear();
            _activeTransformTargets.AddRange(action.Entries
                .Select(entry => entry.Mesh)
                .Where(mesh => mesh != null)
                .Cast<MeshNode>()
                .Distinct());

            _transformPivotWorld = CalculateTransformPivot(_activeTransformTargets, action);
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
            if (_activeTransformAction == null)
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

            UpdateTransformAxisGuide();
            RefreshHighlight();
            RefreshOverlays();
        }

        private void ConfirmActiveViewportTransform()
        {
            var action = _activeTransformAction;
            if (action != null)
                CommitTransformAction(action);

            ClearActiveViewportTransformState();
            UpdateTransformUI();
            RefreshHighlight();
            RefreshOverlays();
        }

        private void CancelActiveViewportTransform()
        {
            RestoreActiveTransformSnapshot();
            ClearActiveViewportTransformState();
            UpdateTransformUI();
            RefreshHighlight();
            RefreshOverlays();
        }

        private void ClearActiveViewportTransformState()
        {
            _activeTransformMode = ViewportTransformMode.None;
            _activeTransformAxis = ViewportTransformAxis.None;
            _activeTransformAxisSpace = ViewportTransformAxisSpace.Global;
            _activeTransformAction = null;
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
            var enabled = _hasTransformSelection;
            SetTransformOverlayButton(MoveTransformButton, enabled, enabled && _selectedGizmoMode == ViewportGizmoMode.Translate);
            SetTransformOverlayButton(RotateTransformButton, enabled, enabled && _selectedGizmoMode == ViewportGizmoMode.Rotate);
            SetTransformOverlayButton(ScaleTransformButton, enabled, enabled && _selectedGizmoMode == ViewportGizmoMode.Scale);
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

        private bool RefreshTransformSelectionAvailability(IReadOnlyList<MeshNode>? knownTargets = null)
        {
            _hasTransformSelection = knownTargets?.Count > 0 || (knownTargets == null && GetViewportTransformTargets().Count > 0);
            if (!_hasTransformSelection)
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

            var targets = GetViewportTransformTargets();
            if (targets.Count == 0)
                return false;

            var action = BeginTransformAction(targets, $"Gizmo {handle.Mode}");
            if (action.Entries.Count == 0)
                return false;

            _isGizmoDragging = true;
            _activeGizmoHandle = handle;
            _gizmoTransformAction = action;
            _gizmoTransformTargets.Clear();
            _gizmoTransformTargets.AddRange(action.Entries
                .Select(entry => entry.Mesh)
                .Where(mesh => mesh != null)
                .Cast<MeshNode>()
                .Distinct());
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
            if (!_isGizmoDragging || _gizmoTransformAction == null ||
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

            var action = _gizmoTransformAction;
            if (action != null)
                CommitTransformAction(action);

            ClearActiveGizmoDragState();
            UpdateTransformUI();
            RefreshHighlight();
            RefreshOverlays();
            UpdateTransformGizmoForSelection();
        }

        private void CancelActiveGizmoDrag()
        {
            if (!_isGizmoDragging)
                return;

            RestoreGizmoTransformSnapshot();
            ClearActiveGizmoDragState();
            UpdateTransformUI();
            RefreshHighlight();
            RefreshOverlays();
            UpdateTransformGizmoForSelection();
        }

        private void ClearActiveGizmoDragState()
        {
            _isGizmoDragging = false;
            _activeGizmoHandle = default;
            _gizmoTransformAction = null;
            _gizmoTransformTargets.Clear();
            _viewport?.ReleasePointerCaptures();
            RestoreViewportCameraInputAfterTransform();
        }

        private void RestoreGizmoTransformSnapshot()
        {
            if (_gizmoTransformAction == null)
                return;

            foreach (var entry in _gizmoTransformAction.Entries)
            {
                if (entry.Mesh?.GeometryData?.SourceMesh == null)
                    continue;

                entry.Mesh.GeometryData.SourceMesh.PositionScale = entry.OldScale;
                entry.Mesh.GeometryData.SourceMesh.PositionTranslate = entry.OldTranslate;
                entry.Mesh.GeometryData.RotationEulerDegrees = entry.OldRotation;
                RefreshModalTransformMesh(entry.Mesh);
            }
        }

        private void ApplyGizmoTranslationPreview(Vector3 delta)
        {
            if (_gizmoTransformAction == null)
                return;

            foreach (var entry in _gizmoTransformAction.Entries)
            {
                if (entry.Mesh?.GeometryData?.SourceMesh == null)
                    continue;

                entry.Mesh.GeometryData.SourceMesh.PositionTranslate = ApplyWorldTranslationDelta(entry.Mesh, entry.OldTranslate, delta);
                RefreshModalTransformMesh(entry.Mesh);
            }
        }

        private void ApplyGizmoRotationPreview(Vector3 axis, float deltaDegrees)
        {
            if (_gizmoTransformAction == null)
                return;

            foreach (var entry in _gizmoTransformAction.Entries)
            {
                if (entry.Mesh?.GeometryData?.SourceMesh == null)
                    continue;

                entry.Mesh.GeometryData.RotationEulerDegrees = ComposeRotationEulerAroundAxis(entry, axis, deltaDegrees);
                RefreshModalTransformMesh(entry.Mesh);
            }
        }

        private void ApplyGizmoScalePreview(float factor)
        {
            if (_gizmoTransformAction == null)
                return;

            foreach (var entry in _gizmoTransformAction.Entries)
            {
                if (entry.Mesh?.GeometryData?.SourceMesh == null)
                    continue;

                var scale = entry.OldScale;
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

                entry.Mesh.GeometryData.SourceMesh.PositionScale = scale;
                RefreshModalTransformMesh(entry.Mesh);
            }
        }

        private Vector3 ComposeRotationEulerAroundAxis(MeshTransformEntry entry, Vector3 axis, float deltaDegrees)
        {
            var initial = CreateRenderRotationMatrix(entry.OldRotation);
            var radians = deltaDegrees * MathF.PI / 180f;
            var axisRotation = Matrix4x4.CreateFromAxisAngle(NormalizeOrDefault(axis, Vector3.UnitZ), radians);
            return ExtractRenderEulerDegrees(initial * axisRotation);
        }

        private void UpdateTransformGizmoForSelection()
        {
            var targets = GetViewportTransformTargets();
            UpdateTransformGizmoForTargets(targets);
        }

        private void UpdateTransformGizmoForTargets(IReadOnlyList<MeshNode> targets)
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

            var action = BeginTransformAction(targets, "Gizmo Pivot");
            if (action.Entries.Count == 0)
            {
                _hasTransformSelection = false;
                _selectedGizmoMode = ViewportGizmoMode.None;
                _transformGizmo.SetVisible(false);
                _transformGizmo.SetMode(ViewportGizmoMode.None);
                UpdateTransformOverlayButtons();
                return;
            }

            var pivot = CalculateTransformPivot(targets, action);
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
            if (_activeTransformAction == null)
                return;

            foreach (var entry in _activeTransformAction.Entries)
            {
                if (entry.Mesh?.GeometryData?.SourceMesh == null)
                    continue;

                entry.Mesh.GeometryData.SourceMesh.PositionScale = entry.OldScale;
                entry.Mesh.GeometryData.SourceMesh.PositionTranslate = entry.OldTranslate;
                entry.Mesh.GeometryData.RotationEulerDegrees = entry.OldRotation;
                RefreshModalTransformMesh(entry.Mesh);
            }
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

            if (_activeTransformAction != null && _hasLastViewportPointerPosition)
                UpdateActiveViewportTransform(_lastViewportPointerPosition);
        }

        private void ApplyTranslationPreview(Vector3 delta)
        {
            if (_activeTransformAction == null)
                return;

            foreach (var entry in _activeTransformAction.Entries)
            {
                if (entry.Mesh?.GeometryData?.SourceMesh == null)
                    continue;

                entry.Mesh.GeometryData.SourceMesh.PositionTranslate = ApplyWorldTranslationDelta(entry.Mesh, entry.OldTranslate, delta);

                RefreshModalTransformMesh(entry.Mesh);
            }
        }

        private void ApplyRotationPreview(float deltaDegrees)
        {
            if (_activeTransformAction == null)
                return;

            foreach (var entry in _activeTransformAction.Entries)
            {
                if (entry.Mesh?.GeometryData?.SourceMesh == null)
                    continue;

                entry.Mesh.GeometryData.RotationEulerDegrees = ComposeRotationEuler(entry, deltaDegrees);
                RefreshModalTransformMesh(entry.Mesh);
            }
        }

        private void ApplyScalePreview(float scaleFactor)
        {
            if (_activeTransformAction == null)
                return;

            foreach (var entry in _activeTransformAction.Entries)
            {
                if (entry.Mesh?.GeometryData?.SourceMesh == null)
                    continue;

                var oldScale = entry.OldScale;
                var newScale = oldScale;

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

                entry.Mesh.GeometryData.SourceMesh.PositionScale = newScale;
                RefreshModalTransformMesh(entry.Mesh);
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
            if (_activeTransformAction == null || _activeTransformAction.Entries.Count == 0)
                return Vector3.Zero;

            var axis = GetActiveAxisDirection(_activeTransformAction.Entries[0]);
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

        private Vector3 ComposeRotationEuler(MeshTransformEntry entry, float deltaDegrees)
        {
            var initial = CreateRenderRotationMatrix(entry.OldRotation);
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

        private List<MeshNode> GetViewportTransformTargets()
        {
            IEnumerable<MeshNode> targets = Enumerable.Empty<MeshNode>();

            if (_isMultiSelectActive && _multiSelectedMeshes.Count > 0)
            {
                targets = _multiSelectedMeshes;
            }
            else if (ViewModel.SelectedNode is MeshNode meshNode)
            {
                targets = new[] { meshNode };
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
                .Where(mesh => mesh.GeometryData?.SourceMesh != null)
                .Distinct()
                .ToList();
        }

        private Vector3 CalculateTransformPivot(IReadOnlyList<MeshNode> targets, MeshTransformAction action)
        {
            var hasBounds = false;
            var bounds = new SDX.BoundingBox();

            foreach (var mesh in targets)
            {
                if (_renderMap.TryGetValue(mesh, out var model) &&
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

            var translateEntries = action.Entries
                .Where(entry => entry.Mesh?.GeometryData?.SourceMesh != null)
                .ToList();

            if (translateEntries.Count == 0)
                return Vector3.Zero;

            var total = Vector3.Zero;
            foreach (var entry in translateEntries)
                total += new Vector3(entry.OldTranslate.X, entry.OldTranslate.Y, entry.OldTranslate.Z);

            return total / translateEntries.Count;
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
            if (_activeTransformAxis == ViewportTransformAxis.None || _activeTransformAction == null)
            {
                ClearTransformAxisGuide(removeFromViewport: false);
                return;
            }

            EnsureTransformAxisGuide();
            if (_transformAxisGuide == null)
                return;

            var axis = GetActiveAxisDirection(_activeTransformAction.Entries[0]);
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

        private Vector3 GetActiveAxisDirection(MeshTransformEntry referenceEntry)
        {
            var axis = GetAxisUnitVector(_activeTransformAxis);
            if (_activeTransformAxisSpace == ViewportTransformAxisSpace.Local)
            {
                axis = Vector3.TransformNormal(axis, CreateRenderRotationMatrix(referenceEntry.OldRotation));
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
