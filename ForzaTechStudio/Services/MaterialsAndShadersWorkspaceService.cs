using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ForzaTechStudio.Models;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ForzaTechStudio.Services;

public enum MaterialShaderAssetKind
{
    Materialbin,
    Shaderbin,
}

public sealed class MaterialsAndShadersWorkspace
{
    public MaterialShaderAssetKind PrimaryKind { get; set; }
    public string PrimarySourcePath { get; set; } = string.Empty;
    public string? SelectedGameId { get; set; }
    public string? SelectedGameRootPath { get; set; }
    public string? LinkedShaderPathHint { get; set; }
    public string? ResolvedLinkedShaderSource { get; set; }
    public MaterialDocumentSnapshot? MaterialDocument { get; set; }
    public ShaderDocumentSnapshot? ShaderDocument { get; set; }
    public List<MaterialShaderReferenceNode> ReferenceChain { get; set; } = [];
    public List<MaterialShaderValidationIssue> ValidationIssues { get; set; } = [];
    public List<MaterialShaderTextureReference> TextureReferences { get; set; } = [];
    public List<MaterialShaderMappingItem> ConstantBufferMappings { get; set; } = [];
    public List<MaterialShaderMappingItem> TextureMappings { get; set; } = [];
    public List<MaterialShaderMappingItem> SamplerMappings { get; set; } = [];
    public List<MaterialShaderScenarioItem> Scenarios { get; set; } = [];
    public List<MaterialShaderRenderTargetItem> RenderTargets { get; set; } = [];
    public List<MaterialShaderRawBlobItem> RawBlobs { get; set; } = [];
}

public sealed class MaterialDocumentSnapshot
{
    internal Bundle SourceBundle { get; set; } = null!;
    public string DisplayName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string SourceDisplayPath { get; set; } = string.Empty;
    public string BundleVersionText { get; set; } = string.Empty;
    public string ShaderPath { get; set; } = string.Empty;
    public string ShaderPathV1_1 { get; set; } = string.Empty;
    public string ShaderPathV1_2 { get; set; } = string.Empty;
    public string AtlasSummary { get; set; } = string.Empty;
    public string FooterSummary { get; set; } = string.Empty;
    public ObservableCollection<ShaderParameter> Parameters { get; set; } = [];
}

public sealed class ShaderDocumentSnapshot
{
    internal Bundle SourceBundle { get; set; } = null!;
    public string DisplayName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string SourceDisplayPath { get; set; } = string.Empty;
    public string BundleVersionText { get; set; } = string.Empty;
    public string BlendSummary { get; set; } = string.Empty;
    public string PresenceSummary { get; set; } = string.Empty;
    public ObservableCollection<ShaderParameter> DefaultParameters { get; set; } = [];
}

public sealed class MaterialShaderLibraryAsset
{
    public string AssetKind { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string GamePath { get; set; } = string.Empty;
    public string ArchiveSource { get; set; } = string.Empty;
    public string SecondaryText => string.IsNullOrWhiteSpace(ArchiveSource)
        ? GamePath
        : $"{GamePath}  |  {ArchiveSource}";
}

public sealed class MaterialShaderReferenceNode
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class MaterialShaderValidationIssue
{
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class MaterialShaderTextureReference : ObservableObject
{
    public string ParameterName { get; set; } = string.Empty;
    public string NameHashText { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string SourceState { get; set; } = string.Empty;
    public bool IsExplicitOverride { get; set; }
    public string TexturePath { get; set; } = string.Empty;
    public string PathHashText { get; set; } = string.Empty;
    public string ResolvedSource { get; set; } = string.Empty;
    public string DefaultTexturePath { get; set; } = string.Empty;
    public string DefaultPathHashText { get; set; } = string.Empty;
    public string DefaultResolvedSource { get; set; } = string.Empty;
    public string DefaultStatusText { get; set; } = string.Empty;
    public bool CanOpenResolvedSource { get; set; }
    public bool CanOpenDefaultResolvedSource { get; set; }
    public bool IsResolved { get; set; }
    public string SizeText { get; set; } = string.Empty;
    public string MipLevelsText { get; set; } = string.Empty;
    public string FormatText { get; set; } = string.Empty;
    public string EncodingText { get; set; } = string.Empty;
    public string TranscodingText { get; set; } = string.Empty;
    public string ColorProfileText { get; set; } = string.Empty;
    public string PlatformText { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;

    internal SwatchbinInfo? PreviewSwatchInfo { get; set; }

    private BitmapImage? _previewImage;
    private bool _isPreviewLoading;
    private string _previewStateText = "Preview unavailable.";

    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        set
        {
            if (SetProperty(ref _previewImage, value))
            {
                OnPropertyChanged(nameof(HasPreviewImage));
                OnPropertyChanged(nameof(ShowPreviewPlaceholder));
            }
        }
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        set
        {
            if (SetProperty(ref _isPreviewLoading, value))
                OnPropertyChanged(nameof(ShowPreviewPlaceholder));
        }
    }

    public string PreviewStateText
    {
        get => _previewStateText;
        set => SetProperty(ref _previewStateText, value);
    }

    public bool HasPreviewImage => PreviewImage != null;

    public bool ShowPreviewPlaceholder => !HasPreviewImage && !IsPreviewLoading;
}

public sealed class MaterialShaderMappingItem
{
    public string Name { get; set; } = string.Empty;
    public string NameHashText { get; set; } = string.Empty;
    public string SlotText { get; set; } = string.Empty;
    // Byte offset inside the constant buffer (only set for CBMP; empty for TXMP/SPMP).
    public string ByteOffsetText { get; set; } = string.Empty;
    public string GuidText { get; set; } = string.Empty;
}

public sealed class MaterialShaderScenarioItem
{
    public string Name { get; set; } = string.Empty;
    public string ScenarioHashText { get; set; } = string.Empty;
    public string VersionText { get; set; } = string.Empty;
    public string AnimCountText { get; set; } = string.Empty;
    public string VertexShaderPaths { get; set; } = string.Empty;
    public string GeometryPixelShader { get; set; } = string.Empty;
    // Raw hex value of m_VertexShaderInputFlags from VDCL metadata (e.g. "0x00000023").
    public string VertexInputFlags { get; set; } = string.Empty;
    // Decoded semantic names (e.g. "TEXCOORD0 | TEXCOORD1 | TANGENT0").
    public string VertexInputFlagsDecoded { get; set; } = string.Empty;
    public string StageBitsText { get; set; } = string.Empty;
    public string InlineState { get; set; } = string.Empty;
    // Shown when BLEN metadata is present on the parent LSCE blob.
    public string BlendFlagsText { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class MaterialShaderRenderTargetItem
{
    public string Label { get; set; } = string.Empty;
    public string VersionText { get; set; } = string.Empty;
    public string InlineState { get; set; } = string.Empty;
    public string PayloadLengthText { get; set; } = string.Empty;
    public string EntryCount { get; set; } = string.Empty;
    public string EntrySummary { get; set; } = string.Empty;
    // Newline-separated VS name list for the VS column (parallel to PSPaths).
    public string VSPaths { get; set; } = string.Empty;
    // Newline-separated PS name list for the PS column (parallel to VSPaths).
    public string PSPaths { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class MaterialShaderRawBlobItem
{
    public string DocumentKind { get; set; } = string.Empty;
    public string BlobTag { get; set; } = string.Empty;
    public string VersionText { get; set; } = string.Empty;
    public string MetadataTags { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class MaterialsAndShadersWorkspaceService
{
    private readonly SwatchbinService _swatchbinService = new();

    public MaterialsAndShadersWorkspace LoadFromFile(string filePath, string? gameId = null, string? gameRootPath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A file path is required.", nameof(filePath));

        var assetKind = GetAssetKindFromPath(filePath);
        var loaded = LoadBundleFromBytes(
            File.ReadAllBytes(filePath),
            filePath,
            null,
            filePath,
            assetKind);

        return BuildWorkspaceFromPrimarySource(loaded, gameId, gameRootPath);
    }

    public MaterialsAndShadersWorkspace LoadFromGamePath(string gameId, string gameRootPath, string gamePath)
    {
        if (!TryLoadBundleFromGamePath(gameRootPath, gamePath, out var loaded))
            throw new FileNotFoundException($"Could not load asset '{gamePath}' from '{gameRootPath}'.");

        return BuildWorkspaceFromPrimarySource(loaded!, gameId, gameRootPath);
    }

    public MaterialsAndShadersWorkspace AttachShaderFromFile(MaterialsAndShadersWorkspace currentWorkspace, string shaderFilePath)
    {
        ArgumentNullException.ThrowIfNull(currentWorkspace);

        var loaded = LoadBundleFromBytes(
            File.ReadAllBytes(shaderFilePath),
            shaderFilePath,
            null,
            shaderFilePath,
            MaterialShaderAssetKind.Shaderbin);

        return AttachShader(currentWorkspace, loaded);
    }

    public MaterialsAndShadersWorkspace AttachShaderFromGamePath(MaterialsAndShadersWorkspace currentWorkspace, string gameId, string gameRootPath, string gamePath)
    {
        ArgumentNullException.ThrowIfNull(currentWorkspace);

        if (!TryLoadBundleFromGamePath(gameRootPath, gamePath, out var loaded))
            throw new FileNotFoundException($"Could not load shader '{gamePath}' from '{gameRootPath}'.");

        currentWorkspace.SelectedGameId = gameId;
        currentWorkspace.SelectedGameRootPath = gameRootPath;
        return AttachShader(currentWorkspace, loaded!);
    }

    public string PrepareAssetFileForExternalViewer(string gameRootPath, string gamePath)
    {
        if (!TryLocateAssetBytesFromGamePath(gameRootPath, gamePath, out var assetBytes, out var resolvedSource, out _))
            throw new FileNotFoundException($"Could not resolve asset '{gamePath}' from '{gameRootPath}'.");

        if (Path.IsPathRooted(resolvedSource) && File.Exists(resolvedSource))
            return resolvedSource;

        string normalizedGamePath = NormalizeAssetGamePath(gamePath).Replace('/', '\\');
        string fileName = Path.GetFileName(normalizedGamePath);
        string extension = Path.GetExtension(fileName);

        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"viewer_asset_{Guid.NewGuid():N}{extension}";

        string tempRoot = Path.Combine(Path.GetTempPath(), "ForzaTechStudio", "LibraryViewerAssets");
        Directory.CreateDirectory(tempRoot);

        string tempFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{Guid.NewGuid():N}{extension}";
        string tempPath = Path.Combine(tempRoot, tempFileName);
        File.WriteAllBytes(tempPath, assetBytes);
        return tempPath;
    }

    // Serializes the primary document in the workspace back to <paramref name="filePath"/>.
    // For a materialbin workspace the material bundle is written; for shaderbin the shader bundle.
    public void SaveToFile(MaterialsAndShadersWorkspace workspace, string filePath)
    {
        Bundle? bundle = workspace.PrimaryKind == MaterialShaderAssetKind.Materialbin
            ? workspace.MaterialDocument?.SourceBundle
            : workspace.ShaderDocument?.SourceBundle;

        if (bundle == null)
            throw new InvalidOperationException("No source bundle is available to save.");

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        bundle.Serialize(stream);
    }

    public IReadOnlyList<MaterialShaderLibraryAsset> GetLibraryAssets(string gameId, string gameRootPath, string libraryFilter)
    {
        if (string.IsNullOrWhiteSpace(gameRootPath) || !Directory.Exists(gameRootPath))
            return [];

        bool showShaders = string.Equals(libraryFilter, "Shaderbins", StringComparison.OrdinalIgnoreCase);
        bool showSwatches = string.Equals(libraryFilter, "Swatchbins", StringComparison.OrdinalIgnoreCase);

        string indexedExtension = showSwatches
            ? ".swatchbin"
            : showShaders
                ? ".shaderbin"
                : ".materialbin";

        string assetKind = showSwatches
            ? "Swatchbin"
            : showShaders
                ? "Shaderbin"
                : "Materialbin";

        string archiveSourceLabel = showSwatches
            ? "Indexed swatch library"
            : showShaders
                ? "Indexed shader library"
                : "Indexed material library";

        string? zipSourceFilter = showSwatches
            ? null
            : showShaders
                ? null
                : "materials.zip";

        bool canUseIndexedLookup = GameAssetDatabaseService.DatabaseExists(gameId) &&
            (!showShaders || GameAssetDatabaseService.SupportsIndexedShaderbins(gameId));

        if (canUseIndexedLookup)
        {
            return GameAssetDatabaseService
                .GetAssetPaths(gameId, indexedExtension, zipSourceFilter)
                .Select(path => new MaterialShaderLibraryAsset
                {
                    AssetKind = assetKind,
                    DisplayName = Path.GetFileName(path.Replace('/', '\\')),
                    GamePath = path,
                    ArchiveSource = archiveSourceLabel,
                })
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.GamePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return ScanArchiveLibraryAssets(gameRootPath, indexedExtension)
            .Select(item => new MaterialShaderLibraryAsset
            {
                AssetKind = assetKind,
                DisplayName = Path.GetFileName(item.GamePath.Replace('/', '\\')),
                GamePath = item.GamePath,
                ArchiveSource = item.ArchiveSource,
            })
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.GamePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private MaterialsAndShadersWorkspace BuildWorkspaceFromPrimarySource(LoadedBundleSource loaded, string? gameId, string? gameRootPath)
    {
        var material = loaded.AssetKind == MaterialShaderAssetKind.Materialbin
            ? BuildMaterialDocument(loaded)
            : null;

        ShaderDocumentSnapshot? shader = null;
        string? linkedShaderHint = material?.ShaderPath;
        string? resolvedLinkedShaderSource = null;

        if (loaded.AssetKind == MaterialShaderAssetKind.Shaderbin)
        {
            shader = BuildShaderDocument(loaded);
        }
        else if (material != null && TryResolveLinkedShader(loaded, material, gameId, gameRootPath, out var linkedShader))
        {
            shader = BuildShaderDocument(linkedShader!);
            resolvedLinkedShaderSource = linkedShader!.SourceDisplayPath;
        }

        return BuildWorkspace(
            material,
            shader,
            loaded.AssetKind,
            loaded.SourceDisplayPath,
            gameId,
            gameRootPath,
            linkedShaderHint,
            resolvedLinkedShaderSource);
    }

    private MaterialsAndShadersWorkspace AttachShader(MaterialsAndShadersWorkspace currentWorkspace, LoadedBundleSource shaderSource)
    {
        var shader = BuildShaderDocument(shaderSource);

        return BuildWorkspace(
            currentWorkspace.MaterialDocument,
            shader,
            currentWorkspace.PrimaryKind,
            currentWorkspace.PrimarySourcePath,
            currentWorkspace.SelectedGameId,
            currentWorkspace.SelectedGameRootPath,
            currentWorkspace.LinkedShaderPathHint,
            shader.SourceDisplayPath);
    }

    private MaterialsAndShadersWorkspace BuildWorkspace(
        MaterialDocumentSnapshot? material,
        ShaderDocumentSnapshot? shader,
        MaterialShaderAssetKind primaryKind,
        string primarySourcePath,
        string? gameId,
        string? gameRootPath,
        string? linkedShaderHint,
        string? resolvedLinkedShaderSource)
    {
        string? localSearchDirectory = material?.SourcePath ?? shader?.SourcePath;
        if (!string.IsNullOrWhiteSpace(localSearchDirectory) && File.Exists(localSearchDirectory))
            localSearchDirectory = Path.GetDirectoryName(localSearchDirectory);

        var textures = BuildTextureReferences(material, shader, gameRootPath, localSearchDirectory);

        return new MaterialsAndShadersWorkspace
        {
            PrimaryKind = primaryKind,
            PrimarySourcePath = primarySourcePath,
            SelectedGameId = gameId,
            SelectedGameRootPath = gameRootPath,
            LinkedShaderPathHint = linkedShaderHint,
            ResolvedLinkedShaderSource = resolvedLinkedShaderSource,
            MaterialDocument = material,
            ShaderDocument = shader,
            ReferenceChain = BuildReferenceChain(material, shader, linkedShaderHint, resolvedLinkedShaderSource, textures),
            ValidationIssues = BuildValidationIssues(material, shader, gameRootPath, linkedShaderHint, resolvedLinkedShaderSource, textures),
            TextureReferences = textures,
            ConstantBufferMappings = BuildMappingItems(shader, Bundle.TAG_BLOB_CBMP),
            TextureMappings = BuildMappingItems(shader, Bundle.TAG_BLOB_TXMP),
            SamplerMappings = BuildMappingItems(shader, Bundle.TAG_BLOB_SPMP),
            Scenarios = BuildScenarioItems(shader),
            RenderTargets = BuildRenderTargetItems(shader),
            RawBlobs = BuildRawBlobItems(material, shader),
        };
    }

    private static MaterialDocumentSnapshot BuildMaterialDocument(LoadedBundleSource loaded)
    {
        var matl = loaded.Bundle.Blobs.OfType<MatLBlob>().FirstOrDefault();
        var overrides = loaded.Bundle.Blobs
            .OfType<MaterialShaderParameterBlob>()
            .FirstOrDefault(blob => blob.Tag == Bundle.TAG_BLOB_MaterialShaderParameter);
        var atlas = matl?.GetMetadataByTag<AtlasMetadata>(BundleMetadata.TAG_METADATA_Atlas);

        return new MaterialDocumentSnapshot
        {
            SourceBundle = loaded.Bundle,
            DisplayName = Path.GetFileName(loaded.SourceDisplayPath.Replace('/', '\\')),
            SourcePath = loaded.SourcePath,
            SourceDisplayPath = loaded.SourceDisplayPath,
            BundleVersionText = $"Bundle v{loaded.Bundle.VersionMajor}.{loaded.Bundle.VersionMinor}",
            ShaderPath = ChoosePreferredShaderPath(matl) ?? string.Empty,
            ShaderPathV1_1 = matl?.PathV1_1 ?? string.Empty,
            ShaderPathV1_2 = matl?.PathV1_2 ?? string.Empty,
            AtlasSummary = atlas == null
                ? "No ATST metadata exposed on MATL."
                : $"ATST v{atlas.Version}: FlagA={atlas.Unk}, FlagB={atlas.UnkV2}",
            FooterSummary = overrides == null
                ? "No MTPR override blob present."
                : $"Parameter CRC: 0x{overrides.Unk1:X8}  |  Texture CRC: 0x{overrides.Unk2:X8}  |  Sampler CRC: 0x{overrides.Unk3:X8}",
            Parameters = overrides?.Parameters ?? [],
        };
    }

    private static ShaderDocumentSnapshot BuildShaderDocument(LoadedBundleSource loaded)
    {
        var defaults = loaded.Bundle.Blobs
            .OfType<MaterialShaderParameterBlob>()
            .FirstOrDefault(blob => blob.Tag == Bundle.TAG_BLOB_DefaultShaderParameter);

        var blend = loaded.Bundle.Blobs
            .Select(blob => blob.GetMetadataByTag<BlendMetadata>(BundleMetadata.TAG_METADATA_BLEN))
            .FirstOrDefault(metadata => metadata != null);

        bool hasVers = loaded.Bundle.Blobs.Any(blob => blob.Tag == Bundle.TAG_BLOB_VERS);
        bool hasVars = loaded.Bundle.Blobs.Any(blob => blob.Tag == Bundle.TAG_BLOB_VARS);
        int lsceCount = loaded.Bundle.Blobs.Count(blob => blob.Tag == Bundle.TAG_BLOB_LightScenario);
        int trgtCount = loaded.Bundle.Blobs.Count(blob => blob.Tag == Bundle.TAG_BLOB_TRGT);

        return new ShaderDocumentSnapshot
        {
            SourceBundle = loaded.Bundle,
            DisplayName = Path.GetFileName(loaded.SourceDisplayPath.Replace('/', '\\')),
            SourcePath = loaded.SourcePath,
            SourceDisplayPath = loaded.SourceDisplayPath,
            BundleVersionText = $"Bundle v{loaded.Bundle.VersionMajor}.{loaded.Bundle.VersionMinor}",
            BlendSummary = blend == null
                ? "No BLEN metadata exposed."
                : $"BLEN v{blend.Version}: FlagA={blend.Unk1}, FlagB={blend.Unk2}",
            PresenceSummary = $"LSCE: {lsceCount}  |  TRGT: {trgtCount}  |  VERS: {(hasVers ? "Yes" : "No")}  |  VARS: {(hasVars ? "Yes" : "No")}",
            DefaultParameters = defaults?.Parameters ?? [],
        };
    }

    private List<MaterialShaderTextureReference> BuildTextureReferences(
        MaterialDocumentSnapshot? material,
        ShaderDocumentSnapshot? shader,
        string? gameRootPath,
        string? localSearchDirectory)
    {
        var cache = new Dictionary<string, (SwatchbinInfo? info, string resolvedSource, string statusText)>(StringComparer.OrdinalIgnoreCase);
        var defaultsByKey = BuildTextureParameterLookup(shader?.DefaultParameters);
        var overridesByKey = BuildTextureParameterLookup(material?.Parameters);

        return defaultsByKey.Keys
            .Union(overridesByKey.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                defaultsByKey.TryGetValue(key, out ShaderParameter? defaultParameter);
                overridesByKey.TryGetValue(key, out ShaderParameter? overrideParameter);

                ShaderParameter effectiveParameter = overrideParameter ?? defaultParameter!;
                var defaultTexture = defaultParameter?.Value as TextureParameter;
                var effectiveTexture = effectiveParameter.Value as TextureParameter;
                string defaultTexturePath = defaultTexture?.Path ?? string.Empty;
                string effectiveTexturePath = effectiveTexture?.Path ?? string.Empty;

                var effectiveDetails = GetTextureReferenceDetails(effectiveTexturePath, gameRootPath, localSearchDirectory, cache);
                var defaultDetails = string.Equals(defaultTexturePath, effectiveTexturePath, StringComparison.OrdinalIgnoreCase)
                    ? effectiveDetails
                    : GetTextureReferenceDetails(defaultTexturePath, gameRootPath, localSearchDirectory, cache);

                bool isExplicitOverride = overrideParameter != null;

                return new MaterialShaderTextureReference
                {
                    ParameterName = ResolveParameterName(effectiveParameter.NameHash),
                    NameHashText = $"0x{effectiveParameter.NameHash:X8}",
                    SourceLabel = isExplicitOverride ? "Material Override" : "Shader Default",
                    SourceState = isExplicitOverride ? "Explicit Override" : "Inherited Default",
                    IsExplicitOverride = isExplicitOverride,
                    TexturePath = effectiveTexturePath,
                    PathHashText = effectiveTexture == null ? string.Empty : $"0x{effectiveTexture.PathHash:X8}",
                    ResolvedSource = effectiveDetails.resolvedSource,
                    DefaultTexturePath = defaultTexturePath,
                    DefaultPathHashText = defaultTexture == null ? string.Empty : $"0x{defaultTexture.PathHash:X8}",
                    DefaultResolvedSource = defaultDetails.resolvedSource,
                    DefaultStatusText = defaultDetails.statusText,
                    CanOpenResolvedSource = IsFileBackedResolvedSource(effectiveDetails.resolvedSource),
                    CanOpenDefaultResolvedSource = IsFileBackedResolvedSource(defaultDetails.resolvedSource),
                    IsResolved = effectiveDetails.statusText == "Swatch metadata loaded.",
                    SizeText = effectiveDetails.info == null ? "Not resolved" : $"{effectiveDetails.info.Width} x {effectiveDetails.info.Height} x {effectiveDetails.info.Depth}",
                    MipLevelsText = effectiveDetails.info == null ? "-" : effectiveDetails.info.MipLevels.ToString(),
                    FormatText = effectiveDetails.info?.DxgiFormatName ?? "Not resolved",
                    EncodingText = effectiveDetails.info?.Encoding.ToString() ?? "Unknown",
                    TranscodingText = effectiveDetails.info?.Transcoding.ToString() ?? "Unknown",
                    ColorProfileText = effectiveDetails.info?.ColorProfile.ToString() ?? "Unknown",
                    PlatformText = effectiveDetails.info == null ? "Unknown" : effectiveDetails.info.IsDurangoFormat ? "Durango" : "PC",
                    StatusText = effectiveDetails.statusText,
                    PreviewSwatchInfo = effectiveDetails.info,
                    IsPreviewLoading = effectiveDetails.info != null,
                    PreviewStateText = effectiveDetails.info == null ? "Preview unavailable." : "Loading preview...",
                };
            })
            .OrderBy(item => item.ParameterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TexturePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Dictionary<string, ShaderParameter> BuildTextureParameterLookup(IEnumerable<ShaderParameter>? parameters)
    {
        return parameters == null
            ? new Dictionary<string, ShaderParameter>(StringComparer.OrdinalIgnoreCase)
            : parameters
                .Where(param => param.Type == ShaderParameterType.Texture2D)
                .GroupBy(BuildTextureReferenceKey)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private (SwatchbinInfo? info, string resolvedSource, string statusText) GetTextureReferenceDetails(
        string texturePath,
        string? gameRootPath,
        string? localSearchDirectory,
        Dictionary<string, (SwatchbinInfo? info, string resolvedSource, string statusText)> cache)
    {
        string cacheKey = texturePath.Trim();

        if (!cache.TryGetValue(cacheKey, out var cached))
        {
            cached = TryLoadSwatchbinInfo(texturePath, gameRootPath, localSearchDirectory);
            cache[cacheKey] = cached;
        }

        return cached;
    }

    private (SwatchbinInfo? info, string resolvedSource, string statusText) TryLoadSwatchbinInfo(string texturePath, string? gameRootPath, string? localSearchDirectory)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return (null, "No texture path recorded.", "Texture parameter has no path.");

        if (texturePath.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(gameRootPath) && TryLocateAssetBytesFromGamePath(gameRootPath, texturePath, out var assetBytes, out var resolvedSource, out _))
            {
                try
                {
                    using var stream = new MemoryStream(assetBytes, writable: false);
                    return (_swatchbinService.LoadSwatchbin(stream), resolvedSource ?? "Game asset", "Swatch metadata loaded.");
                }
                catch
                {
                    return (null, resolvedSource ?? "Game asset located but swatch parsing failed.", "Texture metadata could not be resolved yet.");
                }
            }

            return (null, "Game root is not configured or the swatch could not be found.", "Texture metadata could not be resolved yet.");
        }

        if (Path.IsPathRooted(texturePath) && File.Exists(texturePath))
        {
            try
            {
                return (_swatchbinService.LoadSwatchbin(texturePath), texturePath, "Swatch metadata loaded.");
            }
            catch
            {
                return (null, texturePath, "Texture metadata could not be resolved yet.");
            }
        }

        if (!string.IsNullOrWhiteSpace(localSearchDirectory))
        {
            string candidatePath = Path.Combine(localSearchDirectory, Path.GetFileName(texturePath.Replace('/', '\\')));
            if (File.Exists(candidatePath))
            {
                try
                {
                    return (_swatchbinService.LoadSwatchbin(candidatePath), candidatePath, "Swatch metadata loaded.");
                }
                catch
                {
                    return (null, candidatePath, "Texture metadata could not be resolved yet.");
                }
            }
        }

        return (null, "Texture has not been resolved yet.", "Texture metadata could not be resolved yet.");
    }

    private static string BuildTextureReferenceKey(ShaderParameter parameter)
    {
        return $"{parameter.NameHash:X8}|{(byte)parameter.Type:X2}";
    }

    private static bool IsFileBackedResolvedSource(string? resolvedSource)
    {
        return !string.IsNullOrWhiteSpace(resolvedSource)
            && Path.IsPathRooted(resolvedSource)
            && File.Exists(resolvedSource);
    }

    private static List<MaterialShaderReferenceNode> BuildReferenceChain(
        MaterialDocumentSnapshot? material,
        ShaderDocumentSnapshot? shader,
        string? linkedShaderHint,
        string? resolvedLinkedShaderSource,
        IReadOnlyList<MaterialShaderTextureReference> textures)
    {
        var chain = new List<MaterialShaderReferenceNode>();

        if (material != null)
        {
            chain.Add(new MaterialShaderReferenceNode
            {
                Label = "Material",
                Value = material.SourceDisplayPath,
            });
        }

        if (!string.IsNullOrWhiteSpace(linkedShaderHint))
        {
            chain.Add(new MaterialShaderReferenceNode
            {
                Label = "Linked Shader Path",
                Value = linkedShaderHint,
            });
        }

        if (shader != null)
        {
            chain.Add(new MaterialShaderReferenceNode
            {
                Label = "Shader",
                Value = shader.SourceDisplayPath,
            });
        }
        else if (!string.IsNullOrWhiteSpace(resolvedLinkedShaderSource))
        {
            chain.Add(new MaterialShaderReferenceNode
            {
                Label = "Shader",
                Value = resolvedLinkedShaderSource,
            });
        }

        foreach (var texture in textures
            .Where(item => !string.IsNullOrWhiteSpace(item.TexturePath))
            .GroupBy(item => item.TexturePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()))
        {
            chain.Add(new MaterialShaderReferenceNode
            {
                Label = "Texture",
                Value = texture.TexturePath,
            });
        }

        return chain;
    }

    private static List<MaterialShaderValidationIssue> BuildValidationIssues(
        MaterialDocumentSnapshot? material,
        ShaderDocumentSnapshot? shader,
        string? gameRootPath,
        string? linkedShaderHint,
        string? resolvedLinkedShaderSource,
        IReadOnlyList<MaterialShaderTextureReference> textures)
    {
        var issues = new List<MaterialShaderValidationIssue>();

        if (material != null && string.IsNullOrWhiteSpace(linkedShaderHint))
        {
            issues.Add(new MaterialShaderValidationIssue
            {
                Severity = "Warning",
                Message = "Materialbin has no linked shader path in MATL.",
            });
        }

        if (material != null && !string.IsNullOrWhiteSpace(linkedShaderHint) && shader == null)
        {
            issues.Add(new MaterialShaderValidationIssue
            {
                Severity = "Warning",
                Message = "Linked shader path is present but the shaderbin could not be resolved automatically.",
            });
        }

        if (material != null && !string.IsNullOrWhiteSpace(linkedShaderHint) && !string.IsNullOrWhiteSpace(resolvedLinkedShaderSource) && shader == null)
        {
            issues.Add(new MaterialShaderValidationIssue
            {
                Severity = "Info",
                Message = $"Linked shader source was identified at '{resolvedLinkedShaderSource}', but it did not parse as a shaderbin.",
            });
        }

        if (material != null && string.IsNullOrWhiteSpace(gameRootPath) && textures.Any(texture => texture.TexturePath.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new MaterialShaderValidationIssue
            {
                Severity = "Info",
                Message = "Select a configured game root from Setup to resolve Game:\\ texture references.",
            });
        }

        foreach (var texture in textures.Where(texture => texture.StatusText != "Swatch metadata loaded."))
        {
            issues.Add(new MaterialShaderValidationIssue
            {
                Severity = "Info",
                Message = $"Texture '{texture.ParameterName}' is not fully resolved: {texture.StatusText}",
            });
        }

        if (shader != null)
        {
            issues.Add(new MaterialShaderValidationIssue
            {
                Severity = "Info",
                Message = "LSCE and TRGT are shown as partial summaries until parser hardening is completed.",
            });
        }

        return issues;
    }

    private static List<MaterialShaderMappingItem> BuildMappingItems(ShaderDocumentSnapshot? shader, uint expectedTag)
    {
        if (shader == null)
            return [];

        var bundle = shader.SourceBundle;

        return bundle.Blobs
            .OfType<ShaderParameterMappingBlob>()
            .Where(blob => blob.Tag == expectedTag)
            .SelectMany(blob =>
            {
                bool isCbmp = expectedTag == Bundle.TAG_BLOB_CBMP;
                return blob.Mappings.Select(entry => new MaterialShaderMappingItem
                {
                    Name = entry.Name ?? ResolveParameterName(entry.NameHash),
                    NameHashText = entry.NameHash == 0 ? string.Empty : $"0x{entry.NameHash:X8}",
                    SlotText = entry.IdOrOffset.ToString(),
                    // Byte offset only meaningful for CBMP; shows effective (post-scale) value
                    ByteOffsetText = isCbmp ? entry.EffectiveByteOffset.ToString() : string.Empty,
                    GuidText = entry.Guid == Guid.Empty ? string.Empty : entry.Guid.ToString(),
                });
            })
            .ToList();
    }

    private static List<MaterialShaderScenarioItem> BuildScenarioItems(ShaderDocumentSnapshot? shader)
    {
        if (shader == null)
            return [];

        var bundle = shader.SourceBundle;
        var items = new List<MaterialShaderScenarioItem>();

        foreach (var blob in bundle.Blobs.OfType<LightScenarioBlob>())
        {
            var vdclEntries = blob
                .GetMetadataByTag<VDCLMetadata>(BundleMetadata.TAG_METADATA_VDCL)?.Entries;
            var blend = blob.GetMetadataByTag<BlendMetadata>(BundleMetadata.TAG_METADATA_BLEN);

            // Build BLEN blend-flag summary once per blob
            string blenText = blend == null
                ? string.Empty
                : $"Transparent={blend.Unk1}  AlphaTested={blend.Unk2}";

            foreach (var scenario in blob.LightScenarios)
            {
                uint nameHash = HashScenarioName(scenario.Name);
                var vdclEntry = vdclEntries?.FirstOrDefault(e => e.NameHash == nameHash);

                string rawVdclFlags = vdclEntry != null
                    ? $"0x{vdclEntry.VertexInputFlags:X8}"
                    : vdclEntries != null ? string.Empty : string.Empty;
                string vertexInputFlags = vdclEntry != null
                    ? rawVdclFlags
                    : vdclEntries != null ? "No matching VDCL entry" : "No VDCL metadata";
                string decodedSemantics = vdclEntry != null
                    ? DecodeVDCLFlags(vdclEntry.VertexInputFlags)
                    : string.Empty;

                // Format VS paths: show regular + instanced if present
                string vsPaths;
                if (scenario.VertexShaders.Count == 0)
                {
                    vsPaths = "No vertex shader entries";
                }
                else
                {
                    vsPaths = string.Join("\n", scenario.VertexShaders.Select(entry =>
                    {
                        string line = entry.Path ?? "\u2014";
                        if (!string.IsNullOrEmpty(entry.InstancedPath))
                            line += $"  [instanced: {entry.InstancedPath}]";
                        return line;
                    }));
                }

                items.Add(new MaterialShaderScenarioItem
                {
                    Name = scenario.Name,
                    ScenarioHashText = $"0x{nameHash:X8}",
                    VersionText = scenario.Version.ToString(),
                    AnimCountText = scenario.AnimCount > 1 ? scenario.AnimCount.ToString() : string.Empty,
                    VertexShaderPaths = vsPaths,
                    GeometryPixelShader = string.IsNullOrWhiteSpace(scenario.GeometryPixelShader) ? "\u2014" : scenario.GeometryPixelShader,
                    VertexInputFlags = vertexInputFlags,
                    VertexInputFlagsDecoded = decodedSemantics,
                    StageBitsText = FormatStageBits(scenario.ShaderStageBits),
                    InlineState = blob.IsInline ? "Inline" : "External",
                    BlendFlagsText = blenText,
                    Notes = string.Empty,
                });
            }
        }

        return items;
    }

    private static uint HashScenarioName(string name)
    {
        const uint Polynomial = 0xEDB88320u;
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }

    private static string FormatStageBits(int bits)
    {
        if (bits == 0) return "None";
        var stages = new System.Text.StringBuilder();
        if ((bits & 0x01) != 0) { if (stages.Length > 0) stages.Append(" | "); stages.Append("VS"); }
        if ((bits & 0x08) != 0) { if (stages.Length > 0) stages.Append(" | "); stages.Append("GS"); }
        if ((bits & 0x10) != 0) { if (stages.Length > 0) stages.Append(" | "); stages.Append("PS"); }
        int knownMask = 0x01 | 0x08 | 0x10;
        int remaining = bits & ~knownMask;
        if (remaining != 0) { if (stages.Length > 0) stages.Append(" | "); stages.Append($"?0x{remaining:X}"); }
        return stages.Length > 0 ? $"{stages} (0x{bits:X2})" : $"0x{bits:X2}";
    }


    // POSITION, NORMAL, and BINORMAL are not represented in this field.
    private static string DecodeVDCLFlags(uint flags)
    {
        if (flags == 0) return "None";

        // Ordered table: (mask, label). Bit 5 covers both TEXCOORD5 and TANGENT0 (shared — TEXCOORD5 never
        // appears in vehicle models, so we label it TANGENT0 when only that bit is set alongside higher bits).
        var parts = new System.Collections.Generic.List<string>();
        if ((flags & 0x001) != 0) parts.Add("TEXCOORD0");
        if ((flags & 0x002) != 0) parts.Add("TEXCOORD1");
        if ((flags & 0x004) != 0) parts.Add("TEXCOORD2");
        if ((flags & 0x008) != 0) parts.Add("TEXCOORD3");
        if ((flags & 0x010) != 0) parts.Add("TEXCOORD4");
        if ((flags & 0x020) != 0) parts.Add("TANGENT0");   // shared with TEXCOORD5; vehicle models never use TEXCOORD5
        if ((flags & 0x040) != 0) parts.Add("TANGENT1");
        if ((flags & 0x080) != 0) parts.Add("TANGENT2");
        if ((flags & 0x100) != 0) parts.Add("TANGENT3");
        if ((flags & 0x200) != 0) parts.Add("TANGENT4");
        if ((flags & 0x400) != 0) parts.Add("COLOR0");
        // Report any unrecognised bits so nothing is silently dropped
        uint knownBits = 0x7FF;
        uint unknown = flags & ~knownBits;
        if (unknown != 0) parts.Add($"?0x{unknown:X}");

        return string.Join(" | ", parts);
    }

    private static List<MaterialShaderRenderTargetItem> BuildRenderTargetItems(ShaderDocumentSnapshot? shader)
    {
        if (shader == null)
            return [];

        var bundle = shader.SourceBundle;

        return bundle.Blobs
            .OfType<RenderTargetBlob>()
            .Select((blob, index) =>
            {
                int count = blob.Entries.Count;
                string summary = count == 0
                    ? "No entries parsed"
                    : string.Join("\n", blob.Entries.Select(e =>
                        $"VS: {(string.IsNullOrEmpty(e.VertexShaderName) ? "\u2014" : e.VertexShaderName)}  PS: {(string.IsNullOrEmpty(e.PixelShaderName) ? "\u2014" : e.PixelShaderName)}"));

                string vsPaths = count == 0 ? string.Empty
                    : string.Join("\n", blob.Entries.Select(e =>
                        string.IsNullOrEmpty(e.VertexShaderName) ? "\u2014" : e.VertexShaderName));

                string psPaths = count == 0 ? string.Empty
                    : string.Join("\n", blob.Entries.Select(e =>
                        string.IsNullOrEmpty(e.PixelShaderName) ? "\u2014" : e.PixelShaderName));

                return new MaterialShaderRenderTargetItem
                {
                    Label = $"TRGT Blob {index}",
                    VersionText = $"v{blob.VersionMajor}.{blob.VersionMinor}",
                    InlineState = blob.IsInline ? "Inline" : "External",
                    PayloadLengthText = blob.UnkLength.ToString(),
                    EntryCount = count.ToString(),
                    EntrySummary = summary,
                    VSPaths = vsPaths,
                    PSPaths = psPaths,
                    Notes = string.Empty,
                };
            })
            .ToList();
    }

    private static List<MaterialShaderRawBlobItem> BuildRawBlobItems(MaterialDocumentSnapshot? material, ShaderDocumentSnapshot? shader)
    {
        var items = new List<MaterialShaderRawBlobItem>();

        if (material != null)
            items.AddRange(BuildRawBlobItemsForBundle("Material", material.SourceBundle));

        if (shader != null)
            items.AddRange(BuildRawBlobItemsForBundle("Shader", shader.SourceBundle));

        return items;
    }

    private static IEnumerable<MaterialShaderRawBlobItem> BuildRawBlobItemsForBundle(string documentKind, Bundle bundle)
    {
        return bundle.Blobs.Select(blob => new MaterialShaderRawBlobItem
        {
            DocumentKind = documentKind,
            BlobTag = FormatTag(blob.Tag),
            VersionText = $"v{blob.VersionMajor}.{blob.VersionMinor}",
            MetadataTags = blob.Metadatas.Count == 0
                ? "No metadata"
                : string.Join(", ", blob.Metadatas.Select(metadata => FormatTag(metadata.Tag))),
            Notes = blob.Tag switch
            {
                Bundle.TAG_BLOB_LightScenario => "LSCE parser is simplified in the current codebase.",
                Bundle.TAG_BLOB_TRGT => "TRGT parser is currently partial.",
                Bundle.TAG_BLOB_VARS => "VARS is still treated as opaque/raw data.",
                _ => string.Empty,
            },
        });
    }

    private bool TryResolveLinkedShader(
        LoadedBundleSource materialSource,
        MaterialDocumentSnapshot material,
        string? gameId,
        string? gameRootPath,
        out LoadedBundleSource? linkedShader)
    {
        linkedShader = null;
        string linkedShaderPath = material.ShaderPath;
        if (string.IsNullOrWhiteSpace(linkedShaderPath))
            return false;

        if (!string.IsNullOrWhiteSpace(gameRootPath) && TryLoadBundleFromGamePath(gameRootPath, linkedShaderPath, out linkedShader))
            return true;

        if (!string.IsNullOrWhiteSpace(gameId) && GameAssetDatabaseService.DatabaseExists(gameId))
        {
            string fileName = Path.GetFileName(linkedShaderPath.Replace('/', '\\'));
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                string? indexedPath = GameAssetDatabaseService.LookupFile(gameId, fileName);
                if (!string.IsNullOrWhiteSpace(indexedPath) && !string.IsNullOrWhiteSpace(gameRootPath) && TryLoadBundleFromGamePath(gameRootPath, indexedPath, out linkedShader))
                    return true;
            }
        }

        if (File.Exists(materialSource.SourcePath))
        {
            string candidate = Path.Combine(Path.GetDirectoryName(materialSource.SourcePath) ?? string.Empty, Path.GetFileName(linkedShaderPath.Replace('/', '\\')));
            if (File.Exists(candidate))
            {
                linkedShader = LoadBundleFromBytes(File.ReadAllBytes(candidate), candidate, null, candidate, MaterialShaderAssetKind.Shaderbin);
                return true;
            }
        }

        return false;
    }

    private static string ChoosePreferredShaderPath(MatLBlob? matl)
    {
        if (matl == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(matl.PathV1_2))
            return matl.PathV1_2;

        if (!string.IsNullOrWhiteSpace(matl.PathV1_1))
            return matl.PathV1_1;

        return matl.Path ?? string.Empty;
    }

    private static MaterialShaderAssetKind GetAssetKindFromPath(string filePath)
    {
        return string.Equals(Path.GetExtension(filePath), ".shaderbin", StringComparison.OrdinalIgnoreCase)
            ? MaterialShaderAssetKind.Shaderbin
            : MaterialShaderAssetKind.Materialbin;
    }

    private static string ResolveParameterName(uint nameHash)
    {
        return NameHashService.Instance.GetName(nameHash) ?? $"Unknown Hash (0x{nameHash:X8})";
    }

    private LoadedBundleSource LoadBundleFromBytes(
        byte[] bytes,
        string sourcePath,
        string? archiveSource,
        string sourceDisplayPath,
        MaterialShaderAssetKind assetKind)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var bundle = new Bundle();
        bundle.Load(stream);

        return new LoadedBundleSource
        {
            AssetKind = assetKind,
            Bundle = bundle,
            SourcePath = sourcePath,
            ArchiveSource = archiveSource,
            SourceDisplayPath = string.IsNullOrWhiteSpace(archiveSource)
                ? sourceDisplayPath
                : $"{sourceDisplayPath} ({archiveSource})",
        };
    }

    private bool TryLoadBundleFromGamePath(string gameRootPath, string gamePath, out LoadedBundleSource? loaded)
    {
        loaded = null;

        if (!TryLocateAssetBytesFromGamePath(gameRootPath, gamePath, out var assetBytes, out var resolvedSource, out var sourcePath))
            return false;

        loaded = LoadBundleFromBytes(
            assetBytes,
            sourcePath,
            resolvedSource,
            gamePath,
            GetAssetKindFromPath(gamePath));

        return true;
    }

    private bool TryLocateAssetBytesFromGamePath(
        string gameRootPath,
        string gamePath,
        out byte[] assetBytes,
        out string resolvedSource,
        out string sourcePath)
    {
        assetBytes = Array.Empty<byte>();
        resolvedSource = string.Empty;
        sourcePath = string.Empty;

        if (string.IsNullOrWhiteSpace(gameRootPath) || !Directory.Exists(gameRootPath) || string.IsNullOrWhiteSpace(gamePath))
            return false;

        string normalizedGamePath = NormalizeAssetGamePath(gamePath);
        foreach (string relativePath in BuildRelativePathCandidates(normalizedGamePath))
        {
            string loosePath = Path.Combine(gameRootPath, relativePath);
            if (File.Exists(loosePath))
            {
                assetBytes = File.ReadAllBytes(loosePath);
                resolvedSource = loosePath;
                sourcePath = loosePath;
                return true;
            }

            if (TryLoadFromDerivedZip(gameRootPath, relativePath, out assetBytes, out resolvedSource))
            {
                sourcePath = normalizedGamePath;
                return true;
            }
        }

        string fileName = Path.GetFileName(normalizedGamePath.Replace('/', '\\'));
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        foreach (var zipPath in GetRelevantAssetZipPaths(gameRootPath, Path.GetExtension(fileName)))
        {
            if (TryExtractZipEntryByFileName(zipPath, fileName, out assetBytes))
            {
                resolvedSource = Path.GetRelativePath(gameRootPath, zipPath);
                sourcePath = normalizedGamePath;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> BuildRelativePathCandidates(string assetPath)
    {
        string relative = assetPath.Replace('/', '\\').Trim();
        if (relative.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("Game:/", StringComparison.OrdinalIgnoreCase))
            relative = relative[6..];

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
        if (!relative.StartsWith("media\\shaders\\", StringComparison.OrdinalIgnoreCase))
            AddCandidate(candidates, Path.Combine("media", "shaders", relative));

        return candidates;
    }

    private static bool TryLoadFromDerivedZip(string gameRootPath, string relativePath, out byte[] assetBytes, out string archiveSource)
    {
        assetBytes = Array.Empty<byte>();
        archiveSource = string.Empty;

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
            {
                archiveSource = Path.GetRelativePath(gameRootPath, zipPath);
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractZipEntry(string zipPath, string entryPath, out byte[] assetBytes)
    {
        assetBytes = Array.Empty<byte>();

        try
        {
            using var customZip = new CustomZipFile(zipPath);
            var entry = customZip.GetEntries().FirstOrDefault(candidate =>
                candidate.Name.Replace('\\', '/').TrimStart('/').Equals(entryPath.Replace('\\', '/').TrimStart('/'), StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                return false;

            assetBytes = customZip.ExtractToMemory(entry);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractZipEntryByFileName(string zipPath, string fileName, out byte[] assetBytes)
    {
        assetBytes = Array.Empty<byte>();

        try
        {
            using var customZip = new CustomZipFile(zipPath);
            var entry = customZip.GetEntries().FirstOrDefault(candidate =>
                Path.GetFileName(candidate.Name).Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                return false;

            assetBytes = customZip.ExtractToMemory(entry);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<(string GamePath, string ArchiveSource)> ScanArchiveLibraryAssets(string gameRootPath, string extension)
    {
        var results = new List<(string GamePath, string ArchiveSource)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string zipPath in GetRelevantAssetZipPaths(gameRootPath, extension))
        {
            if (!File.Exists(zipPath))
                continue;

            string archiveSource = Path.GetRelativePath(gameRootPath, zipPath);
            string zipFolder = Path.Combine(
                Path.GetDirectoryName(archiveSource) ?? string.Empty,
                Path.GetFileNameWithoutExtension(archiveSource));

            try
            {
                using var customZip = new CustomZipFile(zipPath);
                foreach (var entry in customZip.GetEntries())
                {
                    if (entry.IsDirectory)
                        continue;

                    if (!Path.GetExtension(entry.Name).Equals(extension, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string gamePath = $"Game:\\{Path.Combine(zipFolder, entry.Name.Replace('/', '\\'))}";
                    if (seen.Add(gamePath))
                        results.Add((gamePath, archiveSource));
                }
            }
            catch
            {
            }
        }

        return results;
    }

    private static IReadOnlyList<string> GetRelevantAssetZipPaths(string gameRootPath, string extension)
    {
        if (string.IsNullOrWhiteSpace(gameRootPath) || !Directory.Exists(gameRootPath))
            return [];

        string mediaPath = Path.Combine(gameRootPath, "media");
        if (!Directory.Exists(mediaPath))
            mediaPath = gameRootPath;

        var results = new List<string>();
        foreach (var zipName in GetPriorityZipNamesForAsset(extension))
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

        return results
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string[] GetPriorityZipNamesForAsset(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".materialbin" => ["materials.zip", "Materials.zip"],
            ".shaderbin" => ["shaders.zip", "Shaders.zip", "materials.zip", "Materials.zip"],
            ".swatchbin" => ["textures.zip", "Textures.zip"],
            _ => ["materials.zip", "Materials.zip", "shaders.zip", "Shaders.zip", "textures.zip", "Textures.zip"],
        };
    }

    private static string NormalizeAssetGamePath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return string.Empty;

        string normalized = assetPath.Trim().Replace('/', '\\');
        if (normalized.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Game:/", StringComparison.OrdinalIgnoreCase))
        {
            string relative = normalized[6..].TrimStart('\\', '/');
            return $"Game:\\{relative}";
        }

        return normalized.TrimStart('\\', '/');
    }

    private static string FormatTag(uint tag)
    {
        char[] chars =
        [
            (char)((tag >> 24) & 0xFF),
            (char)((tag >> 16) & 0xFF),
            (char)((tag >> 8) & 0xFF),
            (char)(tag & 0xFF),
        ];

        if (chars.All(character => character is >= ' ' and <= '~'))
            return new string(chars);

        return $"0x{tag:X8}";
    }

    private sealed class LoadedBundleSource
    {
        public MaterialShaderAssetKind AssetKind { get; init; }
        public Bundle Bundle { get; init; } = null!;
        public string SourcePath { get; init; } = string.Empty;
        public string SourceDisplayPath { get; init; } = string.Empty;
        public string? ArchiveSource { get; init; }
    }
}
