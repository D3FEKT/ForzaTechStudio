using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Numerics;
using SDX = SharpDX;
using Color = Windows.UI.Color;

namespace ForzaTechStudio.Views
{
    internal enum ViewportGizmoMode
    {
        None,
        Translate,
        Rotate,
        Scale
    }

    internal enum ViewportGizmoHandleKind
    {
        None,
        AxisX,
        AxisY,
        AxisZ,
        PlaneXY,
        PlaneXZ,
        PlaneYZ,
        Uniform,
        View
    }

    internal readonly struct ViewportGizmoHandle
    {
        public ViewportGizmoHandle(ViewportGizmoMode mode, ViewportGizmoHandleKind kind, Vector3 axis, Vector3 planeNormal)
        {
            Mode = mode;
            Kind = kind;
            Axis = axis;
            PlaneNormal = planeNormal;
        }

        public ViewportGizmoMode Mode { get; }
        public ViewportGizmoHandleKind Kind { get; }
        public Vector3 Axis { get; }
        public Vector3 PlaneNormal { get; }

        public bool IsAxis => Kind is ViewportGizmoHandleKind.AxisX or ViewportGizmoHandleKind.AxisY or ViewportGizmoHandleKind.AxisZ;
        public bool IsPlane => Kind is ViewportGizmoHandleKind.PlaneXY or ViewportGizmoHandleKind.PlaneXZ or ViewportGizmoHandleKind.PlaneYZ;
        public bool IsUniform => Kind == ViewportGizmoHandleKind.Uniform;
        public bool IsView => Kind == ViewportGizmoHandleKind.View;
    }

    internal sealed class ViewportTransformGizmo
    {
        private static readonly Color XColor = Color.FromArgb(255, 238, 74, 74);
        private static readonly Color YColor = Color.FromArgb(255, 82, 214, 105);
        private static readonly Color ZColor = Color.FromArgb(255, 80, 137, 255);
        private static readonly Color ViewColor = Color.FromArgb(255, 230, 230, 230);
        private static readonly Color PlaneXYColor = Color.FromArgb(72, 255, 220, 64);
        private static readonly Color PlaneXZColor = Color.FromArgb(72, 255, 96, 220);
        private static readonly Color PlaneYZColor = Color.FromArgb(72, 64, 220, 255);

        private readonly Dictionary<GeometryModel3D, ViewportGizmoHandle> _handles = new();
        private ViewportGizmoMode _mode = ViewportGizmoMode.None;
        private Vector3 _pivot;
        private Vector3 _cameraForward = Vector3.UnitZ;
        private float _scale = 1f;

        public GroupModel3D Root { get; } = new() { Visibility = Visibility.Collapsed };

        public ViewportGizmoMode Mode => _mode;
        public bool IsVisible => Root.Visibility == Visibility.Visible;
        public Vector3 Pivot => _pivot;

        public void SetMode(ViewportGizmoMode mode)
        {
            if (_mode == mode)
                return;

            _mode = mode;
            Rebuild();
        }

        public void SetVisible(bool visible)
        {
            Root.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void Update(Vector3 pivot, float scale, Vector3 cameraForward)
        {
            scale = Math.Max(0.001f, scale);
            cameraForward = NormalizeOrDefault(cameraForward, Vector3.UnitZ);

            if (AlmostEqual(_pivot, pivot) &&
                MathF.Abs(_scale - scale) < 0.0001f &&
                AlmostEqual(_cameraForward, cameraForward))
            {
                return;
            }

            _pivot = pivot;
            _scale = scale;
            _cameraForward = cameraForward;
            Rebuild();
        }

        public bool TryResolveHandle(GeometryModel3D? model, out ViewportGizmoHandle handle)
        {
            if (model != null && _handles.TryGetValue(model, out handle))
                return true;

            handle = default;
            return false;
        }

        private void Rebuild()
        {
            Root.Children.Clear();
            _handles.Clear();

            switch (_mode)
            {
                case ViewportGizmoMode.None:
                    break;
                case ViewportGizmoMode.Translate:
                    BuildTranslate();
                    break;
                case ViewportGizmoMode.Rotate:
                    BuildRotate();
                    break;
                case ViewportGizmoMode.Scale:
                    BuildScale();
                    break;
            }
        }

        private void BuildTranslate()
        {
            AddAxisArrow(Vector3.UnitX, XColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.AxisX, Vector3.UnitX, Vector3.Zero));
            AddAxisArrow(Vector3.UnitY, YColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.AxisY, Vector3.UnitY, Vector3.Zero));
            AddAxisArrow(Vector3.UnitZ, ZColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.AxisZ, Vector3.UnitZ, Vector3.Zero));

            AddPlaneHandle(Vector3.UnitX, Vector3.UnitY, PlaneXYColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.PlaneXY, Vector3.Zero, Vector3.UnitZ));
            AddPlaneHandle(Vector3.UnitX, Vector3.UnitZ, PlaneXZColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.PlaneXZ, Vector3.Zero, Vector3.UnitY));
            AddPlaneHandle(Vector3.UnitY, Vector3.UnitZ, PlaneYZColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.PlaneYZ, Vector3.Zero, Vector3.UnitX));
        }

        private void BuildRotate()
        {
            AddRing(Vector3.UnitX, XColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.AxisX, Vector3.UnitX, Vector3.Zero), _scale * 0.62f, 1.25);
            AddRing(Vector3.UnitY, YColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.AxisY, Vector3.UnitY, Vector3.Zero), _scale * 0.68f, 1.25);
            AddRing(Vector3.UnitZ, ZColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.AxisZ, Vector3.UnitZ, Vector3.Zero), _scale * 0.74f, 1.25);
            AddRing(_cameraForward, ViewColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.View, _cameraForward, Vector3.Zero), _scale * 0.82f, 1.0);
        }

        private void BuildScale()
        {
            AddAxisScale(Vector3.UnitX, XColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.AxisX, Vector3.UnitX, Vector3.Zero));
            AddAxisScale(Vector3.UnitY, YColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.AxisY, Vector3.UnitY, Vector3.Zero));
            AddAxisScale(Vector3.UnitZ, ZColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.AxisZ, Vector3.UnitZ, Vector3.Zero));
            AddCube(_pivot, _scale * 0.04f, ViewColor, new ViewportGizmoHandle(_mode, ViewportGizmoHandleKind.Uniform, Vector3.Zero, Vector3.Zero));
        }

        private void AddAxisArrow(Vector3 axis, Color color, ViewportGizmoHandle handle)
        {
            var end = _pivot + axis * (_scale * 0.74f);
            var line = CreateLine(_pivot, end, color, 1.6);
            Register(line, handle);

            var cone = CreateCone(end - axis * (_scale * 0.1f), end, _scale * 0.026f, color);
            Register(cone, handle);
        }

        private void AddAxisScale(Vector3 axis, Color color, ViewportGizmoHandle handle)
        {
            var end = _pivot + axis * (_scale * 0.72f);
            Register(CreateLine(_pivot, end, color, 1.6), handle);
            AddCube(end, _scale * 0.036f, color, handle);
        }

        private void AddPlaneHandle(Vector3 a, Vector3 b, Color color, ViewportGizmoHandle handle)
        {
            var offset = _scale * 0.22f;
            var size = _scale * 0.08f;
            var center = _pivot + (a + b) * offset;

            var p0 = center - a * size - b * size;
            var p1 = center + a * size - b * size;
            var p2 = center + a * size + b * size;
            var p3 = center - a * size + b * size;

            Register(CreateQuad(p0, p1, p2, p3, color), handle);
        }

        private void AddRing(Vector3 normal, Color color, ViewportGizmoHandle handle, float radius, double thickness)
        {
            normal = NormalizeOrDefault(normal, Vector3.UnitZ);
            CreateBasis(normal, out var u, out var v);

            var builder = new LineBuilder();
            const int segments = 96;
            var previous = _pivot + u * radius;
            for (int i = 1; i <= segments; i++)
            {
                var angle = (MathF.PI * 2f) * i / segments;
                var current = _pivot + ((u * MathF.Cos(angle)) + (v * MathF.Sin(angle))) * radius;
                builder.AddLine(ToSharpDx(previous), ToSharpDx(current));
                previous = current;
            }

            var line = new LineGeometryModel3D
            {
                Geometry = builder.ToLineGeometry3D(),
                Color = color,
                Thickness = thickness,
                HitTestThickness = 24,
                DepthBias = -100000,
                IsThrowingShadow = false,
                RenderOrder = 10000,
                EnableViewFrustumCheck = false
            };
            Register(line, handle);
        }

        private LineGeometryModel3D CreateLine(Vector3 start, Vector3 end, Color color, double thickness)
        {
            var builder = new LineBuilder();
            builder.AddLine(ToSharpDx(start), ToSharpDx(end));
            return new LineGeometryModel3D
            {
                Geometry = builder.ToLineGeometry3D(),
                Color = color,
                Thickness = thickness,
                HitTestThickness = 24,
                DepthBias = -100000,
                IsThrowingShadow = false,
                RenderOrder = 10000,
                EnableViewFrustumCheck = false
            };
        }

        private MeshGeometryModel3D CreateCone(Vector3 baseCenter, Vector3 apex, float radius, Color color)
        {
            var axis = NormalizeOrDefault(apex - baseCenter, Vector3.UnitY);
            CreateBasis(axis, out var u, out var v);

            var positions = new Vector3Collection();
            var indices = new IntCollection();
            const int segments = 20;

            positions.Add(ToSharpDx(apex));
            positions.Add(ToSharpDx(baseCenter));
            for (int i = 0; i < segments; i++)
            {
                var angle = (MathF.PI * 2f) * i / segments;
                var p = baseCenter + ((u * MathF.Cos(angle)) + (v * MathF.Sin(angle))) * radius;
                positions.Add(ToSharpDx(p));
            }

            for (int i = 0; i < segments; i++)
            {
                var a = 2 + i;
                var b = 2 + ((i + 1) % segments);
                indices.Add(0); indices.Add(a); indices.Add(b);
                indices.Add(1); indices.Add(b); indices.Add(a);
            }

            return CreateMeshModel(positions, indices, color, false);
        }

        private MeshGeometryModel3D CreateQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color color)
        {
            var positions = new Vector3Collection
            {
                ToSharpDx(p0),
                ToSharpDx(p1),
                ToSharpDx(p2),
                ToSharpDx(p3)
            };

            var indices = new IntCollection { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };
            return CreateMeshModel(positions, indices, color, true);
        }

        private void AddCube(Vector3 center, float halfSize, Color color, ViewportGizmoHandle handle)
        {
            Register(CreateCube(center, halfSize, color), handle);
        }

        private MeshGeometryModel3D CreateCube(Vector3 center, float halfSize, Color color)
        {
            var corners = new[]
            {
                center + new Vector3(-halfSize, -halfSize, -halfSize),
                center + new Vector3( halfSize, -halfSize, -halfSize),
                center + new Vector3( halfSize,  halfSize, -halfSize),
                center + new Vector3(-halfSize,  halfSize, -halfSize),
                center + new Vector3(-halfSize, -halfSize,  halfSize),
                center + new Vector3( halfSize, -halfSize,  halfSize),
                center + new Vector3( halfSize,  halfSize,  halfSize),
                center + new Vector3(-halfSize,  halfSize,  halfSize)
            };

            var positions = new Vector3Collection();
            foreach (var corner in corners)
                positions.Add(ToSharpDx(corner));

            var indices = new IntCollection
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7
            };

            return CreateMeshModel(positions, indices, color, false);
        }

        private MeshGeometryModel3D CreateMeshModel(Vector3Collection positions, IntCollection indices, Color color, bool transparent)
        {
            var geometry = new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = indices
            };
            geometry.UpdateBounds();

            return new MeshGeometryModel3D
            {
                Geometry = geometry,
                Material = CreateMaterial(color),
                IsTransparent = transparent || color.A < 255,
                CullMode = SDX.Direct3D11.CullMode.None,
                DepthBias = -100000,
                IsThrowingShadow = false,
                RenderOrder = 10000,
                EnableViewFrustumCheck = false
            };
        }

        private void Register(GeometryModel3D model, ViewportGizmoHandle handle)
        {
            Root.Children.Add(model);
            _handles[model] = handle;
        }

        private static PhongMaterial CreateMaterial(Color color)
        {
            var diffuse = new SDX.Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
            var emissive = new SDX.Color4(color.R / 255f, color.G / 255f, color.B / 255f, MathF.Min(1f, MathF.Max(0.35f, color.A / 255f)));
            return new PhongMaterial
            {
                DiffuseColor = diffuse,
                AmbientColor = diffuse,
                EmissiveColor = emissive,
                SpecularColor = new SDX.Color4(0.15f, 0.15f, 0.15f, 1f)
            };
        }

        private static void CreateBasis(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            normal = NormalizeOrDefault(normal, Vector3.UnitZ);
            var reference = MathF.Abs(Vector3.Dot(normal, Vector3.UnitY)) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
            u = NormalizeOrDefault(Vector3.Cross(reference, normal), Vector3.UnitX);
            v = NormalizeOrDefault(Vector3.Cross(normal, u), Vector3.UnitY);
        }

        private static bool AlmostEqual(Vector3 left, Vector3 right)
        {
            return Vector3.DistanceSquared(left, right) < 0.000001f;
        }

        private static Vector3 NormalizeOrDefault(Vector3 value, Vector3 fallback)
        {
            return value.LengthSquared() > 0.000001f ? Vector3.Normalize(value) : fallback;
        }

        private static SDX.Vector3 ToSharpDx(Vector3 value)
        {
            return new SDX.Vector3(value.X, value.Y, value.Z);
        }
    }
}
