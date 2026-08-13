using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ForzaTechStudio.Views
{
    public sealed partial class ViewportPage : Page
    {
        private async Task<VehicleTexturePackageResult> ExportCompleteVehicleTexturePackageAsync(
            JsonObject document,
            string jsonFilePath,
            IReadOnlyCollection<string> sourceZipPaths)
        {
            string jsonDirectory = Path.GetDirectoryName(jsonFilePath) ?? Environment.CurrentDirectory;
            string packageFolderName = $"{Path.GetFileNameWithoutExtension(jsonFilePath)} Textures";
            string packageDirectory = Path.Combine(jsonDirectory, packageFolderName);
            string vehicleDirectory = Path.Combine(packageDirectory, "Vehicle");
            string libraryDirectory = Path.Combine(packageDirectory, "Library");
            Directory.CreateDirectory(vehicleDirectory);
            Directory.CreateDirectory(libraryDirectory);

            // Vehicle archives contain dashboard, AO, label and other car-specific swatches.
            await ExportZipTextures(sourceZipPaths, vehicleDirectory, ExportTextureFormat.Png);
            int excludedDamageTextureCount = RemoveExcludedDamageTextures(vehicleDirectory);
            int vehicleTextureCount = CountPngFiles(vehicleDirectory);

            var gameSource = await ResolveMaterialTextureGameSourceAsync();
            var sharedMaterialCache = new Dictionary<string, IReadOnlyList<VehicleTextureRequest>>(StringComparer.OrdinalIgnoreCase);
            var exportedTextureFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var workspaceService = new MaterialsAndShadersWorkspaceService();
            int unresolvedCount = 0;

            if (document["materials"] is JsonArray materials)
            {
                foreach (JsonNode? materialNode in materials)
                {
                    if (materialNode is not JsonObject material)
                        continue;

                    string materialPath = material["materialPath"]?.GetValue<string>() ?? string.Empty;
                    var effectiveTextures = new Dictionary<string, VehicleTextureRequest>(StringComparer.OrdinalIgnoreCase);

                    if (!string.IsNullOrWhiteSpace(materialPath) && gameSource.IsConfigured)
                    {
                        string materialCacheKey = NormalizeTexturePackagePath(materialPath);
                        if (!sharedMaterialCache.TryGetValue(materialCacheKey, out var inheritedTextures))
                        {
                            inheritedTextures = await Task.Run(() => LoadSharedMaterialTextureRequests(
                                workspaceService,
                                gameSource,
                                materialPath));
                            sharedMaterialCache[materialCacheKey] = inheritedTextures;
                        }

                        foreach (var texture in inheritedTextures)
                            effectiveTextures[texture.Slot] = texture;
                    }

                    // Instance parameters in the car override inherited material/shader defaults.
                    foreach (var texture in ReadInstanceTextureRequests(material)
                        .Where(texture => !IsExcludedDamageTexture(texture.Slot, texture.GamePath)))
                        effectiveTextures[texture.Slot] = texture;

                    var resolvedTextures = new JsonArray();
                    foreach (var request in effectiveTextures.Values
                        .OrderBy(request => request.Slot, StringComparer.OrdinalIgnoreCase))
                    {
                        var exported = await ExportVehicleTextureRequestAsync(
                            request,
                            gameSource,
                            libraryDirectory,
                            packageFolderName,
                            exportedTextureFiles);
                        resolvedTextures.Add(exported.Json);
                        if (!exported.Resolved)
                            unresolvedCount++;
                    }

                    material["resolvedTextures"] = resolvedTextures;
                    AddNeutralCarPaintFallback(material);
                }
            }

            int libraryTextureCount = exportedTextureFiles.Values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            excludedDamageTextureCount += RemoveExcludedDamageTextures(libraryDirectory);

            document["texturePackage"] = new JsonObject
            {
                ["folder"] = packageFolderName,
                ["game"] = gameSource.GameId,
                ["vehicleTextureCount"] = vehicleTextureCount,
                ["libraryTextureCount"] = libraryTextureCount,
                ["excludedDamageTextureCount"] = excludedDamageTextureCount,
                ["unresolvedReferenceCount"] = unresolvedCount,
                ["carPaintFallback"] = "Neutral 18% gray clear-coat material when no explicit paint texture/color is present."
            };

            return new VehicleTexturePackageResult(
                packageDirectory,
                vehicleTextureCount + libraryTextureCount,
                unresolvedCount);
        }

        private async Task<VehicleTextureGameSource> ResolveMaterialTextureGameSourceAsync()
        {
            if (_selectedTextureGameSource?.IsConfigured == true)
            {
                return new VehicleTextureGameSource(
                    _selectedTextureGameSource.GameId,
                    _selectedTextureGameSource.RootPath);
            }

            var settings = await new SettingsService().LoadAsync();
            if (!string.IsNullOrWhiteSpace(settings.DefaultGameId) &&
                settings.GamePaths.TryGetValue(settings.DefaultGameId, out string? defaultPath) &&
                !string.IsNullOrWhiteSpace(defaultPath) && Directory.Exists(defaultPath))
            {
                return new VehicleTextureGameSource(settings.DefaultGameId, defaultPath);
            }

            // Prefer FH6 for current assets, then accept any configured game installation.
            if (settings.GamePaths.TryGetValue("FH6", out string? fh6Path) &&
                !string.IsNullOrWhiteSpace(fh6Path) && Directory.Exists(fh6Path))
            {
                return new VehicleTextureGameSource("FH6", fh6Path);
            }

            foreach (var pair in settings.GamePaths)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value) && Directory.Exists(pair.Value))
                    return new VehicleTextureGameSource(pair.Key, pair.Value);
            }

            return new VehicleTextureGameSource(string.Empty, string.Empty);
        }

        private static IReadOnlyList<VehicleTextureRequest> LoadSharedMaterialTextureRequests(
            MaterialsAndShadersWorkspaceService workspaceService,
            VehicleTextureGameSource gameSource,
            string materialPath)
        {
            try
            {
                var workspace = workspaceService.LoadFromGamePath(
                    gameSource.GameId,
                    gameSource.RootPath,
                    materialPath);

                return workspace.TextureReferences
                    .Where(texture => HasTextureReference(texture.TexturePath, texture.PathHashText))
                    .Where(texture => !IsExcludedDamageTexture(texture.ParameterName, texture.TexturePath))
                    .Select(texture => new VehicleTextureRequest(
                        texture.ParameterName,
                        texture.TexturePath,
                        texture.PathHashText,
                        texture.IsExplicitOverride ? "sharedMaterialOverride" : "shaderDefault",
                        texture.PreviewSwatchInfo))
                    .ToList();
            }
            catch
            {
                return [];
            }
        }

        private static IEnumerable<VehicleTextureRequest> ReadInstanceTextureRequests(JsonObject material)
        {
            if (material["textures"] is not JsonArray textures)
                yield break;

            foreach (JsonNode? textureNode in textures)
            {
                if (textureNode is not JsonObject texture)
                    continue;

                string slot = texture["name"]?.GetValue<string>() ?? "Texture";
                string path = texture["value"]?["path"]?.GetValue<string>() ?? string.Empty;
                string pathHash = texture["value"]?["pathHash"]?.GetValue<string>() ?? string.Empty;
                if (!HasTextureReference(path, pathHash))
                    continue;

                yield return new VehicleTextureRequest(slot, path, pathHash, "vehicleOverride", null);
            }
        }

        private async Task<ExportedVehicleTexture> ExportVehicleTextureRequestAsync(
            VehicleTextureRequest request,
            VehicleTextureGameSource gameSource,
            string libraryDirectory,
            string packageFolderName,
            Dictionary<string, string> exportedTextureFiles)
        {
            string normalizedPath = NormalizeTexturePackagePath(request.GamePath);
            string identity = !string.IsNullOrWhiteSpace(normalizedPath)
                ? normalizedPath
                : request.PathHash;

            if (string.IsNullOrWhiteSpace(identity))
                return CreateUnresolvedTexture(request, "Texture has no path or hash.");

            if (exportedTextureFiles.TryGetValue(identity, out string? existingRelativePath))
                return CreateResolvedTexture(request, existingRelativePath);

            SwatchbinInfo? info = request.SwatchInfo;
            if (info == null && !string.IsNullOrWhiteSpace(request.GamePath))
                info = ResolveTextureInfo(request.GamePath, gameSource);

            if (info == null)
                return CreateUnresolvedTexture(request, "Referenced swatchbin could not be found in the vehicle or game library.");

            try
            {
                byte[]? png = await TextureImageExporter.ConvertAsync(info, ExportTextureFormat.Png);
                if (png == null || png.Length == 0)
                    return CreateUnresolvedTexture(request, "Texture was found but PNG conversion failed.");

                string fileName = BuildTexturePackageFileName(request, identity);
                string outputPath = Path.Combine(libraryDirectory, fileName);
                await File.WriteAllBytesAsync(outputPath, png);

                string relativePath = Path.Combine(packageFolderName, "Library", fileName).Replace('\\', '/');
                exportedTextureFiles[identity] = relativePath;
                return CreateResolvedTexture(request, relativePath);
            }
            catch (Exception ex)
            {
                return CreateUnresolvedTexture(request, ex.Message);
            }
        }

        private SwatchbinInfo? ResolveTextureInfo(string texturePath, VehicleTextureGameSource gameSource)
        {
            try
            {
                if (_viewportTextureLookupDirty)
                    RefreshViewportTextureLookupFromLoadedRoots();

                var localEntry = ResolveTextureEntryByPath(texturePath);
                if (localEntry != null)
                {
                    using var localStream = new MemoryStream(localEntry.SwatchbinData, writable: false);
                    return new SwatchbinService().LoadSwatchbin(localStream);
                }
            }
            catch
            {
            }

            if (!gameSource.IsConfigured)
                return null;

            try
            {
                foreach (string candidate in BuildViewportGameTexturePathCandidates(gameSource.GameId, texturePath))
                {
                    if (!SwatchbinArchiveService.TryLoadGameTextureEntry(
                        gameSource.RootPath,
                        candidate,
                        gameSource.GameId,
                        out var entry) || entry == null)
                        continue;

                    using var stream = new MemoryStream(entry.SwatchbinData, writable: false);
                    return new SwatchbinService().LoadSwatchbin(stream);
                }
            }
            catch
            {
            }

            return null;
        }

        private static ExportedVehicleTexture CreateResolvedTexture(
            VehicleTextureRequest request,
            string relativePath)
        {
            return new ExportedVehicleTexture(true, new JsonObject
            {
                ["slot"] = request.Slot,
                ["gamePath"] = request.GamePath,
                ["file"] = relativePath,
                ["source"] = request.Source,
                ["resolved"] = true
            });
        }

        private static ExportedVehicleTexture CreateUnresolvedTexture(
            VehicleTextureRequest request,
            string reason)
        {
            return new ExportedVehicleTexture(false, new JsonObject
            {
                ["slot"] = request.Slot,
                ["gamePath"] = request.GamePath,
                ["pathHash"] = request.PathHash,
                ["source"] = request.Source,
                ["resolved"] = false,
                ["reason"] = reason
            });
        }

        private static void AddNeutralCarPaintFallback(JsonObject material)
        {
            string name = material["name"]?.GetValue<string>() ?? string.Empty;
            string path = material["materialPath"]?.GetValue<string>() ?? string.Empty;
            bool isCarPaint = name.Contains("carpaint", StringComparison.OrdinalIgnoreCase) ||
                              path.Contains("carpaint", StringComparison.OrdinalIgnoreCase);
            if (!isCarPaint)
                return;

            bool hasExplicitColor = material["colors"] is JsonArray colors && colors.Count > 0;
            if (hasExplicitColor)
                return;

            material["neutralFallback"] = new JsonObject
            {
                ["baseColorLinear"] = new JsonArray(0.18, 0.18, 0.18, 1.0),
                ["metallic"] = 0.9,
                ["roughness"] = 0.25,
                ["clearCoat"] = 1.0
            };
        }

        private static string BuildTexturePackageFileName(VehicleTextureRequest request, string identity)
        {
            string baseName = Path.GetFileNameWithoutExtension(
                (request.GamePath ?? string.Empty).Replace('/', '\\'));
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = request.Slot;

            var safeName = new StringBuilder(baseName.Length);
            foreach (char character in baseName)
                safeName.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);

            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
                .Substring(0, 8)
                .ToLowerInvariant();
            return $"{safeName}_{hash}.png";
        }

        private static string NormalizeTexturePackagePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string normalized = path.Trim().Replace('/', '\\');
            if (normalized.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[6..];
            return normalized.TrimStart('\\').ToLowerInvariant();
        }

        private static int CountPngFiles(string directory)
        {
            if (!Directory.Exists(directory))
                return 0;

            return Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories).Count();
        }

        private static int RemoveExcludedDamageTextures(string directory)
        {
            if (!Directory.Exists(directory))
                return 0;

            int removed = 0;
            foreach (string filePath in Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories))
            {
                if (!IsExcludedDamageTexture(string.Empty, Path.GetFileNameWithoutExtension(filePath)))
                    continue;

                File.Delete(filePath);
                removed++;
            }

            return removed;
        }

        private static bool IsExcludedDamageTexture(string slot, string path)
        {
            string value = $"{slot} {path}";
            return value.Contains("damage", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("dmg_", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("impactmask", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("impact_mask", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasTextureReference(string path, string pathHash)
        {
            if (!string.IsNullOrWhiteSpace(path))
                return true;

            return !string.IsNullOrWhiteSpace(pathHash) &&
                   !string.Equals(pathHash, "0x00000000", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(pathHash, "0", StringComparison.OrdinalIgnoreCase);
        }

        private sealed record VehicleTextureRequest(
            string Slot,
            string GamePath,
            string PathHash,
            string Source,
            SwatchbinInfo? SwatchInfo);

        private sealed record VehicleTextureGameSource(string GameId, string RootPath)
        {
            public bool IsConfigured => !string.IsNullOrWhiteSpace(GameId) &&
                                        !string.IsNullOrWhiteSpace(RootPath) &&
                                        Directory.Exists(RootPath);
        }

        private sealed record ExportedVehicleTexture(bool Resolved, JsonObject Json);

        private sealed record VehicleTexturePackageResult(
            string PackageDirectory,
            int ExportedTextureCount,
            int UnresolvedTextureCount);
    }
}
