using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using ForzaTools.Shared;

namespace ForzaTechStudio.Services;

// Service that handles converting modelbin bundle files between Forza game versions.
// VLay conversion is source-aware: it preserves the source layout's semantic set and only
// converts formats and adds elements that are genuinely required by the target game.
public class ModelbinConversionService
{
    private readonly Dictionary<string, uint?> _materialRequirementCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _materialShaderPathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint?> _shaderRequirementCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _assetZipPathCache = new(StringComparer.OrdinalIgnoreCase);

    // Describes a single element in a VLay slot 1 layout.
    public class VLayElementInfo
    {
        public string SemanticName { get; set; }
        public short SemanticIndex { get; set; }
        public DXGI_FORMAT Format { get; set; }
        public DXGI_FORMAT PackedFormat { get; set; }
        public int ByteSize { get; set; }
    }

    // Describes a format conversion needed for a single element when formats differ
    // between source and target (e.g. FH2's FLOAT16x4 NORMAL vs FM6+'s SNORM16x2).
    public enum VLayFormatConversion
    {
        None,
        // R16G16B16A16_FLOAT (8 bytes) ? R16G16_SNORM (4 bytes)
        Float16x4_To_Snorm16x2,
        // R16G16_SNORM (4 bytes) ? R16G16B16A16_FLOAT (8 bytes)
        Snorm16x2_To_Float16x4,
        // R16G16B16A16_FLOAT (8 bytes) ? R10G10B10A2_UNORM (4 bytes)
        Float16x4_To_R10G10B10A2,
        // R10G10B10A2_UNORM (4 bytes) ? R16G16B16A16_FLOAT (8 bytes)
        R10G10B10A2_To_Float16x4,
    }

    // Maps how vertex data bytes move from old layout to new layout,
    // including any format conversions needed.
    public class VLayConversionMap
    {
        public int OldSlot1Stride { get; set; }
        public int NewSlot1Stride { get; set; }
        // Direct byte copies where formats match: (srcOffset, dstOffset, byteCount)
        public List<(int srcOff, int dstOff, int size)> ByteMappings { get; set; } = [];
        // Format conversions where the same semantic has different DXGI formats: (srcOffset, dstOffset, srcSize, dstSize, conversion)
        public List<(int srcOff, int dstOff, int srcSize, int dstSize, VLayFormatConversion conversion)> FormatConversions { get; set; } = [];

        // True if the layout changed and VerB data needs patching.
        public bool LayoutChanged => OldSlot1Stride != NewSlot1Stride || FormatConversions.Count > 0 ||
                                      ByteMappings.Count == 0;
    }

    public sealed class MaterialPathSnapshot
    {
        public MaterialBlob MaterialBlob { get; init; }
        public int? MaterialId { get; init; }
        public string MaterialName { get; init; }
        public string OriginalMaterialPath { get; init; }
    }

    public sealed class MaterialShaderCleanupResult
    {
        public int ChangedMaterialCount { get; set; }
        public int ClearedBlobCount { get; set; }
        public int ClearedParameterCount { get; set; }
        public List<string> Log { get; } = [];
    }

    // Converts a modelbin bundle's blobs in-place to the specified target game format.
    // Does NOT handle file I/O or path conversion � caller is responsible for loading/saving.
    public void ConvertModelbinBundle(Bundle bundle, ForzaGameTarget target, ConversionResult result, ConversionOptions? options = null)
    {
        var (targetBundleVer, targetModlVer, targetMeshVer, targetVlayVer) = GetModelbinTargetVersions(target);

        result.Log.Add($"Target: {target} -> Bundle v{targetBundleVer.maj}.{targetBundleVer.min}, " +
                       $"Mesh v{targetMeshVer.maj}.{targetMeshVer.min}");

        bundle.VersionMajor = targetBundleVer.maj;
        bundle.VersionMinor = targetBundleVer.min;

        // Resolve the modelbin filename for pattern-aware VLay conversion
        string modelbinFileName = null;
        foreach (var blob in bundle.Blobs)
        {
            if (blob.Tag == Bundle.TAG_BLOB_Model)
            {
                var nameMeta = blob.GetMetadataByTag<NameMetadata>(BundleMetadata.TAG_METADATA_Name);
                if (nameMeta != null)
                {
                    modelbinFileName = nameMeta.Name;
                    break;
                }
            }
        }

        // For FH5: convert ALL VLay blobs that have slot 1 elements (TANGENT2 + TEXCOORD0 must be present).
        // For other targets: only convert the main VLay blob (ID 0) to avoid modifying position-only layouts.
        bool convertAllVlayBlobs = target is ForzaGameTarget.FH5;
        int mainVlayBlobIndex = convertAllVlayBlobs ? -1 : FindMainVlayBlobIndex(bundle);

        // Pass 1: Convert version-stamped blobs and collect VLay conversion maps
        // Key = VLay blob index in bundle.Blobs, Value = conversion map
        var vlayConversionMaps = new Dictionary<int, VLayConversionMap>();

        for (int i = 0; i < bundle.Blobs.Count; i++)
        {
            var blob = bundle.Blobs[i];
            switch (blob.Tag)
            {
                case Bundle.TAG_BLOB_Model:
                    ConvertModlBlob(blob, targetModlVer, target, result);
                    break;
                case Bundle.TAG_BLOB_Mesh:
                    ConvertMeshBlob(blob, targetMeshVer, target, result);
                    break;
                case Bundle.TAG_BLOB_VertexLayout:
                    // For FH5: convert all VLay blobs with slot 1 elements
                    // For other targets: only convert the main VLay blob
                    if (convertAllVlayBlobs || i == mainVlayBlobIndex)
                    {
                        var map = ConvertVlayBlobData(blob, target, result, modelbinFileName);
                        if (map != null)
                            vlayConversionMaps[i] = map;
                    }
                    else
                    {
                        result.Log.Add($"  VLay blob (non-main, index {i}) -> version updated only");
                    }
                    // Version stamp all VLay blobs
                    blob.VersionMajor = targetVlayVer.maj;
                    blob.VersionMinor = targetVlayVer.min;
                    break;
                case Bundle.TAG_BLOB_MaterialInstance:
                    ConvertMatiBlob(blob, result);
                    break;
            }
        }

            NormalizeFm2023MeshLodState(bundle, options, result);

        // Build a lookup from VLay identifier to conversion map
        var vlayIdToMap = new Dictionary<int, VLayConversionMap>();
        foreach (var (blobIdx, map) in vlayConversionMaps)
        {
            var vlayBlob = bundle.Blobs[blobIdx];
            var idMeta = vlayBlob.GetMetadataByTag<IdentifierMetadata>(BundleMetadata.TAG_METADATA_Identifier);
            if (idMeta != null)
                vlayIdToMap[(int)idMeta.Id] = map;
        }

        // Pass 2: Patch VerB data and Mesh stride references using VLay conversion maps
        var verbIdToVlayMap = new Dictionary<int, VLayConversionMap>();

        foreach (var blob in bundle.Blobs)
        {
            if (blob is MeshBlob mesh)
            {
                int vlayId = mesh.VertexLayoutIndex;
                if (!vlayIdToMap.TryGetValue(vlayId, out var convMap))
                    continue;

                // Find slot 1 vertex buffer reference
                foreach (var vb in mesh.VertexBuffers)
                {
                    if (vb.InputSlot == 1)
                    {
                        verbIdToVlayMap.TryAdd(vb.Index, convMap);

                        // Patch the mesh's stride reference to match new VLay
                        if (convMap.LayoutChanged && vb.Stride == (uint)convMap.OldSlot1Stride)
                        {
                            vb.Stride = (uint)convMap.NewSlot1Stride;
                        }
                    }
                }
            }
        }

        // Now patch VerB blobs
        foreach (var blob in bundle.Blobs)
        {
            if (blob.Tag != Bundle.TAG_BLOB_VertexBuffer) continue;

            var idMeta = blob.GetMetadataByTag<IdentifierMetadata>(BundleMetadata.TAG_METADATA_Identifier);
            if (idMeta == null) continue;

            int verbId = (int)idMeta.Id;
            if (!verbIdToVlayMap.TryGetValue(verbId, out var convMap))
            {
                result.Log.Add($"  VerB[{verbId}] kept (no VLay conversion needed)");
                continue;
            }

            if (!convMap.LayoutChanged)
            {
                result.Log.Add($"  VerB[{verbId}] kept (layout unchanged)");
                continue;
            }

            ConvertVerbBlobData(blob, convMap, result);
        }
    }

    private static void NormalizeFm2023MeshLodState(Bundle bundle, ConversionOptions options, ConversionResult result)
    {
        if (bundle == null || result == null)
            return;

        string sourceFileName = Path.GetFileName(options?.SourceModelbinFileName);
        bool forceAllMeshes = !string.IsNullOrWhiteSpace(sourceFileName) &&
            sourceFileName.EndsWith(".modelbin", StringComparison.OrdinalIgnoreCase) &&
            sourceFileName.IndexOf("_SLOD", StringComparison.OrdinalIgnoreCase) >= 0;

        bool isFm2023Source = IsFm2023SourceModelbin(bundle, options, out string sourceReason);
        if (!forceAllMeshes && !isFm2023Source)
            return;

        int slodMatchCount = 0;
        int lod0MatchCount = 0;

        foreach (var mesh in bundle.Blobs.OfType<MeshBlob>())
        {
            bool forceMesh = forceAllMeshes;
            if (forceMesh)
            {
                slodMatchCount++;
            }
            else if (isFm2023Source)
            {
                string meshName = GetNormalizedMetadataName(mesh);
                if (!string.IsNullOrWhiteSpace(meshName) &&
                    meshName.IndexOf("LOD0", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    forceMesh = true;
                    lod0MatchCount++;
                }
            }

            if (!forceMesh)
                continue;

            mesh.LODFlags = 0x03;
            mesh.LODLevel1 = 0x00;
            mesh.LODLevel2 = 0xFF;
        }

        if (slodMatchCount > 0)
        {
            result.Log.Add($"  FM2023 LOD normalization: forced {slodMatchCount} mesh(es) because source file '{sourceFileName}' contains _SLOD.");
        }

        if (lod0MatchCount > 0)
        {
            result.Log.Add($"  FM2023 LOD normalization: forced {lod0MatchCount} mesh(es) whose mesh Name metadata contains LOD0 ({sourceReason}).");
        }
    }

    private static string GetNormalizedMetadataName(BundleBlob blob)
    {
        string name = blob?.Metadatas.OfType<NameMetadata>().FirstOrDefault()?.Name;
        if (string.IsNullOrEmpty(name))
            return name;

        return name.Replace("\0", string.Empty).Trim();
    }

    private static bool IsFm2023SourceModelbin(Bundle bundle, ConversionOptions options, out string reason)
    {
        reason = null;

        string detectedGame = options?.SourceDetectedGame;
        if (!string.IsNullOrWhiteSpace(detectedGame) &&
            detectedGame.IndexOf("FM2023", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            reason = $"detected source game '{detectedGame}'";
            return true;
        }

        if (bundle == null)
            return false;

        byte meshMajor = 0;
        byte meshMinor = 0;
        byte modlMajor = 0;
        byte modlMinor = 0;

        foreach (var blob in bundle.Blobs)
        {
            switch (blob.Tag)
            {
                case Bundle.TAG_BLOB_Model:
                    modlMajor = blob.VersionMajor;
                    modlMinor = blob.VersionMinor;
                    break;
                case Bundle.TAG_BLOB_Mesh:
                    meshMajor = blob.VersionMajor;
                    meshMinor = blob.VersionMinor;
                    break;
            }
        }

        if (meshMajor == 1 && meshMinor == 8 && modlMajor == 1 && modlMinor == 2 && bundle.VersionMajor == 1 && bundle.VersionMinor >= 1)
        {
            reason = $"source bundle profile v{bundle.VersionMajor}.{bundle.VersionMinor}, modl v{modlMajor}.{modlMinor}, mesh v{meshMajor}.{meshMinor}";
            return true;
        }

        return false;
    }

    public IReadOnlyList<MaterialPathSnapshot> CaptureMaterialPathSnapshot(Bundle bundle)
    {
        if (bundle == null)
            return [];

        var snapshots = new List<MaterialPathSnapshot>();

        foreach (var materialBlob in bundle.Blobs.OfType<MaterialBlob>())
        {
            string materialPath = GetTrackedMaterialPath(materialBlob);
            if (string.IsNullOrWhiteSpace(materialPath))
                continue;

            var idMeta = materialBlob.GetMetadataByTag<IdentifierMetadata>(BundleMetadata.TAG_METADATA_Identifier);
            string materialName = materialBlob.GetMetadataByTag<NameMetadata>(BundleMetadata.TAG_METADATA_Name)?.Name;

            snapshots.Add(new MaterialPathSnapshot
            {
                MaterialBlob = materialBlob,
                MaterialId = idMeta != null ? (int)idMeta.Id : null,
                MaterialName = materialName,
                OriginalMaterialPath = materialPath,
            });
        }

        return snapshots;
    }

    public MaterialShaderCleanupResult RemoveConflictingShaderParameters(IReadOnlyList<MaterialPathSnapshot> snapshots)
    {
        var cleanupResult = new MaterialShaderCleanupResult();
        if (snapshots == null || snapshots.Count == 0)
            return cleanupResult;

        foreach (var snapshot in snapshots)
        {
            string originalPath = snapshot.OriginalMaterialPath;
            string currentPath = GetTrackedMaterialPath(snapshot.MaterialBlob);

            if (string.IsNullOrWhiteSpace(originalPath) ||
                string.IsNullOrWhiteSpace(currentPath) ||
                string.Equals(originalPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            cleanupResult.ChangedMaterialCount++;

            int clearedBlobCount = 0;
            int clearedParameterCount = 0;

            if (snapshot.MaterialBlob?.Bundle != null)
            {
                foreach (var shaderBlob in snapshot.MaterialBlob.Bundle.Blobs.OfType<MaterialShaderParameterBlob>())
                {
                    if (shaderBlob.Tag != Bundle.TAG_BLOB_MaterialShaderParameter)
                        continue;

                    int parameterCount = shaderBlob.Parameters?.Count ?? 0;
                    if (parameterCount == 0)
                        continue;

                    shaderBlob.Parameters.Clear();
                    clearedBlobCount++;
                    clearedParameterCount += parameterCount;
                }
            }

            cleanupResult.ClearedBlobCount += clearedBlobCount;
            cleanupResult.ClearedParameterCount += clearedParameterCount;

            string materialLabel = DescribeMaterialSnapshot(snapshot);
            if (clearedBlobCount > 0)
            {
                cleanupResult.Log.Add($"{materialLabel}: cleared {clearedParameterCount} parameter(s) from {clearedBlobCount} MTPR blob(s) after MATI swap '{originalPath}' -> '{currentPath}'.");
            }
            else
            {
                cleanupResult.Log.Add($"{materialLabel}: MATI swap '{originalPath}' -> '{currentPath}' had no MTPR parameters to clear.");
            }
        }

        return cleanupResult;
    }

    private static string GetTrackedMaterialPath(MaterialBlob materialBlob)
    {
        if (materialBlob?.Bundle == null)
            return null;

        return materialBlob.Bundle.Blobs
            .OfType<MaterialResourceBlob>()
            .Select(resource => resource.Path)
            .FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path) &&
                string.Equals(Path.GetExtension(path), ".materialbin", StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeMaterialSnapshot(MaterialPathSnapshot snapshot)
    {
        if (snapshot == null)
            return "Material";

        if (!string.IsNullOrWhiteSpace(snapshot.MaterialName) && snapshot.MaterialId.HasValue)
            return $"{snapshot.MaterialName} (Id {snapshot.MaterialId.Value})";

        if (!string.IsNullOrWhiteSpace(snapshot.MaterialName))
            return snapshot.MaterialName;

        if (snapshot.MaterialId.HasValue)
            return $"Material Id {snapshot.MaterialId.Value}";

        return "Material";
    }

    // Applies a shader-aware VLay patch after material paths have been resolved.
    // This expands target layouts only when a referenced target shader requires
    // additional vertex semantics beyond the converted baseline layout.
    public void ApplyAdvancedVlayBlobPatch(Bundle bundle, ForzaGameTarget target, ConversionResult result, ConversionOptions options)
    {
        if (bundle == null || result == null || options?.EnableAdvancedVlayBlobPatch != true)
            return;

        if (string.IsNullOrWhiteSpace(options.TargetGamePath) || !Directory.Exists(options.TargetGamePath))
        {
            result.Warnings.Add("Advanced VLay blob patch skipped: target game path is not configured.");
            return;
        }

        result.Log.Add("--- Advanced VLay blob patch ---");
        string targetGameId = ConversionService.GetGameSettingsId(target);

        var materialPathsById = BuildMaterialPathMap(bundle);
        if (materialPathsById.Count == 0)
        {
            result.Log.Add("  No material resource paths were found in the modelbin bundle.");
            return;
        }

        var requiredFlagsByLayout = new Dictionary<int, uint>();
        var loggedIssues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mesh in bundle.Blobs.OfType<MeshBlob>())
        {
            int materialId = GetMeshMaterialId(mesh);
            if (materialId < 0 || mesh.VertexLayoutIndex < 0)
                continue;

            if (!materialPathsById.TryGetValue(materialId, out string materialPath))
                continue;

            if (!TryResolveMaterialInputFlags(materialPath, options.TargetGamePath, targetGameId, loggedIssues, result,
                    out uint shaderFlags, out string _))
            {
                continue;
            }

            if (shaderFlags == 0)
                continue;

            if (requiredFlagsByLayout.TryGetValue(mesh.VertexLayoutIndex, out uint existingFlags))
                requiredFlagsByLayout[mesh.VertexLayoutIndex] = existingFlags | shaderFlags;
            else
                requiredFlagsByLayout[mesh.VertexLayoutIndex] = shaderFlags;
        }

        if (requiredFlagsByLayout.Count == 0)
        {
            result.Log.Add("  No target shader requirements could be resolved from the referenced materials.");
            return;
        }

        var conversionMapsByLayout = new Dictionary<int, VLayConversionMap>();

        foreach (var layoutEntry in requiredFlagsByLayout.OrderBy(entry => entry.Key))
        {
            if (!TryResolveVertexLayoutBlob(bundle, layoutEntry.Key, out _, out var vlayBlob))
            {
                if (loggedIssues.Add($"layout:{layoutEntry.Key}"))
                    result.Warnings.Add($"Advanced VLay blob patch skipped layout {layoutEntry.Key}: VLay blob was not found.");
                continue;
            }

            var convMap = PatchVlayBlobForRequiredFlags(vlayBlob, layoutEntry.Key, layoutEntry.Value, target, result);
            if (convMap != null && convMap.LayoutChanged)
                conversionMapsByLayout[layoutEntry.Key] = convMap;
        }

        if (conversionMapsByLayout.Count == 0)
        {
            result.Log.Add("  All referenced VLay blobs already satisfy their target shader requirements.");
            return;
        }

        ApplyVlayConversionMaps(bundle, conversionMapsByLayout, result);
        result.Log.Add($"  Advanced VLay blob patch updated {conversionMapsByLayout.Count} VLay layout(s).");
    }

    // Finds the index of the "main" VLay blob in the bundle � the one with ID 0.
    private static int FindMainVlayBlobIndex(Bundle bundle)
    {
        int firstVlayWithSlot1 = -1;

        for (int i = 0; i < bundle.Blobs.Count; i++)
        {
            var blob = bundle.Blobs[i];
            if (blob.Tag != Bundle.TAG_BLOB_VertexLayout)
                continue;

            var idMeta = blob.GetMetadataByTag<IdentifierMetadata>(BundleMetadata.TAG_METADATA_Identifier);
            if (idMeta != null && idMeta.Id == 0)
                return i;

            if (firstVlayWithSlot1 == -1 && blob is VertexLayoutBlob vlay)
            {
                if (vlay.Elements.Any(e => e.InputSlot == 1))
                    firstVlayWithSlot1 = i;
            }
        }

        return firstVlayWithSlot1;
    }

    private static Dictionary<int, string> BuildMaterialPathMap(Bundle bundle)
    {
        var materialPaths = new Dictionary<int, string>();

        foreach (var materialBlob in bundle.Blobs.OfType<MaterialBlob>())
        {
            var idMeta = materialBlob.GetMetadataByTag<IdentifierMetadata>(BundleMetadata.TAG_METADATA_Identifier);
            if (idMeta == null || materialBlob.Bundle == null)
                continue;

            string materialPath = materialBlob.Bundle.Blobs
                .OfType<MaterialResourceBlob>()
                .Select(blob => blob.Path)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

            if (!string.IsNullOrWhiteSpace(materialPath))
                materialPaths[(int)idMeta.Id] = materialPath;
        }

        return materialPaths;
    }

    private static int GetMeshMaterialId(MeshBlob mesh)
    {
        if (mesh.MaterialIds != null && mesh.MaterialIds.Length > 1)
            return mesh.MaterialIds[1];

        return mesh.MaterialId;
    }

    private static bool TryResolveVertexLayoutBlob(Bundle bundle, int layoutIndex, out int blobIndex, out VertexLayoutBlob vlayBlob)
    {
        blobIndex = -1;
        vlayBlob = null;

        for (int i = 0; i < bundle.Blobs.Count; i++)
        {
            if (bundle.Blobs[i] is not VertexLayoutBlob candidate)
                continue;

            var idMeta = candidate.GetMetadataByTag<IdentifierMetadata>(BundleMetadata.TAG_METADATA_Identifier);
            if (idMeta != null && (int)idMeta.Id == layoutIndex)
            {
                blobIndex = i;
                vlayBlob = candidate;
                return true;
            }
        }

        var layouts = bundle.Blobs.OfType<VertexLayoutBlob>().ToArray();
        if (layoutIndex >= 0 && layoutIndex < layouts.Length)
        {
            vlayBlob = layouts[layoutIndex];
            blobIndex = bundle.Blobs.IndexOf(vlayBlob);
            return blobIndex >= 0;
        }

        return false;
    }

    private bool TryResolveMaterialInputFlags(
        string materialPath,
        string targetGamePath,
        string targetGameId,
        HashSet<string> loggedIssues,
        ConversionResult result,
        out uint shaderFlags,
        out string shaderPath)
    {
        shaderFlags = 0;
        shaderPath = null;

        string normalizedMaterialPath = NormalizeAssetGamePath(materialPath);
        if (string.IsNullOrWhiteSpace(normalizedMaterialPath))
            return false;

        string materialCacheKey = BuildAssetCacheKey(targetGamePath, normalizedMaterialPath);

        if (_materialRequirementCache.TryGetValue(materialCacheKey, out uint? cachedMaterialFlags))
        {
            _materialShaderPathCache.TryGetValue(materialCacheKey, out shaderPath);
            if (cachedMaterialFlags.HasValue)
            {
                shaderFlags = cachedMaterialFlags.Value;
                return true;
            }

            return false;
        }

        if (!TryLoadBundleFromGamePath(targetGamePath, normalizedMaterialPath, targetGameId, out var materialBundle))
        {
            _materialRequirementCache[materialCacheKey] = null;
            if (loggedIssues.Add($"material-load:{normalizedMaterialPath}"))
                result.Warnings.Add($"Advanced VLay blob patch: could not load target material '{normalizedMaterialPath}'.");
            return false;
        }

        var matlBlob = materialBundle.Blobs.OfType<MatLBlob>().FirstOrDefault();
        shaderPath = SelectShaderPath(matlBlob);
        if (string.IsNullOrWhiteSpace(shaderPath))
        {
            _materialRequirementCache[materialCacheKey] = null;
            if (loggedIssues.Add($"material-shader:{normalizedMaterialPath}"))
                result.Warnings.Add($"Advanced VLay blob patch: material '{normalizedMaterialPath}' does not contain a usable shader path.");
            return false;
        }

        shaderPath = NormalizeAssetGamePath(shaderPath);
        _materialShaderPathCache[materialCacheKey] = shaderPath;

        string shaderCacheKey = BuildAssetCacheKey(targetGamePath, shaderPath);

        if (_shaderRequirementCache.TryGetValue(shaderCacheKey, out uint? cachedShaderFlags))
        {
            _materialRequirementCache[materialCacheKey] = cachedShaderFlags;
            if (cachedShaderFlags.HasValue)
            {
                shaderFlags = cachedShaderFlags.Value;
                return true;
            }

            return false;
        }

        if (!TryLoadBundleFromGamePath(targetGamePath, shaderPath, targetGameId, out var shaderBundle))
        {
            _shaderRequirementCache[shaderCacheKey] = null;
            _materialRequirementCache[materialCacheKey] = null;
            if (loggedIssues.Add($"shader-load:{shaderPath}"))
                result.Warnings.Add($"Advanced VLay blob patch: could not load target shader '{shaderPath}'.");
            return false;
        }

        bool foundVdcl = false;
        uint resolvedFlags = 0;

        foreach (var lightScenarioBlob in shaderBundle.Blobs.OfType<LightScenarioBlob>())
        {
            var vdclMetadata = lightScenarioBlob.GetMetadataByTag<VDCLMetadata>(BundleMetadata.TAG_METADATA_VDCL);
            if (vdclMetadata?.Entries == null || vdclMetadata.Entries.Count == 0)
                continue;

            foundVdcl = true;
            foreach (var entry in vdclMetadata.Entries)
                resolvedFlags |= entry.VertexInputFlags;
        }

        if (!foundVdcl)
        {
            _shaderRequirementCache[shaderCacheKey] = null;
            _materialRequirementCache[materialCacheKey] = null;
            if (loggedIssues.Add($"shader-vdcl:{shaderPath}"))
                result.Warnings.Add($"Advanced VLay blob patch: shader '{shaderPath}' has no VDCL metadata to derive requirements from.");
            return false;
        }

        _shaderRequirementCache[shaderCacheKey] = resolvedFlags;
        _materialRequirementCache[materialCacheKey] = resolvedFlags;
        shaderFlags = resolvedFlags;
        return true;
    }

    private static string BuildAssetCacheKey(string gameRootPath, string assetPath)
    {
        string normalizedRoot = string.IsNullOrWhiteSpace(gameRootPath)
            ? string.Empty
            : Path.GetFullPath(gameRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string normalizedPath = NormalizeAssetGamePath(assetPath) ?? string.Empty;
        return $"{normalizedRoot}|{normalizedPath}";
    }

    private static string SelectShaderPath(MatLBlob matlBlob)
    {
        if (matlBlob == null)
            return null;

        if (!string.IsNullOrWhiteSpace(matlBlob.PathV1_2))
            return matlBlob.PathV1_2;

        if (!string.IsNullOrWhiteSpace(matlBlob.PathV1_1))
            return matlBlob.PathV1_1;

        return string.IsNullOrWhiteSpace(matlBlob.Path) ? null : matlBlob.Path;
    }

    private static string NormalizeAssetGamePath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return null;

        string normalized = assetPath.Trim().Replace('/', '\\');

        if (normalized.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Game:/", StringComparison.OrdinalIgnoreCase))
        {
            string relative = normalized[6..].TrimStart('\\', '/');
            return $"Game:\\{relative}";
        }

        return normalized.TrimStart('\\', '/');
    }

    private bool TryLoadBundleFromGamePath(string gameRootPath, string assetPath, string targetGameId, out Bundle bundle)
    {
        bundle = null;

        if (!TryLoadAssetBytesFromGamePath(gameRootPath, assetPath, targetGameId, out byte[] assetBytes))
            return false;

        try
        {
            using var stream = new MemoryStream(assetBytes, writable: false);
            bundle = new Bundle();
            bundle.Load(stream);
            return true;
        }
        catch
        {
            bundle = null;
            return false;
        }
    }

    private bool TryLoadAssetBytesFromGamePath(string gameRootPath, string assetPath, string targetGameId, out byte[] assetBytes)
    {
        assetBytes = null;

        if (string.IsNullOrWhiteSpace(gameRootPath) || !Directory.Exists(gameRootPath) || string.IsNullOrWhiteSpace(assetPath))
            return false;

        string normalizedAssetPath = NormalizeAssetGamePath(assetPath);
        if (string.IsNullOrWhiteSpace(normalizedAssetPath))
            return false;

        if (TryLoadAssetBytesFromCandidatePaths(gameRootPath, normalizedAssetPath, out assetBytes))
            return true;

        if (TryResolveIndexedMaterialPath(targetGameId, normalizedAssetPath, out string indexedMaterialPath) &&
            !string.Equals(indexedMaterialPath, normalizedAssetPath, StringComparison.OrdinalIgnoreCase) &&
            TryLoadAssetBytesFromCandidatePaths(gameRootPath, indexedMaterialPath, out assetBytes))
        {
            return true;
        }

        string fileName = Path.GetFileName(normalizedAssetPath.Replace('/', '\\'));
        string extension = Path.GetExtension(fileName);
        return !string.IsNullOrWhiteSpace(fileName) && TryLoadAssetBytesByFileName(gameRootPath, fileName, extension, out assetBytes);
    }

    private static bool TryLoadAssetBytesFromCandidatePaths(string gameRootPath, string assetPath, out byte[] assetBytes)
    {
        assetBytes = null;

        foreach (string relativePath in BuildRelativePathCandidates(assetPath))
        {
            string loosePath = Path.Combine(gameRootPath, relativePath);
            if (File.Exists(loosePath))
            {
                assetBytes = File.ReadAllBytes(loosePath);
                return true;
            }

            if (TryLoadFromDerivedZip(gameRootPath, relativePath, out assetBytes))
                return true;
        }

        return false;
    }

    private static bool TryResolveIndexedMaterialPath(string targetGameId, string assetPath, out string resolvedGamePath)
    {
        resolvedGamePath = null;

        if (string.IsNullOrWhiteSpace(targetGameId) ||
            string.IsNullOrWhiteSpace(assetPath) ||
            !Path.GetExtension(assetPath).Equals(".materialbin", StringComparison.OrdinalIgnoreCase) ||
            !GameAssetDatabaseService.DatabaseExists(targetGameId))
        {
            return false;
        }

        resolvedGamePath = GameAssetDatabaseService.LookupByPath(targetGameId, assetPath);
        if (!string.IsNullOrWhiteSpace(resolvedGamePath))
            return true;

        string fileName = Path.GetFileName(assetPath.Replace('/', '\\'));
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        resolvedGamePath = GameAssetDatabaseService.LookupFile(targetGameId, fileName);
        return !string.IsNullOrWhiteSpace(resolvedGamePath);
    }

    private static IEnumerable<string> BuildRelativePathCandidates(string assetPath)
    {
        string relative = assetPath.Replace('/', '\\').Trim();
        if (relative.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Game:/", StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[6..];
        }

        relative = relative.TrimStart('\\', '/');

        var candidates = new List<string>();

        static void AddCandidate(List<string> list, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            string normalized = value.Replace('/', '\\').TrimStart('\\', '/');
            if (!list.Any(existing => existing.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                list.Add(normalized);
        }

        AddCandidate(candidates, relative);

        if (!relative.StartsWith("media\\", StringComparison.OrdinalIgnoreCase))
            AddCandidate(candidates, Path.Combine("media", relative));

        if (!relative.StartsWith("media\\materials\\", StringComparison.OrdinalIgnoreCase))
            AddCandidate(candidates, Path.Combine("media", "materials", relative));

        return candidates;
    }

    private static bool TryLoadFromDerivedZip(string gameRootPath, string relativePath, out byte[] assetBytes)
    {
        assetBytes = null;

        var parts = relativePath
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            return false;

        for (int prefixLength = parts.Length - 1; prefixLength >= 1; prefixLength--)
        {
            string zipRelativePath = Path.Combine(parts.Take(prefixLength).ToArray()) + ".zip";
            string zipPath = Path.Combine(gameRootPath, zipRelativePath);
            if (!File.Exists(zipPath))
                continue;

            string entryPath = string.Join('/', parts.Skip(prefixLength));
            if (TryExtractZipEntry(zipPath, entryPath, out assetBytes))
                return true;
        }

        return false;
    }

    private bool TryLoadAssetBytesByFileName(string gameRootPath, string fileName, string extension, out byte[] assetBytes)
    {
        assetBytes = null;

        foreach (string zipPath in GetRelevantAssetZipPaths(gameRootPath, extension))
        {
            if (TryExtractZipEntryByFileName(zipPath, fileName, out assetBytes))
                return true;
        }

        return false;
    }

    private IReadOnlyList<string> GetRelevantAssetZipPaths(string gameRootPath, string extension)
    {
        if (string.IsNullOrWhiteSpace(gameRootPath) || !Directory.Exists(gameRootPath))
            return [];

        string normalizedExtension = string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.ToLowerInvariant();
        string cacheKey = $"{Path.GetFullPath(gameRootPath)}|{normalizedExtension}";

        if (_assetZipPathCache.TryGetValue(cacheKey, out var cachedZipPaths))
            return cachedZipPaths;

        string mediaPath = Path.Combine(gameRootPath, "media");
        if (!Directory.Exists(mediaPath))
            mediaPath = gameRootPath;

        var results = new List<string>();
        foreach (string zipName in GetPriorityZipNamesForAsset(normalizedExtension))
        {
            try
            {
                results.AddRange(Directory.GetFiles(mediaPath, zipName, SearchOption.AllDirectories));
            }
            catch
            {
            }
        }

        if (results.Count == 0)
        {
            try
            {
                var libraryDirs = Directory.GetDirectories(mediaPath, "_library", SearchOption.AllDirectories);
                foreach (var libraryDir in libraryDirs)
                {
                    try
                    {
                        results.AddRange(Directory.GetFiles(libraryDir, "*.zip", SearchOption.AllDirectories));
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        if (results.Count == 0)
        {
            try
            {
                string carsPath = Path.Combine(mediaPath, "cars");
                if (Directory.Exists(carsPath))
                    results.AddRange(Directory.GetFiles(carsPath, "*.zip", SearchOption.AllDirectories));
            }
            catch
            {
            }
        }

        var zipPaths = results
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _assetZipPathCache[cacheKey] = zipPaths;
        return zipPaths;
    }

    private static string[] GetPriorityZipNamesForAsset(string extension)
    {
        return extension switch
        {
            ".materialbin" => ["materials.zip", "Materials.zip"],
            ".shaderbin" => ["shaders.zip", "Shaders.zip", "materials.zip", "Materials.zip"],
            _ => ["materials.zip", "Materials.zip", "shaders.zip", "Shaders.zip"],
        };
    }

    private static bool TryExtractZipEntry(string zipPath, string entryPath, out byte[] assetBytes)
    {
        assetBytes = null;
        string normalizedEntryPath = entryPath.Replace('\\', '/').TrimStart('/');

        try
        {
            using var customZip = new CustomZipFile(zipPath);
            var entry = customZip.GetEntries().FirstOrDefault(candidate =>
                candidate.Name.Replace('\\', '/').TrimStart('/').Equals(normalizedEntryPath, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                assetBytes = customZip.ExtractToMemory(entry);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryExtractZipEntryByFileName(string zipPath, string fileName, out byte[] assetBytes)
    {
        assetBytes = null;

        try
        {
            using var customZip = new CustomZipFile(zipPath);
            var entry = customZip.GetEntries().FirstOrDefault(candidate =>
                Path.GetFileName(candidate.Name).Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                assetBytes = customZip.ExtractToMemory(entry);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private VLayConversionMap PatchVlayBlobForRequiredFlags(VertexLayoutBlob vlayBlob, int layoutIndex, uint requiredFlags, ForzaGameTarget target, ConversionResult result)
    {
        var sourceSlot1Elements = new List<(D3D12_INPUT_LAYOUT_DESC desc, int byteOffset, int byteSize, int elementIndex)>();
        int sourceStride = 0;

        for (int elementIndex = 0; elementIndex < vlayBlob.Elements.Count; elementIndex++)
        {
            var element = vlayBlob.Elements[elementIndex];
            if (element.InputSlot != 1)
                continue;

            int byteSize = GetDxgiFormatByteSize(element.Format);
            sourceSlot1Elements.Add((element, sourceStride, byteSize, elementIndex));
            sourceStride += byteSize;
        }

        if (sourceSlot1Elements.Count == 0)
        {
            result.Log.Add($"  VLay[{layoutIndex}] advanced patch skipped (position-only layout).");
            return null;
        }

        var currentElements = new List<VLayElementInfo>();
        var currentSemantics = new HashSet<(string name, short idx)>();

        foreach (var element in sourceSlot1Elements)
        {
            string semanticName = vlayBlob.SemanticNames[element.desc.SemanticNameIndex];
            DXGI_FORMAT packedFormat = element.elementIndex >= 0 && element.elementIndex < vlayBlob.PackedFormats.Count
                ? vlayBlob.PackedFormats[element.elementIndex]
                : DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;

            currentElements.Add(new VLayElementInfo
            {
                SemanticName = semanticName,
                SemanticIndex = element.desc.SemanticIndex,
                Format = element.desc.Format,
                PackedFormat = packedFormat,
                ByteSize = element.byteSize,
            });

            currentSemantics.Add((semanticName, element.desc.SemanticIndex));
        }

        uint currentFlags = ComputeVlayFlags(currentElements, target);
        uint missingFlags = requiredFlags & ~currentFlags;
        if (missingFlags == 0)
            return null;

        var additions = BuildAdvancedPatchAdditions(missingFlags, currentSemantics, target, result);
        if (additions.Count == 0)
        {
            result.Warnings.Add($"Advanced VLay blob patch could not map missing shader bits 0x{missingFlags:X3} for layout {layoutIndex}.");
            return null;
        }

        var targetElements = currentElements.ToList();
        foreach (var addition in additions)
        {
            if (!targetElements.Any(existing => existing.SemanticName == addition.SemanticName && existing.SemanticIndex == addition.SemanticIndex))
                targetElements.Add(addition);
        }

        targetElements.Sort((left, right) =>
            GetElementSortKey(left.SemanticName, left.SemanticIndex).CompareTo(GetElementSortKey(right.SemanticName, right.SemanticIndex)));

        var conversionMap = new VLayConversionMap
        {
            OldSlot1Stride = sourceStride,
            NewSlot1Stride = targetElements.Sum(element => element.ByteSize)
        };

        int targetByteOffset = 0;
        foreach (var targetElement in targetElements)
        {
            var sourceMatch = sourceSlot1Elements.FirstOrDefault(candidate =>
            {
                string sourceName = vlayBlob.SemanticNames[candidate.desc.SemanticNameIndex];
                return sourceName == targetElement.SemanticName && candidate.desc.SemanticIndex == targetElement.SemanticIndex;
            });

            if (sourceMatch.desc != null)
            {
                int copySize = Math.Min(sourceMatch.byteSize, targetElement.ByteSize);
                conversionMap.ByteMappings.Add((sourceMatch.byteOffset, targetByteOffset, copySize));
            }

            targetByteOffset += targetElement.ByteSize;
        }

        var nameToIndex = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < vlayBlob.SemanticNames.Count; i++)
            nameToIndex[vlayBlob.SemanticNames[i]] = (short)i;

        foreach (var element in targetElements)
        {
            if (!nameToIndex.ContainsKey(element.SemanticName))
            {
                nameToIndex[element.SemanticName] = (short)vlayBlob.SemanticNames.Count;
                vlayBlob.SemanticNames.Add(element.SemanticName);
            }
        }

        var positionElement = vlayBlob.Elements.FirstOrDefault(element => element.InputSlot == 0);
        var positionPackedFormat = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        if (positionElement != null)
        {
            int positionIndex = vlayBlob.Elements.IndexOf(positionElement);
            if (positionIndex >= 0 && positionIndex < vlayBlob.PackedFormats.Count)
                positionPackedFormat = vlayBlob.PackedFormats[positionIndex];
        }

        vlayBlob.Elements.Clear();
        vlayBlob.PackedFormats.Clear();

        if (positionElement != null)
        {
            vlayBlob.Elements.Add(positionElement);
            vlayBlob.PackedFormats.Add(positionPackedFormat != DXGI_FORMAT.DXGI_FORMAT_UNKNOWN
                ? positionPackedFormat
                : (DXGI_FORMAT)49);
        }

        foreach (var element in targetElements)
        {
            vlayBlob.Elements.Add(new D3D12_INPUT_LAYOUT_DESC
            {
                SemanticNameIndex = nameToIndex[element.SemanticName],
                SemanticIndex = element.SemanticIndex,
                InputSlot = 1,
                InputSlotClass = 0,
                Format = element.Format,
                AlignedByteOffset = -1,
                InstanceDataStepRate = 0,
            });
            vlayBlob.PackedFormats.Add(element.PackedFormat);
        }

        vlayBlob.Flags = ComputeVlayFlags(targetElements, target);

        result.Log.Add($"  VLay[{layoutIndex}] advanced patch: flags 0x{currentFlags:X3} -> 0x{vlayBlob.Flags:X3}, added {string.Join(", ", additions.Select(add => $"{add.SemanticName}{add.SemanticIndex}"))}");
        result.Log.Add($"  VLay[{layoutIndex}] slot1 stride {sourceStride} -> {conversionMap.NewSlot1Stride}");

        return conversionMap;
    }

    private static List<VLayElementInfo> BuildAdvancedPatchAdditions(
        uint missingFlags,
        HashSet<(string name, short idx)> currentSemantics,
        ForzaGameTarget target,
        ConversionResult result)
    {
        var additions = new List<VLayElementInfo>();

        foreach (var (bitMask, semanticName, semanticIndex) in new (uint bitMask, string semanticName, short semanticIndex)[]
        {
            (0x001, "TEXCOORD", 0),
            (0x002, "TEXCOORD", 1),
            (0x004, "TEXCOORD", 2),
            (0x008, "TEXCOORD", 3),
            (0x010, "TEXCOORD", 4),
            (0x020, "TANGENT", 0),
            (0x040, "TANGENT", 1),
            (0x080, "TANGENT", 2),
            (0x100, "TANGENT", 3),
            (0x200, "TANGENT", 4),
            (0x400, "COLOR", 0),
        })
        {
            if ((missingFlags & bitMask) == 0 || currentSemantics.Contains((semanticName, semanticIndex)))
                continue;

            var targetElement = GetTargetElementFormat(semanticName, semanticIndex, target);
            if (targetElement != null)
            {
                additions.Add(targetElement);
            }
            else
            {
                result.Warnings.Add($"Advanced VLay blob patch: target {target} does not support required shader semantic {semanticName}{semanticIndex}.");
            }
        }

        uint handledFlags = 0x001 | 0x002 | 0x004 | 0x008 | 0x010 | 0x020 | 0x040 | 0x080 | 0x100 | 0x200 | 0x400;
        uint unhandledFlags = missingFlags & ~handledFlags;
        if (unhandledFlags != 0)
            result.Warnings.Add($"Advanced VLay blob patch encountered unknown shader flag bits 0x{unhandledFlags:X3}.");

        return additions;
    }

    private static void ApplyVlayConversionMaps(Bundle bundle, IReadOnlyDictionary<int, VLayConversionMap> conversionMapsByLayout, ConversionResult result)
    {
        var verbIdToMap = new Dictionary<int, VLayConversionMap>();

        foreach (var mesh in bundle.Blobs.OfType<MeshBlob>())
        {
            if (!conversionMapsByLayout.TryGetValue(mesh.VertexLayoutIndex, out var conversionMap))
                continue;

            foreach (var vertexBuffer in mesh.VertexBuffers)
            {
                if (vertexBuffer.InputSlot != 1)
                    continue;

                verbIdToMap.TryAdd(vertexBuffer.Index, conversionMap);

                if (conversionMap.LayoutChanged && vertexBuffer.Stride == (uint)conversionMap.OldSlot1Stride)
                    vertexBuffer.Stride = (uint)conversionMap.NewSlot1Stride;
            }
        }

        foreach (var blob in bundle.Blobs)
        {
            if (blob.Tag != Bundle.TAG_BLOB_VertexBuffer)
                continue;

            var idMeta = blob.GetMetadataByTag<IdentifierMetadata>(BundleMetadata.TAG_METADATA_Identifier);
            if (idMeta == null)
                continue;

            if (!verbIdToMap.TryGetValue((int)idMeta.Id, out var conversionMap) || !conversionMap.LayoutChanged)
                continue;

            ConvertVerbBlobData(blob, conversionMap, result);
        }
    }

    #region Version Tables

    public static (
        (byte maj, byte min) bundle,
        (byte maj, byte min) modl,
        (byte maj, byte min) mesh,
        (byte maj, byte min) vlay
    ) GetModelbinTargetVersions(ForzaGameTarget target)
    {
        return target switch
        {
            ForzaGameTarget.FM5 or ForzaGameTarget.FH2 =>
                ((1, 0), (1, 1), (1, 7), (1, 1)),
            ForzaGameTarget.FM6 =>
                ((1, 1), (1, 2), (1, 7), (1, 1)),
            ForzaGameTarget.FM7 =>
                ((1, 1), (1, 2), (1, 7), (1, 1)),
            ForzaGameTarget.FH3 =>
                ((1, 1), (1, 2), (1, 8), (1, 1)),
            ForzaGameTarget.FH4 =>
                ((1, 1), (1, 2), (1, 9), (1, 1)),
            ForzaGameTarget.FH5 =>
                ((1, 1), (1, 2), (1, 9), (1, 1)),
            ForzaGameTarget.FM2023 =>
                ((1, 1), (1, 2), (1, 8), (1, 1)),
            _ => ((1, 1), (1, 2), (1, 9), (1, 1))
        };
    }

    #endregion

    #region VLay Conversion

    // Gets the target DXGI format and packed format for a given semantic in the target game.
    // Returns null if the semantic is not valid for the target game.
    private static VLayElementInfo GetTargetElementFormat(string semanticName, short semanticIndex, ForzaGameTarget target)
    {
        bool isFH2 = target is ForzaGameTarget.FH2 or ForzaGameTarget.FM5;
        bool hasTangent2 = target is ForzaGameTarget.FH5 or ForzaGameTarget.FM2023;
        bool hasColor = target is ForzaGameTarget.FH3 or ForzaGameTarget.FH4 or ForzaGameTarget.FH5;
        bool hasTexcoord4 = target is ForzaGameTarget.FH3 or ForzaGameTarget.FH4 or ForzaGameTarget.FH5 or ForzaGameTarget.FM2023;

        return (semanticName, semanticIndex) switch
        {
            ("NORMAL", 0) when isFH2 => new() { SemanticName = "NORMAL", SemanticIndex = 0, Format = DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT, PackedFormat = (DXGI_FORMAT)9, ByteSize = 8 },
            ("NORMAL", 0) => new() { SemanticName = "NORMAL", SemanticIndex = 0, Format = DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM, PackedFormat = (DXGI_FORMAT)52, ByteSize = 4 },

            ("TANGENT", 0) when isFH2 => new() { SemanticName = "TANGENT", SemanticIndex = 0, Format = DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT, PackedFormat = (DXGI_FORMAT)9, ByteSize = 8 },
            ("TANGENT", 0) => new() { SemanticName = "TANGENT", SemanticIndex = 0, Format = DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM, PackedFormat = (DXGI_FORMAT)48, ByteSize = 4 },

            ("TANGENT", 1) when isFH2 => new() { SemanticName = "TANGENT", SemanticIndex = 1, Format = DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT, PackedFormat = (DXGI_FORMAT)9, ByteSize = 8 },
            ("TANGENT", 1) => new() { SemanticName = "TANGENT", SemanticIndex = 1, Format = DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM, PackedFormat = (DXGI_FORMAT)48, ByteSize = 4 },

            // TANGENT2 only exists in FH5/FM2023 � if target doesn't support it, drop it
            ("TANGENT", 2) when hasTangent2 => new() { SemanticName = "TANGENT", SemanticIndex = 2, Format = DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM, PackedFormat = (DXGI_FORMAT)48, ByteSize = 4 },
            ("TANGENT", 2) => null, // Not supported in target � will be dropped

            // TANGENT3/4 only exist in the rare 12-element pattern; convert format but keep
            ("TANGENT", 3) when isFH2 => new() { SemanticName = "TANGENT", SemanticIndex = 3, Format = DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT, PackedFormat = (DXGI_FORMAT)9, ByteSize = 8 },
            ("TANGENT", 3) => new() { SemanticName = "TANGENT", SemanticIndex = 3, Format = DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM, PackedFormat = (DXGI_FORMAT)48, ByteSize = 4 },
            ("TANGENT", 4) when isFH2 => new() { SemanticName = "TANGENT", SemanticIndex = 4, Format = DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT, PackedFormat = (DXGI_FORMAT)9, ByteSize = 8 },
            ("TANGENT", 4) => new() { SemanticName = "TANGENT", SemanticIndex = 4, Format = DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM, PackedFormat = (DXGI_FORMAT)48, ByteSize = 4 },

            // COLOR0 only exists in FH3+ � if target doesn't support it, drop it
            ("COLOR", 0) when hasColor => new() { SemanticName = "COLOR", SemanticIndex = 0, Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM, PackedFormat = (DXGI_FORMAT)22, ByteSize = 4 },
            ("COLOR", 0) => null, // Not supported in FH2/FM5/FM6/FM7

            // TEXCOORD0-3 always use UNORM16x2
            ("TEXCOORD", >= 0 and <= 3) => new() { SemanticName = "TEXCOORD", SemanticIndex = semanticIndex, Format = DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM, PackedFormat = (DXGI_FORMAT)46, ByteSize = 4 },

            // TEXCOORD4 only exists in FH3+
            ("TEXCOORD", 4) when hasTexcoord4 => new() { SemanticName = "TEXCOORD", SemanticIndex = 4, Format = DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM, PackedFormat = (DXGI_FORMAT)46, ByteSize = 4 },
            ("TEXCOORD", 4) => null, // Not supported in FH2/FM5/FM6/FM7

            _ => null // Unknown semantic � drop it
        };
    }

    // Determines required element additions for the target game based on the source's existing semantics.
    private static List<VLayElementInfo> GetRequiredAdditions(
        HashSet<(string name, short idx)> srcSemantics, ForzaGameTarget target)
    {
        var additions = new List<VLayElementInfo>();

        bool hasTangent = srcSemantics.Any(s => s.name == "TANGENT");
        bool hasAnyTexcoord = srcSemantics.Any(s => s.name == "TEXCOORD");
        bool hasTexcoord0 = srcSemantics.Contains(("TEXCOORD", 0));
        bool hasTexcoord1 = srcSemantics.Contains(("TEXCOORD", 1));
        bool hasTexcoord3 = srcSemantics.Contains(("TEXCOORD", 3));
        bool hasTangent0or1 = srcSemantics.Contains(("TANGENT", 0)) || srcSemantics.Contains(("TANGENT", 1));
        bool hasCompleteTexcoordSet = hasTexcoord0 && hasTexcoord1 &&
                                      srcSemantics.Contains(("TEXCOORD", 2)) && hasTexcoord3;

        bool targetHasColor = target is ForzaGameTarget.FH3 or ForzaGameTarget.FH4 or ForzaGameTarget.FH5;
        bool targetHasTexcoord4 = target is ForzaGameTarget.FH3 or ForzaGameTarget.FH4 or ForzaGameTarget.FH5 or ForzaGameTarget.FM2023;
        bool targetHasTangent2 = target is ForzaGameTarget.FH5 or ForzaGameTarget.FM2023;
        bool targetIsFH5 = target is ForzaGameTarget.FH5;

        // COLOR0: add if source has any tangent (i.e. not a minimal NORMAL-only layout) and target supports it
        if (targetHasColor && hasTangent && !srcSemantics.Contains(("COLOR", 0)))
        {
            additions.Add(new() { SemanticName = "COLOR", SemanticIndex = 0, Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM, PackedFormat = (DXGI_FORMAT)22, ByteSize = 4 });
        }

        // TEXCOORD0:
        // FH5: add whenever source has ANY texcoord but not TEXCOORD0
        // Other targets: add only if source has TEXCOORD1+ but not TEXCOORD0
        if (targetIsFH5 && !hasTexcoord0 && hasAnyTexcoord)
        {
            additions.Add(new() { SemanticName = "TEXCOORD", SemanticIndex = 0, Format = DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM, PackedFormat = (DXGI_FORMAT)46, ByteSize = 4 });
        }
        else if (!targetIsFH5 && target is ForzaGameTarget.FM2023 && !hasTexcoord0 && hasTexcoord1)
        {
            additions.Add(new() { SemanticName = "TEXCOORD", SemanticIndex = 0, Format = DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM, PackedFormat = (DXGI_FORMAT)46, ByteSize = 4 });
        }

        // TEXCOORD4: add ONLY if source has the COMPLETE texcoord set (TEXCOORD0 through TEXCOORD3).
        // Parts with partial texcoord sets (e.g. glass parts missing TEXCOORD0) should NOT get TEXCOORD4,
        // even if they have TEXCOORD3. This matches observed FH5 behavior.
        if (targetHasTexcoord4 && hasCompleteTexcoordSet && !srcSemantics.Contains(("TEXCOORD", 4)))
        {
            additions.Add(new() { SemanticName = "TEXCOORD", SemanticIndex = 4, Format = DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM, PackedFormat = (DXGI_FORMAT)46, ByteSize = 4 });
        }

        // TANGENT2:
        // FH5: add whenever source has ANY tangent OR any texcoord (not just NORMAL-only).
        //   FH5 data shows TANGENT2 in 3-elem layouts like NORMAL+TANGENT2+TEXCOORD0.
        // Other targets: add only if source has TANGENT0 or TANGENT1.
        if (targetHasTangent2 && !srcSemantics.Contains(("TANGENT", 2)))
        {
            if (targetIsFH5 && (hasTangent || hasAnyTexcoord))
            {
                additions.Add(new() { SemanticName = "TANGENT", SemanticIndex = 2, Format = DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM, PackedFormat = (DXGI_FORMAT)48, ByteSize = 4 });
            }
            else if (!targetIsFH5 && hasTangent0or1)
            {
                additions.Add(new() { SemanticName = "TANGENT", SemanticIndex = 2, Format = DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM, PackedFormat = (DXGI_FORMAT)48, ByteSize = 4 });
            }
        }

        return additions;
    }

    // Gets the canonical element order for the target game. Elements should be sorted
    // to match the target game's expected order: NORMAL, TEXCOORD0-N, TANGENT0-N, COLOR.
    // This matches the observed element ordering in FH5/FH4/FH3 VLay blobs.
    private static int GetElementSortKey(string semanticName, short semanticIndex)
    {
        return semanticName switch
        {
            "NORMAL" => 0 + semanticIndex,
            "TEXCOORD" => 100 + semanticIndex,
            "TANGENT" => 200 + semanticIndex,
            "COLOR" => 300 + semanticIndex,
            _ => 900 + semanticIndex
        };
    }

    // Computes VLay flags based on the actual elements present in the layout.
    // Matches the game's m_MaterialSystemUsage bit mapping confirmed in the RE notes.
    private static uint ComputeVlayFlags(List<VLayElementInfo> elements, ForzaGameTarget target)
    {
        uint flags = 0;

        foreach (var element in elements)
        {
            if (element.SemanticName == "TEXCOORD" && element.SemanticIndex is >= 0 and <= 5)
            {
                flags |= 1u << element.SemanticIndex;
            }
            else if (element.SemanticName == "TANGENT" && element.SemanticIndex is >= 0 and <= 4)
            {
                flags |= 1u << (element.SemanticIndex + 5);
            }
            else if (element.SemanticName == "COLOR" && element.SemanticIndex == 0)
            {
                flags |= 0x400;
            }
        }

        return flags;
    }

    // Converts VLay blob data in-place for the target game using source-aware layout conversion.
    private VLayConversionMap ConvertVlayBlobData(BundleBlob blob, ForzaGameTarget target, ConversionResult result, string? modelbinFileName = null)
    {
        if (blob is not VertexLayoutBlob vlay)
        {
            result.Log.Add($"  VLay blob -> version updated (raw blob, no element conversion)");
            return null;
        }

        // Check if this is a position-only layout (slot 0 only, no slot 1 elements)
        bool hasSlot1 = vlay.Elements.Any(e => e.InputSlot == 1);
        if (!hasSlot1)
        {
            result.Log.Add($"  VLay (position-only) -> no element conversion needed");
            return null;
        }

        // Analyze current slot 1 layout
        var srcSlot1Elements = new List<(D3D12_INPUT_LAYOUT_DESC desc, int byteOff, int byteSize)>();
        int srcByteOffset = 0;
        foreach (var elem in vlay.Elements)
        {
            if (elem.InputSlot != 1) continue;
            int size = GetDxgiFormatByteSize(elem.Format);
            srcSlot1Elements.Add((elem, srcByteOffset, size));
            srcByteOffset += size;
        }
        int srcSlot1Stride = srcByteOffset;

        // Build the set of source semantics
        var srcSemantics = new HashSet<(string name, short idx)>();
        foreach (var (desc, _, _) in srcSlot1Elements)
        {
            string name = vlay.SemanticNames[desc.SemanticNameIndex];
            srcSemantics.Add((name, desc.SemanticIndex));
        }

        // Log pattern category from VLay advisor
        string patternDesc = VLayPatternAdvisor.GetPatternDescription(
            modelbinFileName, srcSlot1Elements.Count, srcSlot1Stride, target);
        result.Log.Add($"  VLay pattern: {patternDesc}");

        // Get suppressed additions based on filename + source layout analysis
        var suppressedAdditions = VLayPatternAdvisor.GetSuppressedAdditions(
            modelbinFileName, srcSemantics, srcSlot1Elements.Count, srcSlot1Stride, target);

        if (suppressedAdditions.Count > 0)
        {
            result.Log.Add($"    Suppressed additions: {string.Join(", ", suppressedAdditions.Select(s => $"{s.name}{s.idx}"))}");
        }

        // Step 1: Convert each existing source element to the target format, dropping unsupported ones
        var targetElements = new List<VLayElementInfo>();
        foreach (var (name, idx) in srcSemantics.OrderBy(s => GetElementSortKey(s.name, s.idx)))
        {
            var targetElem = GetTargetElementFormat(name, idx, target);
            if (targetElem != null)
                targetElements.Add(targetElem);
            else
                result.Log.Add($"    {name}{idx}: dropped (not supported in {target})");
        }

        // Step 2: Add required elements the target needs, filtering by suppression list
        var additions = GetRequiredAdditions(srcSemantics, target);
        foreach (var add in additions)
        {
            if (suppressedAdditions.Contains((add.SemanticName, add.SemanticIndex)))
            {
                result.Log.Add($"    {add.SemanticName}{add.SemanticIndex}: suppressed (pattern advisor)");
                continue;
            }

            if (!targetElements.Any(e => e.SemanticName == add.SemanticName && e.SemanticIndex == add.SemanticIndex))
            {
                targetElements.Add(add);
                result.Log.Add($"    {add.SemanticName}{add.SemanticIndex}: added (required by {target})");
            }
        }

        // Step 3: Sort into canonical order
        targetElements.Sort((a, b) =>
        {
            int ka = GetElementSortKey(a.SemanticName, a.SemanticIndex);
            int kb = GetElementSortKey(b.SemanticName, b.SemanticIndex);
            return ka.CompareTo(kb);
        });

        int targetSlot1Stride = targetElements.Sum(e => e.ByteSize);

        // Build name-to-index lookup, adding any missing semantic names
        var nameToIndex = new Dictionary<string, short>();
        for (int i = 0; i < vlay.SemanticNames.Count; i++)
            nameToIndex[vlay.SemanticNames[i]] = (short)i;

        foreach (var tElem in targetElements)
        {
            if (!nameToIndex.ContainsKey(tElem.SemanticName))
            {
                nameToIndex[tElem.SemanticName] = (short)vlay.SemanticNames.Count;
                vlay.SemanticNames.Add(tElem.SemanticName);
            }
        }

        // Build byte-level mapping from old layout to new, tracking format conversions
        var convMap = new VLayConversionMap
        {
            OldSlot1Stride = srcSlot1Stride,
            NewSlot1Stride = targetSlot1Stride
        };

        int dstByteOffset = 0;
        foreach (var tElem in targetElements)
        {
            // Try to find matching element in source by semantic name + index
            var srcMatch = srcSlot1Elements.FirstOrDefault(s =>
            {
                string srcName = vlay.SemanticNames[s.desc.SemanticNameIndex];
                return srcName == tElem.SemanticName && s.desc.SemanticIndex == tElem.SemanticIndex;
            });

            if (srcMatch.desc != null)
            {
                // Check if formats differ
                var conversion = DetectFormatConversion(srcMatch.desc.Format, tElem.Format);
                if (conversion != VLayFormatConversion.None)
                {
                    convMap.FormatConversions.Add((srcMatch.byteOff, dstByteOffset, srcMatch.byteSize, tElem.ByteSize, conversion));
                    result.Log.Add($"    {tElem.SemanticName}{tElem.SemanticIndex}: format conversion {srcMatch.desc.Format} -> {tElem.Format}");
                }
                else
                {
                    int copySize = Math.Min(srcMatch.byteSize, tElem.ByteSize);
                    convMap.ByteMappings.Add((srcMatch.byteOff, dstByteOffset, copySize));
                }
            }
            // else: new element, zero-filled in VerB

            dstByteOffset += tElem.ByteSize;
        }

        // Rebuild the Elements and PackedFormats lists
        var positionElement = vlay.Elements.FirstOrDefault(e => e.InputSlot == 0);
        var positionPackedFmt = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        if (positionElement != null)
        {
            int posIdx = vlay.Elements.IndexOf(positionElement);
            if (posIdx >= 0 && posIdx < vlay.PackedFormats.Count)
                positionPackedFmt = vlay.PackedFormats[posIdx];
        }

        vlay.Elements.Clear();
        vlay.PackedFormats.Clear();

        // Re-add POSITION element (slot 0)
        if (positionElement != null)
        {
            vlay.Elements.Add(positionElement);
            vlay.PackedFormats.Add(positionPackedFmt != DXGI_FORMAT.DXGI_FORMAT_UNKNOWN
                ? positionPackedFmt
                : (DXGI_FORMAT)49);
        }

        // Add target slot 1 elements
        foreach (var tElem in targetElements)
        {
            vlay.Elements.Add(new D3D12_INPUT_LAYOUT_DESC
            {
                SemanticNameIndex = nameToIndex[tElem.SemanticName],
                SemanticIndex = tElem.SemanticIndex,
                InputSlot = 1,
                InputSlotClass = 0,
                Format = tElem.Format,
                AlignedByteOffset = -1, // D3D12_APPEND_ALIGNED_ELEMENT
                InstanceDataStepRate = 0
            });
            vlay.PackedFormats.Add(tElem.PackedFormat);
        }

        vlay.Flags = ComputeVlayFlags(targetElements, target);

        int elementsAdded = targetElements.Count - srcSlot1Elements.Count;
        int fmtConvCount = convMap.FormatConversions.Count;
        string changeDesc = elementsAdded switch
        {
            > 0 => $"+{elementsAdded} elements",
            < 0 => $"{elementsAdded} elements",
            _ when fmtConvCount > 0 => "format changes only",
            _ => "no changes"
        };
        if (fmtConvCount > 0)
            changeDesc += $", {fmtConvCount} format conversion(s)";
        result.Log.Add($"  VLay slot1: stride {srcSlot1Stride} -> {targetSlot1Stride} ({changeDesc}, {targetElements.Count} elements)");

        return convMap;
    }

    // Gets the byte size of a DXGI_FORMAT used in vertex layouts.
    internal static int GetDxgiFormatByteSize(DXGI_FORMAT format)
    {
        return format switch
        {
            DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT => 16,
            DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT => 12,
            DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT => 8,
            DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM => 8,
            DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM => 8,
            DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT => 8,
            DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM => 4,
            DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM => 4,
            DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SNORM => 4,
            DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT => 4,
            DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM => 4,
            DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM => 4,
            DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT => 4,
            DXGI_FORMAT.DXGI_FORMAT_R32_UINT => 4,
            DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM => 2,
            DXGI_FORMAT.DXGI_FORMAT_R8G8_SNORM => 2,
            DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT => 2,
            DXGI_FORMAT.DXGI_FORMAT_R16_UNORM => 2,
            DXGI_FORMAT.DXGI_FORMAT_R16_SNORM => 2,
            DXGI_FORMAT.DXGI_FORMAT_R8_UNORM => 1,
            _ => 4
        };
    }

    // Detects what format conversion is needed between a source and target DXGI format.
    private static VLayFormatConversion DetectFormatConversion(DXGI_FORMAT srcFormat, DXGI_FORMAT dstFormat)
    {
        if (srcFormat == dstFormat)
            return VLayFormatConversion.None;

        // NORMAL: R16G16B16A16_FLOAT (FH2) ? R16G16_SNORM (FM6+)
        if (srcFormat == DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT && dstFormat == DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM)
            return VLayFormatConversion.Float16x4_To_Snorm16x2;
        if (srcFormat == DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM && dstFormat == DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT)
            return VLayFormatConversion.Snorm16x2_To_Float16x4;

        // TANGENT: R16G16B16A16_FLOAT (FH2) ? R10G10B10A2_UNORM (FM6+)
        if (srcFormat == DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT && dstFormat == DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM)
            return VLayFormatConversion.Float16x4_To_R10G10B10A2;
        if (srcFormat == DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM && dstFormat == DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT)
            return VLayFormatConversion.R10G10B10A2_To_Float16x4;

        return VLayFormatConversion.None;
    }

    // Converts VerB blob data in-place using the conversion map (handles stride changes and format conversions).
    private static void ConvertVerbBlobData(BundleBlob blob, VLayConversionMap convMap, ConversionResult result)
    {
        if (blob is not VertexBufferBlob verbBlob)
        {
            result.Log.Add($"  VerB -> raw blob, cannot patch stride");
            return;
        }

        var header = verbBlob.Header;
        byte[] srcData = header.GetRawData();
        if (srcData == null || srcData.Length == 0 || header.Stride == 0)
        {
            result.Log.Add($"  VerB -> empty data, skipping");
            return;
        }

        int srcStride = header.Stride;
        int vertexCount = srcData.Length / srcStride;

        // The VerB stride should match either the old VLay stride (pre-conversion)
        // or already match the new stride (no conversion needed).
        if (srcStride == convMap.NewSlot1Stride)
        {
            // Already at target stride � only apply format conversions if any
            if (convMap.FormatConversions.Count == 0)
            {
                result.Log.Add($"  VerB -> already at target stride {srcStride}, skipping");
                return;
            }
        }
        else if (srcStride != convMap.OldSlot1Stride)
        {
            result.Log.Add($"  VerB -> stride {srcStride} doesn't match expected {convMap.OldSlot1Stride}, skipping");
            return;
        }

        int newStride = convMap.NewSlot1Stride;
        byte[] newData = new byte[vertexCount * newStride];

        for (int v = 0; v < vertexCount; v++)
        {
            int srcBase = v * srcStride;
            int dstBase = v * newStride;

            // Copy mapped byte ranges (direct copies where formats match)
            foreach (var (srcOff, dstOff, size) in convMap.ByteMappings)
            {
                Buffer.BlockCopy(srcData, srcBase + srcOff, newData, dstBase + dstOff, size);
            }

            // Apply format conversions
            foreach (var (srcOff, dstOff, srcSize, dstSize, conversion) in convMap.FormatConversions)
            {
                ConvertVertexElement(srcData, srcBase + srcOff, newData, dstBase + dstOff, conversion);
            }

            // New elements that don't have a source mapping are left as zero (default fill)
        }

        header.SetRawData(newData, vertexCount, (ushort)newStride);

        var idMeta = blob.GetMetadataByTag<IdentifierMetadata>(BundleMetadata.TAG_METADATA_Identifier);
        string idStr = idMeta != null ? $"[{idMeta.Id}]" : "";
        int fmtCount = convMap.FormatConversions.Count;
        string fmtStr = fmtCount > 0 ? $", {fmtCount} format conversion(s)" : "";
        result.Log.Add($"  VerB{idStr}: stride {srcStride} -> {newStride}, {vertexCount} vertices patched{fmtStr}");
    }

    // Converts a single vertex element from one DXGI format to another.
    private static void ConvertVertexElement(byte[] srcData, int srcOffset, byte[] dstData, int dstOffset, VLayFormatConversion conversion)
    {
        switch (conversion)
        {
            case VLayFormatConversion.Float16x4_To_Snorm16x2:
                {
                    ushort rHalf = BitConverter.ToUInt16(srcData, srcOffset);
                    ushort gHalf = BitConverter.ToUInt16(srcData, srcOffset + 2);
                    float rFloat = HalfToFloat(rHalf);
                    float gFloat = HalfToFloat(gHalf);
                    short rSnorm = FloatToSnorm16(rFloat);
                    short gSnorm = FloatToSnorm16(gFloat);
                    BitConverter.TryWriteBytes(dstData.AsSpan(dstOffset), rSnorm);
                    BitConverter.TryWriteBytes(dstData.AsSpan(dstOffset + 2), gSnorm);
                }
                break;

            case VLayFormatConversion.Snorm16x2_To_Float16x4:
                {
                    short rSnorm = BitConverter.ToInt16(srcData, srcOffset);
                    short gSnorm = BitConverter.ToInt16(srcData, srcOffset + 2);
                    float rFloat = Snorm16ToFloat(rSnorm);
                    float gFloat = Snorm16ToFloat(gSnorm);
                    ushort rHalf = FloatToHalf(rFloat);
                    ushort gHalf = FloatToHalf(gFloat);
                    ushort bHalf = FloatToHalf(0.0f);
                    ushort aHalf = FloatToHalf(1.0f);
                    BitConverter.TryWriteBytes(dstData.AsSpan(dstOffset), rHalf);
                    BitConverter.TryWriteBytes(dstData.AsSpan(dstOffset + 2), gHalf);
                    BitConverter.TryWriteBytes(dstData.AsSpan(dstOffset + 4), bHalf);
                    BitConverter.TryWriteBytes(dstData.AsSpan(dstOffset + 6), aHalf);
                }
                break;

            case VLayFormatConversion.Float16x4_To_R10G10B10A2:
                {
                    ushort rH = BitConverter.ToUInt16(srcData, srcOffset);
                    ushort gH = BitConverter.ToUInt16(srcData, srcOffset + 2);
                    ushort bH = BitConverter.ToUInt16(srcData, srcOffset + 4);
                    ushort aH = BitConverter.ToUInt16(srcData, srcOffset + 6);
                    float r = Math.Clamp(HalfToFloat(rH), 0f, 1f);
                    float g = Math.Clamp(HalfToFloat(gH), 0f, 1f);
                    float b = Math.Clamp(HalfToFloat(bH), 0f, 1f);
                    float a = Math.Clamp(HalfToFloat(aH), 0f, 1f);
                    r = (r + 1.0f) * 0.5f;
                    g = (g + 1.0f) * 0.5f;
                    b = (b + 1.0f) * 0.5f;
                    r = Math.Clamp(r, 0f, 1f);
                    g = Math.Clamp(g, 0f, 1f);
                    b = Math.Clamp(b, 0f, 1f);
                    uint rBits = (uint)(r * 1023f + 0.5f) & 0x3FF;
                    uint gBits = (uint)(g * 1023f + 0.5f) & 0x3FF;
                    uint bBits = (uint)(b * 1023f + 0.5f) & 0x3FF;
                    uint aBits = (uint)(a * 3f + 0.5f) & 0x3;
                    uint packed = rBits | (gBits << 10) | (bBits << 20) | (aBits << 30);
                    BitConverter.TryWriteBytes(dstData.AsSpan(dstOffset), packed);
                }
                break;

            case VLayFormatConversion.R10G10B10A2_To_Float16x4:
                {
                    uint packed = BitConverter.ToUInt32(srcData, srcOffset);
                    float r = (packed & 0x3FF) / 1023f;
                    float g = ((packed >> 10) & 0x3FF) / 1023f;
                    float b = ((packed >> 20) & 0x3FF) / 1023f;
                    float a = ((packed >> 30) & 0x3) / 3f;
                    r = r * 2.0f - 1.0f;
                    g = g * 2.0f - 1.0f;
                    b = b * 2.0f - 1.0f;
                    ushort rH = FloatToHalf(r);
                    ushort gH = FloatToHalf(g);
                    ushort bH = FloatToHalf(b);
                    ushort aH = FloatToHalf(a);
                    BitConverter.TryWriteBytes(dstData.AsSpan(dstOffset), rH);
                    BitConverter.TryWriteBytes(dstData.AsSpan(dstOffset + 2), gH);
                    BitConverter.TryWriteBytes(dstData.AsSpan(dstOffset + 4), bH);
                    BitConverter.TryWriteBytes(dstData.AsSpan(dstOffset + 6), aH);
                }
                break;
        }
    }

    #endregion

    #region Half-Float / SNORM Conversion Helpers

    private static float HalfToFloat(ushort half)
    {
        return (float)BitConverter.UInt16BitsToHalf(half);
    }

    private static ushort FloatToHalf(float value)
    {
        return BitConverter.HalfToUInt16Bits((Half)value);
    }

    private static short FloatToSnorm16(float value)
    {
        value = Math.Clamp(value, -1f, 1f);
        return (short)(value * 32767f + (value >= 0 ? 0.5f : -0.5f));
    }

    private static float Snorm16ToFloat(short value)
    {
        return Math.Max(value / 32767f, -1f);
    }

    #endregion

    #region Modelbin Blob Converters

    private static void ConvertModlBlob(BundleBlob blob, (byte maj, byte min) targetVer, ForzaGameTarget target, ConversionResult result)
    {
        blob.VersionMajor = targetVer.maj;
        blob.VersionMinor = targetVer.min;

        if (blob is ModelBlob modl)
        {
            if (targetVer.min >= 2 && modl.DecompressFlags == 0)
                modl.DecompressFlags = 0x01;
            else if (targetVer.min < 2)
                modl.DecompressFlags = 0;
        }

        result.Log.Add($"  Modl blob -> v{targetVer.maj}.{targetVer.min}");
    }

    private static void ConvertMeshBlob(BundleBlob blob, (byte maj, byte min) targetVer, ForzaGameTarget target, ConversionResult result)
    {
        byte srcMaj = blob.VersionMajor;
        byte srcMin = blob.VersionMinor;

        blob.VersionMajor = targetVer.maj;
        blob.VersionMinor = targetVer.min;

        if (blob is MeshBlob mesh)
        {
            string meshName = GetNormalizedMetadataName(mesh) ?? "?";

            if (srcMin < 9 && targetVer.min >= 9)
            {
                mesh.MaterialIds = [-1, mesh.MaterialId, -1, -1];
                result.Log.Add($"  Mesh '{meshName}': Upgraded MaterialId {mesh.MaterialId} to MaterialIds array");
            }
            else if (srcMin >= 9 && targetVer.min < 9)
            {
                if (mesh.MaterialIds != null && mesh.MaterialIds.Length >= 2)
                {
                    mesh.MaterialId = mesh.MaterialIds[1];
                    result.Log.Add($"  Mesh '{meshName}': Downgraded MaterialIds array to single MaterialId {mesh.MaterialId}");
                }
                mesh.MaterialIds = null;
            }

            if (srcMin < 8 && targetVer.min >= 8)
            {
                if (mesh.PositionScale == default) mesh.PositionScale = System.Numerics.Vector4.One;
                if (mesh.PositionTranslate == default) mesh.PositionTranslate = System.Numerics.Vector4.Zero;
            }

            if (srcMin < 6 && targetVer.min >= 6)
            {
                if (mesh.ACMR == 0) mesh.ACMR = 0.65f;
            }

            if (srcMin < 5 && targetVer.min >= 5)
            {
                mesh.TexCoordTransforms ??= new System.Numerics.Vector4[5];
                for (int i = 0; i < 5; i++)
                {
                    if (mesh.TexCoordTransforms[i] == default)
                        mesh.TexCoordTransforms[i] = new System.Numerics.Vector4(0, 1, 0, 1);
                }
            }

            if (srcMin < 4 && targetVer.min >= 4)
            {
                if (mesh.MorphDataBufferIndex == 0) mesh.MorphDataBufferIndex = -1;
                if (mesh.SkinningDataBufferIndex == 0) mesh.SkinningDataBufferIndex = -1;
            }

            if (srcMin < 3 && targetVer.min >= 3)
            {
                mesh.IsMorphDamage = true;
            }

            if (srcMin < 2 && targetVer.min >= 2)
            {
                if (mesh.MorphWeightsCount == 0) mesh.MorphWeightsCount = 1;
            }
        }
    }

    private static void ConvertMatiBlob(BundleBlob blob, ConversionResult result)
    {
        result.Log.Add($"  MatI blob kept at v{blob.VersionMajor}.{blob.VersionMinor}");
    }

    #endregion
}
