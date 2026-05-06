using System;
using System.Collections.Generic;
using System.Linq;

namespace ForzaTechStudio.Services
{
    public class NameHashService
    {
        private static Lazy<NameHashService> _instance = new Lazy<NameHashService>(() => new NameHashService());
        public static NameHashService Instance => _instance.Value;

        private Dictionary<uint, string> _hashToName;
        private Dictionary<string, uint> _nameToHash;

        public NameHashService()
        {
            InitializeData();
        }

        public string GetName(uint hash)
        {
            if (_hashToName.TryGetValue(hash, out var name))
                return name;
            return null; // Or return hex string? Better to return null and let UI decide fallback
        }

        public uint? GetHash(string name)
        {
            if (_nameToHash.TryGetValue(name, out var hash))
                return hash;
            return null;
        }

        public IReadOnlyDictionary<uint, string> GetAll() => _hashToName;

        private void InitializeData()
        {
            _hashToName = new Dictionary<uint, string>
            {
                // Copied from grub template, thanks doliman 😀
                { 0xEA718FBE, "UniqueBaseColorColorParam" },
                { 0x0014A502, "PaintColorGroupColorParam" },
                { 0xC0CB2820, "PaintColorColorParam" },
                { 0xA415641F, "PaintFlakeF0Vector4" },
                { 0x3FA9F5C9, "PaintFinishColorParam" },
                { 0x1BB17DD2, "radColour1ColorParam_pg_radiosity" },
                { 0x43AFD4FA, "radColour2ColorParam_pg_radiosity" },
                { 0xC28AB1DD, "radColour3ColorParam_pg_radiosity" },
                { 0x003E4460, "PaintFinishBColorParam" },
                { 0x63040D89, "DiffuseColorColorParam" },
                { 0x1F9B6488, "F0aVector4" },
                { 0x9114636B, "F0bVector4" },
                { 0x23C8B47A, "ClearCoatF0Vector4" },
                { 0xF51639BE, "DiffuseColorGroupColorParam" },
                { 0x0E29312A, "ReflectionTintColorParam" },
                { 0x57C321A6, "ColorColorParam" },
                { 0x938926B0, "F0Vector4" },
                { 0x73A9E2DF, "ColorGroupColorParam" },
                { 0x0B379E68, "CarbonClearCoatColorParam" },
                { 0xC308C931, "IlluminationColoColorParam" },
                { 0x1F3EB7A9, "DiffTintColorParam" },
                { 0x9BCED46E, "IlluminationColorColorParam" },
                { 0xEF5CCE09, "DiffuseColorAColorParam" },
                { 0x76BEA808, "DiffuseColorBColorParam" },
                { 0xC017A27E, "ReflectionTintColorColorParam" },
                { 0x8467AAA4, "g_CarUserColor0" },
                { 0x1F30F777, "GlassColor0ColorParam" },
                { 0x108D7FE5, "CrackF0Vector4" },
                { 0x1CE52912, "OrangePeelTiling" },
                { 0x8ED8D865, "FlakeNormalTiling" },
                { 0x46E92CB5, "CH1MaskTiling" },
                { 0x730F2086, "NormalTiling" },
                { 0xC19C70CD, "DiffuseATiling" },
                { 0x3EC91ED5, "GlossTiling" },
                { 0xCB4FD76E, "RTintTiling" },
                { 0x75D6E294, "LocalAOTiling" },
                { 0x519B26A1, "CH2DiffuseTextureTiling" },
                { 0x53372732, "CH2RTintTiling" },
                { 0xB879D9F0, "LightMapTiling" },
                { 0x7495F9AC, "AlphaTiling" },
                { 0x8591411B, "CH1GlossDiffMaskTiling" },
                { 0x114A45A9, "CH2NormalTiling" },
                { 0xAC802967, "CH1NormalTiling" },
                { 0x0222B0B9, "CH1NormalMaskTiling" },
                { 0x2F354D91, "AOTiling" },
                { 0x548284FC, "CH1_CLCNormalMap0Tiling" },
                { 0x74226C85, "BaseCoatScalar_floatVal" },
                { 0xA2896771, "stepMinRimSideAngle_floatVal_surfaceFX_snow_surfaceFX" },
                { 0x193593DA, "stepMaxRimSideAngle_floatVal_surfaceFX_snow_surfaceFX" },
                { 0x8CF21130, "lerpMinRimSideAngle_floatVal_surfaceFX_snow_surfaceFX" },
                { 0x374EE59B, "lerpMaxRimSideAngle_floatVal_surfaceFX_snow_surfaceFX" },
                { 0x8EDE2B71, "stepMinRimMask_floatVal_surfaceFX_snow_surfaceFX" },
                { 0xB893E9E2, "stepMaxRimMask_floatVal_surfaceFX_snow_surfaceFX" },
                { 0x86EF8FB1, "FlakeAmount_floatVal" },
                { 0x3F770DFC, "CH1NormalHeightScale" },
                { 0xA49CC530, "OrangePeelHeightScale" },
                { 0xC8C003A0, "RainUVScale_floatVal_basewaterbeading_uvrot0" },
                { 0x33E9FD2B, "DirectionalFalloff_Offset_floatVal_basewaterbeading_uvrot0" },
                { 0xB7BB947A, "DirectionalFalloff_Tightness_floatVal_basewaterbeading_uvrot0" },
                { 0x99CC69B1, "FlakeGloss_floatVal" },
                { 0x2A57C2B6, "FlakeNormalIntensity_floatVal" },
                { 0x18F91688, "PaintType_floatVal" },
                { 0x8F88A8DD, "radEV1_floatVal_pg_radiosity" },
                { 0xF96D91E0, "radEV2_floatVal_pg_radiosity" },
                { 0x621E7B34, "radEV3_floatVal_pg_radiosity" },
                { 0xD6B74780, "FlakeAmountB_floatVal" },
                { 0xA75C82D2, "RainUVScale_floatVal_basewaterbeading" },
                { 0x2C6CEF66, "DirectionalFalloff_Offset_floatVal_basewaterbeading" },
                { 0xA9383667, "DirectionalFalloff_Tightness_floatVal_basewaterbeading" },
                { 0xD6A86466, "FlakeGlossB_floatVal" },
                { 0xBB022974, "NormalHeightScale" },
                { 0x52E99DA3, "GlossA_floatVal" },
                { 0xB9DE26A0, "GlossB_floatVal" },
                { 0x7E88DE7D, "ClearCoatGloss_floatVal" },
                { 0x00CE7933, "NormalSpecPower" },
                { 0x7A549CB2, "DiffuseDarkeningMaxPorosity_floatVal_basewaterbeading_nonclearcoat" },
                { 0x5FF94E67, "Gloss_floatVal" },
                { 0x18385024, "RainUVScale_floatVal_basewaterbeading_uvrot" },
                { 0xE58ABE7B, "DirectionalFalloff_Offset_floatVal_basewaterbeading_uvrot" },
                { 0x09A23168, "DirectionalFalloff_Tightness_floatVal_basewaterbeading_uvrot" },
                { 0x2DB53178, "FlakeGlossDmg_floatVal" },
                { 0x6F45C5F1, "EmissiveType_floatVal" },
                { 0x1364DE85, "pgModEV_floatVal" },
                { 0xF9F19BA6, "PartDamageThreshold_floatVal" },
                { 0x495BB2FB, "UVRotation_floatVal" },
                { 0xB0B8947E, "uTile_floatVal" },
                { 0xCCD9B1A5, "vTile_floatVal" },
                { 0xD5F1D09E, "CH2NormalHeightScale" },
                { 0xF94165FE, "CH1_CLCNormalMap0HeightScale" },
                { 0x8F731786, "RainUVScale_floatVal_basewaterbeading_interior" },
                { 0x6F3A994F, "AllowUseColorBool" },
                { 0x5393C778, "radFunction1CarLightParam_pg_radiosity" },
                { 0xE007EABB, "radFunction2CarLightParam_pg_radiosity" },
                { 0x8E8BF1FA, "radFunction3CarLightParam_pg_radiosity" },
                { 0x493073A0, "LightFunctionCarLightParam" },
                { 0x9615CAAA, "UniqueBaseTextureSwitchBool" },
                { 0x78004A9C, "ColorGroupSwitchBool" },
                { 0xD047A271, "UseDiffuseAlphaBool" },
                { 0x7169AC81, "CH1DiffuseTextureSwitchBool" },
                { 0xE1D827FD, "UniqueBaseColorSwitchBool" },
                { 0xFF73057F, "LiverySwitchBool" },
                { 0x9A8DF740, "DamageSwitchBool" },
                { 0x61730CD3, "ScrapeSwitchBool" },
                { 0xF03E432F, "FlakeMaskSwitchBool" },
                { 0x76C4A90E, "CH1NormalSwitchBool" },
                { 0xE159D67B, "CH1AlphaSwitchBool" },
                { 0xE4AAD859, "FlakeGlossMaskBool" },
                { 0x9554D18D, "GlossMaskSwitchBool" },
                { 0x05A401E7, "DiffuseTextureSwitchBool" },
                { 0x08B2C17F, "CH1MaskSwitchBool" },
                { 0xCBB3D988, "CH1OpacitySwitchBool" },
                { 0x04F8F9FA, "CH1DiffColTextureSwitchBool" },
                { 0x0F70E9CC, "RTintMaskSwitchBool" },
                { 0x71263EB7, "CH1F0MaskSwitchBool" },
                { 0x5A0DA36A, "CH1GlossMaskSwitchBool" },
                { 0xE876DDCC, "CH1LocalAOSwitchBool" },
                { 0xFE5BAD37, "CH1LocalAOSwitch0Bool" },
                { 0xA6BF15E8, "CH1OpacityMaskSwitchBool" },
                { 0x4E3A085E, "RTintTextureSwitchBool" },
                { 0x6C03F944, "LocalAOSwitchBool" },
                { 0x255EF28A, "CH2NormalSwitchBool" },
                { 0x07ACB91F, "RGBMapSwitchBool" },
                { 0x0BF3318B, "EmissiveOnOffBool" },
                { 0xE5BC49CB, "AlphaSwitchBool" },
                { 0x989B026F, "MetalSwitchBool" },
                { 0x7487EB77, "FillSwitchBool" },
                { 0x553D641D, "CH1NormalMapSwitchBool" },
                { 0x6F80D6E1, "CH2RTintMaskSwitchBool" },
                { 0x22BC6533, "CH2F0MaskSwitchBool" },
                { 0xFA9429D7, "CH2NormalMapSwitchBool" },
                { 0xF5A4EEA0, "CH2GlossMaskSwitchBool" },
                { 0x02F000AE, "CH2LocalAOSwitchBool" },
                { 0x2B935F14, "CH2DirectAOSwitchBool" },
                { 0x9FC7B8A8, "CH2OpacityMaskSwitchBool" },
                { 0x6FA53B68, "CH2DiffuseLERPSwitchBool" },
                { 0x56DD9628, "CH1DiffuseLERPSwitchBool" },
                { 0x88C5D9BD, "CH1DirectAOSwitchBool" },
                { 0x48287560, "ClearCoatNormalSwitch0Bool" },
                { 0x22819153, "DialRadiositySwitchBool_dialRadiosity" },
                { 0x66E53F62, "CH1AlphaTextureTexture" },
                { 0x10350BBC, "CH1DiffuseTextureTexture" },
                { 0x5BB7DA76, "CH1NormalTexture" },
                { 0xB59BE3AB, "FlakeNormalTexture" },
                { 0x8F186353, "HighNoiseTextureTexture_surfaceFX_snow_surfaceFX" },
                { 0x26169D54, "LowNoiseTextureTexture_surfaceFX_snow_surfaceFX" },
                { 0x8B653400, "MudTileDirectionalLeftTexture_surfaceFX_opus_surfaceFX" },
                { 0x154A5458, "MudTileNonDirectionalTexture_surfaceFX_opus_surfaceFX" },
                { 0x5F37B059, "NormalMapWithIntensityTexture" },
                { 0xD7D2D75D, "NormalMapWithIntensityTexture_surfaceFX_opus_surfaceFX" },
                { 0x8382A669, "NormalMapWithIntensityTexture_surfaceFX_snow_surfaceFX" },
                { 0x7B683AC5, "OrangePeelTexture" },
                { 0x99EE46B6, "radTextureTexture_pg_radiosity" },
                { 0x9D396DAD, "SnowDiffuseTextureTexture_surfaceFX_snow_surfaceFX" },
                { 0xCA80B33B, "TexLowFrequencyNoiseTexture_surfaceFX_opus_surfaceFX" },
                { 0x16F462B7, "TextureTexture" },
                { 0x05EECB86, "TextureWetnessEdgeTexture_surfaceFX_opus_surfaceFX" },
                { 0x51BEBAB2, "TextureWetnessEdgeTexture_surfaceFX_snow_surfaceFX" },
                { 0x3380008B, "CH1MaskTexture" },
                { 0x6DD98CD9, "DiffuseATexture" },
                { 0x8C658791, "NormalTexture" },
                { 0x7E4A41E1, "GlossTexture" },
                { 0x7FDA2F1B, "LocalAOTexture" },
                { 0x220CAD2C, "RTintTexture" },
                { 0x57D9D49E, "AlphaTexture" },
                { 0x294DA6FC, "CH2DiffuseTextureTexture" },
                { 0x4049C803, "CH2RTintTexture" },
                { 0x35C82561, "LightMapTexture" },
                { 0x64C94C50, "SplatterYTexture" },
                { 0x043E9751, "GlossVariationMapTexture" },
                { 0x0FEA383B, "AOTexture" },
                { 0x022DF609, "CH1GlossDiffMaskTexture" },
                { 0x3A72873C, "CH1NormalMaskTexture" },
                { 0x27D6FFAD, "CH2NormalTexture" },
                { 0x3C929217, "CH1_CLCNormalMap0Texture" },
                { 0xA27F63E2, "ShatterMapTexture" },
                { 0xF01369AD, "SamplerStatesLiverySampler" },
                { 0x41359340, "SamplerStatesSampler_pg_radiosity" },
                { 0xB74664B2, "SamplerStatesSampler_surfaceFX" },
                { 0xEEACBDFB, "pgSamplerStatesSampler" },
                { 0xA8B882A2, "SamplerStatesSampler" },
                { 0x80846FBD, "SamplerStatesSampler_dialRadiosity" }
            };


            AddEngineNames(_engineParameterNames);

            AddEngineNames(_lightScenarioNames);

            _nameToHash = _hashToName.ToDictionary(x => x.Value, x => x.Key);
        }

        private void AddEngineNames(string[] names)
        {
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                uint hash = ComputeHashName(name);
                _hashToName.TryAdd(hash, name);
            }
        }

        // CRC-32 IEEE 802.3 (poly 0xEDB88320, reflected) — case-sensitive, matches engine HashName
        private static uint ComputeHashName(string name)
        {
            const uint Poly = 0xEDB88320u;
            uint crc = 0xFFFFFFFFu;
            foreach (char c in name)
            {
                crc ^= (byte)c;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ Poly : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }

        // Engine parameter binding slot names 
        private static readonly string[] _engineParameterNames =
        [
            "ColorShaderParameter", "FloatShaderParameter", "ScaleShaderParameter",
            "BoolShaderParameter", "ColorGradientShaderParameter", "SwizzleShaderParameter",
            "FunctionRangeShaderParameter", "Vector2ShaderParameter", "Vector4ShaderParameter",
            "Vector3ShaderParameter", "Texture2DShaderParameter", "SamplerShaderParameter",
            "RenderTargetShaderParameter", "CarShaderParameter", "IntShaderParameter",
            "CarDirtShaderParameter", "CarLightParameter", "TrackShaderParameter",
            "CarWheelBlurShaderParameter", "ImageProcessorTexDimParameter", "ProxyLodCarParameter",
            "LightGlowInitOcclusionsParameter", "BinkVertexParameter",
            "CarLightScenarioWetVSParameter", "ImageProcessorDebugLightingVisualizerParameter",
            "LensFlareLensTextureParameter", "RacelineGSConstantParameter",
            "ParticleConstantPSParameter", "ImageProcessorDualTexturesParameter",
            "ImageProcessorBokehSpriteTexturesParameter", "CrowdLightScenarioParameter",
            "PoseParameter", "CarModelParameter", "LightGlowTexturesParameter",
            "MaterialSamplersParameter", "ImageProcessorSingleTextureParameter",
            "IBLBakedDaySHToCubemapParameter", "LensFlareObjectConstantsParameter",
            "DropShadowConstantParameter", "ImageProcessorShadowMapBlurParameter",
            "ProjectedShadowGlobalRootParameter", "CarDebugLightingLightScenarioParameter",
            "LiverySamplersParameter", "CrowdSpritePerSectionRootParameter",
            "TrackLightScenarioDynamicParameter", "TrackTexturesParameter",
            "QuarterResSamplersParameter", "LensFlareHoopParameter", "LightGlowInstancesParameter",
            "ImageProcessorPostProcessParameter", "LightGlowPresetOcclusionsParameter",
            "GlobalParameter", "BinkSamplerParameter", "ImageProcessorSamplersParameter",
            "FMDynamicSkySamplerParameter", "ShadowMapSamplerParameter",
            "LensFlareCommonParameter", "ImageProcessorSkyQuadParameter",
            "RacelinePSConstantParameter", "ParticleLightScenarioParameter",
            "TrackLightScenarioParameter", "ImageProcessorWritableTextureParameter",
            "ImageProcessorProjectedShadowBlurParameter", "GenerateMipSamplerParameter",
            "ModelObjectParameter", "ParticleDataParameter", "DebugLightingCarModelParameter",
            "LightGlowSamplersParameter", "MaterialParameter", "ForwardPlusCullViewConstParameter",
            "LuminanceHistogramSamplerParameter", "LensFlareInstanceConstantsParameter",
            "ProjectedShadowModelRootParameter", "IBLBakedNightSHToCubemapParameter",
            "LiveryMaskConstantParameter", "DownSample9SamplerParameter",
            "QuarterResTexturesParameter", "ImageProcessorBokehAppendBufferParameter",
            "LightGlowConstParameter", "LensFlareRingParameter", "TrackModelDynamicParameter",
            "ImageProcessorCopyTextureArrayParameter", "LightGlowRWOcclusionsParameter",
            "DebugLightingLightScenarioParameter", "ImageProcessorTexturesParameter",
            "RacelineTextureParameter", "GlobalSamplersParameter", "CarSharedParameter",
            "ImageProcessorLightRayParameter", "LightGlowPreRenderParameter",
            "LensFlareOcclusionTestSamplerParameter", "ModelMorphAndSkinningParameter",
            "CubemapLightScenarioParameter", "RainParticleInputsParameter",
            "CarLightScenarioParameter", "DropShadowSamplerParameter",
            "LensFlareInstanceProcessingOcclusionParameter", "LensFlareSamplersParameter",
            "ModelInstancingParameter", "ForwardPlusCullViewConstStaticParameter",
            "TrackModelStaticParameter", "LensFlareGlowParameter", "ShadowDepthParameter",
            "BCCompressSamplerParameter", "LiveryElementConstantParameter", "BinkTextureParameter",
            "ParticleGPUDataCheckParameter", "ImageProcessorBokehPointBufferParameter",
            "ProxyLodVBInParameter", "LightGlowScreenSizeConstParameter",
            "LensFlareLensOrbsParameter", "ImageProcessorWeightedBlendParameter",
            "CrowdLightScenarioVSParameter", "RacelineSamplerParameter",
            "ImageProcessorHDRParameter", "ModelInstanceParameter",
            "LensFlareOcclusionTestOutputParameter", "ImageProcessorFilterCubemapParameter",
            "NormalSamplerParameter", "ProjectShadowMapLightScenarioParameter",
            "LightGlowSinglePackedParameter", "RainDropTexturesParameter",
            "LensFlareConstantsParameter", "DropShadowTexturesParameter",
            "CrowdSpriteRootParameter", "LiveryTexturesParameter",
            "ShadowDepthScenarioCBParameter", "CrowdPerSectionParameter", "TrackParameter",
            "ParticleRecompositeParameter", "LensFlareIrisParameter",
            "LiveryBlitSectionConstantParameter", "CarInstancingPoseParameter",
            "LightGlowPresetsParameter", "LightGlowOcclusionsParameter",
            "LightGlowPackedParameter", "RenderTargetParameter",
            "ProxyLodLightScenarioTextures", "CrowdLightScenarioParameter_Deprecated",
            "CubemapLightScenarioParameter_Deprecated", "TrackLightScenarioParameter_Deprecated",
            "ParticleLightScenarioParameterPS",
        ];

        // Canonical light scenario name strings 
        private static readonly string[] _lightScenarioNames =
        [
            "CarNightRacingLightScenario", "CarLOD15NightRacingLightScenario",
            "CarNightRacingForwardPlusLightScenario", "CarLOD15NightRacingForwardPlusLightScenario",
            "CarForwardPlusLightScenario", "CarLOD15ForwardPlusLightScenario",
            "CarWetLightScenario", "CarLOD15WetLightScenario", "CarWetHomespaceLightScenario",
            "CarWetUltraLightScenario", "CarLOD15WetUltraLightScenario",
            "CarBaseLightScenario", "CarLightScenario", "CarLOD15LightScenario",
            "CarUltraLightScenario", "CarLOD15UltraLightScenario",
            "ShadowDepthLightScenario", "ProxyLodLightScenario",
            "TrackWetUltraLightScenario", "TrackWetLightScenario",
            "TrackWetUltraLODFadeLightScenario", "TrackWetLODFadeLightScenario",
            "TrackWetUltraVegetationLightScenario", "TrackWetVegetationLightScenario",
            "TrackUltraLightScenario", "TrackLightScenario", "TrackUltraLODFadeLightScenario",
            "TrackLODFadeLightScenario", "TrackUltraVegetationLightScenario",
            "TrackVegetationLightScenario", "CrowdNightLightScenario", "CrowdWetLightScenario",
            "CrowdBaseLightScenario", "CrowdLightScenario",
            "CubemapLightScenario", "CubemapForwardPlusLightScenario",
            "CubemapNightRacingLightScenario", "CubemapWetLightScenario",
            "ParticleLightScenario", "ParticleForwardPlusLightScenario",
            "ParticleWetLightScenario", "ParticleLowResLightScenario",
            "ParticleForwardPlusLowResLightScenario", "ParticleWetLowResLightScenario",
            "ProjectedShadowMapLightScenario", "TrackShadowDepthLightScenario",
            "TrackSimpleDayLightScenario", "TrackSimpleNightLightScenario",
            "TrackNightRacingLightScenario", "TrackForwardPlusLightScenario",
            "TrackBaseLightScenario", "TrackNightRacingVegetationLightScenario",
            "TrackForwardPlusVegetationLightScenario", "TrackDepthPassLightScenario",
            "TrackSimpleDayLODFadeLightScenario", "TrackSimpleNightLODFadeLightScenario",
            "TrackNightRacingLODFadeLightScenario", "TrackForwardPlusLODFadeLightScenario",
            "TrackBaseLODFadeLightScenario",
        ];
    }
}
