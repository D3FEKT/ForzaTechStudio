using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.SharpDX.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Windows.UI.Core;

namespace ForzaTechStudio.Views
{
    // Tree/Node Management and Event Handling Methods
    public sealed partial class ViewportPage : Page
    {
        private int _visibilityBatchDepth;
        private bool _visibilityPostRefreshQueued;
        private bool _pendingViewDropdownSync;
        private bool _pendingHighlightRefresh;
        private readonly HashSet<ModelBinNode> _pendingCarbinRefreshModelBins = new();
        private readonly HashSet<LightGroupNode> _pendingLightRefreshGroups = new();

        private void RunVisibilityBatch(Action action)
        {
            _visibilityBatchDepth++;
            try
            {
                action();
            }
            finally
            {
                if (_visibilityBatchDepth > 0)
                    _visibilityBatchDepth--;

                if (_visibilityBatchDepth == 0)
                    ScheduleVisibilityPostRefresh();
            }
        }

        private void QueueVisibilityPostRefresh(
            bool syncViewDropdown = false,
            bool refreshHighlight = false,
            ModelBinNode? carbinModelBin = null,
            LightGroupNode? lightRefreshGroup = null)
        {
            _pendingViewDropdownSync |= syncViewDropdown;
            _pendingHighlightRefresh |= refreshHighlight;

            if (carbinModelBin != null)
                _pendingCarbinRefreshModelBins.Add(carbinModelBin);

            if (lightRefreshGroup != null)
                _pendingLightRefreshGroups.Add(lightRefreshGroup);

            if (_visibilityBatchDepth == 0)
                ScheduleVisibilityPostRefresh();
        }

        private void ScheduleVisibilityPostRefresh()
        {
            if (_visibilityPostRefreshQueued || !HasPendingVisibilityPostRefresh())
                return;

            _visibilityPostRefreshQueued = true;
            if (!DispatcherQueue.TryEnqueue(FlushVisibilityPostRefresh))
                FlushVisibilityPostRefresh();
        }

        private bool HasPendingVisibilityPostRefresh()
        {
            return _pendingViewDropdownSync
                || _pendingHighlightRefresh
                || _pendingCarbinRefreshModelBins.Count > 0
                || _pendingLightRefreshGroups.Count > 0;
        }

        private void FlushVisibilityPostRefresh()
        {
            _visibilityPostRefreshQueued = false;

            if (_visibilityBatchDepth > 0)
            {
                ScheduleVisibilityPostRefresh();
                return;
            }

            bool syncViewDropdown = _pendingViewDropdownSync;
            bool refreshHighlight = _pendingHighlightRefresh;
            var carbinModelBins = _pendingCarbinRefreshModelBins.ToList();
            var lightRefreshGroups = _pendingLightRefreshGroups.ToList();

            _pendingViewDropdownSync = false;
            _pendingHighlightRefresh = false;
            _pendingCarbinRefreshModelBins.Clear();
            _pendingLightRefreshGroups.Clear();

            foreach (var lightGroup in lightRefreshGroups)
            {
                if (lightGroup.IsChecked != false)
                {
                    HideLight(lightGroup);
                    RenderLight(lightGroup);
                }
            }

            foreach (var modelBin in carbinModelBins)
                RefreshCarbinInstancesForModel(modelBin);

            if (syncViewDropdown)
                SyncViewDropdownItems();

            if (refreshHighlight)
                RefreshHighlight();
        }

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
            else if (node is CarbinModelNode carbinModelNode)
            {
                carbinModelNode.PropertyChanged += CarbinModelNode_PropertyChanged;
                if (!deferRendering && carbinModelNode.IsChecked == true)
                {
                    RenderCarbinModel(carbinModelNode);
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
                     if (_isBulkLoading)
                         _pendingMeshRenders.Add(meshNode);
                     else
                         RenderMesh(meshNode);
                 }
                else
                {
                     HideMesh(meshNode);
                 }

                 if (!_isBulkLoading)
                 {
                     QueueVisibilityPostRefresh(
                         syncViewDropdown: true,
                         refreshHighlight: true,
                         carbinModelBin: meshNode.ParentModelBin);
                 }
            }
        }

        private void CarbinModelNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if ((e.PropertyName == nameof(CarbinModelNode.IsChecked)
                    || e.PropertyName == nameof(CarbinModelNode.UseTransforms))
                && sender is CarbinModelNode carbinModelNode)
            {
                if (carbinModelNode.IsChecked == true && carbinModelNode.UseTransforms)
                    RenderCarbinModel(carbinModelNode);
                else
                    HideCarbinModel(carbinModelNode);

                if (ReferenceEquals(ViewModel.SelectedNode, carbinModelNode))
                    QueueVisibilityPostRefresh(refreshHighlight: true);
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
                QueueVisibilityPostRefresh(syncViewDropdown: true, refreshHighlight: true);
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
                        QueueVisibilityPostRefresh(
                            refreshHighlight: true,
                            lightRefreshGroup: lightNode);
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
                QueueVisibilityPostRefresh(syncViewDropdown: true, refreshHighlight: true);
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
                QueueVisibilityPostRefresh(syncViewDropdown: true, refreshHighlight: true);
            }
        }

        private void DamageMeshNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DamageMeshNode.IsChecked) && sender is DamageMeshNode dmgNode)
            {
                if (dmgNode.IsChecked == true)
                {
                    if (_isBulkLoading)
                        _pendingDamageMeshRenders.Add(dmgNode);
                    else
                        RenderDamageMesh(dmgNode);
                }
                else
                    HideDamageMesh(dmgNode);

                QueueVisibilityPostRefresh(refreshHighlight: true);
            }
        }

        private void ViewModel_RequestCloseRoot(object? sender, IViewerNode root)
        {
            var closingNodes = CollectSubtreeNodes(root);
            bool removedActiveSelection = closingNodes.Contains(ViewModel.SelectedNode);

            SuppressTransformUiUpdates(() =>
            {
                ClearClosedRootSelections(closingNodes);
                ClearClosedRootUiReferences(closingNodes);
                ClearClosedRootPendingWork(closingNodes);
                ClearClosedRootAnimationReferences(closingNodes);
                _carbinModelBinCache.Clear();
            });

            if (_treeNodeMap.TryGetValue(root, out var treeNode))
            {
                FileTree.RootNodes.Remove(treeNode);
            }

            CleanupNodeRecusrive(root);
            DetachClosedSubtree(root);
            PruneClosedRootFromTabs(root);

            InvalidateViewportTextureLookup();
            _viewportAssignedMaterialCache.Clear();
            InvalidateViewportMaterialCache();
            DispatcherQueue.TryEnqueue(() =>
            {
                SuppressTransformUiUpdates(RefreshModelList);
                if (removedActiveSelection && ModelBinSelector.SelectedItem is not ModelBinNode)
                    ClearSelectionVisuals();
                else
                    UpdateTransformUI();
                RefreshManufacturerColorsFromLoadedRoots();
                UpdateMeshColors(SingleColorToggle?.IsChecked ?? false);
            });
        }

        private HashSet<IViewerNode> CollectSubtreeNodes(IViewerNode root)
        {
            var nodes = new HashSet<IViewerNode>();
            CollectSubtreeNodesRecursive(root, nodes);
            return nodes;
        }

        private void CollectSubtreeNodesRecursive(IViewerNode node, HashSet<IViewerNode> nodes)
        {
            if (!nodes.Add(node))
                return;

            foreach (var child in node.Children)
                CollectSubtreeNodesRecursive(child, nodes);
        }

        private void ClearClosedRootSelections(HashSet<IViewerNode> closingNodes)
        {
            if (closingNodes.Contains(ViewModel.SelectedNode))
                ViewModel.SelectedNode = null;

            foreach (var item in FileTree.SelectedItems.ToList())
            {
                if (item is IViewerNode node && closingNodes.Contains(node))
                    FileTree.SelectedItems.Remove(item);
            }

            foreach (var node in closingNodes)
                node.IsSelected = false;

            _multiSelectedMeshes.RemoveAll(mesh => closingNodes.Contains(mesh));
            foreach (var mesh in _multiSelectSnapshots.Keys.Where(closingNodes.Contains).ToList())
                _multiSelectSnapshots.Remove(mesh);
            _multiSelectedLightGroups.RemoveAll(group => closingNodes.Contains(group));
            foreach (var group in _multiSelectLightSnapshots.Keys.Where(closingNodes.Contains).ToList())
                _multiSelectLightSnapshots.Remove(group);

            _currentHighlightTargets = _currentHighlightTargets.Where(mesh => !closingNodes.Contains(mesh)).ToList();
            _currentLightHighlightTargets = _currentLightHighlightTargets.Where(group => !closingNodes.Contains(group)).ToList();

            _modelScopeDeltaMeshes.RemoveAll(mesh => closingNodes.Contains(mesh));
            foreach (var mesh in _modelScopeDeltaSnapshots.Keys.Where(closingNodes.Contains).ToList())
                _modelScopeDeltaSnapshots.Remove(mesh);

            _isMultiSelectActive = _multiSelectedMeshes.Count > 1;
            _isMultiLightSelectActive = _multiSelectedLightGroups.Count > 1;
            if (!_isMultiSelectActive && !_isMultiLightSelectActive)
                MultiSelectText.Visibility = Visibility.Collapsed;
        }

        private void ClearClosedRootUiReferences(HashSet<IViewerNode> closingNodes)
        {
            ClearComboBoxClosedSelection(ModelBinSelector, closingNodes);
            ClearComboBoxClosedSelection(MeshSelector, closingNodes);
            ClearComboBoxClosedSelection(DamageMeshSelector, closingNodes);
            ClearComboBoxClosedSelection(LightPartSelector, closingNodes);
            ClearComboBoxClosedSelection(LocatorPartSelector, closingNodes);
            ClearComboBoxClosedSelection(AvPinSelector, closingNodes);
            ClearComboBoxClosedSelection(CarbinModelSelector, closingNodes);
            ClearComboBoxClosedSelection(AnimationSelector, closingNodes);
        }

        private void ClearComboBoxClosedSelection(ComboBox selector, HashSet<IViewerNode> closingNodes)
        {
            if (selector.SelectedItem is IViewerNode selectedNode && closingNodes.Contains(selectedNode))
                selector.SelectedItem = null;

            if (ItemsSourceContainsClosedNode(selector.ItemsSource, closingNodes))
                selector.ItemsSource = null;
        }

        private bool ItemsSourceContainsClosedNode(object? itemsSource, HashSet<IViewerNode> closingNodes)
        {
            if (itemsSource is not System.Collections.IEnumerable items)
                return false;

            foreach (var item in items)
            {
                if (item is IViewerNode node && closingNodes.Contains(node))
                    return true;

                if (item is MeshScopeItem scopeItem && scopeItem.Node != null && closingNodes.Contains(scopeItem.Node))
                    return true;
            }

            return false;
        }

        private void ClearClosedRootPendingWork(HashSet<IViewerNode> closingNodes)
        {
            _pendingMeshRenders.RemoveAll(mesh => closingNodes.Contains(mesh));
            _pendingDamageMeshRenders.RemoveAll(mesh => closingNodes.Contains(mesh));

            foreach (var modelBin in _pendingCarbinRefreshModelBins.Where(closingNodes.Contains).ToList())
                _pendingCarbinRefreshModelBins.Remove(modelBin);

            foreach (var lightGroup in _pendingLightRefreshGroups.Where(closingNodes.Contains).ToList())
                _pendingLightRefreshGroups.Remove(lightGroup);
        }

        private void ClearClosedRootAnimationReferences(HashSet<IViewerNode> closingNodes)
        {
            bool animationUsesClosedRoot = AnimationSelector.SelectedItem is IViewerNode selectedAnimationNode && closingNodes.Contains(selectedAnimationNode)
                || _currentAnimSkeletonNode != null && closingNodes.Contains(_currentAnimSkeletonNode)
                || _boneMeshLinkMap?.Values.SelectMany(meshes => meshes).Any(mesh => closingNodes.Contains(mesh)) == true
                || _originalMeshBoneTransforms?.Keys.Any(mesh => closingNodes.Contains(mesh)) == true;

            if (!animationUsesClosedRoot)
                return;

            _animTimer?.Stop();
            _isAnimPlaying = false;
            _animCurrentTime = 0;
            _animDuration = 0;
            _currentAnimation = null;
            _currentAnimSkeleton = null;
            _currentAnimSkeletonNode = null;
            _currentTrackFilter = null;
            _preAnimBoneTransforms = null;
            _boneMeshLinkMap = null;
            _trackOnlyBoneTransforms = null;
            _gr2BindPoseWorldTransforms = null;
            _gr2InverseBindPoseTransforms = null;
            _mbBindPoseWorldTransforms = null;
            _mbInverseBindPoseTransforms = null;
            _originalMeshBoneTransforms = null;
            _animAnchorInverseTransforms = null;
            _cachedBoneMap = null;
            _cachedBoneMapSkeleton = null;
            _boneLocalTransforms = null;
            _linkedSkeletonMbPath = null;

            AnimationSelector.SelectedItem = null;
            AnimationSelector.ItemsSource = null;
            TrackSelector.SelectedItem = null;
            TrackSelector.ItemsSource = null;
            AnimTimeSlider.Value = 0;
            PlayPauseIcon.Glyph = "\uE768";
            UpdateAnimTimeDisplay();
            UpdateAnimBoneInfo();
            UpdateBoneMatchDetails();
        }

        private void ClearSelectionVisuals()
        {
            UpdateHighlight((IViewerNode?)null);
            UpdateHighlightForLightGroups(Array.Empty<LightGroupNode>());
            HideTransformGizmo();
            ClearTransformFields();
        }

        private void PruneClosedRootFromTabs(IViewerNode root)
        {
            foreach (var tab in ViewModel.Tabs)
                tab.Roots.Remove(root);
        }

        private void DetachClosedSubtree(IViewerNode node)
        {
            foreach (var child in node.Children.ToList())
                DetachClosedSubtree(child);

            node.Children.Clear();
            node.Parent = null;
        }
        
        private void CleanupNodeRecusrive(IViewerNode node)
        {
            if (node is MeshNode meshNode)
            {
                meshNode.PropertyChanged -= MeshNode_PropertyChanged;
                ReleaseMesh(meshNode);
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
                ReleaseDamageMesh(dmgNode);
            }
            else if (node is CarbinModelNode carbinModelNode)
            {
                carbinModelNode.PropertyChanged -= CarbinModelNode_PropertyChanged;
                _carbinModelBinCache.Remove(carbinModelNode);
                ReleaseCarbinModel(carbinModelNode);
            }

            
            foreach (var child in node.Children)
            {
                CleanupNodeRecusrive(child);
            }
            
            _treeNodeMap.Remove(node);
        }

        private void FileTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (_isSyncingSelection)
                return;

            var item = args.InvokedItem;
            if (item is TreeViewNode treeNode)
            {
                item = treeNode.Content;
            }

            if (item is IViewerNode node)
            {
                ApplySelectionFromTreeItems(sender.SelectedItems.Count > 0 ? sender.SelectedItems : new[] { item });
            }
        }

        private void FileTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            if (_isSyncingSelection)
                return;

            ApplySelectionFromTreeItems(sender.SelectedItems);
        }

        private void FileTreeRow_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_isSyncingSelection)
                return;

            var point = e.GetCurrentPoint(sender as UIElement);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            if (sender is not FrameworkElement { DataContext: TreeViewNode treeNode })
                return;

            if (treeNode.Content is not IViewerNode node)
                return;

            bool isCtrlHeld = Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(CoreVirtualKeyStates.Down);

            SelectTreeNodeFromClick(treeNode, node, isCtrlHeld);
            e.Handled = true;
        }

        private void SelectTreeNodeFromClick(TreeViewNode treeNode, IViewerNode node, bool isCtrlHeld)
        {
            if (!isCtrlHeld)
            {
                ApplySingleSelection(node, syncTree: true);
                return;
            }

            var selectedTreeNodes = FileTree.SelectedItems
                .OfType<TreeViewNode>()
                .Where(selectedNode => selectedNode.Content is IViewerNode)
                .ToList();

            if (selectedTreeNodes.Contains(treeNode))
                selectedTreeNodes.Remove(treeNode);
            else
                selectedTreeNodes.Add(treeNode);

            ApplySelectionFromTreeItems(selectedTreeNodes);
            SyncTreeSelection(selectedTreeNodes
                .Select(selectedNode => selectedNode.Content)
                .OfType<IViewerNode>());
        }

        private void ApplySelectionFromTreeItems(IEnumerable<object> selectedItems)
        {
            var nodes = selectedItems
                .Select(item => TryGetViewerNode(item, out var node) ? node : null)
                .Where(node => node != null)
                .Cast<IViewerNode>()
                .Distinct()
                .ToList();

            ApplySelection(nodes, syncTree: false);
        }

        private void ApplySelection(IReadOnlyList<IViewerNode> nodes, bool syncTree)
        {
            if (_isSyncingSelection)
                return;

            _isSyncingSelection = true;
            try
            {
                if (nodes.Count == 0)
                {
                    ClearSelection(syncTree);
                    return;
                }

                var primaryNode = nodes[^1];
                var meshNodes = nodes.OfType<MeshNode>().ToList();
                var lightGroups = nodes
                    .Select(node => node is LightRowNode row ? row.Parent as LightGroupNode : node as LightGroupNode)
                    .Where(group => group != null)
                    .Cast<LightGroupNode>()
                    .Distinct()
                    .ToList();

                if (nodes.Count > 1 && meshNodes.Count == nodes.Count)
                {
                    ApplyMeshMultiSelection(meshNodes, primaryNode, syncTree);
                    return;
                }

                if (nodes.Count > 1 && lightGroups.Count == nodes.Count)
                {
                    ApplyLightMultiSelection(lightGroups, primaryNode, syncTree);
                    return;
                }

                if (nodes.Count > 1)
                {
                    ApplyMixedMultiSelection(nodes, primaryNode, syncTree);
                    return;
                }

                ApplySingleSelection(primaryNode, syncTree);
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        private void ApplySingleSelection(IViewerNode node, bool syncTree)
        {
            if (!_isSyncingSelection)
            {
                _isSyncingSelection = true;
                try
                {
                    ApplySingleSelection(node, syncTree);
                }
                finally
                {
                    _isSyncingSelection = false;
                }
                return;
            }

            ClearMultiSelection();
            ClearSelectionState();
            node.IsSelected = true;
            if (syncTree)
                SyncTreeSelection(new[] { node });
            ViewModel.SelectedNode = node;
            SyncSelectionToUI();
        }

        private void ApplyMeshMultiSelection(IEnumerable<MeshNode> meshes, IViewerNode primaryNode, bool syncTree)
        {
            if (!_isSyncingSelection)
            {
                _isSyncingSelection = true;
                try
                {
                    ApplyMeshMultiSelection(meshes, primaryNode, syncTree);
                }
                finally
                {
                    _isSyncingSelection = false;
                }
                return;
            }

            var selectedMeshes = meshes.Distinct().ToList();
            ClearMultiSelection();
            ClearSelectionState();

            _multiSelectedMeshes = selectedMeshes;
            _isMultiSelectActive = selectedMeshes.Count > 0;
            foreach (var mesh in selectedMeshes)
                mesh.IsSelected = true;
            primaryNode.IsSelected = true;
            if (syncTree)
                SyncTreeSelection(selectedMeshes.Cast<IViewerNode>());

            ViewModel.SelectedNode = primaryNode;
            SnapshotMultiSelectValues();
            UpdateHighlight(_multiSelectedMeshes);
            UpdateTransformUIForMultiSelect();
        }

        private void ApplyLightMultiSelection(IEnumerable<LightGroupNode> groups, IViewerNode primaryNode, bool syncTree)
        {
            if (!_isSyncingSelection)
            {
                _isSyncingSelection = true;
                try
                {
                    ApplyLightMultiSelection(groups, primaryNode, syncTree);
                }
                finally
                {
                    _isSyncingSelection = false;
                }
                return;
            }

            var selectedGroups = groups.Distinct().ToList();
            ClearMultiSelection();
            ClearSelectionState();

            _multiSelectedLightGroups = selectedGroups;
            _isMultiLightSelectActive = selectedGroups.Count > 0;
            foreach (var group in selectedGroups)
                group.IsSelected = true;
            primaryNode.IsSelected = true;
            if (syncTree)
                SyncTreeSelection(selectedGroups.Cast<IViewerNode>());

            ViewModel.SelectedNode = primaryNode;
            SnapshotMultiLightSelectValues();
            UpdateHighlightForLightGroups(_multiSelectedLightGroups);
            UpdateTransformUIForMultiLightSelect();
        }

        private void ApplyMixedMultiSelection(IEnumerable<IViewerNode> nodes, IViewerNode primaryNode, bool syncTree)
        {
            if (!_isSyncingSelection)
            {
                _isSyncingSelection = true;
                try
                {
                    ApplyMixedMultiSelection(nodes, primaryNode, syncTree);
                }
                finally
                {
                    _isSyncingSelection = false;
                }
                return;
            }

            var selectedNodes = nodes.Distinct().ToList();
            ClearMultiSelection();
            ClearSelectionState();

            foreach (var selectedNode in selectedNodes)
                selectedNode.IsSelected = true;
            primaryNode.IsSelected = true;

            if (syncTree)
                SyncTreeSelection(selectedNodes);

            ViewModel.SelectedNode = primaryNode;

            var meshTargets = selectedNodes.SelectMany(GetHighlightMeshesForNode).Distinct().ToList();
            var lightTargets = selectedNodes.SelectMany(GetHighlightLightGroupsForNode).Distinct().ToList();

            _currentHighlightTargets = meshTargets;
            _currentLightHighlightTargets = lightTargets;

            if (meshTargets.Count > 0)
            {
                _multiSelectedMeshes = meshTargets;
                _isMultiSelectActive = true;
                SnapshotMultiSelectValues();
                UpdateHighlight(_multiSelectedMeshes);
                UpdateTransformUIForMultiSelect();
            }
            else
            {
                UpdateHighlight(meshTargets);
            }

            if (lightTargets.Count > 0)
            {
                _multiSelectedLightGroups = lightTargets;
                _isMultiLightSelectActive = meshTargets.Count == 0;
                SnapshotMultiLightSelectValues();
                UpdateHighlightForLightGroups(lightTargets);

                if (meshTargets.Count == 0)
                    UpdateTransformUIForMultiLightSelect();
            }

            if (meshTargets.Count == 0 && lightTargets.Count == 0)
                SyncSelectionToUI();
        }

        private void ClearSelection(bool syncTree)
        {
            if (!_isSyncingSelection)
            {
                _isSyncingSelection = true;
                try
                {
                    ClearSelection(syncTree);
                }
                finally
                {
                    _isSyncingSelection = false;
                }
                return;
            }

            ClearMultiSelection();
            ClearSelectionState();
            if (syncTree)
                SyncTreeSelection(Array.Empty<IViewerNode>());
            ViewModel.SelectedNode = null;
            UpdateHighlight((IViewerNode?)null);
        }

        private void ClearSelectionState()
        {
            foreach (var root in ViewModel.Roots)
                SetSelectedRecursive(root, false);
        }

        private void SetSelectedRecursive(IViewerNode node, bool isSelected)
        {
            node.IsSelected = isSelected;
            foreach (var child in node.Children)
                SetSelectedRecursive(child, isSelected);
        }

        private void SyncTreeSelection(IEnumerable<IViewerNode> selectedNodes)
        {
            var selectedTreeNodes = selectedNodes
                .Select(node => _treeNodeMap.TryGetValue(node, out var treeNode) ? treeNode : null)
                .Where(treeNode => treeNode != null)
                .Cast<TreeViewNode>()
                .ToList();

            FileTree.SelectedItems.Clear();
            foreach (var treeNode in selectedTreeNodes)
                FileTree.SelectedItems.Add(treeNode);
        }

        private IEnumerable<MeshNode> GetHighlightMeshesForNode(IViewerNode node)
        {
            if (node is MeshNode mesh)
            {
                yield return mesh;
                yield break;
            }

            foreach (var child in node.Children)
            {
                foreach (var childMesh in GetHighlightMeshesForNode(child))
                    yield return childMesh;
            }
        }

        private IEnumerable<LightGroupNode> GetHighlightLightGroupsForNode(IViewerNode node)
        {
            if (node is LightGroupNode lightGroup)
            {
                yield return lightGroup;
                yield break;
            }

            if (node is LightRowNode rowNode && rowNode.Parent is LightGroupNode parentGroup)
            {
                yield return parentGroup;
                yield break;
            }

            foreach (var child in node.Children)
            {
                foreach (var childGroup in GetHighlightLightGroupsForNode(child))
                    yield return childGroup;
            }
        }

        private async void FileTree_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (e.OriginalSource is not FrameworkElement element) return;
            if (element.DataContext is not TreeViewNode treeNode) return;
            if (treeNode.Content is not IViewerNode viewerNode) return;

            var menu = new MenuFlyout();

            // Find the root of the right-clicked node
            IViewerNode root = viewerNode;
            while (root.Parent != null)
                root = root.Parent;

            if (ViewModel.Roots.Contains(root))
            {
                var closeItem = new MenuFlyoutItem
                {
                    Text = $"Close \"{root.Name}\"",
                    Icon = new FontIcon { Glyph = "\uE8BB" }
                };
                closeItem.Click += (s, args) =>
                {
                    ViewModel_RequestCloseRoot(this, root);
                    ViewModel.RemoveRoot(root);
                    ViewModel.ActiveTab?.Roots.Remove(root);
                };
                menu.Items.Add(closeItem);
            }

            if (viewerNode is ModelBinNode binNode && !string.IsNullOrEmpty(binNode.SourceZipPath))
            {
                if (menu.Items.Count > 0)
                    menu.Items.Add(new MenuFlyoutSeparator());
                var replaceItem = new MenuFlyoutItem { Text = "Replace File in ZIP", Icon = new FontIcon { Glyph = "\uE8E5" } };
                replaceItem.Click += async (s, args) => await ReplaceModelBinInZip(binNode);
                menu.Items.Add(replaceItem);
            }

            if (menu.Items.Count > 0)
                menu.ShowAt(element, e.GetPosition(element));
        }

        private void SyncSelectionToUI()
        {
            var node = ViewModel.SelectedNode;
            RefreshTransformSelectionAvailability();
            
            // If multi-select is active, don't let single selection override the UI
            if (_isMultiSelectActive || _isMultiLightSelectActive)
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

            // Handle Carbin model selection
            if (node is CarbinModelNode carbinModelNode)
            {
                ShowCarbinModelUI(carbinModelNode);
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
            if (ViewModel.SelectedNode is CarbinModelNode carbinModelNode)
            {
                UpdateHighlight(carbinModelNode);
                return;
            }

            if (_currentLightHighlightTargets.Count > 0)
                UpdateHighlightForLightGroups(_currentLightHighlightTargets);
            else
                UpdateHighlight(_currentHighlightTargets);
        }

        private void UpdateHighlight(IEnumerable<MeshNode>? meshes)
        {
            if (_highlightModel == null) return;

            if (_lightHighlightModel != null)
                _lightHighlightModel.Visibility = Visibility.Collapsed;
            
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
                UpdateTransformGizmoForSelection();
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
                UpdateTransformGizmoForTargets(GetViewportTransformTargets(meshList));
            }
            else
            {
                 _highlightModel.Visibility = Visibility.Collapsed;
                  UpdateTransformGizmoForSelection();
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

            if (node is CarbinModelNode carbinModelNode)
            {
                UpdateHighlightForCarbinModel(carbinModelNode);
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
            HideTransformGizmo();
            _currentHighlightTargets = new List<MeshNode>();
            _currentLightHighlightTargets = new List<LightGroupNode>();
            if (_lightHighlightModel != null)
                _lightHighlightModel.Visibility = Visibility.Collapsed;

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
            if (_lightHighlightModel == null) return;

            var groupList = groups?.ToList() ?? new List<LightGroupNode>();
            _currentLightHighlightTargets = groupList;
            _currentHighlightTargets = new List<MeshNode>();
            if (_highlightModel != null)
                _highlightModel.Visibility = Visibility.Collapsed;

            if (groupList.Count == 0)
            {
                _lightHighlightModel.Visibility = Visibility.Collapsed;
                UpdateTransformGizmoForSelection();
                return;
            }

            var builder = new LineBuilder();
            int coneCount = 0;

            foreach (var group in groupList)
            {
                if (_renderMap.TryGetValue(group, out var model) && model.Visibility != Visibility.Collapsed)
                {
                    AppendLightConeWireframe(builder, group, damage: false, radius: 0.046f, height: 0.132f);
                    coneCount++;
                }

                if (_lightDamageRenderMap.TryGetValue(group, out var damageModel) &&
                    damageModel.Visibility != Visibility.Collapsed)
                {
                    AppendLightConeWireframe(builder, group, damage: true, radius: 0.046f, height: 0.132f);
                    coneCount++;
                }
            }

            if (coneCount > 0)
            {
                _lightHighlightModel.Geometry = builder.ToLineGeometry3D();
                _lightHighlightModel.Visibility = Visibility.Visible;
            }
            else
            {
                _lightHighlightModel.Visibility = Visibility.Collapsed;
            }

            UpdateTransformGizmoForTargets(GetViewportTransformTargets(groupList));
        }

        private void UpdateHighlightForCarbinModel(CarbinModelNode node)
        {
            if (_highlightModel == null) return;
            HideTransformGizmo();

            _currentHighlightTargets = new List<MeshNode>();
            _currentLightHighlightTargets = new List<LightGroupNode>();
            if (_lightHighlightModel != null)
                _lightHighlightModel.Visibility = Visibility.Collapsed;

            if (!_carbinRenderMap.TryGetValue(node, out var models) || models.Count == 0)
            {
                _highlightModel.Visibility = Visibility.Collapsed;
                return;
            }

            var mergedGeo = new MeshGeometry3D();
            var pos = new Vector3Collection();
            var ind = new IntCollection();
            int offset = 0;

            foreach (var model in models)
            {
                if (model.Visibility == Visibility.Collapsed)
                    continue;

                if (model.Geometry is not MeshGeometry3D geo || geo.Positions == null || geo.Positions.Count == 0)
                    continue;

                pos.AddRange(geo.Positions);
                foreach (var i in geo.TriangleIndices)
                    ind.Add(i + offset);
                offset += geo.Positions.Count;
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
