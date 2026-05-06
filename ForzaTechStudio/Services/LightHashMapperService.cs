using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForzaTechStudio.Services
{
    // Maps light-preset IDs to .modelbin names by cross-referencing lights.bin attachments with lightpresets.bin V1/V2 data.
    public static class LightHashMapperService
    {
        private class JsonExportEntry
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            [JsonPropertyName("Hash")]
            public string Hash { get; set; }

            [JsonPropertyName("Name")]
            public string Name { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            [JsonPropertyName("Parameters")]
            public List<JsonParameterEntry> Parameters { get; set; }

            [JsonPropertyName("RawBytes")]
            public string RawBytes { get; set; }
        }

        private class JsonParameterEntry
        {
            [JsonPropertyName("Id")]
            public string Id { get; set; }

            [JsonPropertyName("Name")]
            public string Name { get; set; }

            [JsonPropertyName("Type")]
            public string Type { get; set; }

            [JsonPropertyName("Value")]
            public string Value { get; set; }
        }

        public class HashMapEntry
        {
            public uint Hash { get; set; }
            public uint Version { get; set; }

            // Final output name: "Preset_N_basename.modelbin" or "Preset_basename.modelbin".
            // Assigned by <see cref="BuildFinalNames"/> after deduplication.
            public string PresetFileName { get; set; } = string.Empty;

            // Filename-only part of the source attachment path (e.g. "chassis.modelbin").
            public string SourceBaseName { get; set; } = string.Empty;

            // Full attachment virtual path from lights.bin.
            public string FullModelPath { get; set; } = string.Empty;

            // The matched preset from lightpresets.bin (parameters + raw bytes).
            public LightPresetsBinParser.LightPreset Preset { get; set; }

            // Source zip file the data was read from.
            public string SourceZip { get; set; } = string.Empty;
        }

        // Produces hash→preset mappings for a single zip.
        // Both lights.bin and lightpresets.bin must be present and parseable.
        public static List<HashMapEntry> Map(
            Stream lightsBinStream,
            Stream presetsBinStream,
            string sourceZip)
        {
            var results = new List<HashMapEntry>();

            var lightsParser = new LightsBinParser();
            LightsBinParser.LightsBinData lightsData;
            try { lightsData = lightsParser.ParseToData(lightsBinStream); }
            catch (Exception ex)
            { throw new InvalidDataException($"Failed to parse lights.bin: {ex.Message}", ex); }

            var presetsParser = new LightPresetsBinParser();
            LightPresetsBinParser.LightPresetsData presetsData;
            try { presetsData = presetsParser.Parse(presetsBinStream); }
            catch (Exception ex)
            { throw new InvalidDataException($"Failed to parse lightpresets.bin: {ex.Message}", ex); }

            // Build hash → preset lookup (V2+ ID table)
            var presetByHash = new Dictionary<uint, LightPresetsBinParser.LightPreset>();
            if (presetsData.Version >= 2)
            {
                foreach (var preset in presetsData.Presets)
                {
                    if (preset.Id != 0)
                        presetByHash.TryAdd(preset.Id, preset);
                }
            }

            foreach (var light in lightsData.Groups)
            {
                LightPresetsBinParser.LightPreset matchedPreset = null;
                if (presetsData.Version >= 2)
                {
                    presetByHash.TryGetValue(light.Id, out matchedPreset);
                }
                else
                {
                    // V1 (FH3/FH4) - assume light.Id is the index in presets array
                    if (light.Id < (uint)presetsData.Presets.Count)
                        matchedPreset = presetsData.Presets[(int)light.Id];
                }

                if (matchedPreset == null)
                    continue;

                string fullPath = light.FullModelPath ?? string.Empty;
                if (string.IsNullOrEmpty(fullPath))
                    continue;

                // Filename only
                string baseName = Path.GetFileName(fullPath.Replace('\\', '/'));
                if (string.IsNullOrEmpty(baseName))
                    baseName = fullPath;

                results.Add(new HashMapEntry
                {
                    Hash = light.Id,
                    Version = presetsData.Version,
                    SourceBaseName = baseName,
                    FullModelPath = fullPath,
                    Preset = matchedPreset,
                    SourceZip = sourceZip
                });
            }

            return results;
        }

        // Deduplicates by (Hash, SourceBaseName, ParameterContent) and assigns the final
        // "Preset_[N_]basename.modelbin" names for V2 archives.
        public static List<HashMapEntry> Deduplicate(IEnumerable<HashMapEntry> entries)
        {
            // Collapse duplicates sharing same Hash, same modelbin FileName, and same Parameter content.
            // Using Base64 of RawBlockBytes as a reliable key for content comparison in the anonymous type.
            var deduped = entries
                .GroupBy(e => new
                {
                    e.Hash,
                    Content = e.Preset?.RawBlockBytes != null ? Convert.ToBase64String(e.Preset.RawBlockBytes) : string.Empty
                })
                .Select(g => g.First())
                .OrderBy(e => e.Hash)
                .ThenBy(e => e.SourceBaseName)
                .ToList();

            BuildFinalNames(deduped);
            return deduped;
        }

        // Deduplicates specifically for V1 archives. Skips entries with same raw parameter bytes.
        // Assigns sequential names: Preset_1, Preset_2, etc.
        public static List<HashMapEntry> DeduplicateV1(IEnumerable<HashMapEntry> entries)
        {
            var deduped = entries
                .GroupBy(e => e.Preset?.RawBlockBytes != null ? Convert.ToBase64String(e.Preset.RawBlockBytes) : string.Empty)
                .Select(g => g.First())
                .OrderBy(e => e.Hash) // Sort by original index/hash
                .ToList();

            for (int i = 0; i < deduped.Count; i++)
            {
                deduped[i].PresetFileName = $"Preset_{i + 1}";
            }

            return deduped;
        }

        private static void BuildFinalNames(List<HashMapEntry> entries)
        {
            var groups = entries
                .GroupBy(e => (e.Hash, e.SourceBaseName))
                .ToList();

            foreach (var group in groups)
            {
                var list = group.ToList();
                if (list.Count == 1)
                {
                    list[0].PresetFileName = $"Preset_{list[0].SourceBaseName}";
                }
                else
                {
                    for (int i = 0; i < list.Count; i++)
                        list[i].PresetFileName = $"Preset_{i + 1}_{list[i].SourceBaseName}";
                }
            }
        }

        // Serialises the full map to JSON with easy-to-read formatting.
        // Includes strongly-typed decoded parameters alongside the raw bytes.
        public static string ToJson(IEnumerable<HashMapEntry> entries)
        {
            var exportList = entries.Select(e =>
            {
                List<JsonParameterEntry> paramEntries = null;
                if (e.Preset?.Parameters is { Count: > 0 })
                {
                    paramEntries = e.Preset.Parameters.Select(p => new JsonParameterEntry
                    {
                        Id = $"0x{p.Id:X2}",
                        Name = p.Name,
                        Type = p.DataType.ToString(),
                        Value = p.DisplayValue
                    }).ToList();
                }

                return new JsonExportEntry
                {
                    Hash = e.Version >= 2 ? $"0x{e.Hash:X8}" : null,
                    Name = e.PresetFileName,
                    Parameters = paramEntries,
                    RawBytes = e.Preset?.RawBlockBytes != null
                        ? BitConverter.ToString(e.Preset.RawBlockBytes).Replace("-", " ")
                        : string.Empty
                };
            }).ToList();

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(exportList, options);
        }

        // Short summary line used for the UI results list.
        public static string ToDisplayLine(HashMapEntry e)
        {
            if (e.Version >= 2)
                return $"0x{e.Hash:X8} = {e.PresetFileName}";
            else
                return e.PresetFileName;
        }
    }
}
