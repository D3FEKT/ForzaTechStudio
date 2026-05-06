using System;
using System.Collections.Generic;

namespace ForzaTechStudio.Services;

// Options for modelbin and carbin conversion
public sealed class ConversionOptions
{
    public bool EnablePathConversion { get; init; }
    public bool EnableAdvancedVlayBlobPatch { get; init; }
    public bool RemoveConflictingShaderParameters { get; init; }
    public string? CarZipName { get; init; }
    public string? TargetGamePath { get; init; }
    public string? SourceGamePath { get; init; }
    public string? SourceDetectedGame { get; init; }
    public string? SourceModelbinFileName { get; init; }
    public Dictionary<string, string> ManualMaterialOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
}

// Groups unresolved asset references for UI follow-up
public sealed class UnresolvedAssetReference
{
    public string? FileName { get; init; }
    public string? Type { get; init; }
    public string? SampleOriginalPath { get; init; }
    public List<string> OriginalPaths { get; init; } = [];

    public int ReferenceCount => OriginalPaths.Count;
}