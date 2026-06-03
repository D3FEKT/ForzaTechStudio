using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using SDX = SharpDX;
using Color = Windows.UI.Color;

namespace ForzaTechStudio.Views
{
    // 3D Rendering and Geometry Creation Methods
    public sealed partial class ViewportPage : Page
    {
        // Skeleton rendering maps
        private Dictionary<SkeletonNode, List<Element3D>> _skeletonRenderMap = new();
        private Dictionary<DamageMeshNode, GeometryModel3D> _damageRenderMap = new();
        private float _boneScale = 1.0f;

        // Batch rendering
        private bool _isBulkLoading;
        private readonly List<MeshNode> _pendingMeshRenders = new();
        private readonly List<DamageMeshNode> _pendingDamageMeshRenders = new();

        private void RenderMesh(MeshNode node)
        {
            if (ShouldAutoHideProxyModelBin(node.ParentModelBin))
            {
                HideMesh(node);
                return;
            }

            if (ShouldSuppressOriginalModelBin(node.ParentModelBin))
            {
                HideMesh(node);
                return;
            }

            if (_renderMap.ContainsKey(node)) return;

            if (node.GeometryData.Positions != null && (node.GeometryData.Indices == null || node.GeometryData.Indices.Length == 0))
            {
                var model = CreatePoint3D(node.GeometryData);
                if (model != null)
                {
                    _modelGroup.Children.Add(model);
                    _renderMap[node] = model;
                }
            }
            else
            {
                var geometry = CreateMesh3D(node.GeometryData, node.ParentModelBin);
                _modelGroup.Children.Add(geometry);
                _renderMap[node] = geometry;
            }
        }

        private void HideMesh(MeshNode node)
        {
            if (_renderMap.TryGetValue(node, out var geometry))
            {
                _modelGroup.Children.Remove(geometry);
                _renderMap.Remove(node);
            }
        }

        private void RenderDamageMesh(DamageMeshNode node)
        {
            if (ShouldAutoHideProxyModelBin(node.ParentModelBin))
            {
                HideDamageMesh(node);
                return;
            }

            if (ShouldSuppressOriginalModelBin(node.ParentModelBin))
            {
                HideDamageMesh(node);
                return;
            }

            if (_damageRenderMap.ContainsKey(node)) return;

            var geometry = CreateMesh3D(node.GeometryData, node.ParentModelBin);
            _modelGroup.Children.Add(geometry);
            _damageRenderMap[node] = geometry;
        }

        private void HideDamageMesh(DamageMeshNode node)
        {
            if (_damageRenderMap.TryGetValue(node, out var geometry))
            {
                _modelGroup.Children.Remove(geometry);
                _damageRenderMap.Remove(node);
            }
        }


        private async Task BatchRenderPendingMeshesAsync()
        {
            if (_pendingMeshRenders.Count == 0 && _pendingDamageMeshRenders.Count == 0)
                return;

            // (ShouldAutoHideProxy / ShouldSuppressOriginal read scene state)
            var solidMeshes = _pendingMeshRenders
                .Where(n => n.GeometryData != null
                         && !_renderMap.ContainsKey(n)
                         && !ShouldAutoHideProxyModelBin(n.ParentModelBin)
                         && !ShouldSuppressOriginalModelBin(n.ParentModelBin)
                         && !(n.GeometryData.Positions != null
                              && (n.GeometryData.Indices == null || n.GeometryData.Indices.Length == 0)))
                .ToList();

            var pointMeshes = _pendingMeshRenders
                .Where(n => n.GeometryData?.Positions != null
                         && (n.GeometryData.Indices == null || n.GeometryData.Indices.Length == 0)
                         && !_renderMap.ContainsKey(n)
                         && !ShouldAutoHideProxyModelBin(n.ParentModelBin)
                         && !ShouldSuppressOriginalModelBin(n.ParentModelBin))
                .ToList();

            var dmgMeshes = _pendingDamageMeshRenders
                .Where(n => n.GeometryData != null
                         && !_damageRenderMap.ContainsKey(n)
                         && !ShouldAutoHideProxyModelBin(n.ParentModelBin)
                         && !ShouldSuppressOriginalModelBin(n.ParentModelBin))
                .ToList();

            _pendingMeshRenders.Clear();
            _pendingDamageMeshRenders.Clear();

            // Build all solid mesh geometries in parallel on background threads
            var builtMeshes = new ConcurrentBag<(MeshNode Node, MeshGeometryModel3D Model)>();
            var builtDmg    = new ConcurrentBag<(DamageMeshNode Node, MeshGeometryModel3D Model)>();

            await Task.Run(() =>
            {
                Parallel.ForEach(solidMeshes, node =>
                {
                    try
                    {
                        var model = CreateMesh3D(node.GeometryData, node.ParentModelBin);
                        builtMeshes.Add((node, model));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BatchRender] Mesh '{node.Name}': {ex.Message}");
                    }
                });

                Parallel.ForEach(dmgMeshes, node =>
                {
                    try
                    {
                        var model = CreateMesh3D(node.GeometryData, node.ParentModelBin);
                        builtDmg.Add((node, model));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BatchRender] DmgMesh '{node.Name}': {ex.Message}");
                    }
                });
            });

            // Add all pre-built models to the scene on UI thread
            foreach (var (node, model) in builtMeshes)
            {
                if (!_renderMap.ContainsKey(node))
                {
                    _modelGroup.Children.Add(model);
                    _renderMap[node] = model;
                }
            }

            foreach (var (node, model) in builtDmg)
            {
                if (!_damageRenderMap.ContainsKey(node))
                {
                    _modelGroup.Children.Add(model);
                    _damageRenderMap[node] = model;
                }
            }

            // Point-cloud meshes are fast to build; handle them synchronously on UI thread
            foreach (var node in pointMeshes)
            {
                if (_renderMap.ContainsKey(node)) continue;
                var model = CreatePoint3D(node.GeometryData);
                if (model != null)
                {
                    _modelGroup.Children.Add(model);
                    _renderMap[node] = model;
                }
            }
        }


        private void RenderLight(LightGroupNode node)
        {
            if (_renderMap.ContainsKey(node)) return;

            // Normal-state cone (Pos + Rot)
            var normalCone = CreateLightCone(node, damage: false);
            if (normalCone != null)
            {
                _modelGroup.Children.Add(normalCone);
                _renderMap[node] = normalCone;
            }

            // Damage-state cone (DamagePos + DamageRot) ? dimmer, distinct colour
            var damageCone = CreateLightCone(node, damage: true);
            if (damageCone != null)
            {
                _modelGroup.Children.Add(damageCone);
                _lightDamageRenderMap[node] = damageCone;
            }
        }

        private void HideLight(LightGroupNode node)
        {
            if (_renderMap.TryGetValue(node, out var model))
            {
                _modelGroup.Children.Remove(model);
                _renderMap.Remove(node);
            }
            if (_lightDamageRenderMap.TryGetValue(node, out var damageModel))
            {
                _modelGroup.Children.Remove(damageModel);
                _lightDamageRenderMap.Remove(node);
            }
        }

        // Skeleton Rendering

        private void RenderSkeleton(SkeletonNode node)
        {
            if (_skeletonRenderMap.ContainsKey(node)) return;

            var elements = new List<Element3D>();
            var skeleton = node.SkeletonData;
            if (skeleton == null || skeleton.Bones.Count == 0) return;

            // Create bone lines (parent-child connections)
            var lineBuilder = new LineBuilder();
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                var bone = skeleton.Bones[i];
                var worldPos = GetBoneWorldPosition(bone);

                if (bone.ParentIndex >= 0 && bone.ParentIndex < skeleton.Bones.Count)
                {
                    var parentPos = GetBoneWorldPosition(skeleton.Bones[bone.ParentIndex]);
                    lineBuilder.AddLine(
                        new SDX.Vector3(parentPos.X, parentPos.Y, parentPos.Z) * _boneScale,
                        new SDX.Vector3(worldPos.X, worldPos.Y, worldPos.Z) * _boneScale);
                }
            }

            var boneLines = new LineGeometryModel3D
            {
                Geometry = lineBuilder.ToLineGeometry3D(),
                Color = Color.FromArgb(255, 0, 255, 128),
                Thickness = 1.5
            };
            _viewport.Items.Add(boneLines);
            elements.Add(boneLines);

            // Create joint spheres at each bone position
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                var bone = skeleton.Bones[i];
                var worldPos = GetBoneWorldPosition(bone);
                var pos = new SDX.Vector3(worldPos.X, worldPos.Y, worldPos.Z) * _boneScale;

                bool isRoot = bone.ParentIndex < 0;
                float jointRadius = isRoot ? 0.02f : 0.01f;

                var jointMesh = CreateJointSphere(pos, jointRadius);
                if (jointMesh != null)
                {
                    var jointModel = new MeshGeometryModel3D
                    {
                        Geometry = jointMesh,
                        Material = new PhongMaterial
                        {
                            DiffuseColor = isRoot
                                ? new SDX.Color4(1f, 0.3f, 0.1f, 1f)
                                : new SDX.Color4(0f, 1f, 0.5f, 1f),
                            EmissiveColor = isRoot
                                ? new SDX.Color4(0.3f, 0.1f, 0f, 1f)
                                : new SDX.Color4(0f, 0.2f, 0.1f, 1f)
                        },
                        CullMode = SDX.Direct3D11.CullMode.None
                    };
                    _viewport.Items.Add(jointModel);
                    elements.Add(jointModel);
                }

                // Render bone-local axes (small colored lines for orientation)
                if (isRoot || i < 5) // Show axes on root + first few bones
                {
                    var axesLines = CreateBoneAxes(bone, _boneScale);
                    if (axesLines != null)
                    {
                        foreach (var axisLine in axesLines)
                        {
                            _viewport.Items.Add(axisLine);
                            elements.Add(axisLine);
                        }
                    }
                }
            }

            _skeletonRenderMap[node] = elements;
        }

        private void HideSkeleton(SkeletonNode node)
        {
            if (_skeletonRenderMap.TryGetValue(node, out var elements))
            {
                foreach (var elem in elements)
                    _viewport.Items.Remove(elem);
                _skeletonRenderMap.Remove(node);
            }
        }

        private Vector3 GetBoneWorldPosition(GrannyBone bone)
        {
            // Extract translation from the world transform matrix
            var m = bone.WorldTransform;
            return new Vector3(m.M41, m.M42, m.M43);
        }

        private MeshGeometry3D CreateJointSphere(SDX.Vector3 center, float radius)
        {
            const int segments = 6;
            const int rings = 4;

            var positions = new Vector3Collection();
            var normals = new Vector3Collection();
            var indices = new IntCollection();

            // Generate sphere vertices
            for (int ring = 0; ring <= rings; ring++)
            {
                float phi = (float)(Math.PI * ring / rings);
                float sinPhi = (float)Math.Sin(phi);
                float cosPhi = (float)Math.Cos(phi);

                for (int seg = 0; seg <= segments; seg++)
                {
                    float theta = (float)(2 * Math.PI * seg / segments);
                    float sinTheta = (float)Math.Sin(theta);
                    float cosTheta = (float)Math.Cos(theta);

                    var normal = new SDX.Vector3(sinPhi * cosTheta, cosPhi, sinPhi * sinTheta);
                    positions.Add(center + normal * radius);
                    normals.Add(normal);
                }
            }

            // Generate indices
            for (int ring = 0; ring < rings; ring++)
            {
                for (int seg = 0; seg < segments; seg++)
                {
                    int current = ring * (segments + 1) + seg;
                    int next = current + segments + 1;

                    indices.Add(current); indices.Add(next); indices.Add(current + 1);
                    indices.Add(current + 1); indices.Add(next); indices.Add(next + 1);
                }
            }

            var geometry = new MeshGeometry3D
            {
                Positions = positions,
                Normals = normals,
                TriangleIndices = indices
            };
            geometry.UpdateBounds();
            return geometry;
        }

        private List<LineGeometryModel3D> CreateBoneAxes(GrannyBone bone, float scale)
        {
            var result = new List<LineGeometryModel3D>();
            var m = bone.WorldTransform;
            var origin = new SDX.Vector3(m.M41, m.M42, m.M43) * scale;

            float axisLength = 0.03f;

            // X axis (Red)
            var xDir = new SDX.Vector3(m.M11, m.M12, m.M13);
            xDir.Normalize();
            var xBuilder = new LineBuilder();
            xBuilder.AddLine(origin, origin + xDir * axisLength);
            result.Add(new LineGeometryModel3D
            {
                Geometry = xBuilder.ToLineGeometry3D(),
                Color = Color.FromArgb(255, 255, 60, 60),
                Thickness = 1.0
            });

            // Y axis (Green)
            var yDir = new SDX.Vector3(m.M21, m.M22, m.M23);
            yDir.Normalize();
            var yBuilder = new LineBuilder();
            yBuilder.AddLine(origin, origin + yDir * axisLength);
            result.Add(new LineGeometryModel3D
            {
                Geometry = yBuilder.ToLineGeometry3D(),
                Color = Color.FromArgb(255, 60, 255, 60),
                Thickness = 1.0
            });

            // Z axis (Blue)
            var zDir = new SDX.Vector3(m.M31, m.M32, m.M33);
            zDir.Normalize();
            var zBuilder = new LineBuilder();
            zBuilder.AddLine(origin, origin + zDir * axisLength);
            result.Add(new LineGeometryModel3D
            {
                Geometry = zBuilder.ToLineGeometry3D(),
                Color = Color.FromArgb(255, 60, 60, 255),
                Thickness = 1.0
            });

            return result;
        }

        // Existing methods

        private MeshGeometryModel3D CreateMesh3D(ForzaGeometryData data, ModelBinNode? modelBin = null)
        {
            var geometry = new MeshGeometry3D();
            var posCol = new Vector3Collection();
            var boneTransform = IsFiniteMatrix(data.BoneTransform) ? data.BoneTransform : Matrix4x4.Identity;
            
            if (data.InitialRenderPositions != null)
            {
                foreach (var p in data.InitialRenderPositions)
                {
                    var safe = IsFiniteVector(p) ? p : Vector3.Zero;
                    posCol.Add(new SDX.Vector3(safe.X, safe.Y, safe.Z));
                }
            }
            else if (data.RawPositions != null && data.SourceMesh != null)
            {
                var scale = data.SourceMesh.PositionScale;
                var trans = data.SourceMesh.PositionTranslate;
                var rotMatrix = data.GetRotationMatrix();
                bool hasRotation = rotMatrix != Matrix4x4.Identity;
                
                foreach (var raw in data.RawPositions)
                {
                    float nx = raw.X * scale.X;
                    float ny = raw.Y * scale.Y;
                    float nz = raw.Z * scale.Z;

                    var scaled = new Vector3(nx, ny, nz);
                    
                    // Apply rotation (after scale, before translate)
                    if (hasRotation)
                        scaled = Vector3.Transform(scaled, rotMatrix);
                    
                    var v = new Vector3(scaled.X + trans.X, scaled.Y + trans.Y, scaled.Z + trans.Z);
                    var transformed = Vector3.Transform(v, boneTransform);

                    if (!IsFiniteVector(transformed))
                        transformed = IsFiniteVector(v) ? v : Vector3.Zero;

                    posCol.Add(new SDX.Vector3(transformed.X, transformed.Y, transformed.Z));
                }
            }
            else if (data.Positions != null)
            {
                foreach (var p in data.Positions)
                {
                    var safe = IsFiniteVector(p) ? p : Vector3.Zero;
                    posCol.Add(new SDX.Vector3(safe.X, safe.Y, safe.Z));
                }
            }
            
            var normCol = new Vector3Collection();
            if (data.Normals != null)
            {
                var normRotMatrix = data.GetRotationMatrix();
                bool hasNormRotation = normRotMatrix != Matrix4x4.Identity;
                foreach (var n in data.Normals)
                {
                    var rotated = hasNormRotation ? Vector3.TransformNormal(n, normRotMatrix) : n;
                    var rn = NormalizeOrDefault(rotated, Vector3.UnitY);
                    normCol.Add(new SDX.Vector3(rn.X, rn.Y, rn.Z));
                }
            }

            var uvCol = new Vector2Collection();
            var materialUvTiling = ResolveViewportMaterialUvTiling(data, modelBin);
            if (data.UVs != null)
                foreach (var u in data.UVs)
                {
                    var transformedUv = ApplyViewportUvTransform(data, u, materialUvTiling);
                    uvCol.Add(new SDX.Vector2(transformedUv.X, transformedUv.Y));
                }
            
            var indCol = new IntCollection();
            if (data.Indices != null)
                foreach (var i in data.Indices) indCol.Add(i);

            geometry.Positions = posCol;
            geometry.Normals = normCol;
            geometry.TextureCoordinates = uvCol;
            geometry.TriangleIndices = indCol;
            geometry.UpdateBounds();

            // Build with color-only material initially so geometry appears instantly.
            // Textures are loaded asynchronously afterwards via StartViewportTextureRefreshAsync.
            var material = CreateViewportMaterial(data, modelBin, out bool isTransparent, loadTextures: false);

            return new MeshGeometryModel3D
            {
                Geometry = geometry,
                Material = material,
                IsTransparent = isTransparent,
                CullMode = SDX.Direct3D11.CullMode.None
            };
        }

        private static Vector2 ApplyViewportUvTransform(ForzaGeometryData data, Vector2 uv, Vector2 materialUvTiling)
        {
            var transforms = data?.SourceMesh?.TexCoordTransforms;
            float sourceU = uv.X;
            float sourceV = 1f - uv.Y;
            var transformedUv = new Vector2(sourceU, sourceV);

            if (transforms != null && transforms.Length > 0)
            {
                var transform = transforms[0];
                if (transform != default)
                {
                    float u = sourceU * transform.Y + transform.X;
                    float transformedSourceV = sourceV * transform.W + transform.Z;
                    transformedUv = new Vector2(u, transformedSourceV);
                }
            }

            transformedUv = new Vector2(
                transformedUv.X * materialUvTiling.X,
                transformedUv.Y * materialUvTiling.Y);

            return float.IsFinite(transformedUv.X) && float.IsFinite(transformedUv.Y)
                ? transformedUv
                : uv;
        }
        
        private PointGeometryModel3D CreatePoint3D(ForzaGeometryData data)
        {
            var geometry = new PointGeometry3D();
            var posCol = new Vector3Collection();
            
            foreach (var p in data.Positions)
            {
                posCol.Add(new SDX.Vector3(p.X, p.Y, p.Z));
            }
            
            geometry.Positions = posCol;
            
            return new PointGeometryModel3D
            {
                Geometry = geometry,
                Color = Color.FromArgb(255, 0, 255, 0),
                Size = new Windows.Foundation.Size(5, 5),
                FixedSize = true
            };
        }

        // Builds a small oriented edge pyramid for a single CarLight position.

        private LineGeometryModel3D CreateLightCone(LightGroupNode node, bool damage)
        {
            const float radius = 0.04f;
            const float height = 0.12f;

            // Normal state: light blue. Damage state: slightly dimmer blue.
            var color = damage
                ? Color.FromArgb(191, 77, 128, 179)
                : Color.FromArgb(255, 140, 204, 255);

            var builder = new LineBuilder();
            AppendLightConeWireframe(builder, node, damage, radius, height);

            return new LineGeometryModel3D
            {
                Geometry = builder.ToLineGeometry3D(),
                Color = color,
                Thickness = damage ? 0.9 : 1.1
            };
        }

        private static void AppendLightConeWireframe(
            LineBuilder builder,
            LightGroupNode node,
            bool damage,
            float radius = 0.04f,
            float height = 0.12f)
        {
            var group = node.GroupData;

            var posV4 = damage ? group.DamagePos : group.Pos;
            var rotQ = damage ? group.DamageRotation : group.Rotation;

            var origin = new SDX.Vector3(posV4.X, posV4.Y, posV4.Z);
            var qn = System.Numerics.Quaternion.Normalize(rotQ);
            var rotMat = System.Numerics.Matrix4x4.CreateFromQuaternion(qn);

            var axisRight = new SDX.Vector3(rotMat.M11, rotMat.M12, rotMat.M13);
            var axisUp = new SDX.Vector3(rotMat.M21, rotMat.M22, rotMat.M23);
            var axisAhead = new SDX.Vector3(rotMat.M31, rotMat.M32, rotMat.M33);
            var apex = origin + axisAhead * height;

            AppendPyramidWireframeLines(
                builder,
                origin + axisRight * -radius + axisUp * -radius,
                origin + axisRight * radius + axisUp * -radius,
                origin + axisRight * radius + axisUp * radius,
                origin + axisRight * -radius + axisUp * radius,
                apex);
        }

        private static LineGeometryModel3D CreatePyramidWireframe(
            SDX.Vector3 base0,
            SDX.Vector3 base1,
            SDX.Vector3 base2,
            SDX.Vector3 base3,
            SDX.Vector3 apex,
            Color color,
            double thickness)
        {
            var builder = new LineBuilder();
            AppendPyramidWireframeLines(builder, base0, base1, base2, base3, apex);

            return new LineGeometryModel3D
            {
                Geometry = builder.ToLineGeometry3D(),
                Color = color,
                Thickness = thickness
            };
        }

        private static void AppendPyramidWireframeLines(
            LineBuilder builder,
            SDX.Vector3 base0,
            SDX.Vector3 base1,
            SDX.Vector3 base2,
            SDX.Vector3 base3,
            SDX.Vector3 apex)
        {
            builder.AddLine(base0, base1);
            builder.AddLine(base1, base2);
            builder.AddLine(base2, base3);
            builder.AddLine(base3, base0);

            builder.AddLine(apex, base0);
            builder.AddLine(apex, base1);
            builder.AddLine(apex, base2);
            builder.AddLine(apex, base3);
        }

        private LineGeometryModel3D CreateGrid()
        {
            var builder = new LineBuilder();
            int range = 10;

            for (int i = -range; i <= range; i++)
            {
                builder.AddLine(new SDX.Vector3(-range, 0, i), new SDX.Vector3(range, 0, i));
                builder.AddLine(new SDX.Vector3(i, 0, -range), new SDX.Vector3(i, 0, range));
            }

            return new LineGeometryModel3D
            {
                Geometry = builder.ToLineGeometry3D(),
                Color = Color.FromArgb(100, 1, 1, 1),
                Thickness = 0.6
            };
        }

        private void UpdateMeshColors(bool useSingleColor)
        {
            foreach (var kvp in _renderMap)
            {
                var node = kvp.Key;
                var model = kvp.Value;

                if (model is MeshGeometryModel3D meshModel && node is MeshNode meshNode)
                {
                    ApplyViewportMaterial(meshModel, meshNode.GeometryData, meshNode.ParentModelBin);
                }
            }

            foreach (var kvp in _damageRenderMap)
            {
                if (kvp.Value is MeshGeometryModel3D meshModel)
                    ApplyViewportMaterial(meshModel, kvp.Key.GeometryData, kvp.Key.ParentModelBin);
            }

            foreach (var kvp in _carbinMaterialContextMap)
            {
                if (kvp.Key is MeshGeometryModel3D meshModel)
                    ApplyViewportMaterial(meshModel, kvp.Value.Geometry, kvp.Value.ModelBin);
            }
        }

        private void AutoFitCamera()
        {
             if (_modelGroup == null || _viewport.Camera is not PerspectiveCamera camera) return;

             var totalBounds = new SDX.BoundingBox();
             bool hasBounds = false;

             foreach (var child in _modelGroup.Children)
             {
                 if (child is GeometryModel3D mesh && mesh.IsRendering && mesh.Geometry != null)
                 {
                     if (!hasBounds)
                     {
                         totalBounds = mesh.Bounds;
                         hasBounds = true;
                     }
                     else
                     {
                         totalBounds = SDX.BoundingBox.Merge(totalBounds, mesh.Bounds);
                     }
                 }
             }

             // Also consider skeleton bounds
             foreach (var kvp in _skeletonRenderMap)
             {
                 foreach (var elem in kvp.Value)
                 {
                     if (elem is MeshGeometryModel3D meshElem && meshElem.Geometry != null)
                     {
                         if (!hasBounds)
                         {
                             totalBounds = meshElem.Bounds;
                             hasBounds = true;
                         }
                         else
                         {
                             totalBounds = SDX.BoundingBox.Merge(totalBounds, meshElem.Bounds);
                         }
                     }
                 }
             }

             if (!hasBounds || totalBounds.Minimum == totalBounds.Maximum) return;

             var center = (totalBounds.Maximum + totalBounds.Minimum) / 2.0f;
             var radius = (totalBounds.Maximum - totalBounds.Minimum).Length() / 2.0f;

             var lookDir = camera.LookDirection;
             if (lookDir.LengthSquared() < 0.001f) lookDir = new SDX.Vector3(-1, -1, -1);
             lookDir.Normalize();
              
             var distance = radius * 2.5f;
             if (distance < 10.0f) distance = 10.0f;

             camera.Position = center - (lookDir * distance);
             camera.LookDirection = lookDir * distance; 
             camera.UpDirection = new SDX.Vector3(0, 1, 0);
             camera.FarPlaneDistance = Math.Max(distance * 10, 1000);
             camera.NearPlaneDistance = Math.Max(distance * 0.001, 0.01);
        }

        internal void FocusOnSelected()
        {
            if (_viewport?.Camera is not PerspectiveCamera camera) return;

            // Collect meshes to focus on: multi-selection, single MeshNode, or ModelBinNode children
            var meshesToFocus = new List<MeshGeometryModel3D>();

            if (_multiSelectedMeshes.Count > 0)
            {
                foreach (var meshNode in _multiSelectedMeshes)
                {
                    if (_renderMap.TryGetValue(meshNode, out var elem) && elem is MeshGeometryModel3D m && m.Geometry != null)
                        meshesToFocus.Add(m);
                }
            }
            else if (ViewModel.SelectedNode is MeshNode singleMesh)
            {
                if (_renderMap.TryGetValue(singleMesh, out var elem) && elem is MeshGeometryModel3D m && m.Geometry != null)
                    meshesToFocus.Add(m);
            }
            else if (ViewModel.SelectedNode is ModelBinNode binNode)
            {
                // Collect all mesh children of this ModelBin
                foreach (var child in binNode.Children)
                {
                    if (child is MeshNode meshChild && _renderMap.TryGetValue(meshChild, out var elem)
                        && elem is MeshGeometryModel3D m && m.Geometry != null)
                        meshesToFocus.Add(m);
                }
            }

            if (meshesToFocus.Count == 0)
            {
                // Nothing specific selected � fall back to fitting all
                AutoFitCamera();
                return;
            }

            var totalBounds = meshesToFocus[0].Bounds;
            for (int i = 1; i < meshesToFocus.Count; i++)
                totalBounds = SDX.BoundingBox.Merge(totalBounds, meshesToFocus[i].Bounds);

            if (totalBounds.Minimum == totalBounds.Maximum) return;

            var center = (totalBounds.Maximum + totalBounds.Minimum) / 2.0f;
            var radius = (totalBounds.Maximum - totalBounds.Minimum).Length() / 2.0f;

            var lookDir = camera.LookDirection;
            if (lookDir.LengthSquared() < 0.001f) lookDir = new SDX.Vector3(-1, -1, -1);
            lookDir.Normalize();

            var distance = radius * 2.5f;
            if (distance < 1.0f) distance = 1.0f;

            camera.Position = center - (lookDir * distance);
            camera.LookDirection = lookDir * distance;
            camera.UpDirection = new SDX.Vector3(0, 1, 0);
            camera.FarPlaneDistance = Math.Max(distance * 10, 1000);
            camera.NearPlaneDistance = Math.Max(distance * 0.001, 0.01);
        }

        private void RenderLocator(LocatorNode node)
        {
            if (_renderMap.ContainsKey(node)) return;

            var model = CreateLocatorCone(node);
            if (model != null)
            {
                _modelGroup.Children.Add(model);
                _renderMap[node] = model;
            }
        }

        private void HideLocator(LocatorNode node)
        {
            if (_renderMap.TryGetValue(node, out var model))
            {
                _modelGroup.Children.Remove(model);
                _renderMap.Remove(node);
            }
        }

        private LineGeometryModel3D CreateLocatorCone(LocatorNode node)
        {
            const float radius = 0.025f;
            const float height = 0.0625f;

            var m = node.LocatorEntry.SceneTransform;

            SDX.Vector3 TransformPoint(float lx, float ly, float lz)
            {
                float wx = lx * m.M11 + ly * m.M21 + lz * m.M31 + m.M41;
                float wy = lx * m.M12 + ly * m.M22 + lz * m.M32 + m.M42;
                float wz = lx * m.M13 + ly * m.M23 + lz * m.M33 + m.M43;
                return new SDX.Vector3(wx, wy, wz);
            }

            bool isSelected = ViewModel.SelectedNode == node;
            var color = isSelected
                ? Color.FromArgb(255, 255, 255, 0)
                : Color.FromArgb(255, 199, 148, 97); // light brown

            return CreatePyramidWireframe(
                TransformPoint(-radius, -radius, 0f),
                TransformPoint( radius, -radius, 0f),
                TransformPoint( radius,  radius, 0f),
                TransformPoint(-radius,  radius, 0f),
                TransformPoint(0f, 0f, height),
                color,
                isSelected ? 1.8 : 1.2);
        }

        private void RefreshLocatorCone(LocatorNode node)
        {
            HideLocator(node);
            if (node.IsChecked == true)
                RenderLocator(node);
        }

        // AvPin (POI edge pyramid) rendering

        private void RenderAvPin(AvPinNode node)
        {
            if (_renderMap.ContainsKey(node)) return;
            var model = CreateAvPinCone(node);
            if (model != null)
            {
                _modelGroup.Children.Add(model);
                _renderMap[node] = model;
            }
        }

        private void HideAvPin(AvPinNode node)
        {
            if (_renderMap.TryGetValue(node, out var model))
            {
                _modelGroup.Children.Remove(model);
                _renderMap.Remove(node);
            }
        }

        private void RefreshAvPinCone(AvPinNode node)
        {
            HideAvPin(node);
            if (node.IsChecked == true)
                RenderAvPin(node);
        }

        private LineGeometryModel3D? CreateAvPinCone(AvPinNode node)
        {
            const float radius   = 0.025f;
            const float height   = 0.0625f;

            var vis = node.PoiData?.Visibility;
            if (vis == null) return null;

            // World-space origin from Visibility.Pos
            var origin = new SDX.Vector3((float)vis.PosX, (float)vis.PosY, (float)vis.PosZ);

            // Build an orientation from AxisYaw / AxisPitch (both in degrees).
            // Yaw = rotation around world Y. Pitch = rotation around local X afterwards.
            float yawRad   = (float)(vis.AxisYaw   * Math.PI / 180.0);
            float pitchRad = (float)(vis.AxisPitch  * Math.PI / 180.0);

            var rotY   = System.Numerics.Matrix4x4.CreateRotationY(yawRad);
            var rotX   = System.Numerics.Matrix4x4.CreateRotationX(pitchRad);
            var rotMat = rotX * rotY;

            // Local axes in world space (the cone points along local +Z = forward).
            var axisRight   = new SDX.Vector3(rotMat.M11, rotMat.M12, rotMat.M13);
            var axisUp      = new SDX.Vector3(rotMat.M21, rotMat.M22, rotMat.M23);
            var axisForward = new SDX.Vector3(rotMat.M31, rotMat.M32, rotMat.M33);

            bool isSelected = ViewModel.SelectedNode == node;
            var color = isSelected
                ? Color.FromArgb(255, 255, 255, 0)
                : Color.FromArgb(255, 255, 255, 255);

            var apex = origin + axisForward * height;
            return CreatePyramidWireframe(
                origin + axisRight * -radius + axisUp * -radius,
                origin + axisRight *  radius + axisUp * -radius,
                origin + axisRight *  radius + axisUp *  radius,
                origin + axisRight * -radius + axisUp *  radius,
                apex,
                color,
                isSelected ? 1.8 : 1.2);
        }
    }
}
