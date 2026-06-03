using ForzaTechStudio.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ForzaTechStudio.Services
{
    public class SettingsService
    {
        private const string SETTINGS_FILENAME = "settings.config";
        private const string APP_FOLDER = "ForzaTechStudio";
        
        private readonly string _settingsPath;
        private readonly string _backupPath;
        private SettingsConfig _cachedSettings;

        public SettingsService()
        {
            // Get LocalAppData folder path
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(localAppData, APP_FOLDER);

            // Create directory if it doesn't exist
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }

            _settingsPath = Path.Combine(appFolder, SETTINGS_FILENAME);
            _backupPath = Path.Combine(appFolder, SETTINGS_FILENAME + ".bak");

            System.Diagnostics.Debug.WriteLine($"Settings path: {_settingsPath}");
        }

        public async Task<SettingsConfig> LoadAsync()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = await File.ReadAllTextAsync(_settingsPath);
                    var settings = JsonSerializer.Deserialize<SettingsConfig>(json);
                    
                    if (settings != null && settings.IsValid())
                    {
                        EnsureGamePathDefaults(settings);
                        _cachedSettings = settings;
                        System.Diagnostics.Debug.WriteLine($"Settings loaded successfully from {_settingsPath}");
                        return settings;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Invalid settings file, loading defaults");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Settings file not found, creating new");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
                
                // Try to load from backup
                if (File.Exists(_backupPath))
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(_backupPath);
                        var settings = JsonSerializer.Deserialize<SettingsConfig>(json);
                        if (settings != null && settings.IsValid())
                        {
                            System.Diagnostics.Debug.WriteLine("Loaded settings from backup");
                            _cachedSettings = settings;
                            return settings;
                        }
                    }
                    catch
                    {
                        // Backup also failed, will return new config
                    }
                }
            }

            // Return new default config
            _cachedSettings = new SettingsConfig();
            EnsureGamePathDefaults(_cachedSettings);
            return _cachedSettings;
        }

        public async Task<bool> SaveAsync(SettingsConfig settings)
        {
            try
            {
                if (settings == null)
                {
                    System.Diagnostics.Debug.WriteLine("Cannot save null settings");
                    return false;
                }

                settings.UpdateTimestamp();
                EnsureGamePathDefaults(settings);

                // Serialize to JSON with formatting
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string json = JsonSerializer.Serialize(settings, options);

                // Create backup of existing file
                if (File.Exists(_settingsPath))
                {
                    try
                    {
                        File.Copy(_settingsPath, _backupPath, true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning: Could not create backup: {ex.Message}");
                    }
                }

                // Write to temp file first (atomic write)
                string tempPath = _settingsPath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);

                // Move temp file to actual location (atomic operation)
                File.Move(tempPath, _settingsPath, true);

                _cachedSettings = settings;
                System.Diagnostics.Debug.WriteLine($"Settings saved successfully to {_settingsPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public string GetGamePath(string gameId)
        {
            if (_cachedSettings != null && _cachedSettings.GamePaths.TryGetValue(gameId, out string path))
            {
                return path;
            }
            return string.Empty;
        }

        public async Task<bool> SetGamePathAsync(string gameId, string path)
        {
            if (_cachedSettings == null)
            {
                _cachedSettings = await LoadAsync();
            }

            _cachedSettings.GamePaths[gameId] = path ?? string.Empty;
            return await SaveAsync(_cachedSettings);
        }

        public async Task<bool> SetDefaultGameIdAsync(string gameId)
        {
            if (_cachedSettings == null)
            {
                _cachedSettings = await LoadAsync();
            }

            _cachedSettings.DefaultGameId = gameId ?? string.Empty;
            return await SaveAsync(_cachedSettings);
        }

        public async Task MigrateFromUwpSettingsAsync(Windows.Storage.ApplicationDataContainer uwpSettings)
        {
            try
            {
                if (_cachedSettings == null)
                {
                    _cachedSettings = await LoadAsync();
                }

                // Check if we already have settings (don't overwrite)
                bool hasExistingPaths = false;
                foreach (var path in _cachedSettings.GamePaths.Values)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        hasExistingPaths = true;
                        break;
                    }
                }

                if (hasExistingPaths)
                {
                    System.Diagnostics.Debug.WriteLine("Settings already exist, skipping migration");
                    return;
                }

                // Migrate from UWP settings
                var gameKeys = ForzaGameCatalog.AllGames
                    .Select(game => ($"GamePath_{game.GameId}", game.GameId));

                bool migrated = false;
                foreach (var (uwpKey, gameId) in gameKeys)
                {
                    if (uwpSettings.Values.TryGetValue(uwpKey, out object value) && value is string path && !string.IsNullOrEmpty(path))
                    {
                        _cachedSettings.GamePaths[gameId] = path;
                        migrated = true;
                        System.Diagnostics.Debug.WriteLine($"Migrated {gameId}: {path}");
                    }
                }

                if (migrated)
                {
                    await SaveAsync(_cachedSettings);
                    System.Diagnostics.Debug.WriteLine("Migration from UWP settings completed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during migration: {ex.Message}");
            }
        }

        private static void EnsureGamePathDefaults(SettingsConfig settings)
        {
            foreach (var game in ForzaGameCatalog.AllGames)
            {
                settings.GamePaths.TryAdd(game.GameId, string.Empty);
            }
        }

        public string GetSettingsFilePath() => _settingsPath;
    }
}
