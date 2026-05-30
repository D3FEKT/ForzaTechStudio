using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ForzaTechStudio.ViewModels
{
    public partial class CarbinEditorViewModel
    {
        private void ParseCarbinFile(BinaryReader reader, long fileLength)
        {
            _lastParsingContext = "Clearing collections";
            _lastFilePosition = reader.BaseStream.Position;

            NonUpgradableParts.Clear();
            UpgradableParts.Clear();

            // Read Scene version
            _lastParsingContext = "Reading scene version";
            _lastFilePosition = reader.BaseStream.Position;
            ushort sceneVersion = reader.ReadUInt16();

            if (sceneVersion == 0 || sceneVersion > 20)
            {
                throw new InvalidDataException($"Invalid scene version: {sceneVersion}. Expected 5, 6, 7, 10, or 11.");
            }

            DetectedSceneVersion = sceneVersion;

            // Determine series based on scene version (heuristic from .bt)
            IsHorizon = GetSceneSeriesIsHorizon(sceneVersion);

            // Build GUID (version >= 3)
            Guid buildGuid = Guid.Empty;
            if (sceneVersion >= 3)
            {
                _lastParsingContext = "Reading build GUID";
                _lastFilePosition = reader.BaseStream.Position;
                buildGuid = new Guid(reader.ReadBytes(16));
            }

            // Build Strict (version >= 5)
            bool buildStrict = false;
            if (sceneVersion >= 5)
            {
                _lastParsingContext = "Reading build strict";
                _lastFilePosition = reader.BaseStream.Position;
                buildStrict = reader.ReadByte() != 0;
            }

            // Ordinal
            _lastParsingContext = "Reading ordinal";
            _lastFilePosition = reader.BaseStream.Position;
            uint ordinal = reader.ReadUInt32();

            // Media Name
            _lastParsingContext = "Reading media name";
            _lastFilePosition = reader.BaseStream.Position;
            string mediaName = ReadString(reader);

            // Skeleton Path
            _lastParsingContext = "Reading skeleton path";
            _lastFilePosition = reader.BaseStream.Position;
            string skeletonPath = ReadString(reader);

            // LOD Flags (version >= 2)
            ushort lodFlags = 0x7E;
            if (sceneVersion >= 2)
            {
                _lastParsingContext = "Reading LOD flags";
                _lastFilePosition = reader.BaseStream.Position;
                lodFlags = reader.ReadUInt16();
            }

            string sceneName = ExtractSceneNameFromPath(skeletonPath);

            // Non-Upgradable Parts
            _lastParsingContext = "Reading non-upgradable parts count";
            _lastFilePosition = reader.BaseStream.Position;
            uint nonUpgradablePartsCount = reader.ReadUInt32();

            for (int i = 0; i < nonUpgradablePartsCount; i++)
            {
                _lastParsingContext = $"Reading non-upgradable part {i}";
                _lastFilePosition = reader.BaseStream.Position;

                // scene version >= 4 has a part type byte prefix
                // For Motorsport scene >= 6, this byte uses CCarParts_Enum directly.
                // For older versions, this byte uses CCarParts_EnumV1 (needs +1 offset for >= 42).
                CCarPartsEnum? partTypeHint = null;
                if (sceneVersion >= 4)
                {
                    byte partTypeByte = reader.ReadByte();
                    uint rawType = partTypeByte;
                    if (!IsHorizon && sceneVersion >= 6)
                    {
                        // FM2023+: CCarParts_Enum directly
                        partTypeHint = (CCarPartsEnum)rawType;
                    }
                    else
                    {
                        // Older: CCarParts_EnumV1 ? values >= 42 need +1
                        partTypeHint = (CCarPartsEnum)ConvertEnumV1ToLatest(rawType);
                    }
                }

                var partEntry = ParsePart(reader, sceneVersion, i, partTypeHint);
                NonUpgradableParts.Add(partEntry);
            }

            // Upgradable Parts
            _lastParsingContext = "Reading upgradable parts count";
            _lastFilePosition = reader.BaseStream.Position;
            uint upgradablePartsCount = reader.ReadUInt32();

            for (int i = 0; i < upgradablePartsCount; i++)
            {
                _lastParsingContext = $"Parsing upgradable part {i}";
                _lastFilePosition = reader.BaseStream.Position;

                var partEntry = ParseUpgradablePart(reader, sceneVersion, i);

                // Sync wrappers for UI binding
                PopulateUpgradeIdWrappers(partEntry);

                UpgradableParts.Add(partEntry);
            }

            // Horizon scene trailer bytes
            if (IsHorizon && sceneVersion >= 6 && reader.BaseStream.Position < fileLength)
            {
                _sceneUnkV6 = reader.ReadByte() != 0;
            }

            if (IsHorizon && sceneVersion >= 7 && reader.BaseStream.Position < fileLength)
            {
                _sceneUnkV7 = reader.ReadByte() != 0;
            }

            // Update UI properties
            SceneName = sceneName;
            MediaName = mediaName;
            SkeletonPath = skeletonPath;
            Ordinal = ordinal;
            BuildStrict = buildStrict;
            BuildGuid = buildGuid;

            LodFlagLODS = (lodFlags & 0x01) != 0;
            LodFlagLOD0 = (lodFlags & 0x02) != 0;
            LodFlagLOD1 = (lodFlags & 0x04) != 0;
            LodFlagLOD2 = (lodFlags & 0x08) != 0;
            LodFlagLOD3 = (lodFlags & 0x10) != 0;
            LodFlagLOD4 = (lodFlags & 0x20) != 0;
            LodFlagLOD5 = (lodFlags & 0x40) != 0;

            SelectedVersionIndex = DetermineVersionIndex(sceneVersion, DetectedModelVersion, IsHorizon);
            SelectedNonUpgradablePart = NonUpgradableParts.FirstOrDefault();
            SelectedUpgradablePart = UpgradableParts.FirstOrDefault();
        }

        private CarbinPartEntry ParsePart(BinaryReader reader, ushort sceneVersion, int partIndex, CCarPartsEnum? partTypeHint)
        {
            var partEntry = new CarbinPartEntry(name => { });

            _lastParsingContext = $"Part {partIndex} version";
            _lastFilePosition = reader.BaseStream.Position;
            ushort partVersion = reader.ReadUInt16();
            partEntry.OriginalPartVersion = partVersion;

            _lastParsingContext = $"Part {partIndex} type";
            _lastFilePosition = reader.BaseStream.Position;
            uint rawPartType = reader.ReadUInt32();
            partEntry.OriginalPartTypeUint = rawPartType;

            // Motorsport Part version >= 3 uses CCarParts_Enum directly
            // Otherwise CCarParts_EnumV1 (values >= 42 need +1)
            if (!IsHorizon && partVersion >= 3)
                partEntry.PartType = (CCarPartsEnum)rawPartType;
            else
                partEntry.PartType = (CCarPartsEnum)ConvertEnumV1ToLatest(rawPartType);

            partEntry.PartTypeName = partEntry.PartType.ToString();

            _lastParsingContext = $"Part {partIndex} models count";
            _lastFilePosition = reader.BaseStream.Position;
            uint modelsCount = reader.ReadUInt32();

            for (int j = 0; j < modelsCount; j++)
            {
                _lastParsingContext = $"Part {partIndex} Model {j}";
                _lastFilePosition = reader.BaseStream.Position;
                var modelEntry = ParseCarRenderModel(reader, sceneVersion, $"Part{partIndex}_Model{j}");
                partEntry.Models.Add(modelEntry);
            }

            if (partVersion >= 2)
            {
                _lastParsingContext = $"Part {partIndex} bounds";
                _lastFilePosition = reader.BaseStream.Position;
                partEntry.BoundsMinX = reader.ReadSingle();
                partEntry.BoundsMinY = reader.ReadSingle();
                partEntry.BoundsMinZ = reader.ReadSingle();
                partEntry.BoundsMinW = reader.ReadSingle();
                partEntry.BoundsMaxX = reader.ReadSingle();
                partEntry.BoundsMaxY = reader.ReadSingle();
                partEntry.BoundsMaxZ = reader.ReadSingle();
                partEntry.BoundsMaxW = reader.ReadSingle();
            }

            return partEntry;
        }

        private CarbinPartEntry ParseUpgradablePart(BinaryReader reader, ushort sceneVersion, int partIndex)
        {
            var partEntry = new CarbinPartEntry(name => { });

            _lastParsingContext = $"UpgradablePart {partIndex} version";
            _lastFilePosition = reader.BaseStream.Position;
            ushort upgradablePartVersion = reader.ReadUInt16();
            partEntry.OriginalUpgradablePartVersion = upgradablePartVersion;

            _lastParsingContext = $"UpgradablePart {partIndex} type";
            _lastFilePosition = reader.BaseStream.Position;
            uint rawPartType = reader.ReadUInt32();
            partEntry.OriginalPartTypeUint = rawPartType;

            //  Motorsport UpgradablePart version >= 4 uses CCarParts_Enum directly
            // Otherwise CCarParts_EnumV1 (values >= 42 need +1)
            if (!IsHorizon && upgradablePartVersion >= 4)
                partEntry.PartType = (CCarPartsEnum)rawPartType;
            else
                partEntry.PartType = (CCarPartsEnum)ConvertEnumV1ToLatest(rawPartType);

            partEntry.PartTypeName = partEntry.PartType.ToString();

            _lastParsingContext = $"UpgradablePart {partIndex} upgrades count";
            _lastFilePosition = reader.BaseStream.Position;
            uint upgradesCount = reader.ReadUInt32();

            // Parse and store upgrades
            for (int i = 0; i < upgradesCount; i++)
            {
                var upgrade = ParseUpgradeEntry(reader, upgradablePartVersion, sceneVersion, partIndex, i);
                partEntry.Upgrades.Add(upgrade);
            }

            if (upgradablePartVersion >= 3)
            {
                _lastParsingContext = $"UpgradablePart {partIndex} shared models count";
                _lastFilePosition = reader.BaseStream.Position;
                uint sharedModelsCount = reader.ReadUInt32();

                for (int i = 0; i < sharedModelsCount; i++)
                {
                    _lastParsingContext = $"UpgradablePart {partIndex} SharedModel {i} upgrade IDs";
                    _lastFilePosition = reader.BaseStream.Position;

                    // Read and store upgrade IDs
                    uint upgradeIdsCount = reader.ReadUInt32();
                    var upgradeIds = new int[upgradeIdsCount];
                    for (int j = 0; j < upgradeIdsCount; j++)
                    {
                        upgradeIds[j] = reader.ReadInt32();
                    }

                    var modelEntry = ParseCarRenderModel(reader, sceneVersion, $"UpgPart{partIndex}_SharedModel{i}");

                    // Store the upgrade IDs in the model
                    foreach (var id in upgradeIds)
                    {
                        modelEntry.UpgradeIds.Add(id);
                    }

                    partEntry.Models.Add(modelEntry);
                }
            }

            return partEntry;
        }

        private void PopulateUpgradeIdWrappers(CarbinPartEntry partEntry)
        {
            foreach (var model in partEntry.Models)
            {
                model.UpgradeIdWrappers.Clear();
                foreach (var id in model.UpgradeIds)
                {
                    model.UpgradeIdWrappers.Add(new UpgradeIdWrapper(id));
                }
            }
        }

        private UpgradeEntry ParseUpgradeEntry(BinaryReader reader, ushort upgradablePartVersion, ushort sceneVersion, int partIndex, int upgradeIndex)
        {
            _lastParsingContext = $"Upgrade {partIndex}/{upgradeIndex} version";
            _lastFilePosition = reader.BaseStream.Position;
            ushort upgradeVersion = reader.ReadUInt16();

            byte level = reader.ReadByte();
            bool isStock = reader.ReadByte() != 0;
            int partId = reader.ReadInt32();
            int carBodyId = reader.ReadInt32();
            bool parentIsStock = reader.ReadByte() != 0;

            var upgradeEntry = new UpgradeEntry(partId, level, isStock)
            {
                Version = upgradeVersion,
                CarBodyId = carBodyId,
                ParentIsStock = parentIsStock
            };

            if (upgradeVersion < 3)
            {
                uint modelsCount = reader.ReadUInt32();
                for (int i = 0; i < modelsCount; i++)
                {
                    ParseCarRenderModel(reader, sceneVersion, $"Upgrade{partIndex}_{upgradeIndex}_Model{i}");
                }
            }

            if (upgradeVersion >= 2)
            {
                upgradeEntry.BoundsMinX = reader.ReadSingle();
                upgradeEntry.BoundsMinY = reader.ReadSingle();
                upgradeEntry.BoundsMinZ = reader.ReadSingle();
                upgradeEntry.BoundsMinW = reader.ReadSingle();
                upgradeEntry.BoundsMaxX = reader.ReadSingle();
                upgradeEntry.BoundsMaxY = reader.ReadSingle();
                upgradeEntry.BoundsMaxZ = reader.ReadSingle();
                upgradeEntry.BoundsMaxW = reader.ReadSingle();
            }

            return upgradeEntry;
        }

        private CarbinModelEntry ParseCarRenderModel(BinaryReader reader, ushort sceneVersion, string modelContext)
        {
            var model = new CarbinModelEntry();

            _lastParsingContext = $"{modelContext} version";
            _lastFilePosition = reader.BaseStream.Position;
            ushort modelVersion = reader.ReadUInt16();
            DetectedModelVersion = modelVersion;
            model.OriginalModelVersion = modelVersion;

            // Determine series from model version (heuristic from .bt)
            bool? modelIsHorizon = TryResolveModelSeriesIsHorizon(sceneVersion, modelVersion);
            if (modelIsHorizon.HasValue)
                IsHorizon = modelIsHorizon.Value;

            // Path
            _lastParsingContext = $"{modelContext} path";
            _lastFilePosition = reader.BaseStream.Position;
            string path = ReadString(reader);
            model.ModelGamePath = path;
            model.ModelFileName = Path.GetFileName(path.Replace("game:\\", "").Replace("game:/", ""));

            // Transform matrix
            _lastParsingContext = $"{modelContext} transform";
            _lastFilePosition = reader.BaseStream.Position;

            float m11 = reader.ReadSingle(); float m12 = reader.ReadSingle(); float m13 = reader.ReadSingle(); float m14 = reader.ReadSingle();
            float m21 = reader.ReadSingle(); float m22 = reader.ReadSingle(); float m23 = reader.ReadSingle(); float m24 = reader.ReadSingle();
            float m31 = reader.ReadSingle(); float m32 = reader.ReadSingle(); float m33 = reader.ReadSingle(); float m34 = reader.ReadSingle();
            float m41 = reader.ReadSingle(); float m42 = reader.ReadSingle(); float m43 = reader.ReadSingle(); float m44 = reader.ReadSingle();

            var transformMatrix = new System.Numerics.Matrix4x4(
                m11, m12, m13, m14,
                m21, m22, m23, m24,
                m31, m32, m33, m34,
                m41, m42, m43, m44);

            model.TransformMatrix = transformMatrix;

            // LOD Flags
            _lastParsingContext = $"{modelContext} LOD flags";
            _lastFilePosition = reader.BaseStream.Position;
            ushort lodFlags;
            if (modelVersion >= 5)
                lodFlags = reader.ReadUInt16();
            else
                lodFlags = (ushort)reader.ReadUInt32();

            model.ModelLodFlagLODS = (lodFlags & 0x01) != 0;
            model.ModelLodFlagLOD0 = (lodFlags & 0x02) != 0;
            model.ModelLodFlagLOD1 = (lodFlags & 0x04) != 0;
            model.ModelLodFlagLOD2 = (lodFlags & 0x08) != 0;
            model.ModelLodFlagLOD3 = (lodFlags & 0x10) != 0;
            model.ModelLodFlagLOD4 = (lodFlags & 0x20) != 0;
            model.ModelLodFlagLOD5 = (lodFlags & 0x40) != 0;

            // Bone Name
            model.BoneName = ReadString(reader);
            model.BoneId = reader.ReadInt16();
            model.SnapToParent = reader.ReadByte() != 0;

            // Draw Groups - read as int32, store raw value for byte-parity
            uint drawGroups = reader.ReadUInt32();
            model.RawDrawGroupsValue = drawGroups;
            model.DrawGroupExterior = (drawGroups & 0x01) != 0;
            model.DrawGroupCockpit = (drawGroups & 0x02) != 0;
            model.DrawGroupShadow = (drawGroups & 0x04) != 0;
            model.DrawGroupHood = (drawGroups & 0x08) != 0;
            model.DrawGroupWindshieldReflection = (drawGroups & 0x10) != 0;
            model.DrawGroupDriverlessCockpit = (drawGroups & 0x20) != 0;
            model.DrawGroupWindshieldReflectionDriverlessCockpit = (drawGroups & 0x40) != 0;
            model.DrawGroupProxyLOD = (drawGroups & 0x80) != 0;

            // Old AO swatchbin path (version < 9)
            if (modelVersion < 9)
            {
                string aoPath = ReadString(reader);
                if (!string.IsNullOrEmpty(aoPath))
                {
                    model.AoSwatchbinGamePath = aoPath;
                    model.AoSwatchbinFileName = Path.GetFileName(aoPath.Replace("game:\\", "").Replace("game:/", ""));
                }
            }

            // Material Overrides (version >= 2) - preserve for byte-parity
            if (modelVersion >= 2)
            {
                uint overridesCount = reader.ReadUInt32();
                for (int i = 0; i < overridesCount; i++)
                {
                    string key = ReadString(reader);
                    uint valueLength = reader.ReadUInt32();
                    byte[] data = valueLength > 0 ? reader.ReadBytes((int)valueLength) : [];
                    model.MaterialOverrides[key] = data;
                }
            }

            // Material Indexes (version >= 3)
            if (modelVersion >= 3)
            {
                _lastParsingContext = $"{modelContext} material indexes";
                _lastFilePosition = reader.BaseStream.Position;
                bool useHexValue = UsesFh6MaterialHashEditor(sceneVersion, IsHorizon, modelVersion);
                uint indexesCount = reader.ReadUInt32();
                for (int i = 0; i < indexesCount; i++)
                {
                    string key = ReadString(reader);
                    ulong val;
                    if (UsesWideMaterialIndexes(modelVersion))
                        val = reader.ReadUInt64();
                    else
                        val = (ulong)reader.ReadInt32();
                    model.MaterialIndexes.Add(new MaterialIndexEntry(key, val, useHexValue));
                }
            }

            // Droppable (version >= 6)
            if (modelVersion >= 6)
            {
                model.IsDroppable = reader.ReadByte() != 0;
                if (model.IsDroppable)
                {
                    model.DropValue = reader.ReadSingle();
                    model.DropPartId = reader.ReadUInt32();
                }
            }

            // Break Amount (version >= 8)
            if (modelVersion >= 8)
                model.BreakAmount = reader.ReadSingle();

            // AO Map Info (version >= 9)
            if (modelVersion >= 9)
            {
                _lastParsingContext = $"{modelContext} AO map infos";
                _lastFilePosition = reader.BaseStream.Position;
                uint aoMapInfoCount = reader.ReadUInt32();
                for (int i = 0; i < aoMapInfoCount; i++)
                {
                    var aoInfo = new AOMapInfoEntry();
                    aoInfo.Version = reader.ReadUInt16();
                    aoInfo.Path = ReadString(reader);
                    aoInfo.PartType = reader.ReadUInt32();
                    aoInfo.PartId = reader.ReadInt32();

                    if (aoInfo.Version >= 2)
                    {
                        aoInfo.DroppedModelInstanceGuid = new Guid(reader.ReadBytes(16));
                    }
                    else
                    {
                        aoInfo.BoneIndex = reader.ReadInt16();
                        aoInfo.IsDropped = reader.ReadByte() != 0;
                    }

                    aoInfo.IsDefault = reader.ReadByte() != 0;

                    if (aoInfo.Version >= 3)
                    {
                        aoInfo.LodTest = reader.ReadSByte();
                        aoInfo.LodValue = reader.ReadSByte();
                    }

                    model.AoMapInfos.Add(aoInfo);

                    if (i == 0)
                    {
                        model.AoSwatchbinGamePath = aoInfo.Path;
                        model.AoSwatchbinFileName = Path.GetFileName(aoInfo.Path.Replace("game:\\", "").Replace("game:/", ""));
                    }
                }
            }

            if (modelVersion >= 10)
                model.IsInteriorWindshield = reader.ReadByte() != 0;

            if (modelVersion >= 11)
            {
                model.ReceivesImpactMask = reader.ReadByte() != 0;
                model.ReceivesSplatterMask = reader.ReadByte() != 0;
                model.ReceivesDamage = reader.ReadUInt32() != 0;
                model.ReceivesDirt = reader.ReadUInt32() != 0;
                model.ReceivesOil = reader.ReadUInt32() != 0;
                model.ReceivesRubber = reader.ReadUInt32() != 0;
            }

            if (modelVersion >= 12)
                model.AssemblyName = ReadString(reader);

            if (modelVersion >= 13)
                model.GuidV13 = new Guid(reader.ReadBytes(16));

            if (modelVersion >= 14)
            {
                model.DropGuidV14 = new Guid(reader.ReadBytes(16));
                model.AoMapInfoIdV14 = reader.ReadUInt32();
            }

            if (IsHorizon && modelVersion >= 15)
                model.HorizonUnkV15 = reader.ReadInt32();

            if ((!IsHorizon && modelVersion >= 15) || (IsHorizon && modelVersion >= 16))
            {
                uint damageGuidsCount = reader.ReadUInt32();
                for (int i = 0; i < damageGuidsCount; i++)
                    model.DamageGuids.Add(new Guid(reader.ReadBytes(16)));
            }

            //  Motorsport and Horizon have different tail fields
            if (!IsHorizon)
            {
                if (modelVersion >= 16)
                    model.ReceivesRain = reader.ReadUInt32() != 0;
                if (modelVersion >= 17)
                    model.ProxyLodId = reader.ReadByte();
                if (modelVersion >= 18)
                    model.MotorsportUnkV18 = ReadString(reader);
                if (modelVersion >= 19)
                    model.MotorsportUnkV19 = ReadString(reader);
                if (modelVersion >= 20)
                {
                    model.IsInterior = reader.ReadByte() != 0;
                    model.IsLeftSideWindow = reader.ReadUInt32() != 0;
                    model.IsRightSideWindow = reader.ReadUInt32() != 0;
                    model.IsNascarWiper = reader.ReadByte() != 0;
                    model.IsLicensePlate = reader.ReadByte() != 0;
                }
            }
            else
            {
                if (modelVersion >= 17)
                    model.HorizonId = reader.ReadByte();
                if (modelVersion >= 18)
                    model.HorizonUnkV18 = reader.ReadUInt32();
                if (modelVersion >= 21)
                {
                    model.HorizonUnkV21Flag = reader.ReadUInt32();
                    model.HorizonUnkV21Path = ReadString(reader);
                }
            }

            return model;
        }

        // Converts CCarParts_EnumV1 to the latest CCarParts_Enum.
        // FM2023 added CCarParts_Ballast at index 42, shifting MotorParts(43), WheelStyle(44), Aspiration(45) up by 1.
        // Pre-FM2023 files store the old values where >= 42 need +1 to match the latest enum.
        private static uint ConvertEnumV1ToLatest(uint value)
        {
            if (value >= 42)
                return value + 1;
            return value;
        }

        private string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length <= 0) return "";
            if (length > 10_000)
                throw new InvalidDataException($"String length {length} too long");
            byte[] bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        private string ExtractSceneNameFromPath(string skeletonPath)
        {
            try
            {
                var parts = skeletonPath.Replace("game:\\", "").Replace("game:/", "").Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts[0].Equals("media", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("cars", StringComparison.OrdinalIgnoreCase))
                    return parts[2];
            }
            catch { }
            return "UNKNOWN";
        }

        private int DetermineVersionIndex(ushort sceneVersion, ushort modelVersion, bool isHorizon)
        {
            if (isHorizon)
            {
                if (sceneVersion == 7 && modelVersion == 21) return 6;
                if (sceneVersion == 6 && modelVersion == 18) return 5;
                if (sceneVersion == 5 && modelVersion == 16) return 4;
                if (sceneVersion == 5 && modelVersion == 15) return 3;
            }
            else
            {
                if ((sceneVersion == 10 || sceneVersion == 11) && modelVersion == 21) return 2;
                if (sceneVersion == 5 && modelVersion == 17) return 1;
                if (sceneVersion == 5 && modelVersion == 14) return 0;
            }
            return 5;
        }
    }
}
