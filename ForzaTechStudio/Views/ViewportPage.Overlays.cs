using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using SDX = SharpDX;
using Color = Windows.UI.Color;

namespace ForzaTechStudio.Views
{
    // Visual overlays: wireframe, normals, bounding boxes, stats
    public sealed partial class ViewportPage : Page
    {
        private bool _wireframeEnabled;
        private bool _normalsEnabled;
        private bool _boundingBoxesEnabled;

        // Wireframe overlay models
        private readonly List<LineGeometryModel3D> _wireframeModels = new();

        // Normals overlay models
        private readonly List<LineGeometryModel3D> _normalsModels = new();

        // Bounding box overlay models
        private readonly List<LineGeometryModel3D> _bboxModels = new();

        // Stats timer
        private Microsoft.UI.Xaml.DispatcherTimer? _statsTimer;

        private void WireframeToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item)
            {
                _wireframeEnabled = item.IsChecked;
                UpdateWireframeOverlay();
            }
        }

        private void NormalsToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item)
            {
                _normalsEnabled = item.IsChecked;
                UpdateNormalsOverlay();
            }
        }

        private void BoundingBoxToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item)
            {
                _boundingBoxesEnabled = item.IsChecked;
                UpdateBoundingBoxOverlay();
            }
        }

        private void UpdateWireframeOverlay()
        {
            // Remove existing wireframe models
            foreach (var wf in _wireframeModels)
            {
                _viewport?.Items.Remove(wf);
            }
            _wireframeModels.Clear();

            if (!_wireframeEnabled || _viewport == null) return;

            foreach (var kvp in _renderMap)
            {
                if (kvp.Value is MeshGeometryModel3D meshModel &&
                    meshModel.Geometry is MeshGeometry3D geo &&
                    meshModel.IsRendering)
                {
                    var wireframe = CreateWireframeFromMesh(geo);
                    if (wireframe != null)
                    {
                        _viewport.Items.Add(wireframe);
                        _wireframeModels.Add(wireframe);
                    }
                }
            }
        }

        private LineGeometryModel3D? CreateWireframeFromMesh(MeshGeometry3D geo)
        {
            if (geo.Positions == null || geo.TriangleIndices == null) return null;

            var builder = new LineBuilder();
            var indices = geo.TriangleIndices;
            var positions = geo.Positions;

            // Deduplicate edges using sorted tuple pairs
            var edgeSet = new HashSet<(int, int)>();

            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                AddEdge(edgeSet, i0, i1);
                AddEdge(edgeSet, i1, i2);
                AddEdge(edgeSet, i2, i0);
            }

            foreach (var (a, b) in edgeSet)
            {
                if (a < positions.Count && b < positions.Count)
                {
                    builder.AddLine(positions[a], positions[b]);
                }
            }

            return new LineGeometryModel3D
            {
                Geometry = builder.ToLineGeometry3D(),
                Color = Color.FromArgb(180, 0, 255, 255),
                Thickness = 0.4
            };
        }

        private static void AddEdge(HashSet<(int, int)> set, int a, int b)
        {
            var edge = a < b ? (a, b) : (b, a);
            set.Add(edge);
        }

        private void UpdateNormalsOverlay()
        {
            foreach (var nm in _normalsModels)
            {
                _viewport?.Items.Remove(nm);
            }
            _normalsModels.Clear();

            if (!_normalsEnabled || _viewport == null) return;

            foreach (var kvp in _renderMap)
            {
                if (kvp.Value is MeshGeometryModel3D meshModel &&
                    meshModel.Geometry is MeshGeometry3D geo &&
                    meshModel.IsRendering)
                {
                    var normalLines = CreateNormalsFromMesh(geo);
                    if (normalLines != null)
                    {
                        _viewport.Items.Add(normalLines);
                        _normalsModels.Add(normalLines);
                    }
                }
            }
        }

        private LineGeometryModel3D? CreateNormalsFromMesh(MeshGeometry3D geo)
        {
            if (geo.Positions == null || geo.Normals == null) return null;

            var builder = new LineBuilder();
            float normalLength = 0.05f;

            // Sample normals to avoid too many lines
            int step = Math.Max(1, geo.Positions.Count / 500);

            for (int i = 0; i < geo.Positions.Count && i < geo.Normals.Count; i += step)
            {
                var p = geo.Positions[i];
                var n = geo.Normals[i];
                builder.AddLine(p, p + n * normalLength);
            }

            return new LineGeometryModel3D
            {
                Geometry = builder.ToLineGeometry3D(),
                Color = Color.FromArgb(200, 255, 255, 0),
                Thickness = 0.5
            };
        }

        private void UpdateBoundingBoxOverlay()
        {
            foreach (var bb in _bboxModels)
            {
                _viewport?.Items.Remove(bb);
            }
            _bboxModels.Clear();

            if (!_boundingBoxesEnabled || _viewport == null) return;

            foreach (var kvp in _renderMap)
            {
                if (kvp.Value is MeshGeometryModel3D meshModel &&
                    meshModel.Geometry is MeshGeometry3D geo &&
                    meshModel.IsRendering)
                {
                    var bbox = CreateBoundingBox(geo.Bound);
                    if (bbox != null)
                    {
                        _viewport.Items.Add(bbox);
                        _bboxModels.Add(bbox);
                    }
                }
            }
        }

        private LineGeometryModel3D? CreateBoundingBox(SDX.BoundingBox bounds)
        {
            var min = bounds.Minimum;
            var max = bounds.Maximum;

            if (min == max) return null;

            var builder = new LineBuilder();

            // Bottom face
            builder.AddLine(new SDX.Vector3(min.X, min.Y, min.Z), new SDX.Vector3(max.X, min.Y, min.Z));
            builder.AddLine(new SDX.Vector3(max.X, min.Y, min.Z), new SDX.Vector3(max.X, min.Y, max.Z));
            builder.AddLine(new SDX.Vector3(max.X, min.Y, max.Z), new SDX.Vector3(min.X, min.Y, max.Z));
            builder.AddLine(new SDX.Vector3(min.X, min.Y, max.Z), new SDX.Vector3(min.X, min.Y, min.Z));

            // Top face
            builder.AddLine(new SDX.Vector3(min.X, max.Y, min.Z), new SDX.Vector3(max.X, max.Y, min.Z));
            builder.AddLine(new SDX.Vector3(max.X, max.Y, min.Z), new SDX.Vector3(max.X, max.Y, max.Z));
            builder.AddLine(new SDX.Vector3(max.X, max.Y, max.Z), new SDX.Vector3(min.X, max.Y, max.Z));
            builder.AddLine(new SDX.Vector3(min.X, max.Y, max.Z), new SDX.Vector3(min.X, max.Y, min.Z));

            // Verticals
            builder.AddLine(new SDX.Vector3(min.X, min.Y, min.Z), new SDX.Vector3(min.X, max.Y, min.Z));
            builder.AddLine(new SDX.Vector3(max.X, min.Y, min.Z), new SDX.Vector3(max.X, max.Y, min.Z));
            builder.AddLine(new SDX.Vector3(max.X, min.Y, max.Z), new SDX.Vector3(max.X, max.Y, max.Z));
            builder.AddLine(new SDX.Vector3(min.X, min.Y, max.Z), new SDX.Vector3(min.X, max.Y, max.Z));

            return new LineGeometryModel3D
            {
                Geometry = builder.ToLineGeometry3D(),
                Color = Color.FromArgb(200, 0, 255, 0),
                Thickness = 0.6
            };
        }

        private void ClearAllOverlays()
        {
            foreach (var wf in _wireframeModels)
                _viewport?.Items.Remove(wf);
            _wireframeModels.Clear();

            foreach (var nm in _normalsModels)
                _viewport?.Items.Remove(nm);
            _normalsModels.Clear();

            foreach (var bb in _bboxModels)
                _viewport?.Items.Remove(bb);
            _bboxModels.Clear();
        }

        // Refreshes all active overlays (call after mesh visibility/geometry changes).
        private void RefreshOverlays()
        {
            if (_wireframeEnabled) UpdateWireframeOverlay();
            if (_normalsEnabled) UpdateNormalsOverlay();
            if (_boundingBoxesEnabled) UpdateBoundingBoxOverlay();
        }

        // Stats Overlay

        private void InitializeStatsOverlay()
        {
            _statsTimer = new Microsoft.UI.Xaml.DispatcherTimer();
            _statsTimer.Interval = TimeSpan.FromMilliseconds(500);
            _statsTimer.Tick += StatsTimer_Tick;
            _statsTimer.Start();
        }

        private void StopStatsOverlay()
        {
            _statsTimer?.Stop();
            _statsTimer = null;
        }

        private void StatsTimer_Tick(object? sender, object e)
        {
            if (StatsOverlay == null || _viewport == null) return;

            // Count triangles and vertices
            int totalVertices = 0;
            int totalTriangles = 0;
            int visibleMeshes = 0;

            foreach (var kvp in _renderMap)
            {
                if (kvp.Value is MeshGeometryModel3D meshModel &&
                    meshModel.IsRendering &&
                    meshModel.Geometry is MeshGeometry3D geo)
                {
                    totalVertices += geo.Positions?.Count ?? 0;
                    totalTriangles += (geo.TriangleIndices?.Count ?? 0) / 3;
                    visibleMeshes++;
                }
            }

            int skeletonCount = _skeletonRenderMap.Count;

            if (StatsText != null)
            {
                string stats = $"Meshes: {visibleMeshes}  |  Verts: {totalVertices:N0}  |  Tris: {totalTriangles:N0}";
                if (skeletonCount > 0)
                    stats += $"  |  Skeletons: {skeletonCount}";
                StatsText.Text = stats;
            }
        }
    }
}
