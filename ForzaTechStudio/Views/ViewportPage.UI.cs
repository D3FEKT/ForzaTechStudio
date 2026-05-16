using ForzaTools.Bundles.Blobs;
using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Text;
using SDX = SharpDX;
using Color = Windows.UI.Color;

namespace ForzaTechStudio.Views
{
    // UI Management and Selection Handling Methods
    public sealed partial class ViewportPage : Page
    {
        private bool _isUpdatingLocatorUi = false;
        private SDX.Color4 _singleColor = new SDX.Color4(0.5f, 0.5f, 0.5f, 1.0f);
        private float _sceneOpacity = 1.0f;

        private void UpdateTransformUI()
        {
            if (ModelBinSelector.SelectedItem is not ModelBinNode modelBin) 
            {
                ClearTransformFields();
                return;
            }
            
            var meshScope = MeshSelector.SelectedItem as MeshScopeItem;
            if (meshScope == null)
            {
                ClearTransformFields();
                return;
            }

            // Determine affected meshes
            IEnumerable<MeshNode> targetMeshes;
            if (meshScope.Node != null)
            {
                targetMeshes = new[] { meshScope.Node };
            }
            else if (!string.IsNullOrEmpty(meshScope.MaterialGroup))
            {
                targetMeshes = modelBin.Children.OfType<MeshNode>()
                    .Where(m => m.GeometryData?.MaterialName == meshScope.MaterialGroup);
            }
            else
            {
                targetMeshes = modelBin.Children.OfType<MeshNode>();
            }
            
            var meshList = targetMeshes.ToList();
            if (meshList.Count == 0)
            {
                ClearTransformFields();
                return;
            }
            
            // Get reference values - always show mesh translate (that's what gets saved)
            var firstMesh = meshList[0];
            var refScale = firstMesh.GeometryData?.SourceMesh?.PositionScale ?? Vector4.One;
            var refTrans = firstMesh.GeometryData?.SourceMesh?.PositionTranslate ?? Vector4.Zero;
            var refRot = firstMesh.GeometryData?.RotationEulerDegrees ?? Vector3.Zero;
            
            // Check for conflicts across multiple meshes
            bool conflictSX = false, conflictSY = false, conflictSZ = false;
            bool conflictRX = false, conflictRY = false, conflictRZ = false;
            bool conflictTX = false, conflictTY = false, conflictTZ = false;
            
            for (int i = 1; i < meshList.Count; i++)
            {
                var m = meshList[i];
                var s = m.GeometryData?.SourceMesh?.PositionScale ?? Vector4.One;
                var t = m.GeometryData?.SourceMesh?.PositionTranslate ?? Vector4.Zero;
                var r = m.GeometryData?.RotationEulerDegrees ?? Vector3.Zero;
                
                if (Math.Abs(s.X - refScale.X) > 0.001f) conflictSX = true;
                if (Math.Abs(s.Y - refScale.Y) > 0.001f) conflictSY = true;
                if (Math.Abs(s.Z - refScale.Z) > 0.001f) conflictSZ = true;
                if (Math.Abs(r.X - refRot.X) > 0.001f) conflictRX = true;
                if (Math.Abs(r.Y - refRot.Y) > 0.001f) conflictRY = true;
                if (Math.Abs(r.Z - refRot.Z) > 0.001f) conflictRZ = true;
                if (Math.Abs(t.X - refTrans.X) > 0.001f) conflictTX = true;
                if (Math.Abs(t.Y - refTrans.Y) > 0.001f) conflictTY = true;
                if (Math.Abs(t.Z - refTrans.Z) > 0.001f) conflictTZ = true;
            }
            
            bool anyConflict = conflictSX || conflictSY || conflictSZ || conflictRX || conflictRY || conflictRZ || conflictTX || conflictTY || conflictTZ;
            ConflictingTransformsText.Visibility = anyConflict ? Visibility.Visible : Visibility.Collapsed;
            
             _isUpdatingUi = true;
             
             ScaleControlsPanel.Visibility = Visibility.Visible;
             RotationControlsPanel.Visibility = Visibility.Visible;
             
             UpdateField(ScaleX, conflictSX, refScale.X);
             UpdateField(ScaleY, conflictSY, refScale.Y);
             UpdateField(ScaleZ, conflictSZ, refScale.Z);
             UpdateField(RotX, conflictRX, refRot.X);
             UpdateField(RotY, conflictRY, refRot.Y);
             UpdateField(RotZ, conflictRZ, refRot.Z);
             UpdateField(TransX, conflictTX, refTrans.X);
             UpdateField(TransY, conflictTY, refTrans.Y);
             UpdateField(TransZ, conflictTZ, refTrans.Z);
             
             _isUpdatingUi = false;
        }
        
        private void UpdateField(TextBox box, bool conflict, float val)
        {
            box.IsEnabled = !conflict;
            box.Text = conflict ? "" : val.ToString("F5");
        }

        private void ClearTransformFields()
        {
            _isUpdatingUi = true;
            ConflictingTransformsText.Visibility = Visibility.Collapsed;
            ScaleControlsPanel.Visibility = Visibility.Visible;
            RotationControlsPanel.Visibility = Visibility.Visible;
            EnableAndSet(ScaleX, "1.00000");
            EnableAndSet(ScaleY, "1.00000");
            EnableAndSet(ScaleZ, "1.00000");
            EnableAndSet(RotX, "0.00000");
            EnableAndSet(RotY, "0.00000");
            EnableAndSet(RotZ, "0.00000");
            EnableAndSet(TransX, "0.00000");
            EnableAndSet(TransY, "0.00000");
            EnableAndSet(TransZ, "0.00000");
            _isUpdatingUi = false;
        }
        
        private void EnableAndSet(TextBox box, string text)
        {
            box.IsEnabled = true;
            box.Text = text;
        }

        private void ModelBinSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // If user manually changes the Model selector, exit multi-select mode
            if (!_isUpdatingUi && _isMultiSelectActive)
            {
                ClearMultiSelection();
            }

            if (ModelBinSelector.SelectedItem is ModelBinNode mb)
            {
                var scopes = new List<MeshScopeItem>();
                
                bool allChecked = mb.Children.All(c => c.IsChecked == true);
                scopes.Add(new MeshScopeItem 
                { 
                    Name = "All Meshes", 
                    Node = null, 
                    IsChecked = allChecked ? true : null
                });
                
                var materials = mb.Children.OfType<MeshNode>()
                                           .Select(m => m.GeometryData?.MaterialName)
                                           .Where(n => !string.IsNullOrEmpty(n))
                                           .Distinct()
                                           .OrderBy(n => n);
                                           
                foreach (var mat in materials)
                {
                    var matMeshes = mb.Children.OfType<MeshNode>().Where(m => m.GeometryData?.MaterialName == mat);
                    bool isMatVisible = matMeshes.Any(m => m.IsChecked == true);

                    scopes.Add(new MeshScopeItem 
                    { 
                        Name = $"Material: {mat}", 
                        MaterialGroup = mat,
                        IsChecked = isMatVisible
                    });
                }
                
                foreach (var child in mb.Children)
                {
                    if (child is MeshNode mn)
                    {
                        var item = new MeshScopeItem { Name = mn.Name, Node = mn, IsChecked = mn.IsChecked };
                        scopes.Add(item);
                    }
                }
                
                MeshSelector.ItemsSource = scopes;
                if (scopes.Count > 0)
                {
                     MeshSelector.SelectedIndex = 0;
                }
                else 
                {
                     MeshSelector.SelectedIndex = -1;
                     UpdateHighlightFromUI();
                }
                UpdateBoneUI();

                // Populate damage morph targets section
                var damageNodes = mb.Children.OfType<DamageMeshNode>().ToList();
                if (damageNodes.Count > 0)
                {
                    DamageMeshSelector.ItemsSource = damageNodes;
                    if (DamageMeshSelector.Items.Count > 0) DamageMeshSelector.SelectedIndex = 0;

                    var morphBlob = mb.Bundle?.Blobs.OfType<MorphBlob>().FirstOrDefault();
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"Morph buffers: {damageNodes.Count}");
                    if (morphBlob != null && morphBlob.Strings.Count > 0)
                        sb.Append($"  |  Targets: {string.Join(", ", morphBlob.Strings)}");
                    MorphInfoText.Text = sb.ToString();
                    DamageModelExpander.Visibility = Visibility.Visible;
                }
                else
                {
                    DamageMeshSelector.ItemsSource = null;
                    MorphInfoText.Text = string.Empty;
                    DamageModelExpander.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                MeshSelector.ItemsSource = null;
                ClearTransformFields();
                UpdateHighlight((IEnumerable<MeshNode>?)null);
                DamageMeshSelector.ItemsSource = null;
                MorphInfoText.Text = string.Empty;
                DamageModelExpander.Visibility = Visibility.Collapsed;
            }
        }

        private void MeshSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Normal model mesh case
            UpdateTransformUI();
            UpdateHighlightFromUI();
            UpdateBoneUI();
            RefreshMeshMaterialSection();
        }

        private void UpdateHighlightFromUI()
        {
            if (ModelBinSelector.SelectedItem is not ModelBinNode modelBin) 
            {
                UpdateHighlight((IEnumerable<MeshNode>?)null);
                return;
            }

            var meshScope = MeshSelector.SelectedItem as MeshScopeItem;
            IEnumerable<MeshNode>? targets = null;

            if (meshScope != null)
            {
                if (meshScope.Node != null)
                {
                    targets = new[] { meshScope.Node };
                }
                else if (!string.IsNullOrEmpty(meshScope.MaterialGroup))
                {
                    targets = modelBin.Children.OfType<MeshNode>()
                                             .Where(m => m.GeometryData?.MaterialName == meshScope.MaterialGroup);
                }
                else
                {
                    targets = modelBin.Children.OfType<MeshNode>();
                }
            }
            else
            {
                targets = modelBin.Children.OfType<MeshNode>();
            }

            UpdateHighlight(targets);
        }

        private void ApplyLODFilterRecursive(IViewerNode node, bool l0, bool l1, bool l2, bool l3, bool l4, bool l5, bool shadows, bool showDamage = false)
        {
            if (node is MeshNode mesh)
            {
                bool isVisible = false;

                if (mesh.IsShadow)
                {
                    isVisible = shadows;
                }
                else
                {
                    var flags = mesh.GeometryData?.SourceMesh?.LODFlags ?? 0;

                    if (flags != 0)
                    {
                        if (l0 && (flags & 1) != 0) isVisible = true;
                        if (l1 && (flags & 2) != 0) isVisible = true;
                        if (l2 && (flags & 4) != 0) isVisible = true;
                        if (l3 && (flags & 8) != 0) isVisible = true;
                        if (l4 && (flags & 16) != 0) isVisible = true;
                        if (l5 && (flags & 32) != 0) isVisible = true;
                    }
                    else
                    {
                        string name = mesh.Name ?? "";
                        if (name.Contains("LOD0") && l0) isVisible = true;
                        else if (name.Contains("LOD1") && l1) isVisible = true;
                        else if (name.Contains("LOD2") && l2) isVisible = true;
                        else if (name.Contains("LOD3") && l3) isVisible = true;
                        else if (name.Contains("LOD4") && l4) isVisible = true;
                        else if (name.Contains("LOD5") && l5) isVisible = true;

                        if (!isVisible && l0 && !name.Contains("LOD", StringComparison.OrdinalIgnoreCase))
                        {
                            isVisible = true;
                        }
                    }
                }

                if (mesh.Name.Contains("Proxy", StringComparison.OrdinalIgnoreCase)) isVisible = false;

                mesh.IsChecked = isVisible;
            }
            else if (node is DamageMeshNode dmgMesh)
            {
                if (dmgMesh.IsShadow)
                {
                    dmgMesh.IsChecked = shadows;
                }
                else
                {
                    // Non-shadow damage nodes follow the same LOD rules as their sibling mesh, gated by showDamage
                    bool isVisible = false;
                    if (showDamage)
                    {
                        var flags = dmgMesh.GeometryData?.SourceMesh?.LODFlags ?? 0;
                        string dmgName = dmgMesh.GeometryData?.Name ?? dmgMesh.Name ?? "";
                        if (flags != 0)
                        {
                            if (l0 && (flags & 1) != 0) isVisible = true;
                            if (l1 && (flags & 2) != 0) isVisible = true;
                            if (l2 && (flags & 4) != 0) isVisible = true;
                            if (l3 && (flags & 8) != 0) isVisible = true;
                            if (l4 && (flags & 16) != 0) isVisible = true;
                            if (l5 && (flags & 32) != 0) isVisible = true;
                        }
                        else
                        {
                            if (dmgName.Contains("LOD0") && l0) isVisible = true;
                            else if (dmgName.Contains("LOD1") && l1) isVisible = true;
                            else if (dmgName.Contains("LOD2") && l2) isVisible = true;
                            else if (dmgName.Contains("LOD3") && l3) isVisible = true;
                            else if (dmgName.Contains("LOD4") && l4) isVisible = true;
                            else if (dmgName.Contains("LOD5") && l5) isVisible = true;
                            if (!isVisible && l0 && !dmgName.Contains("LOD", StringComparison.OrdinalIgnoreCase))
                                isVisible = true;
                        }
                        if (dmgName.Contains("Proxy", StringComparison.OrdinalIgnoreCase))
                            isVisible = false;
                    }
                    dmgMesh.IsChecked = isVisible;
                }
            }

            foreach (var child in node.Children)
            {
                ApplyLODFilterRecursive(child, l0, l1, l2, l3, l4, l5, shadows, showDamage);
            }
        }
        
        private void RefreshModelList()
        {
            var allItems = new List<object>();
            foreach (var root in ViewModel.Roots)
            {
                CollectSelectableItems(root, allItems);
            }
            
            var currentSelection = ModelBinSelector.SelectedItem;
            
            ModelBinSelector.ItemsSource = allItems;
            
            if (currentSelection != null && allItems.Contains(currentSelection))
            {
                ModelBinSelector.SelectedItem = currentSelection;
            }
            else if (allItems.Count > 0)
            {
                ModelBinSelector.SelectedIndex = 0;
            }
            else
            {
                ModelBinSelector.SelectedItem = null;
                MeshSelector.ItemsSource = null;
                ClearTransformFields();
            }

            UpdateLocatorExpanderVisibility();
            UpdateLightTransformExpanderVisibility();
            UpdateAnimationExpanderVisibility();

            // Show Model Properties expander only when at least one .modelbin is loaded
            bool hasModelBin = allItems.OfType<ModelBinNode>().Any();
            ModelPropertiesExpander.Visibility = hasModelBin ? Visibility.Visible : Visibility.Collapsed;

            // Auto-link a _skeleton.modelbin if one is present in the loaded files
            TryAutoLinkSkeletonModelBin();
            UpdateSkeletonMbLinkText();
        }
        
        private void CollectSelectableItems(IViewerNode node, List<object> items)
        {
            if (node is ModelBinNode)
            {
                items.Add(node);
            }
            
            foreach (var child in node.Children)
            {
                CollectSelectableItems(child, items);
            }
        }

        private void CollectModelBinsAndLights(IViewerNode node, List<IViewerNode> items)
        {
            if (node is ModelBinNode || node is LightsBinNode)
            {
                items.Add(node);
            }
            
            foreach (var child in node.Children)
            {
                CollectModelBinsAndLights(child, items);
            }
        }

        private void CollectModelBins(IViewerNode node, List<ModelBinNode> bins)
        {
            if (node is ModelBinNode bin)
            {
                bins.Add(bin);
            }
            
            foreach (var child in node.Children)
            {
                CollectModelBins(child, bins);
            }
        }

        private void ModelBinVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is IViewerNode node)
            {
                node.IsChecked = !(node.IsChecked ?? false);
            }
        }

        private void MeshVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is MeshScopeItem item)
            {
                bool newState = !(item.IsChecked ?? false);
                item.IsChecked = newState;

                if (item.Node != null)
                {
                    item.Node.IsChecked = newState;
                }
                else
                {
                   if (ModelBinSelector.SelectedItem is ModelBinNode modelBin)
                   {
                       IEnumerable<MeshNode> targets;
                       if (!string.IsNullOrEmpty(item.MaterialGroup))
                       {
                            targets = modelBin.Children.OfType<MeshNode>()
                                                    .Where(m => m.GeometryData?.MaterialName == item.MaterialGroup);
                       }
                       else
                       {
                            targets = modelBin.Children.OfType<MeshNode>();
                       }
                       
                       foreach(var t in targets) t.IsChecked = newState;
                   }
                   else if (ModelBinSelector.SelectedItem is PhysicsDefinitionNode physicsNode)
                   {
                       foreach(var t in physicsNode.Children.OfType<MeshNode>()) t.IsChecked = newState;
                   }
                   else if (ModelBinSelector.SelectedItem is LocatorsXmlNode locatorsNode)
                   {
                       if (item.Name == "All Locators")
                       {
                           foreach(var t in locatorsNode.Children.OfType<LocatorNode>()) t.IsChecked = newState;
                       }
                       else
                       {
                           var target = locatorsNode.Children.OfType<LocatorNode>().FirstOrDefault(l => l.Name == item.Name);
                           if (target != null) target.IsChecked = newState;
                       }
                   }
                   else if (ModelBinSelector.SelectedItem is LightsBinNode lightsBin)
                   {
                       if (item.Name == "All Lights")
                       {
                           foreach(var t in lightsBin.Children.OfType<LightGroupNode>()) t.IsChecked = newState;
                       }
                       else
                       {
                           var target = lightsBin.Children.OfType<LightGroupNode>().FirstOrDefault(l => l.Name == item.Name);
                           if (target != null) target.IsChecked = newState;
                       }
                   }
                   else if (ModelBinSelector.SelectedItem is LightGroupNode lightGroup)
                   {
                       if (!string.IsNullOrEmpty(item.MaterialGroup) && item.MaterialGroup.StartsWith("Row"))
                       {
                           if (int.TryParse(item.MaterialGroup.Substring(3), out int idx))
                           {
                               if (idx >= 0 && idx < lightGroup.Children.Count && lightGroup.Children[idx] is LightRowNode rowNode)
                               {
                                   rowNode.IsChecked = newState;
                               }
                           }
                       }
                   }
                }
            }
        }

        private void DamageMeshVisibility_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DamageMeshNode dmgNode)
                dmgNode.IsChecked = !(dmgNode.IsChecked ?? false);
        }

        private void DamageMeshSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void GridToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_gridLines != null && sender is CheckBox toggle)
            {
                _gridLines.IsRendering = toggle.IsChecked ?? false;
            }
        }

        private void SingleColorToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox toggle)
            {
                UpdateMeshColors(toggle.IsChecked ?? false);
            }
        }

        private void MeshColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            _singleColor = new SDX.Color4(args.NewColor.R / 255f, args.NewColor.G / 255f, args.NewColor.B / 255f, 1.0f);
            UpdateMeshColors(SingleColorToggle?.IsChecked ?? false);
        }

        private void SceneOpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            _sceneOpacity = (float)e.NewValue;
            UpdateMeshColors(SingleColorToggle?.IsChecked ?? false);
        }

        private void SceneLightToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_sceneLight != null && sender is CheckBox toggle)
                _sceneLight.IsRendering = toggle.IsChecked ?? true;
        }

        private void FullbrightToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox toggle) return;
            bool fullbright = toggle.IsChecked ?? false;

            if (fullbright)
            {
                // Disable the normal scene lights and enable 6-axis directional lights
                if (_sceneLight != null)   _sceneLight.IsRendering   = false;
                if (_fillLight != null)    _fillLight.IsRendering     = false;
                if (_backLight != null)    _backLight.IsRendering     = false;
                if (_ambientLight != null) _ambientLight.IsRendering  = false;
                if (_fullbrightLights != null)
                    foreach (var fl in _fullbrightLights) fl.IsRendering = true;
            }
            else
            {
                // Restore the original scene lights and turn the 6-axis lights off.
                if (_sceneLight != null)   { _sceneLight.Color = Microsoft.UI.Colors.White;              _sceneLight.IsRendering   = SceneLightToggle?.IsChecked ?? true; }
                if (_fillLight != null)    { _fillLight.Color  = Color.FromArgb(255, 128, 128, 128);     _fillLight.IsRendering    = true; }
                if (_backLight != null)    { _backLight.Color  = Color.FromArgb(255, 64, 64, 64);        _backLight.IsRendering    = true; }
                if (_ambientLight != null) { _ambientLight.Color = Color.FromArgb(255, 80, 80, 80);      _ambientLight.IsRendering = true; }
                if (_fullbrightLights != null)
                    foreach (var fl in _fullbrightLights) fl.IsRendering = false;
            }
        }

        private void LightAngleSlider_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_sceneLight == null) return;

            double hDeg = LightHorizontalSlider?.Value ?? 315;
            double vDeg = LightVerticalSlider?.Value ?? 45;

            if (LightHorizontalLabel != null) LightHorizontalLabel.Text = $"{(int)hDeg}°";
            if (LightVerticalLabel != null) LightVerticalLabel.Text = $"{(int)vDeg}°";

            // Convert spherical angles to a direction vector
            double hRad = hDeg * Math.PI / 180.0;
            double vRad = vDeg * Math.PI / 180.0;
            float x = (float)(Math.Cos(vRad) * Math.Sin(hRad));
            float y = (float)(-Math.Sin(vRad));
            float z = (float)(Math.Cos(vRad) * Math.Cos(hRad));
            _sceneLight.Direction = new SDX.Vector3(x, y, z);
        }

        private void LOD_Click(object sender, RoutedEventArgs e)
        {
            if (sender == ShadowsItem)
            {
                // Only toggle shadow meshes; don't re-evaluate regular meshes.
                bool shadows = ShadowsItem.IsChecked;
                foreach (var root in ViewModel.Roots)
                    ApplyShadowsOnlyRecursive(root, shadows);
            }
            else
            {
                UpdateLODVisibility();
            }
        }

        private void ApplyShadowsOnlyRecursive(IViewerNode node, bool shadows)
        {
            if (node is MeshNode mesh && mesh.IsShadow)
                mesh.IsChecked = shadows;
            else if (node is DamageMeshNode dmg && dmg.IsShadow)
                dmg.IsChecked = shadows;
            foreach (var child in node.Children)
                ApplyShadowsOnlyRecursive(child, shadows);
        }

        private void ViewType_Click(object sender, RoutedEventArgs e)
        {
            UpdateViewTypeVisibility();
        }

        private void UpdateViewTypeVisibility()
        {
            bool showLights = LightsItem.IsChecked;
            bool showLocators = LocatorsItem.IsChecked;
            bool showPhysics = PhysicsItem.IsChecked;
            bool showDamage = DamageModelItem.IsChecked;

            foreach (var root in ViewModel.Roots)
            {
                ApplyViewTypeFilterRecursive(root, showLights, showLocators, showPhysics, showDamage);
            }
        }

        // Syncs the View dropdown toggles (LightsItem, LocatorsItem, PhysicsItem) to reflect
        // the actual visibility state of nodes in the sidebar tree, without triggering callbacks.
        private void SyncViewDropdownItems()
        {
            bool anyLight = false, anyLocator = false, anyPhysics = false;
            foreach (var root in ViewModel.Roots)
                CollectViewTypeVisible(root, ref anyLight, ref anyLocator, ref anyPhysics);
            LightsItem.IsChecked = anyLight;
            LocatorsItem.IsChecked = anyLocator;
            PhysicsItem.IsChecked = anyPhysics;
        }

        private void CollectViewTypeVisible(IViewerNode node, ref bool anyLight, ref bool anyLocator, ref bool anyPhysics)
        {
            if (node is LightGroupNode lg && lg.IsChecked != false) anyLight = true;
            else if (node is LocatorNode ln && ln.IsChecked == true) anyLocator = true;
            else if (node is AvPinNode apn && apn.IsChecked == true) anyLocator = true;
            else if (node is PhysicsDefinitionNode pdn && pdn.Children.Any(c => c.IsChecked == true)) anyPhysics = true;
            foreach (var child in node.Children)
                CollectViewTypeVisible(child, ref anyLight, ref anyLocator, ref anyPhysics);
        }

        private void ApplyViewTypeFilterRecursive(IViewerNode node, bool showLights, bool showLocators, bool showPhysics, bool showDamage = false)
        {
            if (node is LightsBinNode lightsBin)
            {
                lightsBin.IsChecked = showLights;
            }
            else if (node is LocatorsXmlNode locatorsNode)
            {
                locatorsNode.IsChecked = showLocators;
            }
            else if (node is AvPinsFileNode avPinsNode)
            {
                avPinsNode.IsChecked = showLocators;
            }
            else if (node is PhysicsDefinitionNode physicsNode)
            {
                physicsNode.IsChecked = showPhysics;
            }
            else if (node is DamageMeshNode dmgNode)
            {
                // Shadow-linked damage nodes are controlled by the shadows toggle, not the damage toggle
                if (!dmgNode.IsShadow)
                    dmgNode.IsChecked = showDamage;
            }

            foreach (var child in node.Children)
            {
                ApplyViewTypeFilterRecursive(child, showLights, showLocators, showPhysics, showDamage);
            }
        }

        private void DamageModel_Click(object sender, RoutedEventArgs e)
        {
            UpdateLODVisibility();
        }

        private void UpdateLODVisibility()
        {
            bool l0 = Lod0Item.IsChecked;
            bool l1 = Lod1Item.IsChecked;
            bool l2 = Lod2Item.IsChecked;
            bool l3 = Lod3Item.IsChecked;
            bool l4 = Lod4Item.IsChecked;
            bool l5 = Lod5Item.IsChecked;
            bool shadows = ShadowsItem.IsChecked;
            bool showDamage = DamageModelItem.IsChecked;

            foreach (var root in ViewModel.Roots)
            {
                ApplyLODFilterRecursive(root, l0, l1, l2, l3, l4, l5, shadows, showDamage);
            }
        }

        private void ShowLocatorMatrixUI(LocatorNode locNode)
        {
            // Only update the dropdown selection and matrix fields.
            // Expander visibility is controlled by UpdateLocatorExpanderVisibility.
            var locatorsRoot = locNode.Parent as LocatorsXmlNode;
            if (locatorsRoot != null)
            {
                _isUpdatingLocatorUi = true;
                LocatorPartSelector.ItemsSource = locatorsRoot.Children.OfType<LocatorNode>().ToList();
                LocatorPartSelector.SelectedItem = locNode;
                _isUpdatingLocatorUi = false;
            }

            PopulateMatrixFields(locNode.LocatorEntry.SceneTransform);
        }

        private void HideLocatorMatrixUI()
        {
            // Only clear the matrix fields.
            // Expander visibility is controlled by UpdateLocatorExpanderVisibility.
        }

        // Shows the locator panel when any LocatorsXmlNode is loaded; hides it when none remain.
        private void UpdateLocatorExpanderVisibility()
        {
            var locatorsNodes = new List<LocatorsXmlNode>();
            foreach (var root in ViewModel.Roots)
            {
                CollectLocatorsNodes(root, locatorsNodes);
            }

            bool hasLocators = locatorsNodes.Any();

            if (hasLocators)
            {
                LocatorMatrixExpander.Visibility = Visibility.Visible;

                // Pre-populate the dropdown with the first available locators file
                // if nothing is selected yet.
                if (LocatorPartSelector.ItemsSource == null ||
                    LocatorPartSelector.Items.Count == 0)
                {
                    var firstRoot = locatorsNodes.First();
                    _isUpdatingLocatorUi = true;
                    LocatorPartSelector.ItemsSource = firstRoot.Children.OfType<LocatorNode>().ToList();
                    LocatorPartSelector.SelectedIndex = 0;
                    _isUpdatingLocatorUi = false;

                    if (LocatorPartSelector.SelectedItem is LocatorNode first)
                        PopulateMatrixFields(first.LocatorEntry.SceneTransform);
                }
            }
            else
            {
                LocatorMatrixExpander.Visibility = Visibility.Collapsed;
                LocatorPartSelector.ItemsSource = null;
            }

            UpdateAvPinsExpanderVisibility();
        }

        private void CollectLocatorsNodes(IViewerNode node, List<LocatorsXmlNode> list)
        {
            if (node is LocatorsXmlNode locNode) list.Add(locNode);
            foreach (var child in node.Children) CollectLocatorsNodes(child, list);
        }

        // AvPins UI

        private bool _isUpdatingAvPinsUi = false;

        private void UpdateAvPinsExpanderVisibility()
        {
            var avPinsNodes = new List<AvPinsFileNode>();
            foreach (var root in ViewModel.Roots)
                CollectAvPinsNodes(root, avPinsNodes);

            bool hasAvPins = avPinsNodes.Any();
            AvPinsExpander.Visibility = hasAvPins ? Visibility.Visible : Visibility.Collapsed;

            if (hasAvPins)
            {
                var firstFile = avPinsNodes.First();
                _isUpdatingAvPinsUi = true;
                AvPinSelector.ItemsSource = firstFile.Children.OfType<AvPinNode>().ToList();
                if (AvPinSelector.Items.Count > 0)
                    AvPinSelector.SelectedIndex = 0;
                _isUpdatingAvPinsUi = false;

                if (AvPinSelector.SelectedItem is AvPinNode first)
                    PopulateAvPinFields(first.PoiData?.Visibility);
            }
            else
            {
                AvPinSelector.ItemsSource = null;
            }
        }

        private void CollectAvPinsNodes(IViewerNode node, List<AvPinsFileNode> list)
        {
            if (node is AvPinsFileNode apf) list.Add(apf);
            foreach (var child in node.Children) CollectAvPinsNodes(child, list);
        }

        internal void ShowAvPinUI(AvPinNode pinNode)
        {
            var fileRoot = pinNode.Parent as AvPinsFileNode;
            if (fileRoot != null)
            {
                _isUpdatingAvPinsUi = true;
                AvPinSelector.ItemsSource = fileRoot.Children.OfType<AvPinNode>().ToList();
                AvPinSelector.SelectedItem = pinNode;
                _isUpdatingAvPinsUi = false;
            }
            PopulateAvPinFields(pinNode.PoiData?.Visibility);
        }

        private void PopulateAvPinFields(ForzaTechStudio.Models.PoiVisibility? v)
        {
            _isUpdatingAvPinsUi = true;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (v == null)
            {
                AvPinPosX.Text = "0.000000"; AvPinPosY.Text = "0.000000"; AvPinPosZ.Text = "0.000000";
                AvPinAxisYaw.Text = "0.000000"; AvPinAxisPitch.Text = "0.000000";
                AvPinApexYaw.Text = "0.000000"; AvPinApexPitch.Text = "0.000000";
                AvPinActiveApexYaw.Text = "0.000000"; AvPinActiveApexPitch.Text = "0.000000";
                AvPinMidApexYaw.Text = "0.000000"; AvPinMidApexPitch.Text = "0.000000";
                AvPinNearRadius.Text = "0.000000"; AvPinMidRadius.Text = "0.000000"; AvPinFarRadius.Text = "0.000000";
            }
            else
            {
                AvPinPosX.Text = v.PosX.ToString("F6", ci);
                AvPinPosY.Text = v.PosY.ToString("F6", ci);
                AvPinPosZ.Text = v.PosZ.ToString("F6", ci);
                AvPinAxisYaw.Text   = v.AxisYaw.ToString("F6", ci);
                AvPinAxisPitch.Text = v.AxisPitch.ToString("F6", ci);
                AvPinApexYaw.Text   = v.ApexYaw.ToString("F6", ci);
                AvPinApexPitch.Text = v.ApexPitch.ToString("F6", ci);
                AvPinActiveApexYaw.Text   = v.ActiveApexYaw.ToString("F6", ci);
                AvPinActiveApexPitch.Text = v.ActiveApexPitch.ToString("F6", ci);
                AvPinMidApexYaw.Text   = v.MidApexYaw.ToString("F6", ci);
                AvPinMidApexPitch.Text = v.MidApexPitch.ToString("F6", ci);
                AvPinNearRadius.Text = v.NearRadius.ToString("F6", ci);
                AvPinMidRadius.Text  = v.MidRadius.ToString("F6", ci);
                AvPinFarRadius.Text  = v.FarRadius.ToString("F6", ci);
            }
            _isUpdatingAvPinsUi = false;
        }

        private void AvPinSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingAvPinsUi) return;
            if (AvPinSelector.SelectedItem is AvPinNode pinNode)
            {
                ViewModel.SelectedNode = pinNode;
                if (_treeNodeMap.TryGetValue(pinNode, out var treeViewNode))
                    FileTree.SelectedItem = treeViewNode;
            }
        }

        private void AvPinField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingAvPinsUi) return;

            AvPinNode? pinNode = ViewModel.SelectedNode as AvPinNode;
            if (pinNode == null)
                pinNode = AvPinSelector.SelectedItem as AvPinNode;
            if (pinNode?.PoiData?.Visibility == null) return;

            double Parse(TextBox box, double fallback)
            {
                var text = box?.Text ?? "";
                return double.TryParse(text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double v) ? v : fallback;
            }

            var vis = pinNode.PoiData.Visibility;
            vis.PosX  = Parse(AvPinPosX,  vis.PosX);
            vis.PosY  = Parse(AvPinPosY,  vis.PosY);
            vis.PosZ  = Parse(AvPinPosZ,  vis.PosZ);
            vis.AxisYaw   = Parse(AvPinAxisYaw,   vis.AxisYaw);
            vis.AxisPitch = Parse(AvPinAxisPitch, vis.AxisPitch);
            vis.ApexYaw   = Parse(AvPinApexYaw,   vis.ApexYaw);
            vis.ApexPitch = Parse(AvPinApexPitch, vis.ApexPitch);
            vis.ActiveApexYaw   = Parse(AvPinActiveApexYaw,   vis.ActiveApexYaw);
            vis.ActiveApexPitch = Parse(AvPinActiveApexPitch, vis.ActiveApexPitch);
            vis.MidApexYaw   = Parse(AvPinMidApexYaw,   vis.MidApexYaw);
            vis.MidApexPitch = Parse(AvPinMidApexPitch, vis.MidApexPitch);
            vis.NearRadius = Parse(AvPinNearRadius, vis.NearRadius);
            vis.MidRadius  = Parse(AvPinMidRadius,  vis.MidRadius);
            vis.FarRadius  = Parse(AvPinFarRadius,  vis.FarRadius);

            if (pinNode.Parent is AvPinsFileNode fileNode)
                fileNode.IsDirty = true;

            RefreshAvPinCone(pinNode);
        }

        private async void SaveAvPins_Click(object sender, RoutedEventArgs e)
        {
            AvPinsFileNode? fileNode = null;
            if (ViewModel.SelectedNode is AvPinNode selPin)
                fileNode = selPin.Parent as AvPinsFileNode;
            else if (AvPinSelector.SelectedItem is AvPinNode comboPin)
                fileNode = comboPin.Parent as AvPinsFileNode;

            if (fileNode?.AvPinsData == null)
            {
                await ShowError("No .avpins file is currently active.");
                return;
            }

            try
            {
                IsLoading = true;
                LoadingStatus = "Saving .avpins...";
                var xmlBytes = System.Text.Encoding.UTF8.GetBytes(AvPinsParser.Serialize(fileNode.AvPinsData));

                if (!string.IsNullOrEmpty(fileNode.SourceZipPath) && !string.IsNullOrEmpty(fileNode.ZipEntryName))
                {
                    // Save back into the ZIP archive
                    await System.Threading.Tasks.Task.Run(() =>
                        ZipArchiveHelper.ReplaceEntry(fileNode.SourceZipPath, fileNode.ZipEntryName, xmlBytes));
                    fileNode.IsDirty = false;

                    var dlg = new ContentDialog
                    {
                        Title   = "Success",
                        Content = $"Saved {fileNode.Name} into {System.IO.Path.GetFileName(fileNode.SourceZipPath)}",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await dlg.ShowAsync();
                }
                else if (!string.IsNullOrEmpty(fileNode.FilePath) && System.IO.File.Exists(fileNode.FilePath))
                {
                    await System.Threading.Tasks.Task.Run(() =>
                        System.IO.File.WriteAllBytes(fileNode.FilePath, xmlBytes));
                    fileNode.IsDirty = false;

                    var dlg = new ContentDialog
                    {
                        Title   = "Success",
                        Content = $"Saved to {System.IO.Path.GetFileName(fileNode.FilePath)}",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await dlg.ShowAsync();
                }
                else
                {
                    var savePicker = new Windows.Storage.Pickers.FileSavePicker();
                    var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                    WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);
                    savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                    savePicker.SuggestedFileName = fileNode.Name ?? "file.avpins";
                    savePicker.FileTypeChoices.Add("Autovista POI File", new[] { ".avpins" });
                    var outFile = await savePicker.PickSaveFileAsync();
                    if (outFile != null)
                    {
                        await System.Threading.Tasks.Task.Run(() =>
                            System.IO.File.WriteAllBytes(outFile.Path, xmlBytes));
                        fileNode.FilePath = outFile.Path;
                        fileNode.IsDirty  = false;
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowError($"Failed to save .avpins.\n\nError: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadingStatus = "";
            }
        }

        private void PopulateMatrixFields(Matrix4x4 m)
        {
            _isUpdatingLocatorUi = true;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            M11.Text = m.M11.ToString("F6", ci); M12.Text = m.M12.ToString("F6", ci);
            M13.Text = m.M13.ToString("F6", ci); M14.Text = m.M14.ToString("F6", ci);
            M21.Text = m.M21.ToString("F6", ci); M22.Text = m.M22.ToString("F6", ci);
            M23.Text = m.M23.ToString("F6", ci); M24.Text = m.M24.ToString("F6", ci);
            M31.Text = m.M31.ToString("F6", ci); M32.Text = m.M32.ToString("F6", ci);
            M33.Text = m.M33.ToString("F6", ci); M34.Text = m.M34.ToString("F6", ci);
            M41.Text = m.M41.ToString("F6", ci); M42.Text = m.M42.ToString("F6", ci);
            M43.Text = m.M43.ToString("F6", ci); M44.Text = m.M44.ToString("F6", ci);
            _isUpdatingLocatorUi = false;
        }

        private void LocatorPartSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingLocatorUi) return;
            if (LocatorPartSelector.SelectedItem is LocatorNode locNode)
            {
                ViewModel.SelectedNode = locNode;
                if (_treeNodeMap.TryGetValue(locNode, out var treeViewNode))
                {
                    FileTree.SelectedItem = treeViewNode;
                }
            }
        }

        private void LocatorMatrix_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingLocatorUi) return;

            var locNode = ViewModel.SelectedNode as LocatorNode;
            if (locNode == null)
            {
                if (LocatorPartSelector.SelectedItem is LocatorNode sel) locNode = sel;
            }
            if (locNode == null) return;

            // Capture old transform for undo
            var oldTransform = locNode.LocatorEntry.SceneTransform;

            float Parse(TextBox box, float fallback) =>
                float.TryParse(box.Text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float v) ? v : fallback;

            var cur = locNode.LocatorEntry.SceneTransform;
            var updated = new Matrix4x4(
                Parse(M11, cur.M11), Parse(M12, cur.M12), Parse(M13, cur.M13), Parse(M14, cur.M14),
                Parse(M21, cur.M21), Parse(M22, cur.M22), Parse(M23, cur.M23), Parse(M24, cur.M24),
                Parse(M31, cur.M31), Parse(M32, cur.M32), Parse(M33, cur.M33), Parse(M34, cur.M34),
                Parse(M41, cur.M41), Parse(M42, cur.M42), Parse(M43, cur.M43), Parse(M44, cur.M44)
            );

            locNode.LocatorEntry.SceneTransform = updated;

            if (locNode.Parent is LocatorsXmlNode xmlRoot)
                xmlRoot.IsDirty = true;

            // Push undo
            if (oldTransform != updated)
            {
                var undoAction = new LocatorTransformAction("Locator Transform Edit", locNode, oldTransform);
                undoAction.NewTransform = updated;
                PushUndo(undoAction);
            }

            RefreshLocatorCone(locNode);
        }

        private async void SaveLocatorsXml_Click(object sender, RoutedEventArgs e)
        {
            LocatorsXmlNode? xmlRoot = null;
            if (ViewModel.SelectedNode is LocatorNode locNode)
                xmlRoot = locNode.Parent as LocatorsXmlNode;
            else if (LocatorPartSelector.SelectedItem is LocatorNode selNode)
                xmlRoot = selNode.Parent as LocatorsXmlNode;

            if (xmlRoot == null)
            {
                await ShowError("No locators.xml file is currently active.");
                return;
            }

            try
            {
                IsLoading = true;
                LoadingStatus = "Saving locators.xml...";

                var parser = new LocatorsXmlParser();

                if (!string.IsNullOrEmpty(xmlRoot.FilePath) && System.IO.File.Exists(xmlRoot.FilePath))
                {
                    await System.Threading.Tasks.Task.Run(() => parser.Save(xmlRoot.LocatorsData));
                    xmlRoot.IsDirty = false;

                    var dlg = new ContentDialog
                    {
                        Title = "Success",
                        Content = $"Saved to {System.IO.Path.GetFileName(xmlRoot.FilePath)}",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await dlg.ShowAsync();
                }
                else
                {
                    var savePicker = new Windows.Storage.Pickers.FileSavePicker();
                    var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                    WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);
                    savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                    savePicker.SuggestedFileName = xmlRoot.Name ?? "locators.xml";
                    savePicker.FileTypeChoices.Add("XML File", new[] { ".xml" });
                    var outFile = await savePicker.PickSaveFileAsync();
                    if (outFile != null)
                    {
                        await System.Threading.Tasks.Task.Run(() => parser.Save(xmlRoot.LocatorsData, outFile.Path));
                        xmlRoot.FilePath = outFile.Path;
                        xmlRoot.IsDirty = false;
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowError($"Failed to save locators.xml.\n\nError: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                LoadingStatus = "";
            }
        }

        private void UpdateTransformUIForMultiSelect()
        {
            _isUpdatingUi = true;

            // Deselect model/mesh dropdowns without replacing their ItemsSource
            // so the real list is preserved for when multi-select is cleared.
            ModelBinSelector.SelectedIndex = -1;
            MeshSelector.SelectedIndex = -1;

            // Show all fields as 0.0 (delta mode)
            ConflictingTransformsText.Visibility = Visibility.Collapsed;
            ScaleControlsPanel.Visibility = Visibility.Visible;
            RotationControlsPanel.Visibility = Visibility.Visible;
            EnableAndSet(ScaleX, "0.00000");
            EnableAndSet(ScaleY, "0.00000");
            EnableAndSet(ScaleZ, "0.00000");
            EnableAndSet(RotX, "0.00000");
            EnableAndSet(RotY, "0.00000");
            EnableAndSet(RotZ, "0.00000");
            EnableAndSet(TransX, "0.00000");
            EnableAndSet(TransY, "0.00000");
            EnableAndSet(TransZ, "0.00000");

            _isUpdatingUi = false;

            // Restore the multi-select highlight that was cleared by the SelectionChanged cascade
            UpdateHighlight(_multiSelectedMeshes);

            // Update header text to show multi-select count
            MultiSelectText.Text = $"x{_multiSelectedMeshes.Count} selected";
            MultiSelectText.Visibility = Visibility.Visible;
        }

        private void UpdateTransformUIForMultiLightSelect()
        {
            _isUpdatingUi = true;
            LtPosX.Text = "0.00000"; LtPosY.Text = "0.00000"; LtPosZ.Text = "0.00000"; LtPosW.Text = "0.00000";
            LtRotX.Text = "0.00000"; LtRotY.Text = "0.00000"; LtRotZ.Text = "0.00000"; LtRotW.Text = "0.00000";
            LtDmgPosX.Text = "0.00000"; LtDmgPosY.Text = "0.00000"; LtDmgPosZ.Text = "0.00000"; LtDmgPosW.Text = "0.00000";
            LtDmgRotX.Text = "0.00000"; LtDmgRotY.Text = "0.00000"; LtDmgRotZ.Text = "0.00000"; LtDmgRotW.Text = "0.00000";
            LightPartSelector.SelectedIndex = -1;
            LightMultiSelectText.Text = $"x{_multiSelectedLightGroups.Count} lights selected";
            LightMultiSelectText.Visibility = Visibility.Visible;
            _isUpdatingUi = false;
            UpdateHighlightForLightGroups(_multiSelectedLightGroups);
        }

        private void UpdateLightTransformExpanderVisibility()
        {
            var lightsBinNodes = new List<LightsBinNode>();
            foreach (var root in ViewModel.Roots)
                CollectLightsBinNodes(root, lightsBinNodes);

            bool hasLights = lightsBinNodes.Any();
            LightTransformExpander.Visibility = hasLights ? Visibility.Visible : Visibility.Collapsed;

            if (!hasLights)
            {
                LightPartSelector.ItemsSource = null;
                return;
            }

            var allGroups = lightsBinNodes.SelectMany(lb => lb.Children.OfType<LightGroupNode>()).ToList();
            _isUpdatingUi = true;
            LightPartSelector.ItemsSource = allGroups;
            if (!_isMultiLightSelectActive)
            {
                if (ViewModel.SelectedNode is LightGroupNode sel && allGroups.Contains(sel))
                    LightPartSelector.SelectedItem = sel;
                else if (allGroups.Count > 0)
                    LightPartSelector.SelectedIndex = 0;
            }
            _isUpdatingUi = false;

            if (!_isMultiLightSelectActive && LightPartSelector.SelectedItem is LightGroupNode cur)
                UpdateLightTransformUI(cur);
        }

        private void CollectLightsBinNodes(IViewerNode node, List<LightsBinNode> list)
        {
            if (node is LightsBinNode lb) list.Add(lb);
            foreach (var child in node.Children) CollectLightsBinNodes(child, list);
        }

        private void UpdateLightTransformUI(LightGroupNode group)
        {
            if (group == null) return;
            _isUpdatingUi = true;
            var g = group.GroupData;
            LtPosX.Text = g.Pos.X.ToString("F5");     LtPosY.Text = g.Pos.Y.ToString("F5");
            LtPosZ.Text = g.Pos.Z.ToString("F5");     LtPosW.Text = g.Pos.W.ToString("F5");
            LtRotX.Text = g.Rot.X.ToString("F5");     LtRotY.Text = g.Rot.Y.ToString("F5");
            LtRotZ.Text = g.Rot.Z.ToString("F5");     LtRotW.Text = g.Rot.W.ToString("F5");
            LtDmgPosX.Text = g.DamagePos.X.ToString("F5"); LtDmgPosY.Text = g.DamagePos.Y.ToString("F5");
            LtDmgPosZ.Text = g.DamagePos.Z.ToString("F5"); LtDmgPosW.Text = g.DamagePos.W.ToString("F5");
            LtDmgRotX.Text = g.DamageRot.X.ToString("F5"); LtDmgRotY.Text = g.DamageRot.Y.ToString("F5");
            LtDmgRotZ.Text = g.DamageRot.Z.ToString("F5"); LtDmgRotW.Text = g.DamageRot.W.ToString("F5");
            LightMultiSelectText.Visibility = Visibility.Collapsed;
            _isUpdatingUi = false;
        }

        private void LightPartSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            if (LightPartSelector.SelectedItem is LightGroupNode group)
            {
                if (_isMultiLightSelectActive) ClearMultiLightSelection();
                ViewModel.SelectedNode = group;
                if (_treeNodeMap.TryGetValue(group, out var tvNode))
                    FileTree.SelectedItem = tvNode;
                UpdateLightTransformUI(group);
                _currentLightHighlightTargets = new List<LightGroupNode> { group };
                UpdateHighlightForLightGroups(_currentLightHighlightTargets);
            }
        }

        private async void LightsSave_Click(object sender, RoutedEventArgs e)
        {
            LightsBinNode? lb = null;
            if (LightPartSelector.SelectedItem is LightGroupNode group)
                lb = group.Parent as LightsBinNode;

            if (lb == null)
            {
                await ShowError("No lights.bin file is currently active.");
                return;
            }

            await SaveLightsBin(lb);
        }

        private void HideSelected()
        {
            if (_isMultiLightSelectActive && _multiSelectedLightGroups.Count > 0)
            {
                foreach (var lg in _multiSelectedLightGroups.ToList())
                    lg.IsChecked = false;
                ClearMultiLightSelection();
                UpdateHighlightForLightGroups(new List<LightGroupNode>());
            }
            else if (_isMultiSelectActive && _multiSelectedMeshes.Count > 0)
            {
                foreach (var mesh in _multiSelectedMeshes.ToList())
                    mesh.IsChecked = false;
                ClearMultiSelection();
            }
            else if (ViewModel.SelectedNode != null)
            {
                var n = ViewModel.SelectedNode;
                ViewModel.SelectedNode = null!;
                n.IsChecked = false;
                UpdateHighlight((IEnumerable<MeshNode>?)null);
            }
        }

        private void IsolateSelected()
        {
            var keepMeshes = new List<MeshNode>();
            var keepLights = new List<LightGroupNode>();
            var keepDamage = new List<DamageMeshNode>();

            if (_isMultiSelectActive)
                keepMeshes.AddRange(_multiSelectedMeshes);
            else if (ViewModel.SelectedNode is MeshNode sm)
                keepMeshes.Add(sm);
            else if (ViewModel.SelectedNode is ModelBinNode mb)
                CollectMeshNodes(mb, keepMeshes);
            else if (ViewModel.SelectedNode is DamageMeshNode sd)
                keepDamage.Add(sd);

            if (_isMultiLightSelectActive)
                keepLights.AddRange(_multiSelectedLightGroups);
            else if (ViewModel.SelectedNode is LightGroupNode slg)
                keepLights.Add(slg);

            bool filterMeshes = keepMeshes.Count > 0 || keepDamage.Count > 0;
            bool filterLights = keepLights.Count > 0;
            if (!filterMeshes && !filterLights) return;

            foreach (var root in ViewModel.Roots)
                IsolateRecursive(root, keepMeshes, keepLights, keepDamage, filterMeshes, filterLights);
        }

        private void IsolateRecursive(IViewerNode node,
            List<MeshNode> keepMeshes, List<LightGroupNode> keepLights,
            List<DamageMeshNode> keepDamage,
            bool filterMeshes, bool filterLights)
        {
            if (filterMeshes && node is MeshNode mesh && !keepMeshes.Contains(mesh))
                mesh.IsChecked = false;
            if (filterMeshes && node is DamageMeshNode dmg && !keepDamage.Contains(dmg))
                dmg.IsChecked = false;
            if (node is LightGroupNode lg && !keepLights.Contains(lg))
                lg.IsChecked = false;
            // Always hide locators, avpins, and physics when isolating
            if (node is LocatorNode)
                node.IsChecked = false;
            if (node is AvPinNode)
                node.IsChecked = false;
            if (node is PhysicsDefinitionNode)
                node.IsChecked = false;
            foreach (var child in node.Children)
                IsolateRecursive(child, keepMeshes, keepLights, keepDamage, filterMeshes, filterLights);
        }

        private void UnhideAll()
        {
            foreach (var root in ViewModel.Roots)
                UnhideAllRecursive(root);
            // Re-apply current filter settings so view-option toggles are respected
            UpdateLODVisibility();
            UpdateViewTypeVisibility();
        }

        private void UnhideAllRecursive(IViewerNode node)
        {
            if (node.IsChecked == false)
                node.IsChecked = true;
            foreach (var child in node.Children)
                UnhideAllRecursive(child);
        }

        private void HideSelected_Click(object sender, RoutedEventArgs e) => HideSelected();
        private void IsolateSelected_Click(object sender, RoutedEventArgs e) => IsolateSelected();
        private void UnhideHidden_Click(object sender, RoutedEventArgs e) => UnhideAll();

        // Camera Options handlers

        private void FocusOnSelected_Click(object sender, RoutedEventArgs e)
        {
            CameraOptionsBtn?.Flyout?.Hide();
            FocusOnSelected();
        }

        private void FitAll_Click(object sender, RoutedEventArgs e)
        {
            CameraOptionsBtn?.Flyout?.Hide();
            AutoFitCamera();
        }

        private void ResetCamera_Click(object sender, RoutedEventArgs e)
        {
            if (_viewport?.Camera is not PerspectiveCamera cam) return;
            cam.Position       = new SDX.Vector3(50, 50, 50);
            cam.LookDirection  = new SDX.Vector3(-50, -50, -50);
            cam.UpDirection    = new SDX.Vector3(0, 1, 0);
            cam.FarPlaneDistance  = 50000;
            cam.NearPlaneDistance = 0.1;
        }

        private void CameraRotationMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_viewport == null || sender is not RadioButton rb) return;
            if (rb.Tag is string tag)
                _viewport.CameraRotationMode = tag == "Trackball"
                    ? HelixToolkit.SharpDX.Core.CameraRotationMode.Trackball
                    : HelixToolkit.SharpDX.Core.CameraRotationMode.Turntable;
        }

        private void CameraMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_viewport == null || sender is not RadioButton rb) return;
            if (rb.Tag is string tag)
            {
                _viewport.CameraMode = tag switch
                {
                    "WalkAround"    => HelixToolkit.SharpDX.Core.CameraMode.WalkAround,
                    "FixedPosition" => HelixToolkit.SharpDX.Core.CameraMode.FixedPosition,
                    _               => HelixToolkit.SharpDX.Core.CameraMode.Inspect,
                };
            }
        }

        private void CameraToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_viewport == null || sender is not CheckBox cb) return;
            bool on = cb.IsChecked ?? false;
            switch (cb.Name)
            {
                case nameof(RotateAroundMouseDownToggle):
                    _viewport.RotateAroundMouseDownPoint = on;
                    break;
                case nameof(ZoomAroundMouseDownToggle):
                    _viewport.ZoomAroundMouseDownPoint = on;
                    break;
                case nameof(InertiaToggle):
                    _viewport.IsInertiaEnabled = on;
                    break;
                case nameof(OrthographicToggle):
                    ApplyOrthographicMode(on);
                    break;
            }
        }

        private void ApplyOrthographicMode(bool orthographic)
        {
            if (_viewport == null) return;
            if (orthographic)
            {
                if (_viewport.Camera is PerspectiveCamera pc)
                {
                    _viewport.Camera = new OrthographicCamera
                    {
                        Position        = pc.Position,
                        LookDirection   = pc.LookDirection,
                        UpDirection     = pc.UpDirection,
                        FarPlaneDistance  = pc.FarPlaneDistance,
                        NearPlaneDistance = pc.NearPlaneDistance,
                        Width = (float)pc.LookDirection.Length() * 2,
                        CreateLeftHandSystem = true
                    };
                }
            }
            else
            {
                if (_viewport.Camera is OrthographicCamera oc)
                {
                    _viewport.Camera = new PerspectiveCamera
                    {
                        Position        = oc.Position,
                        LookDirection   = oc.LookDirection,
                        UpDirection     = oc.UpDirection,
                        FarPlaneDistance  = oc.FarPlaneDistance,
                        NearPlaneDistance = oc.NearPlaneDistance,
                        CreateLeftHandSystem = true
                    };
                }
            }
        }

        private void CameraSlider_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_viewport == null || sender is not Slider sl) return;
            double v = e.NewValue;
            switch (sl.Name)
            {
                case nameof(ZoomSensSlider):
                    _viewport.ZoomSensitivity = v;
                    if (ZoomSensLabel != null) ZoomSensLabel.Text = v.ToString("F1");
                    break;
                case nameof(RotSensSlider):
                    _viewport.RotationSensitivity = v;
                    if (RotSensLabel != null) RotSensLabel.Text = v.ToString("F1");
                    break;
                case nameof(PanSensSlider):
                    _viewport.LeftRightPanSensitivity = v;
                    _viewport.UpDownPanSensitivity    = v;
                    if (PanSensLabel != null) PanSensLabel.Text = v.ToString("F1");
                    break;
                case nameof(InertiaSlider):
                    _viewport.CameraInertiaFactor = v;
                    if (InertiaLabel != null) InertiaLabel.Text = v.ToString("F2");
                    break;
                case nameof(NearPlaneSlider):
                    if (_viewport.Camera is PerspectiveCamera pCamN)
                        pCamN.NearPlaneDistance = v;
                    if (NearPlaneLabel != null) NearPlaneLabel.Text = v.ToString("F3");
                    break;
                case nameof(FarPlaneSlider):
                    if (_viewport.Camera is PerspectiveCamera pCamF)
                        pCamF.FarPlaneDistance = v;
                    if (FarPlaneLabel != null) FarPlaneLabel.Text = ((int)v).ToString();
                    break;
                case nameof(FovSlider):
                    if (_viewport.Camera is PerspectiveCamera pCamFov)
                        pCamFov.FieldOfView = v;
                    if (FovLabel != null) FovLabel.Text = $"{(int)v}�";
                    break;
                case nameof(ZoomNearLimitSlider):
                    _viewport.ZoomDistanceLimitNear = v;
                    if (ZoomNearLimitLabel != null) ZoomNearLimitLabel.Text = v.ToString("F3");
                    break;
            }
        }

        private void AutoNearFar_Click(object sender, RoutedEventArgs e)
        {
            if (_viewport?.Camera is not PerspectiveCamera cam || _modelGroup == null) return;

            var totalBounds = new SDX.BoundingBox();
            bool hasBounds = false;
            foreach (var child in _modelGroup.Children)
            {
                if (child is MeshGeometryModel3D mesh && mesh.IsRendering && mesh.Geometry != null)
                {
                    totalBounds = hasBounds
                        ? SDX.BoundingBox.Merge(totalBounds, mesh.Bounds)
                        : mesh.Bounds;
                    hasBounds = true;
                }
            }
            if (!hasBounds) return;

            var center   = (totalBounds.Maximum + totalBounds.Minimum) / 2.0f;
            var camPos   = cam.Position;
            var distance = (float)(center - camPos).Length();
            if (distance < 1.0f) distance = 1.0f;

            var nearVal = Math.Max(distance * 0.001, 0.01);
            var farVal  = Math.Max(distance * 10, 1000);

            cam.NearPlaneDistance = nearVal;
            cam.FarPlaneDistance  = farVal;

            // Sync sliders if they exist
            if (NearPlaneSlider != null) NearPlaneSlider.Value = Math.Min(nearVal, NearPlaneSlider.Maximum);
            if (FarPlaneSlider  != null) FarPlaneSlider.Value  = Math.Min(farVal,  FarPlaneSlider.Maximum);
            if (NearPlaneLabel  != null) NearPlaneLabel.Text   = nearVal.ToString("F3");
            if (FarPlaneLabel   != null) FarPlaneLabel.Text    = ((int)farVal).ToString();
        }

        private bool _leftPanelCollapsed = false;

        private void LeftPanelToggle_Click(object sender, RoutedEventArgs e)
        {
            _leftPanelCollapsed = !_leftPanelCollapsed;

            if (_leftPanelCollapsed)
            {
                LeftPanelContent.Visibility = Visibility.Collapsed;
                LeftPanelColumn.Width = new GridLength(0);
                LeftPanelToggleIcon.Glyph = "\uE76C"; // ChevronRight
                LeftPanelToggleBtn.SetValue(ToolTipService.ToolTipProperty, "Expand panel");
            }
            else
            {
                LeftPanelContent.Visibility = Visibility.Visible;
                LeftPanelColumn.Width = new GridLength(380);
                LeftPanelToggleIcon.Glyph = "\uE76B"; // ChevronLeft
                LeftPanelToggleBtn.SetValue(ToolTipService.ToolTipProperty, "Collapse panel");
            }
        }
    }
}
