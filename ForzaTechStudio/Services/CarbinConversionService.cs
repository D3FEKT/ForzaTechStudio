using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ForzaTools.CarScene;
using ForzaTools.Shared;

namespace ForzaTechStudio.Services;

// Converts carbin files between Forza game versions
public class CarbinConversionService
{
    private readonly record struct CarbinTargetProfile(
        ushort SceneVersion,
        ushort ModelVersion,
        GameSeries Series,
        ushort PartVersion,
        ushort UpgradablePartVersion,
        ushort UpgradeVersion)
    {
        public bool UsesSceneUnkV6 => Series == GameSeries.Horizon && SceneVersion >= 6;
        public bool UsesSceneUnkV7 => Series == GameSeries.Horizon && SceneVersion >= 7;
        public bool UsesRawPartType => Series == GameSeries.Motorsport && PartVersion >= 3;
        public bool UsesRawUpgradablePartType => Series == GameSeries.Motorsport && UpgradablePartVersion >= 4;
    }

    private readonly record struct CarRenderModelProfile(ushort Version, GameSeries Series)
    {
        public bool UsesWideMaterialIndexes => Version >= 21;
        public bool UsesLodDetails => Version >= 5;
        public bool UsesAOSwatchPath => Version < 9;
        public bool UsesAoMapInfos => Version >= 9;
        public bool UsesInteriorWindshield => Version >= 10;
        public bool UsesReceiverFlags => Version >= 11;
        public bool UsesAssemblyName => Version >= 12;
        public bool UsesGuidV13 => Version >= 13;
        public bool UsesDropGuidV14 => Version >= 14;
        public bool UsesHorizonUnkV15 => Series == GameSeries.Horizon && Version >= 15;
        public bool UsesDamageGuids => (Series == GameSeries.Motorsport && Version >= 15) || (Series == GameSeries.Horizon && Version >= 16);
        public bool UsesReceivesRain => Series == GameSeries.Motorsport && Version >= 16;
        public bool UsesHorizonId => Series == GameSeries.Horizon && Version >= 17;
        public bool UsesProxyLodId => Series == GameSeries.Motorsport && Version >= 17;
        public bool UsesMotorsportUnkV18 => Series == GameSeries.Motorsport && Version >= 18;
        public bool UsesHorizonUnkV18 => Series == GameSeries.Horizon && Version >= 18;
        public bool UsesHorizonV21Tail => Series == GameSeries.Horizon && Version >= 21;
        public bool UsesMotorsportUnkV19 => Series == GameSeries.Motorsport && Version >= 19;
        public bool UsesMotorsportV20Flags => Series == GameSeries.Motorsport && Version >= 20;
    }

    // Converts a carbin file to the specified target format
    public ConversionResult ConvertCarbin(string inputPath, string outputPath, ForzaGameTarget target, ConversionOptions? options = null)
    {
        var result = new ConversionResult { OutputPath = outputPath };

        try
        {
            result.Log.Add($"Loading carbin: {Path.GetFileName(inputPath)}");

            using var fs = File.OpenRead(inputPath);
            var carbinFile = new CarbinFile();
            carbinFile.Load(fs);

            var scene = carbinFile.Scene;
            var srcSeries = scene.Series;
            result.Log.Add($"Scene loaded: v{scene.Version}, Series: {scene.Series}, " +
                           $"{scene.NonUpgradableParts.Count} parts, " +
                           $"{scene.UpgradableParts.Count} upgrade parts");

            var targetProfile = GetTargetProfile(target);

            result.Log.Add($"Target: {target} -> Scene v{targetProfile.SceneVersion}, Model v{targetProfile.ModelVersion}, Series: {targetProfile.Series}");

            ApplySceneTarget(scene, targetProfile);

            byte modelIdCounter = 0;

            foreach (var entry in scene.NonUpgradableParts)
                ConvertNonUpgradablePart(entry, srcSeries, targetProfile, ref modelIdCounter, result);

            foreach (var part in scene.UpgradableParts)
                ConvertUpgradablePart(part, srcSeries, targetProfile, ref modelIdCounter, result);

            result.Log.Add("Validating converted carbin structure...");

            using var outStream = new MemoryStream();
            carbinFile.Save(outStream);
            byte[] outputBytes = outStream.ToArray();

            if (!ValidateConvertedCarbin(outputBytes, targetProfile, result))
            {
                result.Success = false;
                return result;
            }

            result.Log.Add($"Writing converted carbin to: {Path.GetFileName(outputPath)}");
            File.WriteAllBytes(outputPath, outputBytes);

            result.Success = true;
            result.Log.Add("Conversion completed successfully.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Conversion failed: {ex.Message}");
        }

        return result;
    }

    private static CarbinTargetProfile GetTargetProfile(ForzaGameTarget target)
    {
        var (sceneVer, modelVer, series, partVer, upgPartVer, upgradeVer) = GetCarbinTargetVersions(target);
        return new CarbinTargetProfile(sceneVer, modelVer, series, partVer, upgPartVer, upgradeVer);
    }

    private static void ApplySceneTarget(Scene scene, CarbinTargetProfile targetProfile)
    {
        scene.Version = targetProfile.SceneVersion;
        scene.Series = targetProfile.Series;
        scene.SeriesIsWeak = false;
        scene.UnkV6 = targetProfile.UsesSceneUnkV6;
        scene.UnkV7 = targetProfile.UsesSceneUnkV7;

        if (scene.Version >= 3 && scene.BuildGuid == Guid.Empty)
            scene.BuildGuid = Guid.NewGuid();

        if (scene.Version >= 2 && scene.LODDetails.Value == 0)
            scene.LODDetails = new LODFlags(0x7E);

        scene.MediaName ??= string.Empty;
        scene.SkeletonPath ??= string.Empty;
    }

    private static void ConvertNonUpgradablePart(
        PartEntry entry,
        GameSeries sourceSceneSeries,
        CarbinTargetProfile targetProfile,
        ref byte modelIdCounter,
        ConversionResult result)
    {
        var normalizedType = NormalizePartTypeForTarget(entry.Type, targetProfile.UsesRawPartType, "part", result);
        entry.Type = normalizedType;
        entry.Part.Type = normalizedType;
        entry.Part.Version = targetProfile.PartVersion;
        entry.Part.Bounds = EnsureBounds(entry.Part.Bounds, targetProfile.PartVersion >= 2);

        foreach (var model in entry.Part.Models)
        {
            ConvertCarRenderModel(model, sourceSceneSeries, targetProfile.ModelVersion, targetProfile.Series, ref modelIdCounter, result);
        }
    }

    private static void ConvertUpgradablePart(
        UpgradablePart part,
        GameSeries sourceSceneSeries,
        CarbinTargetProfile targetProfile,
        ref byte modelIdCounter,
        ConversionResult result)
    {
        part.Type = NormalizePartTypeForTarget(part.Type, targetProfile.UsesRawUpgradablePartType, "upgrade part", result);

        MigrateUpgradeModels(part, targetProfile.UpgradeVersion, result);

        part.Version = targetProfile.UpgradablePartVersion;

        foreach (var upgrade in part.Upgrades)
        {
            upgrade.Version = targetProfile.UpgradeVersion;
            upgrade.Bounds = EnsureBounds(upgrade.Bounds, targetProfile.UpgradeVersion >= 2);

            foreach (var model in upgrade.Models)
            {
                ConvertCarRenderModel(model, sourceSceneSeries, targetProfile.ModelVersion, targetProfile.Series, ref modelIdCounter, result);
            }
        }

        foreach (var sharedModel in part.SharedModels)
        {
            ConvertCarRenderModel(sharedModel.Model, sourceSceneSeries, targetProfile.ModelVersion, targetProfile.Series, ref modelIdCounter, result);
        }
    }

    private static CCarParts NormalizePartTypeForTarget(CCarParts partType, bool targetSupportsRawType, string context, ConversionResult result)
    {
        if (partType == CCarParts.Ballast && !targetSupportsRawType)
        {
            result.Warnings.Add($"  Unsupported {context} type Ballast (42) remapped to MotorParts for target compatibility.");
            return CCarParts.MotorParts;
        }

        return partType;
    }

    private static AABB EnsureBounds(AABB bounds, bool required)
    {
        if (!required)
            return bounds;

        return bounds.Min == Vector4.Zero && bounds.Max == Vector4.Zero
            ? new AABB()
            : bounds;
    }

    private static bool ValidateConvertedCarbin(
        byte[] outputBytes,
        CarbinTargetProfile targetProfile,
        ConversionResult result)
    {
        try
        {
            using var validateStream = new MemoryStream(outputBytes, writable: false);
            var validateFile = new CarbinFile();
            validateFile.Load(validateStream);

            var scene = validateFile.Scene;
            if (scene.Version != targetProfile.SceneVersion)
                throw new InvalidDataException($"Round-trip validation failed: expected Scene v{targetProfile.SceneVersion}, got v{scene.Version}");
            if (scene.Series != targetProfile.Series)
                throw new InvalidDataException($"Round-trip validation failed: expected series {targetProfile.Series}, got {scene.Series}");

            var targetModelProfile = new CarRenderModelProfile(targetProfile.ModelVersion, targetProfile.Series);
            var seenHorizonIds = targetModelProfile.UsesHorizonId ? new HashSet<byte>() : null;

            foreach (var entry in scene.NonUpgradableParts)
            {
                if (entry.Part.Version != targetProfile.PartVersion)
                    throw new InvalidDataException($"Part '{entry.Type}' wrote version {entry.Part.Version}, expected {targetProfile.PartVersion}");

                foreach (var model in entry.Part.Models)
                    ValidateConvertedModel(model, targetModelProfile, seenHorizonIds, GetModelDisplayName(model));
            }

            foreach (var part in scene.UpgradableParts)
            {
                if (part.Version != targetProfile.UpgradablePartVersion)
                    throw new InvalidDataException($"Upgrade part '{part.Type}' wrote version {part.Version}, expected {targetProfile.UpgradablePartVersion}");

                if (targetProfile.UpgradeVersion >= 3 && part.Upgrades.Any(u => u.Models.Count > 0))
                    throw new InvalidDataException($"Upgrade part '{part.Type}' still has inline Upgrade.Models after migrating to v{targetProfile.UpgradeVersion}");

                if (targetProfile.UpgradeVersion < 3 && part.SharedModels.Count > 0)
                    throw new InvalidDataException($"Upgrade part '{part.Type}' still has SharedModels after downgrading to upgrade v{targetProfile.UpgradeVersion}");

                foreach (var upg in part.Upgrades)
                {
                    if (upg.Version != targetProfile.UpgradeVersion)
                        throw new InvalidDataException($"Upgrade '{upg.Id}' wrote version {upg.Version}, expected {targetProfile.UpgradeVersion}");

                    foreach (var model in upg.Models)
                        ValidateConvertedModel(model, targetModelProfile, seenHorizonIds, GetModelDisplayName(model));
                }

                foreach (var shared in part.SharedModels)
                    ValidateConvertedModel(shared.Model, targetModelProfile, seenHorizonIds, GetModelDisplayName(shared.Model));
            }

            result.Log.Add($"Validation passed: {outputBytes.Length:N0} bytes reload cleanly as Scene v{scene.Version}, {scene.Series}");
            return true;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Structural validation failed: {ex.Message}");
            return false;
        }
    }

    private static void ValidateConvertedModel(
        CarRenderModel model,
        CarRenderModelProfile targetProfile,
        HashSet<byte>? seenHorizonIds,
        string modelName)
    {
        if (model.Version != targetProfile.Version)
            throw new InvalidDataException($"Model '{modelName}' wrote version {model.Version}, expected {targetProfile.Version}");

        if (!targetProfile.UsesWideMaterialIndexes && model.MaterialIndexes.Any(item => item.Value > uint.MaxValue))
            throw new InvalidDataException($"Model '{modelName}' retained 64-bit material index values after converting to v{targetProfile.Version}.");

        if (targetProfile.UsesHorizonId && seenHorizonIds != null && !seenHorizonIds.Add(model.HorizonId))
            throw new InvalidDataException($"Model '{modelName}' wrote duplicate Horizon model ID {model.HorizonId}.");
    }

    private static string GetModelDisplayName(CarRenderModel model)
    {
        string fileName = Path.GetFileName(model.Path ?? string.Empty);
        return string.IsNullOrWhiteSpace(fileName) ? "<unnamed>" : fileName;
    }

    // Migrates inline models to/from SharedModels across the Upgrade v3 boundary
    private static void MigrateUpgradeModels(UpgradablePart part, ushort targetUpgradeVer, ConversionResult result)
    {
        bool anyInlineModels = part.Upgrades.Any(u => u.Models.Count > 0);
        bool anySharedModels = part.SharedModels.Count > 0;

        if (anyInlineModels && anySharedModels)
        {
            throw new InvalidDataException($"Upgrade part '{part.Type}' mixes inline Upgrade.Models and SharedModels; conversion cannot safely continue.");
        }

        if (targetUpgradeVer >= 3 && anyInlineModels)
        {
            // Upgrading: move inline models -> SharedModels
            int migratedCount = 0;
            foreach (var upg in part.Upgrades)
            {
                if (upg.Models.Count == 0) continue;

                foreach (var model in upg.Models)
                {
                    var shared = new SharedCarModel
                    {
                        UpgradeIds = new List<int> { upg.Id },
                        Model = model
                    };
                    part.SharedModels.Add(shared);
                    migratedCount++;
                }
                upg.Models.Clear();
            }

            if (migratedCount > 0)
                result.Log.Add($"  Migrated {migratedCount} inline model(s) to SharedModels (Upgrade v<3 -> v>={targetUpgradeVer})");
        }
        else if (targetUpgradeVer < 3 && anySharedModels && !anyInlineModels)
        {
            // Downgrading: move SharedModels -> inline models per upgrade
            int migratedCount = 0;
            foreach (var shared in part.SharedModels)
            {
                if (shared.UpgradeIds.Count > 0)
                {
                    foreach (int upgradeId in shared.UpgradeIds)
                    {
                        var targetUpg = part.Upgrades.FirstOrDefault(u => u.Id == upgradeId);
                        if (targetUpg != null)
                        {
                            targetUpg.Models.Add(CloneCarRenderModel(shared.Model));
                            migratedCount++;
                        }
                    }
                }
                else
                {
                    // No upgrade ID association — assign to all upgrades
                    foreach (var upg in part.Upgrades)
                    {
                        upg.Models.Add(CloneCarRenderModel(shared.Model));
                        migratedCount++;
                    }
                }
            }
            part.SharedModels.Clear();

            if (migratedCount > 0)
                result.Log.Add($"  Migrated {migratedCount} shared model(s) to inline Upgrade.Models (Upgrade v>=3 -> v<3)");
        }
    }

    private static CarRenderModel CloneCarRenderModel(CarRenderModel model)
    {
        return new CarRenderModel
        {
            Version = model.Version,
            Path = model.Path,
            Transform = model.Transform,
            LODDetails = model.LODDetails,
            BoneName = model.BoneName,
            BoneId = model.BoneId,
            SnapToParent = model.SnapToParent,
            DrawGroups = model.DrawGroups,
            AOSwatchPath = model.AOSwatchPath,
            MaterialOverrides = model.MaterialOverrides.ToDictionary(
                pair => pair.Key,
                pair => pair.Value?.ToArray() ?? Array.Empty<byte>(),
                StringComparer.Ordinal),
            MaterialIndexes = model.MaterialIndexes
                .Select(item => new MaterialIndexEntry { Key = item.Key, Value = item.Value })
                .ToList(),
            IsDroppable = model.IsDroppable,
            DropValue = model.DropValue,
            DropPartId = model.DropPartId,
            BreakAmount = model.BreakAmount,
            AOMapInfos = model.AOMapInfos.Select(CloneAoMapInfo).ToList(),
            IsInteriorWindshield = model.IsInteriorWindshield,
            ReceivesImpact = model.ReceivesImpact,
            ReceivesSplatter = model.ReceivesSplatter,
            ReceivesDamage = model.ReceivesDamage,
            ReceivesDirt = model.ReceivesDirt,
            ReceivesOil = model.ReceivesOil,
            ReceivesRubber = model.ReceivesRubber,
            AssemblyName = model.AssemblyName,
            GuidV13 = model.GuidV13,
            DropGuidV14 = model.DropGuidV14,
            AOMapInfoIdV14 = model.AOMapInfoIdV14,
            DamageGuids = [.. model.DamageGuids],
            ReceivesRain = model.ReceivesRain,
            ProxyLodId = model.ProxyLodId,
            MotorsportUnkV18 = model.MotorsportUnkV18,
            MotorsportUnkV19 = model.MotorsportUnkV19,
            IsInterior = model.IsInterior,
            IsLeftSideWindow = model.IsLeftSideWindow,
            IsRightSideWindow = model.IsRightSideWindow,
            IsNascarWiper = model.IsNascarWiper,
            IsLicensePlate = model.IsLicensePlate,
            HorizonUnkV15 = model.HorizonUnkV15,
            HorizonId = model.HorizonId,
            HorizonUnkV18 = model.HorizonUnkV18,
            HorizonUnkV21Flag = model.HorizonUnkV21Flag,
            HorizonUnkV21Path = model.HorizonUnkV21Path,
            RawDrawGroupsValue = model.RawDrawGroupsValue,
        };
    }

    private static AOMapInfo CloneAoMapInfo(AOMapInfo aoMapInfo)
    {
        return new AOMapInfo
        {
            Version = aoMapInfo.Version,
            Path = aoMapInfo.Path,
            PartType = aoMapInfo.PartType,
            PartId = aoMapInfo.PartId,
            DroppedModelInstanceGuid = aoMapInfo.DroppedModelInstanceGuid,
            IsDefault = aoMapInfo.IsDefault,
            LodTest = aoMapInfo.LodTest,
            LodValue = aoMapInfo.LodValue,
        };
    }

    #region Version Tables

    // Returns version tuple (scene, model, series, part, upgPart, upgrade) for the target game
    public static (
        ushort sceneVer,
        ushort modelVer,
        GameSeries series,
        ushort partVer,
        ushort upgPartVer,
        ushort upgradeVer
    ) GetCarbinTargetVersions(ForzaGameTarget target)
    {
        //                          scene  model  series                    part  upgPart upgrade
        return target switch
        {
            ForzaGameTarget.FM5     => (5,  14, GameSeries.Motorsport, 2, 3, 3),
            ForzaGameTarget.FM6
            or ForzaGameTarget.FM7  => (5,  17, GameSeries.Motorsport, 2, 3, 3),
            ForzaGameTarget.FM2023  => (10, 21, GameSeries.Motorsport, 3, 4, 4),
            ForzaGameTarget.FH2     => (5,  15, GameSeries.Horizon,    2, 3, 3),
            ForzaGameTarget.FH3
            or ForzaGameTarget.FH4  => (5,  16, GameSeries.Horizon,    2, 3, 3),
            ForzaGameTarget.FH5     => (6,  18, GameSeries.Horizon,    2, 3, 3),
            ForzaGameTarget.FH6     => (7,  21, GameSeries.Horizon,    2, 3, 3),
            _                       => (6,  18, GameSeries.Horizon,    2, 3, 3)
        };
    }

    #endregion

    #region Carbin Model Converter

    private static void ConvertCarRenderModel(CarRenderModel model, GameSeries srcSeries, ushort targetVersion, GameSeries targetSeries,
        ref byte idCounter, ConversionResult result)
    {
        ushort srcVersion = model.Version;
        GameSeries sourceModelSeries = ResolveModelSourceSeries(srcSeries, model);
        var sourceProfile = new CarRenderModelProfile(srcVersion, sourceModelSeries);
        var targetProfile = new CarRenderModelProfile(targetVersion, targetSeries);

        model.Version = targetVersion;
        model.Path ??= string.Empty;
        model.BoneName ??= string.Empty;
        model.RawDrawGroupsValue = model.DrawGroups.Value;

        ClearUnsupportedModelFields(model, targetProfile);
        NormalizeMaterialIndexes(model, sourceProfile, targetProfile, result);
        ApplyTargetModelDefaults(model, sourceProfile, targetProfile, ref idCounter);
        NormalizeAoMapInfos(model, sourceProfile, targetProfile);

        result.Log.Add($"  Model '{GetModelDisplayName(model)}': v{srcVersion} -> v{targetVersion}{BuildSeriesTransitionSuffix(sourceProfile.Series, targetProfile.Series)}");
    }

    private static GameSeries ResolveModelSourceSeries(GameSeries sourceSceneSeries, CarRenderModel model)
    {
        if (sourceSceneSeries != GameSeries.Auto)
            return sourceSceneSeries;

        return IsMotorsportVersion(model.Version, model)
            ? GameSeries.Motorsport
            : GameSeries.Horizon;
    }

    private static void ClearUnsupportedModelFields(CarRenderModel model, CarRenderModelProfile targetProfile)
    {
        if (!targetProfile.UsesAOSwatchPath)
            model.AOSwatchPath = null;

        if (!targetProfile.UsesDamageGuids)
            model.DamageGuids.Clear();

        if (!targetProfile.UsesReceivesRain)
            model.ReceivesRain = 0;

        if (!targetProfile.UsesProxyLodId)
            model.ProxyLodId = 0;

        if (!targetProfile.UsesMotorsportUnkV18)
            model.MotorsportUnkV18 = null;

        if (!targetProfile.UsesMotorsportUnkV19)
            model.MotorsportUnkV19 = null;

        if (!targetProfile.UsesMotorsportV20Flags)
        {
            model.IsInterior = false;
            model.IsLeftSideWindow = 0;
            model.IsRightSideWindow = 0;
            model.IsNascarWiper = false;
            model.IsLicensePlate = false;
        }

        if (!targetProfile.UsesHorizonUnkV15)
            model.HorizonUnkV15 = 0;

        if (!targetProfile.UsesHorizonId)
            model.HorizonId = 0;

        if (!targetProfile.UsesHorizonUnkV18)
            model.HorizonUnkV18 = 0;

        if (!targetProfile.UsesHorizonV21Tail)
        {
            model.HorizonUnkV21Flag = 0;
            model.HorizonUnkV21Path = null;
        }

        if (!targetProfile.UsesAssemblyName)
            model.AssemblyName = null;
    }

    private static void NormalizeMaterialIndexes(
        CarRenderModel model,
        CarRenderModelProfile sourceProfile,
        CarRenderModelProfile targetProfile,
        ConversionResult result)
    {
        foreach (var materialIndex in model.MaterialIndexes)
        {
            if (!sourceProfile.UsesWideMaterialIndexes)
            {
                ulong widenedValue = materialIndex.Value & uint.MaxValue;
                if (materialIndex.Value != widenedValue)
                {
                    result.Log.Add($"    MaterialIndex '{materialIndex.Key}' normalized from 0x{materialIndex.Value:X} to 0x{widenedValue:X8} for 32-bit source compatibility.");
                    materialIndex.Value = widenedValue;
                }
            }

            if (targetProfile.UsesWideMaterialIndexes)
                continue;

            if (materialIndex.Value > uint.MaxValue)
            {
                ulong truncatedValue = materialIndex.Value & uint.MaxValue;
                result.Warnings.Add($"  MaterialIndex '{materialIndex.Key}' truncated from 0x{materialIndex.Value:X} to 0x{truncatedValue:X8} for target compatibility.");
                materialIndex.Value = truncatedValue;
            }
            else
            {
                materialIndex.Value &= uint.MaxValue;
            }
        }
    }

    private static void ApplyTargetModelDefaults(
        CarRenderModel model,
        CarRenderModelProfile sourceProfile,
        CarRenderModelProfile targetProfile,
        ref byte idCounter)
    {
        if (targetProfile.UsesLodDetails && model.LODDetails.Value == 0)
            model.LODDetails = new LODFlags(0x7E);

        if (targetProfile.UsesGuidV13 && model.GuidV13 == Guid.Empty)
            model.GuidV13 = Guid.NewGuid();

        if (targetProfile.UsesDropGuidV14 && model.DropGuidV14 == Guid.Empty)
            model.DropGuidV14 = Guid.Empty;

        if (targetProfile.UsesReceiverFlags && !sourceProfile.UsesReceiverFlags)
        {
            if (model.ReceivesDamage == 0)
                model.ReceivesDamage = 1;
            if (model.ReceivesDirt == 0)
                model.ReceivesDirt = 1;
        }

        if (targetProfile.UsesAssemblyName)
            model.AssemblyName ??= string.Empty;

        if (targetProfile.UsesHorizonUnkV15 && (!sourceProfile.UsesHorizonUnkV15 || sourceProfile.Series != GameSeries.Horizon))
            model.HorizonUnkV15 = 1;

        if (targetProfile.UsesHorizonId)
            model.HorizonId = idCounter++;

        if (targetProfile.UsesHorizonUnkV18 && (!sourceProfile.UsesHorizonUnkV18 || sourceProfile.Series != GameSeries.Horizon))
            model.HorizonUnkV18 = 1;

        if (targetProfile.UsesHorizonV21Tail)
        {
            if (!sourceProfile.UsesHorizonV21Tail || sourceProfile.Series != GameSeries.Horizon)
                model.HorizonUnkV21Flag = 0;

            model.HorizonUnkV21Path ??= string.Empty;
        }

        if (targetProfile.UsesMotorsportUnkV18)
            model.MotorsportUnkV18 ??= string.Empty;

        if (targetProfile.UsesMotorsportUnkV19)
            model.MotorsportUnkV19 ??= string.Empty;
    }

    private static void NormalizeAoMapInfos(
        CarRenderModel model,
        CarRenderModelProfile sourceProfile,
        CarRenderModelProfile targetProfile)
    {
        if (targetProfile.UsesAoMapInfos)
        {
            foreach (var aoMapInfo in model.AOMapInfos)
            {
                if (aoMapInfo.Version < 2 && aoMapInfo.DroppedModelInstanceGuid == Guid.Empty)
                    aoMapInfo.DroppedModelInstanceGuid = Guid.Empty;

                aoMapInfo.Version = 3;

                if (sourceProfile.Series == GameSeries.Motorsport && targetProfile.Series == GameSeries.Horizon)
                    aoMapInfo.LodValue = 0x1F;
            }

            if (!sourceProfile.UsesAoMapInfos && model.AOMapInfos.Count == 0 && !string.IsNullOrEmpty(model.AOSwatchPath))
            {
                model.AOMapInfos.Add(new AOMapInfo
                {
                    Version = 3,
                    Path = model.AOSwatchPath,
                    PartType = CCarParts.CarBody,
                    PartId = -1,
                    DroppedModelInstanceGuid = Guid.Empty,
                    IsDefault = true,
                    LodTest = 0,
                    LodValue = (sbyte)(sourceProfile.Series == GameSeries.Motorsport && targetProfile.Series == GameSeries.Horizon ? 0x1F : 0)
                });
            }
        }
        else if (string.IsNullOrEmpty(model.AOSwatchPath) && model.AOMapInfos.Count > 0)
        {
            model.AOSwatchPath = model.AOMapInfos[0].Path;
        }
    }

    private static string BuildSeriesTransitionSuffix(GameSeries sourceSeries, GameSeries targetSeries)
    {
        if (sourceSeries == targetSeries)
            return string.Empty;

        return $" ({sourceSeries}->{targetSeries})";
    }

    // Detects if a model belongs to Motorsport when scene series is ambiguous
    private static bool IsMotorsportVersion(ushort version, CarRenderModel model)
    {
        return version switch
        {
            // Unambiguously Motorsport versions
            17 or 20 => true,
            21 => model.HorizonUnkV21Path is null,
            // v18: Motorsport if MotorsportUnkV18 string field is present (non-null after parsing)
            18 => model.MotorsportUnkV18 != null,
            // v19: same logic
            19 => model.MotorsportUnkV18 != null,
            // v14, v15, v16 could be either; v15/v16 default Horizon, v14 default Motorsport
            14 => true,
            15 or 16 => false,
            _ => false
        };
    }

    #endregion
}
