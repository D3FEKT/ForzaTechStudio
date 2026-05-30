using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SDX = SharpDX;
using WinColor = Windows.UI.Color;

namespace ForzaTechStudio.Views
{
    public sealed partial class ViewportPage : Page
    {
        public ObservableCollection<ViewportManufacturerColorItem> ManufacturerColorItems { get; } = new();

        private ViewportManufacturerColorItem? _selectedManufacturerColorItem;
        private SDX.Color4? _manufacturerCarPaintColor;
        private bool _isUpdatingManufacturerColorSelection;

        private static readonly HashSet<uint> ViewportDiffuseColorHashes = new()
        {
            0x53A946B6, // BaseColor_Tint
            0x63040D89, // DiffuseColorColorParam
        };

        private static readonly HashSet<uint> ViewportAlphaHashes = new()
        {
            0x57D9D49E, // AlphaTexture
            0x66E53F62, // CH1AlphaTextureTexture
            0x2FDCDBF0, // AlphaTexture_1
        };

        private static readonly HashSet<uint> ViewportEmissiveHashes = new()
        {
            0x4E0D5E89, // CH1EmissiveMap
            0x6161E552, // EmissiveCH1
            0x020B22EB, // EmissiveMap
            0x212B4B48, // EmissiveTexture
            0x3CB4DFCB, // InteriorEmissiveTexture
            0x21EC1E4D, // EmissiveColor
            0xEFBBC518, // EmissiveTint
        };

        private static readonly HashSet<uint> ViewportEmissiveIntensityHashes = new()
        {
            0x074CCD8C, // Emissive_Intensity
            0x9421C781, // EmissiveIntensity
            0xD78943E8, // EmissiveMultiplier
            0x4C6E94DA, // EmissiveStrength
            0x22F9702D, // EmissiveBrightness
        };

        private static readonly string[] ViewportCarPaintTokens =
        {
            "carpaint",
            "hood_carpaint",
            "carpaint_secondary",
            "mirror_carpaint",
            "wing_carpaint",
        };

        private static readonly string[] ViewportTransparentMaterialTokens =
        {
            "glass",
            "window",
            "windows",
            "windshield",
            "windscreen",
            "tint",
            "lens",
        };

        private PhongMaterial CreateViewportMaterial(ForzaGeometryData data, ModelBinNode? modelBin)
        {
            var materialBlob = ResolveAssignedMaterial(data, modelBin);
            var fallbackDiffuse = CreateFallbackDiffuseColor(data?.Name);
            var state = BuildViewportMaterialState(data, materialBlob, fallbackDiffuse);

            var material = new PhongMaterial
            {
                DiffuseColor = state.DiffuseColor,
                EmissiveColor = state.EmissiveColor,
                SpecularColor = state.SpecularColor,
                SpecularShininess = state.SpecularShininess,
                DiffuseMap = state.DiffuseMap,
                NormalMap = state.NormalMap,
                SpecularColorMap = state.SpecularColorMap,
                RenderDiffuseMap = state.DiffuseMap != null,
                RenderNormalMap = state.NormalMap != null,
                RenderSpecularColorMap = state.SpecularColorMap != null,
                RenderDiffuseAlphaMap = state.DiffuseAlphaMap != null,
                RenderEmissiveMap = state.EmissiveMap != null,
                EnableAutoTangent = state.NormalMap != null
            };

            if (state.DiffuseAlphaMap != null)
                material.SetValue(PhongMaterial.DiffuseAlphaMapProperty, state.DiffuseAlphaMap);
            if (state.EmissiveMap != null)
                material.SetValue(PhongMaterial.EmissiveMapProperty, state.EmissiveMap);

            return material;
        }

        private void ApplyViewportMaterial(MeshGeometryModel3D meshModel, ForzaGeometryData data, ModelBinNode? modelBin)
        {
            meshModel.Material = CreateViewportMaterial(data, modelBin);
        }

        private ViewportRuntimeMaterialState BuildViewportMaterialState(ForzaGeometryData data, MaterialBlob? materialBlob, SDX.Color4 fallbackDiffuse)
        {
            bool isCarPaint = IsCarPaintMaterial(data, materialBlob);
            bool isTransparentHint = IsTransparentMaterial(data, materialBlob);
            float alpha = _sceneOpacity;
            var diffuse = fallbackDiffuse;
            var emissive = new SDX.Color4(0f, 0f, 0f, 1f);
            float emissiveIntensity = 0f;
            bool hasDiffuseColor = false;
            bool hasEmissiveColor = false;
            bool hasEmissiveTexture = false;
            var textureMaps = new ViewportRuntimeTextureMaps();

            if (!isCarPaint && materialBlob?.Bundle != null)
            {
                var paramBlob = materialBlob.Bundle.Blobs.OfType<MaterialShaderParameterBlob>().FirstOrDefault();
                if (paramBlob != null)
                {
                    foreach (var parameter in paramBlob.Parameters)
                    {
                        string parameterName = NameHashService.Instance.GetName(parameter.NameHash) ?? string.Empty;

                        if (parameter.Value is Vector4 vectorValue)
                        {
                            if (IsDiffuseColorParameter(parameter, parameterName))
                            {
                                diffuse = new SDX.Color4(Clamp01(vectorValue.X), Clamp01(vectorValue.Y), Clamp01(vectorValue.Z), alpha * AlphaOrOne(vectorValue.W));
                                alpha = diffuse.Alpha;
                                hasDiffuseColor = true;
                            }
                            else if (IsEmissiveParameter(parameter, parameterName))
                            {
                                emissive = new SDX.Color4(Clamp01(vectorValue.X), Clamp01(vectorValue.Y), Clamp01(vectorValue.Z), AlphaOrOne(vectorValue.W));
                                hasEmissiveColor = true;
                            }
                            else if (IsAlphaParameter(parameter, parameterName))
                            {
                                alpha *= AlphaOrOne(vectorValue.W);
                            }
                        }
                        else if (parameter.Value is float floatValue)
                        {
                            if (IsEmissiveIntensityParameter(parameter, parameterName))
                                emissiveIntensity = MathF.Max(emissiveIntensity, MathF.Max(0f, floatValue));
                            else if (IsAlphaParameter(parameter, parameterName))
                                alpha *= Clamp01(floatValue);
                        }
                        else if (parameter.Type == ShaderParameterType.Texture2D)
                        {
                            if (IsEmissiveParameter(parameter, parameterName))
                                hasEmissiveTexture = true;

                            if (parameter.Value is TextureParameter textureParameter)
                                ApplyViewportTextureParameter(parameter, textureParameter, textureMaps);
                        }
                    }
                }
            }

            if (isCarPaint)
            {
                var carPaintColor = _manufacturerCarPaintColor ?? new SDX.Color4(_singleColor.Red, _singleColor.Green, _singleColor.Blue, 1f);
                diffuse = new SDX.Color4(carPaintColor.Red, carPaintColor.Green, carPaintColor.Blue, alpha);
                emissive = new SDX.Color4(0f, 0f, 0f, 1f);
            }
            else if (!hasDiffuseColor)
            {
                diffuse.Alpha = alpha;
            }

            if (isTransparentHint && alpha >= 0.95f)
                alpha = 0.45f * _sceneOpacity;

            diffuse.Alpha = Clamp01(alpha);

            if (hasEmissiveTexture && !hasEmissiveColor)
                emissive = new SDX.Color4(diffuse.Red, diffuse.Green, diffuse.Blue, 1f);

            if (emissiveIntensity > 0f)
            {
                float scale = MathF.Min(emissiveIntensity, 8f);
                emissive = new SDX.Color4(
                    Clamp01(emissive.Red * scale),
                    Clamp01(emissive.Green * scale),
                    Clamp01(emissive.Blue * scale),
                    1f);
            }

            return new ViewportRuntimeMaterialState
            {
                DiffuseColor = diffuse,
                EmissiveColor = emissive,
                SpecularColor = isTransparentHint
                    ? new SDX.Color4(0.55f, 0.55f, 0.58f, 1f)
                    : new SDX.Color4(0.18f, 0.18f, 0.18f, 1f),
                SpecularShininess = isTransparentHint ? 80f : 32f,
                DiffuseMap = isCarPaint ? null : textureMaps.DiffuseMap,
                DiffuseAlphaMap = isCarPaint ? null : textureMaps.DiffuseAlphaMap,
                NormalMap = textureMaps.NormalMap,
                SpecularColorMap = textureMaps.SpecularColorMap,
                EmissiveMap = textureMaps.EmissiveMap,
            };
        }

        private void ApplyViewportTextureParameter(ShaderParameter parameter, TextureParameter textureParameter, ViewportRuntimeTextureMaps textureMaps)
        {
            var textureModel = ResolveViewportTextureModel(textureParameter);
            if (textureModel == null)
                return;

            switch (ClassifyViewportTextureParameter(parameter, textureParameter))
            {
                case ViewportTextureSlot.Normal:
                    textureMaps.NormalMap ??= textureModel;
                    break;
                case ViewportTextureSlot.Emissive:
                    textureMaps.EmissiveMap ??= textureModel;
                    break;
                case ViewportTextureSlot.Specular:
                    textureMaps.SpecularColorMap ??= textureModel;
                    break;
                case ViewportTextureSlot.Alpha:
                    textureMaps.DiffuseAlphaMap ??= textureModel;
                    break;
                case ViewportTextureSlot.Diffuse:
                    textureMaps.DiffuseMap ??= textureModel;
                    textureMaps.DiffuseAlphaMap ??= textureModel;
                    break;
            }
        }

        private static ViewportTextureSlot ClassifyViewportTextureParameter(ShaderParameter parameter, TextureParameter textureParameter)
        {
            string parameterName = NameHashService.Instance.GetName(parameter.NameHash) ?? string.Empty;
            string tokens = $"{parameterName} {textureParameter.Path}".ToLowerInvariant();

            if (tokens.Contains("normal", StringComparison.Ordinal))
                return ViewportTextureSlot.Normal;
            if (tokens.Contains("emissive", StringComparison.Ordinal) || tokens.Contains("emission", StringComparison.Ordinal))
                return ViewportTextureSlot.Emissive;
            if (tokens.Contains("specular", StringComparison.Ordinal) || tokens.Contains("roughness", StringComparison.Ordinal) || tokens.Contains("metal", StringComparison.Ordinal))
                return ViewportTextureSlot.Specular;
            if (tokens.Contains("alpha", StringComparison.Ordinal) && !tokens.Contains("basecoloralpha", StringComparison.Ordinal))
                return ViewportTextureSlot.Alpha;
            if (tokens.Contains("diffuse", StringComparison.Ordinal) || tokens.Contains("basecolor", StringComparison.Ordinal) || tokens.Contains("albedo", StringComparison.Ordinal) || tokens.Contains("main", StringComparison.Ordinal) || tokens.Contains("color", StringComparison.Ordinal))
                return ViewportTextureSlot.Diffuse;

            return ViewportTextureSlot.Diffuse;
        }

        private SDX.Color4 CreateFallbackDiffuseColor(string? name)
        {
            if (SingleColorToggle?.IsChecked == true)
                return new SDX.Color4(_singleColor.Red, _singleColor.Green, _singleColor.Blue, _sceneOpacity);

            var rnd = new Random(name?.GetHashCode() ?? 0);
            return new SDX.Color4((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble(), _sceneOpacity);
        }

        private static MaterialBlob? ResolveAssignedMaterial(ForzaGeometryData data, ModelBinNode? modelBin)
        {
            if (data?.SourceMesh == null || modelBin?.Bundle == null)
                return null;

            short assignedId = data.SourceMesh.MaterialIds != null && data.SourceMesh.MaterialIds.Length > 1
                ? data.SourceMesh.MaterialIds[1]
                : data.SourceMesh.MaterialId;

            return modelBin.Bundle.Blobs
                .OfType<MaterialBlob>()
                .FirstOrDefault(material => (short)GetMaterialId(material) == assignedId);
        }

        private static uint GetMaterialId(MaterialBlob material)
        {
            return material.Metadatas.OfType<IdentifierMetadata>().FirstOrDefault()?.Id ?? material.Id;
        }

        private static bool IsDiffuseColorParameter(ShaderParameter parameter, string parameterName)
        {
            return ViewportDiffuseColorHashes.Contains(parameter.NameHash)
                || ContainsAny(parameterName, "basecolor", "diffuse") && ContainsAny(parameterName, "tint", "color");
        }

        private static bool IsAlphaParameter(ShaderParameter parameter, string parameterName)
        {
            return ViewportAlphaHashes.Contains(parameter.NameHash)
                || parameterName.Contains("alpha", StringComparison.OrdinalIgnoreCase)
                || parameterName.Contains("opacity", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEmissiveParameter(ShaderParameter parameter, string parameterName)
        {
            return ViewportEmissiveHashes.Contains(parameter.NameHash)
                || parameterName.Contains("emissive", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEmissiveIntensityParameter(ShaderParameter parameter, string parameterName)
        {
            return ViewportEmissiveIntensityHashes.Contains(parameter.NameHash)
                || parameterName.Contains("emissive", StringComparison.OrdinalIgnoreCase) && ContainsAny(parameterName, "intensity", "multiplier", "strength", "brightness", "scale");
        }

        private static bool IsCarPaintMaterial(ForzaGeometryData data, MaterialBlob? materialBlob)
        {
            return MaterialTextContains(data, materialBlob, ViewportCarPaintTokens);
        }

        private static bool IsTransparentMaterial(ForzaGeometryData data, MaterialBlob? materialBlob)
        {
            return MaterialTextContains(data, materialBlob, ViewportTransparentMaterialTokens);
        }

        private static bool MaterialTextContains(ForzaGeometryData data, MaterialBlob? materialBlob, IEnumerable<string> tokens)
        {
            string text = string.Join(" ", new[]
            {
                data?.Name ?? string.Empty,
                data?.MaterialName ?? string.Empty,
                materialBlob?.Metadatas.OfType<NameMetadata>().FirstOrDefault()?.Name ?? string.Empty,
                materialBlob?.Bundle?.Blobs.OfType<MaterialResourceBlob>().FirstOrDefault()?.Path ?? string.Empty,
            }).Replace('\\', '/');

            foreach (string token in tokens)
            {
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool ContainsAny(string value, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            foreach (string token in tokens)
            {
                if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static float AlphaOrOne(float value)
        {
            return value <= 0f ? 1f : Clamp01(value);
        }

        private static float Clamp01(float value)
        {
            return Math.Clamp(value, 0f, 1f);
        }

        private void RefreshManufacturerColorsFromLoadedRoots()
        {
            string? selectedKey = _selectedManufacturerColorItem?.Key;
            ManufacturerColorItems.Clear();

            foreach (var zipNode in EnumerateViewerNodes<ZipNode>(ViewModel.Roots))
            {
                var blob = zipNode.ManufacturerColors;
                if (blob == null)
                    continue;

                for (int groupIndex = 0; groupIndex < blob.Groups.Count; groupIndex++)
                {
                    var group = blob.Groups[groupIndex];
                    for (int entryIndex = 0; entryIndex < group.Entries.Count; entryIndex++)
                    {
                        ManufacturerColorItems.Add(new ViewportManufacturerColorItem(zipNode.FilePath, zipNode.Name, groupIndex, entryIndex, group.Entries[entryIndex]));
                    }
                }
            }

            UpdateManufacturerColorControls(selectedKey);
        }

        private void UpdateManufacturerColorControls(string? selectedKey = null)
        {
            if (ManufacturerColorCombo == null)
                return;

            _isUpdatingManufacturerColorSelection = true;
            ManufacturerColorCombo.ItemsSource = ManufacturerColorItems;

            var selectedItem = !string.IsNullOrWhiteSpace(selectedKey)
                ? ManufacturerColorItems.FirstOrDefault(item => item.Key == selectedKey)
                : _selectedManufacturerColorItem != null
                    ? ManufacturerColorItems.FirstOrDefault(item => item.Key == _selectedManufacturerColorItem.Key)
                    : null;

            ManufacturerColorCombo.SelectedItem = selectedItem;
            _selectedManufacturerColorItem = selectedItem;
            _manufacturerCarPaintColor = selectedItem?.ToColor4();
            _isUpdatingManufacturerColorSelection = false;

            bool hasColors = ManufacturerColorItems.Count > 0;
            ManufacturerColorCombo.IsEnabled = hasColors;
            ResetManufacturerColorBtn.IsEnabled = _selectedManufacturerColorItem != null;
            ManufacturerColorStatusText.Text = hasColors
                ? $"{ManufacturerColorItems.Count} manufacturer color(s) loaded."
                : "No manufacturercolors.bin found in the loaded car zip.";
        }

        private void ManufacturerColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingManufacturerColorSelection)
                return;

            _selectedManufacturerColorItem = ManufacturerColorCombo.SelectedItem as ViewportManufacturerColorItem;
            _manufacturerCarPaintColor = _selectedManufacturerColorItem?.ToColor4();
            ResetManufacturerColorBtn.IsEnabled = _selectedManufacturerColorItem != null;
            UpdateMeshColors(SingleColorToggle?.IsChecked ?? false);
        }

        private void ResetManufacturerColor_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _selectedManufacturerColorItem = null;
            _manufacturerCarPaintColor = null;

            if (ManufacturerColorCombo != null)
            {
                _isUpdatingManufacturerColorSelection = true;
                ManufacturerColorCombo.SelectedItem = null;
                _isUpdatingManufacturerColorSelection = false;
            }

            ResetManufacturerColorBtn.IsEnabled = false;
            UpdateMeshColors(SingleColorToggle?.IsChecked ?? false);
        }

        private readonly struct ViewportRuntimeMaterialState
        {
            public SDX.Color4 DiffuseColor { get; init; }
            public SDX.Color4 EmissiveColor { get; init; }
            public SDX.Color4 SpecularColor { get; init; }
            public float SpecularShininess { get; init; }
            public TextureModel? DiffuseMap { get; init; }
            public TextureModel? DiffuseAlphaMap { get; init; }
            public TextureModel? NormalMap { get; init; }
            public TextureModel? SpecularColorMap { get; init; }
            public TextureModel? EmissiveMap { get; init; }
        }

        private sealed class ViewportRuntimeTextureMaps
        {
            public TextureModel? DiffuseMap { get; set; }
            public TextureModel? DiffuseAlphaMap { get; set; }
            public TextureModel? NormalMap { get; set; }
            public TextureModel? SpecularColorMap { get; set; }
            public TextureModel? EmissiveMap { get; set; }
        }

        private enum ViewportTextureSlot
        {
            Diffuse,
            Normal,
            Specular,
            Emissive,
            Alpha
        }
    }

    public sealed class ViewportManufacturerColorItem
    {
        public string SourceZipPath { get; }
        public string SourceZipName { get; }
        public int GroupIndex { get; }
        public int EntryIndex { get; }
        public ManufacturerColorEntry Entry { get; }
        public WinColor SwatchColor { get; }
        public SolidColorBrush SwatchBrush { get; }

        public string Key => $"{SourceZipPath}|{GroupIndex}|{EntryIndex}|{Entry.Path}";

        public string DisplayName
        {
            get
            {
                string entryName = string.IsNullOrWhiteSpace(Entry.Path)
                    ? $"Color {EntryIndex + 1}"
                    : System.IO.Path.GetFileNameWithoutExtension(Entry.Path.Replace('\\', '/'));

                return $"{entryName}  RGB({SwatchColor.R}, {SwatchColor.G}, {SwatchColor.B})";
            }
        }

        public string SourceText => $"{SourceZipName} - Group {GroupIndex + 1}";

        public ViewportManufacturerColorItem(string sourceZipPath, string sourceZipName, int groupIndex, int entryIndex, ManufacturerColorEntry entry)
        {
            SourceZipPath = sourceZipPath;
            SourceZipName = sourceZipName;
            GroupIndex = groupIndex;
            EntryIndex = entryIndex;
            Entry = entry;
            SwatchColor = WinColor.FromArgb(
                255,
                (byte)Math.Clamp(entry.PreviewColor.X * 255f, 0f, 255f),
                (byte)Math.Clamp(entry.PreviewColor.Y * 255f, 0f, 255f),
                (byte)Math.Clamp(entry.PreviewColor.Z * 255f, 0f, 255f));
            SwatchBrush = new SolidColorBrush(SwatchColor);
        }

        public SDX.Color4 ToColor4()
        {
            return new SDX.Color4(Entry.PreviewColor.X, Entry.PreviewColor.Y, Entry.PreviewColor.Z, 1f);
        }
    }
}