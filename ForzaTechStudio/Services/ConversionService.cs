using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using ForzaTools.CarScene;
using ForzaTools.Shared;

namespace ForzaTechStudio.Services;

// Defines the supported Forza game targets for conversion.
public enum ForzaGameTarget
{
    FH2,    // Forza Horizon 2 (Xbox One)
    FM5,    // Forza Motorsport 5
    FM6,    // Forza Motorsport 6 / Apex
    FH3,    // Forza Horizon 3
    FM7,    // Forza Motorsport 7
    FH4,    // Forza Horizon 4
    FH5,    // Forza Horizon 5
    FM2023, // Forza Motorsport (2023)
    FH6,    // Forza Horizon 6
}

// Describes the detected source game from file analysis.
public class FileAnalysisResult
{
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public FileType Type { get; set; }
    public string? DetectedGame { get; set; }
    public string? Details { get; set; }
    public bool IsValid { get; set; }

    // Bundle info (modelbin)
    public byte BundleVersionMajor { get; set; }
    public byte BundleVersionMinor { get; set; }

    // Blob version info
    public byte MeshVersionMajor { get; set; }
    public byte MeshVersionMinor { get; set; }
    public byte ModlVersionMajor { get; set; }
    public byte ModlVersionMinor { get; set; }
    public byte VlayVersionMajor { get; set; }
    public byte VlayVersionMinor { get; set; }

    // Carbin info
    public ushort SceneVersion { get; set; }
    public ushort ModelVersion { get; set; }
    public GameSeries DetectedSeries { get; set; }

    // Counts
    public int MeshCount { get; set; }
    public int MaterialCount { get; set; }
    public int VertexBufferCount { get; set; }
    public int VertexLayoutCount { get; set; }

    // Carbin-specific counts
    public int PartCount { get; set; }
    public int UpgradePartCount { get; set; }

    // LightsBin-specific info
    public uint LightsBinVersion { get; set; }
    public int LightsBinLightCount { get; set; }
    public int LightsBinAttachCount { get; set; }
    public int LightsBinLodCount { get; set; }

    // Swatchbin-specific info
    public bool SwatchbinIsDurango { get; set; }
    public uint SwatchbinWidth { get; set; }
    public uint SwatchbinHeight { get; set; }
    public string? SwatchbinFormatName { get; set; }
    public byte SwatchbinMipLevels { get; set; }
}

public enum FileType
{
    Unknown,
    Modelbin,
    Carbin,
    LightsBin,
    Swatchbin
}

// Tracks the progress and log of a conversion operation.
public class ConversionResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public List<string> Log { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

// Tracks the result of path conversion for materialbin/swatchbin paths.
public class PathConversionResult
{
    public int TotalPaths { get; set; }
    public int SuccessfulPaths { get; set; }
    public int FailedPaths { get; set; }
    public List<string> FailedPathList { get; set; } = [];
    public List<UnresolvedAssetReference> UnresolvedAssets { get; set; } = [];
    public List<string> Log { get; set; } = [];
    public string? CreatedZipPath { get; set; }
}

// Orchestrates modelbin and carbin file conversion, delegating to specialised sub-services.
public class ConversionService
{
    private readonly ModelbinConversionService _modelbinService = new();
    private readonly CarbinConversionService _carbinService = new();
    private readonly ModelCarbinConversionService _modelCarbinService;
    private readonly PathConversionService _pathConversionService = new();
    private readonly LightsBinConversionService _lightsBinService = new();
    private readonly SwatchbinConversionService _swatchbinConversionService = new();

    public ConversionService()
    {
        _modelCarbinService = new ModelCarbinConversionService(_modelbinService, _carbinService);
    }

    // Exposes the path conversion service for direct use by the ViewModel.
    public PathConversionService PathConversion => _pathConversionService;

    // Model+carbin conversion service.
    public ModelCarbinConversionService ModelCarbinConversion => _modelCarbinService;

    // Exposes the modelbin conversion service directly.
    public ModelbinConversionService ModelbinConversion => _modelbinService;

    // Exposes the carbin conversion service directly.
    public CarbinConversionService CarbinConversion => _carbinService;

    // Exposes the lights.bin conversion service for direct use by the ViewModel.
    public LightsBinConversionService LightsBinConversion => _lightsBinService;

    // Exposes the swatchbin conversion service for direct use by the ViewModel.
    public SwatchbinConversionService SwatchbinConversion => _swatchbinConversionService;

    // Analyzes a file to determine its type and source game version.
    public FileAnalysisResult AnalyzeFile(string filePath)
    {
        var result = new FileAnalysisResult
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            IsValid = false
        };

        if (!File.Exists(filePath))
        {
            result.Details = "File not found.";
            return result;
        }

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string name = Path.GetFileName(filePath).ToLowerInvariant();

        try
        {
            if (ext == ".modelbin")
            {
                result.Type = FileType.Modelbin;
                AnalyzeModelbin(filePath, result);
            }
            else if (ext == ".carbin")
            {
                result.Type = FileType.Carbin;
                AnalyzeCarbin(filePath, result);
            }
            else if (name == "lights.bin" || (ext == ".bin" && name.Contains("lights")))
            {
                result.Type = FileType.LightsBin;
                AnalyzeLightsBin(filePath, result);
            }
            else if (ext == ".swatchbin")
            {
                result.Type = FileType.Swatchbin;
                AnalyzeSwatchbin(filePath, result);
            }
            else
            {
                result.Details = $"Unsupported file type: {ext}";
            }
        }
        catch (Exception ex)
        {
            result.Details = $"Error analyzing file: {ex.Message}";
        }

        return result;
    }

    private void AnalyzeModelbin(string filePath, FileAnalysisResult result)
    {
        using var fs = File.OpenRead(filePath);
        var bundle = new Bundle();
        bundle.Load(fs);

        result.BundleVersionMajor = bundle.VersionMajor;
        result.BundleVersionMinor = bundle.VersionMinor;

        foreach (var blob in bundle.Blobs)
        {
            switch (blob.Tag)
            {
                case Bundle.TAG_BLOB_Model:
                    result.ModlVersionMajor = blob.VersionMajor;
                    result.ModlVersionMinor = blob.VersionMinor;
                    if (blob is ModelBlob modl)
                    {
                        result.MeshCount = modl.MeshCount;
                        result.MaterialCount = modl.MaterialCount;
                        result.VertexLayoutCount = modl.VertexLayoutCount;
                    }
                    break;
                case Bundle.TAG_BLOB_Mesh:
                    result.MeshVersionMajor = blob.VersionMajor;
                    result.MeshVersionMinor = blob.VersionMinor;
                    break;
                case Bundle.TAG_BLOB_VertexLayout:
                    result.VlayVersionMajor = blob.VersionMajor;
                    result.VlayVersionMinor = blob.VersionMinor;
                    break;
                case Bundle.TAG_BLOB_VertexBuffer:
                    result.VertexBufferCount++;
                    break;
            }
        }

        result.DetectedGame = DetectModelbinGame(result);
        result.Details = $"Bundle v{result.BundleVersionMajor}.{result.BundleVersionMinor}, " +
                         $"Modl v{result.ModlVersionMajor}.{result.ModlVersionMinor}, " +
                         $"Mesh v{result.MeshVersionMajor}.{result.MeshVersionMinor}, " +
                         $"VLay v{result.VlayVersionMajor}.{result.VlayVersionMinor}, " +
                         $"{result.MeshCount} meshes, {result.MaterialCount} materials";
        result.IsValid = true;
    }

    private string DetectModelbinGame(FileAnalysisResult result)
    {
        byte meshMaj = result.MeshVersionMajor;
        byte meshMin = result.MeshVersionMinor;
        byte modlMaj = result.ModlVersionMajor;
        byte modlMin = result.ModlVersionMinor;
        byte bundleMaj = result.BundleVersionMajor;
        byte bundleMin = result.BundleVersionMinor;

        if (meshMaj == 1 && meshMin >= 9)
        {
            if (modlMaj == 1 && modlMin >= 3) return "FH5 (late)";
            return "FH4 / FH5";
        }
        if (meshMaj == 1 && meshMin == 8)
        {
            if (bundleMaj == 1 && bundleMin >= 1)
                return "FH3 / FM2023";
            return "FM2023";
        }
        if (meshMaj == 1 && meshMin == 7)
        {
            if (bundleMaj == 1 && bundleMin >= 1)
                return "FM6 / FM6 Apex / FM7";
            return "FM5 / FH2";
        }
        if (meshMaj == 1 && meshMin < 7)
        {
            return "FM5 / FH2 (early)";
        }

        return "Unknown";
    }

    private void AnalyzeCarbin(string filePath, FileAnalysisResult result)
    {
        using var fs = File.OpenRead(filePath);
        var carbinFile = new CarbinFile();
        carbinFile.Load(fs);

        var scene = carbinFile.Scene;
        result.SceneVersion = scene.Version;
        result.DetectedSeries = scene.Series;

        ushort modelVersion = 0;
        int totalModels = 0;

        foreach (var entry in scene.NonUpgradableParts)
        {
            foreach (var model in entry.Part.Models)
            {
                if (modelVersion == 0) modelVersion = model.Version;
                totalModels++;
            }
        }

        int totalUpgradeModels = 0;
        int totalUpgrades = 0;
        int totalInlineUpgradeModels = 0;
        foreach (var part in scene.UpgradableParts)
        {
            totalUpgrades += part.Upgrades.Count;
            // Count inline models (Upgrade.Version < 3)
            foreach (var upg in part.Upgrades)
            {
                foreach (var model in upg.Models)
                {
                    if (modelVersion == 0) modelVersion = model.Version;
                    totalInlineUpgradeModels++;
                }
            }
            // Count shared models (UpgradablePart.Version >= 3)
            foreach (var shared in part.SharedModels)
            {
                if (modelVersion == 0) modelVersion = shared.Model.Version;
                totalUpgradeModels++;
            }
        }

        result.ModelVersion = modelVersion;
        result.MeshCount = totalModels + totalUpgradeModels + totalInlineUpgradeModels;
        result.MaterialCount = totalModels; // Non-upgradable model count for display
        result.VertexBufferCount = totalUpgradeModels + totalInlineUpgradeModels; // All upgrade-associated models
        result.PartCount = scene.NonUpgradableParts.Count;
        result.UpgradePartCount = scene.UpgradableParts.Count;

        result.DetectedGame = DetectCarbinGame(result);
        result.Details = $"Scene v{result.SceneVersion}, Model v{result.ModelVersion}, " +
                         $"Series: {result.DetectedSeries}, {totalModels + totalUpgradeModels + totalInlineUpgradeModels} models, " +
                         $"{scene.NonUpgradableParts.Count} standard parts, " +
                         $"{scene.UpgradableParts.Count} upgrade parts ({totalUpgrades} upgrades)";
        result.IsValid = true;
    }

    private string DetectCarbinGame(FileAnalysisResult result)
    {
        ushort sv = result.SceneVersion;
        ushort mv = result.ModelVersion;
        GameSeries series = result.DetectedSeries;

        if (sv >= 10) return "FM2023";
        if (series == GameSeries.Horizon)
        {
            if (sv >= 6 || mv >= 18) return "FH5";
            if (mv >= 16) return "FH3 / FH4";
            if (mv >= 15) return "FH2";
        }
        if (mv >= 21) return "FM2023";
        if (mv >= 17) return "FM6 / FM7";
        if (mv >= 14) return "FM5";

        return "Unknown";
    }

    private void AnalyzeLightsBin(string filePath, FileAnalysisResult result)
    {
        using var fs = File.OpenRead(filePath);
        var analysis = _lightsBinService.Analyze(fs);

        if (!analysis.IsValid)
        {
            result.Details = $"Failed to parse lights.bin: {analysis.Error}";
            return;
        }

        result.LightsBinVersion     = analysis.Version;
        result.LightsBinLightCount  = analysis.LightCount;
        result.LightsBinAttachCount = analysis.AttachmentCount;
        result.LightsBinLodCount    = analysis.LodOverrideCount;
        result.DetectedGame         = analysis.DetectedGame;
        result.Details              = $"{LightsBinConversionService.GetVersionLabel(analysis.Version)}, " +
                                      $"{analysis.LightCount} light(s), {analysis.AttachmentCount} attachment(s)";
        result.IsValid = true;
    }

    private void AnalyzeSwatchbin(string filePath, FileAnalysisResult result)
    {
        bool? isDurango = _swatchbinConversionService.IsDurango(filePath);
        if (isDurango == null)
        {
            result.Details = "Failed to parse swatchbin header.";
            return;
        }

        // Load full info to get dimensions and format
        try
        {
            var info = new SwatchbinService().LoadSwatchbin(filePath);
            result.SwatchbinIsDurango  = info.IsDurangoFormat;
            result.SwatchbinWidth      = info.Width;
            result.SwatchbinHeight     = info.Height;
            result.SwatchbinFormatName = info.DxgiFormatName ?? "Unknown";
            result.SwatchbinMipLevels  = info.MipLevels;
            result.DetectedGame        = info.IsDurangoFormat ? "Xbox (Durango)" : "PC";
            result.Details             = $"{(info.IsDurangoFormat ? "Durango/Xbox" : "PC")}, " +
                                         $"{info.Width}×{info.Height}, {info.DxgiFormatName}, {info.MipLevels} mip(s)";
            result.IsValid = info.IsDurangoFormat; // Only valid for conversion when Durango
            if (!result.IsValid)
                result.Details += " — already PC format, no conversion needed";
        }
        catch (Exception ex)
        {
            result.Details = $"Error reading swatchbin: {ex.Message}";
        }
    }

    // Gets a list of valid target games for a given file analysis.
    public List<ForzaGameTarget> GetValidTargets(FileAnalysisResult analysis)
    {
        if (analysis == null || !analysis.IsValid) return [];

        if (analysis.Type == FileType.Modelbin)
        {
            return
            [
                ForzaGameTarget.FH5,
                ForzaGameTarget.FH4,
                ForzaGameTarget.FH3,
                ForzaGameTarget.FM2023,
                ForzaGameTarget.FM7,
                ForzaGameTarget.FM6,
                ForzaGameTarget.FH2,
            ];
        }

        if (analysis.Type == FileType.Carbin)
        {
            return
            [
                ForzaGameTarget.FH5,
                ForzaGameTarget.FH4,
                ForzaGameTarget.FH3,
                ForzaGameTarget.FM2023,
                ForzaGameTarget.FM7,
                ForzaGameTarget.FM5,
            ];
        }

        if (analysis.Type == FileType.LightsBin)
        {
            return new List<ForzaGameTarget>(LightsBinConversionService.SupportedTargets);
        }

        if (analysis.Type == FileType.Swatchbin)
        {
            // Swatchbin conversion is Durango→PC only; target is always PC (represented as FH5)
            return [ForzaGameTarget.FH5];
        }

        return [];
    }

    // Gets a human-readable name for a game target.
    public static string GetGameName(ForzaGameTarget target)
    {
        return target switch
        {
            ForzaGameTarget.FH2 => "Forza Horizon 2",
            ForzaGameTarget.FM5 => "Forza Motorsport 5",
            ForzaGameTarget.FM6 => "Forza Motorsport 6 / Apex",
            ForzaGameTarget.FH3 => "Forza Horizon 3",
            ForzaGameTarget.FM7 => "Forza Motorsport 7",
            ForzaGameTarget.FH4 => "Forza Horizon 4",
            ForzaGameTarget.FH5 => "Forza Horizon 5",
            ForzaGameTarget.FM2023 => "Forza Motorsport (2023)",
            _ => target.ToString()
        };
    }

    // Gets the game ID string used in SettingsService for a given target.
    public static string GetGameSettingsId(ForzaGameTarget target)
    {
        return target switch
        {
            ForzaGameTarget.FH2 => "FH2",
            ForzaGameTarget.FM5 => "FM5",
            ForzaGameTarget.FM6 => "FM6",
            ForzaGameTarget.FH3 => "FH3",
            ForzaGameTarget.FH4 => "FH4",
            ForzaGameTarget.FH5 => "FH5",
            ForzaGameTarget.FM7 => "FM7",
            ForzaGameTarget.FM2023 => "FM2023",
            _ => target.ToString()
        };
    }

    // Gets the game ID string for the detected source game string.
    public static string? DetectSourceGameId(string detectedGame)
    {
        return ForzaGameCatalog.GetMatchingGameIds(detectedGame).FirstOrDefault();
    }
}
