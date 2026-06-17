using System;
using System.IO;
using System.Linq;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;

namespace ForzaTechStudio.Services
{
    public sealed class MaterialInstanceBuildResult
    {
        public MaterialBlob MaterialBlob { get; init; } = null!;
        public int ShaderParameterCount { get; init; }
        public string SourceMaterialPath { get; init; } = string.Empty;
        public string NormalizedMaterialPath { get; init; } = string.Empty;
        public string SourceBundleVersion { get; init; } = string.Empty;
        public string ShaderParameterVersion { get; init; } = string.Empty;
    }

    public sealed class MaterialInstanceBuilderService
    {
        public MaterialInstanceBuildResult BuildFromMaterialsZip(
            string zipPath,
            string relativeZipPath,
            string materialName,
            ForzaGameTarget target)
        {
            if (string.IsNullOrWhiteSpace(zipPath))
                throw new ArgumentException("Material zip path is required.", nameof(zipPath));
            if (string.IsNullOrWhiteSpace(relativeZipPath))
                throw new ArgumentException("Material zip entry path is required.", nameof(relativeZipPath));

            byte[] materialBytes = ExtractMaterialbinBytes(zipPath, relativeZipPath);
            Bundle sourceBundle = LoadMaterialBundle(materialBytes, relativeZipPath);

            var sourceMaterialBlob = sourceBundle.Blobs.OfType<MatLBlob>().FirstOrDefault();
            if (sourceMaterialBlob == null)
                throw new InvalidDataException($"Materialbin '{relativeZipPath}' does not contain a MATL material blob.");

            var sourceShaderParamBlob = sourceBundle.Blobs
                .OfType<MaterialShaderParameterBlob>()
                .FirstOrDefault(blob => blob.Tag == Bundle.TAG_BLOB_MaterialShaderParameter);
            if (sourceShaderParamBlob == null)
                throw new InvalidDataException($"Materialbin '{relativeZipPath}' does not contain an MTPR shader parameter blob.");

            string sourceMaterialPath = sourceMaterialBlob.Path ?? string.Empty;
            string normalizedMaterialPath = ModelBuilderService.BuildMaterialLibraryGamePath(materialName, relativeZipPath);

            var resourceBlob = new MaterialResourceBlob
            {
                Tag = Bundle.TAG_BLOB_MaterialResource,
                VersionMajor = 1,
                VersionMinor = 0,
                Path = normalizedMaterialPath
            };

            CopySelectedMetadata(sourceMaterialBlob, resourceBlob);

            EnsureNameMetadata(resourceBlob, materialName);
            EnsureAtlasMetadata(resourceBlob);

            var shaderParamBlob = CloneBlob(sourceShaderParamBlob);
            shaderParamBlob.Tag = Bundle.TAG_BLOB_MaterialShaderParameter;

            var nestedBundle = new Bundle
            {
                VersionMajor = 1,
                VersionMinor = 1
            };

            nestedBundle.Blobs.Add(resourceBlob);
            nestedBundle.Blobs.Add(shaderParamBlob);

            var materialBlob = new MaterialBlob
            {
                Tag = Bundle.TAG_BLOB_MaterialInstance,
                VersionMajor = 1,
                VersionMinor = 0,
                Bundle = nestedBundle
            };

            materialBlob.Metadatas.Add(new NameMetadata
            {
                Tag = BundleMetadata.TAG_METADATA_Name,
                Name = materialName
            });

            materialBlob.Metadatas.Add(new AtlasMetadata
            {
                Tag = BundleMetadata.TAG_METADATA_Atlas,
                Version = 2,
                Unk = false,
                UnkV2 = false
            });

            return new MaterialInstanceBuildResult
            {
                MaterialBlob = materialBlob,
                ShaderParameterCount = shaderParamBlob.Parameters?.Count ?? 0,
                SourceMaterialPath = sourceMaterialPath,
                NormalizedMaterialPath = normalizedMaterialPath,
                SourceBundleVersion = $"{sourceBundle.VersionMajor}.{sourceBundle.VersionMinor}",
                ShaderParameterVersion = $"{shaderParamBlob.VersionMajor}.{shaderParamBlob.VersionMinor}"
            };
        }

        private static byte[] ExtractMaterialbinBytes(string zipPath, string relativeZipPath)
        {
            using var zip = new CustomZipFile(zipPath);
            var entry = zip.GetEntries().FirstOrDefault(candidate =>
                NormalizeZipPath(candidate.Name).Equals(NormalizeZipPath(relativeZipPath), StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                throw new FileNotFoundException($"Materialbin entry '{relativeZipPath}' was not found in '{zipPath}'.");

            return zip.ExtractToMemory(entry);
        }

        private static Bundle LoadMaterialBundle(byte[] materialBytes, string sourcePath)
        {
            if (materialBytes.Length < 4)
                throw new InvalidDataException($"Materialbin '{sourcePath}' is too small to contain a bundle.");

            using var stream = new MemoryStream(materialBytes, writable: false);
            var bundle = new Bundle();
            bundle.Load(stream);
            return bundle;
        }

        private static T CloneBlob<T>(T source) where T : BundleBlob
        {
            using (var bundleStream = new MemoryStream())
            {
                var bundle = new Bundle
                {
                    VersionMajor = 1,
                    VersionMinor = 1
                };
                bundle.Blobs.Add(source);
                bundle.Serialize(bundleStream);
                bundleStream.Position = 0;

                var cloneBundle = new Bundle();
                cloneBundle.Load(bundleStream);
                return cloneBundle.Blobs.OfType<T>().First();
            }
        }

        private static void CopySelectedMetadata(BundleBlob source, BundleBlob destination)
        {
            foreach (var metadata in source.Metadatas.Where(metadata =>
                metadata.Tag == BundleMetadata.TAG_METADATA_Atlas ||
                metadata.Tag == BundleMetadata.TAG_METADATA_ARTX))
            {
                destination.Metadatas.Add(CloneMetadata(metadata));
            }
        }

        private static BundleMetadata CloneMetadata(BundleMetadata source)
        {
            using var stream = new MemoryStream();
            using var writer = new Syroot.BinaryData.BinaryStream(stream);
            source.SerializeMetadataData(writer);
            stream.Position = 0;

            BundleMetadata clone = source switch
            {
                AtlasMetadata => new AtlasMetadata(),
                ARTXMetadata => new ARTXMetadata(),
                _ => new RawMetadata()
            };

            clone.Tag = source.Tag;
            clone.Version = source.Version;
            using var reader = new Syroot.BinaryData.BinaryStream(stream);
            clone.ReadMetadataData(reader);
            return clone;
        }

        private static void EnsureNameMetadata(BundleBlob blob, string materialName)
        {
            var nameMetadata = blob.Metadatas.OfType<NameMetadata>().FirstOrDefault();
            if (nameMetadata != null)
            {
                if (string.IsNullOrWhiteSpace(nameMetadata.Name))
                    nameMetadata.Name = materialName;
                return;
            }

            if (blob.Metadatas.Any(metadata => metadata.Tag == BundleMetadata.TAG_METADATA_Name))
                return;

            blob.Metadatas.Add(new NameMetadata
            {
                Tag = BundleMetadata.TAG_METADATA_Name,
                Name = materialName
            });
        }

        private static void EnsureAtlasMetadata(BundleBlob blob)
        {
            if (blob.Metadatas.Any(metadata => metadata.Tag == BundleMetadata.TAG_METADATA_Atlas))
                return;

            blob.Metadatas.Add(new AtlasMetadata
            {
                Tag = BundleMetadata.TAG_METADATA_Atlas,
                Version = 2,
                Unk = false,
                UnkV2 = false
            });
        }

        private static string NormalizeZipPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }
    }
}