using System;
using System.IO;
using System.Text;

namespace ForzaTechStudio.ViewModels
{
    public partial class CarbinEditorViewModel
    {
        private void PrepareForSave()
        {
            foreach (var part in UpgradableParts)
            {
                foreach (var model in part.Models)
                {
                    model.UpgradeIds.Clear();
                    foreach (var w in model.UpgradeIdWrappers)
                    {
                        model.UpgradeIds.Add(w.Value);
                    }
                }
            }
        }

        private void WriteCarbinFile(BinaryWriter writer, ushort sceneVersion, ushort modelVersion, bool isHorizon)
        {
            int modelIndex = 0;
            writer.Write(sceneVersion);

            if (sceneVersion >= 3)
            {
                if (BuildGuid != Guid.Empty)
                    writer.Write(BuildGuid.ToByteArray());
                else
                    writer.Write(Guid.NewGuid().ToByteArray());
            }

            if (sceneVersion >= 5)
                writer.Write((byte)(BuildStrict ? 1 : 0));

            writer.Write(Ordinal);
            WriteString(writer, MediaName);
            WriteString(writer, SkeletonPath);

            if (sceneVersion >= 2)
                writer.Write((ushort)GetLODFlags());

            writer.Write((uint)NonUpgradableParts.Count);
            foreach (var part in NonUpgradableParts)
            {
                if (sceneVersion >= 4)
                {
                    //  Motorsport scene >= 6 writes CCarParts_Enum directly as byte,
                    // older versions write CCarParts_EnumV1 (need to convert back).
                    if (!isHorizon && sceneVersion >= 6)
                        writer.Write((byte)part.PartType);
                    else
                        writer.Write((byte)ConvertEnumLatestToV1((uint)part.PartType));
                }
                WritePartStructure(writer, part, sceneVersion, modelVersion, isHorizon, ref modelIndex);
            }

            writer.Write((uint)UpgradableParts.Count);
            foreach (var part in UpgradableParts)
            {
                WriteUpgradablePartStructure(writer, part, sceneVersion, modelVersion, isHorizon, ref modelIndex);
            }

            // FH5 specific field
            if (isHorizon && sceneVersion >= 6)
                writer.Write((byte)1);
        }

        private void WritePartStructure(BinaryWriter writer, CarbinPartEntry part, ushort sceneVersion, ushort modelVersion, bool isHorizon, ref int modelIndex)
        {
            // Use original part version if available (for byte-parity), otherwise compute
            ushort partVersion = part.OriginalPartVersion > 0
                ? part.OriginalPartVersion
                : (isHorizon ? (ushort)2 : (sceneVersion >= 10 ? (ushort)3 : (ushort)2));
            writer.Write(partVersion);

            // motorsport Part version >= 3 writes CCarParts_Enum directly,
            // otherwise writes CCarParts_EnumV1
            if (!isHorizon && partVersion >= 3)
                writer.Write((uint)part.PartType);
            else
                writer.Write(ConvertEnumLatestToV1((uint)part.PartType));

            writer.Write((uint)part.Models.Count);

            foreach (var model in part.Models)
                WriteCarRenderModel(writer, model, modelVersion, isHorizon, ref modelIndex);

            if (partVersion >= 2)
                WriteAABB(writer, part);
        }

        private void WriteUpgradablePartStructure(BinaryWriter writer, CarbinPartEntry part, ushort sceneVersion, ushort modelVersion, bool isHorizon, ref int modelIndex)
        {
            // Use original version if available for byte-parity
            ushort upgradablePartVersion = part.OriginalUpgradablePartVersion > 0
                ? part.OriginalUpgradablePartVersion
                : (isHorizon ? (ushort)3 : (sceneVersion >= 10 ? (ushort)4 : (ushort)3));
            writer.Write(upgradablePartVersion);

            // Motorsport UpgradablePart version >= 4 writes CCarParts_Enum directly,
            // otherwise writes CCarParts_EnumV1
            if (!isHorizon && upgradablePartVersion >= 4)
                writer.Write((uint)part.PartType);
            else
                writer.Write(ConvertEnumLatestToV1((uint)part.PartType));

            // Write upgrades count and data
            writer.Write((uint)part.Upgrades.Count);

            foreach (var upgrade in part.Upgrades)
            {
                writer.Write(upgrade.Version);
                writer.Write(upgrade.Level);
                writer.Write((byte)(upgrade.IsStock ? 1 : 0));
                writer.Write(upgrade.Id);
                writer.Write(upgrade.CarBodyId);
                writer.Write((byte)(upgrade.ParentIsStock ? 1 : 0));

                // Bounds (version >= 2) - preserve W components for byte-parity
                if (upgrade.Version >= 2)
                {
                    writer.Write(upgrade.BoundsMinX);
                    writer.Write(upgrade.BoundsMinY);
                    writer.Write(upgrade.BoundsMinZ);
                    writer.Write(upgrade.BoundsMinW);
                    writer.Write(upgrade.BoundsMaxX);
                    writer.Write(upgrade.BoundsMaxY);
                    writer.Write(upgrade.BoundsMaxZ);
                    writer.Write(upgrade.BoundsMaxW);
                }
            }

            // Write shared models (version 3+)
            if (upgradablePartVersion >= 3)
            {
                writer.Write((uint)part.Models.Count);
                foreach (var model in part.Models)
                    WriteSharedCarModel(writer, model, modelVersion, isHorizon, ref modelIndex);
            }
        }

        private void WriteSharedCarModel(BinaryWriter writer, CarbinModelEntry model, ushort modelVersion, bool isHorizon, ref int modelIndex)
        {
            if (model.UpgradeIds.Count > 0)
            {
                writer.Write((uint)model.UpgradeIds.Count);
                foreach (var id in model.UpgradeIds)
                {
                    writer.Write(id);
                }
            }
            else
            {
                writer.Write((uint)1);
                writer.Write(0);
            }

            WriteCarRenderModel(writer, model, modelVersion, isHorizon, ref modelIndex);
        }

        private void WriteCarRenderModel(BinaryWriter writer, CarbinModelEntry model, ushort modelVersion, bool isHorizon, ref int modelIndex)
        {
            writer.Write(modelVersion);
            WriteString(writer, model.ModelGamePath);
            WriteTransformMatrix(writer, model);

            if (modelVersion >= 5)
                writer.Write((ushort)model.GetModelLODFlags());
            else
                writer.Write((uint)model.GetModelLODFlags());

            WriteString(writer, model.BoneName);
            writer.Write(model.BoneId);
            writer.Write((byte)(model.SnapToParent ? 1 : 0));

            // Always compute draw groups from individual flags (the user-editable properties).
            // RawDrawGroupsValue is only the parse-time snapshot and is never updated by UI edits.
            writer.Write((uint)model.GetDrawGroupFlags());

            // Old AO swatchbin path (version < 9)
            if (modelVersion < 9)
                WriteString(writer, model.AoSwatchbinGamePath ?? "");

            // Material Overrides (version >= 2) - preserve for byte-parity
            if (modelVersion >= 2)
            {
                writer.Write((uint)model.MaterialOverrides.Count);
                foreach (var kvp in model.MaterialOverrides)
                {
                    WriteString(writer, kvp.Key);
                    writer.Write((uint)kvp.Value.Length);
                    if (kvp.Value.Length > 0)
                        writer.Write(kvp.Value);
                }
            }

            // Material Indexes (version >= 3)
            if (modelVersion >= 3)
            {
                writer.Write((uint)model.MaterialIndexes.Count);
                foreach (var matIdx in model.MaterialIndexes)
                {
                    WriteString(writer, matIdx.Key);
                    if (!isHorizon && modelVersion >= 21)
                        writer.Write(matIdx.Value);
                    else
                        writer.Write((int)matIdx.Value);
                }
            }

            if (modelVersion >= 6)
            {
                writer.Write((byte)(model.IsDroppable ? 1 : 0));
                if (model.IsDroppable)
                {
                    writer.Write(model.DropValue);
                    writer.Write(model.DropPartId);
                }
            }

            if (modelVersion >= 8)
                writer.Write(model.BreakAmount);

            // AO Map Info (version >= 9)
            if (modelVersion >= 9)
            {
                if (model.AoMapInfos.Count > 0)
                {
                    writer.Write((uint)model.AoMapInfos.Count);
                    foreach (var aoInfo in model.AoMapInfos)
                    {
                        writer.Write(aoInfo.Version);
                        WriteString(writer, aoInfo.Path);
                        writer.Write(aoInfo.PartType);
                        writer.Write(aoInfo.PartId);

                        if (aoInfo.Version >= 2)
                        {
                            writer.Write(aoInfo.DroppedModelInstanceGuid.ToByteArray());
                        }
                        else
                        {
                            writer.Write(aoInfo.BoneIndex);
                            writer.Write((byte)(aoInfo.IsDropped ? 1 : 0));
                        }

                        writer.Write((byte)(aoInfo.IsDefault ? 1 : 0));

                        if (aoInfo.Version >= 3)
                        {
                            writer.Write(aoInfo.LodTest);
                            writer.Write(aoInfo.LodValue);
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(model.AoSwatchbinGamePath))
                {
                    writer.Write((uint)1);
                    writer.Write((ushort)3);
                    WriteString(writer, model.AoSwatchbinGamePath);
                    writer.Write((uint)0xFFFFFFFF);
                    writer.Write(-1);
                    writer.Write(Guid.Empty.ToByteArray());
                    writer.Write((byte)1);
                    writer.Write((sbyte)0);
                    writer.Write((sbyte)31);
                }
                else
                {
                    writer.Write((uint)0);
                }
            }

            if (modelVersion >= 10)
                writer.Write((byte)(model.IsInteriorWindshield ? 1 : 0));

            if (modelVersion >= 11)
            {
                writer.Write((byte)(model.ReceivesImpactMask ? 1 : 0));
                writer.Write((byte)(model.ReceivesSplatterMask ? 1 : 0));
                writer.Write((uint)(model.ReceivesDamage ? 1 : 0));
                writer.Write((uint)(model.ReceivesDirt ? 1 : 0));
                writer.Write((uint)(model.ReceivesOil ? 1 : 0));
                writer.Write((uint)(model.ReceivesRubber ? 1 : 0));
            }

            if (modelVersion >= 12)
                WriteString(writer, model.AssemblyName ?? "");

            if (modelVersion >= 13)
            {
                if (model.GuidV13 != Guid.Empty)
                    writer.Write(model.GuidV13.ToByteArray());
                else
                    writer.Write(Guid.NewGuid().ToByteArray());
            }

            if (modelVersion >= 14)
            {
                writer.Write(model.DropGuidV14.ToByteArray());
                writer.Write(model.AoMapInfoIdV14);
            }

            if (isHorizon && modelVersion >= 15)
                writer.Write(model.HorizonUnkV15);

            if ((!isHorizon && modelVersion >= 15) || (isHorizon && modelVersion >= 16))
            {
                writer.Write((uint)model.DamageGuids.Count);
                foreach (var guid in model.DamageGuids)
                    writer.Write(guid.ToByteArray());
            }

            // Motorsport and Horizon have different tail fields
            if (!isHorizon)
            {
                if (modelVersion >= 16)
                    writer.Write((uint)(model.ReceivesRain ? 1 : 0));
                if (modelVersion >= 17)
                    writer.Write(model.ProxyLodId);
                if (modelVersion >= 18)
                    WriteString(writer, model.MotorsportUnkV18 ?? "");
                if (modelVersion >= 19)
                    WriteString(writer, model.MotorsportUnkV19 ?? "");
                if (modelVersion >= 20)
                {
                    writer.Write((byte)(model.IsInterior ? 1 : 0));
                    writer.Write((uint)(model.IsLeftSideWindow ? 1 : 0));
                    writer.Write((uint)(model.IsRightSideWindow ? 1 : 0));
                    writer.Write((byte)(model.IsNascarWiper ? 1 : 0));
                    writer.Write((byte)(model.IsLicensePlate ? 1 : 0));
                }
            }
            else
            {
                if (modelVersion >= 17)
                {
                    // Preserve original HorizonId for byte-parity when round-tripping
                    // Only auto-assign sequential IDs for new models (OriginalModelVersion == 0)
                    if (model.OriginalModelVersion > 0)
                        writer.Write(model.HorizonId);
                    else
                    {
                        writer.Write((byte)modelIndex);
                        modelIndex++;
                    }
                }
                if (modelVersion >= 18)
                    writer.Write(model.HorizonUnkV18);
            }
        }

        // Converts the latest CCarParts_Enum back to CCarParts_EnumV1 for older format writing.
        // Reverses the +1 offset applied for values > 42 (Ballast was inserted at 42).
        private static uint ConvertEnumLatestToV1(uint value)
        {
            if (value == 42) // CCarParts_Ballast doesn't exist in V1
                return value;
            if (value > 42)
                return value - 1;
            return value;
        }

        private void WriteString(BinaryWriter writer, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.Write(0);
            }
            else
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }

        private void WriteTransformMatrix(BinaryWriter writer, CarbinModelEntry model)
        {
            var m = model.TransformMatrix;
            writer.Write(m.M11); writer.Write(m.M12); writer.Write(m.M13); writer.Write(m.M14);
            writer.Write(m.M21); writer.Write(m.M22); writer.Write(m.M23); writer.Write(m.M24);
            writer.Write(m.M31); writer.Write(m.M32); writer.Write(m.M33); writer.Write(m.M34);
            writer.Write(m.M41); writer.Write(m.M42); writer.Write(m.M43); writer.Write(m.M44);
        }

        private void WriteAABB(BinaryWriter writer, CarbinPartEntry part)
        {
            writer.Write(part.BoundsMinX); writer.Write(part.BoundsMinY); writer.Write(part.BoundsMinZ); writer.Write(part.BoundsMinW);
            writer.Write(part.BoundsMaxX); writer.Write(part.BoundsMaxY); writer.Write(part.BoundsMaxZ); writer.Write(part.BoundsMaxW);
        }
    }
}
