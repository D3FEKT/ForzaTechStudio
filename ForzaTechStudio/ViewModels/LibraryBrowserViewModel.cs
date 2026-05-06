using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTechStudio.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ForzaTechStudio.ViewModels;

public partial class LibraryBrowserViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly SettingsService _settingsService = new();
    private readonly MaterialsAndShadersWorkspaceService _workspaceService = new();
    private readonly Dictionary<string, string> _configuredGamePaths = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<MaterialShaderLibraryAsset> _allLoadedItems = [];
    private IReadOnlyList<MaterialShaderLibraryAsset> _filteredItems = [];

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;

    private bool _isBusy;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private string _selectedGameId = string.Empty;
    private string _selectedLibraryFilter = "Materialbins";
    private string _searchQuery = string.Empty;
    private MaterialShaderLibraryAsset? _selectedItem;
    private string _statusMessage = string.Empty;

    public ObservableCollection<ForzaGameDefinition> AvailableGames { get; } = [];
    public ObservableCollection<string> LibraryFilters { get; } = ["Materialbins", "Shaderbins"];
    public ObservableCollection<MaterialShaderLibraryAsset> PagedItems { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(PageSummaryText));
                GoToNextPageCommand.NotifyCanExecuteChanged();
                GoToPreviousPageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, value))
            {
                OnPropertyChanged(nameof(PageSummaryText));
                GoToNextPageCommand.NotifyCanExecuteChanged();
                GoToPreviousPageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string PageSummaryText =>
        _filteredItems.Count == 0
            ? (IsBusy ? "Loading..." : "No results")
            : $"Page {CurrentPage} of {TotalPages}  ·  {_filteredItems.Count:N0} results";

    public string SelectedGameId
    {
        get => _selectedGameId;
        set
        {
            if (SetProperty(ref _selectedGameId, value))
                _ = LoadAsync();
        }
    }

    public string SelectedLibraryFilter
    {
        get => _selectedLibraryFilter;
        set
        {
            if (SetProperty(ref _selectedLibraryFilter, value))
                _ = LoadAsync();
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                _ = DebounceSearchAsync();
        }
    }

    public MaterialShaderLibraryAsset? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
                OnPropertyChanged(nameof(HasSelectedItem));
        }
    }

    public bool HasSelectedItem => SelectedItem != null;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task InitializeAsync()
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
        {
            // Trigger load via setter (which calls LoadAsync)
            SelectedGameId = AvailableGames[0].GameId;
        }
        else
        {
            StatusMessage = "No configured game roots found. Configure paths on the Setup page.";
        }
    }

    public async Task LoadAsync()
    {
        // Cancel any previous load
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        if (string.IsNullOrWhiteSpace(SelectedGameId) || !_configuredGamePaths.ContainsKey(SelectedGameId))
        {
            _allLoadedItems = [];
            _filteredItems = [];
            ApplyPage(1);
            StatusMessage = "Select a configured game to browse.";
            if (ReferenceEquals(_loadCts, cts)) _loadCts = null;
            cts.Dispose();
            return;
        }

        string capturedGameId = SelectedGameId;
        string capturedFilter = SelectedLibraryFilter;
        string gameRoot = _configuredGamePaths[capturedGameId];

        try
        {
            IsBusy = true;
            StatusMessage = "Loading...";
            PagedItems.Clear();
            _allLoadedItems = [];
            _filteredItems = [];

            var assets = await Task.Run(
                () => _workspaceService.GetLibraryAssets(capturedGameId, gameRoot, capturedFilter),
                cts.Token);

            if (cts.Token.IsCancellationRequested) return;

            _allLoadedItems = assets;
            ApplySearchFilter(string.Empty);
            OnPropertyChanged(nameof(PageSummaryText));
            StatusMessage = $"{_filteredItems.Count:N0} entries loaded for {ForzaGameCatalog.GetDisplayName(capturedGameId)}.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.Token.IsCancellationRequested)
            {
                _allLoadedItems = [];
                _filteredItems = [];
                ApplyPage(1);
                StatusMessage = $"Failed to load: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
                IsBusy = false;
            }
            cts.Dispose();
        }
    }

    private async Task DebounceSearchAsync()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _searchCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            await Task.Delay(300, cts.Token);

            if (cts.Token.IsCancellationRequested) return;

            string query = SearchQuery;
            var filtered = await Task.Run(
                () => (IReadOnlyList<MaterialShaderLibraryAsset>)FilterItems(_allLoadedItems, query).ToList(),
                cts.Token);

            if (cts.Token.IsCancellationRequested) return;

            _filteredItems = filtered;
            ApplyPage(1);
            OnPropertyChanged(nameof(PageSummaryText));
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
                _searchCts = null;
            cts.Dispose();
        }
    }

    private void ApplySearchFilter(string query)
    {
        _filteredItems = (IReadOnlyList<MaterialShaderLibraryAsset>)FilterItems(_allLoadedItems, query).ToList();
        ApplyPage(1);
    }

    private static IEnumerable<MaterialShaderLibraryAsset> FilterItems(IReadOnlyList<MaterialShaderLibraryAsset> source, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return source;

        string[] tokens = query.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return source;

        return source.Where(item =>
        {
            string haystack = string.Join('\n', item.DisplayName, item.GamePath, item.ArchiveSource);
            return tokens.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
        });
    }

    private void ApplyPage(int page)
    {
        int total = _filteredItems.Count;
        int pages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)PageSize);
        page = Math.Clamp(page, 1, pages);

        TotalPages = pages;
        CurrentPage = page;

        var slice = _filteredItems.Skip((page - 1) * PageSize).Take(PageSize);

        PagedItems.Clear();
        foreach (var item in slice)
            PagedItems.Add(item);

        // If previously selected item is no longer in the filtered set, clear it
        if (SelectedItem != null && !_filteredItems.Any(i =>
            string.Equals(i.GamePath, SelectedItem.GamePath, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedItem = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void GoToNextPage()
    {
        ApplyPage(CurrentPage + 1);
        OnPropertyChanged(nameof(PageSummaryText));
    }

    private bool CanGoToNextPage() => CurrentPage < TotalPages;

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void GoToPreviousPage()
    {
        ApplyPage(CurrentPage - 1);
        OnPropertyChanged(nameof(PageSummaryText));
    }

    private bool CanGoToPreviousPage() => CurrentPage > 1;
}
