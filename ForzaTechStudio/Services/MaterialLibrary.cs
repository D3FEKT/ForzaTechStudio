using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ForzaTechStudio.Services
{
    public static class MaterialLibrary
    {
        public static string MaterialsDirectoryPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Materials");

        public static string LegacyLibraryPath => Path.Combine(MaterialsDirectoryPath, "materials.json");

        public static string GetLibraryPath(string gameId)
        {
            return string.IsNullOrWhiteSpace(gameId)
                ? LegacyLibraryPath
                : Path.Combine(MaterialsDirectoryPath, ForzaGameCatalog.GetMaterialLibraryFileName(gameId));
        }

        public static Dictionary<string, MaterialEntry> LoadEntries(string gameId, out string resolvedPath, bool includeLegacyFallback = true)
        {
            Directory.CreateDirectory(MaterialsDirectoryPath);

            try
            {
                foreach (string path in EnumerateCandidatePaths(gameId, includeLegacyFallback))
                {
                    if (!File.Exists(path))
                        continue;

                    string json = File.ReadAllText(path);
                    var entries = JsonSerializer.Deserialize(json, MaterialJsonContext.Default.DictionaryStringMaterialEntry);
                    resolvedPath = path;
                    return NormalizeEntries(entries);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MaterialLibrary Load Error: {ex.Message}");
            }

            resolvedPath = GetLibraryPath(gameId);
            return new Dictionary<string, MaterialEntry>(StringComparer.OrdinalIgnoreCase);
        }

        public static void SaveEntries(IDictionary<string, MaterialEntry> entries, string gameId)
        {
            Directory.CreateDirectory(MaterialsDirectoryPath);

            string json = JsonSerializer.Serialize(
                entries ?? new Dictionary<string, MaterialEntry>(StringComparer.OrdinalIgnoreCase),
                typeof(Dictionary<string, MaterialEntry>),
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    TypeInfoResolver = MaterialJsonContext.Default
                });

            File.WriteAllText(GetLibraryPath(gameId), json);
        }

        public static List<string> GetMaterialNames(string? gameId = null)
        {
            var entries = LoadEntries(gameId, out _, includeLegacyFallback: true);
            return entries.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static byte[] GetMaterialData(string name, string? gameId = null)
        {
            var entries = LoadEntries(gameId, out _, includeLegacyFallback: true);

            if (entries.TryGetValue(name, out var entry) && !string.IsNullOrEmpty(entry.MaterialBlob))
            {
                return HexToBytes(entry.MaterialBlob);
            }

            throw new Exception($"Material data for '{name}' could not be found or is empty.");
        }

        private static byte[] HexToBytes(string hex)
        {
            // Remove any spaces or dashes just in case
            hex = hex.Replace(" ", "").Replace("-", "");
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        private static IEnumerable<string> EnumerateCandidatePaths(string gameId, bool includeLegacyFallback)
        {
            string specificPath = GetLibraryPath(gameId);
            yield return specificPath;

            if (includeLegacyFallback && !string.Equals(specificPath, LegacyLibraryPath, StringComparison.OrdinalIgnoreCase))
                yield return LegacyLibraryPath;
        }

        private static Dictionary<string, MaterialEntry> NormalizeEntries(Dictionary<string, MaterialEntry> entries)
        {
            var normalizedEntries = new Dictionary<string, MaterialEntry>(StringComparer.OrdinalIgnoreCase);
            if (entries == null)
                return normalizedEntries;

            foreach (var entry in entries)
            {
                normalizedEntries[entry.Key] = entry.Value;
            }

            return normalizedEntries;
        }
    }
}