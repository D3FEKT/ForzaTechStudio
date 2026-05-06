using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;

namespace ForzaTechStudio.Services;

// Resolves materialbin/swatchbin paths in modelbin bundles via database-first lookup with zip-scan fallback.
public class PathConversionService
{
    // Tracks a single materialbin/swatchbin path reference inside a bundle.
    private class PathEntry
    {
        public string OriginalPath { get; set; }
        public string FileName { get; set; }
        public string Type { get; set; }
        public Action<string> SetPath { get; set; }
    }

    // Cached entry from a zip: maps filename (case-insensitive) to resolved Game:\ path.
    // Also stores full path mappings for exact-path lookups.
    private class ZipEntryCache
    {
        public Dictionary<string, string> FileMap { get; } = new(StringComparer.OrdinalIgnoreCase);
        // Maps full Game:\ paths (case-insensitive) to themselves for existence checks.
        public HashSet<string> PathSet { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // Holds a parsed bundle and all its materialbin/swatchbin path entries for bulk processing.
    public class BundlePathInfo
    {
        public Bundle Bundle { get; set; }
        public string OutputPath { get; set; }
        public string EntryName { get; set; }
        public List<string> FailedPaths { get; set; } = [];
    }

    public void ApplyPathMappings(Bundle bundle, IReadOnlyDictionary<string, string> mappings)
    {
        if (bundle == null || mappings == null || mappings.Count == 0)
            return;

        UpdateBundlePaths(bundle, mappings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase));
    }

    public static string ExtractRelativePath(string gamePath)
    {
        if (string.IsNullOrEmpty(gamePath)) return gamePath;

        if (gamePath.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase) ||
            gamePath.StartsWith("Game:/", StringComparison.OrdinalIgnoreCase))
        {
            return gamePath[6..];
        }

        return gamePath;
    }

    private static string GetFileNameFromGamePath(string gamePath)
    {
        string relative = ExtractRelativePath(gamePath);
        return Path.GetFileName(relative);
    }

    private static List<PathEntry> CollectBundlePaths(Bundle bundle, List<string> log)
    {
        var allPaths = new List<PathEntry>();
        int matiBlobIndex = 0;

        foreach (var blob in bundle.Blobs)
        {
            if (blob is MaterialBlob matBlob && matBlob.Bundle != null)
            {
                string matName = matBlob.GetMetadataByTag<NameMetadata>(BundleMetadata.TAG_METADATA_Name)?.Name ?? $"Material_{matiBlobIndex}";
                log?.Add($"  Processing material: {matName}");

                foreach (var innerBlob in matBlob.Bundle.Blobs)
                {
                    if (innerBlob is MaterialResourceBlob matiBlob && !string.IsNullOrEmpty(matiBlob.Path))
                    {
                        string ext = Path.GetExtension(matiBlob.Path).ToLowerInvariant();
                        if (ext is ".materialbin" or ".swatchbin")
                        {
                            log?.Add($"    MATI path: {matiBlob.Path}");
                            allPaths.Add(new PathEntry
                            {
                                OriginalPath = matiBlob.Path,
                                FileName = GetFileNameFromGamePath(matiBlob.Path),
                                Type = ext == ".materialbin" ? "materialbin" : "swatchbin",
                                SetPath = newPath => matiBlob.Path = newPath
                            });
                        }
                    }

                    if (innerBlob is MaterialShaderParameterBlob shaderBlob)
                    {
                        foreach (var param in shaderBlob.Parameters)
                        {
                            if (param.Type == ShaderParameterType.Texture2D && param.Value is TextureParameter texParam
                                && !string.IsNullOrEmpty(texParam.Path))
                            {
                                string ext2 = Path.GetExtension(texParam.Path).ToLowerInvariant();
                                if (ext2 is ".swatchbin" or ".materialbin")
                                {
                                    log?.Add($"    Texture param (0x{param.NameHash:X8}): {texParam.Path}");
                                    allPaths.Add(new PathEntry
                                    {
                                        OriginalPath = texParam.Path,
                                        FileName = GetFileNameFromGamePath(texParam.Path),
                                        Type = ext2 == ".swatchbin" ? "swatchbin" : "materialbin",
                                        SetPath = newPath => texParam.Path = newPath
                                    });
                                }
                            }
                        }
                    }
                }

                matiBlobIndex++;
            }
        }

        return allPaths;
    }

    // Builds a cache of all materialbin/swatchbin entries inside relevant zips.
    // Each zip is opened once and all entries indexed by filename.
    private static ZipEntryCache BuildTargetZipCache(string gameRootPath, List<string> log)
    {
        var cache = new ZipEntryCache();

        if (string.IsNullOrEmpty(gameRootPath) || !Directory.Exists(gameRootPath))
            return cache;

        string mediaPath = Path.Combine(gameRootPath, "media");
        if (!Directory.Exists(mediaPath))
            mediaPath = gameRootPath;

        var zipPaths = GetRelevantZipPaths(mediaPath).ToList();
        log?.Add($"  Found {zipPaths.Count} relevant zip(s) to index");

        foreach (var zipPath in zipPaths)
        {
            if (!File.Exists(zipPath)) continue;

            string zipRelativeToGame = Path.GetRelativePath(gameRootPath, zipPath);
            string zipFolder = Path.Combine(
                Path.GetDirectoryName(zipRelativeToGame) ?? "",
                Path.GetFileNameWithoutExtension(zipRelativeToGame));

            bool usedStandard = false;
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if (ext is not ".materialbin" and not ".swatchbin") continue;

                    string entryPath = entry.FullName.Replace('/', '\\');
                    string fullRelativePath = Path.Combine(zipFolder, entryPath);
                    string gamePath = $"Game:\\{fullRelativePath}";

                    cache.FileMap.TryAdd(entry.Name, gamePath);
                    cache.PathSet.Add(gamePath);
                }
                usedStandard = true;
            }
            catch { }

            if (usedStandard) continue;

            try
            {
                using var customZip = new CustomZipFile(zipPath);
                var entries = customZip.GetEntries();
                foreach (var entry in entries)
                {
                    if (entry.IsDirectory) continue;
                    string fileName = Path.GetFileName(entry.Name);
                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    if (ext is not ".materialbin" and not ".swatchbin") continue;

                    string entryPath = entry.Name.Replace('/', '\\');
                    string fullRelativePath = Path.Combine(zipFolder, entryPath);
                    string gamePath = $"Game:\\{fullRelativePath}";

                    cache.FileMap.TryAdd(fileName, gamePath);
                    cache.PathSet.Add(gamePath);
                }
            }
            catch { }
        }

        log?.Add($"  Indexed {cache.FileMap.Count} materialbin/swatchbin file(s) from zips");
        return cache;
    }

    // Resolves a filename by checking database first, then loose files, then zip cache.
    private static string FindFile(string fileName, string targetGameId, string gameRootPath, ZipEntryCache zipCache)
    {
        // 1. Database lookup (instant)
        if (!string.IsNullOrEmpty(targetGameId))
        {
            string dbResult = GameAssetDatabaseService.LookupFile(targetGameId, fileName);
            if (dbResult != null)
                return dbResult;
        }

        // 2. Loose file search
        if (!string.IsNullOrEmpty(gameRootPath) && Directory.Exists(gameRootPath))
        {
            string mediaPath = Path.Combine(gameRootPath, "media");
            if (!Directory.Exists(mediaPath))
                mediaPath = gameRootPath;

            try
            {
                var looseFiles = Directory.GetFiles(mediaPath, fileName, SearchOption.AllDirectories);
                if (looseFiles.Length > 0)
                {
                    string relativePath = Path.GetRelativePath(gameRootPath, looseFiles[0]);
                    return $"Game:\\{relativePath}";
                }
            }
            catch { }
        }

        // 3. Zip cache fallback
        if (zipCache != null && zipCache.FileMap.TryGetValue(fileName, out string cachedPath))
            return cachedPath;

        return null;
    }

    // Checks if an original Game:\ path exists in the target game.
    // Uses database path lookup first, then loose file check, then zip cache full-path check.
    // Returns true if the file exists at the exact same path in the target game.
    private static bool CheckOriginalPathExists(string originalGamePath, string targetGameId, string gameRootPath, ZipEntryCache zipCache)
    {
        if (string.IsNullOrEmpty(originalGamePath))
            return false;

        // 1. Database lookup by full path (instant)
        if (!string.IsNullOrEmpty(targetGameId))
        {
            string dbResult = GameAssetDatabaseService.LookupByPath(targetGameId, originalGamePath);
            if (dbResult != null)
                return true;
        }

        // 2. Loose file check
        if (!string.IsNullOrEmpty(gameRootPath) && Directory.Exists(gameRootPath))
        {
            string relativePath = ExtractRelativePath(originalGamePath);
            if (!string.IsNullOrEmpty(relativePath))
            {
                string fullPath = Path.Combine(gameRootPath, relativePath);
                if (File.Exists(fullPath))
                    return true;
            }
        }

        // 3. Zip cache full-path check
        if (zipCache != null && zipCache.PathSet.Contains(originalGamePath))
            return true;

        return false;
    }

    private static IEnumerable<string> GetRelevantZipPaths(string mediaPath)
    {
        var results = new List<string>();

        string[] priorityZipNames = ["materials.zip", "textures.zip"];

        try
        {
            foreach (var zipName in priorityZipNames)
            {
                var found = Directory.GetFiles(mediaPath, zipName, SearchOption.AllDirectories);
                results.AddRange(found);
            }
        }
        catch { }

        if (results.Count == 0)
        {
            try
            {
                var libraryDirs = Directory.GetDirectories(mediaPath, "_library", SearchOption.AllDirectories);
                foreach (var libDir in libraryDirs)
                {
                    try { results.AddRange(Directory.GetFiles(libDir, "*.zip", SearchOption.AllDirectories)); }
                    catch { }
                }
            }
            catch { }
        }

        if (results.Count == 0)
        {
            try
            {
                string carsPath = Path.Combine(mediaPath, "cars");
                if (Directory.Exists(carsPath))
                    results.AddRange(Directory.GetFiles(carsPath, "*.zip", SearchOption.AllDirectories));
            }
            catch { }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // Converts materialbin/swatchbin paths in a single bundle for the target game.
    // First checks if the original path exists in the target game (preserves it if so).
    // Falls back to filename-based resolution via database or zip cache scanning.
    public PathConversionResult ConvertMaterialPaths(Bundle bundle, ForzaGameTarget target,
        string targetGamePath, string sourceGamePath, ConversionResult convResult)
    {
        string targetGameId = ConversionService.GetGameSettingsId(target);
        bool hasDb = GameAssetDatabaseService.DatabaseExists(targetGameId);

        var pathResult = new PathConversionResult();

        pathResult.Log.Add($"Scanning bundle for materialbin/swatchbin paths...");
        pathResult.Log.Add($"Target game path: {targetGamePath}");
        if (hasDb)
            pathResult.Log.Add($"Using database for {targetGameId} (fast lookup)");
        else
            pathResult.Log.Add($"No database for {targetGameId} � using zip scanning fallback");

        var allPaths = CollectBundlePaths(bundle, pathResult.Log);

        var uniqueByFileName = allPaths
            .GroupBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        pathResult.TotalPaths = allPaths.Count;

        if (allPaths.Count == 0)
        {
            pathResult.Log.Add("No materialbin/swatchbin paths found in bundle");
            return pathResult;
        }

        pathResult.Log.Add($"Found {allPaths.Count} path reference(s) ({uniqueByFileName.Count} unique file(s)) to convert");

        // Build zip cache only if no database exists (fallback)
        ZipEntryCache zipCache = null;
        if (!hasDb)
        {
            pathResult.Log.Add($"--- Building target game zip cache ---");
            zipCache = BuildTargetZipCache(targetGamePath, pathResult.Log);
        }

        // Phase 1: Check if original paths exist in target game (preserve if they do)
        // Group by unique original path (case-insensitive) for efficiency
        var uniqueOriginalPaths = allPaths
            .GroupBy(p => p.OriginalPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var preservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int preservedCount = 0;

        if (hasDb)
        {
            // Bulk check original paths in database
            var originalPathsList = uniqueOriginalPaths.Select(g => g.Key).ToList();
            var existingPaths = GameAssetDatabaseService.LookupByPaths(targetGameId, originalPathsList);
            foreach (var kvp in existingPaths)
                preservedPaths.Add(kvp.Key);

            // Also check loose files for paths not found in DB
            foreach (var group in uniqueOriginalPaths)
            {
                if (!preservedPaths.Contains(group.Key))
                {
                    string relativePath = ExtractRelativePath(group.Key);
                    if (!string.IsNullOrEmpty(relativePath) && !string.IsNullOrEmpty(targetGamePath))
                    {
                        string fullPath = Path.Combine(targetGamePath, relativePath);
                        if (File.Exists(fullPath))
                            preservedPaths.Add(group.Key);
                    }
                }
            }
        }
        else
        {
            // Check each original path against loose files and zip cache
            foreach (var group in uniqueOriginalPaths)
            {
                if (CheckOriginalPathExists(group.Key, null, targetGamePath, zipCache))
                    preservedPaths.Add(group.Key);
            }
        }

        // Mark preserved paths as successful
        foreach (var group in uniqueOriginalPaths)
        {
            if (preservedPaths.Contains(group.Key))
            {
                pathResult.Log.Add($"  [KEEP] {GetFileNameFromGamePath(group.Key)}");
                pathResult.Log.Add($"         Path exists in target: {group.Key}");

                foreach (var entry in group)
                {
                    // Path stays the same � no change needed
                    pathResult.SuccessfulPaths++;
                    preservedCount++;
                }
            }
        }

        if (preservedCount > 0)
            pathResult.Log.Add($"Preserved {preservedCount} path(s) that already exist in target game");

        // Phase 2: Resolve remaining paths by filename (only those not preserved)
        // Collect entries that still need resolution
        var needsResolution = allPaths
            .Where(p => !preservedPaths.Contains(p.OriginalPath))
            .GroupBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (needsResolution.Count > 0)
        {
            Dictionary<string, string> resolvedPaths;
            if (hasDb)
            {
                var fileNames = needsResolution.Select(g => g.Key).ToList();
                resolvedPaths = GameAssetDatabaseService.LookupFiles(targetGameId, fileNames);
                pathResult.Log.Add($"Database resolved {resolvedPaths.Count}/{fileNames.Count} remaining file(s)");

                // For any not found in database, try loose files
                foreach (var group in needsResolution)
                {
                    if (!resolvedPaths.ContainsKey(group.Key))
                    {
                        string looseResult = FindFile(group.Key, null, targetGamePath, null);
                        if (looseResult != null)
                            resolvedPaths[group.Key] = looseResult;
                    }
                }
            }
            else
            {
                // Resolve one by one using the zip cache
                resolvedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in needsResolution)
                {
                    string result = FindFile(group.Key, null, targetGamePath, zipCache);
                    if (result != null)
                        resolvedPaths[group.Key] = result;
                }
            }

            pathResult.Log.Add($"--- Applying resolved paths ---");

            foreach (var group in needsResolution)
            {
                string fileName = group.Key;
                string sampleOriginalPath = group.First().OriginalPath;

                if (resolvedPaths.TryGetValue(fileName, out string resolvedPath))
                {
                    pathResult.Log.Add($"  [OK] {fileName}");
                    pathResult.Log.Add($"       Old: {sampleOriginalPath}");
                    pathResult.Log.Add($"       New: {resolvedPath}");

                    foreach (var entry in group)
                    {
                        entry.SetPath(resolvedPath);
                        pathResult.SuccessfulPaths++;
                    }
                }
                else
                {
                    pathResult.Log.Add($"  [MISS] {fileName} � not found in target game");

                    pathResult.UnresolvedAssets.Add(new UnresolvedAssetReference
                    {
                        FileName = fileName,
                        Type = group.First().Type,
                        SampleOriginalPath = sampleOriginalPath,
                        OriginalPaths = group.Select(entry => entry.OriginalPath).ToList(),
                    });

                    foreach (var entry in group)
                    {
                        pathResult.FailedPathList.Add(entry.OriginalPath);
                        pathResult.FailedPaths++;
                    }
                }
            }
        }

        if (pathResult.FailedPaths > 0)
            pathResult.Log.Add($"--- {pathResult.FailedPaths} path(s) not found in target game ---");

        return pathResult;
    }

    // Bulk path conversion: collects paths from multiple bundles, resolves them all in one pass,
    // then applies results back. Returns bundles that have failed (unresolved) paths.
    // First checks if original paths exist in the target game to avoid unnecessary remapping.
    public (List<BundlePathInfo> failedBundles, int totalPaths, int resolvedPaths, int failedPaths)
        ConvertMaterialPathsBulk(
            List<(Bundle bundle, string outputPath, string entryName)> bundles,
            ForzaGameTarget target, string targetGamePath, string sourceGamePath,
            List<string> log)
    {
        string targetGameId = ConversionService.GetGameSettingsId(target);
        bool hasDb = GameAssetDatabaseService.DatabaseExists(targetGameId);

        if (hasDb)
            log.Add($"Using database for {targetGameId} (fast bulk lookup)");
        else
            log.Add($"No database for {targetGameId} � using zip scanning fallback");

        // Phase 1: Collect all paths from all bundles
        var allBundleEntries = new List<(int bundleIdx, List<PathEntry> paths)>();
        var allUniqueFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allUniqueOriginalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < bundles.Count; i++)
        {
            var paths = CollectBundlePaths(bundles[i].bundle, null);
            allBundleEntries.Add((i, paths));
            foreach (var p in paths)
            {
                allUniqueFileNames.Add(p.FileName);
                allUniqueOriginalPaths.Add(p.OriginalPath);
            }
        }

        int totalPaths = allBundleEntries.Sum(e => e.paths.Count);
        log.Add($"Collected {totalPaths} path reference(s) ({allUniqueFileNames.Count} unique file(s), {allUniqueOriginalPaths.Count} unique path(s)) from {bundles.Count} modelbin(s)");

        if (allUniqueFileNames.Count == 0)
            return ([], totalPaths, 0, 0);

        // Phase 1.5: Check which original paths already exist in the target game
        var preservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ZipEntryCache zipCache = null;

        if (hasDb)
        {
            var existingPaths = GameAssetDatabaseService.LookupByPaths(targetGameId, allUniqueOriginalPaths);
            foreach (var kvp in existingPaths)
                preservedPaths.Add(kvp.Key);

            // Also check loose files for paths not found in DB
            if (!string.IsNullOrEmpty(targetGamePath) && Directory.Exists(targetGamePath))
            {
                foreach (var originalPath in allUniqueOriginalPaths)
                {
                    if (!preservedPaths.Contains(originalPath))
                    {
                        string relativePath = ExtractRelativePath(originalPath);
                        if (!string.IsNullOrEmpty(relativePath))
                        {
                            string fullPath = Path.Combine(targetGamePath, relativePath);
                            if (File.Exists(fullPath))
                                preservedPaths.Add(originalPath);
                        }
                    }
                }
            }
        }
        else
        {
            log.Add($"Building zip cache...");
            zipCache = BuildTargetZipCache(targetGamePath, log);

            foreach (var originalPath in allUniqueOriginalPaths)
            {
                if (CheckOriginalPathExists(originalPath, null, targetGamePath, zipCache))
                    preservedPaths.Add(originalPath);
            }
        }

        if (preservedPaths.Count > 0)
            log.Add($"Preserved {preservedPaths.Count} original path(s) that already exist in target game");

        // Phase 2: Bulk resolve remaining unique filenames (only those not preserved)
        var needsResolutionFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, paths) in allBundleEntries)
        {
            foreach (var p in paths)
            {
                if (!preservedPaths.Contains(p.OriginalPath))
                    needsResolutionFileNames.Add(p.FileName);
            }
        }

        Dictionary<string, string> resolvedPaths;
        if (needsResolutionFileNames.Count > 0)
        {
            if (hasDb)
            {
                resolvedPaths = GameAssetDatabaseService.LookupFiles(targetGameId, needsResolutionFileNames);
                log.Add($"Database resolved {resolvedPaths.Count}/{needsResolutionFileNames.Count} remaining file(s)");

                // Fallback for any not in database: loose files
                foreach (var fn in needsResolutionFileNames)
                {
                    if (!resolvedPaths.ContainsKey(fn))
                    {
                        string looseResult = FindFile(fn, null, targetGamePath, null);
                        if (looseResult != null)
                            resolvedPaths[fn] = looseResult;
                    }
                }
            }
            else
            {
                if (zipCache == null)
                {
                    log.Add($"Building zip cache...");
                    zipCache = BuildTargetZipCache(targetGamePath, log);
                }
                resolvedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var fn in needsResolutionFileNames)
                {
                    string result = FindFile(fn, null, targetGamePath, zipCache);
                    if (result != null)
                        resolvedPaths[fn] = result;
                }
            }
        }
        else
        {
            resolvedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        log.Add($"Total resolved: {resolvedPaths.Count}/{needsResolutionFileNames.Count} (plus {preservedPaths.Count} preserved)");

        // Phase 3: Apply resolved paths to all bundles and collect failures
        int totalResolved = 0;
        int totalFailed = 0;
        var failedBundles = new List<BundlePathInfo>();

        for (int i = 0; i < allBundleEntries.Count; i++)
        {
            var (bundleIdx, paths) = allBundleEntries[i];
            var bundleInfo = new BundlePathInfo
            {
                Bundle = bundles[bundleIdx].bundle,
                OutputPath = bundles[bundleIdx].outputPath,
                EntryName = bundles[bundleIdx].entryName
            };

            foreach (var entry in paths)
            {
                // Check if original path is preserved (exists in target game)
                if (preservedPaths.Contains(entry.OriginalPath))
                {
                    // Keep original path � no change needed
                    totalResolved++;
                }
                else if (resolvedPaths.TryGetValue(entry.FileName, out string resolved))
                {
                    entry.SetPath(resolved);
                    totalResolved++;
                }
                else
                {
                    bundleInfo.FailedPaths.Add(entry.OriginalPath);
                    totalFailed++;
                }
            }

            if (bundleInfo.FailedPaths.Count > 0)
                failedBundles.Add(bundleInfo);
        }

        log.Add($"Applied: {totalResolved} resolved, {totalFailed} failed across {bundles.Count} bundle(s)");
        if (failedBundles.Count > 0)
            log.Add($"{failedBundles.Count} bundle(s) have unresolved paths");

        return (failedBundles, totalPaths, totalResolved, totalFailed);
    }

    // Extracts a specific file from a game's directory (loose files or zips) to a destination path.
    private static bool ExtractFileFromSource(string gameRootPath, string fileName, string destinationPath, List<string> log)
    {
        if (string.IsNullOrEmpty(gameRootPath) || !Directory.Exists(gameRootPath))
            return false;

        string mediaPath = Path.Combine(gameRootPath, "media");
        if (!Directory.Exists(mediaPath))
            mediaPath = gameRootPath;

        // 1. Loose files
        try
        {
            var looseFiles = Directory.GetFiles(mediaPath, fileName, SearchOption.AllDirectories);
            if (looseFiles.Length > 0)
            {
                string destDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(looseFiles[0], destinationPath, true);
                log?.Add($"    Copied from loose file");
                return true;
            }
        }
        catch { }

        // 2. Relevant zips
        try
        {
            var zipPaths = GetRelevantZipPaths(mediaPath);

            foreach (var zipPath in zipPaths)
            {
                if (!File.Exists(zipPath)) continue;

                // Standard .NET ZipArchive
                try
                {
                    using var archive = ZipFile.OpenRead(zipPath);
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            string destDir = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                                Directory.CreateDirectory(destDir);

                            entry.ExtractToFile(destinationPath, true);
                            log?.Add($"    Extracted from {Path.GetFileName(zipPath)}");
                            return true;
                        }
                    }
                    continue;
                }
                catch { }

                // Fallback: CustomZipFile
                try
                {
                    using var customZip = new CustomZipFile(zipPath);
                    var entries = customZip.GetEntries();
                    foreach (var entry in entries)
                    {
                        if (entry.IsDirectory) continue;
                        string entryFileName = Path.GetFileName(entry.Name);
                        if (entryFileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            string destDir = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                                Directory.CreateDirectory(destDir);

                            byte[] data = customZip.ExtractToMemory(entry);
                            File.WriteAllBytes(destinationPath, data);
                            log?.Add($"    Extracted from Forza zip {Path.GetFileName(zipPath)}");
                            return true;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return false;
    }

    // Handles unsuccessful paths by copying source files to a "converted" folder
    // and updating paths to Game:\media\cars\{carZipName}\converted\{filename}.
    // The convertedDir parameter allows callers to control where the folder is placed.
    public string HandleUnsuccessfulPaths(Bundle bundle, ForzaGameTarget target,
        string targetGamePath, string sourceGamePath, List<string> failedPaths,
        string outputDirectory, string carZipName, List<string> log)
    {
        if (failedPaths == null || failedPaths.Count == 0) return null;

        var uniqueByFileName = failedPaths
            .GroupBy(p => GetFileNameFromGamePath(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

        log?.Add($"Processing {uniqueByFileName.Count} unique file(s) to copy...");

        string convertedDir = Path.Combine(outputDirectory, "converted");
        Directory.CreateDirectory(convertedDir);
        log?.Add($"Output folder: {convertedDir}");

        var convertedMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int copiedCount = 0;
        int keptOriginalCount = 0;

        foreach (var group in uniqueByFileName)
        {
            string fileName = group.Key;

            string newGamePath = $@"Game:\media\cars\{carZipName}\converted\{fileName}";

            log?.Add($"  {fileName}:");
            if (group.Count() > 1)
                log?.Add($"    ({group.Count()} path references with different casing)");

            string destPath = Path.Combine(convertedDir, fileName);
            bool found = false;

            if (!string.IsNullOrEmpty(sourceGamePath))
            {
                string sourceRelative = ExtractRelativePath(group.First());
                string sourceFullPath = Path.Combine(sourceGamePath, sourceRelative);

                if (File.Exists(sourceFullPath))
                {
                    File.Copy(sourceFullPath, destPath, true);
                    log?.Add($"    Copied from loose file");
                    found = true;
                }

                if (!found)
                {
                    var extractLog = new List<string>();
                    found = ExtractFileFromSource(sourceGamePath, fileName, destPath, extractLog);
                    foreach (var el in extractLog)
                        log?.Add(el);
                }
            }

            if (found)
            {
                copiedCount++;
                log?.Add($"    New path: {newGamePath}");
                foreach (var originalPath in group)
                    convertedMappings[originalPath] = newGamePath;
            }
            else
            {
                log?.Add($"    Source file not found, keeping original path unchanged");
                keptOriginalCount++;
            }
        }

        log?.Add($"Files copied: {copiedCount}, Kept original path: {keptOriginalCount}");

        UpdateBundlePaths(bundle, convertedMappings);
        return convertedDir;
    }

    // Batch-optimized version of HandleUnsuccessfulPaths that processes multiple bundles at once.
    // Collects all unique files to extract, opens each source zip only once, and copies all files
    // in a single pass for significantly improved speed.
    public string HandleUnsuccessfulPathsBatch(
        List<BundlePathInfo> failedBundles, ForzaGameTarget target,
        string targetGamePath, string sourceGamePath,
        string outputDirectory, string carZipName, List<string> log)
    {
        if (failedBundles == null || failedBundles.Count == 0) return null;

        // Phase 1: Collect ALL unique filenames across all bundles
        var allFailedPaths = new Dictionary<string, List<(BundlePathInfo bundleInfo, string originalPath)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var bundleInfo in failedBundles)
        {
            foreach (var failedPath in bundleInfo.FailedPaths)
            {
                string fileName = GetFileNameFromGamePath(failedPath);
                if (!allFailedPaths.TryGetValue(fileName, out var list))
                {
                    list = [];
                    allFailedPaths[fileName] = list;
                }
                list.Add((bundleInfo, failedPath));
            }
        }

        log?.Add($"Batch processing {allFailedPaths.Count} unique file(s) across {failedBundles.Count} bundle(s)...");

        string convertedDir = Path.Combine(outputDirectory, "converted");
        Directory.CreateDirectory(convertedDir);

        // Phase 2: Try loose files first (fast)
        var pendingFromZip = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // fileName -> destPath
        var copiedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int copiedCount = 0;
        int keptOriginalCount = 0;

        foreach (var (fileName, refs) in allFailedPaths)
        {
            string destPath = Path.Combine(convertedDir, fileName);
            bool found = false;

            if (!string.IsNullOrEmpty(sourceGamePath))
            {
                // Try loose file using first reference's relative path
                string sourceRelative = ExtractRelativePath(refs[0].originalPath);
                string sourceFullPath = Path.Combine(sourceGamePath, sourceRelative);

                if (File.Exists(sourceFullPath))
                {
                    File.Copy(sourceFullPath, destPath, true);
                    log?.Add($"  {fileName}: Copied from loose file");
                    found = true;
                    copiedCount++;
                    copiedFileNames.Add(fileName);
                }
            }

            if (!found)
                pendingFromZip[fileName] = destPath;
        }

        // Phase 3: Batch extract from source zips � open each zip only once
        if (pendingFromZip.Count > 0 && !string.IsNullOrEmpty(sourceGamePath))
        {
            log?.Add($"  Extracting {pendingFromZip.Count} file(s) from source zip archives...");

            string mediaPath = Path.Combine(sourceGamePath, "media");
            if (!Directory.Exists(mediaPath))
                mediaPath = sourceGamePath;

            var zipPaths = GetRelevantZipPaths(mediaPath);
            var remaining = new HashSet<string>(pendingFromZip.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (var zipPath in zipPaths)
            {
                if (remaining.Count == 0) break;
                if (!File.Exists(zipPath)) continue;

                // Try standard ZipArchive first
                bool processed = false;
                try
                {
                    using var archive = ZipFile.OpenRead(zipPath);
                    foreach (var entry in archive.Entries)
                    {
                        if (remaining.Count == 0) break;
                        if (string.IsNullOrEmpty(entry.Name)) continue;

                        if (remaining.Contains(entry.Name))
                        {
                            string destPath = pendingFromZip[entry.Name];
                            entry.ExtractToFile(destPath, true);
                            remaining.Remove(entry.Name);
                            copiedCount++;
                            copiedFileNames.Add(entry.Name);
                            log?.Add($"  {entry.Name}: Extracted from {Path.GetFileName(zipPath)}");
                        }
                    }
                    processed = true;
                }
                catch { }

                // Fallback: CustomZipFile
                if (!processed)
                {
                    try
                    {
                        using var customZip = new CustomZipFile(zipPath);
                        var entries = customZip.GetEntries();
                        foreach (var entry in entries)
                        {
                            if (remaining.Count == 0) break;
                            if (entry.IsDirectory) continue;
                            string entryFileName = Path.GetFileName(entry.Name);

                            if (remaining.Contains(entryFileName))
                            {
                                string destPath = pendingFromZip[entryFileName];
                                byte[] data = customZip.ExtractToMemory(entry);
                                File.WriteAllBytes(destPath, data);
                                remaining.Remove(entryFileName);
                                copiedCount++;
                                copiedFileNames.Add(entryFileName);
                                log?.Add($"  {entryFileName}: Extracted from {Path.GetFileName(zipPath)} (custom)");
                            }
                        }
                    }
                    catch { }
                }
            }

            // Count remaining as kept-original (source not found)
            foreach (var fn in remaining)
            {
                log?.Add($"  {fn}: Source not found, keeping original path unchanged");
                keptOriginalCount++;
            }
        }
        else if (pendingFromZip.Count > 0)
        {
            keptOriginalCount += pendingFromZip.Count;
            foreach (var fn in pendingFromZip.Keys)
                log?.Add($"  {fn}: No source game path, keeping original path unchanged");
        }

        log?.Add($"Files copied: {copiedCount}, Kept original path: {keptOriginalCount}");

        // Phase 4: Build path mappings and apply to all bundles
        // Only remap paths where the source file was actually copied
        foreach (var bundleInfo in failedBundles)
        {
            var convertedMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var failedPath in bundleInfo.FailedPaths)
            {
                string fileName = GetFileNameFromGamePath(failedPath);
                if (copiedFileNames.Contains(fileName))
                {
                    string newGamePath = $@"Game:\media\cars\{carZipName}\converted\{fileName}";
                    convertedMappings[failedPath] = newGamePath;
                }
                // If not copied, don't add to mappings � original path stays unchanged
            }
            if (convertedMappings.Count > 0)
                UpdateBundlePaths(bundleInfo.Bundle, convertedMappings);
        }

        return convertedDir;
    }

    private static void UpdateBundlePaths(Bundle bundle, Dictionary<string, string> convertedMappings)
    {
        foreach (var blob in bundle.Blobs)
        {
            if (blob is MaterialBlob matBlob && matBlob.Bundle != null)
            {
                foreach (var innerBlob in matBlob.Bundle.Blobs)
                {
                    if (innerBlob is MaterialResourceBlob matiBlob &&
                        !string.IsNullOrEmpty(matiBlob.Path) &&
                        convertedMappings.TryGetValue(matiBlob.Path, out string newPath))
                    {
                        matiBlob.Path = newPath;
                    }

                    if (innerBlob is MaterialShaderParameterBlob shaderBlob)
                    {
                        foreach (var param in shaderBlob.Parameters)
                        {
                            if (param.Type == ShaderParameterType.Texture2D && param.Value is TextureParameter texParam
                                && !string.IsNullOrEmpty(texParam.Path)
                                && convertedMappings.TryGetValue(texParam.Path, out string newTexPath))
                            {
                                texParam.Path = newTexPath;
                            }
                        }
                    }
                }
            }
        }
    }
}
