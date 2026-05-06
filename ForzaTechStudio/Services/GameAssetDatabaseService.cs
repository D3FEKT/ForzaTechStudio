using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace ForzaTechStudio.Services;

// Builds and queries a per-game SQLite database that indexes materialbin, shaderbin,
// and swatchbin file paths found in game archives.
public class GameAssetDatabaseService
{
    private const string DB_FOLDER = "ForzaTechStudio";
    private const int CurrentAssetIndexVersion = 2;
    private static readonly string DbBasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DB_FOLDER);

    // Returns the database file path for a given game ID (e.g. "FH5").
    public static string GetDatabasePath(string gameId)
    {
        return Path.Combine(DbBasePath, $"{gameId}_assets.db");
    }

    // Returns true if a database exists for the given game ID.
    public static bool DatabaseExists(string gameId)
    {
        return File.Exists(GetDatabasePath(gameId));
    }

    // Returns basic info about an existing database (entry count, last built).
    public static (int entryCount, DateTime lastBuilt)? GetDatabaseInfo(string gameId)
    {
        string dbPath = GetDatabasePath(gameId);
        if (!File.Exists(dbPath)) return null;

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM assets";
            int count = Convert.ToInt32(countCmd.ExecuteScalar());

            using var metaCmd = conn.CreateCommand();
            metaCmd.CommandText = "SELECT value FROM metadata WHERE key = 'built_utc'";
            var builtStr = metaCmd.ExecuteScalar() as string;
            DateTime built = DateTime.TryParse(builtStr, out var dt) ? dt : File.GetLastWriteTimeUtc(dbPath);

            return (count, built);
        }
        catch
        {
            return null;
        }
    }

    // Builds a SQLite database for the given game by scanning relevant archives for
    // .materialbin, .shaderbin, and .swatchbin files and storing their Game:\ paths.
    public static async Task<(int indexed, string dbPath)> BuildDatabaseAsync(
        string gameId, string gameRootPath,
        IProgress<string> progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(gameRootPath) || !Directory.Exists(gameRootPath))
            throw new DirectoryNotFoundException($"Game path not found: {gameRootPath}");

        string mediaPath = Path.Combine(gameRootPath, "media");
        if (!Directory.Exists(mediaPath))
            mediaPath = gameRootPath;

        Directory.CreateDirectory(DbBasePath);
        string dbPath = GetDatabasePath(gameId);

        // Delete old database
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        int totalIndexed = 0;

        await Task.Run(() =>
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            // Create schema
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    CREATE TABLE assets (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        file_name TEXT NOT NULL COLLATE NOCASE,
                        game_path TEXT NOT NULL,
                        zip_source TEXT NOT NULL,
                        extension TEXT NOT NULL COLLATE NOCASE
                    );
                    CREATE INDEX idx_assets_filename ON assets(file_name);
                    CREATE INDEX idx_assets_extension ON assets(extension);

                    CREATE TABLE metadata (
                        key TEXT PRIMARY KEY,
                        value TEXT
                    );
                """;
                cmd.ExecuteNonQuery();
            }

            // Find all relevant zips
            var zipPaths = FindRelevantZips(mediaPath);
            progress?.Report($"Found {zipPaths.Count} zip archive(s) to scan");

            using var transaction = conn.BeginTransaction();
            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = "INSERT INTO assets (file_name, game_path, zip_source, extension) VALUES ($name, $path, $zip, $ext)";
            var pName = insertCmd.Parameters.Add("$name", SqliteType.Text);
            var pPath = insertCmd.Parameters.Add("$path", SqliteType.Text);
            var pZip = insertCmd.Parameters.Add("$zip", SqliteType.Text);
            var pExt = insertCmd.Parameters.Add("$ext", SqliteType.Text);

            foreach (var zipPath in zipPaths)
            {
                ct.ThrowIfCancellationRequested();

                string zipRelativeToGame = Path.GetRelativePath(gameRootPath, zipPath);
                string zipFolder = Path.Combine(
                    Path.GetDirectoryName(zipRelativeToGame) ?? "",
                    Path.GetFileNameWithoutExtension(zipRelativeToGame));

                progress?.Report($"Scanning: {zipRelativeToGame}");
                int zipCount = 0;

                // Try standard .NET ZipArchive first
                bool usedStandard = false;
                try
                {
                    using var archive = ZipFile.OpenRead(zipPath);
                    foreach (var entry in archive.Entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                        if (ext is not ".materialbin" and not ".shaderbin" and not ".swatchbin") continue;

                        string entryPath = entry.FullName.Replace('/', '\\');
                        string fullRelativePath = Path.Combine(zipFolder, entryPath);
                        string gamePath = $"Game:\\{fullRelativePath}";

                        pName.Value = entry.Name;
                        pPath.Value = gamePath;
                        pZip.Value = zipRelativeToGame;
                        pExt.Value = ext;
                        insertCmd.ExecuteNonQuery();
                        zipCount++;
                    }
                    usedStandard = true;
                }
                catch { }

                if (!usedStandard)
                {
                    // Fallback: CustomZipFile for Forza-style (LZX) zips
                    try
                    {
                        using var customZip = new CustomZipFile(zipPath);
                        var entries = customZip.GetEntries();
                        foreach (var entry in entries)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (entry.IsDirectory) continue;
                            string fileName = Path.GetFileName(entry.Name);
                            string ext = Path.GetExtension(fileName).ToLowerInvariant();
                            if (ext is not ".materialbin" and not ".shaderbin" and not ".swatchbin") continue;

                            string entryPath = entry.Name.Replace('/', '\\');
                            string fullRelativePath = Path.Combine(zipFolder, entryPath);
                            string gamePath = $"Game:\\{fullRelativePath}";

                            pName.Value = fileName;
                            pPath.Value = gamePath;
                            pZip.Value = zipRelativeToGame;
                            pExt.Value = ext;
                            insertCmd.ExecuteNonQuery();
                            zipCount++;
                        }
                    }
                    catch { }
                }

                totalIndexed += zipCount;
                progress?.Report($"  Indexed {zipCount} file(s) from {Path.GetFileName(zipPath)}");
            }

            // Store metadata
            using var metaCmd = conn.CreateCommand();
            metaCmd.CommandText = "INSERT INTO metadata (key, value) VALUES ('built_utc', $v)";
            metaCmd.Parameters.AddWithValue("$v", DateTime.UtcNow.ToString("O"));
            metaCmd.ExecuteNonQuery();

            using var metaCmd2 = conn.CreateCommand();
            metaCmd2.CommandText = "INSERT INTO metadata (key, value) VALUES ('game_id', $v)";
            metaCmd2.Parameters.AddWithValue("$v", gameId);
            metaCmd2.ExecuteNonQuery();

            using var metaCmd3 = conn.CreateCommand();
            metaCmd3.CommandText = "INSERT INTO metadata (key, value) VALUES ('total_entries', $v)";
            metaCmd3.Parameters.AddWithValue("$v", totalIndexed.ToString());
            metaCmd3.ExecuteNonQuery();

            using var metaCmd4 = conn.CreateCommand();
            metaCmd4.CommandText = "INSERT INTO metadata (key, value) VALUES ('asset_index_version', $v)";
            metaCmd4.Parameters.AddWithValue("$v", CurrentAssetIndexVersion.ToString());
            metaCmd4.ExecuteNonQuery();

            transaction.Commit();
        }, ct);

        progress?.Report($"Database built: {totalIndexed} entries in {Path.GetFileName(dbPath)}");
        return (totalIndexed, dbPath);
    }

    // Looks up a filename in the database and returns the Game:\ path if found.
    public static string LookupFile(string gameId, string fileName)
    {
        string dbPath = GetDatabasePath(gameId);
        if (!File.Exists(dbPath)) return null;

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT game_path FROM assets WHERE file_name = $name LIMIT 1";
            cmd.Parameters.AddWithValue("$name", fileName);
            return cmd.ExecuteScalar() as string;
        }
        catch
        {
            return null;
        }
    }

    // Checks if a specific Game:\ path exists. Case-insensitive. Returns the stored path or null.
    public static string LookupByPath(string gameId, string gamePath)
    {
        string dbPath = GetDatabasePath(gameId);
        if (!File.Exists(dbPath)) return null;

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT game_path FROM assets WHERE game_path = $path COLLATE NOCASE LIMIT 1";
            cmd.Parameters.AddWithValue("$path", gamePath);
            return cmd.ExecuteScalar() as string;
        }
        catch
        {
            return null;
        }
    }

    // Checks multiple Game:\ paths at once. Returns a dictionary of found paths (original → stored).
    public static Dictionary<string, string> LookupByPaths(string gameId, IEnumerable<string> gamePaths)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string dbPath = GetDatabasePath(gameId);
        if (!File.Exists(dbPath)) return results;

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT game_path FROM assets WHERE game_path = $path COLLATE NOCASE LIMIT 1";
            var param = cmd.Parameters.Add("$path", SqliteType.Text);

            foreach (var gamePath in gamePaths)
            {
                param.Value = gamePath;
                var result = cmd.ExecuteScalar() as string;
                if (result != null)
                    results[gamePath] = result;
            }
        }
        catch { }

        return results;
    }

    // Batch filename lookup. Faster than individual calls for large sets.
    public static Dictionary<string, string> LookupFiles(string gameId, IEnumerable<string> fileNames)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string dbPath = GetDatabasePath(gameId);
        if (!File.Exists(dbPath)) return results;

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT game_path FROM assets WHERE file_name = $name LIMIT 1";
            var param = cmd.Parameters.Add("$name", SqliteType.Text);

            foreach (var fileName in fileNames)
            {
                param.Value = fileName;
                var result = cmd.ExecuteScalar() as string;
                if (result != null)
                    results[fileName] = result;
            }
        }
        catch { }

        return results;
    }

    // Returns all indexed paths for a given extension, optionally filtered by zip source substring.
    public static List<string> GetAssetPaths(string gameId, string extension, string zipSourceContains = null)
    {
        var results = new List<string>();
        string dbPath = GetDatabasePath(gameId);
        if (!File.Exists(dbPath)) return results;

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            if (string.IsNullOrWhiteSpace(zipSourceContains))
            {
                cmd.CommandText = "SELECT game_path FROM assets WHERE extension = $ext ORDER BY game_path COLLATE NOCASE";
            }
            else
            {
                cmd.CommandText = "SELECT game_path FROM assets WHERE extension = $ext AND zip_source LIKE $zip COLLATE NOCASE ORDER BY game_path COLLATE NOCASE";
                cmd.Parameters.AddWithValue("$zip", $"%{zipSourceContains}%");
            }

            cmd.Parameters.AddWithValue("$ext", extension);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                    results.Add(reader.GetString(0));
            }
        }
        catch
        {
            return [];
        }

        return results;
    }

    // Returns true when the stored asset database schema for this game includes shaderbin entries.
    // Older databases still support materialbin/swatchbin lookups but should be rebuilt for shader browsing.
    public static bool SupportsIndexedShaderbins(string gameId)
    {
        string dbPath = GetDatabasePath(gameId);
        if (!File.Exists(dbPath))
            return false;

        return GetAssetIndexVersion(gameId) >= CurrentAssetIndexVersion;
    }

    // Scans materials.zip archives under a game root and returns all materialbin Game:\ paths.
    // Used as a fallback when no asset database has been built yet.
    public static List<string> ScanMaterialbinPathsFromMaterialsZip(string gameRootPath)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(gameRootPath) || !Directory.Exists(gameRootPath))
            return results;

        string mediaPath = Path.Combine(gameRootPath, "media");
        if (!Directory.Exists(mediaPath))
            mediaPath = gameRootPath;

        var zipPaths = new List<string>();
        string[] materialZipNames = ["materials.zip", "Materials.zip"];
        foreach (var zipName in materialZipNames)
        {
            try
            {
                zipPaths.AddRange(Directory.GetFiles(mediaPath, zipName, SearchOption.AllDirectories));
            }
            catch
            {
            }
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var zipPath in zipPaths)
        {
            if (!File.Exists(zipPath))
                continue;

            string zipRelativeToGame = Path.GetRelativePath(gameRootPath, zipPath);
            string zipFolder = Path.Combine(
                Path.GetDirectoryName(zipRelativeToGame) ?? string.Empty,
                Path.GetFileNameWithoutExtension(zipRelativeToGame));

            bool usedStandard = false;
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    if (!Path.GetExtension(entry.Name).Equals(".materialbin", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string entryPath = entry.FullName.Replace('/', '\\');
                    string fullRelativePath = Path.Combine(zipFolder, entryPath);
                    string gamePath = $"Game:\\{fullRelativePath}";
                    if (seenPaths.Add(gamePath))
                        results.Add(gamePath);
                }

                usedStandard = true;
            }
            catch
            {
            }

            if (usedStandard)
                continue;

            try
            {
                using var customZip = new CustomZipFile(zipPath);
                var entries = customZip.GetEntries();
                foreach (var entry in entries)
                {
                    if (entry.IsDirectory)
                        continue;

                    string fileName = Path.GetFileName(entry.Name);
                    if (!Path.GetExtension(fileName).Equals(".materialbin", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string entryPath = entry.Name.Replace('/', '\\');
                    string fullRelativePath = Path.Combine(zipFolder, entryPath);
                    string gamePath = $"Game:\\{fullRelativePath}";
                    if (seenPaths.Add(gamePath))
                        results.Add(gamePath);
                }
            }
            catch
            {
            }
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    // Finds materials.zip, shaders.zip, and textures.zip files in the media directory tree.
    // Falls back to _library folder zips.
    private static List<string> FindRelevantZips(string mediaPath)
    {
        var results = new List<string>();

        string[] priorityZipNames = ["materials.zip", "shaders.zip", "textures.zip", "Materials.zip", "Shaders.zip", "Textures.zip"];

        try
        {
            foreach (var zipName in priorityZipNames)
            {
                var found = Directory.GetFiles(mediaPath, zipName, SearchOption.AllDirectories);
                results.AddRange(found);
            }
        }
        catch { }

        // Also include _library folder zips
        try
        {
            var libraryDirs = Directory.GetDirectories(mediaPath, "_library", SearchOption.AllDirectories);
            foreach (var libDir in libraryDirs)
            {
                try { results.AddRange(Directory.GetFiles(libDir, "*.zip", SearchOption.AllDirectories)); }
                catch { }
            }
        }
        catch { }

        // Deduplicate
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<string>();
        foreach (var path in results)
        {
            if (seen.Add(path))
                deduped.Add(path);
        }
        return deduped;
    }

    private static int GetAssetIndexVersion(string gameId)
    {
        string dbPath = GetDatabasePath(gameId);
        if (!File.Exists(dbPath))
            return 0;

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM metadata WHERE key = 'asset_index_version'";
            string? versionText = cmd.ExecuteScalar() as string;
            return int.TryParse(versionText, out int version)
                ? version
                : 1;
        }
        catch
        {
            return 1;
        }
    }
}
