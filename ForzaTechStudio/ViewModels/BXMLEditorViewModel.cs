using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFormats;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.ViewModels
{
    public partial class BXMLEditorViewModel : ObservableObject
    {
        // file state

        private BXMLFile? _bxmlFile;

        // True when the loaded file was parsed as BXML binary (even if it carries
        // a .xml extension, which is normal for Forza game files).
        private bool _loadedAsBxml;

        private enum EditSource
        {
            Tree,
            XmlSource
        }

        private EditSource _lastEditSource = EditSource.Tree;
        private bool _isUpdatingXmlFromTree;
        private bool _isReplacingDocument;
        private bool _suppressXmlSyncFromTreeChanges;
        private readonly List<BXMLNodeViewModel> _allNodes = new();

        public IReadOnlyList<string> NodeFilterOptions { get; } =
        [
            "All entries",
            "Bundled entries",
            "Containers",
            "Leaf entries"
        ];

        public IReadOnlyList<string> ZipFilterOptions { get; } =
        [
            "All entries",
            "BXML only",
            "XML only"
        ];

        private string _loadedFilePath = "";
        public string LoadedFilePath
        {
            get => _loadedFilePath;
            set => SetProperty(ref _loadedFilePath, value);
        }

        private string _loadedFileName = "";
        public string LoadedFileName
        {
            get => _loadedFileName;
            set => SetProperty(ref _loadedFileName, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsNotBusy));
                    OnPropertyChanged(nameof(IsObjectModelPatchReady));
                    ApplyObjectModelPatchCommand.NotifyCanExecuteChanged();
                }
            }
        }
        public bool IsNotBusy => !IsBusy;

        private bool _isContentVisible;
        public bool IsContentVisible
        {
            get => _isContentVisible;
            set => SetProperty(ref _isContentVisible, value);
        }

        private string _statusMessage = "Waiting..";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // BXML data

        private string _xmlText = "";
        public string XmlText
        {
            get => _xmlText;
            set
            {
                if (SetProperty(ref _xmlText, value))
                {
                    if (!_isUpdatingXmlFromTree && !_isReplacingDocument)
                        _lastEditSource = EditSource.XmlSource;
                    OnPropertyChanged(nameof(XmlSummaryText));
                }
            }
        }

        public string XmlSummaryText
        {
            get
            {
                if (string.IsNullOrEmpty(XmlText))
                    return "0 lines";

                int lines = 1;
                for (int i = 0; i < XmlText.Length; i++)
                {
                    if (XmlText[i] == '\n')
                        lines++;
                }
                return lines == 1 ? "1 line" : $"{lines:N0} lines";
            }
        }

        private BXMLNodeViewModel? _treeRoot;
        public BXMLNodeViewModel? TreeRoot
        {
            get => _treeRoot;
            set
            {
                if (_treeRoot == value)
                    return;

                DetachTreeChangeHandlers(_treeRoot);
                if (SetProperty(ref _treeRoot, value))
                {
                    AttachTreeChangeHandlers(_treeRoot);
                    RebuildNodeIndex(selectRoot: value != null);
                    OnPropertyChanged(nameof(DocumentSummaryText));
                }
            }
        }

        private BXMLNodeViewModel? _selectedNode;
        public BXMLNodeViewModel? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetProperty(ref _selectedNode, value))
                {
                    OnPropertyChanged(nameof(IsNodeSelected));
                    OnPropertyChanged(nameof(IsNodeNotSelected));
                    OnPropertyChanged(nameof(SelectedNodePath));
                    OnPropertyChanged(nameof(SelectedNodeDepthText));
                    OnPropertyChanged(nameof(SelectedNodeAttributeCountText));
                    OnPropertyChanged(nameof(SelectedNodeChildCountText));
                    RefreshSelectedAttributes();
                    RefreshSelectedEntryFields();
                    OnPropertyChanged(nameof(SelectedEntryFieldCountText));
                    OnPropertyChanged(nameof(IsSelectedEntryFieldListEmpty));
                }
            }
        }

        public bool IsNodeSelected => SelectedNode != null;
        public bool IsNodeNotSelected => SelectedNode == null;

        public string SelectedNodePath => SelectedNode?.Path ?? "";
        public string SelectedNodeDepthText => SelectedNode == null ? "Depth 0" : $"Depth {SelectedNode.Depth}";
        public string SelectedNodeAttributeCountText => SelectedNode == null
            ? "0 attributes"
            : SelectedNode.Attributes.Count == 1 ? "1 attribute" : $"{SelectedNode.Attributes.Count:N0} attributes";
        public string SelectedNodeChildCountText => SelectedNode == null
            ? "0 children"
            : SelectedNode.Children.Count == 1 ? "1 child" : $"{SelectedNode.Children.Count:N0} children";

        public ObservableCollection<BXMLNodeViewModel> FilteredNodes { get; } = new();
        public ObservableCollection<BXMLAttributeViewModel> FilteredSelectedAttributes { get; } = new();
        public ObservableCollection<BXMLBrowserItemViewModel> BrowserRoots { get; } = new();
        public ObservableCollection<BXMLBrowserItemViewModel> FilteredBrowserRoots { get; } = new();
        public ObservableCollection<BXMLFieldViewModel> SelectedEntryFields { get; } = new();

        private int _browserTreeVersion;
        public int BrowserTreeVersion
        {
            get => _browserTreeVersion;
            private set => SetProperty(ref _browserTreeVersion, value);
        }

        private BXMLBrowserItemViewModel? _selectedBrowserItem;
        public BXMLBrowserItemViewModel? SelectedBrowserItem
        {
            get => _selectedBrowserItem;
            set
            {
                if (SetProperty(ref _selectedBrowserItem, value))
                {
                    SelectedNode = value?.SourceNode;
                    RefreshSelectedEntryFields();
                    OnPropertyChanged(nameof(SelectedEntryTitle));
                    OnPropertyChanged(nameof(SelectedEntrySubtitle));
                    OnPropertyChanged(nameof(SelectedEntryFieldCountText));
                    OnPropertyChanged(nameof(SelectedEntryChildCountText));
                }
            }
        }

        public string SelectedEntryTitle => SelectedBrowserItem?.DisplayName ?? "";
        public string SelectedEntrySubtitle => SelectedBrowserItem?.Path ?? "";
        public string SelectedEntryFieldCountText => SelectedEntryFields.Count == 1
            ? "1 bundled field"
            : $"{SelectedEntryFields.Count:N0} bundled fields";
        public string SelectedEntryChildCountText => SelectedBrowserItem == null
            ? "0 child entries"
            : SelectedBrowserItem.VisibleChildCount == 1 ? "1 child entry" : $"{SelectedBrowserItem.VisibleChildCount:N0} child entries";
        public bool IsSelectedEntryFieldListEmpty => IsNodeSelected && SelectedEntryFields.Count == 0;

        private string _nodeSearchText = "";
        public string NodeSearchText
        {
            get => _nodeSearchText;
            set
            {
                if (SetProperty(ref _nodeSearchText, value))
                {
                    RefreshFilteredNodes();
                    RefreshFilteredBrowserTree();
                }
            }
        }

        private string _selectedNodeFilter = "All entries";
        public string SelectedNodeFilter
        {
            get => _selectedNodeFilter;
            set
            {
                if (SetProperty(ref _selectedNodeFilter, value))
                {
                    RefreshFilteredNodes();
                    RefreshFilteredBrowserTree();
                }
            }
        }

        private string _attributeSearchText = "";
        public string AttributeSearchText
        {
            get => _attributeSearchText;
            set
            {
                if (SetProperty(ref _attributeSearchText, value))
                {
                    RefreshSelectedAttributes();
                    RefreshSelectedEntryFields();
                }
            }
        }

        private int _totalNodeCount;
        public int TotalNodeCount
        {
            get => _totalNodeCount;
            private set
            {
                if (SetProperty(ref _totalNodeCount, value))
                {
                    OnPropertyChanged(nameof(DocumentSummaryText));
                    OnPropertyChanged(nameof(FilteredNodeCountText));
                }
            }
        }

        private int _totalAttributeCount;
        public int TotalAttributeCount
        {
            get => _totalAttributeCount;
            private set
            {
                if (SetProperty(ref _totalAttributeCount, value))
                {
                    OnPropertyChanged(nameof(DocumentSummaryText));
                    OnPropertyChanged(nameof(FilteredAttributeCountText));
                }
            }
        }

        private int _maxDepth;
        public int MaxDepth
        {
            get => _maxDepth;
            private set
            {
                if (SetProperty(ref _maxDepth, value))
                    OnPropertyChanged(nameof(DocumentSummaryText));
            }
        }

        public string DocumentSummaryText =>
            $"{TotalNodeCount:N0} nodes · {TotalAttributeCount:N0} attributes · depth {MaxDepth}";

        public string FilteredNodeCountText =>
            FilteredNodes.Count == TotalNodeCount
                ? $"{TotalNodeCount:N0} nodes"
                : $"{FilteredNodes.Count:N0} of {TotalNodeCount:N0} nodes";

        public string FilteredAttributeCountText =>
            SelectedNode == null
                ? "0 attributes"
                : FilteredSelectedAttributes.Count == SelectedNode.Attributes.Count
                    ? SelectedNodeAttributeCountText
                    : $"{FilteredSelectedAttributes.Count:N0} of {SelectedNode.Attributes.Count:N0} attributes";

        public bool IsFilteredAttributeListEmpty => IsNodeSelected && FilteredSelectedAttributes.Count == 0;

        private void AttachTreeChangeHandlers(BXMLNodeViewModel? node)
        {
            if (node == null) return;
            node.ContentChanged += TreeNode_ContentChanged;
            foreach (var child in node.Children)
                AttachTreeChangeHandlers(child);
        }

        private void DetachTreeChangeHandlers(BXMLNodeViewModel? node)
        {
            if (node == null) return;
            node.ContentChanged -= TreeNode_ContentChanged;
            foreach (var child in node.Children)
                DetachTreeChangeHandlers(child);
        }

        private void TreeNode_ContentChanged(object? sender, BXMLNodeContentChangedEventArgs e)
        {
            if (_isReplacingDocument || TreeRoot == null)
                return;

            _lastEditSource = EditSource.Tree;
            if (e.AffectsOutline)
                RebuildNodeIndex(selectRoot: false);
            else
            {
                TreeRoot.RefreshMetadata(parent: null, depth: 0);
                TotalAttributeCount = _allNodes.Sum(node => node.Attributes.Count);
                RefreshFilteredNodes();
            }

            RefreshSelectedAttributes();
            OnPropertyChanged(nameof(SelectedNodePath));
            OnPropertyChanged(nameof(SelectedNodeDepthText));
            OnPropertyChanged(nameof(SelectedNodeAttributeCountText));
            OnPropertyChanged(nameof(SelectedNodeChildCountText));
            if (!_suppressXmlSyncFromTreeChanges)
                RefreshXmlFromTree(updateStatus: false);
            MarkCurrentObjectEntryDirty();
        }

        private void RebuildNodeIndex(bool selectRoot)
        {
            _allNodes.Clear();

            if (TreeRoot != null)
            {
                TreeRoot.RefreshMetadata(parent: null, depth: 0);
                FlattenNodes(TreeRoot, _allNodes);
            }

            TotalNodeCount = _allNodes.Count;
            TotalAttributeCount = _allNodes.Sum(node => node.Attributes.Count);
            MaxDepth = _allNodes.Count == 0 ? 0 : _allNodes.Max(node => node.Depth);

            RefreshFilteredNodes();
            RebuildBrowserTree();

            if (TreeRoot == null)
            {
                SelectedNode = null;
            }
            else if (selectRoot || SelectedNode == null || !_allNodes.Contains(SelectedNode))
            {
                SelectedNode = TreeRoot;
            }

            OnPropertyChanged(nameof(DocumentSummaryText));
        }

        private static void FlattenNodes(BXMLNodeViewModel node, List<BXMLNodeViewModel> target)
        {
            target.Add(node);
            foreach (var child in node.Children)
                FlattenNodes(child, target);
        }

        public string BrowserResultCountText
        {
            get
            {
                int total = CountBrowserItems(BrowserRoots);
                int shown = CountBrowserItems(FilteredBrowserRoots);
                return shown == total ? $"{total:N0} entries" : $"{shown:N0} of {total:N0} entries";
            }
        }

        private void RebuildBrowserTree()
        {
            BrowserRoots.Clear();

            if (TreeRoot != null)
                BrowserRoots.Add(BuildBrowserItem(TreeRoot, depth: 0));

            RefreshFilteredBrowserTree();
        }

        private BXMLBrowserItemViewModel BuildBrowserItem(BXMLNodeViewModel sourceNode, int depth)
        {
            var fields = BuildFieldRows(sourceNode);
            var item = new BXMLBrowserItemViewModel(sourceNode, depth, fields);

            foreach (var child in sourceNode.Children)
            {
                if (ShouldBundleChild(sourceNode, child))
                    continue;

                item.Children.Add(BuildBrowserItem(child, depth + 1));
            }

            item.RefreshChildSummary();
            return item;
        }

        private void RefreshFilteredBrowserTree()
        {
            FilteredBrowserRoots.Clear();

            foreach (var root in BrowserRoots)
            {
                var filtered = BuildFilteredBrowserItem(root);
                if (filtered != null)
                    FilteredBrowserRoots.Add(filtered);
            }

            BrowserTreeVersion++;
            OnPropertyChanged(nameof(BrowserResultCountText));

            var selectedSource = SelectedNode;
            var selectedItem = FindBrowserItem(FilteredBrowserRoots, selectedSource)
                ?? FindBrowserItem(BrowserRoots, selectedSource)
                ?? FirstBrowserItem(FilteredBrowserRoots)
                ?? FirstBrowserItem(BrowserRoots);

            if (selectedItem != null && !ReferenceEquals(SelectedBrowserItem?.SourceNode, selectedItem.SourceNode))
                SelectedBrowserItem = selectedItem;
            else
                RefreshSelectedEntryFields();
        }

        private BXMLBrowserItemViewModel? BuildFilteredBrowserItem(BXMLBrowserItemViewModel item)
        {
            var filteredChildren = new List<BXMLBrowserItemViewModel>();
            foreach (var child in item.Children)
            {
                var filteredChild = BuildFilteredBrowserItem(child);
                if (filteredChild != null)
                    filteredChildren.Add(filteredChild);
            }

            bool includeSelf = MatchesBrowserFilter(item);
            if (!includeSelf && filteredChildren.Count == 0)
                return null;

            var clone = item.CloneWithoutChildren();
            foreach (var child in filteredChildren)
                clone.Children.Add(child);
            clone.RefreshChildSummary();
            return clone;
        }

        private bool MatchesBrowserFilter(BXMLBrowserItemViewModel item)
        {
            bool typeMatch = SelectedNodeFilter switch
            {
                "Bundled entries" => item.FieldCount > 0,
                "Containers" => item.VisibleChildCount > 0,
                "Leaf entries" => item.VisibleChildCount == 0,
                _ => true
            };

            if (!typeMatch)
                return false;

            string term = NodeSearchText.Trim();
            return string.IsNullOrWhiteSpace(term) || item.MatchesSearch(term);
        }

        private static int CountBrowserItems(IEnumerable<BXMLBrowserItemViewModel> items)
        {
            int count = 0;
            foreach (var item in items)
                count += 1 + CountBrowserItems(item.Children);
            return count;
        }

        private static BXMLBrowserItemViewModel? FirstBrowserItem(IEnumerable<BXMLBrowserItemViewModel> items)
        {
            foreach (var item in items)
                return item;
            return null;
        }

        private static BXMLBrowserItemViewModel? FindBrowserItem(IEnumerable<BXMLBrowserItemViewModel> items, BXMLNodeViewModel? sourceNode)
        {
            if (sourceNode == null)
                return null;

            foreach (var item in items)
            {
                if (ReferenceEquals(item.SourceNode, sourceNode))
                    return item;

                var child = FindBrowserItem(item.Children, sourceNode);
                if (child != null)
                    return child;
            }

            return null;
        }

        private void RefreshSelectedEntryFields()
        {
            SelectedEntryFields.Clear();

            var node = SelectedBrowserItem?.SourceNode ?? SelectedNode;
            if (node != null)
            {
                string term = AttributeSearchText.Trim();
                foreach (var field in BuildFieldRows(node))
                {
                    if (string.IsNullOrWhiteSpace(term) || field.MatchesSearch(term))
                        SelectedEntryFields.Add(field);
                }
            }

            OnPropertyChanged(nameof(SelectedEntryFieldCountText));
            OnPropertyChanged(nameof(IsSelectedEntryFieldListEmpty));
        }

        private static List<BXMLFieldViewModel> BuildFieldRows(BXMLNodeViewModel node)
        {
            var fields = new List<BXMLFieldViewModel>();

            foreach (var attr in node.Attributes)
                fields.Add(BXMLFieldViewModel.ForAttribute(node, node.Name, attr));

            foreach (var child in node.Children)
            {
                if (IsBundledLeafNode(child))
                {
                    AddLeafFields(child, child.Name, fields);
                    continue;
                }

                if (IsFieldContainer(child))
                {
                    foreach (var leaf in child.Children)
                        AddLeafFields(leaf, leaf.Name, fields);
                }
            }

            return fields;
        }

        private static void AddLeafFields(BXMLNodeViewModel node, string sourceName, List<BXMLFieldViewModel> fields)
        {
            var idAttr = FindAttribute(node, "id");
            var valueAttr = FindAttribute(node, "value");

            if (idAttr != null && valueAttr != null)
            {
                fields.Add(BXMLFieldViewModel.ForIdValueNode(node, sourceName, idAttr, valueAttr));
                return;
            }

            if (valueAttr != null && node.Attributes.Count == 1)
            {
                fields.Add(BXMLFieldViewModel.ForFixedNameValue(node, sourceName, node.Name, valueAttr));
                return;
            }

            foreach (var attr in node.Attributes)
                fields.Add(BXMLFieldViewModel.ForAttribute(node, sourceName, attr));
        }

        private static BXMLAttributeViewModel? FindAttribute(BXMLNodeViewModel node, string name) =>
            node.Attributes.FirstOrDefault(attr => attr.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private static bool HasBundledFields(BXMLNodeViewModel node) =>
            node.Attributes.Count > 0 || node.Children.Any(child => ShouldBundleChild(node, child));

        private static bool ShouldBundleChild(BXMLNodeViewModel parent, BXMLNodeViewModel child) =>
            IsBundledLeafNode(child) || IsFieldContainer(child);

        private static bool IsBundledLeafNode(BXMLNodeViewModel node)
        {
            if (node.Children.Count != 0 || node.Attributes.Count == 0)
                return false;

            if (node.Name.Equals("key", StringComparison.OrdinalIgnoreCase) ||
                node.Name.Equals("property", StringComparison.OrdinalIgnoreCase))
                return true;

            return FindAttribute(node, "id") != null && FindAttribute(node, "value") != null;
        }

        private static bool IsFieldContainer(BXMLNodeViewModel node)
        {
            if (node.Attributes.Count != 0 || node.Children.Count == 0)
                return false;

            if (!node.Name.Equals("value", StringComparison.OrdinalIgnoreCase) &&
                !node.Name.EndsWith("properties", StringComparison.OrdinalIgnoreCase))
                return false;

            return node.Children.All(IsBundledLeafNode);
        }

        private void RefreshFilteredNodes()
        {
            string term = NodeSearchText.Trim();
            IEnumerable<BXMLNodeViewModel> query = _allNodes;

            query = SelectedNodeFilter switch
            {
                "Bundled entries" => query.Where(HasBundledFields),
                "Containers" => query.Where(node => node.Children.Any(child => !ShouldBundleChild(node, child))),
                "Leaf entries" => query.Where(node => node.Children.All(child => ShouldBundleChild(node, child))),
                _ => query
            };

            if (!string.IsNullOrWhiteSpace(term))
                query = query.Where(node => node.MatchesSearch(term));

            FilteredNodes.Clear();
            foreach (var node in query)
                FilteredNodes.Add(node);

            OnPropertyChanged(nameof(FilteredNodeCountText));
        }

        private void RefreshSelectedAttributes()
        {
            FilteredSelectedAttributes.Clear();

            if (SelectedNode != null)
            {
                string term = AttributeSearchText.Trim();
                foreach (var attr in SelectedNode.Attributes)
                {
                    if (string.IsNullOrWhiteSpace(term) || attr.MatchesSearch(term))
                        FilteredSelectedAttributes.Add(attr);
                }
            }

            OnPropertyChanged(nameof(FilteredAttributeCountText));
            OnPropertyChanged(nameof(IsFilteredAttributeListEmpty));
        }

        private bool RefreshXmlFromTree(bool updateStatus)
        {
            if (TreeRoot == null)
                return true;

            try
            {
                _isUpdatingXmlFromTree = true;
                XmlText = TreeViewModelToXmlString(TreeRoot);
                _lastEditSource = EditSource.Tree;
                if (updateStatus)
                    StatusMessage = "XML refreshed from tree edits.";
                return true;
            }
            catch (Exception ex)
            {
                if (updateStatus)
                    StatusMessage = $"Sync error: {ex.Message}";
                return false;
            }
            finally
            {
                _isUpdatingXmlFromTree = false;
            }
        }

        private string? GetXmlForSave()
        {
            if (TreeRoot != null && _lastEditSource == EditSource.Tree && !RefreshXmlFromTree(updateStatus: false))
            {
                StatusMessage = "Save failed: tree contains invalid XML names.";
                return null;
            }

            return XmlText;
        }


        private BXMLFile? GetBxmlFileForSave()
        {
            if (_lastEditSource == EditSource.XmlSource && !string.IsNullOrWhiteSpace(XmlText))
            {
                if (!TryApplyXmlTextToTree(out string? error))
                {
                    StatusMessage = $"Save failed: {error}";
                    return null;
                }
            }

            if (TreeRoot == null)
            {
                StatusMessage = "Save failed: nothing to save.";
                return null;
            }

            return BuildBxmlFileFromTree(TreeRoot);
        }

        private bool TryApplyXmlTextToTree(out string? error)
        {
            error = null;
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(XmlText);
                _isReplacingDocument = true;
                try
                {
                    _bxmlFile = BXMLConverter.FromXmlDocument(doc);
                    TreeRoot = BuildTreeViewModel(_bxmlFile.Root);
                    _lastEditSource = EditSource.Tree;
                }
                finally
                {
                    _isReplacingDocument = false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        [RelayCommand]
        private void ClearFilters()
        {
            NodeSearchText = "";
            SelectedNodeFilter = "All entries";
            AttributeSearchText = "";
            ZipSearchText = "";
            SelectedZipFilter = "All entries";
        }

        // Open

        [RelayCommand]
        private async Task OpenBxmlAsync()
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".bxml");
            picker.FileTypeFilter.Add(".xml");
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            await LoadFileAsync(file.Path);
        }

        public async Task LoadFileAsync(string path)
        {
            IsBusy = true;
            IsContentVisible = false;
            StatusMessage = "Parsing...";
            TreeRoot = null;
            SelectedNode = null;
            _bxmlFile = null;
            _loadedAsBxml = false;

            // Exit ZIP mode
            IsZipMode = false;
            ZipEntries.Clear();
            FilteredZipEntries.Clear();
            _selectedZipEntry = null;
            _loadedZipPath = "";
            LoadedZipName = "";
            ZipEntryCount = 0;
            ResetObjectModelState();
            ClearFilters();

            try
            {
                LoadedFilePath = path;
                LoadedFileName = Path.GetFileName(path);

                var (bxmlFile, xmlText, treeRoot, isBxml) = await Task.Run(() =>
                {
                    BXMLFile file;
                    bool parsedAsBxml;

                    // Forza uses .xml as the extension for BXML binary files.
                    // Read the first 4 bytes to check for the BXML magic before
                    // deciding how to parse — do not rely on extension alone.
                    if (HasBxmlMagic(path))
                    {
                        file = BXMLParser.FromFile(path);
                        parsedAsBxml = true;
                    }
                    else
                    {
                        // Plain XML — load and convert into the BXML in-memory model
                        // so the tree view works for both formats.
                        var doc = new XmlDocument();
                        doc.Load(path);
                        file = BXMLConverter.FromXmlDocument(doc);
                        parsedAsBxml = false;
                    }

                    string xml = BxmlFileToXmlString(file);
                    var root = BuildTreeViewModel(file.Root);
                    return (file, xml, root, parsedAsBxml);
                });

                _isReplacingDocument = true;
                try
                {
                    _bxmlFile = bxmlFile;
                    _loadedAsBxml = isBxml;
                    XmlText = xmlText;
                    TreeRoot = treeRoot;
                    _lastEditSource = EditSource.Tree;
                }
                finally
                {
                    _isReplacingDocument = false;
                }
                IsContentVisible = true;
                string kind = isBxml ? "BXML (binary)" : "XML";
                StatusMessage = $"Loaded {kind} \u2014 {LoadedFileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading file: {ex.Message}";
                IsContentVisible = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Save

        [RelayCommand]
        private async Task SaveAsync()
        {
            if (string.IsNullOrEmpty(LoadedFilePath))
            {
                await SaveAsBxmlAsync();
                return;
            }

            // Use the in-memory flag, not the file extension — Forza BXML files
            // carry a .xml extension but must be written as BXML binary.
            if (_loadedAsBxml)
                await WriteBxmlFileAsync(LoadedFilePath);
            else
                await WriteXmlFileAsync(LoadedFilePath);
        }

        [RelayCommand]
        private async Task SaveAsBxmlAsync()
        {
            // Forza stores BXML binary files with a .xml extension — match that.
            var path = await PickSavePathAsync(".xml", "Forza BXML File (*.xml)");
            if (path == null) return;
            await WriteBxmlFileAsync(path);
        }

        [RelayCommand]
        private async Task SaveAsXmlAsync()
        {
            var path = await PickSavePathAsync(".xml", "XML File");
            if (path == null) return;
            await WriteXmlFileAsync(path);
        }

        private async Task WriteBxmlFileAsync(string path)
        {
            IsBusy = true;
            StatusMessage = "Saving BXML\u2026";
            try
            {
                var bxmlFile = GetBxmlFileForSave();
                if (bxmlFile == null) return;
                await Task.Run(() => BXMLWriter.ToFile(bxmlFile, path));
                LoadedFilePath = path;
                LoadedFileName = Path.GetFileName(path);
                StatusMessage = $"Saved \u2014 {LoadedFileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task WriteXmlFileAsync(string path)
        {
            IsBusy = true;
            StatusMessage = "Saving XML\u2026";
            try
            {
                string? xml = GetXmlForSave();
                if (xml == null) return;
                await Task.Run(() => File.WriteAllText(path, xml, Encoding.UTF8));
                LoadedFilePath = path;
                LoadedFileName = Path.GetFileName(path);
                StatusMessage = $"Saved \u2014 {LoadedFileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // XML ↔ Tree sync

        [RelayCommand]
        private void ApplyXmlChanges()
        {
            if (string.IsNullOrWhiteSpace(XmlText)) return;
            if (TryApplyXmlTextToTree(out string? error))
            {
                _lastEditSource = EditSource.Tree;
                StatusMessage = "XML applied \u2014 tree refreshed.";
            }
            else
            {
                StatusMessage = $"XML parse error: {error}";
            }
        }

        [RelayCommand]
        private void SyncTreeToXml()
        {
            if (TreeRoot == null) return;
            RefreshXmlFromTree(updateStatus: true);
        }

        // Tree node editing

        [RelayCommand]
        private void AddAttribute()
        {
            if (SelectedNode == null) return;
            SelectedNode.Attributes.Add(new BXMLAttributeViewModel("attr", "value"));
        }

        [RelayCommand]
        private void RemoveAttribute(BXMLAttributeViewModel attr)
        {
            if (SelectedNode == null) return;
            SelectedNode.Attributes.Remove(attr);
        }

        // Structural editing

        [RelayCommand]
        private void AddChildElement()
        {
            if (SelectedNode == null) return;
            var child = new BXMLNodeViewModel("element");
            SelectedNode.Children.Add(child);
            RebuildNodeIndex(selectRoot: false);
            SelectedNode = child;
            StatusMessage = "Added child element.";
        }

        [RelayCommand]
        private void AddPropertyField()
        {
            if (SelectedNode == null) return;
            var property = new BXMLNodeViewModel("property");
            property.Attributes.Add(new BXMLAttributeViewModel("id", "NewProperty"));
            property.Attributes.Add(new BXMLAttributeViewModel("value", ""));
            SelectedNode.Children.Add(property);
            RefreshSelectedEntryFields();
            OnPropertyChanged(nameof(SelectedEntryFieldCountText));
            OnPropertyChanged(nameof(IsSelectedEntryFieldListEmpty));
            StatusMessage = "Added property field.";
        }

        [RelayCommand]
        private void DuplicateSelectedElement()
        {
            if (SelectedNode?.Parent is not { } parent) return;
            var clone = CloneNode(SelectedNode);
            int index = parent.Children.IndexOf(SelectedNode);
            parent.Children.Insert(index < 0 ? parent.Children.Count : index + 1, clone);
            RebuildNodeIndex(selectRoot: false);
            SelectedNode = clone;
            StatusMessage = "Duplicated element.";
        }

        [RelayCommand]
        private void DeleteSelectedElement()
        {
            if (SelectedNode?.Parent is not { } parent) return;
            var toRemove = SelectedNode;
            parent.Children.Remove(toRemove);
            RebuildNodeIndex(selectRoot: false);
            SelectedNode = parent;
            StatusMessage = "Deleted element.";
        }

        private static BXMLNodeViewModel CloneNode(BXMLNodeViewModel source)
        {
            var clone = new BXMLNodeViewModel(source.Name);
            foreach (var attr in source.Attributes)
                clone.Attributes.Add(new BXMLAttributeViewModel(attr.Name, attr.Value));
            foreach (var child in source.Children)
                clone.Children.Add(CloneNode(child));
            return clone;
        }

        // Close

        [RelayCommand]
        private void CloseFile()
        {
            ResetDocumentState();
            LoadedFilePath = "";
            LoadedFileName = "";
            IsContentVisible = false;
            StatusMessage = "File closed.";
            ClearFilters();
            ResetZipState();
            ResetObjectModelState();
        }

        // Resets the active document (tree / XML / selection) without touching
        // archive state.
        private void ResetDocumentState()
        {
            _bxmlFile = null;
            _loadedAsBxml = false;
            TreeRoot = null;
            SelectedNode = null;
            XmlText = "";
        }

        private void ResetZipState()
        {
            IsZipMode = false;
            ZipEntries.Clear();
            FilteredZipEntries.Clear();
            _selectedZipEntry = null;
            _loadedZipPath = "";
            LoadedZipName = "";
            ZipEntryCount = 0;
        }

        // ZIP support

        private string _loadedZipPath = "";

        private string _loadedZipName = "";
        public string LoadedZipName
        {
            get => _loadedZipName;
            set => SetProperty(ref _loadedZipName, value);
        }

        private bool _isZipMode;
        public bool IsZipMode
        {
            get => _isZipMode;
            set
            {
                if (SetProperty(ref _isZipMode, value))
                    OnPropertyChanged(nameof(IsNotZipMode));
            }
        }
        public bool IsNotZipMode => !IsZipMode;

        private int _zipEntryCount;
        public int ZipEntryCount
        {
            get => _zipEntryCount;
            private set
            {
                if (SetProperty(ref _zipEntryCount, value))
                {
                    OnPropertyChanged(nameof(ZipEntryCountText));
                    OnPropertyChanged(nameof(FilteredZipEntryCountText));
                }
            }
        }
        public string ZipEntryCountText => ZipEntryCount == 1 ? "(1 file)" : $"({ZipEntryCount} files)";

        public ObservableCollection<ZipEntryViewModel> ZipEntries { get; } = new();
        public ObservableCollection<ZipEntryViewModel> FilteredZipEntries { get; } = new();

        private string _zipSearchText = "";
        public string ZipSearchText
        {
            get => _zipSearchText;
            set
            {
                if (SetProperty(ref _zipSearchText, value))
                    RefreshFilteredZipEntries();
            }
        }

        private string _selectedZipFilter = "All entries";
        public string SelectedZipFilter
        {
            get => _selectedZipFilter;
            set
            {
                if (SetProperty(ref _selectedZipFilter, value))
                    RefreshFilteredZipEntries();
            }
        }

        public string FilteredZipEntryCountText =>
            FilteredZipEntries.Count == ZipEntries.Count
                ? ZipEntryCountText
                : $"({FilteredZipEntries.Count:N0} of {ZipEntries.Count:N0} files)";

        private ZipEntryViewModel? _selectedZipEntry;
        public ZipEntryViewModel? SelectedZipEntry
        {
            get => _selectedZipEntry;
            set
            {
                if (SetProperty(ref _selectedZipEntry, value) && value != null)
                    SelectZipEntry(value);
            }
        }

        private void RefreshFilteredZipEntries()
        {
            string term = ZipSearchText.Trim();
            IEnumerable<ZipEntryViewModel> query = ZipEntries;

            query = SelectedZipFilter switch
            {
                "BXML only" => query.Where(entry => entry.IsBxml),
                "XML only" => query.Where(entry => !entry.IsBxml),
                _ => query
            };

            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(entry =>
                    entry.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    entry.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            FilteredZipEntries.Clear();
            foreach (var entry in query)
                FilteredZipEntries.Add(entry);

            OnPropertyChanged(nameof(FilteredZipEntryCountText));
        }

        [RelayCommand]
        private async Task OpenZipAsync()
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".zip");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            await LoadZipAsync(file.Path);
        }

        public async Task LoadZipAsync(string zipPath)
        {
            // ObjectModelGame archives (manifest.xml + object-model files) get a
            // dedicated grouped editor; detect before the generic flat ZIP path.
            if (TryDetectObjectModelArchive(zipPath, out var omEntries))
            {
                await LoadObjectModelZipAsync(zipPath, omEntries);
                return;
            }

            IsBusy = true;
            IsContentVisible = false;
            StatusMessage = "Opening ZIP\u2026";
            ZipEntries.Clear();
            FilteredZipEntries.Clear();
            IsZipMode = false;
            TreeRoot = null;
            SelectedNode = null;
            _bxmlFile = null;
            _loadedAsBxml = false;
            _loadedZipPath = zipPath;
            LoadedZipName = Path.GetFileName(zipPath);
            ZipEntryCount = 0;
            ResetObjectModelState();
            ClearFilters();

            try
            {
                var entries = await Task.Run(() =>
                {
                    var result = new List<ZipEntryViewModel>();
                    using var archive = ZipFile.OpenRead(zipPath);
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.FullName.EndsWith(".bxml", StringComparison.OrdinalIgnoreCase) &&
                            !entry.FullName.EndsWith(".xml",  StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            using var entryStream = entry.Open();
                            using var ms = new MemoryStream();
                            entryStream.CopyTo(ms);
                            byte[] data = ms.ToArray();

                            bool isBxml = HasBxmlMagicBytes(data);
                            BXMLFile bxmlFile;
                            if (isBxml)
                            {
                                using var parseStream = new MemoryStream(data);
                                bxmlFile = BXMLParser.FromStream(parseStream);
                            }
                            else
                            {
                                var doc = new XmlDocument();
                                using var xmlStream = new MemoryStream(data);
                                doc.Load(xmlStream);
                                bxmlFile = BXMLConverter.FromXmlDocument(doc);
                            }

                            string xmlText = BxmlFileToXmlString(bxmlFile);
                            result.Add(new ZipEntryViewModel(entry.FullName, bxmlFile, xmlText, isBxml));
                        }
                        catch
                        {
                            // Skip entries that cannot be parsed
                        }
                    }
                    return result;
                });

                if (entries.Count == 0)
                {
                    StatusMessage = "No parseable BXML/XML files found in ZIP.";
                    IsBusy = false;
                    return;
                }

                foreach (var e in entries)
                    ZipEntries.Add(e);

                ZipEntryCount = entries.Count;
                RefreshFilteredZipEntries();
                IsZipMode = true;
                LoadedFilePath = "";
                LoadedFileName = LoadedZipName;
                IsContentVisible = true;
                StatusMessage = $"ZIP loaded \u2014 {entries.Count} file(s) \u2014 {LoadedZipName}";

                // Auto-select first entry (triggers SelectZipEntry via setter)
                SelectedZipEntry = entries[0];
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening ZIP: {ex.Message}";
                IsContentVisible = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void SelectZipEntry(ZipEntryViewModel entry)
        {
            _isReplacingDocument = true;
            try
            {
                _bxmlFile     = entry.BxmlFile;
                _loadedAsBxml = entry.IsBxml;
                XmlText       = entry.XmlText;
                TreeRoot      = BuildTreeViewModel(entry.BxmlFile.Root);
                _lastEditSource = EditSource.Tree;
            }
            finally
            {
                _isReplacingDocument = false;
            }
            LoadedFilePath = "";
            LoadedFileName = entry.DisplayName;
            StatusMessage  = $"Viewing: {entry.DisplayName}";
        }

        private static bool HasBxmlMagicBytes(byte[] data) =>
            data.Length >= 4 && data[0] == 0x42 && data[1] == 0x58 && data[2] == 0x4D && data[3] == 0x4C;

        // Helpers

        private const string XmlDeclarationLine = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";

        // Safe XML serialisers


        private static string BxmlFileToXmlString(BXMLFile file)
        {
            var sb = new StringBuilder();
            sb.Append(XmlDeclarationLine).Append('\n');
            WriteSafeXmlNode(sb, file.Root, 0);
            return sb.ToString();
        }

        private static void WriteSafeXmlNode(StringBuilder sb, BXMLNode node, int depth)
        {
            sb.Append(' ', depth * 2).Append('<').Append(node.Name);
            foreach (var attr in node.Attributes)
            {
                sb.Append(' ').Append(attr.Name).Append("=\"");
                AppendXmlEscaped(sb, attr.Value);
                sb.Append('"');
            }

            if (node.Children.Count == 0)
            {
                sb.Append(" />\n");
                return;
            }

            sb.Append(">\n");
            foreach (var child in node.Children)
                WriteSafeXmlNode(sb, child, depth + 1);
            sb.Append(' ', depth * 2).Append("</").Append(node.Name).Append(">\n");
        }

        private static string TreeViewModelToXmlString(BXMLNodeViewModel root)
        {
            var sb = new StringBuilder();
            sb.Append(XmlDeclarationLine).Append('\n');
            WriteSafeTreeXml(sb, root, 0);
            return sb.ToString();
        }

        private static void WriteSafeTreeXml(StringBuilder sb, BXMLNodeViewModel vm, int depth)
        {
            sb.Append(' ', depth * 2).Append('<').Append(vm.Name);
            foreach (var attr in vm.Attributes)
            {
                sb.Append(' ').Append(attr.Name).Append("=\"");
                AppendXmlEscaped(sb, attr.Value);
                sb.Append('"');
            }

            if (vm.Children.Count == 0)
            {
                sb.Append(" />\n");
                return;
            }

            sb.Append(">\n");
            foreach (var child in vm.Children)
                WriteSafeTreeXml(sb, child, depth + 1);
            sb.Append(' ', depth * 2).Append("</").Append(vm.Name).Append(">\n");
        }

        private static void AppendXmlEscaped(StringBuilder sb, string value)
        {
            foreach (char c in value)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\n': sb.Append("&#10;"); break;
                    case '\r': sb.Append("&#13;"); break;
                    case '\t': sb.Append("&#9;"); break;
                    default: sb.Append(c); break;
                }
            }
        }

        private static BXMLNodeViewModel BuildTreeViewModel(BXMLNode node)
        {
            var vm = new BXMLNodeViewModel(node.Name);
            foreach (var attr in node.Attributes)
                vm.Attributes.Add(new BXMLAttributeViewModel(attr.Name, attr.Value));
            foreach (var child in node.Children)
                vm.Children.Add(BuildTreeViewModel(child));
            return vm;
        }

        // Direct tree -> BXML conversion (bypasses textual XML entirely so element
        // names and attribute values are written byte-for-byte as edited).
        internal static BXMLFile BuildBxmlFileFromTree(BXMLNodeViewModel root) =>
            new() { Root = BuildBxmlNodeFromTree(root) };

        private static BXMLNode BuildBxmlNodeFromTree(BXMLNodeViewModel vm)
        {
            var node = new BXMLNode { Name = vm.Name, Flags = BXMLNodeFlags.IsNode };

            if (vm.Attributes.Count > 0)
            {
                node.Flags |= BXMLNodeFlags.HasAttributes;
                foreach (var attr in vm.Attributes)
                    node.Attributes.Add(new BXMLAttribute(attr.Name, attr.Value));
            }

            if (vm.Children.Count > 0)
            {
                node.Flags |= BXMLNodeFlags.HasChildNodes;
                foreach (var child in vm.Children)
                    node.Children.Add(BuildBxmlNodeFromTree(child));
            }

            return node;
        }

        internal static byte[] BuildBxmlBytesFromTree(BXMLNodeViewModel root)
        {
            var file = BuildBxmlFileFromTree(root);
            using var ms = new MemoryStream();
            BXMLWriter.ToStream(file, ms, leaveOpen: true);
            return ms.ToArray();
        }

        // Returns true if the file starts with the BXML magic bytes. Returns false on short files or I/O errors.
        private static bool HasBxmlMagic(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4, false);
                Span<byte> buf = stackalloc byte[4];
                if (fs.Read(buf) < 4) return false;
                // BXML magic little-endian: bytes 42 58 4D 4C
                return buf[0] == 0x42 && buf[1] == 0x58 && buf[2] == 0x4D && buf[3] == 0x4C;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string?> PickSavePathAsync(string extension, string description)
        {
            var picker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeChoices.Add(description, new List<string> { extension });
            if (!string.IsNullOrEmpty(LoadedFileName))
                picker.SuggestedFileName = Path.GetFileNameWithoutExtension(LoadedFileName);
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }
    }

    // BXMLNodeViewModel

    public partial class BXMLNodeViewModel : ObservableObject
    {
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                {
                    OnPropertyChanged(nameof(SearchText));
                    OnPropertyChanged(nameof(AttributePreview));
                    ContentChanged?.Invoke(this, BXMLNodeContentChangedEventArgs.ContentOnly);
                }
            }
        }

        private string _path = "";
        public string Path
        {
            get => _path;
            private set => SetProperty(ref _path, value);
        }

        private int _depth;
        public int Depth
        {
            get => _depth;
            private set
            {
                if (SetProperty(ref _depth, value))
                    OnPropertyChanged(nameof(LevelText));
            }
        }

        public BXMLNodeViewModel? Parent { get; private set; }

        public ObservableCollection<BXMLAttributeViewModel> Attributes { get; } = new();
        public ObservableCollection<BXMLNodeViewModel> Children { get; } = new();

        public event EventHandler<BXMLNodeContentChangedEventArgs>? ContentChanged;

        public string LevelText => Depth == 0 ? "Root" : $"L{Depth}";
        public string AttributeCountText => Attributes.Count == 1 ? "1 attr" : $"{Attributes.Count:N0} attrs";
        public string ChildCountText => Children.Count == 1 ? "1 child" : $"{Children.Count:N0} children";

        public string AttributePreview
        {
            get
            {
                if (Attributes.Count == 0)
                    return "No attributes";

                var parts = Attributes.Take(3).Select(attr => $"{attr.Name}={attr.Value}");
                string preview = string.Join(", ", parts);
                return Attributes.Count > 3 ? $"{preview}, +{Attributes.Count - 3:N0}" : preview;
            }
        }

        public string SearchText
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append(Name).Append(' ').Append(Path);
                foreach (var attr in Attributes)
                    sb.Append(' ').Append(attr.Name).Append(' ').Append(attr.Value);
                return sb.ToString();
            }
        }

        public string SubtextInfo
        {
            get
            {
                var attrPart = Attributes.Count == 1 ? "1 attr" : $"{Attributes.Count} attrs";
                if (Children.Count == 0) return attrPart;
                var childPart = Children.Count == 1 ? "1 child" : $"{Children.Count} children";
                return $"{attrPart} \u00b7 {childPart}";
            }
        }

        public BXMLNodeViewModel(string name)
        {
            _name = name;
            Attributes.CollectionChanged += Attributes_CollectionChanged;
            Children.CollectionChanged += Children_CollectionChanged;
        }

        public void RefreshMetadata(BXMLNodeViewModel? parent, int depth)
        {
            Parent = parent;
            Depth = depth;
            Path = parent == null ? Name : $"{parent.Path}/{Name}";

            foreach (var child in Children)
                child.RefreshMetadata(this, depth + 1);

            OnPropertyChanged(nameof(SubtextInfo));
            OnPropertyChanged(nameof(AttributeCountText));
            OnPropertyChanged(nameof(ChildCountText));
            OnPropertyChanged(nameof(AttributePreview));
            OnPropertyChanged(nameof(SearchText));
        }

        public bool MatchesSearch(string term) =>
            SearchText.Contains(term, StringComparison.OrdinalIgnoreCase);

        private void Attributes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (BXMLAttributeViewModel attr in e.OldItems)
                    attr.PropertyChanged -= Attribute_PropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (BXMLAttributeViewModel attr in e.NewItems)
                    attr.PropertyChanged += Attribute_PropertyChanged;
            }

            OnPropertyChanged(nameof(SubtextInfo));
            OnPropertyChanged(nameof(AttributeCountText));
            OnPropertyChanged(nameof(AttributePreview));
            OnPropertyChanged(nameof(SearchText));
            ContentChanged?.Invoke(this, BXMLNodeContentChangedEventArgs.OutlineAffected);
        }

        private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(SubtextInfo));
            OnPropertyChanged(nameof(ChildCountText));
            ContentChanged?.Invoke(this, BXMLNodeContentChangedEventArgs.OutlineAffected);
        }

        private void Attribute_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(SubtextInfo));
            OnPropertyChanged(nameof(AttributePreview));
            OnPropertyChanged(nameof(SearchText));
            ContentChanged?.Invoke(this, BXMLNodeContentChangedEventArgs.ContentOnly);
        }
    }

    public sealed class BXMLNodeContentChangedEventArgs : EventArgs
    {
        public static BXMLNodeContentChangedEventArgs ContentOnly { get; } = new(affectsOutline: false);
        public static BXMLNodeContentChangedEventArgs OutlineAffected { get; } = new(affectsOutline: true);

        public bool AffectsOutline { get; }

        private BXMLNodeContentChangedEventArgs(bool affectsOutline)
        {
            AffectsOutline = affectsOutline;
        }
    }

    // Logical browser entry shown in the BXML outline.

    public sealed partial class BXMLBrowserItemViewModel : ObservableObject
    {
        private readonly string _searchText;

        public BXMLNodeViewModel SourceNode { get; }
        public ObservableCollection<BXMLBrowserItemViewModel> Children { get; } = new();

        public int Depth { get; }
        public int FieldCount { get; }
        public int VisibleChildCount => Children.Count;
        public string Path => SourceNode.Path;
        public string LevelText => Depth == 0 ? "Root" : $"L{Depth}";
        public string DisplayName { get; }
        public string Subtitle { get; }
        public string FieldPreview { get; }
        public string FieldCountText => FieldCount == 1 ? "1 field" : $"{FieldCount:N0} fields";
        public string ChildCountText => VisibleChildCount == 1 ? "1 child" : $"{VisibleChildCount:N0} children";

        public BXMLBrowserItemViewModel(BXMLNodeViewModel sourceNode, int depth, IReadOnlyList<BXMLFieldViewModel> fields)
        {
            SourceNode = sourceNode;
            Depth = depth;
            FieldCount = fields.Count;

            var keyField = fields.FirstOrDefault(field => field.SourceName.Equals("key", StringComparison.OrdinalIgnoreCase));
            DisplayName = keyField == null || string.IsNullOrWhiteSpace(keyField.Value)
                ? sourceNode.Name
                : $"{sourceNode.Name} [{keyField.Value}]";

            FieldPreview = BuildFieldPreview(fields);
            Subtitle = string.IsNullOrWhiteSpace(FieldPreview) ? sourceNode.Path : FieldPreview;

            var sb = new StringBuilder();
            sb.Append(DisplayName).Append(' ').Append(sourceNode.SearchText).Append(' ').Append(Subtitle);
            foreach (var field in fields)
                sb.Append(' ').Append(field.Name).Append(' ').Append(field.Value).Append(' ').Append(field.SourceName);
            _searchText = sb.ToString();
        }

        private BXMLBrowserItemViewModel(BXMLBrowserItemViewModel original)
        {
            SourceNode = original.SourceNode;
            Depth = original.Depth;
            FieldCount = original.FieldCount;
            DisplayName = original.DisplayName;
            Subtitle = original.Subtitle;
            FieldPreview = original.FieldPreview;
            _searchText = original._searchText;
        }

        public BXMLBrowserItemViewModel CloneWithoutChildren() => new(this);

        public void RefreshChildSummary()
        {
            OnPropertyChanged(nameof(VisibleChildCount));
            OnPropertyChanged(nameof(ChildCountText));
        }

        public bool MatchesSearch(string term) =>
            _searchText.Contains(term, StringComparison.OrdinalIgnoreCase);

        private static string BuildFieldPreview(IReadOnlyList<BXMLFieldViewModel> fields)
        {
            if (fields.Count == 0)
                return "";

            var parts = fields.Take(3).Select(field => $"{field.Name}={field.Value}");
            string preview = string.Join(", ", parts);
            return fields.Count > 3 ? $"{preview}, +{fields.Count - 3:N0}" : preview;
        }
    }

    // A bundled editable field row. This can represent a direct attribute or a child property/id/value pair.

    public sealed partial class BXMLFieldViewModel : ObservableObject
    {
        private readonly BXMLAttributeViewModel? _nameAttribute;
        private readonly bool _nameUsesAttributeName;
        private readonly BXMLAttributeViewModel _valueAttribute;
        private readonly string _fixedName;

        public BXMLNodeViewModel SourceNode { get; }
        public string SourceName { get; }
        public string SourcePath => SourceNode.Path;
        public bool IsNameReadOnly => _nameAttribute == null;

        public string Name
        {
            get
            {
                if (_nameAttribute == null)
                    return _fixedName;

                return _nameUsesAttributeName ? _nameAttribute.Name : _nameAttribute.Value;
            }
            set
            {
                if (_nameAttribute == null)
                    return;

                if (_nameUsesAttributeName)
                    _nameAttribute.Name = value;
                else
                    _nameAttribute.Value = value;

                OnPropertyChanged();
            }
        }

        public string Value
        {
            get => _valueAttribute.Value;
            set
            {
                _valueAttribute.Value = value;
                OnPropertyChanged();
            }
        }

        private BXMLFieldViewModel(
            BXMLNodeViewModel sourceNode,
            string sourceName,
            BXMLAttributeViewModel? nameAttribute,
            bool nameUsesAttributeName,
            BXMLAttributeViewModel valueAttribute,
            string fixedName)
        {
            SourceNode = sourceNode;
            SourceName = sourceName;
            _nameAttribute = nameAttribute;
            _nameUsesAttributeName = nameUsesAttributeName;
            _valueAttribute = valueAttribute;
            _fixedName = fixedName;
        }

        public static BXMLFieldViewModel ForAttribute(BXMLNodeViewModel sourceNode, string sourceName, BXMLAttributeViewModel attr) =>
            new(sourceNode, sourceName, attr, nameUsesAttributeName: true, attr, attr.Name);

        public static BXMLFieldViewModel ForIdValueNode(BXMLNodeViewModel sourceNode, string sourceName, BXMLAttributeViewModel idAttr, BXMLAttributeViewModel valueAttr) =>
            new(sourceNode, sourceName, idAttr, nameUsesAttributeName: false, valueAttr, idAttr.Value);

        public static BXMLFieldViewModel ForFixedNameValue(BXMLNodeViewModel sourceNode, string sourceName, string fixedName, BXMLAttributeViewModel valueAttr) =>
            new(sourceNode, sourceName, nameAttribute: null, nameUsesAttributeName: false, valueAttr, fixedName);

        public bool MatchesSearch(string term) =>
            SourceName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            SourcePath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            Value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    // BXMLAttributeViewModel

    public partial class BXMLAttributeViewModel : ObservableObject
    {
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                    OnPropertyChanged(nameof(PairText));
            }
        }

        private string _value;
        public string Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                    OnPropertyChanged(nameof(PairText));
            }
        }

        public string PairText => $"{Name}=\"{Value}\"";

        public bool MatchesSearch(string term) =>
            Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            Value.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            PairText.Contains(term, StringComparison.OrdinalIgnoreCase);

        public BXMLAttributeViewModel(string name, string value)
        {
            _name  = name;
            _value = value;
        }
    }

    // ZipEntryViewModel — holds one parsed entry from a loaded ZIP archive

    public sealed class ZipEntryViewModel
    {
        /// <summary>Full path of the entry inside the ZIP (e.g. "data/cars/car.xml").</summary>
        public string FullName { get; }

        /// <summary>Just the filename portion, displayed in the picker list.</summary>
        public string DisplayName { get; }

        public BXMLFile BxmlFile { get; }
        public string   XmlText  { get; }
        public bool     IsBxml   { get; }
        public string   KindLabel => IsBxml ? "BXML" : "XML";

        public ZipEntryViewModel(string fullName, BXMLFile bxmlFile, string xmlText, bool isBxml)
        {
            FullName    = fullName;
            DisplayName = Path.GetFileName(fullName);
            BxmlFile    = bxmlFile;
            XmlText     = xmlText;
            IsBxml      = isBxml;
        }
    }
}
