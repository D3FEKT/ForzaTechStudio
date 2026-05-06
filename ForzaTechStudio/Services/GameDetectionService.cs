using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ForzaTechStudio.Services
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string? DetectedExecutable { get; set; }
        public string Message { get; set; } = "";
        public ValidationStatus Status { get; set; }
    }

    public enum ValidationStatus
    {
        Valid,
        Invalid,
        Error,
        NotConfigured
    }

    public class GameDetectionService
    {
        private readonly Dictionary<string, ForzaGameDefinition> _gameConfigs =
            ForzaGameCatalog.AllGames.ToDictionary(game => game.GameId, StringComparer.OrdinalIgnoreCase);

        public async Task<string?> DetectGameAsync(string gameId)
        {
            return await Task.Run(() =>
            {
                if (!_gameConfigs.TryGetValue(gameId, out var config))
                    return null;

                if (!config.SupportsAutoDetect)
                {
                    System.Diagnostics.Debug.WriteLine($"Auto-detect not supported for {config.DisplayName}");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"Detecting {config.DisplayName}...");

                // 1. Try registry detection
                var registryPath = TryRegistryDetection(config);
                if (registryPath != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Found via registry: {registryPath}");
                    return registryPath;
                }

                // 2. Try Steam detection (including all libraries)
                if (!string.IsNullOrEmpty(config.SteamAppId))
                {
                    var steamPath = TrySteamDetection(config);
                    if (steamPath != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Found via Steam: {steamPath}");
                        return steamPath;
                    }
                }

                // 3. Try common paths
                foreach (var commonPath in config.CommonPaths)
                {
                    if (ValidateGamePath(commonPath, config.SignatureFiles))
                    {
                        System.Diagnostics.Debug.WriteLine($"Found at common path: {commonPath}");
                        return commonPath;
                    }
                }

                // 4. Deep search in common game directories on all drives
                var deepSearchPath = TryDeepSearch(config);
                if (deepSearchPath != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Found via deep search: {deepSearchPath}");
                    return deepSearchPath;
                }

                System.Diagnostics.Debug.WriteLine($"Could not find {config.DisplayName}");
                return null;
            });
        }

        public async Task<Dictionary<string, string>> AutoDetectAllGamesAsync()
        {
            var results = new Dictionary<string, string>();

            foreach (var gameId in _gameConfigs.Keys)
            {
                var path = await DetectGameAsync(gameId);
                if (path != null)
                    results[gameId] = path;
            }

            return results;
        }

        public ValidationResult ValidateGamePathFlexible(string path, string[] signatureFiles)
        {
            if (string.IsNullOrEmpty(path))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Message = "Not configured",
                    Status = ValidationStatus.NotConfigured
                };
            }

            if (!Directory.Exists(path))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Message = "Path does not exist",
                    Status = ValidationStatus.Invalid
                };
            }

            try
            {
                // Separate exe files from other signatures
                var exeFiles = signatureFiles.Where(s => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
                var otherSignatures = signatureFiles.Where(s => !s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();

                // Check if at least one exe exists - MUST match the signature list exactly
                string? foundExe = null;
                if (exeFiles.Count > 0)
                {
                    foreach (var exe in exeFiles)
                    {
                        var fullPath = Path.Combine(path, exe);
                        if (File.Exists(fullPath))
                        {
                            foundExe = exe;
                            break;
                        }
                    }

                    if (foundExe == null)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            Message = $"Invalid folder - none of the expected executables found ({string.Join(", ", exeFiles)})",
                            Status = ValidationStatus.Invalid
                        };
                    }
                }

                // Check if all other signatures exist (e.g., "Media" folder)
                foreach (var signature in otherSignatures)
                {
                    var fullPath = Path.Combine(path, signature);
                    if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            Message = $"Missing required files: {signature}",
                            Status = ValidationStatus.Invalid
                        };
                    }
                }

                // All checks passed
                return new ValidationResult
                {
                    IsValid = true,
                    DetectedExecutable = foundExe,
                    Message = string.IsNullOrEmpty(foundExe) ? "Game folder signatures found" : $"Game found: {foundExe}",
                    Status = ValidationStatus.Valid
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Message = "Access denied",
                    Status = ValidationStatus.Error
                };
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Message = $"Path validation failed: {ex.Message}",
                    Status = ValidationStatus.Error
                };
            }
        }

        // Keep existing ValidateGamePath for backward compatibility
        public bool ValidateGamePath(string path, string[] signatureFiles)
        {
            return ValidateGamePathFlexible(path, signatureFiles).IsValid;
        }

        private string? TryRegistryDetection(ForzaGameDefinition config)
        {
            foreach (var regPath in config.RegistryPaths)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(regPath);
                    if (key != null)
                    {
                        var installLocation = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(installLocation) && ValidateGamePath(installLocation, config.SignatureFiles))
                            return installLocation;
                    }
                }
                catch { }

                // Also try HKCU
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(regPath);
                    if (key != null)
                    {
                        var installLocation = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(installLocation) && ValidateGamePath(installLocation, config.SignatureFiles))
                            return installLocation;
                    }
                }
                catch { }
            }

            return null;
        }

        private string? TrySteamDetection(ForzaGameDefinition config)
        {
            try
            {
                // Find Steam installation path
                using var steamKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (steamKey == null) return null;

                var steamPath = steamKey.GetValue("SteamPath") as string;
                if (string.IsNullOrEmpty(steamPath)) return null;

                // Get all Steam library paths
                var libraryPaths = GetSteamLibraryPaths(steamPath);

                // Search in all libraries
                foreach (var libraryPath in libraryPaths)
                {
                    var commonPath = Path.Combine(libraryPath, "steamapps", "common");
                    if (!Directory.Exists(commonPath)) continue;

                    // Try exact folder names first
                    if (config.SteamFolderNames != null)
                    {
                        foreach (var folderName in config.SteamFolderNames)
                        {
                            var gamePath = Path.Combine(commonPath, folderName);
                            if (ValidateGamePath(gamePath, config.SignatureFiles))
                                return gamePath;
                        }
                    }

                    // Fallback: search subdirectories
                    foreach (var dir in Directory.GetDirectories(commonPath))
                    {
                        if (Path.GetFileName(dir).Contains("Forza", StringComparison.OrdinalIgnoreCase))
                        {
                            if (ValidateGamePath(dir, config.SignatureFiles))
                                return dir;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Steam detection error: {ex.Message}");
            }

            return null;
        }

        private List<string> GetSteamLibraryPaths(string steamPath)
        {
            var libraries = new List<string> { steamPath };

            try
            {
                var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryFoldersPath)) return libraries;

                var lines = File.ReadAllLines(libraryFoldersPath);
                foreach (var line in lines)
                {
                    // Parse VDF format: "path"		"D:\\SteamLibrary"
                    if (line.Contains("\"path\"", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            var libraryPath = parts[2].Replace("\\\\", "\\")
                                                      .Trim();

                            if (Directory.Exists(libraryPath) && !libraries.Contains(libraryPath))
                                libraries.Add(libraryPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing Steam library folders: {ex.Message}");
            }

            return libraries;
        }

        private string? TryDeepSearch(ForzaGameDefinition config)
        {
            var searchPaths = new List<string>();

            // Get all fixed drives
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                    .Select(d => d.RootDirectory.FullName)
                    .ToList();

                // Common game folder locations
                var commonFolders = new[] { "Games", "XboxGames", "Program Files", "Program Files (x86)" };

                foreach (var drive in drives)
                {
                    foreach (var folder in commonFolders)
                    {
                        var path = Path.Combine(drive, folder);
                        if (Directory.Exists(path))
                            searchPaths.Add(path);
                    }
                }

                // Search in these locations (limit depth to avoid long scans)
                foreach (var searchPath in searchPaths)
                {
                    try
                    {
                        // Only search 2 levels deep
                        var result = SearchDirectory(searchPath, config, maxDepth: 2, currentDepth: 0);
                        if (result != null)
                            return result;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Deep search error: {ex.Message}");
            }

            return null;
        }

        private string? SearchDirectory(string directory, ForzaGameDefinition config, int maxDepth, int currentDepth)
        {
            if (currentDepth > maxDepth) return null;

            try
            {
                // Check current directory
                if (ValidateGamePath(directory, config.SignatureFiles))
                    return directory;

                // Search subdirectories if we haven't reached max depth
                if (currentDepth < maxDepth)
                {
                    foreach (var subDir in Directory.GetDirectories(directory))
                    {
                        var result = SearchDirectory(subDir, config, maxDepth, currentDepth + 1);
                        if (result != null)
                            return result;
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
