using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using ForzaTechStudio.Services;
using Microsoft.UI.Xaml.Controls;

namespace ForzaTechStudio.ViewModels
{
    public partial class CarbinEditorViewModel
    {
        private async Task LoadMaterialsForModelAsync(CarbinModelEntry model)
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".modelbin");
            picker.FileTypeFilter.Add(".carbin");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            string ext = Path.GetExtension(file.Path).ToLower();

            if (ext == ".carbin")
            {
                await LoadMaterialsFromCarbinAsync(model, file.Path);
            }
            else
            {
                var materials = MaterialExtractionService.GetMaterialNames(file.Path);

                if (materials.Count > 0)
                {
                    model.MaterialIndexes.Clear();
                    bool useHexValue = UsesFh6MaterialHashEditorForModel(model);
                    foreach (var mat in materials)
                    {
                        model.MaterialIndexes.Add(new MaterialIndexEntry(mat, 0, useHexValue));
                    }
                    StatusMessage = $"Loaded {materials.Count} materials from {file.Name}.";
                }
                else
                {
                    StatusMessage = $"No materials found in {file.Name}.";
                }
            }
        }

        private async Task LoadMaterialsFromCarbinAsync(CarbinModelEntry targetModel, string carbinPath)
        {
            try
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(carbinPath);

                if (fileBytes.Length < 2)
                {
                    StatusMessage = $"Error: Carbin file is too small ({fileBytes.Length} bytes)";
                    return;
                }

                var allModels = ParseCarbinForModelList(fileBytes);

                if (allModels.Count == 0)
                {
                    StatusMessage = "No models with material indexes found in the carbin file.";
                    return;
                }

                var selectedModel = await ShowCarbinModelSelectorDialogAsync(allModels);
                if (selectedModel == null) return;

                targetModel.MaterialIndexes.Clear();
                bool useHexValue = UsesFh6MaterialHashEditorForModel(targetModel);
                foreach (var matIdx in selectedModel.MaterialIndexes)
                {
                    targetModel.MaterialIndexes.Add(new MaterialIndexEntry(matIdx.Key, matIdx.Value, useHexValue));
                }

                string carbinFileName = Path.GetFileName(carbinPath);
                StatusMessage = $"Copied {selectedModel.MaterialIndexes.Count} material indexes from '{selectedModel.DisplayName}' in {carbinFileName}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error reading carbin: {ex.Message}";
            }
        }

        // Lightweight carbin model info used for the model selector dialog.
        private class CarbinModelInfo
        {
            public string DisplayName { get; set; } = "";
            public string GamePath { get; set; } = "";
            public string PartContext { get; set; } = "";
            public List<MaterialIndexEntry> MaterialIndexes { get; set; } = [];
        }

        // Parses a carbin file and returns a flat list of all models that have material indexes.
        private List<CarbinModelInfo> ParseCarbinForModelList(byte[] fileBytes)
        {
            var models = new List<CarbinModelInfo>();

            using var ms = new MemoryStream(fileBytes);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);

            ushort sceneVersion = reader.ReadUInt16();

            bool isHorizon = GetSceneSeriesIsHorizon(sceneVersion);

            if (sceneVersion >= 3)
                reader.ReadBytes(16);

            if (sceneVersion >= 5)
                reader.ReadByte();

            reader.ReadUInt32(); // ordinal

            SkipString(reader); // media name
            SkipString(reader); // skeleton path

            if (sceneVersion >= 2)
                reader.ReadUInt16(); // LOD flags

            // Non-Upgradable Parts
            uint nonUpgradablePartsCount = reader.ReadUInt32();
            for (int i = 0; i < nonUpgradablePartsCount; i++)
            {
                if (sceneVersion >= 4)
                    reader.ReadByte(); // part type byte prefix

                ushort partVersion = reader.ReadUInt16();
                uint rawPartType = reader.ReadUInt32();

                // Apply enum V1 conversion for display
                uint partType = (!isHorizon && partVersion >= 3) ? rawPartType : ConvertEnumV1ToLatest(rawPartType);
                string partName = ((CCarPartsEnum)partType).ToString();

                uint modelsCount = reader.ReadUInt32();
                for (int j = 0; j < modelsCount; j++)
                {
                    var info = ParseCarRenderModelForInfo(reader, sceneVersion, isHorizon, $"Standard: {partName}");
                    if (info.MaterialIndexes.Count > 0)
                        models.Add(info);
                }

                if (partVersion >= 2)
                    reader.ReadBytes(32); // AABB
            }

            // Upgradable Parts
            uint upgradablePartsCount = reader.ReadUInt32();
            for (int i = 0; i < upgradablePartsCount; i++)
            {
                ushort upgradablePartVersion = reader.ReadUInt16();
                uint rawPartType = reader.ReadUInt32();

                uint partType = (!isHorizon && upgradablePartVersion >= 4) ? rawPartType : ConvertEnumV1ToLatest(rawPartType);
                string partName = ((CCarPartsEnum)partType).ToString();

                uint upgradesCount = reader.ReadUInt32();
                for (int u = 0; u < upgradesCount; u++)
                {
                    ushort upgradeVersion = reader.ReadUInt16();
                    reader.ReadByte();  // level
                    reader.ReadByte();  // isStock
                    reader.ReadInt32(); // partId
                    reader.ReadInt32(); // carBodyId
                    reader.ReadByte();  // parentIsStock

                    if (upgradeVersion < 3)
                    {
                        uint modelsCount = reader.ReadUInt32();
                        for (int j = 0; j < modelsCount; j++)
                        {
                            var info = ParseCarRenderModelForInfo(reader, sceneVersion, isHorizon, $"Upgrade: {partName} (Lvl {u})");
                            if (info.MaterialIndexes.Count > 0)
                                models.Add(info);
                        }
                    }

                    if (upgradeVersion >= 2)
                        reader.ReadBytes(32); // AABB
                }

                if (upgradablePartVersion >= 3)
                {
                    uint sharedModelsCount = reader.ReadUInt32();
                    for (int s = 0; s < sharedModelsCount; s++)
                    {
                        uint upgradeIdsCount = reader.ReadUInt32();
                        reader.ReadBytes((int)(upgradeIdsCount * 4));

                        var info = ParseCarRenderModelForInfo(reader, sceneVersion, isHorizon, $"Upgrade: {partName} (Shared)");
                        if (info.MaterialIndexes.Count > 0)
                            models.Add(info);
                    }
                }
            }

            return models;
        }

        // Parses a CarRenderModel from the stream, extracting only the path and material indexes.
        // Skips all other data to advance the stream correctly.
        private CarbinModelInfo ParseCarRenderModelForInfo(BinaryReader reader, ushort sceneVersion, bool isHorizon, string partContext)
        {
            var info = new CarbinModelInfo { PartContext = partContext };

            ushort modelVersion = reader.ReadUInt16();

            // Determine series from model version
            bool? modelIsHorizon = TryResolveModelSeriesIsHorizon(sceneVersion, modelVersion);
            if (modelIsHorizon.HasValue)
                isHorizon = modelIsHorizon.Value;

            string path = ReadString(reader);
            info.GamePath = path;
            info.DisplayName = Path.GetFileName(path.Replace("game:\\", "").Replace("game:/", ""));

            reader.ReadBytes(64); // transform matrix

            if (modelVersion >= 5)
                reader.ReadUInt16();
            else
                reader.ReadUInt32();

            SkipString(reader); // bone name
            reader.ReadInt16(); // bone id
            reader.ReadByte();  // snap to parent
            reader.ReadUInt32(); // draw groups

            if (modelVersion < 9)
                SkipString(reader); // old AO path

            if (modelVersion >= 2)
            {
                uint overridesCount = reader.ReadUInt32();
                for (int i = 0; i < overridesCount; i++)
                {
                    SkipString(reader);
                    uint valueLength = reader.ReadUInt32();
                    if (valueLength > 0)
                        reader.ReadBytes((int)valueLength);
                }
            }

            // Material Indexes ? this is what we want
            if (modelVersion >= 3)
            {
                bool useHexValue = UsesFh6MaterialHashEditor(sceneVersion, isHorizon, modelVersion);
                uint indexesCount = reader.ReadUInt32();
                for (int i = 0; i < indexesCount; i++)
                {
                    string key = ReadString(reader);
                    ulong val;
                    if (UsesWideMaterialIndexes(modelVersion))
                        val = reader.ReadUInt64();
                    else
                        val = (ulong)reader.ReadInt32();
                    info.MaterialIndexes.Add(new MaterialIndexEntry(key, val, useHexValue));
                }
            }

            if (modelVersion >= 6)
            {
                bool isDroppable = reader.ReadByte() != 0;
                if (isDroppable)
                {
                    reader.ReadSingle();
                    reader.ReadUInt32();
                }
            }

            if (modelVersion >= 8)
                reader.ReadSingle();

            if (modelVersion >= 9)
            {
                uint aoMapInfoCount = reader.ReadUInt32();
                for (int i = 0; i < aoMapInfoCount; i++)
                {
                    ushort aoVersion = reader.ReadUInt16();
                    SkipString(reader);
                    reader.ReadUInt32();
                    reader.ReadInt32();

                    if (aoVersion >= 2)
                        reader.ReadBytes(16);
                    else
                    {
                        reader.ReadInt16();
                        reader.ReadByte();
                    }

                    reader.ReadByte();

                    if (aoVersion >= 3)
                    {
                        reader.ReadSByte();
                        reader.ReadSByte();
                    }
                }
            }

            if (modelVersion >= 10)
                reader.ReadByte();

            if (modelVersion >= 11)
            {
                reader.ReadByte();
                reader.ReadByte();
                reader.ReadUInt32();
                reader.ReadUInt32();
                reader.ReadUInt32();
                reader.ReadUInt32();
            }

            if (modelVersion >= 12)
                SkipString(reader);

            if (modelVersion >= 13)
                reader.ReadBytes(16);

            if (modelVersion >= 14)
            {
                reader.ReadBytes(16);
                reader.ReadUInt32();
            }

            if (isHorizon && modelVersion >= 15)
                reader.ReadInt32();

            if ((!isHorizon && modelVersion >= 15) || (isHorizon && modelVersion >= 16))
            {
                uint damageGuidsCount = reader.ReadUInt32();
                reader.ReadBytes((int)(damageGuidsCount * 16));
            }

            // Per .bt: Motorsport and Horizon tail fields
            if (!isHorizon)
            {
                if (modelVersion >= 16)
                    reader.ReadUInt32();
                if (modelVersion >= 17)
                    reader.ReadByte();
                if (modelVersion >= 18)
                    SkipString(reader); // String, not uint32
                if (modelVersion >= 19)
                    SkipString(reader); // String
                if (modelVersion >= 20)
                {
                    reader.ReadByte();
                    reader.ReadUInt32();
                    reader.ReadUInt32();
                    reader.ReadByte();
                    reader.ReadByte();
                }
            }
            else
            {
                if (modelVersion >= 17)
                    reader.ReadByte();
                if (modelVersion >= 18)
                    reader.ReadUInt32();
                if (modelVersion >= 21)
                {
                    reader.ReadUInt32();
                    SkipString(reader);
                }
            }

            return info;
        }

        private void SkipString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length > 0)
                reader.ReadBytes(length);
        }

        private async Task<CarbinModelInfo?> ShowCarbinModelSelectorDialogAsync(List<CarbinModelInfo> models)
        {
            var listView = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 400,
                MinWidth = 500,
                ItemTemplate = CreateCarbinModelSelectorTemplate()
            };

            foreach (var m in models)
                listView.Items.Add(m);

            if (models.Count > 0)
                listView.SelectedIndex = 0;

            var dialog = new ContentDialog
            {
                Title = "Select a Model from Carbin",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Found {models.Count} model(s) with material indexes. Select one to copy from:",
                            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                        },
                        listView
                    }
                },
                PrimaryButtonText = "Copy Materials",
                CloseButtonText = "Cancel",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && listView.SelectedItem is CarbinModelInfo selected)
                return selected;

            return null;
        }

        private static Microsoft.UI.Xaml.DataTemplate CreateCarbinModelSelectorTemplate()
        {
            var xaml = @"
                <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                    <StackPanel Padding='4,8' Spacing='2'>
                        <TextBlock Text='{Binding DisplayName}' FontWeight='SemiBold'/>
                        <TextBlock Text='{Binding PartContext}' FontSize='11' Foreground='{ThemeResource TextFillColorSecondaryBrush}'/>
                        <TextBlock Text='{Binding GamePath}' FontSize='10' Foreground='{ThemeResource TextFillColorTertiaryBrush}' TextTrimming='CharacterEllipsis'/>
                    </StackPanel>
                </DataTemplate>";
            return (Microsoft.UI.Xaml.DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
        }

        // Add-model-from-carbin: full model info, parser, dialog, version-aware copy

        // Full model data extracted from a carbin file for use when copying a model entry.
        private class CarbinFullModelInfo
        {
            // Display (same as CarbinModelInfo so we can reuse the DataTemplate)
            public string DisplayName { get; set; } = "";
            public string GamePath { get; set; } = "";
            public string PartContext { get; set; } = "";

            // Source version metadata
            public ushort SourceModelVersion { get; set; }
            public bool SourceIsHorizon { get; set; }

            // LOD flags (raw ushort)
            public ushort LodFlags { get; set; }

            // Transform
            public Matrix4x4 TransformMatrix { get; set; } = Matrix4x4.Identity;

            // Bone
            public string BoneName { get; set; } = "<root>";
            public short BoneId { get; set; }
            public bool SnapToParent { get; set; }

            // Draw groups (raw uint32, decoded at copy time)
            public uint DrawGroupsRaw { get; set; }

            // Material indexes (v3+)
            public List<MaterialIndexEntry> MaterialIndexes { get; set; } = [];

            // Droppable (v6+)
            public bool IsDroppable { get; set; }
            public float DropValue { get; set; }
            public uint DropPartId { get; set; }

            // Break amount (v8+)
            public float BreakAmount { get; set; }

            // AO map infos (v9+)
            public List<AOMapInfoEntry> AoMapInfos { get; set; } = [];

            // Render flags (v10+)
            public bool IsInteriorWindshield { get; set; }

            // Surface interactions (v11+)
            public bool ReceivesImpactMask { get; set; }
            public bool ReceivesSplatterMask { get; set; }
            public bool ReceivesDamage { get; set; }
            public bool ReceivesDirt { get; set; }
            public bool ReceivesOil { get; set; }
            public bool ReceivesRubber { get; set; }

            // Assembly name (v12+)
            public string AssemblyName { get; set; } = "";

            // GUIDs (v13/v14) intentionally omitted - identity fields, not copied.

            // Legacy AO swatchbin path (v < 9)
            public string AoSwatchPathLegacy { get; set; } = "";

            // Damage GUIDs (v15 Motorsport / v16 Horizon)
            public List<Guid> DamageGuids { get; set; } = [];

            // Horizon-only tail fields
            public int HorizonUnkV15 { get; set; }
            public uint HorizonUnkV18 { get; set; }
            public uint HorizonUnkV21Flag { get; set; }
            public string HorizonUnkV21Path { get; set; } = "";

            // ReceivesRain (v16 Motorsport only)
            public bool ReceivesRain { get; set; }

            // FM2023 specific (v20+ Motorsport only)
            public bool IsInterior { get; set; }
            public bool IsLeftSideWindow { get; set; }
            public bool IsRightSideWindow { get; set; }
            public bool IsNascarWiper { get; set; }
            public bool IsLicensePlate { get; set; }
        }

        // Parses a carbin file and returns a flat list of ALL models (no filter).
        private List<CarbinFullModelInfo> ParseCarbinForAllModels(byte[] fileBytes)
        {
            var models = new List<CarbinFullModelInfo>();

            using var ms = new MemoryStream(fileBytes);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);

            ushort sceneVersion = reader.ReadUInt16();

            bool isHorizon = GetSceneSeriesIsHorizon(sceneVersion);

            if (sceneVersion >= 3)
                reader.ReadBytes(16);

            if (sceneVersion >= 5)
                reader.ReadByte();

            reader.ReadUInt32(); // ordinal
            SkipString(reader); // media name
            SkipString(reader); // skeleton path

            if (sceneVersion >= 2)
                reader.ReadUInt16(); // LOD flags

            // Non-Upgradable Parts
            uint nonUpgradablePartsCount = reader.ReadUInt32();
            for (int i = 0; i < nonUpgradablePartsCount; i++)
            {
                if (sceneVersion >= 4)
                    reader.ReadByte(); // part type byte prefix

                ushort partVersion = reader.ReadUInt16();
                uint rawPartType = reader.ReadUInt32();

                uint partType = (!isHorizon && partVersion >= 3) ? rawPartType : ConvertEnumV1ToLatest(rawPartType);
                string partName = ((CCarPartsEnum)partType).ToString();

                uint modelsCount = reader.ReadUInt32();
                for (int j = 0; j < modelsCount; j++)
                    models.Add(ParseCarRenderModelForFullInfo(reader, sceneVersion, isHorizon, $"Standard: {partName}"));

                if (partVersion >= 2)
                    reader.ReadBytes(32); // AABB
            }

            // Upgradable Parts
            uint upgradablePartsCount = reader.ReadUInt32();
            for (int i = 0; i < upgradablePartsCount; i++)
            {
                ushort upgradablePartVersion = reader.ReadUInt16();
                uint rawPartType = reader.ReadUInt32();

                uint partType = (!isHorizon && upgradablePartVersion >= 4) ? rawPartType : ConvertEnumV1ToLatest(rawPartType);
                string partName = ((CCarPartsEnum)partType).ToString();

                uint upgradesCount = reader.ReadUInt32();
                for (int u = 0; u < upgradesCount; u++)
                {
                    ushort upgradeVersion = reader.ReadUInt16();
                    byte level = reader.ReadByte();
                    reader.ReadByte();  // isStock
                    reader.ReadInt32(); // partId
                    reader.ReadInt32(); // carBodyId
                    reader.ReadByte();  // parentIsStock

                    if (upgradeVersion < 3)
                    {
                        uint modelsCount = reader.ReadUInt32();
                        for (int j = 0; j < modelsCount; j++)
                            models.Add(ParseCarRenderModelForFullInfo(reader, sceneVersion, isHorizon, $"Upgrade: {partName} (Lvl {level})"));
                    }

                    if (upgradeVersion >= 2)
                        reader.ReadBytes(32); // AABB
                }

                if (upgradablePartVersion >= 3)
                {
                    uint sharedModelsCount = reader.ReadUInt32();
                    for (int s = 0; s < sharedModelsCount; s++)
                    {
                        uint upgradeIdsCount = reader.ReadUInt32();
                        reader.ReadBytes((int)(upgradeIdsCount * 4));
                        models.Add(ParseCarRenderModelForFullInfo(reader, sceneVersion, isHorizon, $"Upgrade: {partName} (Shared)"));
                    }
                }
            }

            return models;
        }

        // Reads a CarRenderModel from the stream and stores all fields into a CarbinFullModelInfo.
        private CarbinFullModelInfo ParseCarRenderModelForFullInfo(BinaryReader reader, ushort sceneVersion, bool isHorizon, string partContext)
        {
            var info = new CarbinFullModelInfo { PartContext = partContext };

            ushort modelVersion = reader.ReadUInt16();
            info.SourceModelVersion = modelVersion;

            // Determine series from model version (mirrors existing parser logic)
            bool? modelIsHorizon = TryResolveModelSeriesIsHorizon(sceneVersion, modelVersion);
            if (modelIsHorizon.HasValue)
                isHorizon = modelIsHorizon.Value;
            info.SourceIsHorizon = isHorizon;

            // Path
            string path = ReadString(reader);
            info.GamePath = path;
            info.DisplayName = Path.GetFileName(path.Replace("game:\\\\", "").Replace("game:/", ""));

            // Transform matrix (16 floats = 64 bytes)
            float m11 = reader.ReadSingle(); float m12 = reader.ReadSingle(); float m13 = reader.ReadSingle(); float m14 = reader.ReadSingle();
            float m21 = reader.ReadSingle(); float m22 = reader.ReadSingle(); float m23 = reader.ReadSingle(); float m24 = reader.ReadSingle();
            float m31 = reader.ReadSingle(); float m32 = reader.ReadSingle(); float m33 = reader.ReadSingle(); float m34 = reader.ReadSingle();
            float m41 = reader.ReadSingle(); float m42 = reader.ReadSingle(); float m43 = reader.ReadSingle(); float m44 = reader.ReadSingle();
            info.TransformMatrix = new Matrix4x4(m11, m12, m13, m14, m21, m22, m23, m24, m31, m32, m33, m34, m41, m42, m43, m44);

            // LOD flags
            info.LodFlags = modelVersion >= 5 ? reader.ReadUInt16() : (ushort)reader.ReadUInt32();

            // Bone
            info.BoneName = ReadString(reader);
            info.BoneId = reader.ReadInt16();
            info.SnapToParent = reader.ReadByte() != 0;

            // Draw groups
            info.DrawGroupsRaw = reader.ReadUInt32();

            // Old AO swatchbin path (v < 9)
            if (modelVersion < 9)
                info.AoSwatchPathLegacy = ReadString(reader);

            // Material overrides (v2+) � skip for copy purposes
            if (modelVersion >= 2)
            {
                uint overridesCount = reader.ReadUInt32();
                for (int i = 0; i < overridesCount; i++)
                {
                    SkipString(reader);
                    uint valueLength = reader.ReadUInt32();
                    if (valueLength > 0)
                        reader.ReadBytes((int)valueLength);
                }
            }

            // Material indexes (v3+)
            if (modelVersion >= 3)
            {
                bool useHexValue = UsesFh6MaterialHashEditor(sceneVersion, isHorizon, modelVersion);
                uint indexesCount = reader.ReadUInt32();
                for (int i = 0; i < indexesCount; i++)
                {
                    string key = ReadString(reader);
                    ulong val = UsesWideMaterialIndexes(modelVersion) ? reader.ReadUInt64() : (ulong)reader.ReadInt32();
                    info.MaterialIndexes.Add(new MaterialIndexEntry(key, val, useHexValue));
                }
            }

            // Droppable (v6+)
            if (modelVersion >= 6)
            {
                info.IsDroppable = reader.ReadByte() != 0;
                if (info.IsDroppable)
                {
                    info.DropValue = reader.ReadSingle();
                    info.DropPartId = reader.ReadUInt32();
                }
            }

            // Break amount (v8+)
            if (modelVersion >= 8)
                info.BreakAmount = reader.ReadSingle();

            // AO map infos (v9+)
            if (modelVersion >= 9)
            {
                uint aoMapInfoCount = reader.ReadUInt32();
                for (int i = 0; i < aoMapInfoCount; i++)
                {
                    var aoInfo = new AOMapInfoEntry();
                    aoInfo.Version = reader.ReadUInt16();
                    aoInfo.Path = ReadString(reader);
                    aoInfo.PartType = reader.ReadUInt32();
                    aoInfo.PartId = reader.ReadInt32();

                    if (aoInfo.Version >= 2)
                        aoInfo.DroppedModelInstanceGuid = new Guid(reader.ReadBytes(16));
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

                    info.AoMapInfos.Add(aoInfo);
                }
            }

            // IsInteriorWindshield (v10+)
            if (modelVersion >= 10)
                info.IsInteriorWindshield = reader.ReadByte() != 0;

            // Surface interactions (v11+)
            if (modelVersion >= 11)
            {
                info.ReceivesImpactMask = reader.ReadByte() != 0;
                info.ReceivesSplatterMask = reader.ReadByte() != 0;
                info.ReceivesDamage = reader.ReadUInt32() != 0;
                info.ReceivesDirt = reader.ReadUInt32() != 0;
                info.ReceivesOil = reader.ReadUInt32() != 0;
                info.ReceivesRubber = reader.ReadUInt32() != 0;
            }

            // Assembly name (v12+)
            if (modelVersion >= 12)
                info.AssemblyName = ReadString(reader);

            // GUIDs (v13, v14) � read to advance stream, not stored (identity fields)
            if (modelVersion >= 13)
                reader.ReadBytes(16); // GuidV13

            if (modelVersion >= 14)
            {
                reader.ReadBytes(16); // DropGuidV14
                reader.ReadUInt32();  // AoMapInfoIdV14
            }

            // Horizon-only v15 unknown
            if (isHorizon && modelVersion >= 15)
                info.HorizonUnkV15 = reader.ReadInt32();

            // Damage GUIDs (v15 Motorsport / v16 Horizon)
            if ((!isHorizon && modelVersion >= 15) || (isHorizon && modelVersion >= 16))
            {
                uint damageGuidsCount = reader.ReadUInt32();
                for (int i = 0; i < damageGuidsCount; i++)
                    info.DamageGuids.Add(new Guid(reader.ReadBytes(16)));
            }

            // Motorsport tail fields
            if (!isHorizon)
            {
                if (modelVersion >= 16)
                    info.ReceivesRain = reader.ReadUInt32() != 0;
                if (modelVersion >= 17)
                    reader.ReadByte(); // ProxyLodId � not exposed in UI
                if (modelVersion >= 18)
                    SkipString(reader); // MotorsportUnkV18
                if (modelVersion >= 19)
                    SkipString(reader); // MotorsportUnkV19
                if (modelVersion >= 20)
                {
                    info.IsInterior = reader.ReadByte() != 0;
                    info.IsLeftSideWindow = reader.ReadUInt32() != 0;
                    info.IsRightSideWindow = reader.ReadUInt32() != 0;
                    info.IsNascarWiper = reader.ReadByte() != 0;
                    info.IsLicensePlate = reader.ReadByte() != 0;
                }
            }
            else
            {
                if (modelVersion >= 17)
                    reader.ReadByte(); // HorizonId � not exposed in UI
                if (modelVersion >= 18)
                    info.HorizonUnkV18 = reader.ReadUInt32();
                if (modelVersion >= 21)
                {
                    info.HorizonUnkV21Flag = reader.ReadUInt32();
                    info.HorizonUnkV21Path = ReadString(reader);
                }
            }

            return info;
        }

        // Shows a searchable dialog listing all models from a carbin file.
        // Returns the selected CarbinFullModelInfo, or null if the user cancels.
        private Task<CarbinFullModelInfo?> ShowAddModelFromCarbinDialogAsync(List<CarbinFullModelInfo> models)
            => ShowAddModelFromCarbinDialogAsync(models, "Select Model from Carbin", "Add Model");

        private async Task<CarbinFullModelInfo?> ShowAddModelFromCarbinDialogAsync(List<CarbinFullModelInfo> models, string title, string primaryText)
        {
            var listView = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 360,
                MinWidth = 500,
                ItemTemplate = CreateCarbinModelSelectorTemplate()
            };

            var allItems = models.ToList();

            void ApplyFilter(string query)
            {
                listView.Items.Clear();
                var filtered = string.IsNullOrWhiteSpace(query)
                    ? (IEnumerable<CarbinFullModelInfo>)allItems
                    : allItems.Where(m =>
                        m.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        m.GamePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        m.PartContext.Contains(query, StringComparison.OrdinalIgnoreCase));
                foreach (var m in filtered)
                    listView.Items.Add(m);
                if (listView.Items.Count > 0)
                    listView.SelectedIndex = 0;
            }

            ApplyFilter("");

            var searchBox = new TextBox
            {
                PlaceholderText = "Search by name, path or part..."
            };
            searchBox.TextChanged += (s, e) => ApplyFilter(searchBox.Text);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = new StackPanel
                {
                    Spacing = 8,
                    MinWidth = 520,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Found {models.Count} model(s). Select one:",
                            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                        },
                        searchBox,
                        listView
                    }
                },
                PrimaryButtonText = primaryText,
                CloseButtonText = "Cancel",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && listView.SelectedItem is CarbinFullModelInfo selected)
                return selected;

            return null;
        }

        // Creates a new CarbinModelEntry from a carbin-source model
        private CarbinModelEntry CreateModelEntryFromCarbinSource(
            CarbinFullModelInfo source,
            ushort targetModelVersion,
            bool targetIsHorizon)
        {
            // Use the game-path constructor so ModelGamePath is set directly from the source path.
            var entry = new CarbinModelEntry(source.GamePath, "", true);

            // Always: LOD flags
            entry.ModelLodFlagLODS = (source.LodFlags & 0x01) != 0;
            entry.ModelLodFlagLOD0 = (source.LodFlags & 0x02) != 0;
            entry.ModelLodFlagLOD1 = (source.LodFlags & 0x04) != 0;
            entry.ModelLodFlagLOD2 = (source.LodFlags & 0x08) != 0;
            entry.ModelLodFlagLOD3 = (source.LodFlags & 0x10) != 0;
            entry.ModelLodFlagLOD4 = (source.LodFlags & 0x20) != 0;
            entry.ModelLodFlagLOD5 = (source.LodFlags & 0x40) != 0;

            // Always: Transform
            entry.TransformMatrix = source.TransformMatrix;

            // Always: Bone
            entry.BoneName = source.BoneName;
            entry.BoneId = source.BoneId;
            entry.SnapToParent = source.SnapToParent;

            // Always: Draw groups
            entry.DrawGroupExterior = (source.DrawGroupsRaw & 0x01) != 0;
            entry.DrawGroupCockpit = (source.DrawGroupsRaw & 0x02) != 0;
            entry.DrawGroupShadow = (source.DrawGroupsRaw & 0x04) != 0;
            entry.DrawGroupHood = (source.DrawGroupsRaw & 0x08) != 0;
            entry.DrawGroupWindshieldReflection = (source.DrawGroupsRaw & 0x10) != 0;
            entry.DrawGroupDriverlessCockpit = (source.DrawGroupsRaw & 0x20) != 0;
            entry.DrawGroupWindshieldReflectionDriverlessCockpit = (source.DrawGroupsRaw & 0x40) != 0;
            entry.DrawGroupProxyLOD = (source.DrawGroupsRaw & 0x80) != 0;

            // v3+: Material indexes
            if (targetModelVersion >= 3 && source.SourceModelVersion >= 3)
            {
                bool useHexValue = UsesFh6MaterialHashEditorForCurrentOutput();
                foreach (var mat in source.MaterialIndexes)
                    entry.MaterialIndexes.Add(new MaterialIndexEntry(mat.Key, mat.Value, useHexValue));
            }

            // v6+: Droppable
            if (targetModelVersion >= 6 && source.SourceModelVersion >= 6)
            {
                entry.IsDroppable = source.IsDroppable;
                entry.DropValue = source.DropValue;
                entry.DropPartId = source.DropPartId;
            }

            // v8+: Break amount
            if (targetModelVersion >= 8 && source.SourceModelVersion >= 8)
                entry.BreakAmount = source.BreakAmount;

            // v9+: AO map infos
            if (targetModelVersion >= 9 && source.SourceModelVersion >= 9)
            {
                foreach (var ao in source.AoMapInfos)
                {
                    entry.AoMapInfos.Add(new AOMapInfoEntry
                    {
                        Version = ao.Version,
                        Path = ao.Path,
                        PartType = ao.PartType,
                        PartId = ao.PartId,
                        DroppedModelInstanceGuid = ao.DroppedModelInstanceGuid,
                        BoneIndex = ao.BoneIndex,
                        IsDropped = ao.IsDropped,
                        IsDefault = ao.IsDefault,
                        LodTest = ao.LodTest,
                        LodValue = ao.LodValue
                    });
                }
                // Mirror what the file parser does: set swatchbin display from first AO map
                if (source.AoMapInfos.Count > 0)
                {
                    entry.AoSwatchbinGamePath = source.AoMapInfos[0].Path;
                    entry.AoSwatchbinFileName = Path.GetFileName(
                        source.AoMapInfos[0].Path.Replace("game:\\\\", "").Replace("game:/", ""));
                }
            }

            // v10+: Interior windshield
            if (targetModelVersion >= 10 && source.SourceModelVersion >= 10)
                entry.IsInteriorWindshield = source.IsInteriorWindshield;

            // v11+: Surface interactions
            if (targetModelVersion >= 11 && source.SourceModelVersion >= 11)
            {
                entry.ReceivesImpactMask = source.ReceivesImpactMask;
                entry.ReceivesSplatterMask = source.ReceivesSplatterMask;
                entry.ReceivesDamage = source.ReceivesDamage;
                entry.ReceivesDirt = source.ReceivesDirt;
                entry.ReceivesOil = source.ReceivesOil;
                entry.ReceivesRubber = source.ReceivesRubber;
            }

            // v12+: Assembly name
            if (targetModelVersion >= 12 && source.SourceModelVersion >= 12)
                entry.AssemblyName = source.AssemblyName;

            // GUIDs (v13/v14): intentionally NOT copied.
            // New entries get fresh GUIDs on save (Guid.Empty triggers fresh generation).

            // Damage GUIDs (v15 Motorsport / v16 Horizon, series-aware)
            ushort srcDamageMin = source.SourceIsHorizon ? (ushort)16 : (ushort)15;
            ushort tgtDamageMin = targetIsHorizon ? (ushort)16 : (ushort)15;
            if (targetModelVersion >= tgtDamageMin && source.SourceModelVersion >= srcDamageMin)
            {
                foreach (var g in source.DamageGuids)
                    entry.DamageGuids.Add(g);
            }

            if (targetIsHorizon && source.SourceIsHorizon)
            {
                if (targetModelVersion >= 15 && source.SourceModelVersion >= 15)
                    entry.HorizonUnkV15 = source.HorizonUnkV15;

                if (targetModelVersion >= 18 && source.SourceModelVersion >= 18)
                    entry.HorizonUnkV18 = source.HorizonUnkV18;

                if (targetModelVersion >= 21 && source.SourceModelVersion >= 21)
                {
                    entry.HorizonUnkV21Flag = source.HorizonUnkV21Flag;
                    entry.HorizonUnkV21Path = source.HorizonUnkV21Path;
                }
            }

            // v16 Motorsport only: ReceivesRain
            if (!targetIsHorizon && targetModelVersion >= 16 &&
                !source.SourceIsHorizon && source.SourceModelVersion >= 16)
                entry.ReceivesRain = source.ReceivesRain;

            // v20 Motorsport only: FM2023 flags
            if (!targetIsHorizon && targetModelVersion >= 20 &&
                !source.SourceIsHorizon && source.SourceModelVersion >= 20)
            {
                entry.IsInterior = source.IsInterior;
                entry.IsLeftSideWindow = source.IsLeftSideWindow;
                entry.IsRightSideWindow = source.IsRightSideWindow;
                entry.IsNascarWiper = source.IsNascarWiper;
                entry.IsLicensePlate = source.IsLicensePlate;
            }

            return entry;
        }
    }
}
