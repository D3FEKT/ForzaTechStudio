using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels.ThreeDViewer;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using HelixToolkit.SharpDX.Core;
using System.Collections.Concurrent;
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
        private SDX.Color4? _customManufacturerCarPaintColor;
        private bool _isUpdatingManufacturerColorSelection;
        private bool _isUpdatingManufacturerCustomColorUi;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _manufacturerColorApplyTimer;
        private bool _manufacturerColorApplyPending;
        private readonly ConcurrentDictionary<(ModelBinNode ModelBin, short MaterialId), MaterialBlob?> _viewportAssignedMaterialCache = new();
        private readonly ConcurrentDictionary<ViewportMaterialCacheKey, ViewportCachedMaterial> _viewportMaterialCache = new();

        private static readonly HashSet<uint> ViewportDiffuseColorHashes = new()
        {
            0xEA718FBE, // UniqueBaseColorColorParam
            0x53A946B6, // BaseColor_Tint
            0x6B242133, // BaseColor_TintMultiplier
            0x63040D89, // DiffuseColorColorParam
            0xF51639BE, // DiffuseColorGroupColorParam
            0x57C321A6, // ColorColorParam
            0x73A9E2DF, // ColorGroupColorParam
            0x1F3EB7A9, // DiffTintColorParam
            0xEF5CCE09, // DiffuseColorAColorParam
            0x76BEA808, // DiffuseColorBColorParam
            0x1F30F777, // GlassColor0ColorParam
            0x1925D9BF, // GlassColor
            0xD0F0433A, // DiffuseColor0
            0xA76D0485, // DiffuseTintColor
            0xD9826618, // DiffuseTintColor0
            0x00FC00E4, // Tint
            0x1F0BBA20, // BaseColor
            0x36976C2B, // CustomColor
            0x5D1D0449, // TransmissiveColor
        };

        private static readonly HashSet<uint> ViewportGlassColorHashes = new()
        {
            0x1F30F777, // GlassColor0ColorParam
            0x1925D9BF, // GlassColor
            0x5D1D0449, // TransmissiveColor
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

        private static readonly HashSet<uint> ViewportUTilingHashes = new()
        {
            0x19A7D8F1, // U_Tiling
            0xB01AEE8E, // U_Tiling observed in shaderbin parameter tables
        };

        private static readonly HashSet<uint> ViewportVTilingHashes = new()
        {
            0x4A3D8375, // V_Tiling
            0x3E95E96D, // V_Tiling observed in shaderbin parameter tables
        };

        private static readonly string[] ViewportCarPaintTokens =
        {
            "carpaint",
            "hood_carpaint",
            "carpaint_secondary",
            "mirror_carpaint",
            "wing_carpaint",
        };

        private static readonly HashSet<string> ViewportManufacturerColorMaterialNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "carpaint",
            "carpaint_secondary",
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

        private PhongMaterial CreateViewportMaterial(ForzaGeometryData data, ModelBinNode? modelBin, out bool isTransparent, bool loadTextures = true)
        {
            var key = CreateViewportMaterialCacheKey(data, modelBin, loadTextures);
            var cached = _viewportMaterialCache.GetOrAdd(key, _ =>
            {
                var materialBlob = ResolveAssignedMaterial(data, modelBin);
                var fallbackDiffuse = CreateFallbackDiffuseColor(data?.Name);
                var state = BuildViewportMaterialState(data, materialBlob, fallbackDiffuse, loadTextures);
                return new ViewportCachedMaterial(CreatePhongMaterial(state), state.IsTransparent);
            });

            isTransparent = cached.IsTransparent;
            return cached.Material;
        }

        private void ApplyViewportMaterial(MeshGeometryModel3D meshModel, ForzaGeometryData data, ModelBinNode? modelBin)
        {
            var material = CreateViewportMaterial(data, modelBin, out bool isTransparent);
            if (!ReferenceEquals(meshModel.Material, material))
                meshModel.Material = material;
            meshModel.IsTransparent = isTransparent;
        }

        private static PhongMaterial CreatePhongMaterial(ViewportRuntimeMaterialState state)
        {
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

        private ViewportMaterialCacheKey CreateViewportMaterialCacheKey(ForzaGeometryData data, ModelBinNode? modelBin, bool loadTextures)
        {
            bool useSingleColor = SingleColorToggle?.IsChecked == true;
            string textureSourceKey = _selectedTextureGameSource?.Key ?? string.Empty;

            return new ViewportMaterialCacheKey(
                modelBin,
                GetAssignedMaterialId(data),
                data?.Name ?? string.Empty,
                data?.MaterialName ?? string.Empty,
                data?.SourceMesh?.IsTransparent == true,
                useSingleColor,
                QuantizeMaterialFloat(_singleColor.Red),
                QuantizeMaterialFloat(_singleColor.Green),
                QuantizeMaterialFloat(_singleColor.Blue),
                QuantizeMaterialFloat(_sceneOpacity),
                GetManufacturerMaterialCacheKey(),
                loadTextures,
                _useLocalViewportTextures,
                _useLibraryViewportTextures,
                textureSourceKey);
        }

        private string GetManufacturerMaterialCacheKey()
        {
            if (_customManufacturerCarPaintColor.HasValue)
            {
                var color = _customManufacturerCarPaintColor.Value;
                return $"CUSTOM|{QuantizeMaterialFloat(color.Red)}|{QuantizeMaterialFloat(color.Green)}|{QuantizeMaterialFloat(color.Blue)}|{QuantizeMaterialFloat(color.Alpha)}";
            }

            return _selectedManufacturerColorItem?.Key ?? string.Empty;
        }

        private void InvalidateViewportMaterialCache()
        {
            _viewportMaterialCache.Clear();
        }

        private static int QuantizeMaterialFloat(float value)
        {
            return (int)MathF.Round(Clamp01(value) * 10000f);
        }

        private Vector2 ResolveViewportMaterialUvTiling(ForzaGeometryData data, ModelBinNode? modelBin)
        {
            return ResolveViewportMaterialUvTiling(ResolveAssignedMaterial(data, modelBin));
        }

        private static Vector2 ResolveViewportMaterialUvTiling(MaterialBlob? materialBlob)
        {
            if (materialBlob?.Bundle == null)
                return Vector2.One;

            var tiling = Vector2.One;

            foreach (var paramBlob in materialBlob.Bundle.Blobs.OfType<MaterialShaderParameterBlob>())
            {
                if (!IsViewportMaterialShaderParameterBlob(paramBlob))
                    continue;

                foreach (var parameter in paramBlob.Parameters)
                {
                    string parameterName = NameHashService.Instance.GetName(parameter.NameHash) ?? string.Empty;

                    if (TryReadShaderParameterFloat(parameter, out float scalarValue))
                    {
                        if (IsUTilingParameter(parameter, parameterName))
                            tiling.X = SanitizeTilingValue(scalarValue);
                        else if (IsVTilingParameter(parameter, parameterName))
                            tiling.Y = SanitizeTilingValue(scalarValue);
                    }
                    else if (TryReadShaderParameterVector2(parameter, out var vectorValue)
                        && IsUvTilingVectorParameter(parameter, parameterName))
                    {
                        tiling.X = SanitizeTilingValue(vectorValue.X);
                        tiling.Y = SanitizeTilingValue(vectorValue.Y);
                    }
                }
            }

            return tiling;
        }

        private ViewportRuntimeMaterialState BuildViewportMaterialState(ForzaGeometryData data, MaterialBlob? materialBlob, SDX.Color4 fallbackDiffuse, bool loadTextures = true)
        {
            bool isCarPaint = IsCarPaintMaterial(data, materialBlob);
            bool isManufacturerColorPaint = IsManufacturerColorPaintMaterial(data, materialBlob);
            bool isTransparentHint = data?.SourceMesh?.IsTransparent == true || IsTransparentMaterial(data, materialBlob);
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
                foreach (var paramBlob in materialBlob.Bundle.Blobs.OfType<MaterialShaderParameterBlob>())
                {
                    if (!IsViewportMaterialShaderParameterBlob(paramBlob))
                        continue;

                    foreach (var parameter in paramBlob.Parameters)
                    {
                        string parameterName = NameHashService.Instance.GetName(parameter.NameHash) ?? string.Empty;

                        if (parameter.Value is Vector4 vectorValue)
                        {
                            if (IsEmissiveParameter(parameter, parameterName))
                            {
                                emissive = new SDX.Color4(Clamp01(vectorValue.X), Clamp01(vectorValue.Y), Clamp01(vectorValue.Z), AlphaOrOne(vectorValue.W));
                                hasEmissiveColor = true;
                            }
                            else if (IsDiffuseColorParameter(parameter, parameterName))
                            {
                                diffuse = new SDX.Color4(Clamp01(vectorValue.X), Clamp01(vectorValue.Y), Clamp01(vectorValue.Z), alpha);
                                if (ShouldUseColorAlpha(parameter, parameterName))
                                    alpha *= AlphaOrOne(vectorValue.W);
                                hasDiffuseColor = true;
                                isTransparentHint |= IsGlassColorParameter(parameter, parameterName);
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
                        else if (loadTextures && parameter.Type == ShaderParameterType.Texture2D)
                        {
                            if (IsEmissiveParameter(parameter, parameterName))
                                hasEmissiveTexture = true;

                            if (parameter.Value is TextureParameter textureParameter)
                                ApplyViewportTextureParameter(parameter, textureParameter, textureMaps);
                        }
                    }
                }
            }

            var selectedManufacturerColor = TryGetManufacturerColorForMaterial(isManufacturerColorPaint);
            if (selectedManufacturerColor.HasValue)
            {
                var manufacturerColor = selectedManufacturerColor.Value;
                alpha = Clamp01(alpha * manufacturerColor.Alpha);
                diffuse = new SDX.Color4(manufacturerColor.Red, manufacturerColor.Green, manufacturerColor.Blue, alpha);
                emissive = new SDX.Color4(0f, 0f, 0f, 1f);
            }
            else if (isCarPaint)
            {
                var carPaintColor = new SDX.Color4(_singleColor.Red, _singleColor.Green, _singleColor.Blue, 1f);
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
            bool isTransparent = isTransparentHint || diffuse.Alpha < 0.995f;

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
                IsTransparent = isTransparent,
            };
        }

        private SDX.Color4? TryGetManufacturerColorForMaterial(bool isManufacturerColorPaint)
        {
            if (!isManufacturerColorPaint)
                return null;

            if (_customManufacturerCarPaintColor.HasValue)
                return _customManufacturerCarPaintColor.Value;

            var selectedItem = _selectedManufacturerColorItem;
            if (selectedItem == null)
                return null;

            return selectedItem.ToColor4();
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

        private MaterialBlob? ResolveAssignedMaterial(ForzaGeometryData data, ModelBinNode? modelBin)
        {
            if (data?.SourceMesh == null || modelBin?.Bundle == null)
                return null;

            short assignedId = GetAssignedMaterialId(data);
            var key = (modelBin, assignedId);
            if (_viewportAssignedMaterialCache.TryGetValue(key, out var cachedMaterial))
                return cachedMaterial;

            var material = modelBin.Bundle.Blobs
                .OfType<MaterialBlob>()
                .FirstOrDefault(material => (short)GetMaterialId(material) == assignedId);
            _viewportAssignedMaterialCache[key] = material;
            return material;
        }

        private static short GetAssignedMaterialId(ForzaGeometryData data)
        {
            if (data?.SourceMesh == null)
                return -1;

            return data.SourceMesh.MaterialIds != null && data.SourceMesh.MaterialIds.Length > 1
                ? data.SourceMesh.MaterialIds[1]
                : data.SourceMesh.MaterialId;
        }

        private static uint GetMaterialId(MaterialBlob material)
        {
            return material.Metadatas.OfType<IdentifierMetadata>().FirstOrDefault()?.Id ?? material.Id;
        }

        private static bool IsDiffuseColorParameter(ShaderParameter parameter, string parameterName)
        {
            return ViewportDiffuseColorHashes.Contains(parameter.NameHash)
                || IsGlassColorParameter(parameter, parameterName)
                || IsViewportRgbTintParameterName(parameterName);
        }

        private static bool IsGlassColorParameter(ShaderParameter parameter, string parameterName)
        {
            return ViewportGlassColorHashes.Contains(parameter.NameHash)
                || parameterName.Contains("glasscolor", StringComparison.OrdinalIgnoreCase)
                || parameterName.Contains("transmissivecolor", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsViewportRgbTintParameterName(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
                return false;

            if (ContainsAny(parameterName, "emissive", "illumination", "radcolour", "radcolor", "headlight", "taillight", "brakelight", "reverselight", "markerlight"))
                return false;

            if (ContainsAny(parameterName, "normal", "rough", "metal", "gloss", "specular", "opacity", "alpha", "mask", "ao"))
                return false;

            if (parameterName.Equals("Color", StringComparison.OrdinalIgnoreCase)
                || parameterName.Equals("Tint", StringComparison.OrdinalIgnoreCase)
                || parameterName.Equals("BaseColor", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ContainsAny(parameterName, "basecolor", "diffuse", "difftint", "tintcolor", "diffusetint", "customcolor", "carbonfibercolor")
                || parameterName.Contains("colorparam", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldUseColorAlpha(ShaderParameter parameter, string parameterName)
        {
            return parameterName.Contains("alpha", StringComparison.OrdinalIgnoreCase)
                || parameterName.Contains("opacity", StringComparison.OrdinalIgnoreCase)
                || parameterName.Contains("transparency", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsViewportMaterialShaderParameterBlob(MaterialShaderParameterBlob blob)
        {
            return blob.Tag == ForzaTools.Bundles.Bundle.TAG_BLOB_MaterialShaderParameter
                || blob.Tag == ForzaTools.Bundles.Bundle.TAG_BLOB_DefaultShaderParameter;
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

        private static bool IsUTilingParameter(ShaderParameter parameter, string parameterName)
        {
            return ViewportUTilingHashes.Contains(parameter.NameHash)
                || parameterName.Equals("U_Tiling", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVTilingParameter(ShaderParameter parameter, string parameterName)
        {
            return ViewportVTilingHashes.Contains(parameter.NameHash)
                || parameterName.Equals("V_Tiling", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUvTilingVectorParameter(ShaderParameter parameter, string parameterName)
        {
            return parameterName.Contains("uvtiling", StringComparison.OrdinalIgnoreCase)
                || parameterName.Contains("tilingoverride", StringComparison.OrdinalIgnoreCase)
                || parameterName.Equals("BaseColorAlphaTilingOverride", StringComparison.OrdinalIgnoreCase)
                || parameterName.Equals("BaseColorTilingOverride", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadShaderParameterFloat(ShaderParameter parameter, out float value)
        {
            value = 0f;

            switch (parameter.Value)
            {
                case float floatValue:
                    value = floatValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case bool boolValue:
                    value = boolValue ? 1f : 0f;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryReadShaderParameterVector2(ShaderParameter parameter, out Vector2 value)
        {
            value = Vector2.One;

            switch (parameter.Value)
            {
                case Vector2 vector2Value:
                    value = vector2Value;
                    return true;
                case Vector4 vector4Value:
                    value = new Vector2(vector4Value.X, vector4Value.Y);
                    return true;
                default:
                    return false;
            }
        }

        private static float SanitizeTilingValue(float value)
        {
            return float.IsFinite(value) && MathF.Abs(value) > 1e-6f ? value : 1f;
        }

        private static bool IsCarPaintMaterial(ForzaGeometryData data, MaterialBlob? materialBlob)
        {
            return MaterialTextContains(data, materialBlob, ViewportCarPaintTokens);
        }

        private static bool IsManufacturerColorPaintMaterial(ForzaGeometryData data, MaterialBlob? materialBlob)
        {
            foreach (string identifier in EnumerateViewportMaterialIdentifiers(data, materialBlob))
            {
                if (ViewportManufacturerColorMaterialNames.Contains(identifier))
                    return true;
            }

            return false;
        }

        private static bool IsTransparentMaterial(ForzaGeometryData data, MaterialBlob? materialBlob)
        {
            return MaterialTextContains(data, materialBlob, ViewportTransparentMaterialTokens);
        }

        private static bool MaterialTextContains(ForzaGeometryData data, MaterialBlob? materialBlob, IEnumerable<string> tokens)
        {
            string text = BuildMaterialSearchText(data, materialBlob);

            foreach (string token in tokens)
            {
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool ManufacturerColorTargetsMaterial(ManufacturerColorEntry entry, ForzaGeometryData data, MaterialBlob? materialBlob)
        {
            string materialText = BuildMaterialSearchText(data, materialBlob);

            if (!string.IsNullOrWhiteSpace(entry.Path) && MaterialTokenMatches(materialText, entry.Path))
                return true;

            foreach (string materialName in entry.MaterialNames ?? Enumerable.Empty<string>())
            {
                if (MaterialTokenMatches(materialText, materialName))
                    return true;
            }

            short assignedId = GetAssignedMaterialId(data);
            if (entry.MaterialIndexMask != 0 && assignedId >= 0 && assignedId < 32)
                return (entry.MaterialIndexMask & (1u << assignedId)) != 0;

            return false;
        }

        private static bool MaterialTokenMatches(string materialText, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            string normalizedToken = token.Replace('\\', '/').Trim();
            if (materialText.Contains(normalizedToken, StringComparison.OrdinalIgnoreCase))
                return true;

            string leaf = System.IO.Path.GetFileNameWithoutExtension(normalizedToken.Replace('/', '\\'));
            return !string.IsNullOrWhiteSpace(leaf)
                && materialText.Contains(leaf, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildMaterialSearchText(ForzaGeometryData data, MaterialBlob? materialBlob)
        {
            return string.Join(" ", new[]
            {
                data?.Name ?? string.Empty,
                data?.MaterialName ?? string.Empty,
                materialBlob?.Metadatas.OfType<NameMetadata>().FirstOrDefault()?.Name ?? string.Empty,
                GetMaterialResourcePath(materialBlob),
            }).Replace('\\', '/');
        }

        private static IEnumerable<string> EnumerateViewportMaterialIdentifiers(ForzaGeometryData data, MaterialBlob? materialBlob)
        {
            if (!string.IsNullOrWhiteSpace(data?.MaterialName))
                yield return NormalizeViewportMaterialIdentifier(data.MaterialName);

            string? metadataName = materialBlob?.Metadatas.OfType<NameMetadata>().FirstOrDefault()?.Name;
            if (!string.IsNullOrWhiteSpace(metadataName))
                yield return NormalizeViewportMaterialIdentifier(metadataName);

            string resourcePath = GetMaterialResourcePath(materialBlob);
            if (!string.IsNullOrWhiteSpace(resourcePath))
                yield return NormalizeViewportMaterialIdentifier(resourcePath);
        }

        private static string NormalizeViewportMaterialIdentifier(string value)
        {
            string normalized = (value ?? string.Empty).Trim().Replace('\\', '/');
            if (normalized.Length == 0)
                return string.Empty;

            int slashIndex = normalized.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < normalized.Length - 1)
                normalized = normalized[(slashIndex + 1)..];

            return System.IO.Path.GetFileNameWithoutExtension(normalized) ?? normalized;
        }

        private static string GetMaterialResourcePath(MaterialBlob? materialBlob)
        {
            return materialBlob?.Bundle?.Blobs.OfType<MaterialResourceBlob>().FirstOrDefault()?.Path ?? string.Empty;
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

            // Suppress SelectionChanged events fired by Clear() / Add() during the rebuild.
            _isUpdatingManufacturerColorSelection = true;
            try
            {
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
            }
            finally
            {
                _isUpdatingManufacturerColorSelection = false;
            }

            UpdateManufacturerColorControls(selectedKey);
        }

        private void UpdateManufacturerColorControls(string? selectedKey = null)
        {
            if (ManufacturerColorCombo == null)
                return;

            _isUpdatingManufacturerColorSelection = true;
            try
            {
                // Set ItemsSource only once; ObservableCollection notifies the list of item changes automatically
                if (ManufacturerColorCombo.ItemsSource != ManufacturerColorItems)
                    ManufacturerColorCombo.ItemsSource = ManufacturerColorItems;

                var selectedItem = !string.IsNullOrWhiteSpace(selectedKey)
                    ? ManufacturerColorItems.FirstOrDefault(item => item.Key == selectedKey)
                    : _selectedManufacturerColorItem != null
                        ? ManufacturerColorItems.FirstOrDefault(item => item.Key == _selectedManufacturerColorItem.Key)
                        : null;

                ManufacturerColorCombo.SelectedItem = selectedItem;
                _selectedManufacturerColorItem = selectedItem;
                _manufacturerCarPaintColor = selectedItem?.ToColor4();
            }
            finally
            {
                _isUpdatingManufacturerColorSelection = false;
            }

            bool hasColors = ManufacturerColorItems.Count > 0;
            ManufacturerColorCombo.IsEnabled = hasColors;
            ResetManufacturerColorBtn.IsEnabled = _selectedManufacturerColorItem != null || _customManufacturerCarPaintColor.HasValue;
            ManufacturerColorStatusText.Text = hasColors
                ? $"{ManufacturerColorItems.Count} manufacturer color(s) loaded."
                : "No manufacturercolors.bin found in the loaded car zip.";

            SyncManufacturerCustomColorControls();
        }

        private void ManufacturerColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingManufacturerColorSelection)
                return;

            var newItem = e.AddedItems.OfType<ViewportManufacturerColorItem>().FirstOrDefault()
                ?? ManufacturerColorCombo.SelectedItem as ViewportManufacturerColorItem;

            if (newItem == null)
                return;

            _selectedManufacturerColorItem = newItem;
            _manufacturerCarPaintColor = _selectedManufacturerColorItem.ToColor4();
            _customManufacturerCarPaintColor = null;
            SyncManufacturerCustomColorControls();
            ResetManufacturerColorBtn.IsEnabled = true;
            ManufacturerColorStatusText.Text = $"Selected {_selectedManufacturerColorItem.DisplayName}.";
            InvalidateViewportMaterialCache();
            UpdateMeshColors(SingleColorToggle?.IsChecked ?? false);
        }

        private void ResetManufacturerColor_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _selectedManufacturerColorItem = null;
            _manufacturerCarPaintColor = null;
            _customManufacturerCarPaintColor = null;
            _manufacturerColorApplyPending = false;
            _manufacturerColorApplyTimer?.Stop();
            SyncManufacturerCustomColorControls();

            if (ManufacturerColorCombo != null)
            {
                _isUpdatingManufacturerColorSelection = true;
                ManufacturerColorCombo.SelectedItem = null;
                _isUpdatingManufacturerColorSelection = false;
            }

            ResetManufacturerColorBtn.IsEnabled = false;
            ManufacturerColorStatusText.Text = ManufacturerColorItems.Count > 0
                ? $"{ManufacturerColorItems.Count} manufacturer color(s) loaded."
                : "No manufacturercolors.bin found in the loaded car zip.";
            InvalidateViewportMaterialCache();
            UpdateMeshColors(SingleColorToggle?.IsChecked ?? false);
        }

        private void ManufacturerCustomColorBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isUpdatingManufacturerCustomColorUi)
                return;

            var color = ReadManufacturerCustomColorFromBoxes();
            ApplyManufacturerCustomColor(color);
        }

        private void ManufacturerCustomColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (_isUpdatingManufacturerCustomColorUi)
                return;

            ApplyManufacturerCustomColor(ToColor4(args.NewColor));
        }

        private void ApplyManufacturerCustomColor(SDX.Color4 color)
        {
            _customManufacturerCarPaintColor = color;
            _selectedManufacturerColorItem = null;
            _manufacturerCarPaintColor = null;

            if (ManufacturerColorCombo != null)
            {
                _isUpdatingManufacturerColorSelection = true;
                ManufacturerColorCombo.SelectedItem = null;
                _isUpdatingManufacturerColorSelection = false;
            }

            SyncManufacturerCustomColorControls();
            ResetManufacturerColorBtn.IsEnabled = true;
            ManufacturerColorStatusText.Text = "Using custom RGBA carpaint color.";

            // Debounce heavy render updates 
            ScheduleManufacturerColorApply();
        }

        private void FlushManufacturerColorApply()
        {
            _manufacturerColorApplyPending = false;
            _manufacturerColorApplyTimer?.Stop();
            InvalidateViewportMaterialCache();
            UpdateMeshColors(SingleColorToggle?.IsChecked ?? false);
        }

        private void ScheduleManufacturerColorApply()
        {
            _manufacturerColorApplyPending = true;

            if (_manufacturerColorApplyTimer == null)
            {
                _manufacturerColorApplyTimer = DispatcherQueue.CreateTimer();
                _manufacturerColorApplyTimer.Interval = TimeSpan.FromMilliseconds(80);
                _manufacturerColorApplyTimer.IsRepeating = false;
                _manufacturerColorApplyTimer.Tick += (s, e) =>
                {
                    if (_manufacturerColorApplyPending)
                        FlushManufacturerColorApply();
                };
            }

            _manufacturerColorApplyTimer.Stop();
            _manufacturerColorApplyTimer.Start();
        }


        private void ManufacturerCustomColorFlyout_Closed(object? sender, object e)
        {
            if (_manufacturerColorApplyPending)
                FlushManufacturerColorApply();
        }

        private SDX.Color4 ReadManufacturerCustomColorFromBoxes()
        {
            return new SDX.Color4(
                (float)(ClampColorChannelBox(ManufacturerColorRedBox?.Value ?? 128) / 255.0),
                (float)(ClampColorChannelBox(ManufacturerColorGreenBox?.Value ?? 128) / 255.0),
                (float)(ClampColorChannelBox(ManufacturerColorBlueBox?.Value ?? 128) / 255.0),
                (float)(ClampColorChannelBox(ManufacturerColorAlphaBox?.Value ?? 255) / 255.0));
        }

        private void SyncManufacturerCustomColorControls()
        {
            if (ManufacturerColorRedBox == null || ManufacturerCustomColorPicker == null)
                return;

            var color = _customManufacturerCarPaintColor ?? new SDX.Color4(0.5f, 0.5f, 0.5f, 1f);
            var winColor = ToWinColor(color);

            _isUpdatingManufacturerCustomColorUi = true;
            try
            {
                ManufacturerColorRedBox.Value = Math.Round(color.Red * 255.0);
                ManufacturerColorGreenBox.Value = Math.Round(color.Green * 255.0);
                ManufacturerColorBlueBox.Value = Math.Round(color.Blue * 255.0);
                ManufacturerColorAlphaBox.Value = Math.Round(color.Alpha * 255.0);
                ManufacturerCustomColorPicker.Color = winColor;
                ManufacturerCustomColorPreview.Background = new SolidColorBrush(winColor);
            }
            finally
            {
                _isUpdatingManufacturerCustomColorUi = false;
            }
        }

        private static double ClampColorChannelBox(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0;

            return Math.Clamp(Math.Round(value), 0, 255);
        }

        private static SDX.Color4 ToColor4(WinColor color)
        {
            return new SDX.Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        }

        private static WinColor ToWinColor(SDX.Color4 color)
        {
            return WinColor.FromArgb(
                (byte)Math.Clamp(Math.Round(Clamp01(color.Alpha) * 255f), 0, 255),
                (byte)Math.Clamp(Math.Round(Clamp01(color.Red) * 255f), 0, 255),
                (byte)Math.Clamp(Math.Round(Clamp01(color.Green) * 255f), 0, 255),
                (byte)Math.Clamp(Math.Round(Clamp01(color.Blue) * 255f), 0, 255));
        }

        private readonly record struct ViewportMaterialCacheKey(
            ModelBinNode? ModelBin,
            short MaterialId,
            string GeometryName,
            string MaterialName,
            bool SourceTransparent,
            bool UseSingleColor,
            int SingleColorRed,
            int SingleColorGreen,
            int SingleColorBlue,
            int SceneOpacity,
            string ManufacturerColorKey,
            bool LoadTextures,
            bool UseLocalTextures,
            bool UseLibraryTextures,
            string TextureSourceKey);

        private sealed class ViewportCachedMaterial
        {
            public PhongMaterial Material { get; }
            public bool IsTransparent { get; }

            public ViewportCachedMaterial(PhongMaterial material, bool isTransparent)
            {
                Material = material;
                IsTransparent = isTransparent;
            }
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
            public bool IsTransparent { get; init; }
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
