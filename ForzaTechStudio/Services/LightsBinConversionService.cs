using System;
using System.Collections.Generic;
using System.IO;

namespace ForzaTechStudio.Services
{
    // Converts lights.bin between Forza versions (v0/1=FH, v2=FM5-7, v3=FM2023), adding/stripping PresetName and GUID as needed.
    public class LightsBinConversionService
    {
        // Maps a ForzaGameTarget to the lights.bin version it uses.
        public static uint GetTargetLightsVersion(ForzaGameTarget target) => target switch
        {
            ForzaGameTarget.FH3 or ForzaGameTarget.FH4 or ForzaGameTarget.FH5 => 1u,
            ForzaGameTarget.FM5 or ForzaGameTarget.FM6 or ForzaGameTarget.FM7 => 2u,
            ForzaGameTarget.FM2023 => 3u,
            _ => 1u
        };

        // Human-readable version label shown in logs / UI.
        public static string GetVersionLabel(uint version) => version switch
        {
            0 or 1 => "v1 (Horizon)",
            2 => "v2 (Motorsport 5/6/7)",
            3 => "v3 (FM2023)",
            _ => $"v{version}"
        };

        // All targets that lights.bin conversion supports.
        public static IReadOnlyList<ForzaGameTarget> SupportedTargets =>
        [
            ForzaGameTarget.FH3,
            ForzaGameTarget.FH4,
            ForzaGameTarget.FH5,
            ForzaGameTarget.FM5,
            ForzaGameTarget.FM6,
            ForzaGameTarget.FM7,
            ForzaGameTarget.FM2023,
        ];

        // Analyzes a lights.bin stream (does not consume it; resets position after reading).
        public LightsBinAnalysisResult Analyze(Stream stream)
        {
            long originalPos = stream.Position;
            try
            {
                var parser = new LightsBinParser();
                var data = parser.ParseToData(stream);

                return new LightsBinAnalysisResult
                {
                    IsValid = true,
                    Version = (uint)data.Version,
                    LightCount = data.Groups.Count,
                    AttachmentCount = data.Models.Count,
                    LodOverrideCount = data.LODOverrides.Count,
                    DetectedGame = GetGameFromVersion((uint)data.Version)
                };
            }
            catch (Exception ex)
            {
                return new LightsBinAnalysisResult
                {
                    IsValid = false,
                    Error = ex.Message
                };
            }
            finally
            {
                stream.Position = originalPos;
            }
        }

        // Converts lights.bin data in <paramref name="inputStream"/> to the given target and
        // writes the result to <paramref name="outputStream"/>.
        // Returns a log of changes made.
        public List<string> Convert(Stream inputStream, Stream outputStream, ForzaGameTarget target)
        {
            var log = new List<string>();

            var parser = new LightsBinParser();
            var data = parser.ParseToData(inputStream);

            uint sourceVersion = (uint)data.Version;
            uint targetVersion = GetTargetLightsVersion(target);

            log.Add($"Source: {GetVersionLabel(sourceVersion)} ({data.Groups.Count} lights, {data.Models.Count} attachments)");
            log.Add($"Target: {GetVersionLabel(targetVersion)} ({ConversionService.GetGameName(target)})");
            log.Add("");

            if (sourceVersion == targetVersion)
                log.Add("Source and target version are the same � data will be re-serialised unchanged.");

            data.Version = targetVersion;

            // ?? Per-light field changes ??????????????????????????????????????
            int presetAdded   = 0;
            int presetRemoved = 0;
            int guidAdded     = 0;
            int guidRemoved   = 0;

            foreach (var light in data.Groups)
            {
                if (targetVersion >= 2)
                {
                    // Ensure every light has a preset name.
                    if (string.IsNullOrEmpty(light.PresetName))
                    {
                        light.PresetName = $"preset_{light.Id:X8}";
                        presetAdded++;
                    }
                }
                else
                {
                    // Strip preset name when downgrading to v1.
                    if (!string.IsNullOrEmpty(light.PresetName))
                    {
                        light.PresetName = null;
                        presetRemoved++;
                    }
                }

                if (targetVersion >= 3)
                {
                    // Ensure every light has a GUID.
                    if (light.V3Guid == Guid.Empty)
                    {
                        light.V3Guid = Guid.NewGuid();
                        guidAdded++;
                    }
                }
                else
                {
                    // Strip GUID when target is v1 or v2.
                    if (light.V3Guid != Guid.Empty)
                    {
                        light.V3Guid = Guid.Empty;
                        guidRemoved++;
                    }
                }
            }

            if (presetAdded   > 0) log.Add($"  + Added placeholder preset names for {presetAdded} light(s)");
            if (presetRemoved > 0) log.Add($"  - Removed preset names from {presetRemoved} light(s) (not used in v1)");
            if (guidAdded     > 0) log.Add($"  + Generated random GUIDs for {guidAdded} light(s)");
            if (guidRemoved   > 0) log.Add($"  - Removed GUIDs from {guidRemoved} light(s) (not used in v1/v2)");

            log.Add("");
            log.Add($"Writing {data.Groups.Count} light(s) as {GetVersionLabel(targetVersion)}...");

            parser.Serialize(outputStream, data);

            log.Add("? Conversion complete.");
            return log;
        }

        private static string GetGameFromVersion(uint version) => version switch
        {
            0 or 1 => "Forza Horizon (v1)",
            2 => "Motorsport 5/6/7 (v2)",
            3 => "Forza Motorsport 2023 (v3)",
            _ => $"Unknown (v{version})"
        };
    }

    // Result returned by <see cref="LightsBinConversionService.Analyze"/>.
    public class LightsBinAnalysisResult
    {
        public bool IsValid { get; set; }
        public string Error { get; set; }
        public uint Version { get; set; }
        public int LightCount { get; set; }
        public int AttachmentCount { get; set; }
        public int LodOverrideCount { get; set; }
        public string DetectedGame { get; set; }
    }
}
