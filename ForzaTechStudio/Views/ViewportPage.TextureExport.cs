using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
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
        // Exports textures from every zip that contributed to the current export.
        // Returns a short summary string (e.g. "12 texture(s) NIS_SilviaK_92 Textures")
        // or null if no zip sources were found or no swatchbins existed.
        private async Task<string?> ExportZipTextures(
            IEnumerable<string> zipPaths,
            string outputDirectory,
            ExportTextureFormat format)
        {
            var distinct = zipPaths
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinct.Count == 0)
                return null;

            string ext = TextureImageExporter.Extension(format);
            var service = new SwatchbinService();
            var folderSummaries = new List<string>();

            foreach (var zipPath in distinct)
            {
                string zipName = Path.GetFileNameWithoutExtension(zipPath);
                string texFolderPath = Path.Combine(outputDirectory, $"{zipName} Textures");

                int written = 0;
                int failed  = 0;

                await Task.Run(async () =>
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

                            if (info == null)
                            {
                                failed++;
                                continue;
                            }

                            byte[]? outBytes = await TextureImageExporter.ConvertAsync(info, format);
                            if (outBytes == null || outBytes.Length == 0)
                            {
                                failed++;
                                continue;
                            }

                            string baseName = Path.GetFileNameWithoutExtension(
                                Path.GetFileName(entry.Name));
                            string outPath = Path.Combine(texFolderPath, baseName + ext);
                            File.WriteAllBytes(outPath, outBytes);
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

        // Scans zip entries and material shader parameters to build a map of material
        // base name > relative exported texture path.
        private static Dictionary<string, string> BuildTexturePathMap(
            IEnumerable<ModelBinExportData> models,
            IEnumerable<string> zipPaths,
            ExportTextureFormat format)
        {
            var textureByBaseName = BuildExportedTexturePathMap(zipPaths, format);
            var materialMap = new Dictionary<string, string>(textureByBaseName, StringComparer.OrdinalIgnoreCase);

            foreach (var model in models)
            {
                if (model.Bundle == null)
                    continue;

                foreach (var material in model.Bundle.Blobs.OfType<MaterialBlob>())
                {
                    string? materialName = material.Metadatas
                        .OfType<NameMetadata>()
                        .FirstOrDefault()
                        ?.Name;

                    string safeMaterialName = ObjExportService.ExtractMaterialBaseName(materialName ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(safeMaterialName) || materialMap.ContainsKey(safeMaterialName))
                        continue;

                    if (TryFindBestMaterialTexture(material, textureByBaseName, out var texturePath))
                        materialMap[safeMaterialName] = texturePath;
                }
            }

            return materialMap;
        }

        // Scans the zip entries for .swatchbin files and builds a map of exported
        // texture base name > relative path from the MTL/FBX file to the texture.
        // e.g. "BODYWORK" -> "NIS_SilviaK_92 Textures\BODYWORK.png"

        private static Dictionary<string, string> BuildTexturePathMap(
            IEnumerable<string> zipPaths,
            ExportTextureFormat format)
            => BuildExportedTexturePathMap(zipPaths, format);

        private static Dictionary<string, string> BuildExportedTexturePathMap(
            IEnumerable<string> zipPaths,
            ExportTextureFormat format)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string ext = TextureImageExporter.Extension(format);

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
                            map[baseName] = Path.Combine(texFolderName, baseName + ext);
                    }
                }
                catch { }
            }

            return map;
        }

        private static bool TryFindBestMaterialTexture(
            MaterialBlob material,
            IReadOnlyDictionary<string, string> textureByBaseName,
            out string texturePath)
        {
            texturePath = string.Empty;
            if (material.Bundle == null || textureByBaseName.Count == 0)
                return false;

            var candidates = material.Bundle.Blobs
                .OfType<MaterialShaderParameterBlob>()
                .SelectMany(blob => blob.Parameters)
                .Where(param => param.Type == ShaderParameterType.Texture2D && param.Value is TextureParameter)
                .Select(param =>
                {
                    var tex = (TextureParameter)param.Value;
                    return new
                    {
                        Parameter = param,
                        Texture = tex,
                        ParameterName = NameHashService.Instance.GetName(param.NameHash) ?? string.Empty,
                        TextureBaseName = ExtractTextureBaseName(tex.Path)
                    };
                })
                .Where(c => !string.IsNullOrWhiteSpace(c.TextureBaseName) &&
                            textureByBaseName.ContainsKey(c.TextureBaseName))
                .OrderByDescending(c => ScoreTextureCandidate(c.ParameterName, c.Texture.Path))
                .ToList();

            if (candidates.Count == 0)
                return false;

            texturePath = textureByBaseName[candidates[0].TextureBaseName];
            return true;
        }

        private static string ExtractTextureBaseName(string? texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
                return string.Empty;

            string normalized = texturePath
                .Replace("game:", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace('/', '\\');

            string fileName = Path.GetFileName(normalized);
            string baseName = Path.GetFileNameWithoutExtension(fileName);

            // Some paths are stored extensionless or with .dds/.png already stripped by tools.
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = fileName;

            return baseName ?? string.Empty;
        }

        private static int ScoreTextureCandidate(string parameterName, string? texturePath)
        {
            string text = $"{parameterName} {texturePath}".ToLowerInvariant();
            int score = 0;

            if (text.Contains("basecolor") || text.Contains("base_color") ||
                text.Contains("diffuse") || text.Contains("_diff") ||
                text.Contains("albedo") || text.Contains("color"))
                score += 100;

            if (text.Contains("emissive") || text.Contains("radiosity") || text.Contains("_lite"))
                score += 25;

            if (text.Contains("normal") || text.Contains("nrml"))
                score -= 100;

            if (text.Contains("_ao") || text.Contains("ambient") ||
                text.Contains("mask") || text.Contains("rough") || text.Contains("metal"))
                score -= 50;

            return score;
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
