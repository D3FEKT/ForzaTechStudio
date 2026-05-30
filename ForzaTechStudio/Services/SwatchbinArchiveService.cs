using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using ForzaTools.Bundles.Metadata.TextureContentHeaders;
using System;
using System.Collections.Generic;
using System.IO;
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

    public IReadOnlyList<SwatchbinArchiveEntry> LoadZipTextureEntries(string zipPath, CancellationToken cancellationToken = default)
    {
        return LoadZipTextureEntriesCore(zipPath, cancellationToken);
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
            if (entries.Count(e =>
                e.Name.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileName(e.Name), Path.GetFileName(entry.Name), StringComparison.OrdinalIgnoreCase)) > 1)
            {
                displayName = $"[{zipName}] {entry.Name}";
            }

            entriesToLoad.Add(new SwatchbinArchiveEntry
            {
                DisplayName = displayName,
                SourceArchivePath = zipPath,
                LogicalPath = entry.Name,
                SwatchbinData = zip.ExtractToMemory(entry)
            });
        }

        return entriesToLoad;
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
    public string DisplayName { get; init; } = string.Empty;
    public string SourceArchivePath { get; init; } = string.Empty;
    public string LogicalPath { get; init; } = string.Empty;
    public byte[] SwatchbinData { get; init; } = Array.Empty<byte>();
}