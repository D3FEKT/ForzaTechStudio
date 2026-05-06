using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.ViewModels
{
    public partial class SetupViewModel : ObservableObject
    {
        private readonly GameDetectionService _detectionService;
        private readonly SettingsService _settingsService;
        private readonly Dictionary<string, GameInstallation> _gamesById;
        private readonly IReadOnlyList<GameInstallation> _games;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private string _settingsFilePath = "";

        [ObservableProperty]
        private bool _isDbBuilding;

        [ObservableProperty]
        private string _dbBuildProgress = "";

        public IReadOnlyList<GameInstallation> Games => _games;

        public SetupViewModel()
        {
            try
            {
                _gamesById = ForzaGameCatalog.SetupGames.ToDictionary(
                    game => game.GameId,
                    CreateGameInstallation,
                    StringComparer.OrdinalIgnoreCase);
                _games = ForzaGameCatalog.SetupGames
                    .Select(game => _gamesById[game.GameId])
                    .ToList();

                _detectionService = new GameDetectionService();
                _settingsService = new SettingsService();
                _settingsFilePath = _settingsService.GetSettingsFilePath();

                // Migrate from old UWP settings if needed
                _ = MigrateFromUwpAsync();

                // Fields are already initialized above, just load saved paths
                _ = LoadSavedPathsAsync();

                // Refresh database status for all games
                RefreshAllDbStatus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetupViewModel constructor error: {ex.Message}");
                StatusMessage = $"Initialization error: {ex.Message}";
            }
        }

        private static GameInstallation CreateGameInstallation(ForzaGameDefinition game)
        {
            return new GameInstallation(game.GameId, game.DisplayName, game.SignatureFiles);
        }

        private async Task MigrateFromUwpAsync()
        {
            try
            {
                await _settingsService.MigrateFromUwpSettingsAsync(ApplicationData.Current.LocalSettings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Migration error: {ex.Message}");
            }
        }

        private async Task LoadSavedPathsAsync()
        {
            try
            {
                var config = await _settingsService.LoadAsync();

                foreach (var game in Games)
                {
                    LoadGamePath(game, config.GamePaths.GetValueOrDefault(game.GameId, string.Empty));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadSavedPaths error: {ex.Message}");
            }
        }

        private void LoadGamePath(GameInstallation game, string savedPath)
        {
            if (game == null) return;
            
            try
            {
                if (!string.IsNullOrEmpty(savedPath))
                {
                    game.Path = savedPath;
                    ValidateGamePath(game);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadGamePath error: {ex.Message}");
            }
        }

        private void RefreshAllDbStatus()
        {
            foreach (var game in Games)
            {
                RefreshDbStatus(game);
            }
        }

        private static void RefreshDbStatus(GameInstallation game)
        {
            if (game == null) return;
            var info = GameAssetDatabaseService.GetDatabaseInfo(game.GameId);
            if (info.HasValue)
            {
                game.DbEntryCount = info.Value.entryCount;
                game.DbLastBuilt = info.Value.lastBuilt;
                game.HasDatabase = true;
            }
            else
            {
                game.DbEntryCount = 0;
                game.DbLastBuilt = null;
                game.HasDatabase = false;
            }
        }

        [RelayCommand]
        private async Task AutoDetectAllAsync()
        {
            IsBusy = true;
            StatusMessage = "Detecting game installations...";

            try
            {
                var results = await _detectionService.AutoDetectAllGamesAsync();

                int foundCount = 0;
                foreach (var kvp in results)
                {
                    var game = GetGameById(kvp.Key);
                    if (game != null)
                    {
                        game.Path = kvp.Value;
                        ValidateGamePath(game);
                        await SaveGamePathAsync(game.GameId, game.Path);
                        foundCount++;
                    }
                }

                StatusMessage = $"Auto-detection complete. Found {foundCount} game(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during detection: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"AutoDetectAll error: {ex}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task BrowseGamePathAsync(GameInstallation game)
        {
            if (game == null) return;

            try
            {
                var picker = new FolderPicker();
                var window = App.MainWindow;
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

                picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                picker.FileTypeFilter.Add("*");

                var folder = await picker.PickSingleFolderAsync();
                if (folder != null)
                {
                    game.Path = folder.Path;
                    ValidateGamePath(game);
                    await SaveGamePathAsync(game.GameId, game.Path);
                    
                    if (game.IsValid)
                    {
                        StatusMessage = $" {game.DisplayName} configured successfully";
                    }
                    else
                    {
                        StatusMessage = $" {game.DisplayName} path set but validation failed";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error browsing: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"BrowseGamePath error: {ex}");
            }
        }

        [RelayCommand]
        private async Task AutoDetectSingleAsync(GameInstallation game)
        {
            if (game == null) return;

            IsBusy = true;
            StatusMessage = $"Detecting {game.DisplayName}...";

            try
            {
                // Detect ONLY this specific game by its GameId
                var path = await _detectionService.DetectGameAsync(game.GameId);
                
                if (path != null)
                {
                    // ONLY update THIS specific game instance
                    game.Path = path;
                    ValidateGamePath(game);
                    await SaveGamePathAsync(game.GameId, game.Path);
                    StatusMessage = $" Found {game.DisplayName}!";
                }
                else
                {
                    StatusMessage = $"Could not auto-detect {game.DisplayName}. Please browse manually.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"AutoDetectSingle error: {ex}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task BuildDatabaseAsync(GameInstallation game)
        {
            if (game == null || string.IsNullOrEmpty(game.Path)) return;

            IsDbBuilding = true;
            DbBuildProgress = $"Building database for {game.DisplayName}...";
            StatusMessage = DbBuildProgress;

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    DbBuildProgress = msg;
                    StatusMessage = msg;
                });

                var (indexed, dbPath) = await GameAssetDatabaseService.BuildDatabaseAsync(
                    game.GameId, game.Path, progress, CancellationToken.None);

                RefreshDbStatus(game);
                StatusMessage = $"? Database built for {game.DisplayName}: {indexed} entries";
                DbBuildProgress = "";
            }
            catch (Exception ex)
            {
                StatusMessage = $"? Database build failed: {ex.Message}";
                DbBuildProgress = "";
                System.Diagnostics.Debug.WriteLine($"BuildDatabase error: {ex}");
            }
            finally
            {
                IsDbBuilding = false;
            }
        }

        [RelayCommand]
        private async Task SavePathsAsync()
        {
            try
            {
                foreach (var game in Games)
                {
                    await SaveGamePathAsync(game.GameId, game.Path);
                }

                StatusMessage = "All paths saved successfully!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving paths: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"SavePaths error: {ex}");
            }
        }

        [RelayCommand]
        private async Task ResetAllAsync()
        {
            try
            {
                foreach (var game in Games)
                {
                    game.Path = string.Empty;
                    game.UpdateStatus(false, "Not configured");
                    await SaveGamePathAsync(game.GameId, string.Empty);
                }

                StatusMessage = "All paths reset.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error resetting: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"ResetAll error: {ex}");
            }
        }

        private async Task SaveGamePathAsync(string gameId, string path)
        {
            try
            {
                bool success = await _settingsService.SetGamePathAsync(gameId, path ?? "");
                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save path for {gameId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveGamePath error for {gameId}: {ex.Message}");
            }
        }

        private void ValidateGamePath(GameInstallation game)
        {
            if (game == null) return;

            try
            {
                var result = _detectionService.ValidateGamePathFlexible(game.Path, game.SignatureFiles);
                
                game.UpdateStatus(result.IsValid, result.Message, result.DetectedExecutable);

                System.Diagnostics.Debug.WriteLine($"Validated {game.GameId}: {result.Status} - {result.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ValidateGamePath error: {ex.Message}");
                game.UpdateStatus(false, $"Error: {ex.Message}");
            }
        }

        private GameInstallation? GetGameById(string gameId)
        {
            return _gamesById.TryGetValue(gameId ?? string.Empty, out var game)
                ? game
                : null;
        }
    }
}
