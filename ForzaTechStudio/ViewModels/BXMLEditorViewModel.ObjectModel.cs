using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFormats;
using ForzaTechStudio.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.ViewModels
{
    // ObjectModelGame.zip support.

    public partial class BXMLEditorViewModel
    {
        private bool _isObjectModelMode;
        public bool IsObjectModelMode
        {
            get => _isObjectModelMode;
            private set
            {
                if (SetProperty(ref _isObjectModelMode, value))
                {
                    OnPropertyChanged(nameof(IsNotObjectModelMode));
                    OnPropertyChanged(nameof(IsObjectModelPatchReady));
                }
            }
        }
        public bool IsNotObjectModelMode => !IsObjectModelMode;

        private string _objectModelArchivePath = "";

        private readonly Dictionary<string, ObjectModelEntry> _objectEntriesByName =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ObjectModelEntry> _objectEntriesById =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<string>> _objectModelPatchPropertiesByType =
            new(StringComparer.OrdinalIgnoreCase);

        private ObjectModelEntry? _currentObjectEntry;

        public ObservableCollection<ObjectModelNavNodeViewModel> ObjectModelRoots { get; } = new();
        public ObservableCollection<ObjectModelNavNodeViewModel> FilteredObjectModelRoots { get; } = new();
        public ObservableCollection<string> ObjectModelPatchObjectTypes { get; } = new();
        public ObservableCollection<string> ObjectModelPatchProperties { get; } = new();

        private int _objectModelTreeVersion;
        public int ObjectModelTreeVersion
        {
            get => _objectModelTreeVersion;
            private set => SetProperty(ref _objectModelTreeVersion, value);
        }

        private string _objectModelSearchText = "";
        public string ObjectModelSearchText
        {
            get => _objectModelSearchText;
            set
            {
                if (SetProperty(ref _objectModelSearchText, value))
                    RefreshFilteredObjectModelTree();
            }
        }

        private string _objectModelStatusText = "";
        public string ObjectModelStatusText
        {
            get => _objectModelStatusText;
            private set => SetProperty(ref _objectModelStatusText, value);
        }

        private int _objectModelFileCount;
        public int ObjectModelFileCount
        {
            get => _objectModelFileCount;
            private set
            {
                if (SetProperty(ref _objectModelFileCount, value))
                    OnPropertyChanged(nameof(ObjectModelSummaryText));
            }
        }

        private int _objectModelDirtyCount;
        public int ObjectModelDirtyCount
        {
            get => _objectModelDirtyCount;
            private set
            {
                if (SetProperty(ref _objectModelDirtyCount, value))
                {
                    OnPropertyChanged(nameof(ObjectModelSummaryText));
                    OnPropertyChanged(nameof(HasUnsavedObjectModelChanges));
                }
            }
        }

        public bool HasUnsavedObjectModelChanges => ObjectModelDirtyCount > 0;

        public string ObjectModelSummaryText =>
            ObjectModelDirtyCount > 0
                ? $"{ObjectModelFileCount:N0} object models \u00b7 {ObjectModelDirtyCount:N0} edited"
                : $"{ObjectModelFileCount:N0} object models";

        private string? _selectedObjectModelPatchType;
        public string? SelectedObjectModelPatchType
        {
            get => _selectedObjectModelPatchType;
            set
            {
                if (SetProperty(ref _selectedObjectModelPatchType, value))
                {
                    RefreshObjectModelPatchProperties();
                    OnPropertyChanged(nameof(IsObjectModelPatchTypeSelected));
                    OnPropertyChanged(nameof(IsObjectModelPatchReady));
                }
            }
        }

        public bool IsObjectModelPatchTypeSelected => !string.IsNullOrWhiteSpace(SelectedObjectModelPatchType);

        private string? _selectedObjectModelPatchProperty;
        public string? SelectedObjectModelPatchProperty
        {
            get => _selectedObjectModelPatchProperty;
            set
            {
                if (SetProperty(ref _selectedObjectModelPatchProperty, value))
                    OnPropertyChanged(nameof(IsObjectModelPatchReady));
            }
        }

        private string _objectModelPatchValue = "";
        public string ObjectModelPatchValue
        {
            get => _objectModelPatchValue;
            set => SetProperty(ref _objectModelPatchValue, value);
        }

        private string _objectModelPatchStatusText = "Load an ObjectModel archive to patch scribbledata entries.";
        public string ObjectModelPatchStatusText
        {
            get => _objectModelPatchStatusText;
            private set => SetProperty(ref _objectModelPatchStatusText, value);
        }

        public bool IsObjectModelPatchReady =>
            IsObjectModelMode &&
            IsNotBusy &&
            !string.IsNullOrWhiteSpace(SelectedObjectModelPatchType) &&
            !string.IsNullOrWhiteSpace(SelectedObjectModelPatchProperty);

        // Returns true if the archive at the given path is an ObjectModelGame
        // bundle (contains a manifest.xml entry alongside object-model files).
        private static bool TryDetectObjectModelArchive(string zipPath, out List<CustomZipFile.ZipEntryInfo> entries)
        {
            entries = new List<CustomZipFile.ZipEntryInfo>();
            try
            {
                using var zip = new CustomZipFile(zipPath);
                entries = zip.GetEntries();
            }
            catch
            {
                return false;
            }

            bool hasManifest = entries.Any(e =>
                !e.IsDirectory &&
                string.Equals(Path.GetFileName(e.Name.Replace('\\', '/')), "manifest.xml", StringComparison.OrdinalIgnoreCase));

            bool hasOm = entries.Any(e => !e.IsDirectory && IsObjectModelEntryName(e.Name));

            return hasManifest && hasOm;
        }

        private static bool IsObjectModelEntryName(string name)
        {
            string fileName = Path.GetFileName(name.Replace('\\', '/'));
            if (string.IsNullOrEmpty(fileName)) return false;
            if (string.Equals(fileName, "manifest.xml", StringComparison.OrdinalIgnoreCase)) return false;
            return fileName.EndsWith(".om.xml", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        }

        public async Task LoadObjectModelZipAsync(string zipPath, List<CustomZipFile.ZipEntryInfo> entries)
        {
            IsBusy = true;
            IsContentVisible = false;
            StatusMessage = "Opening ObjectModel archive\u2026";

            ResetDocumentState();
            ResetZipState();
            ResetObjectModelState();

            _objectModelArchivePath = zipPath;
            LoadedZipName = Path.GetFileName(zipPath);

            try
            {
                var (manifestEntry, navRoots, fileCount, patchTypes) = await Task.Run(() =>
                    BuildObjectModelGraph(zipPath, entries));

                foreach (var root in navRoots)
                    ObjectModelRoots.Add(root);

                PopulateObjectModelPatchTypes(patchTypes);

                ObjectModelFileCount = fileCount;
                RecountObjectModelDirty();
                RefreshFilteredObjectModelTree();

                IsObjectModelMode = true;
                IsZipMode = false;
                LoadedFilePath = "";
                LoadedFileName = LoadedZipName;
                IsContentVisible = true;
                ObjectModelStatusText = $"Loaded {LoadedZipName}";
                StatusMessage = $"ObjectModel archive loaded \u2014 {fileCount} object model(s).";

                // Open the manifest first so the user lands on something useful.
                if (manifestEntry != null)
                    LoadObjectModelEntry(manifestEntry);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening ObjectModel archive: {ex.Message}";
                IsContentVisible = false;
                IsObjectModelMode = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private (ObjectModelEntry? Manifest, List<ObjectModelNavNodeViewModel> Roots, int FileCount, List<string> PatchTypes) BuildObjectModelGraph(
            string zipPath, List<CustomZipFile.ZipEntryInfo> entries)
        {
            ObjectModelEntry? manifestEntry = null;
            var omEntries = new List<ObjectModelEntry>();
            var patchTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var zip = new CustomZipFile(zipPath))
            {
                foreach (var entry in entries)
                {
                    if (entry.IsDirectory) continue;

                    string normalized = entry.Name.Replace('\\', '/');
                    string fileName = Path.GetFileName(normalized);
                    bool isManifest = string.Equals(fileName, "manifest.xml", StringComparison.OrdinalIgnoreCase);

                    if (!isManifest && !IsObjectModelEntryName(normalized))
                        continue;

                    byte[] data = zip.ExtractToMemory(entry);
                    var omEntry = new ObjectModelEntry(normalized, fileName, data, isManifest);

                    _objectEntriesByName[normalized] = omEntry;

                    if (isManifest)
                    {
                        manifestEntry = omEntry;
                    }
                    else
                    {
                        omEntries.Add(omEntry);
                        foreach (string key in BuildEntryIdKeys(fileName))
                            _objectEntriesById[key] = omEntry;
                    }
                }
            }

            var roots = new List<ObjectModelNavNodeViewModel>();

            // Group navigator derived from the manifest tree.
            if (manifestEntry != null)
            {
                try
                {
                    using var ms = new MemoryStream(manifestEntry.RawBytes);
                    var manifestFile = BXMLParser.FromStream(ms);
                    CollectManifestTypeIds(manifestFile.Root, patchTypes);
                    var manifestNode = BuildManifestNavNode(manifestFile.Root, depth: 0);
                    manifestNode.IsManifestRoot = true;
                    roots.Add(manifestNode);
                }
                catch
                {
                    // Manifest unreadable as BXML
                }
            }

            // Always provide a flat, guaranteed-reachable listing of every object model.
            var allFiles = new ObjectModelNavNodeViewModel($"All Object Models", "ALL", depth: 0);
            foreach (var om in omEntries.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase))
                allFiles.Children.Add(new ObjectModelNavNodeViewModel(om, depth: 1));
            allFiles.RefreshSummary();
            roots.Add(allFiles);

            return (manifestEntry, roots, omEntries.Count, patchTypes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList());
        }

        private static void CollectManifestTypeIds(BXMLNode node, HashSet<string> target)
        {
            string? typeId = TryGetManifestTypeId(node);
            if (!string.IsNullOrWhiteSpace(typeId))
                target.Add(typeId);

            foreach (var child in node.Children)
                CollectManifestTypeIds(child, target);
        }

        private static string? TryGetManifestTypeId(BXMLNode node)
        {
            foreach (var attr in node.Attributes)
            {
                if (attr.Name.Equals("typeId", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(attr.Value))
                {
                    return attr.Value;
                }
            }

            string? idValue = null;
            string? valueValue = null;

            foreach (var attr in node.Attributes)
            {
                if (attr.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                    idValue = attr.Value;
                else if (attr.Name.Equals("value", StringComparison.OrdinalIgnoreCase))
                    valueValue = attr.Value;
            }

            if (!string.IsNullOrWhiteSpace(idValue) &&
                idValue.Equals("typeId", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(valueValue))
            {
                return valueValue;
            }

            if (node.Name.Equals("typeId", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(valueValue))
            {
                return valueValue;
            }

            return null;
        }

        private ObjectModelNavNodeViewModel BuildManifestNavNode(BXMLNode node, int depth)
        {
            var matched = ResolveManifestEntry(node);
            if (matched != null && node.Children.Count == 0)
                return new ObjectModelNavNodeViewModel(matched, depth, manifestLabel: DescribeManifestNode(node));

            var nav = new ObjectModelNavNodeViewModel(DescribeManifestNode(node), "GROUP", depth)
            {
                LinkedEntry = matched
            };

            foreach (var child in node.Children)
                nav.Children.Add(BuildManifestNavNode(child, depth + 1));

            nav.RefreshSummary();
            return nav;
        }

        private ObjectModelEntry? ResolveManifestEntry(BXMLNode node)
        {
            foreach (var attr in node.Attributes)
            {
                if (_objectEntriesById.TryGetValue(attr.Value, out var byVal))
                    return byVal;
            }
            if (_objectEntriesById.TryGetValue(node.Name, out var byName))
                return byName;
            return null;
        }

        private static string DescribeManifestNode(BXMLNode node)
        {
            var id = node.Attributes.FirstOrDefault(a =>
                a.Name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                a.Name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                a.Name.Equals("key", StringComparison.OrdinalIgnoreCase) ||
                a.Name.Equals("type", StringComparison.OrdinalIgnoreCase));
            string suffix = string.IsNullOrEmpty(id.Value) ? "" : $" [{id.Value}]";
            return $"{node.Name}{suffix}";
        }

        // Candidate keys an om file can be referenced by in the manifest.
        private static IEnumerable<string> BuildEntryIdKeys(string fileName)
        {
            string trimmed = fileName;
            if (trimmed.EndsWith(".om.xml", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^".om.xml".Length];
            else if (trimmed.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^".xml".Length];

            yield return fileName;
            yield return trimmed;

            int dot = trimmed.IndexOf('.');
            if (dot > 0)
                yield return trimmed[..dot];
        }

        private void RefreshFilteredObjectModelTree()
        {
            FilteredObjectModelRoots.Clear();

            string term = ObjectModelSearchText.Trim();
            foreach (var root in ObjectModelRoots)
            {
                var filtered = FilterObjectModelNode(root, term);
                if (filtered != null)
                    FilteredObjectModelRoots.Add(filtered);
            }

            ObjectModelTreeVersion++;
        }

        private static ObjectModelNavNodeViewModel? FilterObjectModelNode(ObjectModelNavNodeViewModel node, string term)
        {
            var filteredChildren = new List<ObjectModelNavNodeViewModel>();
            foreach (var child in node.Children)
            {
                var fc = FilterObjectModelNode(child, term);
                if (fc != null)
                    filteredChildren.Add(fc);
            }

            bool selfMatch = string.IsNullOrWhiteSpace(term) || node.MatchesSearch(term);
            if (!selfMatch && filteredChildren.Count == 0)
                return null;

            var clone = node.CloneShallow();
            foreach (var c in filteredChildren)
                clone.Children.Add(c);
            clone.RefreshSummary();
            return clone;
        }

        public void SelectObjectModelNavNode(ObjectModelNavNodeViewModel? node)
        {
            if (node?.Entry != null)
                LoadObjectModelEntry(node.Entry);
            else if (node?.LinkedEntry != null)
                LoadObjectModelEntry(node.LinkedEntry);
        }

        private void LoadObjectModelEntry(ObjectModelEntry entry)
        {
            try
            {
                entry.Tree = EnsureObjectModelEntryTree(entry);
                if (entry.Tree == null)
                    throw new InvalidOperationException($"Could not parse {entry.DisplayName}.");

                _currentObjectEntry = entry;

                _isReplacingDocument = true;
                try
                {
                    _bxmlFile = null;
                    _loadedAsBxml = true;
                    TreeRoot = entry.Tree;
                    XmlText = TreeViewModelToXmlString(entry.Tree);
                    _lastEditSource = EditSource.Tree;
                }
                finally
                {
                    _isReplacingDocument = false;
                }

                LoadedFilePath = "";
                LoadedFileName = entry.DisplayName;
                StatusMessage = entry.IsManifest
                    ? $"Editing manifest \u2014 {entry.DisplayName}"
                    : $"Editing object model \u2014 {entry.DisplayName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not open {entry.DisplayName}: {ex.Message}";
            }
        }

        private static BXMLNodeViewModel? EnsureObjectModelEntryTree(ObjectModelEntry entry)
        {
            if (entry.Tree != null)
                return entry.Tree;

            try
            {
                BXMLFile file;
                if (HasBxmlMagicBytes(entry.RawBytes))
                {
                    using var ms = new MemoryStream(entry.RawBytes);
                    file = BXMLParser.FromStream(ms);
                }
                else
                {
                    var doc = new System.Xml.XmlDocument();
                    using var ms = new MemoryStream(entry.RawBytes);
                    doc.Load(ms);
                    file = BXMLConverter.FromXmlDocument(doc);
                }

                entry.Tree = BuildTreeViewModel(file.Root);
            }
            catch
            {
                entry.Tree = null;
            }

            return entry.Tree;
        }

        // Called from the shared tree-change handler when the active document is an
        // object-model entry, so edits flag the entry for archive rebuild.
        private void MarkCurrentObjectEntryDirty()
        {
            if (!IsObjectModelMode || _currentObjectEntry == null || _isReplacingDocument)
                return;

            if (!_currentObjectEntry.IsDirty)
            {
                _currentObjectEntry.IsDirty = true;
                RecountObjectModelDirty();
                RefreshObjectModelDirtyBadges();
            }
        }

        private void RecountObjectModelDirty() =>
            ObjectModelDirtyCount = _objectEntriesByName.Values.Count(e => e.IsDirty);

        private void RefreshObjectModelDirtyBadges()
        {
            foreach (var root in ObjectModelRoots)
                root.RefreshDirtyRecursive();
            foreach (var root in FilteredObjectModelRoots)
                root.RefreshDirtyRecursive();
        }

        private void PopulateObjectModelPatchTypes(IEnumerable<string> typeIds)
        {
            ObjectModelPatchObjectTypes.Clear();
            foreach (string typeId in typeIds)
                ObjectModelPatchObjectTypes.Add(typeId);

            _objectModelPatchPropertiesByType.Clear();
            SelectedObjectModelPatchType = null;
            SelectedObjectModelPatchProperty = null;
            ObjectModelPatchValue = "";
            ObjectModelPatchStatusText = ObjectModelPatchObjectTypes.Count == 0
                ? "No typeId values were found in manifest.xml."
                : "Select an object type to load patchable property ids from source/scribbledata/.";
        }

        private void RefreshObjectModelPatchProperties()
        {
            ObjectModelPatchProperties.Clear();
            SelectedObjectModelPatchProperty = null;

            if (string.IsNullOrWhiteSpace(SelectedObjectModelPatchType))
            {
                ObjectModelPatchStatusText = ObjectModelPatchObjectTypes.Count == 0
                    ? "No typeId values were found in manifest.xml."
                    : "Select an object type to load patchable property ids from source/scribbledata/.";
                return;
            }

            if (!_objectModelPatchPropertiesByType.TryGetValue(SelectedObjectModelPatchType, out var propertyIds))
            {
                propertyIds = DiscoverObjectModelPatchProperties(SelectedObjectModelPatchType);
                _objectModelPatchPropertiesByType[SelectedObjectModelPatchType] = propertyIds;
            }

            foreach (string propertyId in propertyIds)
                ObjectModelPatchProperties.Add(propertyId);

            ObjectModelPatchStatusText = ObjectModelPatchProperties.Count == 0
                ? $"No patchable property ids were found for {SelectedObjectModelPatchType} under source/scribbledata/."
                : $"Found {ObjectModelPatchProperties.Count:N0} property id(s) for {SelectedObjectModelPatchType}.";
        }

        private IReadOnlyList<string> DiscoverObjectModelPatchProperties(string objectType)
        {
            var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in EnumerateScribbleDataEntries())
            {
                var tree = EnsureObjectModelEntryTree(entry);
                if (tree == null)
                    continue;

                foreach (var objectNode in EnumerateObjectNodes(tree, objectType))
                    CollectPatchablePropertyIds(objectNode, properties);
            }

            return properties.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        }

        [RelayCommand]
        private async Task ApplyObjectModelPatchAsync()
        {
            if (!IsObjectModelPatchReady)
                return;

            string objectType = SelectedObjectModelPatchType!;
            string propertyId = SelectedObjectModelPatchProperty!;
            string newValue = ObjectModelPatchValue;

            IsBusy = true;
            StatusMessage = $"Patching {objectType} / {propertyId}…";

            try
            {
                await Task.Yield();

                int patchedFiles = 0;

                foreach (var entry in EnumerateScribbleDataEntries())
                {
                    var tree = EnsureObjectModelEntryTree(entry);
                    if (tree == null)
                        continue;

                    bool shouldSuppressXmlSync = ReferenceEquals(entry, _currentObjectEntry) && ReferenceEquals(tree, TreeRoot);
                    bool changed = false;

                    if (shouldSuppressXmlSync)
                        _suppressXmlSyncFromTreeChanges = true;

                    try
                    {
                        foreach (var objectNode in EnumerateObjectNodes(tree, objectType))
                        {
                            if (PatchObjectNodeProperty(objectNode, propertyId, newValue))
                                changed = true;
                        }
                    }
                    finally
                    {
                        if (shouldSuppressXmlSync)
                            _suppressXmlSyncFromTreeChanges = false;
                    }

                    if (!changed)
                        continue;

                    patchedFiles++;
                    entry.IsDirty = true;

                    if (shouldSuppressXmlSync)
                        RefreshXmlFromTree(updateStatus: false);
                }

                RecountObjectModelDirty();
                RefreshObjectModelDirtyBadges();

                ObjectModelPatchStatusText = $"Patched {patchedFiles:N0} file(s) for {objectType} / {propertyId}.";
                StatusMessage = ObjectModelPatchStatusText;
                App.ShowInfoDialog($"Patched {patchedFiles:N0} amount of files.", "ObjectModel Patch");
            }
            catch (Exception ex)
            {
                ObjectModelPatchStatusText = $"Patch failed: {ex.Message}";
                StatusMessage = ObjectModelPatchStatusText;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private IEnumerable<ObjectModelEntry> EnumerateScribbleDataEntries() =>
            _objectEntriesByName.Values.Where(entry =>
                !entry.IsManifest &&
                IsScribbleDataEntryPath(entry.EntryName));

        private static bool IsScribbleDataEntryPath(string entryName)
        {
            var segments = entryName
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals("source", StringComparison.OrdinalIgnoreCase) &&
                    segments[i + 1].Equals("scribbledata", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<BXMLNodeViewModel> EnumerateObjectNodes(BXMLNodeViewModel root, string objectType)
        {
            if (IsMatchingObjectNode(root, objectType))
                yield return root;

            foreach (var child in root.Children)
            {
                foreach (var descendant in EnumerateObjectNodes(child, objectType))
                    yield return descendant;
            }
        }

        private static bool IsMatchingObjectNode(BXMLNodeViewModel node, string objectType)
        {
            if (!node.Name.Equals("object", StringComparison.OrdinalIgnoreCase))
                return false;

            var typeAttr = FindAttributeIgnoreCase(node, "type");
            return typeAttr != null && typeAttr.Value.Equals(objectType, StringComparison.OrdinalIgnoreCase);
        }

        private static void CollectPatchablePropertyIds(BXMLNodeViewModel root, HashSet<string> propertyIds)
        {
            foreach (var child in root.Children)
            {
                var idAttr = FindAttributeIgnoreCase(child, "id");
                var valueAttr = FindAttributeIgnoreCase(child, "value");
                if (idAttr != null && valueAttr != null && !string.IsNullOrWhiteSpace(idAttr.Value))
                    propertyIds.Add(idAttr.Value);

                CollectPatchablePropertyIds(child, propertyIds);
            }
        }

        private static bool PatchObjectNodeProperty(BXMLNodeViewModel root, string propertyId, string newValue)
        {
            bool changed = false;

            foreach (var child in root.Children)
            {
                var idAttr = FindAttributeIgnoreCase(child, "id");
                var valueAttr = FindAttributeIgnoreCase(child, "value");

                if (idAttr != null &&
                    valueAttr != null &&
                    idAttr.Value.Equals(propertyId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(valueAttr.Value, newValue, StringComparison.Ordinal))
                {
                    valueAttr.Value = newValue;
                    changed = true;
                }

                if (PatchObjectNodeProperty(child, propertyId, newValue))
                    changed = true;
            }

            return changed;
        }

        private static BXMLAttributeViewModel? FindAttributeIgnoreCase(BXMLNodeViewModel node, string attributeName) =>
            node.Attributes.FirstOrDefault(attr => attr.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));

        [RelayCommand]
        private async Task SaveObjectModelZipAsync()
        {
            if (!IsObjectModelMode || string.IsNullOrEmpty(_objectModelArchivePath))
                return;

            var dirty = _objectEntriesByName.Values.Where(e => e.IsDirty && e.Tree != null).ToList();
            if (dirty.Count == 0)
            {
                StatusMessage = "No object-model changes to save.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Rebuilding ObjectModel archive\u2026";
            try
            {
                await Task.Run(() =>
                {
                    var replacements = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in dirty)
                    {
                        byte[] bytes = BuildBxmlBytesFromTree(entry.Tree!);
                        replacements[entry.EntryName] = bytes;
                        entry.RawBytes = bytes;
                    }
                    ZipArchiveHelper.ReplaceEntries(_objectModelArchivePath, replacements);
                });

                foreach (var entry in dirty)
                    entry.IsDirty = false;

                RecountObjectModelDirty();
                RefreshObjectModelDirtyBadges();
                StatusMessage = $"Saved {dirty.Count} object model(s) into {LoadedZipName}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Archive save failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task SaveObjectModelZipAsAsync()
        {
            if (!IsObjectModelMode || string.IsNullOrEmpty(_objectModelArchivePath))
                return;

            var picker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeChoices.Add("ZIP Archive (*.zip)", new List<string> { ".zip" });
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(LoadedZipName);
            var target = await picker.PickSaveFileAsync();
            if (target == null) return;

            IsBusy = true;
            StatusMessage = "Writing ObjectModel archive\u2026";
            try
            {
                string sourcePath = _objectModelArchivePath;
                string destPath = target.Path;
                var dirty = _objectEntriesByName.Values.Where(e => e.IsDirty && e.Tree != null).ToList();

                await Task.Run(() =>
                {
                    if (!string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
                        File.Copy(sourcePath, destPath, overwrite: true);

                    if (dirty.Count > 0)
                    {
                        var replacements = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                        foreach (var entry in dirty)
                            replacements[entry.EntryName] = BuildBxmlBytesFromTree(entry.Tree!);
                        ZipArchiveHelper.ReplaceEntries(destPath, replacements);
                    }
                });

                _objectModelArchivePath = destPath;
                LoadedZipName = Path.GetFileName(destPath);
                LoadedFileName = LoadedZipName;
                foreach (var entry in dirty)
                {
                    entry.RawBytes = BuildBxmlBytesFromTree(entry.Tree!);
                    entry.IsDirty = false;
                }
                RecountObjectModelDirty();
                RefreshObjectModelDirtyBadges();
                StatusMessage = $"Saved archive \u2014 {LoadedZipName}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Archive save failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ResetObjectModelState()
        {
            IsObjectModelMode = false;
            _objectModelArchivePath = "";
            _currentObjectEntry = null;
            _objectEntriesByName.Clear();
            _objectEntriesById.Clear();
            _objectModelPatchPropertiesByType.Clear();
            ObjectModelRoots.Clear();
            FilteredObjectModelRoots.Clear();
            ObjectModelPatchObjectTypes.Clear();
            ObjectModelPatchProperties.Clear();
            _selectedObjectModelPatchType = null;
            _selectedObjectModelPatchProperty = null;
            ObjectModelPatchValue = "";
            ObjectModelPatchStatusText = "Load an ObjectModel archive to patch scribbledata entries.";
            ObjectModelSearchText = "";
            ObjectModelStatusText = "";
            ObjectModelFileCount = 0;
            ObjectModelDirtyCount = 0;
        }
    }

    // One archive member (manifest or object-model file).
    public sealed partial class ObjectModelEntry : ObservableObject
    {
        public string EntryName { get; }
        public string DisplayName { get; }
        public bool IsManifest { get; }
        public byte[] RawBytes { get; set; }
        public BXMLNodeViewModel? Tree { get; set; }

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            set => SetProperty(ref _isDirty, value);
        }

        public ObjectModelEntry(string entryName, string displayName, byte[] rawBytes, bool isManifest)
        {
            EntryName = entryName;
            DisplayName = displayName;
            RawBytes = rawBytes;
            IsManifest = isManifest;
        }
    }

    // A node in the ObjectModel group/file navigator.
    public sealed partial class ObjectModelNavNodeViewModel : ObservableObject
    {
        public ObservableCollection<ObjectModelNavNodeViewModel> Children { get; } = new();

        public ObjectModelEntry? Entry { get; }
        public ObjectModelEntry? LinkedEntry { get; set; }
        public int Depth { get; }
        public bool IsFile => Entry != null;
        public bool IsManifestRoot { get; set; }

        public string DisplayName { get; }
        public string Badge { get; }

        private string _subtitle = "";
        public string Subtitle
        {
            get => _subtitle;
            private set => SetProperty(ref _subtitle, value);
        }

        public bool IsDirty => Entry?.IsDirty ?? Children.Any(c => c.IsDirty);

        private readonly string _searchText;

        // File node.
        public ObjectModelNavNodeViewModel(ObjectModelEntry entry, int depth, string? manifestLabel = null)
        {
            Entry = entry;
            Depth = depth;
            DisplayName = string.IsNullOrEmpty(manifestLabel) || manifestLabel == entry.DisplayName
                ? entry.DisplayName
                : $"{entry.DisplayName}";
            Badge = entry.IsManifest ? "MANIFEST" : "OM";
            _searchText = $"{DisplayName} {entry.EntryName} {manifestLabel}";
            entry.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ObjectModelEntry.IsDirty))
                    OnPropertyChanged(nameof(IsDirty));
            };
        }

        // Group node.
        public ObjectModelNavNodeViewModel(string displayName, string badge, int depth)
        {
            DisplayName = displayName;
            Badge = badge;
            Depth = depth;
            _searchText = displayName;
        }

        private ObjectModelNavNodeViewModel(ObjectModelNavNodeViewModel source)
        {
            Entry = source.Entry;
            LinkedEntry = source.LinkedEntry;
            Depth = source.Depth;
            IsManifestRoot = source.IsManifestRoot;
            DisplayName = source.DisplayName;
            Badge = source.Badge;
            _searchText = source._searchText;
        }

        public ObjectModelNavNodeViewModel CloneShallow() => new(this);

        public int CountFiles()
        {
            int count = IsFile ? 1 : 0;
            foreach (var c in Children)
                count += c.CountFiles();
            return count;
        }

        public void RefreshSummary()
        {
            if (IsFile)
            {
                Subtitle = Entry!.EntryName;
            }
            else
            {
                int files = CountFiles();
                Subtitle = files == 1 ? "1 file" : $"{files:N0} files";
            }
        }

        public void RefreshDirtyRecursive()
        {
            OnPropertyChanged(nameof(IsDirty));
            foreach (var c in Children)
                c.RefreshDirtyRecursive();
        }

        public bool MatchesSearch(string term) =>
            _searchText.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
