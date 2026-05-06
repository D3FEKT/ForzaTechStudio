using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.SharpDX.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace ForzaTechStudio.Views
{
    // Tree/Node Management and Event Handling Methods
    public sealed partial class ViewportPage : Page
    {
        // Adds a node tree to the TreeView. When <paramref name="deferRendering"/> is true,
        // PropertyChanged subscriptions are still attached but mesh rendering is deferred
        // (the caller is responsible for triggering a render pass afterwards via LOD filtering).
        private void AddNodeToTree(IViewerNode node, TreeViewNode? parentTree, bool deferRendering = false)
        {
            var treeNode = new TreeViewNode { Content = node, IsExpanded = node.IsExpanded };
            _treeNodeMap[node] = treeNode;

            if (parentTree == null)
            {
                FileTree.RootNodes.Add(treeNode);
            }
            else
            {
                parentTree.Children.Add(treeNode);
            }

            if (node is MeshNode meshNode)
            {
                meshNode.PropertyChanged += MeshNode_PropertyChanged;
                if (!deferRendering && meshNode.IsChecked == true)
                {
                    RenderMesh(meshNode);
                }
            }
            else if (node is LightGroupNode lightNode)
            {
                lightNode.PropertyChanged += LightNode_PropertyChanged;
                if (!deferRendering && lightNode.IsChecked != false)
                {
                    RenderLight(lightNode);
                }
            }
            else if (node is LightRowNode rowNode)
            {
                rowNode.PropertyChanged += LightRowNode_PropertyChanged;
            }
            else if (node is LocatorNode locatorNode)
            {
                locatorNode.PropertyChanged += LocatorNode_PropertyChanged;
                if (!deferRendering && locatorNode.IsChecked == true)
                {
                    RenderLocator(locatorNode);
                }
            }
            else if (node is SkeletonNode skelNode)
            {
                skelNode.PropertyChanged += SkeletonNode_PropertyChanged;
                if (!deferRendering && skelNode.IsChecked == true)
                {
                    RenderSkeleton(skelNode);
                }
            }
            else if (node is AvPinNode avPinNode)
            {
                avPinNode.PropertyChanged += AvPinNode_PropertyChanged;
                if (!deferRendering && avPinNode.IsChecked == true)
                {
                    RenderAvPin(avPinNode);
                }
            }
            else if (node is DamageMeshNode dmgNode)
            {
                dmgNode.PropertyChanged += DamageMeshNode_PropertyChanged;
                if (!deferRendering && dmgNode.IsChecked == true)
                {
                    RenderDamageMesh(dmgNode);
                }
            }

            foreach (var child in node.Children)
            {
                AddNodeToTree(child, treeNode, deferRendering);
            }
        }

        // Keep backward-compatible overload for callers that don't pass deferRendering
        private void AddNodeToTree(IViewerNode node, TreeViewNode? parentTree)
        {
            AddNodeToTree(node, parentTree, deferRendering: false);
        }

        private void MeshNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MeshNode.IsChecked) && sender is MeshNode meshNode)
            {
                 if (meshNode.IsChecked == true)
                 {
                     RenderMesh(meshNode);
                 }
                 else
                 {
                     HideMesh(meshNode);
                     RefreshHighlight();
                 }
                 SyncViewDropdownItems();
            }
        }

        private void LightNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LightGroupNode.IsChecked) && sender is LightGroupNode lightNode)
            {
                if (lightNode.IsChecked != false)
                {
                    RenderLight(lightNode);
                }
                else
                {
                    HideLight(lightNode);
                }
                SyncViewDropdownItems();
            }
        }

        private void LightRowNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LightRowNode.IsChecked) && sender is LightRowNode rowNode)
            {
                if (rowNode.Parent is LightGroupNode lightNode)
                {
                    if (lightNode.IsChecked != false)
                    {
                        HideLight(lightNode);
                        RenderLight(lightNode);
                    }
                }
            }
        }

        private void LocatorNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LocatorNode.IsChecked) && sender is LocatorNode locNode)
            {
                if (locNode.IsChecked == true)
                    RenderLocator(locNode);
                else
                    HideLocator(locNode);
                SyncViewDropdownItems();
            }
        }

        private void SkeletonNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SkeletonNode.IsChecked) && sender is SkeletonNode skelNode)
            {
                if (skelNode.IsChecked == true)
                    RenderSkeleton(skelNode);
                else
                    HideSkeleton(skelNode);
            }
        }

        private void AvPinNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AvPinNode.IsChecked) && sender is AvPinNode pinNode)
            {
                if (pinNode.IsChecked == true)
                    RenderAvPin(pinNode);
                else
                    HideAvPin(pinNode);
                SyncViewDropdownItems();
            }
        }

        private void DamageMeshNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DamageMeshNode.IsChecked) && sender is DamageMeshNode dmgNode)
            {
                if (dmgNode.IsChecked == true)
                    RenderDamageMesh(dmgNode);
                else
                    HideDamageMesh(dmgNode);
            }
        }

        private void ViewModel_RequestCloseRoot(object? sender, IViewerNode root)
        {
            if (_treeNodeMap.TryGetValue(root, out var treeNode))
            {
                 FileTree.RootNodes.Remove(treeNode);
                 CleanupNodeRecusrive(root);
            }
        }
        
        private void CleanupNodeRecusrive(IViewerNode node)
        {
            if (node is MeshNode meshNode)
            {
                meshNode.PropertyChanged -= MeshNode_PropertyChanged;
                HideMesh(meshNode);
            }
            else if (node is LightGroupNode lightNode)
            {
                lightNode.PropertyChanged -= LightNode_PropertyChanged;
                HideLight(lightNode);  // HideLight already cleans both _renderMap and _lightDamageRenderMap
            }
            else if (node is LightRowNode rowNode)
            {
                rowNode.PropertyChanged -= LightRowNode_PropertyChanged;
            }
            else if (node is LocatorNode locatorNode)
            {
                locatorNode.PropertyChanged -= LocatorNode_PropertyChanged;
                HideLocator(locatorNode);
            }
            else if (node is SkeletonNode skelNode)
            {
                skelNode.PropertyChanged -= SkeletonNode_PropertyChanged;
                HideSkeleton(skelNode);
            }
            else if (node is AvPinNode avPinNode)
            {
                avPinNode.PropertyChanged -= AvPinNode_PropertyChanged;
                HideAvPin(avPinNode);
            }
            else if (node is DamageMeshNode dmgNode)
            {
                dmgNode.PropertyChanged -= DamageMeshNode_PropertyChanged;
                HideDamageMesh(dmgNode);
            }

            
            foreach (var child in node.Children)
            {
                CleanupNodeRecusrive(child);
            }
            
            _treeNodeMap.Remove(node);
        }

        private void FileTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            var item = args.InvokedItem;
            if (item is TreeViewNode treeNode)
            {
                item = treeNode.Content;
            }

            if (item is IViewerNode node)
            {
                ViewModel.SelectedNode = node;
            }
        }

        private async void FileTree_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement element && element.DataContext is TreeViewNode treeNode)
            {
                if (treeNode.Content is ModelBinNode binNode && !string.IsNullOrEmpty(binNode.SourceZipPath))
                {
                    var menu = new MenuFlyout();
                    var replaceItem = new MenuFlyoutItem { Text = "Replace File in ZIP", Icon = new FontIcon { Glyph = "\uE8E5" } };
                    replaceItem.Click += async (s, args) => await ReplaceModelBinInZip(binNode);
                    menu.Items.Add(replaceItem);
                    
                    menu.ShowAt(element, e.GetPosition(element));
                }
            }
        }

        private void SyncSelectionToUI()
        {
            var node = ViewModel.SelectedNode;
            
            // If multi-select is active, don't let single selection override the UI
            if (_isMultiSelectActive)
            {
                // Still refresh locator cone colors
                foreach (var root in ViewModel.Roots)
                {
                    RefreshLocatorConesForRoot(root);
                }
                return;
            }

            // Refresh all locator cone colors whenever selection changes
            foreach (var root in ViewModel.Roots)
            {
                RefreshLocatorConesForRoot(root);
            }
            
            UpdateHighlight(node);
            
            if (node == null) return;

            // Sync tree view selection without auto-expanding; the selected item will
            // appear highlighted when the user manually expands the tree.
            if (_treeNodeMap.TryGetValue(node, out var tvNode))
            {
                FileTree.SelectedItem = tvNode;
            }

            // Handle LocatorNode selection
            if (node is LocatorNode locNode)
            {
                ShowLocatorMatrixUI(locNode);
                return;
            }

            // Handle AvPinNode selection
            if (node is AvPinNode avPinNode)
            {
                ShowAvPinUI(avPinNode);
                return;
            }
            
            // Handle LightGroupNode selection - sync to LightPartSelector
            if (node is LightGroupNode lightGroupNode)
            {
                _isUpdatingUi = true;
                LightPartSelector.SelectedItem = lightGroupNode;
                _isUpdatingUi = false;
                UpdateLightTransformUI(lightGroupNode);
                if (!_isMultiLightSelectActive)
                {
                    _currentLightHighlightTargets = new List<LightGroupNode> { lightGroupNode };
                    UpdateHighlightForLightGroups(_currentLightHighlightTargets);
                }
                return;
            }

            // Handle LightRowNode selection - sync parent group to LightPartSelector
            if (node is LightRowNode rowNode)
            {
                if (rowNode.Parent is LightGroupNode parentGroup)
                {
                    _isUpdatingUi = true;
                    LightPartSelector.SelectedItem = parentGroup;
                    _isUpdatingUi = false;
                    UpdateLightTransformUI(parentGroup);
                    if (!_isMultiLightSelectActive)
                    {
                        _currentLightHighlightTargets = new List<LightGroupNode> { parentGroup };
                        UpdateHighlightForLightGroups(_currentLightHighlightTargets);
                    }
                }
                return;
            }

            // Handle GrannyFileNode selection
            if (node is GrannyFileNode grannyNode)
            {
                ModelBinSelector.SelectedItem = grannyNode;
                return;
            }

            // Handle SkeletonNode selection
            if (node is SkeletonNode skelNode)
            {
                // Find parent GrannyFileNode
                var parent = skelNode.Parent;
                if (parent is GrannyFileNode gfn)
                    ModelBinSelector.SelectedItem = gfn;
                return;
            }

            // Handle BoneNode selection
            if (node is BoneNode boneNode)
            {
                // Find parent GrannyFileNode
                var parent = boneNode.Parent;
                while (parent != null && !(parent is GrannyFileNode))
                    parent = parent.Parent;
                if (parent is GrannyFileNode gfn2)
                    ModelBinSelector.SelectedItem = gfn2;
                return;
            }

            // Handle AnimationClipNode selection
            if (node is AnimationClipNode animNode)
            {
                var parent = animNode.Parent;
                if (parent is GrannyFileNode gfn3)
                    ModelBinSelector.SelectedItem = gfn3;
                return;
            }

            // Handle DamageMeshNode selection — sync ModelBinSelector + DamageMeshSelector
            if (node is DamageMeshNode dmgNode2 && dmgNode2.ParentModelBin != null)
            {
                ModelBinSelector.SelectedItem = dmgNode2.ParentModelBin;
                // DamageMeshSelector is populated by ModelBinSelector_SelectionChanged above
                foreach (var item in DamageMeshSelector.Items.OfType<DamageMeshNode>())
                {
                    if (item == dmgNode2)
                    {
                        DamageMeshSelector.SelectedItem = item;
                        break;
                    }
                }
                return;
            }

            ModelBinNode? targetBin = null;
            if (node is ModelBinNode mb) targetBin = mb;
            else if (node is MeshNode mn && mn.Parent is ModelBinNode mbParent) targetBin = mbParent;
            
            if (targetBin != null)
            {
                ModelBinSelector.SelectedItem = targetBin;
                
                if (node is MeshNode meshNode)
                {
                    foreach (var item in MeshSelector.Items)
                    {
                        if (item is MeshScopeItem scope && scope.Node == meshNode)
                        {
                            MeshSelector.SelectedItem = item;
                            break;
                        }
                    }
                }
                else
                {
                    if (MeshSelector.Items.Count > 0) MeshSelector.SelectedIndex = 0;
                }
            }
        }

        private void RefreshLocatorConesForRoot(IViewerNode root)
        {
            if (root is LocatorNode locNode)
            {
                RefreshLocatorCone(locNode);
            }
            if (root is AvPinNode avPinNode)
            {
                RefreshAvPinCone(avPinNode);
            }
            foreach (var child in root.Children)
            {
                RefreshLocatorConesForRoot(child);
            }
        }

        private void RefreshHighlight()
        {
            if (_currentLightHighlightTargets.Count > 0)
                UpdateHighlightForLightGroups(_currentLightHighlightTargets);
            else
                UpdateHighlight(_currentHighlightTargets);
        }

        private void UpdateHighlight(IEnumerable<MeshNode>? meshes)
        {
            if (_highlightModel == null) return;
            
            // Clear any active light-group highlight
            _currentLightHighlightTargets = new List<LightGroupNode>();

            var meshList = meshes != null ? meshes.ToList() : new List<MeshNode>();
            
            if (meshes != _currentHighlightTargets)
            {
                _currentHighlightTargets = meshList;
            }

            if (meshList.Count == 0)
            {
                _highlightModel.Visibility = Visibility.Collapsed;
                return;
            }

            var mergedGeo = new MeshGeometry3D();
            var pos = new Vector3Collection();
            var ind = new IntCollection();
            int offset = 0;
            
            foreach(var mesh in meshList)
            {
                if (_renderMap.TryGetValue(mesh, out var model))
                {
                    if (model.Visibility == Visibility.Collapsed) continue;
                    
                    if (model.Geometry is MeshGeometry3D geo)
                    {
                        pos.AddRange(geo.Positions);
                        
                        foreach(var i in geo.TriangleIndices)
                        {
                            ind.Add(i + offset);
                        }
                        offset += geo.Positions.Count;
                    }
                }
            }
            
            if (pos.Count > 0)
            {
                mergedGeo.Positions = pos;
                mergedGeo.TriangleIndices = ind;
                _highlightModel.Geometry = mergedGeo;
                _highlightModel.Visibility = Visibility.Visible;
            }
            else
            {
                 _highlightModel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateHighlight(IViewerNode? node)
        {
            if (node == null)
            {
                UpdateHighlight((IEnumerable<MeshNode>?)null);
                return;
            }

            if (node is LightGroupNode lg)
            {
                UpdateHighlightForLightGroups(new[] { lg });
                return;
            }

            if (node is DamageMeshNode dmgNode)
            {
                UpdateHighlightForDamageMesh(dmgNode);
                return;
            }

            var meshesToHighlight = new List<MeshNode>();
            if (node is MeshNode mn) meshesToHighlight.Add(mn);
            else if (node is ModelBinNode mb)
            {
                CollectMeshNodes(mb, meshesToHighlight);
            }
            // GrannyFile nodes don't have mesh highlights - skeleton is shown via lines
            
            UpdateHighlight(meshesToHighlight);
        }

        private void UpdateHighlightForDamageMesh(DamageMeshNode node)
        {
            if (_highlightModel == null) return;
            _currentHighlightTargets = new List<MeshNode>();
            _currentLightHighlightTargets = new List<LightGroupNode>();

            if (!_damageRenderMap.TryGetValue(node, out var model) ||
                model.Visibility == Visibility.Collapsed ||
                !(model.Geometry is MeshGeometry3D srcGeo) ||
                srcGeo.Positions == null || srcGeo.Positions.Count == 0)
            {
                _highlightModel.Visibility = Visibility.Collapsed;
                return;
            }

            var pos = new Vector3Collection();
            var ind = new IntCollection();
            pos.AddRange(srcGeo.Positions);
            foreach (var i in srcGeo.TriangleIndices) ind.Add(i);

            _highlightModel.Geometry = new MeshGeometry3D { Positions = pos, TriangleIndices = ind };
            _highlightModel.Visibility = Visibility.Visible;
        }

        private void UpdateHighlightForLightGroups(IEnumerable<LightGroupNode> groups)
        {
            if (_highlightModel == null) return;

            var groupList = groups?.ToList() ?? new List<LightGroupNode>();
            _currentLightHighlightTargets = groupList;
            _currentHighlightTargets = new List<MeshNode>();

            if (groupList.Count == 0)
            {
                _highlightModel.Visibility = Visibility.Collapsed;
                return;
            }

            var mergedGeo = new MeshGeometry3D();
            var pos = new Vector3Collection();
            var ind = new IntCollection();
            int offset = 0;

            foreach (var group in groupList)
            {
                if (_renderMap.TryGetValue(group, out var model) && model.Visibility != Visibility.Collapsed)
                {
                    if (model.Geometry is MeshGeometry3D geo)
                    {
                        pos.AddRange(geo.Positions);
                        foreach (var i in geo.TriangleIndices)
                            ind.Add(i + offset);
                        offset += geo.Positions.Count;
                    }
                }
            }

            if (pos.Count > 0)
            {
                mergedGeo.Positions = pos;
                mergedGeo.TriangleIndices = ind;
                _highlightModel.Geometry = mergedGeo;
                _highlightModel.Visibility = Visibility.Visible;
            }
            else
            {
                _highlightModel.Visibility = Visibility.Collapsed;
            }
        }

        private void ExpandAncestors(IViewerNode node)
        {
            var current = node.Parent;
            while (current != null)
            {
                if (_treeNodeMap.TryGetValue(current, out var tv))
                    tv.IsExpanded = true;
                current = current.Parent;
            }
        }
        
        private void CollectMeshNodes(IViewerNode node, List<MeshNode> list)
        {
            if (node is MeshNode m) list.Add(m);
            foreach(var c in node.Children) CollectMeshNodes(c, list);
        }
    }
}
