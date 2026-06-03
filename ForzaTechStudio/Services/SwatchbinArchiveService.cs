using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using ForzaTools.Bundles.Metadata.TextureContentHeaders;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ForzaTechStudio.Services;

public sealed class SwatchbinArchiveService
{
    public Task<IReadOnlyList<SwatchbinArchiveEntry>> LoadZipTextureEntriesAsync(string zipPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => LoadZipTextureEntries(zipPath, cancellationToken), cancellationToken);
    }

    public IReadOnlyList<SwatchbinArchiveEntry> IndexZipTextureEntries(string zipPath, CancellationToken cancellationToken = default)
    {
        return IndexZipTextureEntriesCore(zipPath, cancellationToken);
    }

    public IReadOnlyList<SwatchbinArchiveEntry> LoadZipTextureEntries(string zipPath, CancellationToken cancellationToken = default)
    {
        return LoadZipTextureEntriesCore(zipPath, cancellationToken);
    }

    public static bool TryLoadGameTextureEntry(string gameRootPath, string requestedPath, string sourceLabel, out SwatchbinArchiveEntry? entry)
    {
        entry = null;

        if (string.IsNullOrWhiteSpace(gameRootPath) || !Directory.Exists(gameRootPath) || string.IsNullOrWhiteSpace(requestedPath))
            return false;

        foreach (string relativePath in ExpandGameRelativePathCandidates(requestedPath))
        {
            string directPath = Path.Combine(gameRootPath, relativePath);
            if (File.Exists(directPath))
            {
                entry = new SwatchbinArchiveEntry
                {
                    DisplayName = $"[{sourceLabel}] {Path.GetFileName(relativePath)}",
                    SourceArchivePath = directPath,
                    LogicalPath = relativePath,
                    DataLoader = () => File.ReadAllBytes(directPath)
                };
                return true;
            }

            if (TryFindGamePathInZip(gameRootPath, relativePath, out var zipPath, out var entryName))
            {
                entry = new SwatchbinArchiveEntry
                {
                    DisplayName = $"[{sourceLabel}] {entryName}",
                    SourceArchivePath = zipPath,
                    LogicalPath = relativePath,
                    DataLoader = () => LoadZipEntryBytes(zipPath, entryName)
                };
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<SwatchbinArchiveEntry> LoadZipTextureEntriesCore(string zipPath, CancellationToken cancellationToken)
    {
        var entriesToLoad = new List<SwatchbinArchiveEntry>();
        string zipName = Path.GetFileName(zipPath);

        using var zip = new CustomZipFile(zipPath);
        var entries = zip.GetEntries()
            .Where(e => !e.IsDirectory &&
                (e.Name.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase) ||
                 e.Name.EndsWith(".pb", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var swatchbinNameCounts = entries
            .Where(e => e.Name.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => Path.GetFileName(e.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.Name.EndsWith(".pb", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var data = zip.ExtractToMemory(entry);
                    using var stream = new MemoryStream(data);
                    entriesToLoad.AddRange(ExtractTextureBundleEntries(stream, zipPath, entry.Name, $"[{zipName}] {entry.Name}", entry.Name));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Zip/PB] {entry.Name}: {ex.Message}");
                }

                continue;
            }

            string displayName = $"[{zipName}] {Path.GetFileName(entry.Name)}";
            if (swatchbinNameCounts.TryGetValue(Path.GetFileName(entry.Name), out int duplicateCount) && duplicateCount > 1)
            {
                displayName = $"[{zipName}] {entry.Name}";
            }

            entriesToLoad.Add(new SwatchbinArchiveEntry
            {
                DisplayName = displayName,
                SourceArchivePath = zipPath,
                LogicalPath = entry.Name,
                DataLoader = () => LoadZipEntryBytes(zipPath, entry.Name)
            });
        }

        return entriesToLoad;
    }

    private static IReadOnlyList<SwatchbinArchiveEntry> IndexZipTextureEntriesCore(string zipPath, CancellationToken cancellationToken)
    {
        var indexedEntries = new List<SwatchbinArchiveEntry>();
        string zipName = Path.GetFileName(zipPath);

        using var zip = new CustomZipFile(zipPath);
        var entries = zip.GetEntries()
            .Where(e => !e.IsDirectory && e.Name.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var nameCounts = entries
            .GroupBy(e => Path.GetFileName(e.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string entryName = entry.Name;
            string displayName = $"[{zipName}] {Path.GetFileName(entryName)}";
            if (nameCounts.TryGetValue(Path.GetFileName(entryName), out int duplicateCount) && duplicateCount > 1)
                displayName = $"[{zipName}] {entryName}";

            indexedEntries.Add(new SwatchbinArchiveEntry
            {
                DisplayName = displayName,
                SourceArchivePath = zipPath,
                LogicalPath = entryName,
                DataLoader = () => LoadZipEntryBytes(zipPath, entryName)
            });
        }

        return indexedEntries;
    }

    private static IEnumerable<string> ExpandGameRelativePathCandidates(string requestedPath)
    {
        string normalized = requestedPath.Trim().Replace('/', '\\');
        if (normalized.StartsWith("Game:\\", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Game:/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[6..];

        normalized = normalized.TrimStart('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        foreach (string candidate in ExpandExtensionCandidates(normalized))
        {
            yield return candidate;

            if (candidate.StartsWith("media\\", StringComparison.OrdinalIgnoreCase))
                yield return candidate[6..];
        }
    }

    private static IEnumerable<string> ExpandExtensionCandidates(string relativePath)
    {
        yield return relativePath;

        if (string.IsNullOrWhiteSpace(Path.GetExtension(relativePath)))
            yield return relativePath + ".swatchbin";
    }

    private static bool TryFindGamePathInZip(string gameRootPath, string relativePath, out string zipPath, out string entryName)
    {
        zipPath = string.Empty;
        entryName = string.Empty;

        string[] segments = relativePath
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
            return false;

        for (int zipSegmentIndex = segments.Length - 2; zipSegmentIndex >= 0; zipSegmentIndex--)
        {
            string zipPrefix = Path.Combine(segments.Take(zipSegmentIndex + 1).ToArray());
            string remainingEntry = string.Join('/', segments.Skip(zipSegmentIndex + 1));

            foreach (string candidateZipPath in BuildZipPathCandidates(gameRootPath, zipPrefix))
            {
                if (!File.Exists(candidateZipPath))
                    continue;

                if (ZipEntryExists(candidateZipPath, remainingEntry))
                {
                    zipPath = candidateZipPath;
                    entryName = remainingEntry;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ZipEntryExists(string zipPath, string entryName)
    {
        string normalizedEntryName = NormalizeArchiveEntryName(entryName);

        try
        {
            using var customZip = new CustomZipFile(zipPath);
            if (customZip.GetEntries().Any(entry =>
                !entry.IsDirectory &&
                string.Equals(NormalizeArchiveEntryName(entry.Name), normalizedEntryName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch
        {
        }

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            return archive.Entries.Any(entry =>
                !string.IsNullOrEmpty(entry.Name) &&
                string.Equals(NormalizeArchiveEntryName(entry.FullName), normalizedEntryName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static byte[] LoadZipEntryBytes(string zipPath, string entryName)
    {
        return TryExtractZipEntry(zipPath, entryName, out var data)
            ? data
            : Array.Empty<byte>();
    }

    private static IEnumerable<string> BuildZipPathCandidates(string gameRootPath, string zipPrefix)
    {
        string direct = Path.Combine(gameRootPath, zipPrefix);
        if (direct.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            yield return direct;
            yield break;
        }

        yield return direct + ".zip";
        yield return direct;
    }

    private static bool TryExtractZipEntry(string zipPath, string entryName, out byte[] data)
    {
        data = Array.Empty<byte>();
        string normalizedEntryName = NormalizeArchiveEntryName(entryName);

        try
        {
            using var customZip = new CustomZipFile(zipPath);
            var match = customZip.GetEntries().FirstOrDefault(entry =>
                !entry.IsDirectory &&
                string.Equals(NormalizeArchiveEntryName(entry.Name), normalizedEntryName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                data = customZip.ExtractToMemory(match);
                return true;
            }
        }
        catch
        {
        }

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var match = archive.Entries.FirstOrDefault(entry =>
                !string.IsNullOrEmpty(entry.Name) &&
                string.Equals(NormalizeArchiveEntryName(entry.FullName), normalizedEntryName, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                return false;

            using var stream = match.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            data = memory.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeArchiveEntryName(string value)
    {
        return (value ?? string.Empty).Replace('\\', '/').TrimStart('/');
    }

    public static uint ComputeCrc32(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        uint crc = 0xffffffff;

        for (int i = 0; i < bytes.Length; i++)
        {
            byte index = (byte)((crc & 0xff) ^ bytes[i]);
            crc = (crc >> 8) ^ Crc32Table[index];
        }

        return ~crc;
    }

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        const uint polynomial = 0xedb88320;

        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0
                    ? (value >> 1) ^ polynomial
                    : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }

    public static List<SwatchbinArchiveEntry> ExtractTextureBundleEntries(Stream bundleStream, string sourceArchivePath, string logicalPath, string displayPrefix, string sourceName)
    {
        var bundle = new Bundle();
        bundle.Load(bundleStream);
        return ExtractTextureBundleEntries(bundle, sourceArchivePath, logicalPath, displayPrefix, sourceName);
    }

    public static List<SwatchbinArchiveEntry> ExtractTextureBundleEntries(Bundle bundle, string sourceArchivePath, string logicalPath, string displayPrefix, string sourceName)
    {
        var textureBlobs = bundle.Blobs.OfType<TextureContentBlob>().ToList();
        var extractedEntries = new List<SwatchbinArchiveEntry>(textureBlobs.Count);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < textureBlobs.Count; index++)
        {
            var textureBlob = textureBlobs[index];
            string textureName = MakeUniqueName(GetTextureEntryName(textureBlob, index), usedNames);

            var singleTextureBundle = new Bundle
            {
                VersionMajor = bundle.VersionMajor,
                VersionMinor = bundle.VersionMinor
            };
            singleTextureBundle.Blobs.Add(textureBlob);

            using var outputStream = new MemoryStream();
            singleTextureBundle.SerializeConverted(outputStream);

            extractedEntries.Add(new SwatchbinArchiveEntry
            {
                DisplayName = $"{displayPrefix} {textureName}",
                SourceArchivePath = sourceArchivePath,
                LogicalPath = $"{logicalPath}/{textureName}",
                SwatchbinData = outputStream.ToArray()
            });
        }

        return extractedEntries;
    }

    private static string MakeUniqueName(string baseName, ISet<string> usedNames)
    {
        string candidate = baseName;
        int suffix = 2;

        while (!usedNames.Add(candidate))
            candidate = $"{baseName}_{suffix++}";

        return candidate;
    }

    private static string GetTextureEntryName(TextureContentBlob textureBlob, int index)
    {
        var idMetadata = textureBlob.GetMetadataByTag<IdentifierMetadata>(BundleMetadata.TAG_METADATA_Identifier);
        if (idMetadata != null)
            return $"tex_0x{idMetadata.Id:X8}";

        var txchMetadata = textureBlob.GetMetadataByTag<TextureContentHeaderMetadata>(BundleMetadata.TAG_METADATA_TextureContentHeader);
        if (txchMetadata != null)
        {
            txchMetadata.ParseWithBlobVersion(textureBlob.VersionMajor, textureBlob.VersionMinor);

            Guid textureId = txchMetadata.PCHeader?.Id ?? txchMetadata.DurangoHeader?.Id ?? Guid.Empty;
            if (textureId != Guid.Empty)
                return $"tex_{textureId:N}";
        }

        return $"tex{index:D2}";
    }
}

public sealed class SwatchbinArchiveEntry
{
    private readonly object _dataLock = new();
    private byte[]? _swatchbinData;

    public string DisplayName { get; init; } = string.Empty;
    public string SourceArchivePath { get; init; } = string.Empty;
    public string LogicalPath { get; init; } = string.Empty;
    public Func<byte[]>? DataLoader { private get; init; }
    public byte[] SwatchbinData
    {
        get
        {
            if (_swatchbinData != null)
                return _swatchbinData;

            lock (_dataLock)
            {
                _swatchbinData ??= DataLoader?.Invoke() ?? Array.Empty<byte>();
                return _swatchbinData;
            }
        }
        init => _swatchbinData = value ?? Array.Empty<byte>();
    }
}