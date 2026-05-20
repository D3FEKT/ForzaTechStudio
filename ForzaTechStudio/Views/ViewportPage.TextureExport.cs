using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ForzaTechStudio.Views
{
    // Texture export: converts all .swatchbin files from each source ZIP into
    // a "{ZipName} Textures" subfolder alongside the exported geometry file.
    public sealed partial class ViewportPage : Page
    {
        // Exports DDS textures from every zip that contributed to the current export.
        // Returns a short summary string (e.g. "12 texture(s) NIS_SilviaK_92 Textures")
        // or null if no zip sources were found or no swatchbins existed.
        private async Task<string?> ExportZipTextures(
            IEnumerable<string> zipPaths,
            string outputDirectory)
        {
            var distinct = zipPaths
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinct.Count == 0)
                return null;

            var service = new SwatchbinService();
            var folderSummaries = new List<string>();

            foreach (var zipPath in distinct)
            {
                string zipName = Path.GetFileNameWithoutExtension(zipPath);
                string texFolderPath = Path.Combine(outputDirectory, $"{zipName} Textures");

                int written = 0;
                int failed  = 0;

                await Task.Run(() =>
                {
                    List<CustomZipFile.ZipEntryInfo> swatchEntries;
                    using (var zip = new CustomZipFile(zipPath))
                    {
                        swatchEntries = zip.GetEntries()
                            .Where(e => !e.IsDirectory &&
                                        e.Name.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }

                    if (swatchEntries.Count == 0)
                        return; // nothing to write - skip folder creation

                    Directory.CreateDirectory(texFolderPath);

                    // Re-open for extraction 
                    using var zipExtract = new CustomZipFile(zipPath);
                    foreach (var entry in swatchEntries)
                    {
                        try
                        {
                            byte[] bytes = zipExtract.ExtractToMemory(entry);
                            using var ms  = new MemoryStream(bytes);
                            var info = service.LoadSwatchbin(ms);

                            if (info?.DdsData == null || info.DdsData.Length == 0)
                            {
                                failed++;
                                continue;
                            }

                            string baseName = Path.GetFileNameWithoutExtension(
                                Path.GetFileName(entry.Name));
                            string ddsPath = Path.Combine(texFolderPath, baseName + ".dds");
                            File.WriteAllBytes(ddsPath, info.DdsData);
                            written++;
                        }
                        catch
                        {
                            failed++;
                        }
                    }
                });

                if (written > 0)
                    folderSummaries.Add($"{written} texture(s) → {zipName} Textures");
            }

            return folderSummaries.Count > 0
                ? string.Join("\n", folderSummaries)
                : null;
        }

        // Scans the zip entries for .swatchbin files and builds a map of material base name > relative path from the MTL file to the exported DDS.
        // e.g. "BODYWORK" - "NIS_SilviaK_92 Textures\BODYWORK.dds"

        private static Dictionary<string, string> BuildTexturePathMap(
            IEnumerable<string> zipPaths)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var zipPath in zipPaths
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string zipName      = Path.GetFileNameWithoutExtension(zipPath);
                string texFolderName = $"{zipName} Textures";

                try
                {
                    using var zip = new CustomZipFile(zipPath);
                    foreach (var entry in zip.GetEntries()
                        .Where(e => !e.IsDirectory &&
                                    e.Name.EndsWith(".swatchbin", StringComparison.OrdinalIgnoreCase)))
                    {
                        string baseName = Path.GetFileNameWithoutExtension(
                            Path.GetFileName(entry.Name));

                        if (!string.IsNullOrEmpty(baseName) && !map.ContainsKey(baseName))
                            map[baseName] = Path.Combine(texFolderName, baseName + ".dds");
                    }
                }
                catch { }
            }

            return map;
        }

        private IEnumerable<string> CollectSourceZipPaths(IEnumerable<IViewerNode> roots)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
                foreach (var path in CollectSourceZipPathsRecursive(root, seen))
                    yield return path;
        }

        private static IEnumerable<string> CollectSourceZipPathsRecursive(
            IViewerNode node,
            HashSet<string> seen)
        {
            if (node is ModelBinNode mb &&
                !string.IsNullOrEmpty(mb.SourceZipPath) &&
                seen.Add(mb.SourceZipPath))
            {
                yield return mb.SourceZipPath;
            }

            foreach (var child in node.Children)
                foreach (var path in CollectSourceZipPathsRecursive(child, seen))
                    yield return path;
        }
    }
}
