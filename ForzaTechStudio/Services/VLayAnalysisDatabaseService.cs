using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ForzaTechStudio.Services;

// Supported Forza game titles for per-game database storage.
public enum ForzaGame
{
    FH2,
    FM6,
    FH3,
    FM7,
    FH4,
    FH5,
    FM2023
}

// Represents a single VLay element entry parsed from a modelbin.
public class VLayElementRecord
{
    public string SemanticName { get; set; }
    public short SemanticIndex { get; set; }
    public short InputSlot { get; set; }
    public int Format { get; set; }
    public int PackedFormat { get; set; }
    public int ByteSize { get; set; }

    public string Key => $"{SemanticName}{SemanticIndex}";

    public override string ToString() =>
        $"{SemanticName}{SemanticIndex} Slot{InputSlot} Fmt={Format} Packed={PackedFormat} Size={ByteSize}";
}

// Represents material usage of a VLay � which material ID references which VLay index in a mesh.
public class VLayMaterialUsageRecord
{
    public int MeshIndex { get; set; }
    public int VLayIndex { get; set; }
    public short MaterialId { get; set; }
    public string MaterialName { get; set; }
}

// A full VLay layout record from a single modelbin's VLay blob, including material usage.
public class VLayLayoutRecord
{
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public string SourceZip { get; set; }
    public byte VlayVersionMajor { get; set; }
    public byte VlayVersionMinor { get; set; }
    public uint Flags { get; set; }
    public int VlayBlobIndex { get; set; }
    public int Slot1ElementCount { get; set; }
    public int Slot1Stride { get; set; }
    public List<VLayElementRecord> Slot0Elements { get; set; } = [];
    public List<VLayElementRecord> Slot1Elements { get; set; } = [];
    public List<VLayMaterialUsageRecord> MaterialUsages { get; set; } = [];

    // Generates a canonical string key for this layout's slot 1 signature.
    public string Slot1SignatureKey
    {
        get
        {
            var parts = Slot1Elements
                .OrderBy(e => e.SemanticName)
                .ThenBy(e => e.SemanticIndex)
                .Select(e => $"{e.SemanticName}{e.SemanticIndex}:{e.Format}:{e.PackedFormat}");
            return string.Join("|", parts);
        }
    }
}

// Aggregated pattern statistics for display.
public class VLayPatternStats
{
    public string SignatureKey { get; set; }
    public int Occurrences { get; set; }
    public int Slot1ElementCount { get; set; }
    public int Slot1Stride { get; set; }
    public uint Flags { get; set; }
    public List<VLayElementRecord> Elements { get; set; } = [];
    public List<string> SampleFiles { get; set; } = [];
    public List<string> MaterialNames { get; set; } = [];
}

// Summary info for a single game database.
public class VLayDatabaseSummary
{
    public ForzaGame Game { get; set; }
    public int TotalModelbins { get; set; }
    public int TotalEntries { get; set; }
    public int TotalPatterns { get; set; }
    public int TotalMaterialUsages { get; set; }
    public List<VLayPatternStats> Patterns { get; set; } = [];
    public DateTime LastUpdated { get; set; }
}

// Represents a difference between two games for a given VLay pattern or material.
public class VLayComparisonEntry
{
    public string Description { get; set; }
    public string GameA { get; set; }
    public string GameB { get; set; }
    public string ValueA { get; set; }
    public string ValueB { get; set; }
    public string DiffType { get; set; } // "PatternOnly", "ElementDiff", "MaterialDiff", "FlagsDiff"
}

// SQLite-backed per-game database for storing VLay layout analysis data.
// Each Forza game gets its own database file. Entries are never overwritten � each load adds new rows.
public class VLayAnalysisDatabaseService
{
    private const string DB_FOLDER = "ForzaTechStudio";

    private static string GetDbPath(ForzaGame game)
    {
        string dbName = $"vlay_{game}.db";
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DB_FOLDER, dbName);
    }

    public static string DatabasePath(ForzaGame game) => GetDbPath(game);

    public static bool DatabaseExists(ForzaGame game) => File.Exists(GetDbPath(game));

    public static void EnsureDatabase(ForzaGame game)
    {
        string dbPath = GetDbPath(game);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS modelbins (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_name TEXT NOT NULL,
                file_path TEXT NOT NULL,
                source_zip TEXT,
                vlay_version_major INTEGER NOT NULL,
                vlay_version_minor INTEGER NOT NULL,
                vlay_flags INTEGER NOT NULL,
                vlay_blob_index INTEGER NOT NULL DEFAULT 0,
                slot1_element_count INTEGER NOT NULL,
                slot1_stride INTEGER NOT NULL,
                slot1_signature TEXT NOT NULL,
                added_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_modelbins_signature ON modelbins(slot1_signature);

            CREATE TABLE IF NOT EXISTS vlay_elements (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                modelbin_id INTEGER NOT NULL,
                semantic_name TEXT NOT NULL,
                semantic_index INTEGER NOT NULL,
                input_slot INTEGER NOT NULL,
                format INTEGER NOT NULL,
                packed_format INTEGER NOT NULL,
                byte_size INTEGER NOT NULL,
                FOREIGN KEY (modelbin_id) REFERENCES modelbins(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_elements_modelbin ON vlay_elements(modelbin_id);

            CREATE TABLE IF NOT EXISTS material_usages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                modelbin_id INTEGER NOT NULL,
                mesh_index INTEGER NOT NULL,
                vlay_index INTEGER NOT NULL,
                material_id INTEGER NOT NULL,
                material_name TEXT,
                FOREIGN KEY (modelbin_id) REFERENCES modelbins(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_material_modelbin ON material_usages(modelbin_id);
            CREATE INDEX IF NOT EXISTS idx_material_name ON material_usages(material_name);

            CREATE TABLE IF NOT EXISTS metadata (
                key TEXT PRIMARY KEY,
                value TEXT
            );
        """;
        cmd.ExecuteNonQuery();
    }

    // Inserts multiple layouts in a single transaction. Never overwrites � always appends new entries.
    public static void InsertLayouts(ForzaGame game, IEnumerable<VLayLayoutRecord> records)
    {
        EnsureDatabase(game);

        string dbPath = GetDbPath(game);
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var transaction = conn.BeginTransaction();

        foreach (var record in records)
        {
            using var insertModelbin = conn.CreateCommand();
            insertModelbin.CommandText = """
                INSERT INTO modelbins (file_name, file_path, source_zip, vlay_version_major, vlay_version_minor,
                                       vlay_flags, vlay_blob_index, slot1_element_count, slot1_stride, slot1_signature, added_utc)
                VALUES ($name, $path, $zip, $vmaj, $vmin, $flags, $bidx, $cnt, $stride, $sig, $utc);
                SELECT last_insert_rowid();
            """;
            insertModelbin.Parameters.AddWithValue("$name", record.FileName);
            insertModelbin.Parameters.AddWithValue("$path", record.FilePath ?? "");
            insertModelbin.Parameters.AddWithValue("$zip", record.SourceZip ?? "");
            insertModelbin.Parameters.AddWithValue("$vmaj", record.VlayVersionMajor);
            insertModelbin.Parameters.AddWithValue("$vmin", record.VlayVersionMinor);
            insertModelbin.Parameters.AddWithValue("$flags", (long)record.Flags);
            insertModelbin.Parameters.AddWithValue("$bidx", record.VlayBlobIndex);
            insertModelbin.Parameters.AddWithValue("$cnt", record.Slot1ElementCount);
            insertModelbin.Parameters.AddWithValue("$stride", record.Slot1Stride);
            insertModelbin.Parameters.AddWithValue("$sig", record.Slot1SignatureKey);
            insertModelbin.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));

            long modelbinId = (long)insertModelbin.ExecuteScalar()!;

            // Insert elements
            using var insertElement = conn.CreateCommand();
            insertElement.CommandText = """
                INSERT INTO vlay_elements (modelbin_id, semantic_name, semantic_index, input_slot, format, packed_format, byte_size)
                VALUES ($mid, $sname, $sidx, $slot, $fmt, $pfmt, $sz)
            """;
            var pMid = insertElement.Parameters.Add("$mid", SqliteType.Integer);
            var pSname = insertElement.Parameters.Add("$sname", SqliteType.Text);
            var pSidx = insertElement.Parameters.Add("$sidx", SqliteType.Integer);
            var pSlot = insertElement.Parameters.Add("$slot", SqliteType.Integer);
            var pFmt = insertElement.Parameters.Add("$fmt", SqliteType.Integer);
            var pPfmt = insertElement.Parameters.Add("$pfmt", SqliteType.Integer);
            var pSz = insertElement.Parameters.Add("$sz", SqliteType.Integer);

            foreach (var elem in record.Slot0Elements.Concat(record.Slot1Elements))
            {
                pMid.Value = modelbinId;
                pSname.Value = elem.SemanticName;
                pSidx.Value = elem.SemanticIndex;
                pSlot.Value = elem.InputSlot;
                pFmt.Value = elem.Format;
                pPfmt.Value = elem.PackedFormat;
                pSz.Value = elem.ByteSize;
                insertElement.ExecuteNonQuery();
            }

            // Insert material usages
            if (record.MaterialUsages.Count > 0)
            {
                using var insertMat = conn.CreateCommand();
                insertMat.CommandText = """
                    INSERT INTO material_usages (modelbin_id, mesh_index, vlay_index, material_id, material_name)
                    VALUES ($mid, $meshIdx, $vlayIdx, $matId, $matName)
                """;
                var mMid = insertMat.Parameters.Add("$mid", SqliteType.Integer);
                var mMeshIdx = insertMat.Parameters.Add("$meshIdx", SqliteType.Integer);
                var mVlayIdx = insertMat.Parameters.Add("$vlayIdx", SqliteType.Integer);
                var mMatId = insertMat.Parameters.Add("$matId", SqliteType.Integer);
                var mMatName = insertMat.Parameters.Add("$matName", SqliteType.Text);

                foreach (var mu in record.MaterialUsages)
                {
                    mMid.Value = modelbinId;
                    mMeshIdx.Value = mu.MeshIndex;
                    mVlayIdx.Value = mu.VLayIndex;
                    mMatId.Value = mu.MaterialId;
                    mMatName.Value = mu.MaterialName ?? "";
                    insertMat.ExecuteNonQuery();
                }
            }
        }

        using var metaCmd = conn.CreateCommand();
        metaCmd.CommandText = "INSERT OR REPLACE INTO metadata (key, value) VALUES ('last_updated', $v)";
        metaCmd.Parameters.AddWithValue("$v", DateTime.UtcNow.ToString("O"));
        metaCmd.ExecuteNonQuery();

        transaction.Commit();
    }

    // Loads a complete summary of a game's database.
    public static VLayDatabaseSummary GetSummary(ForzaGame game)
    {
        var summary = new VLayDatabaseSummary { Game = game };
        if (!DatabaseExists(game)) return summary;

        string dbPath = GetDbPath(game);
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        // Total entries (rows)
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM modelbins";
            summary.TotalEntries = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Distinct modelbin names
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(DISTINCT file_name) FROM modelbins";
            summary.TotalModelbins = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Material usages count
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM material_usages";
            summary.TotalMaterialUsages = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Unique patterns
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT slot1_signature, COUNT(*) as cnt, slot1_element_count, slot1_stride, vlay_flags
                FROM modelbins
                GROUP BY slot1_signature
                ORDER BY cnt DESC
            """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var pattern = new VLayPatternStats
                {
                    SignatureKey = reader.GetString(0),
                    Occurrences = reader.GetInt32(1),
                    Slot1ElementCount = reader.GetInt32(2),
                    Slot1Stride = reader.GetInt32(3),
                    Flags = (uint)reader.GetInt64(4)
                };
                summary.Patterns.Add(pattern);
            }
        }

        summary.TotalPatterns = summary.Patterns.Count;

        // Per-pattern details
        foreach (var pattern in summary.Patterns)
        {
            // Sample files
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT file_name FROM modelbins WHERE slot1_signature = $sig LIMIT 5";
                cmd.Parameters.AddWithValue("$sig", pattern.SignatureKey);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    pattern.SampleFiles.Add(reader.GetString(0));
            }

            // Elements
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT e.semantic_name, e.semantic_index, e.input_slot, e.format, e.packed_format, e.byte_size
                    FROM vlay_elements e
                    JOIN modelbins m ON e.modelbin_id = m.id
                    WHERE m.slot1_signature = $sig AND e.input_slot = 1
                    LIMIT 20
                """;
                cmd.Parameters.AddWithValue("$sig", pattern.SignatureKey);
                using var reader = cmd.ExecuteReader();
                var seenElements = new HashSet<string>();
                while (reader.Read())
                {
                    string key = $"{reader.GetString(0)}{reader.GetInt32(1)}";
                    if (seenElements.Add(key))
                    {
                        pattern.Elements.Add(new VLayElementRecord
                        {
                            SemanticName = reader.GetString(0),
                            SemanticIndex = (short)reader.GetInt32(1),
                            InputSlot = (short)reader.GetInt32(2),
                            Format = reader.GetInt32(3),
                            PackedFormat = reader.GetInt32(4),
                            ByteSize = reader.GetInt32(5)
                        });
                    }
                }
            }

            // Material names using this pattern
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT DISTINCT mu.material_name
                    FROM material_usages mu
                    JOIN modelbins m ON mu.modelbin_id = m.id
                    WHERE m.slot1_signature = $sig AND mu.material_name != ''
                    ORDER BY mu.material_name
                """;
                cmd.Parameters.AddWithValue("$sig", pattern.SignatureKey);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    pattern.MaterialNames.Add(reader.GetString(0));
            }

            // Modelbin names using this pattern
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT DISTINCT file_name
                    FROM modelbins
                    WHERE slot1_signature = $sig
                    ORDER BY file_name
                """;
                cmd.Parameters.AddWithValue("$sig", pattern.SignatureKey);
                using var reader = cmd.ExecuteReader();
                var modelbinList = new List<string>();
                while (reader.Read())
                    modelbinList.Add(reader.GetString(0));
                
                // Store in SampleFiles for now (we can rename this property or add a new one)
                pattern.SampleFiles.Clear();
                pattern.SampleFiles.AddRange(modelbinList);
            }
        }

        // Last updated
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM metadata WHERE key = 'last_updated'";
            var val = cmd.ExecuteScalar() as string;
            if (DateTime.TryParse(val, out var dt))
                summary.LastUpdated = dt;
        }

        return summary;
    }

    // Compares two game databases and produces a list of difference entries.
    public static List<VLayComparisonEntry> CompareDatabases(ForzaGame gameA, ForzaGame gameB)
    {
        var results = new List<VLayComparisonEntry>();

        var summaryA = GetSummary(gameA);
        var summaryB = GetSummary(gameB);

        var sigA = summaryA.Patterns.Select(p => p.SignatureKey).ToHashSet();
        var sigB = summaryB.Patterns.Select(p => p.SignatureKey).ToHashSet();

        // Patterns only in A
        foreach (var sig in sigA.Except(sigB))
        {
            var p = summaryA.Patterns.First(x => x.SignatureKey == sig);
            var elemStr = string.Join(", ", p.Elements.Select(e => e.Key));
            var matStr = p.MaterialNames.Count > 0 ? string.Join(", ", p.MaterialNames.Take(5)) : "(none)";
            results.Add(new VLayComparisonEntry
            {
                DiffType = "PatternOnly",
                Description = $"Pattern only in {gameA}: {p.Slot1ElementCount} elem, stride={p.Slot1Stride}",
                GameA = $"{p.Occurrences} occurrences. Elements: {elemStr}",
                GameB = "Not present",
                ValueA = $"Materials: {matStr}",
                ValueB = ""
            });
        }

        // Patterns only in B
        foreach (var sig in sigB.Except(sigA))
        {
            var p = summaryB.Patterns.First(x => x.SignatureKey == sig);
            var elemStr = string.Join(", ", p.Elements.Select(e => e.Key));
            var matStr = p.MaterialNames.Count > 0 ? string.Join(", ", p.MaterialNames.Take(5)) : "(none)";
            results.Add(new VLayComparisonEntry
            {
                DiffType = "PatternOnly",
                Description = $"Pattern only in {gameB}: {p.Slot1ElementCount} elem, stride={p.Slot1Stride}",
                GameA = "Not present",
                GameB = $"{p.Occurrences} occurrences. Elements: {elemStr}",
                ValueA = "",
                ValueB = $"Materials: {matStr}"
            });
        }

        // Common patterns � compare flags, element formats, material differences
        foreach (var sig in sigA.Intersect(sigB))
        {
            var pA = summaryA.Patterns.First(x => x.SignatureKey == sig);
            var pB = summaryB.Patterns.First(x => x.SignatureKey == sig);

            if (pA.Flags != pB.Flags)
            {
                results.Add(new VLayComparisonEntry
                {
                    DiffType = "FlagsDiff",
                    Description = $"Flags differ for pattern: {pA.Slot1ElementCount} elem, stride={pA.Slot1Stride}",
                    GameA = $"0x{pA.Flags:X}",
                    GameB = $"0x{pB.Flags:X}",
                    ValueA = $"{pA.Occurrences} occurrences",
                    ValueB = $"{pB.Occurrences} occurrences"
                });
            }

            // Compare materials used by same pattern
            var matsA = pA.MaterialNames.ToHashSet();
            var matsB = pB.MaterialNames.ToHashSet();
            var onlyInA = matsA.Except(matsB).ToList();
            var onlyInB = matsB.Except(matsA).ToList();

            if (onlyInA.Count > 0 || onlyInB.Count > 0)
            {
                results.Add(new VLayComparisonEntry
                {
                    DiffType = "MaterialDiff",
                    Description = $"Material usage differs for pattern: {pA.Slot1ElementCount} elem",
                    GameA = onlyInA.Count > 0 ? $"Only in {gameA}: {string.Join(", ", onlyInA.Take(8))}" : "No unique materials",
                    GameB = onlyInB.Count > 0 ? $"Only in {gameB}: {string.Join(", ", onlyInB.Take(8))}" : "No unique materials",
                    ValueA = $"{matsA.Count} total materials",
                    ValueB = $"{matsB.Count} total materials"
                });
            }
        }

        // Overall stats diff
        results.Insert(0, new VLayComparisonEntry
        {
            DiffType = "Summary",
            Description = "Database Overview",
            GameA = $"{gameA}: {summaryA.TotalEntries} entries, {summaryA.TotalModelbins} modelbins, {summaryA.TotalPatterns} patterns",
            GameB = $"{gameB}: {summaryB.TotalEntries} entries, {summaryB.TotalModelbins} modelbins, {summaryB.TotalPatterns} patterns",
            ValueA = $"{summaryA.TotalMaterialUsages} material usages",
            ValueB = $"{summaryB.TotalMaterialUsages} material usages"
        });

        return results;
    }

    // Returns available games that have databases.
    public static List<ForzaGame> GetAvailableGames()
    {
        return Enum.GetValues<ForzaGame>()
            .Where(DatabaseExists)
            .ToList();
    }

    // Gets all distinct modelbin names for a specific game, ordered alphabetically.
    public static List<string> GetAllModelbinNames(ForzaGame game)
    {
        var modelbinNames = new List<string>();
        if (!DatabaseExists(game)) return modelbinNames;

        string dbPath = GetDbPath(game);
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT file_name FROM modelbins ORDER BY file_name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            modelbinNames.Add(reader.GetString(0));

        return modelbinNames;
    }

    // Resets a specific game's database.
    public static void ResetDatabase(ForzaGame game)
    {
        string dbPath = GetDbPath(game);
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }

    // Returns the path where cross-game summary reports are stored.
    public static string GetSummaryReportPath()
    {
        string summariesFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DB_FOLDER, "Summaries");
        Directory.CreateDirectory(summariesFolder);
        return summariesFolder;
    }

    // Saves a cross-game VLay summary report to a timestamped text file; returns the output path.
    public static string SaveSummaryReport(string summaryText, List<ForzaGame> gamesCovered)
    {
        string summariesFolder = GetSummaryReportPath();
        
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string gamesLabel = string.Join("-", gamesCovered.Select(g => g.ToString()));
        string fileName = $"VLaySummary_{gamesLabel}_{timestamp}.txt";
        string filePath = Path.Combine(summariesFolder, fileName);

        // Add metadata header to the report
        var lines = new List<string>
        {
            "??????????????????????????????????????????????????????????????????????????",
            "?           FORZA VLAY CROSS-GAME ANALYSIS SUMMARY REPORT               ?",
            "??????????????????????????????????????????????????????????????????????????",
            "",
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Games Analyzed: {string.Join(", ", gamesCovered)}",
            $"Total Games: {gamesCovered.Count}",
            "",
            "This report provides a comprehensive analysis of vertex layout (VLay) blob",
            "differences across Forza game titles, including:",
            "  � Per-game statistics (modelbins, entries, patterns, material usages)",
            "  � Pattern inventory (which games have which VLay signatures)",
            "  � Material coverage analysis (shared vs unique materials per pattern)",
            "  � Key evolutionary differences (exclusive patterns, stride changes)",
            "",
            new string('?', 76),
            ""
        };

        lines.AddRange(summaryText.Split(new[] { '\r', '\n' }, StringSplitOptions.None)
            .Where(line => line != null));

        lines.Add("");
        lines.Add(new string('?', 76));
        lines.Add($"Report saved to: {filePath}");
        lines.Add($"Database location: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DB_FOLDER)}");

        File.WriteAllLines(filePath, lines);
        return filePath;
    }

    // Gets a list of all saved summary reports, ordered by creation date (newest first).
    public static List<(string FilePath, DateTime Created, string[] GamesCovered)> GetSavedSummaryReports()
    {
        string summariesFolder = GetSummaryReportPath();
        var reports = new List<(string FilePath, DateTime Created, string[] GamesCovered)>();

        if (!Directory.Exists(summariesFolder))
            return reports;

        foreach (var file in Directory.GetFiles(summariesFolder, "VLaySummary_*.txt"))
        {
            try
            {
                var fileInfo = new FileInfo(file);
                var fileName = Path.GetFileNameWithoutExtension(file);
                
                // Parse games from filename: VLaySummary_{Games}_{Timestamp}.txt
                var parts = fileName.Split('_');
                string[] games = [];
                
                if (parts.Length >= 2)
                {
                    // Find the games part (between first _ and last _)
                    string gamesPart = string.Join("_", parts.Skip(1).Take(parts.Length - 2));
                    games = gamesPart.Split('-');
                }

                reports.Add((file, fileInfo.CreationTime, games));
            }
            catch
            {
                // Skip malformed files
            }
        }

        return reports.OrderByDescending(r => r.Created).ToList();
    }

    // Loads a summary report from disk.
    public static string LoadSummaryReport(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        return File.ReadAllText(filePath);
    }

    // Deletes a saved summary report.
    public static void DeleteSummaryReport(string filePath)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    // Resets all game databases.
    public static void ResetAllDatabases()
    {
        foreach (var game in Enum.GetValues<ForzaGame>())
            ResetDatabase(game);
    }
}
