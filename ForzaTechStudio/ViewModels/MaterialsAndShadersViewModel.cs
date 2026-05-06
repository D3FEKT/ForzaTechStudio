using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTechStudio.Services;
using ForzaTools.Bundles.Blobs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.ViewModels;

public sealed class MaterialShaderKeyValueItem
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class MaterialShaderParameterComparisonItem
{
    public string Name { get; set; } = string.Empty;
    public string SourceState { get; set; } = string.Empty;
    public bool IsExplicitOverride { get; set; }
    public string TypeText { get; set; } = string.Empty;
    public string VersionText { get; set; } = string.Empty;
    public string NameHashText { get; set; } = string.Empty;
    public string GuidText { get; set; } = string.Empty;
    public string ExtraValueText { get; set; } = string.Empty;
    public string DefaultValueText { get; set; } = string.Empty;
    public string EffectiveValueText { get; set; } = string.Empty;
}

public partial class MaterialsAndShadersViewModel : ObservableObject
{
    // Top-level Pivot indices
    private const int TopLevelMaterialbinIndex = 0;
    private const int TopLevelShaderbinIndex = 1;

    // Materialbin nested Pivot indices
    private const int MatOverviewIndex = 0;
    private const int MatParametersIndex = 1;
    private const int MatTexturesIndex = 2;

    // Shaderbin nested Pivot indices
    private const int ShaderOverviewIndex = 0;
    private const int ShaderParametersIndex = 1;
    private const int ShaderScenariosIndex = 2;
    private const int ShaderMappingsIndex = 3;
    private const int ShaderRenderTargetsIndex = 4;
    private const int ShaderRawBundleIndex = 5;

    private readonly SettingsService _settingsService = new();
    private readonly MaterialsAndShadersWorkspaceService _workspaceService = new();
    private readonly SwatchbinPreviewService _swatchbinPreviewService = new();
    private readonly Dictionary<string, string> _configuredGamePaths = new(StringComparer.OrdinalIgnoreCase);

    private MaterialsAndShadersWorkspace? _currentWorkspace;
    private CancellationTokenSource? _texturePreviewLoadCts;
    private bool _isInitialized;

    public ObservableCollection<ForzaGameDefinition> AvailableGames { get; } = [];
    public ObservableCollection<MaterialShaderReferenceNode> ReferenceChainItems { get; } = [];
    public ObservableCollection<MaterialShaderValidationIssue> ValidationItems { get; } = [];
    public ObservableCollection<MaterialShaderTextureReference> TextureItems { get; } = [];
    public ObservableCollection<MaterialShaderMappingItem> ConstantBufferMappings { get; } = [];
    public ObservableCollection<MaterialShaderMappingItem> TextureMappings { get; } = [];
    public ObservableCollection<MaterialShaderMappingItem> SamplerMappings { get; } = [];
    public ObservableCollection<MaterialShaderScenarioItem> ScenarioItems { get; } = [];
    public ObservableCollection<MaterialShaderRenderTargetItem> RenderTargetItems { get; } = [];
    public ObservableCollection<MaterialShaderRawBlobItem> RawBlobItems { get; } = [];
    public ObservableCollection<MaterialShaderKeyValueItem> SourceInfoItems { get; } = [];
    public ObservableCollection<MaterialShaderKeyValueItem> MaterialSummaryItems { get; } = [];
    public ObservableCollection<MaterialShaderKeyValueItem> ShaderSummaryItems { get; } = [];
    public ObservableCollection<MaterialShaderKeyValueItem> MaterialDefinitionItems { get; } = [];
    public ObservableCollection<MaterialShaderKeyValueItem> ShaderIdentityItems { get; } = [];
    public ObservableCollection<MaterialShaderKeyValueItem> InspectorDetailItems { get; } = [];
    public ObservableCollection<MaterialShaderParameterComparisonItem> MaterialParameterComparisonItems { get; } = [];
    public ObservableCollection<ShaderParameter> MaterialParameters { get; } = [];
    public ObservableCollection<ShaderParameter> ShaderParameters { get; } = [];

    private bool _isBusy;
    private string _inspectorSectionTitle = string.Empty;
    private MaterialShaderTextureReference? _selectedTextureItem;
    private MaterialShaderScenarioItem? _selectedScenarioItem;
    private MaterialShaderMappingItem? _selectedMappingItem;
    private MaterialShaderRenderTargetItem? _selectedRenderTargetItem;
    private string _statusMessage = "Open a .materialbin or .shaderbin to begin, or use Browse Game Library in the toolbar.";
    private string _selectedGameId = string.Empty;
    private MaterialShaderLibraryAsset? _selectedLibraryItem;
    private int _selectedTopLevelPivotIndex;
    private int _selectedMaterialPivotIndex;
    private int _selectedShaderPivotIndex;
    private string? _loadErrorBanner;
    private bool _isDirty;
    private string? _attachedShaderOverrideFilePath;
    private string? _attachedShaderOverrideGamePath;
    private string? _attachedShaderOverrideGameId;
    private string? _attachedShaderOverrideGameRootPath;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string InspectorSectionTitle
    {
        get => _inspectorSectionTitle;
        private set => SetProperty(ref _inspectorSectionTitle, value);
    }

    public bool HasInspectorDetails => InspectorDetailItems.Count > 0;

    public MaterialShaderTextureReference? SelectedTextureItem
    {
        get => _selectedTextureItem;
        set
        {
            if (SetProperty(ref _selectedTextureItem, value))
                RebuildInspectorDetails();
        }
    }

    public MaterialShaderScenarioItem? SelectedScenarioItem
    {
        get => _selectedScenarioItem;
        set
        {
            if (SetProperty(ref _selectedScenarioItem, value))
                RebuildInspectorDetails();
        }
    }

    public MaterialShaderMappingItem? SelectedMappingItem
    {
        get => _selectedMappingItem;
        set
        {
            if (SetProperty(ref _selectedMappingItem, value))
                RebuildInspectorDetails();
        }
    }

    public MaterialShaderRenderTargetItem? SelectedRenderTargetItem
    {
        get => _selectedRenderTargetItem;
        set
        {
            if (SetProperty(ref _selectedRenderTargetItem, value))
                RebuildInspectorDetails();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SelectedGameId
    {
        get => _selectedGameId;
        set => SetProperty(ref _selectedGameId, value);
    }

    public MaterialShaderLibraryAsset? SelectedLibraryItem
    {
        get => _selectedLibraryItem;
        set
        {
            if (SetProperty(ref _selectedLibraryItem, value))
            {
                OnPropertyChanged(nameof(HasSelectedLibraryItem));
                OnPropertyChanged(nameof(SelectedLibraryItemIsSwatchbin));
            }
        }
    }

    public int SelectedTopLevelPivotIndex
    {
        get => _selectedTopLevelPivotIndex;
        set => SetProperty(ref _selectedTopLevelPivotIndex, value);
    }

    public int SelectedMaterialPivotIndex
    {
        get => _selectedMaterialPivotIndex;
        set => SetProperty(ref _selectedMaterialPivotIndex, value);
    }

    public int SelectedShaderPivotIndex
    {
        get => _selectedShaderPivotIndex;
        set => SetProperty(ref _selectedShaderPivotIndex, value);
    }

    public string? LoadErrorBanner
    {
        get => _loadErrorBanner;
        private set
        {
            if (SetProperty(ref _loadErrorBanner, value))
                OnPropertyChanged(nameof(HasLoadError));
        }
    }

    public bool HasLoadError => !string.IsNullOrEmpty(_loadErrorBanner);

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
                SaveAsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasWorkspace => _currentWorkspace != null;
    public bool HasNoWorkspace => !HasWorkspace;
    public bool HasMaterialDocument => _currentWorkspace?.MaterialDocument != null;
    public bool HasShaderDocument => _currentWorkspace?.ShaderDocument != null;
    public bool HasSelectedLibraryItem => SelectedLibraryItem != null;
    public bool HasTextureReferences => TextureItems.Count > 0;
    public bool HasRawBlobs => RawBlobItems.Count > 0;
    public bool HasMaterialParameterComparison => MaterialParameterComparisonItems.Count > 0;
    public bool SelectedLibraryItemIsSwatchbin => string.Equals(SelectedLibraryItem?.AssetKind, "Swatchbin", StringComparison.OrdinalIgnoreCase);
    public bool HasConstantBufferMappings => ConstantBufferMappings.Count > 0;
    public bool HasTextureMappings => TextureMappings.Count > 0;
    public bool HasSamplerMappings => SamplerMappings.Count > 0;

    public string EditorModeText => _currentWorkspace == null
        ? "No document loaded"
        : _currentWorkspace.PrimaryKind == MaterialShaderAssetKind.Materialbin
            ? "Materialbin workstation"
            : "Shaderbin workstation";

    public string MaterialEditorStateText => HasMaterialDocument
        ? "Edit material parameter overrides below. Changes are written directly to the source bundle and saved with Save / Save As."
        : "No materialbin is loaded.";

    public string ShaderEditorStateText => HasShaderDocument
        ? "Read-only shader defaults and blob identity for the current increment."
        : "No shaderbin is loaded.";

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        _isInitialized = true;
        await LoadConfiguredGamesAsync();
    }

    [RelayCommand]
    private async Task OpenMaterialbinAsync()
    {
        string? path = await PickFileAsync(".materialbin");
        if (string.IsNullOrWhiteSpace(path))
            return;

        await LoadWorkspaceAsync(
            () => _workspaceService.LoadFromFile(path, SelectedGameIdOrNull(), SelectedGameRootOrNull()),
            $"Loaded materialbin '{Path.GetFileName(path)}'.",
            () =>
            {
                ClearAttachedShaderOverride();
                SelectedTopLevelPivotIndex = TopLevelMaterialbinIndex;
                SelectedMaterialPivotIndex = MatOverviewIndex;
            });
    }

    [RelayCommand]
    private async Task OpenShaderbinAsync()
    {
        string? path = await PickFileAsync(".shaderbin");
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (HasMaterialDocument)
        {
            await LoadWorkspaceAsync(
                () => _workspaceService.AttachShaderFromFile(_currentWorkspace!, path),
                $"Attached shaderbin '{Path.GetFileName(path)}'.",
                () =>
                {
                    RememberAttachedShaderFile(path);
                    SelectedTopLevelPivotIndex = TopLevelShaderbinIndex;
                    SelectedShaderPivotIndex = ShaderOverviewIndex;
                });
        }
        else
        {
            await LoadWorkspaceAsync(
                () => _workspaceService.LoadFromFile(path, SelectedGameIdOrNull(), SelectedGameRootOrNull()),
                $"Loaded shaderbin '{Path.GetFileName(path)}'.",
                () =>
                {
                    ClearAttachedShaderOverride();
                    SelectedTopLevelPivotIndex = TopLevelShaderbinIndex;
                    SelectedShaderPivotIndex = ShaderOverviewIndex;
                });
        }
    }

    [RelayCommand]
    private async Task OpenSelectedLibraryAssetAsync()
    {
        if (SelectedLibraryItem == null)
            return;

        string? gameRoot = SelectedGameRootOrNull();
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            StatusMessage = "The selected game root is not configured.";
            return;
        }

        bool isShaderAsset = string.Equals(SelectedLibraryItem.AssetKind, "Shaderbin", StringComparison.OrdinalIgnoreCase);

        if (HasMaterialDocument && isShaderAsset)
        {
            await LoadWorkspaceAsync(
                () => _workspaceService.AttachShaderFromGamePath(_currentWorkspace!, SelectedGameId, gameRoot, SelectedLibraryItem.GamePath),
                $"Attached shaderbin '{SelectedLibraryItem.DisplayName}' from the shared library.",
                () =>
                {
                    RememberAttachedShaderGamePath(SelectedGameId, gameRoot, SelectedLibraryItem.GamePath);
                    SelectedTopLevelPivotIndex = TopLevelShaderbinIndex;
                    SelectedShaderPivotIndex = ShaderOverviewIndex;
                });
            return;
        }

        await LoadWorkspaceAsync(
            () => _workspaceService.LoadFromGamePath(SelectedGameId, gameRoot, SelectedLibraryItem.GamePath),
            $"Loaded '{SelectedLibraryItem.DisplayName}' from the shared library.",
            () =>
            {
                ClearAttachedShaderOverride();
                SelectedTopLevelPivotIndex = TopLevelMaterialbinIndex;
                SelectedMaterialPivotIndex = MatOverviewIndex;
            });
    }

    public async Task<string?> ResolveSelectedLibrarySwatchPathAsync()
    {
        if (!SelectedLibraryItemIsSwatchbin || SelectedLibraryItem == null)
            return null;

        string? gameRoot = SelectedGameRootOrNull();
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            StatusMessage = "The selected game root is not configured.";
            return null;
        }

        try
        {
            IsBusy = true;
            string preparedPath = await Task.Run(() => _workspaceService.PrepareAssetFileForExternalViewer(gameRoot, SelectedLibraryItem.GamePath));
            StatusMessage = $"Prepared swatchbin '{SelectedLibraryItem.DisplayName}' from the shared library.";
            return preparedPath;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to prepare swatchbin '{SelectedLibraryItem.DisplayName}': {ex.Message}";
            App.ShowErrorDialog(StatusMessage);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReloadWorkspaceAsync()
    {
        if (_currentWorkspace == null)
            return;

        string? gameRoot = _currentWorkspace.SelectedGameRootPath;
        string? gameId = _currentWorkspace.SelectedGameId;
        string sourcePath = _currentWorkspace.PrimarySourcePath;

        try
        {
            IsBusy = true;

            MaterialsAndShadersWorkspace workspace = await Task.Run(() =>
            {
                MaterialsAndShadersWorkspace reloadedWorkspace;

                if (sourcePath.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(gameRoot) && !string.IsNullOrWhiteSpace(gameId))
                {
                    reloadedWorkspace = _workspaceService.LoadFromGamePath(gameId, gameRoot, sourcePath);
                }
                else
                {
                    reloadedWorkspace = _workspaceService.LoadFromFile(sourcePath, gameId, gameRoot);
                }

                if (reloadedWorkspace.MaterialDocument == null)
                    return reloadedWorkspace;

                if (!string.IsNullOrWhiteSpace(_attachedShaderOverrideFilePath))
                {
                    return _workspaceService.AttachShaderFromFile(reloadedWorkspace, _attachedShaderOverrideFilePath);
                }

                if (!string.IsNullOrWhiteSpace(_attachedShaderOverrideGamePath) &&
                    !string.IsNullOrWhiteSpace(_attachedShaderOverrideGameId) &&
                    !string.IsNullOrWhiteSpace(_attachedShaderOverrideGameRootPath))
                {
                    return _workspaceService.AttachShaderFromGamePath(
                        reloadedWorkspace,
                        _attachedShaderOverrideGameId,
                        _attachedShaderOverrideGameRootPath,
                        _attachedShaderOverrideGamePath);
                }

                return reloadedWorkspace;
            });

            ApplyWorkspace(workspace);
            StatusMessage = HasAttachedShaderOverride()
                ? $"Reloaded '{Path.GetFileName(sourcePath)}' and preserved the explicitly attached shader."
                : $"Reloaded '{Path.GetFileName(sourcePath)}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reload failed: {ex.Message}";
            App.ShowErrorDialog(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CloseWorkspace()
    {
        _currentWorkspace = null;
        IsDirty = false;
        ClearAttachedShaderOverride();
        ClearWorkspaceCollections();
        SelectedTopLevelPivotIndex = TopLevelMaterialbinIndex;
        SelectedMaterialPivotIndex = MatOverviewIndex;
        LoadErrorBanner = null;
        StatusMessage = "Workspace cleared.";
        RaiseWorkspaceStateChanged();
    }

    [RelayCommand(CanExecute = nameof(HasShaderDocument))]
    private void RevealLinkedShader()
    {
        SelectedTopLevelPivotIndex = TopLevelShaderbinIndex;
        SelectedShaderPivotIndex = ShaderOverviewIndex;
    }

    [RelayCommand(CanExecute = nameof(HasTextureReferences))]
    private void RevealReferencedTextures()
    {
        SelectedTopLevelPivotIndex = TopLevelMaterialbinIndex;
        SelectedMaterialPivotIndex = MatTexturesIndex;
    }

    [RelayCommand(CanExecute = nameof(HasRawBlobs))]
    private void OpenRawBlobView()
    {
        SelectedTopLevelPivotIndex = TopLevelShaderbinIndex;
        SelectedShaderPivotIndex = ShaderRawBundleIndex;
    }

    private async Task LoadConfiguredGamesAsync()
    {
        var settings = await _settingsService.LoadAsync();
        AvailableGames.Clear();
        _configuredGamePaths.Clear();

        foreach (var game in ForzaGameCatalog.AllGames)
        {
            if (!settings.GamePaths.TryGetValue(game.GameId, out string? gamePath) || string.IsNullOrWhiteSpace(gamePath))
                continue;

            AvailableGames.Add(game);
            _configuredGamePaths[game.GameId] = gamePath;
        }

        if (AvailableGames.Count > 0)
            SelectedGameId = AvailableGames[0].GameId;
    }

    private async Task LoadWorkspaceAsync(Func<MaterialsAndShadersWorkspace> loader, string successMessage, Action? onSuccess = null)
    {
        try
        {
            IsBusy = true;
            LoadErrorBanner = null;
            var workspace = await Task.Run(loader);
            ApplyWorkspace(workspace);
            onSuccess?.Invoke();
            StatusMessage = successMessage;
        }
        catch (Exception ex)
        {
            // Reset to clean state so the page is usable after a failed load
            CloseWorkspace();
            StatusMessage = $"Load failed: {ex.Message}";
            LoadErrorBanner = StatusMessage;
            App.ShowErrorDialog(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DismissLoadError()
    {
        LoadErrorBanner = null;
    }

    [RelayCommand]
    private void RemoveMaterialParameter(ShaderParameter? parameter)
    {
        if (parameter == null || _currentWorkspace?.MaterialDocument == null)
            return;

        // Remove from both the ViewModel display collection and the underlying blob collection so serialization is correct
        MaterialParameters.Remove(parameter);
        _currentWorkspace.MaterialDocument.Parameters.Remove(parameter);
    }

    [RelayCommand]
    private async Task AddMaterialParameterAsync()
    {
        if (_currentWorkspace?.MaterialDocument == null)
            return;

        var nameBox = new AutoSuggestBox
        {
            Header = "Parameter Name (or 0x hex hash)",
            PlaceholderText = "e.g. DiffuseTexture or 0xA1B2C3D4",
            ItemsSource = NameHashService.Instance.GetAll().Values.OrderBy(n => n).ToList(),
        };
        nameBox.TextChanged += (s, e) =>
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var q = s.Text.ToLowerInvariant();
                s.ItemsSource = NameHashService.Instance.GetAll().Values
                    .Where(n => n.ToLowerInvariant().Contains(q))
                    .OrderBy(n => n).ToList();
            }
        };

        var typeBox = new ComboBox
        {
            Header = "Parameter Type",
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            ItemsSource = Enum.GetValues<ShaderParameterType>(),
        };
        typeBox.SelectedIndex = 0;

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(nameBox);
        panel.Children.Add(typeBox);

        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = "Add Shader Parameter",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        uint hash = 0;
        string inputName = nameBox.Text.Trim();
        var knownHash = NameHashService.Instance.GetHash(inputName);
        if (knownHash.HasValue)
        {
            hash = knownHash.Value;
        }
        else
        {
            string hexText = inputName.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? inputName[2..] : inputName;
            if (!uint.TryParse(hexText, System.Globalization.NumberStyles.HexNumber, null, out hash))
            {
                App.ShowErrorDialog($"Unknown parameter name or invalid hash: {inputName}");
                return;
            }
        }

        var type = (ShaderParameterType)typeBox.SelectedItem!;
        object? defaultValue = type switch
        {
            ShaderParameterType.Bool => (object?)false,
            ShaderParameterType.Float => 0f,
            ShaderParameterType.Int => 0,
            ShaderParameterType.Vector2 => System.Numerics.Vector2.Zero,
            ShaderParameterType.Vector => System.Numerics.Vector4.Zero,
            ShaderParameterType.Color => System.Numerics.Vector4.One,
            ShaderParameterType.Texture2D => new TextureParameter { Path = string.Empty },
            ShaderParameterType.Sampler => new SamplerParameter(),
            _ => null,
        };

        var newParam = new ShaderParameter
        {
            VersionMajor = 2,
            VersionMinor = 0,
            NameHash = hash,
            Type = type,
            Value = defaultValue,
        };

        // Add to the underlying blob collection (will be serialized on save)
        _currentWorkspace.MaterialDocument.Parameters.Add(newParam);
        // Add to the ViewModel display collection
        MaterialParameters.Add(newParam);
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task SaveAsync()
    {
        if (_currentWorkspace == null)
            return;

        string sourcePath = _currentWorkspace.PrimarySourcePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || sourcePath.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase))
        {
            await SaveAsAsync();
            return;
        }

        try
        {
            IsBusy = true;
            await Task.Run(() => _workspaceService.SaveToFile(_currentWorkspace, sourcePath));
            IsDirty = false;
            StatusMessage = $"Saved '{Path.GetFileName(sourcePath)}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            App.ShowErrorDialog(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task SaveAsAsync()
    {
        if (_currentWorkspace == null)
            return;

        string ext = _currentWorkspace.PrimaryKind == MaterialShaderAssetKind.Materialbin
            ? ".materialbin" : ".shaderbin";

        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add(ext == ".materialbin" ? "Materialbin" : "Shaderbin", [ext]);
        picker.SuggestedFileName = Path.GetFileName(_currentWorkspace.PrimarySourcePath);

        var file = await picker.PickSaveFileAsync();
        if (file == null)
            return;

        try
        {
            IsBusy = true;
            await Task.Run(() => _workspaceService.SaveToFile(_currentWorkspace, file.Path));
            IsDirty = false;
            StatusMessage = $"Saved '{file.Name}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save As failed: {ex.Message}";
            App.ShowErrorDialog(StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyWorkspace(MaterialsAndShadersWorkspace workspace)
    {
        CancelTexturePreviewLoad();
        _currentWorkspace = workspace;
        IsDirty = false;

        // Unsubscribe previous dirty-tracking hook then subscribe to new collection
        MaterialParameters.CollectionChanged -= OnMaterialParametersChanged;

        ReplaceCollection(SourceInfoItems, BuildSourceInfo(workspace));
        ReplaceCollection(MaterialSummaryItems, BuildMaterialSummary(workspace.MaterialDocument));
        ReplaceCollection(ShaderSummaryItems, BuildShaderSummary(workspace.ShaderDocument));
        ReplaceCollection(MaterialDefinitionItems, BuildMaterialDefinitions(workspace.MaterialDocument));
        ReplaceCollection(ShaderIdentityItems, BuildShaderIdentity(workspace.ShaderDocument));
        ReplaceCollection(MaterialParameterComparisonItems, BuildMaterialParameterComparisons(workspace.MaterialDocument, workspace.ShaderDocument));

        ReplaceCollection(ReferenceChainItems, workspace.ReferenceChain);
        ReplaceCollection(ValidationItems, workspace.ValidationIssues);
        ReplaceCollection(TextureItems, workspace.TextureReferences);
        ReplaceCollection(ConstantBufferMappings, workspace.ConstantBufferMappings);
        ReplaceCollection(TextureMappings, workspace.TextureMappings);
        ReplaceCollection(SamplerMappings, workspace.SamplerMappings);
        ReplaceCollection(ScenarioItems, workspace.Scenarios);
        ReplaceCollection(RenderTargetItems, workspace.RenderTargets);
        ReplaceCollection(RawBlobItems, workspace.RawBlobs);
        ReplaceCollection(MaterialParameters, workspace.MaterialDocument?.Parameters ?? Enumerable.Empty<ShaderParameter>());
        ReplaceCollection(ShaderParameters, workspace.ShaderDocument?.DefaultParameters ?? Enumerable.Empty<ShaderParameter>());

        MaterialParameters.CollectionChanged += OnMaterialParametersChanged;

        RaiseWorkspaceStateChanged();
        StartTexturePreviewLoad(workspace.TextureReferences);
    }

    private void OnMaterialParametersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        IsDirty = true;
    }

    public void NotifyMaterialParametersEdited()
    {
        IsDirty = true;
    }

    private void ClearWorkspaceCollections()
    {
        CancelTexturePreviewLoad();
        MaterialParameters.CollectionChanged -= OnMaterialParametersChanged;
        SourceInfoItems.Clear();
        MaterialSummaryItems.Clear();
        ShaderSummaryItems.Clear();
        MaterialDefinitionItems.Clear();
        ShaderIdentityItems.Clear();
        MaterialParameterComparisonItems.Clear();
        ReferenceChainItems.Clear();
        ValidationItems.Clear();
        TextureItems.Clear();
        ConstantBufferMappings.Clear();
        TextureMappings.Clear();
        SamplerMappings.Clear();
        ScenarioItems.Clear();
        RenderTargetItems.Clear();
        RawBlobItems.Clear();
        MaterialParameters.Clear();
        ShaderParameters.Clear();
        SelectedTextureItem = null;
        SelectedScenarioItem = null;
        SelectedMappingItem = null;
        SelectedRenderTargetItem = null;
        InspectorDetailItems.Clear();
        InspectorSectionTitle = string.Empty;
        OnPropertyChanged(nameof(HasInspectorDetails));
    }

    private void RaiseWorkspaceStateChanged()
    {
        OnPropertyChanged(nameof(HasWorkspace));
        OnPropertyChanged(nameof(HasNoWorkspace));
        OnPropertyChanged(nameof(HasMaterialDocument));
        OnPropertyChanged(nameof(HasShaderDocument));
        OnPropertyChanged(nameof(HasSelectedLibraryItem));
        OnPropertyChanged(nameof(HasTextureReferences));
        OnPropertyChanged(nameof(HasRawBlobs));
        OnPropertyChanged(nameof(HasMaterialParameterComparison));
        OnPropertyChanged(nameof(HasConstantBufferMappings));
        OnPropertyChanged(nameof(HasTextureMappings));
        OnPropertyChanged(nameof(HasSamplerMappings));
        OnPropertyChanged(nameof(EditorModeText));
        OnPropertyChanged(nameof(MaterialEditorStateText));
        OnPropertyChanged(nameof(ShaderEditorStateText));
        RevealLinkedShaderCommand.NotifyCanExecuteChanged();
        RevealReferencedTexturesCommand.NotifyCanExecuteChanged();
        OpenRawBlobViewCommand.NotifyCanExecuteChanged();
        AddMaterialParameterCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
    }

    private void RebuildInspectorDetails()
    {
        InspectorDetailItems.Clear();

        if (SelectedTextureItem != null)
        {
            InspectorSectionTitle = SelectedTextureItem.ParameterName;
            AddInspectorRow("Parameter", SelectedTextureItem.ParameterName);
            AddInspectorRow("Hash", SelectedTextureItem.NameHashText);
            AddInspectorRow("Path", SelectedTextureItem.TexturePath);
            AddInspectorRow("Size", SelectedTextureItem.SizeText);
            AddInspectorRow("Format", SelectedTextureItem.FormatText);
            AddInspectorRow("Mip Levels", SelectedTextureItem.MipLevelsText);
            AddInspectorRow("Platform", SelectedTextureItem.PlatformText);
            AddInspectorRow("Encoding", SelectedTextureItem.EncodingText);
            AddInspectorRow("Status", SelectedTextureItem.StatusText);
        }
        else if (SelectedScenarioItem != null)
        {
            InspectorSectionTitle = SelectedScenarioItem.Name;
            AddInspectorRow("Name", SelectedScenarioItem.Name);
            AddInspectorRow("Hash", SelectedScenarioItem.ScenarioHashText);
            AddInspectorRow("Version", SelectedScenarioItem.VersionText);
            AddInspectorRow("Inline", SelectedScenarioItem.InlineState);
            AddInspectorRow("Vertex Input", SelectedScenarioItem.VertexInputFlags);
            AddInspectorRow("Shader Stages", SelectedScenarioItem.StageBitsText);
        }
        else if (SelectedMappingItem != null)
        {
            InspectorSectionTitle = SelectedMappingItem.Name;
            AddInspectorRow("Name", SelectedMappingItem.Name);
            AddInspectorRow("Hash", SelectedMappingItem.NameHashText);
            AddInspectorRow("Slot", SelectedMappingItem.SlotText);
            AddInspectorRow("GUID", SelectedMappingItem.GuidText);
        }
        else if (SelectedRenderTargetItem != null)
        {
            InspectorSectionTitle = SelectedRenderTargetItem.Label;
            AddInspectorRow("Label", SelectedRenderTargetItem.Label);
            AddInspectorRow("Version", SelectedRenderTargetItem.VersionText);
            AddInspectorRow("Inline", SelectedRenderTargetItem.InlineState);
            AddInspectorRow("Entries", SelectedRenderTargetItem.EntryCount);
            AddInspectorRow("VS / PS", SelectedRenderTargetItem.EntrySummary);
        }
        else
        {
            InspectorSectionTitle = string.Empty;
        }

        OnPropertyChanged(nameof(HasInspectorDetails));
    }

    private void AddInspectorRow(string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            InspectorDetailItems.Add(new MaterialShaderKeyValueItem { Label = label, Value = value });
    }

    private void StartTexturePreviewLoad(IReadOnlyList<MaterialShaderTextureReference> textureItems)
    {
        CancelTexturePreviewLoad();
        if (textureItems.Count == 0)
            return;

        var cancellationSource = new CancellationTokenSource();
        _texturePreviewLoadCts = cancellationSource;
        _ = LoadTexturePreviewsAsync(textureItems, cancellationSource);
    }

    private async Task LoadTexturePreviewsAsync(IReadOnlyList<MaterialShaderTextureReference> textureItems, CancellationTokenSource cancellationSource)
    {
        var previewCache = new Dictionary<string, BitmapImage?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var item in textureItems)
            {
                cancellationSource.Token.ThrowIfCancellationRequested();

                if (item.PreviewSwatchInfo == null)
                {
                    item.IsPreviewLoading = false;
                    item.PreviewStateText = "Preview unavailable.";
                    continue;
                }

                string cacheKey = !string.IsNullOrWhiteSpace(item.TexturePath)
                    ? item.TexturePath
                    : item.ResolvedSource;

                if (previewCache.TryGetValue(cacheKey, out var cachedPreview))
                {
                    item.PreviewImage = cachedPreview;
                    item.IsPreviewLoading = false;
                    item.PreviewStateText = cachedPreview == null ? "Preview unavailable." : string.Empty;
                    continue;
                }

                var previewImage = await _swatchbinPreviewService.CreatePreviewImageAsync(item.PreviewSwatchInfo, cancellationToken: cancellationSource.Token);
                previewCache[cacheKey] = previewImage;

                cancellationSource.Token.ThrowIfCancellationRequested();

                item.PreviewImage = previewImage;
                item.IsPreviewLoading = false;
                item.PreviewStateText = previewImage == null ? "Preview unavailable." : string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_texturePreviewLoadCts, cancellationSource))
            {
                _texturePreviewLoadCts = null;
                cancellationSource.Dispose();
            }
        }
    }

    private void CancelTexturePreviewLoad()
    {
        if (_texturePreviewLoadCts == null)
            return;

        var cancellationSource = _texturePreviewLoadCts;
        _texturePreviewLoadCts = null;
        cancellationSource.Cancel();
        cancellationSource.Dispose();
    }

    private string? SelectedGameIdOrNull()
    {
        return string.IsNullOrWhiteSpace(SelectedGameId) ? null : SelectedGameId;
    }

    private string? SelectedGameRootOrNull()
    {
        return _configuredGamePaths.TryGetValue(SelectedGameId, out string? path) && !string.IsNullOrWhiteSpace(path)
            ? path
            : null;
    }

    private bool HasAttachedShaderOverride()
    {
        return !string.IsNullOrWhiteSpace(_attachedShaderOverrideFilePath) ||
               (!string.IsNullOrWhiteSpace(_attachedShaderOverrideGamePath) &&
                !string.IsNullOrWhiteSpace(_attachedShaderOverrideGameId) &&
                !string.IsNullOrWhiteSpace(_attachedShaderOverrideGameRootPath));
    }

    private void RememberAttachedShaderFile(string filePath)
    {
        _attachedShaderOverrideFilePath = filePath;
        _attachedShaderOverrideGamePath = null;
        _attachedShaderOverrideGameId = null;
        _attachedShaderOverrideGameRootPath = null;
    }

    private void RememberAttachedShaderGamePath(string gameId, string gameRootPath, string gamePath)
    {
        _attachedShaderOverrideFilePath = null;
        _attachedShaderOverrideGamePath = gamePath;
        _attachedShaderOverrideGameId = gameId;
        _attachedShaderOverrideGameRootPath = gameRootPath;
    }

    private void ClearAttachedShaderOverride()
    {
        _attachedShaderOverrideFilePath = null;
        _attachedShaderOverrideGamePath = null;
        _attachedShaderOverrideGameId = null;
        _attachedShaderOverrideGameRootPath = null;
    }

    private static async Task<string?> PickFileAsync(string extension)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.ViewMode = PickerViewMode.List;
        picker.FileTypeFilter.Add(extension);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private static List<MaterialShaderKeyValueItem> BuildSourceInfo(MaterialsAndShadersWorkspace workspace)
    {
        var items = new List<MaterialShaderKeyValueItem>
        {
            new() { Label = "Mode", Value = workspace.PrimaryKind == MaterialShaderAssetKind.Materialbin ? "Materialbin workstation" : "Shaderbin workstation" },
            new() { Label = "Primary Source", Value = workspace.PrimarySourcePath },
        };

        if (!string.IsNullOrWhiteSpace(workspace.SelectedGameId))
        {
            items.Add(new MaterialShaderKeyValueItem
            {
                Label = "Game Root",
                Value = $"{ForzaGameCatalog.GetDisplayName(workspace.SelectedGameId)}  |  {workspace.SelectedGameRootPath}",
            });
        }

        if (!string.IsNullOrWhiteSpace(workspace.LinkedShaderPathHint))
        {
            items.Add(new MaterialShaderKeyValueItem
            {
                Label = "Linked Shader",
                Value = workspace.LinkedShaderPathHint,
            });
        }

        if (!string.IsNullOrWhiteSpace(workspace.ResolvedLinkedShaderSource))
        {
            items.Add(new MaterialShaderKeyValueItem
            {
                Label = "Resolved Shader Source",
                Value = workspace.ResolvedLinkedShaderSource,
            });
        }

        return items;
    }

    private static List<MaterialShaderKeyValueItem> BuildMaterialSummary(MaterialDocumentSnapshot? material)
    {
        if (material == null)
        {
            return [new MaterialShaderKeyValueItem { Label = "Material", Value = "No materialbin loaded." }];
        }

        return
        [
            new MaterialShaderKeyValueItem { Label = "File", Value = material.DisplayName },
            new MaterialShaderKeyValueItem { Label = "Source", Value = material.SourceDisplayPath },
            new MaterialShaderKeyValueItem { Label = "Bundle", Value = material.BundleVersionText },
            new MaterialShaderKeyValueItem { Label = "Shader Path", Value = string.IsNullOrWhiteSpace(material.ShaderPath) ? "Not recorded" : material.ShaderPath },
            new MaterialShaderKeyValueItem { Label = "Overrides", Value = material.Parameters.Count.ToString() },
            new MaterialShaderKeyValueItem { Label = "ATST", Value = material.AtlasSummary },
        ];
    }

    private static List<MaterialShaderKeyValueItem> BuildShaderSummary(ShaderDocumentSnapshot? shader)
    {
        if (shader == null)
        {
            return [new MaterialShaderKeyValueItem { Label = "Shader", Value = "No shaderbin loaded." }];
        }

        return
        [
            new MaterialShaderKeyValueItem { Label = "File", Value = shader.DisplayName },
            new MaterialShaderKeyValueItem { Label = "Source", Value = shader.SourceDisplayPath },
            new MaterialShaderKeyValueItem { Label = "Bundle", Value = shader.BundleVersionText },
            new MaterialShaderKeyValueItem { Label = "Defaults", Value = shader.DefaultParameters.Count.ToString() },
            new MaterialShaderKeyValueItem { Label = "BLEN", Value = shader.BlendSummary },
            new MaterialShaderKeyValueItem { Label = "Blob Presence", Value = shader.PresenceSummary },
        ];
    }

    private static List<MaterialShaderKeyValueItem> BuildMaterialDefinitions(MaterialDocumentSnapshot? material)
    {
        if (material == null)
        {
            return [new MaterialShaderKeyValueItem { Label = "State", Value = "No materialbin loaded." }];
        }

        return
        [
            new MaterialShaderKeyValueItem { Label = "Primary Shader Path", Value = string.IsNullOrWhiteSpace(material.ShaderPath) ? "Not recorded" : material.ShaderPath },
            new MaterialShaderKeyValueItem { Label = "Alternate Path v1.1", Value = string.IsNullOrWhiteSpace(material.ShaderPathV1_1) ? "-" : material.ShaderPathV1_1 },
            new MaterialShaderKeyValueItem { Label = "Alternate Path v1.2", Value = string.IsNullOrWhiteSpace(material.ShaderPathV1_2) ? "-" : material.ShaderPathV1_2 },
            new MaterialShaderKeyValueItem { Label = "ATST Metadata", Value = material.AtlasSummary },
            new MaterialShaderKeyValueItem { Label = "Override Footer", Value = material.FooterSummary },
        ];
    }

    private static List<MaterialShaderParameterComparisonItem> BuildMaterialParameterComparisons(
        MaterialDocumentSnapshot? material,
        ShaderDocumentSnapshot? shader)
    {
        if (material == null || shader == null)
            return [];

        var defaultsByKey = shader.DefaultParameters
            .GroupBy(BuildParameterComparisonKey)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var overridesByKey = material.Parameters
            .GroupBy(BuildParameterComparisonKey)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return defaultsByKey.Keys
            .Union(overridesByKey.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                defaultsByKey.TryGetValue(key, out ShaderParameter? defaultParameter);
                overridesByKey.TryGetValue(key, out ShaderParameter? overrideParameter);

                ShaderParameter effectiveParameter = overrideParameter ?? defaultParameter!;
                bool isExplicitOverride = overrideParameter != null;

                return new MaterialShaderParameterComparisonItem
                {
                    Name = ResolveParameterName(effectiveParameter.NameHash),
                    SourceState = isExplicitOverride ? "Explicit Override" : "Inherited Default",
                    IsExplicitOverride = isExplicitOverride,
                    TypeText = effectiveParameter.Type.ToString(),
                    VersionText = $"v{effectiveParameter.VersionMajor}.{effectiveParameter.VersionMinor}",
                    NameHashText = $"0x{effectiveParameter.NameHash:X8}",
                    GuidText = effectiveParameter.Guid == Guid.Empty ? "-" : effectiveParameter.Guid.ToString(),
                    ExtraValueText = effectiveParameter.UnkV3_1 == 0 ? "-" : $"0x{effectiveParameter.UnkV3_1:X8}",
                    DefaultValueText = defaultParameter == null ? "No shader default recorded" : FormatShaderParameterValue(defaultParameter),
                    EffectiveValueText = FormatShaderParameterValue(effectiveParameter),
                };
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TypeText, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<MaterialShaderKeyValueItem> BuildShaderIdentity(ShaderDocumentSnapshot? shader)
    {
        if (shader == null)
        {
            return [new MaterialShaderKeyValueItem { Label = "State", Value = "No shaderbin loaded." }];
        }

        return
        [
            new MaterialShaderKeyValueItem { Label = "Source", Value = shader.SourceDisplayPath },
            new MaterialShaderKeyValueItem { Label = "Bundle", Value = shader.BundleVersionText },
            new MaterialShaderKeyValueItem { Label = "BLEN", Value = shader.BlendSummary },
            new MaterialShaderKeyValueItem { Label = "Presence", Value = shader.PresenceSummary },
        ];
    }

    private static string BuildParameterComparisonKey(ShaderParameter parameter)
    {
        return $"{parameter.NameHash:X8}|{(byte)parameter.Type:X2}";
    }

    private static string FormatShaderParameterValue(ShaderParameter parameter)
    {
        return parameter.Value switch
        {
            null => "-",
            bool boolValue => boolValue ? "True" : "False",
            float floatValue => floatValue.ToString("0.###"),
            int intValue => intValue.ToString(),
            Vector2 vector2Value => $"({vector2Value.X:0.###}, {vector2Value.Y:0.###})",
            Vector4 vector4Value => $"({vector4Value.X:0.###}, {vector4Value.Y:0.###}, {vector4Value.Z:0.###}, {vector4Value.W:0.###})",
            TextureParameter textureValue => string.IsNullOrWhiteSpace(textureValue.Path)
                ? "Texture path not set"
                : textureValue.Path,
            SamplerParameter samplerValue => $"AddressU={samplerValue.AddressU}, AddressV={samplerValue.AddressV}, Mode={samplerValue.UnkType}",
            ColorGradientParameter gradientValue => $"{gradientValue.Values.Count} gradient stop(s)",
            string stringValue => stringValue,
            _ => parameter.Value.ToString() ?? "-",
        };
    }

    private static string ResolveParameterName(uint nameHash)
    {
        return NameHashService.Instance.GetName(nameHash) ?? $"Unknown Hash (0x{nameHash:X8})";
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }
}