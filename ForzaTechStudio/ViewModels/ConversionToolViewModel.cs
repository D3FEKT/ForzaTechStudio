using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using ForzaTechStudio.Services;
using ForzaTools.Bundles;

namespace ForzaTechStudio.ViewModels;

public partial class ConversionToolViewModel : ObservableObject
{
    private readonly Services.ConversionService _conversionService = new();
    private readonly Services.SettingsService _settingsService = new();
    private readonly Services.ZipCreationService _zipCreationService = new();
    private readonly Dictionary<string, IReadOnlyList<MaterialPickerItem>> _materialPickerCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoadingPreferences;

    private sealed class MaterialPickerItem
    {
        public string FileName { get; init; } = null!;
        public string GamePath { get; init; } = null!;
        public string DisplayText => $"{FileName}    {GamePath}";
    }

    private enum MaterialPickerDialogAction
    {
        Skip,
        UseSelected,
        SkipAll,
    }

    private sealed class MaterialPickerDialogResult
    {
        public MaterialPickerDialogAction Action { get; init; }
        public string SelectedPath { get; init; } = null!;
    }

    public ConversionToolViewModel()
    {
        _ = LoadPreferencesAsync();
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Waiting..";

    [ObservableProperty]
    private bool _hasAnalysis;

    [ObservableProperty]
    private bool _hasResult;

    // Path conversion toggle
    [ObservableProperty]
    private bool _isPathConversionEnabled;

    // Car zip name for converted path output (e.g. "NIS_SilviaK_92")
    [ObservableProperty]
    private string _carZipName = "";

    // Batch output format: true = repack to zip, false = output as folder
    [ObservableProperty]
    private bool _batchOutputAsZip;

    [ObservableProperty]
    private bool _isAdvancedVlayBlobPatchEnabled;

    [ObservableProperty]
    private bool _isRemoveConflictingShaderParametersEnabled;

    // File analysis
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileTypeName = string.Empty;

    [ObservableProperty]
    private string _detectedGame = string.Empty;

    [ObservableProperty]
    private string _fileDetails = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPathConversion))]
    private bool _isModelbin;

    [ObservableProperty]
    private bool _isCarbin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPathConversion))]
    private bool _isBatchZip;

    [ObservableProperty]
    private bool _isLightsBin;

    [ObservableProperty]
    private bool _isSwatchbin;

    // Swatchbin details
    [ObservableProperty]
    private bool _swatchbinIsDurango;

    [ObservableProperty]
    private string _swatchbinResolution = string.Empty;

    [ObservableProperty]
    private string _swatchbinFormat = string.Empty;

    [ObservableProperty]
    private int _swatchbinMipLevels;

    // True when the path conversion UI should be shown – for single modelbin or batch zip files.
    public bool ShowPathConversion => IsModelbin || IsBatchZip;

    // Modelbin details
    [ObservableProperty]
    private string _bundleVersion = string.Empty;

    [ObservableProperty]
    private string _meshVersion = string.Empty;

    [ObservableProperty]
    private string _modlVersion = string.Empty;

    [ObservableProperty]
    private string _vlayVersion = string.Empty;

    [ObservableProperty]
    private int _meshCount;

    [ObservableProperty]
    private int _materialCount;

    [ObservableProperty]
    private int _vertexBufferCount;

    [ObservableProperty]
    private string _vlayPatternCategory = string.Empty;

    // Carbin details
    [ObservableProperty]
    private string _sceneVersion = string.Empty;

    [ObservableProperty]
    private string _modelVersion = string.Empty;

    [ObservableProperty]
    private string _detectedSeries = string.Empty;

    [ObservableProperty]
    private int _partCount;

    [ObservableProperty]
    private int _upgradePartCount;

    [ObservableProperty]
    private int _standardModelCount;

    [ObservableProperty]
    private int _sharedModelCount;

    // LightsBin details
    [ObservableProperty]
    private string _lightsBinVersion = string.Empty;

    [ObservableProperty]
    private int _lightsBinLightCount;

    [ObservableProperty]
    private int _lightsBinAttachCount;

    [ObservableProperty]
    private int _lightsBinLodCount;

    // Target selection
    public ObservableCollection<string> AvailableTargets { get; } = [];

    [ObservableProperty]
    private int _selectedTargetIndex = -1;

    partial void OnSelectedTargetIndexChanged(int value)
    {
        OnPropertyChanged(nameof(HasTargetSelected));
        OnPropertyChanged(nameof(TargetVersionInfo));
    }

    public bool HasTargetSelected => SelectedTargetIndex >= 0 && SelectedTargetIndex < _currentTargets.Count;

    public string TargetVersionInfo
    {
        get
        {
            if (!HasTargetSelected) return "";
            var target = _currentTargets[SelectedTargetIndex];

            if (_currentAnalysis?.Type == Services.FileType.Carbin)
            {
                var (sv, mv, series, pv, upv, uv) = Services.ModelCarbinConversionService.GetCarbinTargetVersions(target);
                return $"Target: Scene v{sv}, Model v{mv}, {series}, Part v{pv}, UpgPart v{upv}, Upgrade v{uv}";
            }
            else if (_currentAnalysis?.Type == Services.FileType.Modelbin)
            {
                var (bv, modlv, meshv, vlayv) = Services.ModelCarbinConversionService.GetModelbinTargetVersions(target);
                string vlayInfo = target switch
                {
                    Services.ForzaGameTarget.FH2 or Services.ForzaGameTarget.FM5 =>
                        "VLay: NORMAL(Float16x4) TANGENT(Float16x4) TEXCOORD0-3, stride=40",
                    Services.ForzaGameTarget.FM6 or Services.ForzaGameTarget.FM7 =>
                        "VLay: NORMAL(Snorm16x2) TANGENT(R10G10B10A2) TEXCOORD0-3, stride=28",
                    Services.ForzaGameTarget.FH3 or Services.ForzaGameTarget.FH4 =>
                        "VLay: COLOR0 NORMAL(Snorm16x2) TANGENT0-1(R10G10B10A2) TEXCOORD0-4, stride=36",
                    Services.ForzaGameTarget.FM2023 =>
                        "VLay: NORMAL(Snorm16x2) TANGENT0-2(R10G10B10A2) TEXCOORD0-4, stride=36",
                    _ =>
                        "VLay: COLOR0 NORMAL(Snorm16x2) TANGENT0-2(R10G10B10A2) TEXCOORD0-4, stride=40",
                };
                return $"Target: Bundle v{bv.maj}.{bv.min}, Modl v{modlv.maj}.{modlv.min}, Mesh v{meshv.maj}.{meshv.min}\n{vlayInfo}";
            }
            else if (_currentAnalysis?.Type == Services.FileType.LightsBin)
            {
                uint targetVer = Services.LightsBinConversionService.GetTargetLightsVersion(target);
                return $"Target: lights.bin {Services.LightsBinConversionService.GetVersionLabel(targetVer)}";
            }
            else if (_currentAnalysis?.Type == Services.FileType.Swatchbin)
            {
                return "Target: PC format (Durango → PC detile + re-bundle)";
            }
            return "";
        }
    }

    private Services.FileAnalysisResult? _currentAnalysis;
    private List<Services.ForzaGameTarget> _currentTargets = [];
    private string? _batchZipPath;

    // Conversion result
    public ObservableCollection<string> ConversionLog { get; } = [];

    [ObservableProperty]
    private bool _conversionSuccess;

    [ObservableProperty]
    private string? _outputFilePath;

    // Helper to append a log line and flush it to the UI immediately.
    private void AddLog(string message)
    {
        string normalizedMessage = NormalizeLogMessage(message);

        if (App.MainWindow?.DispatcherQueue != null)
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() => ConversionLog.Add(normalizedMessage));
        }
        else
        {
            ConversionLog.Add(normalizedMessage);
        }
    }

    private static string NormalizeLogMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        string normalized = message
            .Replace("✓", "[OK]")
            .Replace("✗", "[ERR]")
            .Replace("→", "->")
            .Replace("�", string.Empty);

        normalized = ReplaceLeadingMarker(normalized, "?", "[WARN]");
        normalized = ReplaceLeadingMarker(normalized, "~", "[SKIP]");

        var builder = new StringBuilder(normalized.Length);
        foreach (char character in normalized)
        {
            if (character == '\r' || character == '\n')
            {
                builder.Append(character);
                continue;
            }

            if (character == '\t')
            {
                builder.Append(' ');
                continue;
            }

            if (character == '\uFFFD')
            {
                builder.Append('_');
                continue;
            }

            if (!char.IsControl(character))
                builder.Append(character);
        }

        return builder.ToString();
    }

    private static string ReplaceLeadingMarker(string message, string marker, string replacement)
    {
        int indentLength = 0;
        while (indentLength < message.Length && message[indentLength] == ' ')
            indentLength++;

        string trimmed = message[indentLength..];
        string markerWithSpace = marker + " ";
        if (!trimmed.StartsWith(markerWithSpace, StringComparison.Ordinal))
            return message;

        return message[..indentLength] + replacement + " " + trimmed[markerWithSpace.Length..];
    }

    partial void OnIsAdvancedVlayBlobPatchEnabledChanged(bool value)
    {
        if (_isLoadingPreferences)
            return;

        _ = SaveAdvancedVlayPreferenceAsync(value);
    }

    private async Task LoadPreferencesAsync()
    {
        try
        {
            _isLoadingPreferences = true;
            var settings = await _settingsService.LoadAsync();
            IsAdvancedVlayBlobPatchEnabled = settings.EnableAdvancedVlayBlobPatch;
        }
        catch
        {
            IsAdvancedVlayBlobPatchEnabled = false;
        }
        finally
        {
            _isLoadingPreferences = false;
        }
    }

    private async Task SaveAdvancedVlayPreferenceAsync(bool value)
    {
        try
        {
            var settings = await _settingsService.LoadAsync();
            if (settings.EnableAdvancedVlayBlobPatch == value)
                return;

            settings.EnableAdvancedVlayBlobPatch = value;
            await _settingsService.SaveAsync(settings);
        }
        catch
        {
            // Preference persistence is best-effort only.
        }
    }

    private ConversionOptions CreateConversionOptions(
        bool doPathConversion = false,
        string? carZipName = null,
        string? targetGamePath = null,
        string? sourceGamePath = null,
        bool? enableAdvancedVlayBlobPatch = null,
        string? sourceModelbinFileName = null,
        string? sourceDetectedGame = null,
        bool? removeConflictingShaderParameters = null)
    {
        return new ConversionOptions
        {
            EnablePathConversion = doPathConversion,
            EnableAdvancedVlayBlobPatch = enableAdvancedVlayBlobPatch ?? IsAdvancedVlayBlobPatchEnabled,
            RemoveConflictingShaderParameters = removeConflictingShaderParameters ?? IsRemoveConflictingShaderParametersEnabled,
            CarZipName = carZipName,
            TargetGamePath = targetGamePath,
            SourceGamePath = sourceGamePath,
            SourceDetectedGame = sourceDetectedGame,
            SourceModelbinFileName = sourceModelbinFileName,
        };
    }

    private async Task<IReadOnlyList<MaterialPickerItem>> GetMaterialPickerItemsAsync(Services.ForzaGameTarget target, string targetGamePath)
    {
        string targetGameId = Services.ConversionService.GetGameSettingsId(target);
        string cacheKey = $"{targetGameId}|{targetGamePath}";

        if (_materialPickerCache.TryGetValue(cacheKey, out var cachedItems))
            return cachedItems;

        var items = await Task.Run(() =>
        {
            List<string> materialPaths = [];

            if (GameAssetDatabaseService.DatabaseExists(targetGameId))
            {
                materialPaths = GameAssetDatabaseService.GetAssetPaths(targetGameId, ".materialbin", "materials.zip");
            }

            if (materialPaths.Count == 0 && !string.IsNullOrEmpty(targetGamePath))
            {
                materialPaths = GameAssetDatabaseService.ScanMaterialbinPathsFromMaterialsZip(targetGamePath);
            }

            return materialPaths
                .Select(path => new MaterialPickerItem
                {
                    FileName = Path.GetFileName(Services.PathConversionService.ExtractRelativePath(path)),
                    GamePath = path,
                })
                .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.GamePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        });

        _materialPickerCache[cacheKey] = items;
        return items;
    }

    private async Task<Dictionary<string, string>> ResolveMaterialbinSelectionsAsync(
        IReadOnlyList<UnresolvedAssetReference> unresolvedAssets,
        Services.ForzaGameTarget target,
        string targetGamePath)
    {
        var manualMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (unresolvedAssets == null || unresolvedAssets.Count == 0)
            return manualMappings;

        var materialAssets = unresolvedAssets
            .Where(asset => string.Equals(asset.Type, "materialbin", StringComparison.OrdinalIgnoreCase))
            .OrderBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (materialAssets.Count == 0)
            return manualMappings;

        var candidates = await GetMaterialPickerItemsAsync(target, targetGamePath);
        if (candidates.Count == 0)
        {
            AddLog("? Manual material selection unavailable: no materialbin entries were found in the target game's materials.zip index.");
            return manualMappings;
        }

        AddLog($"--- Manual materialbin selection for {materialAssets.Count} unresolved file(s) ---");

        for (int index = 0; index < materialAssets.Count; index++)
        {
            var unresolvedAsset = materialAssets[index];
            var selection = await ShowMaterialPickerDialogAsync(unresolvedAsset, candidates);

            if (selection.Action == MaterialPickerDialogAction.SkipAll)
            {
                int remainingCount = materialAssets.Count - index;
                AddLog($"? Manual selection skipped for {remainingCount} remaining unresolved materialbin file(s); falling back to converted-folder copy.");
                break;
            }

            if (selection.Action != MaterialPickerDialogAction.UseSelected || string.IsNullOrEmpty(selection.SelectedPath))
            {
                AddLog($"? Manual selection cancelled for {unresolvedAsset.FileName}; falling back to converted-folder copy.");
                continue;
            }

            foreach (var originalPath in unresolvedAsset.OriginalPaths)
                manualMappings[originalPath] = selection.SelectedPath;

            AddLog($"? Manual material selected: {unresolvedAsset.FileName} -> {selection.SelectedPath}");
        }

        return manualMappings;
    }

    private async Task<MaterialPickerDialogResult> ShowMaterialPickerDialogAsync(
        UnresolvedAssetReference unresolvedAsset,
        IReadOnlyList<MaterialPickerItem> materialCandidates)
    {
        if (materialCandidates == null || materialCandidates.Count == 0)
        {
            return new MaterialPickerDialogResult
            {
                Action = MaterialPickerDialogAction.Skip,
            };
        }

        var listView = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 420,
            MinWidth = 1560,
            DisplayMemberPath = nameof(MaterialPickerItem.DisplayText)
        };

        IReadOnlyList<MaterialPickerItem> currentItems = materialCandidates;

        void ApplyFilter(string query)
        {
            var filteredItems = string.IsNullOrWhiteSpace(query)
                ? materialCandidates
                : materialCandidates
                    .Where(item =>
                        item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        item.GamePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            currentItems = RankMaterialCandidates(filteredItems, unresolvedAsset.FileName);
            listView.ItemsSource = currentItems;

            var autoSelectedItem = SelectAutoMaterialCandidate(currentItems, unresolvedAsset.FileName);
            if (autoSelectedItem != null)
            {
                listView.SelectedItem = autoSelectedItem;
            }
            else if (currentItems.Count > 0)
            {
                listView.SelectedIndex = 0;
            }
        }

        var searchBox = new TextBox
        {
            PlaceholderText = "Search target materials.zip by name or path..."
        };
        searchBox.TextChanged += (s, e) => ApplyFilter(searchBox.Text);

        ApplyFilter(Path.GetFileNameWithoutExtension(unresolvedAsset.FileName));

        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow?.Content?.XamlRoot,
            Title = $"Select Replacement for {unresolvedAsset.FileName}",
            Width = 1700,
            MinWidth = 1640,
            MaxWidth = 2000,
            PrimaryButtonText = "Use Selected Material",
            SecondaryButtonText = "Skip All",
            CloseButtonText = "Skip",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                MinWidth = 1620,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Original path: {unresolvedAsset.SampleOriginalPath}",
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = $"This unresolved material is referenced {unresolvedAsset.ReferenceCount} time(s). Pick a target materialbin from the destination game's materials.zip.",
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        Opacity = 0.75
                    },
                    searchBox,
                    listView
                }
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && listView.SelectedItem is MaterialPickerItem selected)
        {
            return new MaterialPickerDialogResult
            {
                Action = MaterialPickerDialogAction.UseSelected,
                SelectedPath = selected.GamePath,
            };
        }

        if (result == ContentDialogResult.Secondary)
        {
            return new MaterialPickerDialogResult
            {
                Action = MaterialPickerDialogAction.SkipAll,
            };
        }

        return new MaterialPickerDialogResult
        {
            Action = MaterialPickerDialogAction.Skip,
        };
    }

    private static IReadOnlyList<MaterialPickerItem> RankMaterialCandidates(
        IEnumerable<MaterialPickerItem> materialCandidates,
        string referenceFileName)
    {
        var rankedItems = materialCandidates?
            .Select(item => new
            {
                Item = item,
                Score = ComputeMaterialSimilarityScore(referenceFileName, item.FileName)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Item.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Item.GamePath, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Item)
            .ToList();

        return rankedItems ?? [];
    }

    private static MaterialPickerItem SelectAutoMaterialCandidate(
        IReadOnlyList<MaterialPickerItem> materialCandidates,
        string referenceFileName)
    {
        if (materialCandidates == null || materialCandidates.Count == 0)
            return null;

        MaterialPickerItem? bestCandidate = null;
        double bestScore = 0.0;

        foreach (var candidate in materialCandidates)
        {
            double score = ComputeMaterialSimilarityScore(referenceFileName, candidate.FileName);
            if (score > bestScore)
            {
                bestScore = score;
                bestCandidate = candidate;
            }
        }

        return bestScore >= 0.45 ? bestCandidate : null;
    }

    private static double ComputeMaterialSimilarityScore(string referenceFileName, string candidateFileName)
    {
        string normalizedReference = NormalizeMaterialNameForComparison(referenceFileName);
        string normalizedCandidate = NormalizeMaterialNameForComparison(candidateFileName);

        if (string.IsNullOrWhiteSpace(normalizedReference) || string.IsNullOrWhiteSpace(normalizedCandidate))
            return 0.0;

        string compactReference = normalizedReference.Replace(" ", string.Empty, StringComparison.Ordinal);
        string compactCandidate = normalizedCandidate.Replace(" ", string.Empty, StringComparison.Ordinal);

        if (compactReference.Equals(compactCandidate, StringComparison.Ordinal))
            return 2.0;

        int longestCommonSubstring = GetLongestCommonSubstringLength(compactReference, compactCandidate);
        int sharedPrefixLength = GetSharedPrefixLength(compactReference, compactCandidate);

        var referenceTokens = TokenizeMaterialName(normalizedReference);
        var candidateTokens = TokenizeMaterialName(normalizedCandidate);
        int sharedTokenCount = referenceTokens.Intersect(candidateTokens).Count();
        int totalTokenCount = referenceTokens.Union(candidateTokens).Count();

        double lcsScore = (double)longestCommonSubstring / Math.Max(compactReference.Length, compactCandidate.Length);
        double prefixScore = (double)sharedPrefixLength / Math.Max(compactReference.Length, compactCandidate.Length);
        double tokenScore = totalTokenCount == 0 ? 0.0 : (double)sharedTokenCount / totalTokenCount;
        double containsBonus = compactCandidate.Contains(compactReference, StringComparison.Ordinal) ||
            compactReference.Contains(compactCandidate, StringComparison.Ordinal)
            ? 0.20
            : 0.0;

        string leadingReferenceToken = GetLeadingMaterialToken(normalizedReference);
        string leadingCandidateToken = GetLeadingMaterialToken(normalizedCandidate);
        double leadingTokenBonus = !string.IsNullOrEmpty(leadingReferenceToken) &&
            leadingReferenceToken.Equals(leadingCandidateToken, StringComparison.Ordinal)
            ? 0.15
            : 0.0;

        return lcsScore * 0.45 + prefixScore * 0.25 + tokenScore * 0.20 + containsBonus + leadingTokenBonus;
    }

    private static string NormalizeMaterialNameForComparison(string value)
    {
        string stem = Path.GetFileNameWithoutExtension(value ?? string.Empty);
        var builder = new StringBuilder(stem.Length);

        foreach (char character in stem)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }

    private static HashSet<string> TokenizeMaterialName(string normalizedName)
    {
        return normalizedName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string GetLeadingMaterialToken(string normalizedName)
    {
        return normalizedName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
    }

    private static int GetSharedPrefixLength(string left, string right)
    {
        int length = Math.Min(left.Length, right.Length);
        int prefixLength = 0;

        while (prefixLength < length && left[prefixLength] == right[prefixLength])
            prefixLength++;

        return prefixLength;
    }

    private static int GetLongestCommonSubstringLength(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return 0;

        int[,] lengths = new int[left.Length + 1, right.Length + 1];
        int bestLength = 0;

        for (int leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            for (int rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                if (left[leftIndex - 1] != right[rightIndex - 1])
                    continue;

                lengths[leftIndex, rightIndex] = lengths[leftIndex - 1, rightIndex - 1] + 1;
                if (lengths[leftIndex, rightIndex] > bestLength)
                    bestLength = lengths[leftIndex, rightIndex];
            }
        }

        return bestLength;
    }

    private static List<UnresolvedAssetReference> BuildBatchUnresolvedMaterialAssets(IEnumerable<Services.PathConversionService.BundlePathInfo> failedBundles)
    {
        return failedBundles
            .SelectMany(bundle => bundle.FailedPaths)
            .Where(path => Path.GetExtension(Services.PathConversionService.ExtractRelativePath(path)).Equals(".materialbin", StringComparison.OrdinalIgnoreCase))
            .GroupBy(path => Path.GetFileName(Services.PathConversionService.ExtractRelativePath(path)), StringComparer.OrdinalIgnoreCase)
            .Select(group => new UnresolvedAssetReference
            {
                FileName = group.Key,
                Type = "materialbin",
                SampleOriginalPath = group.First(),
                OriginalPaths = group.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            })
            .OrderBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<Services.PathConversionService.BundlePathInfo> FilterFailedBundles(
        IEnumerable<Services.PathConversionService.BundlePathInfo> failedBundles,
        IReadOnlyDictionary<string, string> resolvedMappings)
    {
        var remainingBundles = new List<Services.PathConversionService.BundlePathInfo>();

        foreach (var bundleInfo in failedBundles)
        {
            var remainingPaths = bundleInfo.FailedPaths
                .Where(path => !resolvedMappings.ContainsKey(path))
                .ToList();

            if (remainingPaths.Count == 0)
                continue;

            remainingBundles.Add(new Services.PathConversionService.BundlePathInfo
            {
                Bundle = bundleInfo.Bundle,
                OutputPath = bundleInfo.OutputPath,
                EntryName = bundleInfo.EntryName,
                FailedPaths = remainingPaths,
            });
        }

        return remainingBundles;
    }

    [RelayCommand]
    public async Task OpenFileAsync()
    {
        var picker = new FileOpenPicker();
        var window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add(".modelbin");
        picker.FileTypeFilter.Add(".carbin");
        picker.FileTypeFilter.Add(".bin");
        picker.FileTypeFilter.Add(".swatchbin");
        picker.FileTypeFilter.Add(".zip");

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        await ProcessDroppedFileAsync(file.Path);
    }

    public async Task ProcessDroppedFileAsync(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string name = Path.GetFileName(filePath).ToLowerInvariant();
        if (ext == ".zip")
        {
            await AnalyzeBatchZipAsync(filePath);
        }
        else
        {
            await AnalyzeFileAsync(filePath);
        }
    }

    private async Task AnalyzeBatchZipAsync(string zipPath)
    {
        IsBusy = true;
        StatusMessage = "Analyzing zip contents...";
        HasResult = false;
        ConversionLog.Clear();
        _batchZipPath = zipPath;

        try
        {
            int modelbinCount = 0;
            int carbinCount = 0;
            int lightsBinCount = 0;
            int swatchbinCount = 0;
            int otherCount = 0;

            await Task.Run(() =>
            {
                using var customZip = new CustomZipFile(zipPath);
                var entries = customZip.GetEntries();
                foreach (var entry in entries)
                {
                    if (entry.IsDirectory) continue;
                    string entryExt  = Path.GetExtension(entry.Name).ToLowerInvariant();
                    string entryName = Path.GetFileName(entry.Name).ToLowerInvariant();
                    if (entryExt == ".modelbin") modelbinCount++;
                    else if (entryExt == ".carbin") carbinCount++;
                    else if (entryName == "lights.bin") lightsBinCount++;
                    else if (entryExt == ".swatchbin") swatchbinCount++;
                    else otherCount++;
                }
            });

            FileName = Path.GetFileName(zipPath);
            FilePath = zipPath;
            FileTypeName = "Batch Zip";
            DetectedGame = "Multiple files";

            var detailParts = new System.Collections.Generic.List<string>();
            if (modelbinCount > 0)   detailParts.Add($"{modelbinCount} modelbin(s)");
            if (carbinCount > 0)     detailParts.Add($"{carbinCount} carbin(s)");
            if (lightsBinCount > 0)  detailParts.Add($"{lightsBinCount} lights.bin");
            if (swatchbinCount > 0)  detailParts.Add($"{swatchbinCount} swatchbin(s)");
            if (otherCount > 0)      detailParts.Add($"{otherCount} other file(s)");
            FileDetails = string.Join(", ", detailParts);

            IsModelbin = false;
            IsCarbin = false;
            IsLightsBin = false;
            IsSwatchbin = false;
            IsBatchZip = true;
            MeshCount = modelbinCount + carbinCount;

            if (string.IsNullOrEmpty(CarZipName))
                CarZipName = TryDetectCarName(zipPath);

            // Batch zip targets: superset of modelbin + carbin + lights.bin valid targets
            AvailableTargets.Clear();
            _currentTargets =
            [
                Services.ForzaGameTarget.FH5,
                Services.ForzaGameTarget.FH4,
                Services.ForzaGameTarget.FH3,
                Services.ForzaGameTarget.FM2023,
                Services.ForzaGameTarget.FM7,
                Services.ForzaGameTarget.FM6,
                Services.ForzaGameTarget.FM5,
            ];
            foreach (var target in _currentTargets)
                AvailableTargets.Add(Services.ConversionService.GetGameName(target));

            if (AvailableTargets.Count > 0)
                SelectedTargetIndex = 0;

            _currentAnalysis = new Services.FileAnalysisResult
            {
                FilePath = zipPath,
                FileName = Path.GetFileName(zipPath),
                Type = Services.FileType.Unknown,
                IsValid = true,
                DetectedGame = "Batch Zip"
            };

            HasAnalysis = true;
            StatusMessage = $"Zip analyzed: {modelbinCount} modelbins, {carbinCount} carbins";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            HasAnalysis = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AnalyzeFileAsync(string path)
    {
        IsBusy = true;
        StatusMessage = "Analyzing file...";
        HasResult = false;
        ConversionLog.Clear();
        IsBatchZip = false;
        _batchZipPath = null;

        try
        {
            var analysis = await Task.Run(() => _conversionService.AnalyzeFile(path));
            _currentAnalysis = analysis;

            FileName = analysis.FileName;
            FilePath = analysis.FilePath;
            FileTypeName = analysis.Type.ToString();
            DetectedGame = analysis.DetectedGame;
            FileDetails = analysis.Details;
            IsModelbin = analysis.Type == Services.FileType.Modelbin;
            IsCarbin = analysis.Type == Services.FileType.Carbin;
            IsLightsBin = analysis.Type == Services.FileType.LightsBin;
            IsSwatchbin = analysis.Type == Services.FileType.Swatchbin;
            IsBatchZip = false;

            if (IsModelbin)
            {
                BundleVersion = $"{analysis.BundleVersionMajor}.{analysis.BundleVersionMinor}";
                MeshVersion = $"{analysis.MeshVersionMajor}.{analysis.MeshVersionMinor}";
                ModlVersion = $"{analysis.ModlVersionMajor}.{analysis.ModlVersionMinor}";
                VlayVersion = $"{analysis.VlayVersionMajor}.{analysis.VlayVersionMinor}";
                MeshCount = analysis.MeshCount;
                MaterialCount = analysis.MaterialCount;
                VertexBufferCount = analysis.VertexBufferCount;

                // Try to auto-detect car zip name from file path
                // e.g. "...media\cars\NIS_SilviaK_92\scene\..." -> "NIS_SilviaK_92"
                if (string.IsNullOrEmpty(CarZipName))
                {
                    CarZipName = TryDetectCarName(path);
                }

                var category = VLayPatternAdvisor.CategorizeModelbin(analysis.FileName);
                VlayPatternCategory = category switch
                {
                    VLayPatternCategory.Full => "Full layout (body/exterior/interior)",
                    VLayPatternCategory.DetailSecondary => "Detail/secondary (caliper/rotor/drum)",
                    VLayPatternCategory.Minimal => "Minimal (simplified part)",
                    VLayPatternCategory.InteriorReduced => "Interior reduced",
                    VLayPatternCategory.PositionOnly => "Position-only",
                    _ => "Auto-detect from source"
                };
            }
            else if (IsCarbin)
            {
                SceneVersion = analysis.SceneVersion.ToString();
                ModelVersion = analysis.ModelVersion.ToString();
                DetectedSeries = analysis.DetectedSeries.ToString();
                MeshCount = analysis.MeshCount;
                StandardModelCount = analysis.MaterialCount;
                SharedModelCount = analysis.VertexBufferCount;
                PartCount = analysis.PartCount;
                UpgradePartCount = analysis.UpgradePartCount;
            }
            else if (IsLightsBin)
            {
                LightsBinVersion = Services.LightsBinConversionService.GetVersionLabel(analysis.LightsBinVersion);
                LightsBinLightCount = analysis.LightsBinLightCount;
                LightsBinAttachCount = analysis.LightsBinAttachCount;
                LightsBinLodCount = analysis.LightsBinLodCount;
            }
            else if (IsSwatchbin)
            {
                SwatchbinIsDurango = analysis.SwatchbinIsDurango;
                SwatchbinResolution = $"{analysis.SwatchbinWidth} × {analysis.SwatchbinHeight}";
                SwatchbinFormat = analysis.SwatchbinFormatName ?? "Unknown";
                SwatchbinMipLevels = analysis.SwatchbinMipLevels;
            }

            AvailableTargets.Clear();
            _currentTargets = _conversionService.GetValidTargets(analysis);
            foreach (var target in _currentTargets)
            {
                AvailableTargets.Add(Services.ConversionService.GetGameName(target));
            }

            if (AvailableTargets.Count > 0)
                SelectedTargetIndex = 0;

            HasAnalysis = analysis.IsValid;
            StatusMessage = analysis.IsValid
                ? $"File analyzed: {analysis.DetectedGame}"
                : $"Analysis failed: {analysis.Details}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            HasAnalysis = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Tries to detect the car name from the file path by looking for a "cars" folder in the hierarchy.
    private static string TryDetectCarName(string filePath)
    {
        try
        {
            var parts = filePath.Replace('/', '\\').Split('\\');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("cars", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                {
                    string candidate = parts[i + 1];
                    // Skip _library and other special folders
                    if (!candidate.StartsWith("_") && !candidate.Equals("scene", StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }
        }
        catch { }
        return "";
    }

    [RelayCommand]
    public async Task ConvertAsync()
    {
        if (!HasAnalysis) return;
        if (SelectedTargetIndex < 0 || SelectedTargetIndex >= _currentTargets.Count) return;

        var target = _currentTargets[SelectedTargetIndex];

        if (IsBatchZip && !string.IsNullOrEmpty(_batchZipPath))
        {
            await ConvertBatchZipAsync(target);
            return;
        }

        if (_currentAnalysis == null || !_currentAnalysis.IsValid) return;

        // Pick output file � keep original filename
        var picker = new FileSavePicker();
        var window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

        string ext = Path.GetExtension(_currentAnalysis.FilePath);
        string typeName = _currentAnalysis.Type switch
        {
            Services.FileType.Modelbin => "Modelbin",
            Services.FileType.Carbin => "Carbin",
            Services.FileType.LightsBin => "Lights Bin",
            Services.FileType.Swatchbin => "Swatchbin",
            _ => "File"
        };
        picker.FileTypeChoices.Add($"{typeName} File", [ext]);
        picker.SuggestedFileName = _currentAnalysis.FileName;

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        IsBusy = true;
        StatusMessage = $"Converting to {Services.ConversionService.GetGameName(target)}...";
        ConversionLog.Clear();
        HasResult = false;

        try
        {
            if (_currentAnalysis.Type == Services.FileType.Carbin)
                await ConvertCarbinAsync(file.Path, target);
            else if (_currentAnalysis.Type == Services.FileType.LightsBin)
                await ConvertLightsBinAsync(file.Path, target);
            else if (_currentAnalysis.Type == Services.FileType.Swatchbin)
                await ConvertSwatchbinAsync(file.Path);
            else
                await ConvertModelbinAsync(file.Path, target);
        }
        catch (Exception ex)
        {
            AddLog($"✗ Exception: {ex.Message}");
            ConversionSuccess = false;
            HasResult = true;
            StatusMessage = $"Conversion failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConvertBatchZipAsync(Services.ForzaGameTarget target)
    {
        // Pick output folder
        var picker = new FolderPicker();
        var window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;

        IsBusy = true;
        string zipName = Path.GetFileNameWithoutExtension(_batchZipPath);
        StatusMessage = $"Batch converting {zipName} to {Services.ConversionService.GetGameName(target)}...";
        ConversionLog.Clear();
        HasResult = false;

        // Resolve path conversion settings before entering the batch loop
        bool doPathConversion = IsPathConversionEnabled;
        bool doAdvancedVlayPatch = IsAdvancedVlayBlobPatchEnabled;
        bool doRemoveConflictingShaderParameters = IsRemoveConflictingShaderParametersEnabled;
        string carZipName = CarZipName?.Trim();
        string targetGamePath = null;
        string sourceGamePath = null;
        Models.SettingsConfig settings = null;

        if (doPathConversion || doAdvancedVlayPatch)
        {
            settings = await _settingsService.LoadAsync();

            string targetGameId = Services.ConversionService.GetGameSettingsId(target);
            if (settings.GamePaths.TryGetValue(targetGameId, out string tPath) && !string.IsNullOrEmpty(tPath))
                targetGamePath = tPath;

            if (string.IsNullOrEmpty(targetGamePath) && doAdvancedVlayPatch)
            {
                AddLog("? Advanced VLay blob patch enabled but target game path not configured in Setup. Skipping advanced VLay patch.");
                doAdvancedVlayPatch = false;
            }
        }

        if (doPathConversion)
        {
            if (string.IsNullOrEmpty(carZipName))
            {
                AddLog("? Path conversion enabled but no car name specified. Skipping path conversion.");
                doPathConversion = false;
            }
            else if (string.IsNullOrEmpty(targetGamePath))
            {
                AddLog("? Path conversion enabled but target game path not configured in Setup. Skipping path conversion.");
                doPathConversion = false;
            }
            else
            {
                AddLog($"Target game path: {targetGamePath}");
                AddLog($"Car zip name: {carZipName}");

                string sourceGameId = await ShowSourceGameSelectionDialogAsync(settings);

                if (sourceGameId == null)
                {
                    AddLog("? No source game selected for path conversion. Skipping path conversion.");
                    doPathConversion = false;
                }
                else if (settings.GamePaths.TryGetValue(sourceGameId, out string sPath) && !string.IsNullOrEmpty(sPath))
                {
                    sourceGamePath = sPath;
                    AddLog($"Source game: {sourceGameId} ({sourceGamePath})");
                }
                else
                {
                    AddLog("? Source game path not configured � missing files won't be copied.");
                }
            }
        }

        try
        {
            string outputBaseDir = Path.Combine(folder.Path, zipName);
            bool repackAsZip = BatchOutputAsZip;

            // Use a temp dir if repacking as zip
            string workDir = repackAsZip
                ? Path.Combine(Path.GetTempPath(), $"ForzaBatchConv_{Guid.NewGuid():N}"[..32])
                : outputBaseDir;

            AddLog($"Source zip: {Path.GetFileName(_batchZipPath)}");
            AddLog($"Target: {Services.ConversionService.GetGameName(target)}");
            AddLog($"Output: {(repackAsZip ? "Zip" : "Folder")} -> {(repackAsZip ? outputBaseDir + ".zip" : outputBaseDir)}");
            if (doPathConversion)
                AddLog($"Path conversion: Enabled (car name: {carZipName})");
            if (doPathConversion && doRemoveConflictingShaderParameters)
                AddLog("Conflicting shader parameter cleanup: Enabled");
            if (doAdvancedVlayPatch)
                AddLog("Advanced VLay blob patch: Enabled");
            AddLog("");

            int convertedCount = 0;
            int copiedCount = 0;
            int errorCount = 0;

            // Track converted modelbins for bulk path conversion
            var convertedBundles = new List<(Bundle bundle, string outputPath, string entryName, IReadOnlyList<Services.ModelbinConversionService.MaterialPathSnapshot> materialPathSnapshots)>();

            await Task.Run(() =>
            {
                Directory.CreateDirectory(workDir);

                using var customZip = new CustomZipFile(_batchZipPath);
                var entries = customZip.GetEntries();
                int total = entries.Count(e => !e.IsDirectory);
                int current = 0;

                foreach (var entry in entries)
                {
                    if (entry.IsDirectory) continue;
                    current++;

                    string entryExt = Path.GetExtension(entry.Name).ToLowerInvariant();
                    string outputPath = Path.Combine(workDir, entry.Name.Replace('/', '\\'));
                    string outputDir = Path.GetDirectoryName(outputPath);

                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                        Directory.CreateDirectory(outputDir);

                    if (entryExt == ".modelbin")
                    {
                        try
                        {
                            byte[] fileData = customZip.ExtractToMemory(entry);

                            using var ms = new MemoryStream(fileData);
                            var bundle = new Bundle();
                            bundle.Load(ms);

                            var convResult = new Services.ConversionResult();
                            _conversionService.ModelCarbinConversion.ConvertModelbinBundle(
                                bundle,
                                target,
                                convResult,
                                CreateConversionOptions(
                                    doPathConversion,
                                    carZipName,
                                    targetGamePath,
                                    sourceGamePath,
                                    sourceModelbinFileName: Path.GetFileName(entry.Name)));

                            foreach (var log in convResult.Log)
                                AddLog($"  {log}");
                            foreach (var warn in convResult.Warnings)
                                AddLog($"  ? {warn}");

                            // Don't serialize yet if path conversion is needed � we'll do it after bulk resolve
                            if (doPathConversion && !string.IsNullOrEmpty(targetGamePath))
                            {
                                var materialPathSnapshots = doRemoveConflictingShaderParameters
                                    ? _conversionService.ModelbinConversion.CaptureMaterialPathSnapshot(bundle)
                                    : Array.Empty<Services.ModelbinConversionService.MaterialPathSnapshot>();

                                convertedBundles.Add((bundle, outputPath, entry.Name, materialPathSnapshots));
                            }
                            else
                            {
                                using var outStream = File.Create(outputPath);
                                bundle.SerializeConverted(outStream);
                            }

                            AddLog($"  ? Converted: {entry.Name}");
                            convertedCount++;
                        }
                        catch (Exception ex)
                        {
                            AddLog($"  ? Error converting {entry.Name}: {ex.Message}");
                            try
                            {
                                byte[] rawData = customZip.ExtractToMemory(entry);
                                File.WriteAllBytes(outputPath, rawData);
                            }
                            catch { }
                            errorCount++;
                        }
                    }
                    else if (entryExt == ".carbin")
                    {
                        try
                        {
                            byte[] fileData = customZip.ExtractToMemory(entry);

                            string tempInput = Path.Combine(Path.GetTempPath(), $"carbin_temp_{Guid.NewGuid():N}"[..24] + ".carbin");
                            try
                            {
                                File.WriteAllBytes(tempInput, fileData);
                                var convResult = _conversionService.ModelCarbinConversion.ConvertCarbin(
                                    tempInput,
                                    outputPath,
                                    target,
                                    CreateConversionOptions(doPathConversion, carZipName, targetGamePath, sourceGamePath));

                                foreach (var log in convResult.Log)
                                    AddLog($"  {log}");
                                foreach (var warn in convResult.Warnings)
                                    AddLog($"  ? {warn}");
                                foreach (var err in convResult.Errors)
                                    AddLog($"  ? {err}");

                                if (convResult.Success)
                                {
                                    AddLog($"  ? Converted: {entry.Name}");
                                    convertedCount++;
                                }
                                else
                                {
                                    AddLog($"  ? Carbin conversion failed for {entry.Name}, original file will be used");
                                    File.WriteAllBytes(outputPath, fileData);
                                    errorCount++;
                                }
                            }
                            finally
                            {
                                try { File.Delete(tempInput); } catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLog($"  ? Error converting {entry.Name}: {ex.Message}");
                            try
                            {
                                byte[] rawData = customZip.ExtractToMemory(entry);
                                File.WriteAllBytes(outputPath, rawData);
                            }
                            catch { }
                            errorCount++;
                        }
                    }
                    else if (Path.GetFileName(entry.Name).Equals("lights.bin", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            byte[] fileData = customZip.ExtractToMemory(entry);

                            using var inputMs  = new MemoryStream(fileData);
                            using var outputMs = new MemoryStream();
                            var lightLog = _conversionService.LightsBinConversion.Convert(inputMs, outputMs, target);

                            foreach (var line in lightLog)
                                AddLog($"  {line}");

                            File.WriteAllBytes(outputPath, outputMs.ToArray());
                            AddLog($"  ✓ Converted: {entry.Name}");
                            convertedCount++;
                        }
                        catch (Exception ex)
                        {
                            AddLog($"  ✗ Error converting {entry.Name}: {ex.Message}");
                            try
                            {
                                byte[] rawData = customZip.ExtractToMemory(entry);
                                File.WriteAllBytes(outputPath, rawData);
                            }
                            catch { }
                            errorCount++;
                        }
                    }
                    else if (Path.GetExtension(entry.Name).Equals(".swatchbin", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            byte[] fileData = customZip.ExtractToMemory(entry);

                            // Probe whether this swatchbin is Durango format before converting
                            bool isDurango = false;
                            using (var probeMs = new MemoryStream(fileData))
                                isDurango = _conversionService.SwatchbinConversion.IsDurango(probeMs) == true;

                            if (isDurango)
                            {
                                using var inputMs  = new MemoryStream(fileData);
                                using var outputMs = new MemoryStream();
                                _conversionService.SwatchbinConversion.ConvertDurangoToPc(inputMs, outputMs);

                                File.WriteAllBytes(outputPath, outputMs.ToArray());
                                AddLog($"  ✓ Converted (Durango→PC): {entry.Name}");
                                convertedCount++;
                            }
                            else
                            {
                                // Already PC — copy as-is
                                File.WriteAllBytes(outputPath, fileData);
                                AddLog($"  ~ Skipped (already PC): {entry.Name}");
                                copiedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLog($"  ✗ Error converting {entry.Name}: {ex.Message}");
                            try
                            {
                                byte[] rawData = customZip.ExtractToMemory(entry);
                                File.WriteAllBytes(outputPath, rawData);
                            }
                            catch { }
                            errorCount++;
                        }
                    }
                    else
                    {
                        try
                        {
                            byte[] rawData = customZip.ExtractToMemory(entry);
                            File.WriteAllBytes(outputPath, rawData);
                        }
                        catch { }
                        copiedCount++;
                    }
                }
            });

            // Bulk path conversion: resolve all paths from all modelbins in one pass
            if (doPathConversion && convertedBundles.Count > 0 && !string.IsNullOrEmpty(targetGamePath))
            {
                AddLog("");
                AddLog($"--- Bulk path conversion for {convertedBundles.Count} modelbin(s) ---");

                var bulkLog = new List<string>();
                var (failedBundles, totalPaths, resolvedPaths, failedPaths) = await Task.Run(() =>
                    _conversionService.PathConversion.ConvertMaterialPathsBulk(
                        convertedBundles.Select(item => (item.bundle, item.outputPath, item.entryName)).ToList(),
                        target,
                        targetGamePath,
                        sourceGamePath,
                        bulkLog));

                foreach (var log in bulkLog)
                    AddLog($"  {log}");

                AddLog($"  Paths: {resolvedPaths}/{totalPaths} resolved, {failedPaths} failed");

                if (failedBundles.Count > 0)
                {
                    var unresolvedMaterialAssets = BuildBatchUnresolvedMaterialAssets(failedBundles);
                    if (unresolvedMaterialAssets.Count > 0)
                    {
                        AddLog("");
                        var manualMappings = await ResolveMaterialbinSelectionsAsync(unresolvedMaterialAssets, target, targetGamePath);
                        if (manualMappings.Count > 0)
                        {
                            foreach (var bundleInfo in failedBundles)
                                _conversionService.PathConversion.ApplyPathMappings(bundleInfo.Bundle, manualMappings);

                            failedBundles = FilterFailedBundles(failedBundles, manualMappings);
                            failedPaths = failedBundles.Sum(bundleInfo => bundleInfo.FailedPaths.Count);
                            AddLog($"  Applied {manualMappings.Count} manual material path override(s).");
                            AddLog($"  Remaining unresolved paths after manual selection: {failedPaths}");
                        }
                    }
                }

                if (doRemoveConflictingShaderParameters)
                {
                    AddLog("");
                    AddLog($"--- Removing conflicting shader parameters for {convertedBundles.Count} modelbin(s) ---");

                    int bundlesWithCleanup = 0;
                    int changedMaterials = 0;
                    int clearedBlobs = 0;
                    int clearedParameters = 0;

                    await Task.Run(() =>
                    {
                        foreach (var (_, _, entryName, materialPathSnapshots) in convertedBundles)
                        {
                            var cleanupResult = _conversionService.ModelbinConversion.RemoveConflictingShaderParameters(materialPathSnapshots);
                            if (cleanupResult.ChangedMaterialCount == 0)
                                continue;

                            bundlesWithCleanup++;
                            changedMaterials += cleanupResult.ChangedMaterialCount;
                            clearedBlobs += cleanupResult.ClearedBlobCount;
                            clearedParameters += cleanupResult.ClearedParameterCount;

                            AddLog($"  {entryName}: cleared conflicting shader parameters from {cleanupResult.ChangedMaterialCount} changed material(s), {cleanupResult.ClearedBlobCount} blob(s), {cleanupResult.ClearedParameterCount} parameter(s).");
                            foreach (var log in cleanupResult.Log)
                                AddLog($"    {log}");
                        }
                    });

                    if (bundlesWithCleanup == 0)
                    {
                        AddLog("  No materialbin swaps required shader parameter cleanup.");
                    }
                    else
                    {
                        AddLog($"  Cleanup summary: {bundlesWithCleanup} modelbin(s), {changedMaterials} changed material(s), {clearedBlobs} MTPR blob(s), {clearedParameters} parameter(s) cleared.");
                    }
                }

                if (doAdvancedVlayPatch)
                {
                    AddLog("");
                    AddLog($"--- Advanced VLay blob patch for {convertedBundles.Count} modelbin(s) ---");
                    await Task.Run(() =>
                    {
                        var patchOptions = CreateConversionOptions(
                            doPathConversion,
                            carZipName,
                            targetGamePath,
                            sourceGamePath,
                            doAdvancedVlayPatch);

                        int patchIndex = 0;
                        int patchTotal = convertedBundles.Count;

                        foreach (var (bundle, _, entryName, _) in convertedBundles)
                        {
                            patchIndex++;
                            var patchResult = new Services.ConversionResult();
                            _conversionService.ModelbinConversion.ApplyAdvancedVlayBlobPatch(
                                bundle,
                                target,
                                patchResult,
                                patchOptions);

                            AddLog($"  [{patchIndex}/{patchTotal}] Advanced VLay patch complete: {entryName}");
                            foreach (var log in patchResult.Log.Skip(1))
                                AddLog($"    {log}");
                            foreach (var warn in patchResult.Warnings)
                                AddLog($"    ? {warn}");
                            foreach (var err in patchResult.Errors)
                                AddLog($"    ? {err}");
                        }
                    });
                }

                // Handle failed paths � use batch method to open each source zip only once
                if (failedBundles.Count > 0)
                {
                    AddLog("");
                    AddLog($"--- Batch copying {failedPaths} unresolved file(s) to converted folder ---");

                    var handleLog = new List<string>();

                    // Use the batch-optimized method that opens each zip once for all bundles
                    await Task.Run(() =>
                        _conversionService.PathConversion.HandleUnsuccessfulPathsBatch(
                            failedBundles, target, targetGamePath, sourceGamePath,
                            workDir, carZipName, handleLog));

                    foreach (var log in handleLog)
                        AddLog($"  {log}");
                }

                // Now serialize all bundles (with resolved + converted paths applied)
                AddLog("");
                AddLog($"Serializing {convertedBundles.Count} modelbin(s) with updated paths...");

                await Task.Run(() =>
                {
                    foreach (var (bundle, outputPath, _, _) in convertedBundles)
                    {
                        using var outStream = File.Create(outputPath);
                        bundle.SerializeConverted(outStream);
                    }
                });

                AddLog($"  ? All modelbins serialized with updated paths");
            }

            AddLog("");
            AddLog($"Converted: {convertedCount} file(s)");
            AddLog($"Copied: {copiedCount} file(s)");
            if (errorCount > 0)
                AddLog($"? Errors: {errorCount} file(s) copied as-is");

            // Repack as zip if requested
            if (repackAsZip)
            {
                string zipOutputPath = outputBaseDir + ".zip";
                AddLog($"Repacking to zip: {Path.GetFileName(zipOutputPath)}");

                var topFiles = new List<string>();
                var topFolders = new List<string>();
                foreach (var f in Directory.GetFiles(workDir))
                    topFiles.Add(f);
                foreach (var d in Directory.GetDirectories(workDir))
                    topFolders.Add(d);

                await _zipCreationService.CreateForzaZipAsync(zipOutputPath, topFiles, topFolders);

                AddLog($"? Zip created: {zipOutputPath}");
                OutputFilePath = zipOutputPath;

                try { Directory.Delete(workDir, true); } catch { }
            }
            else
            {
                AddLog($"? Output folder: {workDir}");
                OutputFilePath = workDir;
            }

            ConversionSuccess = true;
            HasResult = true;
            StatusMessage = $"Batch conversion complete: {convertedCount} converted, {copiedCount} copied";
        }
        catch (Exception ex)
        {
            AddLog($"? Exception: {ex.Message}");
            ConversionSuccess = false;
            HasResult = true;
            StatusMessage = $"Batch conversion failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConvertSwatchbinAsync(string outputPath)
    {
        AddLog($"Loading swatchbin: {Path.GetFileName(_currentAnalysis.FilePath)}");
        AddLog($"Source: Durango/Xbox  {_currentAnalysis.SwatchbinWidth}×{_currentAnalysis.SwatchbinHeight}  {_currentAnalysis.SwatchbinFormatName}");
        AddLog($"Target: PC format (detile + re-bundle)");
        AddLog("");

        await Task.Run(() =>
        {
            _conversionService.SwatchbinConversion.ConvertDurangoToPc(
                _currentAnalysis.FilePath, outputPath);
        });

        AddLog($"✓ Detiled and converted to PC swatchbin.");

        ConversionSuccess = true;
        OutputFilePath = outputPath;
        HasResult = true;
        StatusMessage = $"Swatchbin converted: {Path.GetFileName(outputPath)}";
    }

    private async Task ConvertLightsBinAsync(string outputPath, Services.ForzaGameTarget target)
    {
        AddLog($"Loading lights.bin: {Path.GetFileName(_currentAnalysis.FilePath)}");
        AddLog($"Source: {Services.LightsBinConversionService.GetVersionLabel(_currentAnalysis.LightsBinVersion)}");
        AddLog($"Target: {Services.ConversionService.GetGameName(target)}");
        AddLog("");

        List<string> log = null;

        await Task.Run(() =>
        {
            byte[] fileData = File.ReadAllBytes(_currentAnalysis.FilePath);

            using var inputStream = new MemoryStream(fileData);
            using var outputStream = new MemoryStream();
            log = _conversionService.LightsBinConversion.Convert(inputStream, outputStream, target);

            File.WriteAllBytes(outputPath, outputStream.ToArray());
        });

        foreach (var entry in log)
            AddLog(entry);

        ConversionSuccess = true;
        OutputFilePath = outputPath;
        HasResult = true;
        StatusMessage = $"lights.bin converted: {Path.GetFileName(outputPath)}";
    }

    private async Task ConvertCarbinAsync(string outputPath, Services.ForzaGameTarget target)
    {
        AddLog($"Loading carbin: {Path.GetFileName(_currentAnalysis.FilePath)}");
        AddLog($"Source: Scene v{_currentAnalysis.SceneVersion}, Model v{_currentAnalysis.ModelVersion}, {_currentAnalysis.DetectedSeries}");

        var (targetSceneVer, targetModelVer, targetSeries, _, _, _) = Services.ModelCarbinConversionService.GetCarbinTargetVersions(target);
        AddLog($"Target: {target} -> Scene v{targetSceneVer}, Model v{targetModelVer}, {targetSeries}");
        AddLog("");

        var convResult = await Task.Run(() =>
            _conversionService.ModelCarbinConversion.ConvertCarbin(
                _currentAnalysis.FilePath,
                outputPath,
                target,
                CreateConversionOptions()));

        foreach (var log in convResult.Log)
            AddLog(log);
        foreach (var warn in convResult.Warnings)
            AddLog($"? {warn}");
        foreach (var err in convResult.Errors)
            AddLog($"? {err}");

        ConversionSuccess = convResult.Success;
        OutputFilePath = convResult.OutputPath;
        HasResult = true;

        StatusMessage = convResult.Success
            ? $"Conversion successful: {Path.GetFileName(convResult.OutputPath)}"
            : "Conversion failed. Check log for details.";
    }

    private async Task ConvertModelbinAsync(string outputPath, Services.ForzaGameTarget target)
    {
        // Resolve game paths from settings if path conversion is enabled
        string targetGamePath = null;
        string sourceGamePath = null;
        bool doPathConversion = IsPathConversionEnabled;
        bool doAdvancedVlayPatch = IsAdvancedVlayBlobPatchEnabled;
        bool doRemoveConflictingShaderParameters = IsRemoveConflictingShaderParametersEnabled;
        string carZipName = CarZipName?.Trim();
        Models.SettingsConfig settings = null;

        if (doPathConversion || doAdvancedVlayPatch)
        {
            settings = await _settingsService.LoadAsync();

            string targetGameId = Services.ConversionService.GetGameSettingsId(target);
            if (settings.GamePaths.TryGetValue(targetGameId, out string tPath) && !string.IsNullOrEmpty(tPath))
                targetGamePath = tPath;

            if (string.IsNullOrEmpty(targetGamePath) && doAdvancedVlayPatch)
            {
                AddLog("? Advanced VLay blob patch enabled but target game path not configured in Setup. Skipping advanced VLay patch.");
                doAdvancedVlayPatch = false;
            }
        }

        if (doPathConversion)
        {
            if (string.IsNullOrEmpty(carZipName))
            {
                AddLog("? Path conversion enabled but no car name specified. Skipping path conversion.");
                doPathConversion = false;
            }
            else if (string.IsNullOrEmpty(targetGamePath))
            {
                AddLog("? Path conversion enabled but target game path not configured in Setup. Skipping path conversion.");
                doPathConversion = false;
            }
            else
            {
                AddLog($"Target game path: {targetGamePath}");
                AddLog($"Car zip name: {carZipName}");

                string sourceGameId = await ShowSourceGameSelectionDialogAsync(settings);

                if (sourceGameId == null)
                {
                    AddLog("? No source game selected for path conversion. Skipping path conversion.");
                    doPathConversion = false;
                }
                else if (settings.GamePaths.TryGetValue(sourceGameId, out string sPath) && !string.IsNullOrEmpty(sPath))
                {
                    sourceGamePath = sPath;
                    AddLog($"Source game: {sourceGameId} ({sourceGamePath})");
                }
                else
                {
                    AddLog("? Source game path not configured � missing files won't be copied.");
                }
            }
        }

        // Phase 1: Load bundle, convert blobs, optionally convert paths
        AddLog($"Loading modelbin: {Path.GetFileName(_currentAnalysis.FilePath)}");

        Bundle? bundle = null;
        var convResult = new Services.ConversionResult { OutputPath = outputPath };
        Services.PathConversionResult? pathResult = null;
        IReadOnlyList<Services.ModelbinConversionService.MaterialPathSnapshot> materialPathSnapshots = Array.Empty<Services.ModelbinConversionService.MaterialPathSnapshot>();

        await Task.Run(() =>
        {
            byte[] fileData = File.ReadAllBytes(_currentAnalysis.FilePath);
            using var ms = new MemoryStream(fileData);
            bundle = new Bundle();
            bundle.Load(ms);

            convResult.Log.Add($"Bundle loaded: v{bundle.VersionMajor}.{bundle.VersionMinor}, {bundle.Blobs.Count} blobs");

            // Convert blobs in-place
            _conversionService.ModelCarbinConversion.ConvertModelbinBundle(
                bundle,
                target,
                convResult,
                CreateConversionOptions(
                    doPathConversion,
                    carZipName,
                    targetGamePath,
                    sourceGamePath,
                    sourceModelbinFileName: Path.GetFileName(_currentAnalysis.FilePath),
                    sourceDetectedGame: _currentAnalysis?.DetectedGame));

            if (doPathConversion && doRemoveConflictingShaderParameters)
            {
                materialPathSnapshots = _conversionService.ModelbinConversion.CaptureMaterialPathSnapshot(bundle);
            }

            // Path conversion � search target game for matching files
            if (doPathConversion && !string.IsNullOrEmpty(targetGamePath))
            {
                convResult.Log.Add("--- Path Conversion ---");
                pathResult = _conversionService.PathConversion.ConvertMaterialPaths(
                    bundle, target, targetGamePath, sourceGamePath, convResult);

                if (pathResult != null)
                {
                    convResult.Log.Add($"Path conversion: {pathResult.SuccessfulPaths}/{pathResult.TotalPaths} resolved");
                    foreach (var log in pathResult.Log)
                        convResult.Log.Add($"  {log}");
                    if (pathResult.FailedPaths > 0)
                        convResult.Warnings.Add($"{pathResult.FailedPaths} path(s) could not be automatically resolved");
                }
            }
        });

        // Flush phase 1 log to UI
        foreach (var log in convResult.Log)
            AddLog(log);

        Dictionary<string, string> manualMappings = [];
        if (doPathConversion && pathResult?.UnresolvedAssets?.Count > 0 && !string.IsNullOrEmpty(targetGamePath))
        {
            AddLog("");
            manualMappings = await ResolveMaterialbinSelectionsAsync(pathResult.UnresolvedAssets, target, targetGamePath);

            if (manualMappings.Count > 0)
            {
                _conversionService.PathConversion.ApplyPathMappings(bundle, manualMappings);
                AddLog($"Applied {manualMappings.Count} manual material path override(s).");
            }
        }

        if (doPathConversion && doRemoveConflictingShaderParameters)
        {
            AddLog("");
            AddLog("--- Removing conflicting shader parameters ---");

            var cleanupResult = await Task.Run(() =>
                _conversionService.ModelbinConversion.RemoveConflictingShaderParameters(materialPathSnapshots));

            if (cleanupResult.ChangedMaterialCount == 0)
            {
                AddLog("No materialbin swaps required shader parameter cleanup.");
            }
            else
            {
                AddLog($"Cleared conflicting shader parameters from {cleanupResult.ChangedMaterialCount} changed material(s), {cleanupResult.ClearedBlobCount} blob(s), {cleanupResult.ClearedParameterCount} parameter(s).");
                foreach (var log in cleanupResult.Log)
                    AddLog($"  {log}");
            }
        }

        if (doAdvancedVlayPatch)
        {
            var advancedPatchResult = new Services.ConversionResult();

            await Task.Run(() =>
                _conversionService.ModelbinConversion.ApplyAdvancedVlayBlobPatch(
                    bundle,
                    target,
                    advancedPatchResult,
                    CreateConversionOptions(
                        doPathConversion,
                        carZipName,
                        targetGamePath,
                        sourceGamePath,
                        doAdvancedVlayPatch)));

            foreach (var log in advancedPatchResult.Log)
                AddLog(log);
            foreach (var warn in advancedPatchResult.Warnings)
                AddLog($"? {warn}");
            foreach (var err in advancedPatchResult.Errors)
                AddLog($"? {err}");
        }

        // Phase 2: Handle unsuccessful paths � copy files to "converted" folder
        if (doPathConversion && pathResult != null && pathResult.FailedPaths > 0)
        {
            var failedPathsList = pathResult.FailedPathList
                .Where(path => !manualMappings.ContainsKey(path))
                .ToList();

            if (failedPathsList.Count == 0)
            {
                AddLog("All unresolved path references were resolved automatically or manually.");
            }
            else
            {
                AddLog("");
                AddLog($"--- {failedPathsList.Count} path reference(s) need attention ---");
                AddLog($"Missing files will be copied to a 'converted' folder alongside the output.");
                AddLog($"Paths will be updated to: Game:\\media\\cars\\{carZipName}\\converted\\<filename>");
                AddLog("");

                string outputDirectory = Path.GetDirectoryName(outputPath) ?? ".";
                var handleLog = new List<string>();

                string convertedDir = await Task.Run(() =>
                    _conversionService.PathConversion.HandleUnsuccessfulPaths(
                        bundle, target, targetGamePath, sourceGamePath,
                        failedPathsList, outputDirectory, carZipName, handleLog));

                foreach (var log in handleLog)
                    AddLog(log);

                if (!string.IsNullOrEmpty(convertedDir))
                    AddLog($"? Converted files saved to: {convertedDir}");
            }
        }

        // Phase 3: Write the final converted bundle
        AddLog($"Writing converted modelbin to: {Path.GetFileName(outputPath)}");

        await Task.Run(() =>
        {
            using var outStream = File.Create(outputPath);
            bundle.SerializeConverted(outStream);
        });

        convResult.Success = true;
        AddLog("Conversion completed successfully.");

        // Flush remaining warnings/errors
        foreach (var warn in convResult.Warnings)
            AddLog($"? {warn}");
        foreach (var err in convResult.Errors)
            AddLog($"? {err}");

        ConversionSuccess = convResult.Success;
        OutputFilePath = convResult.OutputPath;
        HasResult = true;

        StatusMessage = convResult.Success
            ? $"Conversion successful: {Path.GetFileName(convResult.OutputPath)}"
            : "Conversion failed. Check log for details.";
    }

    // Shows a dialog for the user to select which game to use as the source for path conversion.
    // Returns the game ID (e.g., "FH5", "FH4") or null if cancelled.
    private async Task<string> ShowSourceGameSelectionDialogAsync(Models.SettingsConfig settings)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow?.Content?.XamlRoot,
                Title = "Select Source Game",
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var stackPanel = new StackPanel { Spacing = 12 };
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Select which game to use as the source for materialbin/swatchbin files:",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 8)
            });

            var comboBox = new ComboBox
            {
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                PlaceholderText = "Choose a game..."
            };

            var gameOrder = ForzaGameCatalog.AllGames
                .Select((game, index) => new { game.GameId, index })
                .ToDictionary(item => item.GameId, item => item.index, StringComparer.OrdinalIgnoreCase);

            var availableGames = settings.GamePaths
                .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
                .Select(kvp => (gameId: kvp.Key, displayName: ForzaGameCatalog.GetDisplayName(kvp.Key), path: kvp.Value))
                .OrderBy(item => gameOrder.TryGetValue(item.gameId, out int index) ? index : int.MaxValue)
                .ToList();

            foreach (var game in availableGames)
            {
                comboBox.Items.Add($"{game.displayName} ({game.path})");
            }

            if (availableGames.Count == 0)
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "No game paths are configured. Please configure game paths in Setup first.",
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange)
                });
                dialog.IsPrimaryButtonEnabled = false;
            }
            else
            {
                stackPanel.Children.Add(comboBox);
                foreach (string detectedGameId in ForzaGameCatalog.GetMatchingGameIds(_currentAnalysis?.DetectedGame))
                {
                    int index = availableGames.FindIndex(g => g.gameId == detectedGameId);
                    if (index >= 0)
                    {
                        comboBox.SelectedIndex = index;
                        stackPanel.Children.Add(new TextBlock
                        {
                            Text = $"Auto-selected based on detected game: {_currentAnalysis?.DetectedGame}",
                            FontSize = 11, Opacity = 0.7,
                            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                        });
                        break;
                    }
                }
            }

            dialog.Content = stackPanel;
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && comboBox.SelectedIndex >= 0)
                return availableGames[comboBox.SelectedIndex].gameId;

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error showing source game selection dialog: {ex.Message}");
            return null;
        }
    }

    [RelayCommand]
    public void ClearAll()
    {
        _currentAnalysis = null;
        _currentTargets.Clear();
        _batchZipPath = null;
        AvailableTargets.Clear();
        ConversionLog.Clear();

        FileName = null;
        FilePath = null;
        FileTypeName = null;
        DetectedGame = null;
        FileDetails = null;
        IsModelbin = false;
        IsCarbin = false;
        IsBatchZip = false;
        IsLightsBin = false;
        IsSwatchbin = false;
        HasAnalysis = false;
        HasResult = false;
        SelectedTargetIndex = -1;
        StandardModelCount = 0;
        SharedModelCount = 0;
        PartCount = 0;
        UpgradePartCount = 0;
        CarZipName = "";

        StatusMessage = "Ready. Open a file to begin.";
    }
}
