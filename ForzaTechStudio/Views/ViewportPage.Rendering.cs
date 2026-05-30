using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

        private void RenderMesh(MeshNode node)
        {
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
            if (data.UVs != null)
                foreach (var u in data.UVs) uvCol.Add(new SDX.Vector2(u.X, u.Y));
            
            var indCol = new IntCollection();
            if (data.Indices != null)
                foreach (var i in data.Indices) indCol.Add(i);

            geometry.Positions = posCol;
            geometry.Normals = normCol;
            geometry.TextureCoordinates = uvCol;
            geometry.TriangleIndices = indCol;
            geometry.UpdateBounds();

            var material = CreateViewportMaterial(data, modelBin);

            return new MeshGeometryModel3D
            {
                Geometry = geometry,
                Material = material,
                CullMode = SDX.Direct3D11.CullMode.None
            };
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

        // Builds a small oriented cone for a single CarLight position.

        private MeshGeometryModel3D CreateLightCone(LightGroupNode node, bool damage)
        {
            const int   segments = 10;
            const float coneRadius = 0.04f;
            const float coneHeight = 0.12f;

            var group = node.GroupData;

            // Choose pos + rot based on which variant we're building
            var posV4  = damage ? group.DamagePos : group.Pos;
            var rotQ   = damage ? group.DamageRotation : group.Rotation;

            // World origin of this cone
            var origin = new SDX.Vector3(posV4.X, posV4.Y, posV4.Z);

            // Orientation: rotate local +Y so the cone points "forward" for the light.

            var qn = System.Numerics.Quaternion.Normalize(rotQ);
            var rotMat = System.Numerics.Matrix4x4.CreateFromQuaternion(qn);

            // Local axes in world space (row-vector convention: col = row of rotMat)
            var axisRight = new SDX.Vector3(rotMat.M11,  rotMat.M12,  rotMat.M13);  // local +X
            var axisUp    = new SDX.Vector3(rotMat.M21,  rotMat.M22,  rotMat.M23);  // local +Y
            var axisAhead = new SDX.Vector3(rotMat.M31,  rotMat.M32,  rotMat.M33);  // local +Z

            // Cone apex: point along local +Z axis
            var apex = origin + axisAhead * coneHeight;

            var positions = new Vector3Collection();
            var normals   = new Vector3Collection();
            var indices   = new IntCollection();

            // Apex (index 0)
            positions.Add(apex);
            normals.Add(axisAhead);

            // Base ring (indices 1 ? segments) - in local XY plane
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)(2.0 * Math.PI * i / segments);
                float lx = (float)Math.Cos(angle) * coneRadius;
                float ly = (float)Math.Sin(angle) * coneRadius;

                // Ring vertex in world space
                var ringPt = origin + axisRight * lx + axisUp * ly;
                positions.Add(ringPt);

                // Approximate outward normal for shading
                var outward = SDX.Vector3.Normalize(ringPt - origin);
                normals.Add(outward);
            }

            // Base centre (index segments+1)
            int baseCenterIdx = positions.Count;
            positions.Add(origin);
            normals.Add(-axisAhead);

            // Side triangles: apex ? ring
            for (int i = 0; i < segments; i++)
            {
                int curr = 1 + i;
                int next = 1 + (i + 1) % segments;
                indices.Add(0);    indices.Add(next); indices.Add(curr);
            }

            // Base cap
            for (int i = 0; i < segments; i++)
            {
                int curr = 1 + i;
                int next = 1 + (i + 1) % segments;
                indices.Add(baseCenterIdx); indices.Add(curr); indices.Add(next);
            }

            var geometry = new MeshGeometry3D
            {
                Positions       = positions,
                Normals         = normals,
                TriangleIndices = indices
            };
            geometry.UpdateBounds();

            // Normal state: light blue. Damage state: slightly dimmer blue.
            SDX.Color4 diffuse  = damage
                ? new SDX.Color4(0.30f, 0.50f, 0.70f, 0.75f)   // damage
                : new SDX.Color4(0.55f, 0.80f, 1.00f, 1.0f);   // normal 

            SDX.Color4 emissive = damage
                ? new SDX.Color4(0.02f, 0.05f, 0.10f, 1f)
                : new SDX.Color4(0.05f, 0.10f, 0.20f, 1f);

            return new MeshGeometryModel3D
            {
                Geometry = geometry,
                Material = new PhongMaterial
                {
                    DiffuseColor  = diffuse,
                    EmissiveColor = emissive
                },
                CullMode = SDX.Direct3D11.CullMode.None
            };
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
                 if (child is MeshGeometryModel3D mesh && mesh.IsRendering && mesh.Geometry != null)
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

        private MeshGeometryModel3D CreateLocatorCone(LocatorNode node)
        {
            const int segments = 8;
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

            var positions = new Vector3Collection();
            var indices   = new IntCollection();
            var normals   = new Vector3Collection();


            // Apex
            positions.Add(TransformPoint(0f, 0f, height));
            normals.Add(new SDX.Vector3(0, 0, 1));

            // Base ring
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)(2.0 * Math.PI * i / segments);
                float lx = (float)Math.Cos(angle) * radius;
                float ly = (float)Math.Sin(angle) * radius;
                positions.Add(TransformPoint(lx, ly, 0f));
                normals.Add(new SDX.Vector3(lx, ly, 0f));
            }

            // Base centre
            int baseCenterIdx = positions.Count;
            positions.Add(TransformPoint(0f, 0f, 0f));
            normals.Add(new SDX.Vector3(0, 0, -1));

            // Side triangles (apex ? ring)
            for (int i = 0; i < segments; i++)
            {
                int curr = 1 + i;
                int next = 1 + (i + 1) % segments;
                indices.Add(0);    indices.Add(next); indices.Add(curr);
            }

            // Base cap
            for (int i = 0; i < segments; i++)
            {
                int curr = 1 + i;
                int next = 1 + (i + 1) % segments;
                indices.Add(baseCenterIdx); indices.Add(curr); indices.Add(next);
            }

            var geometry = new MeshGeometry3D
            {
                Positions       = positions,
                Normals         = normals,
                TriangleIndices = indices
            };
            geometry.UpdateBounds();

            bool isSelected = ViewModel.SelectedNode == node;
            var color = isSelected
                ? new SDX.Color4(1f, 1f, 0f, 1f)
                : new SDX.Color4(0.78f, 0.58f, 0.38f, 1f); // light brown

            return new MeshGeometryModel3D
            {
                Geometry = geometry,
                Material = new PhongMaterial
                {
                    DiffuseColor  = color,
                    EmissiveColor = isSelected ? new SDX.Color4(0.4f, 0.4f, 0f, 1f) : new SDX.Color4(0.12f, 0.06f, 0.0f, 1f)
                },
                CullMode = SDX.Direct3D11.CullMode.None
            };
        }

        private void RefreshLocatorCone(LocatorNode node)
        {
            HideLocator(node);
            if (node.IsChecked == true)
                RenderLocator(node);
        }

        // AvPin (POI Cone) Rendering

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

        private MeshGeometryModel3D CreateAvPinCone(AvPinNode node)
        {
            const int   segments = 8;
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

            var apex = origin + axisForward * height;

            var positions = new Vector3Collection();
            var normals   = new Vector3Collection();
            var indices   = new IntCollection();

            // Apex (index 0)
            positions.Add(apex);
            normals.Add(axisForward);

            // Base ring
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)(2.0 * Math.PI * i / segments);
                float lx = (float)Math.Cos(angle) * radius;
                float ly = (float)Math.Sin(angle) * radius;
                var pt = origin + axisRight * lx + axisUp * ly;
                positions.Add(pt);
                normals.Add(SDX.Vector3.Normalize(pt - origin));
            }

            // Base centre
            int baseCenterIdx = positions.Count;
            positions.Add(origin);
            normals.Add(-axisForward);

            // Side triangles (apex ? ring)
            for (int i = 0; i < segments; i++)
            {
                int curr = 1 + i;
                int next = 1 + (i + 1) % segments;
                indices.Add(0); indices.Add(next); indices.Add(curr);
            }

            // Base cap
            for (int i = 0; i < segments; i++)
            {
                int curr = 1 + i;
                int next = 1 + (i + 1) % segments;
                indices.Add(baseCenterIdx); indices.Add(curr); indices.Add(next);
            }

            var geometry = new MeshGeometry3D
            {
                Positions       = positions,
                Normals         = normals,
                TriangleIndices = indices
            };
            geometry.UpdateBounds();

            bool isSelected = ViewModel.SelectedNode == node;
            var diffuse  = isSelected
                ? new SDX.Color4(1f, 1f, 0f, 1f)          // yellow when selected
                : new SDX.Color4(1f, 1f, 1f, 1f);          // white
            var emissive = isSelected
                ? new SDX.Color4(0.4f, 0.4f, 0f, 1f)
                : new SDX.Color4(0.15f, 0.15f, 0.15f, 1f);

            return new MeshGeometryModel3D
            {
                Geometry = geometry,
                Material = new PhongMaterial
                {
                    DiffuseColor  = diffuse,
                    EmissiveColor = emissive
                },
                CullMode = SDX.Direct3D11.CullMode.None
            };
        }
    }
}
