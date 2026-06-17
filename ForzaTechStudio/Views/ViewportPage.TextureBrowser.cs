using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using ForzaTools.Bundles.Blobs;
using HelixToolkit.SharpDX.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ForzaTechStudio.Views
{
    public sealed partial class ViewportPage : Page
    {
        private readonly SwatchbinArchiveService _swatchbinArchiveService = new();
        private readonly SwatchbinService _viewportSwatchbinService = new();
        private readonly SwatchbinPreviewService _viewportSwatchbinPreviewService = new();
        private readonly SwatchbinConversionService _viewportSwatchbinConversionService = new();
        private readonly Dictionary<string, SwatchbinArchiveEntry> _viewportTextureLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, SwatchbinArchiveEntry> _viewportTextureHashLookup = new();
        private readonly Dictionary<string, TextureModel?> _viewportTextureModelCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SwatchbinArchiveEntry?> _viewportGameTextureEntryCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ViewportLooseTextureFile> _viewportLooseTextureFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _viewportTextureModelBuildFailures = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _viewportTextureLoadInFlight = new(StringComparer.OrdinalIgnoreCase);
        private bool _viewportTextureLookupDirty = true;
        private bool _isUpdatingTextureGameSelection;
        private ViewportGameTextureSourceItem? _selectedTextureGameSource;
        private bool _useLocalViewportTextures = true;
        private bool _useLibraryViewportTextures;
        private CancellationTokenSource _textureRefreshCts = new CancellationTokenSource();

        public ObservableCollection<ViewportGameTextureSourceItem> TextureGameSources { get; } = new();

        private void InvalidateViewportTextureLookup()
        {
            // Cancel any in-flight texture refresh so it doesn't update stale materials
            _textureRefreshCts.Cancel();
            _textureRefreshCts = new CancellationTokenSource();

            _viewportTextureLookupDirty = true;
            _viewportTextureLookup.Clear();
            _viewportTextureHashLookup.Clear();
            _viewportTextureModelCache.Clear();
            _viewportGameTextureEntryCache.Clear();
            _viewportTextureModelBuildFailures.Clear();
            _viewportTextureLoadInFlight.Clear();
            InvalidateViewportMaterialCache();
        }

        private void RegisterLooseViewportTextureFiles(IEnumerable<ViewportLooseTextureDropFile> textureFiles)
        {
            bool changed = false;

            foreach (var textureFile in textureFiles)
            {
                string texturePath = textureFile.Path;
                if (string.IsNullOrWhiteSpace(texturePath) || !File.Exists(texturePath))
                    continue;

                string fullPath = Path.GetFullPath(texturePath);
                string logicalPath = string.IsNullOrWhiteSpace(textureFile.LogicalPath)
                    ? Path.GetFileName(fullPath)
                    : textureFile.LogicalPath;

                if (_viewportLooseTextureFiles.TryAdd(fullPath, new ViewportLooseTextureFile(fullPath, logicalPath)))
                    changed = true;
            }

            if (changed)
                InvalidateViewportTextureLookup();
        }

        private void RefreshViewportTextureLookupFromLoadedRoots()
        {
            _viewportTextureLookupDirty = false;
            _viewportTextureLookup.Clear();
            _viewportTextureHashLookup.Clear();
            _viewportTextureModelCache.Clear();
            _viewportTextureModelBuildFailures.Clear();
            InvalidateViewportMaterialCache();

            if (!_useLocalViewportTextures)
                return;

            foreach (var zipNode in EnumerateViewerNodes<ZipNode>(ViewModel.Roots))
            {
                if (string.IsNullOrWhiteSpace(zipNode.FilePath) || !File.Exists(zipNode.FilePath))
                    continue;

                try
                {
                    foreach (var entry in _swatchbinArchiveService.IndexZipTextureEntries(zipNode.FilePath))
                        AddViewportTextureLookupEntry(entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Viewport/Textures] {zipNode.FilePath}: {ex.Message}");
                }
            }

            foreach (var looseTextureFile in _viewportLooseTextureFiles.Values)
            {
                if (!File.Exists(looseTextureFile.Path))
                    continue;

                try
                {
                    foreach (var entry in IndexLooseViewportTextureFile(looseTextureFile))
                        AddViewportTextureLookupEntry(entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Viewport/Textures] {looseTextureFile.Path}: {ex.Message}");
                }
            }

            // Scan sibling .swatchbin / .pb files next to loaded modelbins and folder roots
            IndexLocalFolderTextures();
        }

        private void IndexLocalFolderTextures()
        {
            var scannedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var modelBin in EnumerateViewerNodes<ModelBinNode>(ViewModel.Roots))
            {
                if (string.IsNullOrWhiteSpace(modelBin.FilePath) || !File.Exists(modelBin.FilePath))
                    continue;

                string? folder = Path.GetDirectoryName(modelBin.FilePath);
                if (string.IsNullOrWhiteSpace(folder) || !scannedFolders.Add(folder))
                    continue;

                IndexSiblingTextureFiles(folder);
            }

            foreach (var folderNode in EnumerateViewerNodes<FolderNode>(ViewModel.Roots))
            {
                if (string.IsNullOrWhiteSpace(folderNode.FolderPath) || !Directory.Exists(folderNode.FolderPath))
                    continue;

                string folder = Path.GetFullPath(folderNode.FolderPath);
                if (!scannedFolders.Add(folder))
                    continue;

                IndexSiblingTextureFiles(folder);
            }
        }

        private void IndexSiblingTextureFiles(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            string normalizedRoot = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            try
            {
                foreach (string filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(filePath);
                    if (!extension.Equals(".swatchbin", StringComparison.OrdinalIgnoreCase)
                        && !extension.Equals(".pb", StringComparison.OrdinalIgnoreCase)
                        && !extension.Equals(".materialbin", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string fullPath = Path.GetFullPath(filePath);
                    // Use relative path from the folder root so path-based lookups (e.g. "textures\car_paint.swatchbin") match
                    string logicalPath = fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                        ? fullPath.Substring(normalizedRoot.Length)
                        : Path.GetFileName(fullPath);

                    try
                    {
                        if (extension.Equals(".swatchbin", StringComparison.OrdinalIgnoreCase))
                        {
                            AddViewportTextureLookupEntry(new SwatchbinArchiveEntry
                            {
                                DisplayName = $"[Folder] {logicalPath}",
                                SourceArchivePath = fullPath,
                                LogicalPath = logicalPath,
                                DataLoader = () => File.ReadAllBytes(fullPath)
                            });
                        }
                        else if (extension.Equals(".pb", StringComparison.OrdinalIgnoreCase))
                        {
                            using var stream = File.OpenRead(fullPath);
                            foreach (var entry in SwatchbinArchiveService.ExtractTextureBundleEntries(
                                stream, fullPath, logicalPath,
                                $"[Folder] {logicalPath}",
                                logicalPath))
                            {
                                AddViewportTextureLookupEntry(entry);
                            }
                        }
                        else if (extension.Equals(".materialbin", StringComparison.OrdinalIgnoreCase))
                        {
                            // Index the materialbin itself so ResolveMaterialbinToSwatchbinEntry can load it
                            AddViewportTextureLookupEntry(new SwatchbinArchiveEntry
                            {
                                DisplayName = $"[Folder] {logicalPath}",
                                SourceArchivePath = fullPath,
                                LogicalPath = logicalPath,
                                DataLoader = () => File.ReadAllBytes(fullPath)
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Viewport/Textures] {fullPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Viewport/Textures] {folderPath}: {ex.Message}");
            }
        }

        private static IEnumerable<SwatchbinArchiveEntry> IndexLooseViewportTextureFile(ViewportLooseTextureFile textureFile)
        {
            string extension = Path.GetExtension(textureFile.Path);

            if (extension.Equals(".swatchbin", StringComparison.OrdinalIgnoreCase))
            {
                yield return new SwatchbinArchiveEntry
                {
                    DisplayName = $"[Folder] {textureFile.LogicalPath}",
                    SourceArchivePath = textureFile.Path,
                    LogicalPath = textureFile.LogicalPath,
                    DataLoader = () => File.ReadAllBytes(textureFile.Path)
                };

                yield break;
            }

            if (extension.Equals(".pb", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(textureFile.Path);
                foreach (var entry in SwatchbinArchiveService.ExtractTextureBundleEntries(
                    stream,
                    textureFile.Path,
                    textureFile.LogicalPath,
                    $"[Folder] {textureFile.LogicalPath}",
                    Path.GetFileName(textureFile.Path)))
                {
                    yield return entry;
                }
            }
        }

        private void AddViewportTextureLookupEntry(SwatchbinArchiveEntry entry)
        {
            foreach (string key in BuildViewportTextureLookupKeys(entry.LogicalPath))
            {
                _viewportTextureLookup.TryAdd(key, entry);
                _viewportTextureHashLookup.TryAdd(SwatchbinArchiveService.ComputeCrc32(key), entry);
                _viewportTextureHashLookup.TryAdd(SwatchbinArchiveService.ComputeCrc32(key.ToLowerInvariant()), entry);
            }
        }


        internal async Task StartViewportTextureRefreshAsync()
        {
            // Cancel any prior refresh
            _textureRefreshCts.Cancel();
            _textureRefreshCts = new CancellationTokenSource();
            var ct = _textureRefreshCts.Token;


            if (_viewportTextureLookupDirty)
                RefreshViewportTextureLookupFromLoadedRoots();

            // Only prewarm textures referenced by loaded model materials. The lookup still
            // indexes every zip entry, so uncommon textures can be resolved on demand later.
            var textureRequests = CollectViewportTextureRequestsToPrewarm();
            var uniqueLocalEntries = _useLocalViewportTextures
                ? CollectLocalTextureEntriesToPrewarm(textureRequests)
                : new List<SwatchbinArchiveEntry>();

            // Collect every texture path that rendered meshes need from the game library
            var gameTexturePaths = _useLibraryViewportTextures
                ? CollectGameTexturePathsToPrewarm(textureRequests)
                : new List<string>();

            // Snapshot keys already in the game entry cache so the background thread can skip them
            var alreadyCachedGameKeys = new HashSet<string>(_viewportGameTextureEntryCache.Keys, StringComparer.OrdinalIgnoreCase);

            // Snapshot source reference for the background thread
            var textureSource = _selectedTextureGameSource;

            if (uniqueLocalEntries.Count == 0 && gameTexturePaths.Count == 0)
            {
                UpdateMeshColors(SingleColorToggle?.IsChecked ?? false);
                return;
            }

            // Local dicts to accumulate background results; merged into the main caches on UI thread
            var newTextureModels = new ConcurrentDictionary<string, TextureModel?>(StringComparer.OrdinalIgnoreCase);
            var newGameEntries = new ConcurrentDictionary<string, SwatchbinArchiveEntry?>(StringComparer.OrdinalIgnoreCase);
            int totalWork = uniqueLocalEntries.Count + gameTexturePaths.Count;
            int completedWork = 0;


            await Task.Run(() =>
            {

                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount - 1, 6))
                };

                Parallel.ForEach(uniqueLocalEntries, parallelOptions, entry =>
                {
                    try
                    {
                        _ = entry.SwatchbinData; // decompress (SwatchbinArchiveEntry has its own lock)
                        string modelKey = BuildViewportTextureModelCacheKey(entry);
                        newTextureModels[modelKey] = TryBuildTextureModelFromEntry(entry);
                    }
                    catch { /* skip unreadable entries */ }
                    int done = Interlocked.Increment(ref completedWork);
                    DispatcherQueue.TryEnqueue(() => LoadingDetail = $"Loading textures... ({done} / {totalWork})");
                });

                if (ct.IsCancellationRequested) return;


                if (textureSource?.IsConfigured == true)
                {
                    foreach (string path in gameTexturePaths)
                    {
                        if (ct.IsCancellationRequested) return;
                        try
                        {
                            foreach (string candidate in BuildViewportGameTexturePathCandidates(textureSource.GameId, path))
                            {
                                string cacheKey = $"{textureSource.GameId}|{textureSource.RootPath}|{candidate}";
                                // Skip candidates already resolved in a prior pass or earlier this run
                                if (alreadyCachedGameKeys.Contains(cacheKey) || newGameEntries.ContainsKey(cacheKey))
                                    continue;

                                if (SwatchbinArchiveService.TryLoadGameTextureEntry(
                                    textureSource.RootPath, candidate, textureSource.GameId, out var loadedEntry))
                                {
                                    newGameEntries[cacheKey] = loadedEntry;
                                    if (loadedEntry != null)
                                    {
                                        try { _ = loadedEntry.SwatchbinData; } catch { }
                                        string modelKey = BuildViewportTextureModelCacheKey(loadedEntry);
                                        if (!newTextureModels.ContainsKey(modelKey))
                                            newTextureModels[modelKey] = TryBuildTextureModelFromEntry(loadedEntry);
                                    }
                                    break; // found 
                                }
                                else
                                {
                                    newGameEntries[cacheKey] = null;
                                }
                            }
                        }
                        catch { /* skip */ }
                        int done = Interlocked.Increment(ref completedWork);
                        DispatcherQueue.TryEnqueue(() => LoadingDetail = $"Loading textures... ({done} / {totalWork})");
                    }
                }
            }, ct);

            if (ct.IsCancellationRequested) return;


            foreach (var kvp in newTextureModels)
            {
                if (!_viewportTextureModelCache.ContainsKey(kvp.Key))
                    _viewportTextureModelCache[kvp.Key] = kvp.Value;
                if (kvp.Value == null)
                    _viewportTextureModelBuildFailures.Add(kvp.Key);
            }

            foreach (var kvp in newGameEntries)
                if (!_viewportGameTextureEntryCache.ContainsKey(kvp.Key))
                    _viewportGameTextureEntryCache[kvp.Key] = kvp.Value;


            UpdateMeshColors(SingleColorToggle?.IsChecked ?? false);
        }


        private List<ViewportTextureRequest> CollectViewportTextureRequestsToPrewarm()
        {
            var requests = new Dictionary<string, ViewportTextureRequest>(StringComparer.OrdinalIgnoreCase);

            void CollectFromMesh(ForzaGeometryData? data, ModelBinNode? modelBin)
            {
                if (data == null) return;
                var materialBlob = ResolveAssignedMaterial(data, modelBin);
                if (materialBlob?.Bundle == null) return;

                foreach (var paramBlob in materialBlob.Bundle.Blobs.OfType<MaterialShaderParameterBlob>())
                {
                    if (!IsViewportMaterialShaderParameterBlob(paramBlob)) continue;
                    foreach (var parameter in paramBlob.Parameters)
                    {
                        if (parameter.Type != ShaderParameterType.Texture2D) continue;
                        if (parameter.Value is TextureParameter tp)
                            AddTextureRequest(tp);
                    }
                }
            }

            void AddTextureRequest(TextureParameter textureParameter)
            {
                string path = textureParameter.Path ?? string.Empty;
                uint pathHash = textureParameter.PathHash;
                if (string.IsNullOrWhiteSpace(path) && pathHash == 0)
                    return;

                string key = !string.IsNullOrWhiteSpace(path)
                    ? NormalizeViewportTexturePath(path)
                    : $"#{pathHash:X8}";

                if (!requests.ContainsKey(key))
                    requests[key] = new ViewportTextureRequest(path, pathHash);
            }

            foreach (var modelBin in EnumerateViewerNodes<ModelBinNode>(ViewModel.Roots))
            {
                foreach (var meshNode in modelBin.Children.OfType<MeshNode>())
                    CollectFromMesh(meshNode.GeometryData, meshNode.ParentModelBin);

                foreach (var damageMeshNode in modelBin.Children.OfType<DamageMeshNode>())
                    CollectFromMesh(damageMeshNode.GeometryData, damageMeshNode.ParentModelBin);
            }

            foreach (var kvp in _carbinMaterialContextMap)
                CollectFromMesh(kvp.Value.Geometry, kvp.Value.ModelBin);

            return requests.Values.ToList();
        }

        private List<SwatchbinArchiveEntry> CollectLocalTextureEntriesToPrewarm(IEnumerable<ViewportTextureRequest> requests)
        {
            var entries = new HashSet<SwatchbinArchiveEntry>();

            foreach (var request in requests)
            {
                foreach (string key in BuildViewportTextureLookupKeys(request.Path))
                    if (_viewportTextureLookup.TryGetValue(key, out var entry))
                        entries.Add(entry);

                if (request.PathHash != 0 && _viewportTextureHashLookup.TryGetValue(request.PathHash, out var hashedEntry))
                    entries.Add(hashedEntry);
            }

            return entries.ToList();
        }

        private static List<string> CollectGameTexturePathsToPrewarm(IEnumerable<ViewportTextureRequest> requests)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var request in requests)
            {
                if (!string.IsNullOrWhiteSpace(request.Path))
                    paths.Add(request.Path);
            }

            return [.. paths];
        }


        private TextureModel? TryBuildTextureModelFromEntry(SwatchbinArchiveEntry entry)
        {
            try
            {
                byte[]? ddsData = _viewportSwatchbinConversionService.LoadRenderReadyDdsFromBytes(entry.SwatchbinData);
                if (ddsData == null || ddsData.Length == 0)
                    return null;

                var textureStream = new MemoryStream(ddsData, writable: false);
                return new TextureModel(textureStream, autoCloseStream: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Viewport/Textures] TryBuildTextureModelFromEntry {entry.DisplayName}: {ex.Message}");
                return null;
            }
        }

        private static string BuildViewportTextureModelCacheKey(SwatchbinArchiveEntry entry)
        {
            return $"{entry.SourceArchivePath}|{entry.LogicalPath}";
        }

        private static IEnumerable<string> BuildViewportTextureLookupKeys(string texturePath)
        {
            string normalized = NormalizeViewportTexturePath(texturePath);
            if (string.IsNullOrWhiteSpace(normalized))
                yield break;

            foreach (string path in ExpandViewportTexturePathCandidates(normalized))
            {
                yield return path;
                yield return path.Replace('/', '\\');
                yield return path.Replace('\\', '/');
            }
        }

        private static IEnumerable<string> ExpandViewportTexturePathCandidates(string normalizedPath)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            static void Add(HashSet<string> set, string value)
            {
                value = NormalizeViewportTexturePath(value);
                if (!string.IsNullOrWhiteSpace(value))
                    set.Add(value);
            }

            Add(candidates, normalizedPath);

            // Strip "media\" prefix if present (common in Game:\media\... references)
            if (normalizedPath.StartsWith("media\\", StringComparison.OrdinalIgnoreCase))
            {
                string stripped = normalizedPath[6..];
                Add(candidates, stripped);
                string strippedNoExt = Path.ChangeExtension(stripped, null) ?? stripped;
                Add(candidates, strippedNoExt);
                Add(candidates, strippedNoExt + ".swatchbin");
                Add(candidates, strippedNoExt + ".materialbin");
                Add(candidates, strippedNoExt + ".pb");
            }

            string withoutExtension = Path.ChangeExtension(normalizedPath, null) ?? normalizedPath;
            Add(candidates, withoutExtension);
            Add(candidates, withoutExtension + ".swatchbin");
            Add(candidates, withoutExtension + ".materialbin");
            Add(candidates, withoutExtension + ".pb");

            string fileName = Path.GetFileName(normalizedPath.Replace('/', '\\'));
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                Add(candidates, fileName);
                string fileNameWithoutExtension = Path.ChangeExtension(fileName, null) ?? fileName;
                Add(candidates, fileNameWithoutExtension);
                Add(candidates, fileNameWithoutExtension + ".swatchbin");
                Add(candidates, fileNameWithoutExtension + ".materialbin");
                Add(candidates, fileNameWithoutExtension + ".pb");
            }

            foreach (string candidate in candidates)
                yield return candidate;
        }

        private static string NormalizeViewportTexturePath(string texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
                return string.Empty;

            string normalized = texturePath.Trim().Replace('/', '\\');
            if (normalized.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Game:/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[6..];

            return normalized.TrimStart('\\', '/');
        }

        private SwatchbinArchiveEntry? ResolveViewportTextureEntry(TextureParameter textureParameter)
        {
            if (_useLocalViewportTextures)
            {
                if (_viewportTextureLookupDirty)
                    RefreshViewportTextureLookupFromLoadedRoots();

                foreach (string key in BuildViewportTextureLookupKeys(textureParameter.Path ?? string.Empty))
                {
                    if (_viewportTextureLookup.TryGetValue(key, out var entry))
                        return entry;
                }

                if (textureParameter.PathHash != 0 && _viewportTextureHashLookup.TryGetValue(textureParameter.PathHash, out var hashedEntry))
                    return hashedEntry;
            }

            if (_useLibraryViewportTextures)
            {
                var gameEntry = ResolveViewportGameTextureEntry(textureParameter.Path ?? string.Empty);
                if (gameEntry != null)
                    return gameEntry;
            }

            return null;
        }

        private SwatchbinArchiveEntry? ResolveViewportGameTextureEntry(string requestedPath)
        {
            var source = _selectedTextureGameSource;
            if (source?.IsConfigured != true || string.IsNullOrWhiteSpace(requestedPath))
                return null;

            foreach (string pathCandidate in BuildViewportGameTexturePathCandidates(source.GameId, requestedPath))
            {
                string cacheKey = $"{source.GameId}|{source.RootPath}|{pathCandidate}";
                if (_viewportGameTextureEntryCache.TryGetValue(cacheKey, out var cachedEntry))
                {
                    if (cachedEntry != null) return cachedEntry;
                    continue; // already confirmed not found via this candidate
                }

                if (SwatchbinArchiveService.TryLoadGameTextureEntry(source.RootPath, pathCandidate, source.GameId, out var loadedEntry))
                {
                    _viewportGameTextureEntryCache[cacheKey] = loadedEntry;
                    return loadedEntry;
                }

                _viewportGameTextureEntryCache[cacheKey] = null;
            }

            return null;
        }

        private static IEnumerable<string> BuildViewportGameTexturePathCandidates(string gameId, string requestedPath)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            static void Add(HashSet<string> set, string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                set.Add(value.Trim());
            }

            Add(candidates, requestedPath);

            string normalized = NormalizeViewportTexturePath(requestedPath);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                Add(candidates, normalized);
                if (string.IsNullOrWhiteSpace(Path.GetExtension(normalized)))
                    Add(candidates, normalized + ".swatchbin");
            }

            string fileName = Path.GetFileName(normalized.Replace('/', '\\'));
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                string? indexedPath = GameAssetDatabaseService.LookupFile(gameId, fileName);
                if (!string.IsNullOrWhiteSpace(indexedPath))
                    Add(candidates, indexedPath);
            }

            return candidates;
        }

        private async Task RefreshViewportGameTextureSourcesAsync()
        {
            string? selectedKey = _selectedTextureGameSource?.Key;
            var settings = await new SettingsService().LoadAsync();

            // Guard the flag before modifying the collection so that ComboBox SelectionChanged
            // events fired by Clear() / Add() are all suppressed during the rebuild.
            _isUpdatingTextureGameSelection = true;
            try
            {
                TextureGameSources.Clear();
                TextureGameSources.Add(ViewportGameTextureSourceItem.None);

                foreach (var game in ForzaGameCatalog.SetupGames)
                {
                    if (!settings.GamePaths.TryGetValue(game.GameId, out string? gamePath))
                        continue;

                    if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
                        continue;

                    TextureGameSources.Add(new ViewportGameTextureSourceItem(
                        game.GameId,
                        game.DisplayName,
                        gamePath,
                        GameAssetDatabaseService.DatabaseExists(game.GameId)));
                }

                var selected = !string.IsNullOrWhiteSpace(selectedKey)
                    ? TextureGameSources.FirstOrDefault(source => source.Key == selectedKey)
                    : (!string.IsNullOrWhiteSpace(settings.DefaultGameId)
                        ? TextureGameSources.FirstOrDefault(source => source.GameId == settings.DefaultGameId)
                        : null)
                      ?? TextureGameSources.FirstOrDefault();

                if (_useLibraryViewportTextures && selected?.IsConfigured != true)
                    selected = TextureGameSources.FirstOrDefault(source => source.IsConfigured) ?? selected;

                if (UseLocalTexturesCheckBox != null)
                    UseLocalTexturesCheckBox.IsChecked = _useLocalViewportTextures;
                if (UseLibraryTexturesCheckBox != null)
                    UseLibraryTexturesCheckBox.IsChecked = _useLibraryViewportTextures;
                if (TextureGameSourceCombo != null)
                {
                    // Set ItemsSource only once; ObservableCollection handles item change notifications
                    if (TextureGameSourceCombo.ItemsSource != TextureGameSources)
                        TextureGameSourceCombo.ItemsSource = TextureGameSources;
                    // Use SelectedItem - WinUI 3's SelectedValue/SelectedValuePath setter is unreliable
                    TextureGameSourceCombo.SelectedItem = selected ?? TextureGameSources.FirstOrDefault();
                    // Enable whenever there are items so the user can always open the combo
                    TextureGameSourceCombo.IsEnabled = TextureGameSources.Count > 0;
                }

                _selectedTextureGameSource = selected ?? TextureGameSources.FirstOrDefault();
            }
            finally
            {
                _isUpdatingTextureGameSelection = false;
            }
            UpdateTextureGameSourceStatus();
            InvalidateViewportTextureLookup();
            IsLoading = true;
            LoadingStatus = "Loading textures...";
            LoadingDetail = "";
            await StartViewportTextureRefreshAsync();
            IsLoading = false;
            LoadingStatus = "";
            LoadingDetail = "";
        }

        private async void TextureGameSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingTextureGameSelection)
                return;

            var newSource = e.AddedItems.OfType<ViewportGameTextureSourceItem>().FirstOrDefault()
                ?? TextureGameSourceCombo.SelectedItem as ViewportGameTextureSourceItem;


            if (newSource == null)
                return;

            _selectedTextureGameSource = newSource;
            InvalidateViewportTextureLookup();
            UpdateTextureGameSourceStatus();

            IsLoading = true;
            LoadingStatus = "Loading textures...";
            LoadingDetail = "";
            await StartViewportTextureRefreshAsync();
            IsLoading = false;
            LoadingStatus = "";
            LoadingDetail = "";
        }

        private async void ViewportTextureSourceCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _useLocalViewportTextures = UseLocalTexturesCheckBox?.IsChecked == true;
            _useLibraryViewportTextures = UseLibraryTexturesCheckBox?.IsChecked == true;

            if (_useLibraryViewportTextures && _selectedTextureGameSource?.IsConfigured != true)
            {
                _selectedTextureGameSource = TextureGameSources.FirstOrDefault(source => source.IsConfigured)
                    ?? TextureGameSources.FirstOrDefault();

                if (TextureGameSourceCombo != null && _selectedTextureGameSource != null)
                {
                    _isUpdatingTextureGameSelection = true;
                    TextureGameSourceCombo.SelectedItem = _selectedTextureGameSource;
                    _isUpdatingTextureGameSelection = false;
                }
            }

            InvalidateViewportTextureLookup();
            UpdateTextureGameSourceStatus();

            IsLoading = true;
            LoadingStatus = "Loading textures...";
            LoadingDetail = "";
            await StartViewportTextureRefreshAsync();
            IsLoading = false;
            LoadingStatus = "";
            LoadingDetail = "";
        }

        private void UpdateTextureGameSourceStatus()
        {
            if (TextureGameSourceStatusText == null)
                return;

            if (!_useLocalViewportTextures && !_useLibraryViewportTextures)
            {
                TextureGameSourceStatusText.Text = "Texture lookup disabled.";
                return;
            }

            var configuredCount = TextureGameSources.Count(source => source.IsConfigured);
            if (configuredCount == 0)
            {
                TextureGameSourceStatusText.Text = _useLocalViewportTextures
                    ? "Using local textures only; no configured game paths found in Setup."
                    : "No configured game paths found in Setup.";
                return;
            }

            var source = _selectedTextureGameSource;
            string localText = _useLocalViewportTextures ? "local zips" : "local off";
            string libraryText = _useLibraryViewportTextures
                ? (source?.IsConfigured == true ? source.DisplayName : "no game selected")
                : "library off";

            TextureGameSourceStatusText.Text = $"Texture sources: {localText}; {libraryText}.";
        }

        private TextureModel? ResolveViewportTextureModel(TextureParameter textureParameter)
        {
            var entry = ResolveViewportTextureEntry(textureParameter);
            if (entry == null)
                return null;

            string cacheKey = BuildViewportTextureModelCacheKey(entry);
            if (_viewportTextureModelCache.TryGetValue(cacheKey, out var cachedModel))
                return cachedModel;

            if (_viewportTextureModelBuildFailures.Contains(cacheKey))
                return null;

            // Texture not yet loaded. Return null for this frame 
            _ = LoadAndCacheTextureAsync(entry, cacheKey);
            return null;
        }


        private async Task LoadAndCacheTextureAsync(SwatchbinArchiveEntry entry, string cacheKey)
        {
            // Avoid duplicate loads for the same cache key
            if (_viewportTextureModelCache.ContainsKey(cacheKey)
                || _viewportTextureModelBuildFailures.Contains(cacheKey))
                return;

            // Avoid starting duplicate loads
            if (!_viewportTextureLoadInFlight.Add(cacheKey))
                return;

            try
            {
                TextureModel? model = null;
                try
                {
                    var ct = _textureRefreshCts.Token;
                    model = await Task.Run(() => TryBuildTextureModelFromEntry(entry), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Viewport/Textures] Async load failed {entry.DisplayName}: {ex.Message}");
                }

                // Cache the result on the UI thread and refresh materials
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        if (_viewportTextureLookupDirty)
                            return; // Session was closed; don't update stale caches

                        _viewportTextureModelCache.TryAdd(cacheKey, model);
                        if (model == null)
                            _viewportTextureModelBuildFailures.Add(cacheKey);

                        // Refresh materials so the newly loaded texture appears
                        InvalidateViewportMaterialCache();
                        UpdateMeshColors(SingleColorToggle?.IsChecked ?? false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Viewport/Textures] UI refresh error: {ex.Message}");
                    }
                });
            }
            finally
            {
                _viewportTextureLoadInFlight.Remove(cacheKey);
            }
        }

        private async void ShowZipTextures_Click(object sender, RoutedEventArgs e)
        {
            var zipNodes = EnumerateViewerNodes<ZipNode>(ViewModel.Roots)
                .Where(node => !string.IsNullOrWhiteSpace(node.FilePath) && File.Exists(node.FilePath))
                .ToList();

            if (zipNodes.Count == 0)
            {
                await ShowTextureInfoDialogAsync("No loaded car zip is available for texture browsing.");
                return;
            }

            ZipTexturesBtn.IsEnabled = false;

            try
            {
                var textureItems = new List<ViewportZipTextureItem>();

                foreach (var zipNode in zipNodes)
                {
                    var entries = await _swatchbinArchiveService.LoadZipTextureEntriesAsync(zipNode.FilePath);
                    foreach (var entry in entries)
                    {
                        textureItems.Add(await CreateTextureItemAsync(zipNode.Name, entry));
                    }
                }

                if (textureItems.Count == 0)
                {
                    await ShowTextureInfoDialogAsync("No .swatchbin or .pb texture bundles were found in the loaded car zip.");
                    return;
                }

                await ShowTextureBrowserDialogAsync(textureItems);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await ShowTextureInfoDialogAsync($"Failed to load zip textures: {ex.Message}");
            }
            finally
            {
                ZipTexturesBtn.IsEnabled = true;
            }
        }

        private async Task<ViewportZipTextureItem> CreateTextureItemAsync(string zipName, SwatchbinArchiveEntry entry)
        {
            try
            {
                using var stream = new MemoryStream(entry.SwatchbinData);
                SwatchbinInfo info = _viewportSwatchbinService.LoadSwatchbin(stream);
                info.FileName = Path.GetFileName(entry.LogicalPath);
                var preview = await _viewportSwatchbinPreviewService.CreatePreviewImageAsync(info, 144);

                return new ViewportZipTextureItem
                {
                    DisplayName = entry.DisplayName,
                    SourceText = zipName,
                    DetailText = $"{info.Width}x{info.Height} - {info.DxgiFormatName} - {info.MipLevels} mip(s)",
                    PreviewImage = preview
                };
            }
            catch (Exception ex)
            {
                return new ViewportZipTextureItem
                {
                    DisplayName = entry.DisplayName,
                    SourceText = zipName,
                    DetailText = $"Preview unavailable - {ex.Message}"
                };
            }
        }

        private async Task ShowTextureBrowserDialogAsync(IReadOnlyList<ViewportZipTextureItem> textureItems)
        {
            var texturePanel = new StackPanel
            {
                Spacing = 8,
                Padding = new Thickness(2)
            };

            foreach (var item in textureItems)
                texturePanel.Children.Add(CreateTextureRow(item));

            var scrollViewer = new ScrollViewer
            {
                Content = texturePanel,
                MaxHeight = 560,
                MinWidth = 640,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = $"Zip Textures ({textureItems.Count})",
                CloseButtonText = "Close",
                Content = scrollViewer
            };

            await dialog.ShowAsync();
        }

        private UIElement CreateTextureRow(ViewportZipTextureItem item)
        {
            var grid = new Grid
            {
                ColumnSpacing = 12,
                Padding = new Thickness(8),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"]
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var previewHost = new Border
            {
                Width = 96,
                Height = 96,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"]
            };

            if (item.PreviewImage != null)
            {
                previewHost.Child = new Image
                {
                    Source = item.PreviewImage,
                    Stretch = Stretch.Uniform
                };
            }
            else
            {
                previewHost.Child = new FontIcon
                {
                    Glyph = "\uEB9F",
                    FontSize = 28,
                    Opacity = 0.55
                };
            }

            grid.Children.Add(previewHost);

            var textPanel = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };

            textPanel.Children.Add(new TextBlock
            {
                Text = item.DisplayName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.WrapWholeWords
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = item.DetailText,
                Opacity = 0.75,
                TextWrapping = TextWrapping.WrapWholeWords
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = item.SourceText,
                FontSize = 11,
                Opacity = 0.6,
                TextWrapping = TextWrapping.WrapWholeWords
            });

            Grid.SetColumn(textPanel, 1);
            grid.Children.Add(textPanel);

            return grid;
        }

        private async Task ShowTextureInfoDialogAsync(string message)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Zip Textures",
                CloseButtonText = "Close",
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 380
                }
            };

            await dialog.ShowAsync();
        }

        private readonly struct ViewportTextureRequest
        {
            public string Path { get; }
            public uint PathHash { get; }

            public ViewportTextureRequest(string path, uint pathHash)
            {
                Path = path;
                PathHash = pathHash;
            }
        }

        private sealed record ViewportLooseTextureFile(string Path, string LogicalPath);
    }

    public sealed class ViewportZipTextureItem
    {
        public string DisplayName { get; init; } = string.Empty;
        public string SourceText { get; init; } = string.Empty;
        public string DetailText { get; init; } = string.Empty;
        public BitmapImage? PreviewImage { get; init; }
    }

    public sealed class ViewportGameTextureSourceItem
    {
        public static ViewportGameTextureSourceItem None { get; } = new("", "Loaded zips only", "", false);

        public string Key => IsConfigured ? GameId : "__loaded_zips_only__";
        public string GameId { get; }
        public string DisplayName { get; }
        public string RootPath { get; }
        public bool HasDatabase { get; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(GameId) && !string.IsNullOrWhiteSpace(RootPath);
        public string DetailText => IsConfigured
            ? HasDatabase ? "Setup path with asset database" : "Setup path"
            : "Use textures from loaded car zips";

        public ViewportGameTextureSourceItem(string gameId, string displayName, string rootPath, bool hasDatabase)
        {
            GameId = gameId;
            DisplayName = displayName;
            RootPath = rootPath;
            HasDatabase = hasDatabase;
        }
    }
}
