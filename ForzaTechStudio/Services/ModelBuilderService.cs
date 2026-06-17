using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using ForzaTools.Shared;
using Syroot.BinaryData;

namespace ForzaTechStudio.Services
{
    public class ObjectBuildData
    {
        public ProcessedGeometry Geometry { get; set; } = null!;
        public string MaterialName { get; set; } = "Default";
        public MaterialBlob? MaterialBlob { get; set; }
        public string ObjectName { get; set; } = "Model";
        public string? MaterialRelativePath { get; set; }

        public bool IsOpaque { get; set; } = true;
        public bool IsDecal { get; set; }
        public bool IsTransparent { get; set; }
        public bool IsShadow { get; set; }
        public bool IsNotShadow { get; set; } = true;
        public bool IsAlphaToCoverage { get; set; }
        public bool IsMorphDamage { get; set; } = true;
    }

    public class GameLayoutInfo
    {
        public int ElementCount { get; set; }
        public int Stride { get; set; }
        public string NormalFormat { get; set; } = string.Empty;
        public string TangentFormat { get; set; } = string.Empty;
        public int TexcoordCount { get; set; }
        public int TangentCount { get; set; }
        public bool HasColor { get; set; }
    }

    public class ModelBuilderService
    {
        private const string GamePathPrefix = @"Game:\";
        private const string MaterialLibraryRelativePrefix = @"Media\cars\_library\materials\";
        private const string MaterialLibraryGamePathPrefix = GamePathPrefix + MaterialLibraryRelativePrefix;

        private readonly GeometryProcessingService _geometryService;

        public ModelBuilderService()
        {
            _geometryService = new GeometryProcessingService();
        }


        public ProcessedGeometry ProcessGeometry(GeometryInput input, ForzaGameTarget target = ForzaGameTarget.FH5)
        {
            return _geometryService.ProcessGeometry(input, target);
        }


        public Bundle CreateBundleInMemory(ProcessedGeometry processed, string materialName)
        {
            return CreateBundleFromObjects(new List<ObjectBuildData> 
            { 
                new ObjectBuildData 
                { 
                    Geometry = processed, 
                    MaterialName = materialName,
                    ObjectName = "Model"
                } 
            });
        }

        public Bundle CreateBundleFromObjects(List<ObjectBuildData> objects, ForzaGameTarget target = ForzaGameTarget.FH5)
        {
            if (objects == null || objects.Count == 0)
                throw new ArgumentException("At least one object is required.", nameof(objects));

            // Get the correct version numbers for the target game
            var (targetBundleVer, targetModlVer, targetMeshVer, targetVlayVer) =
                ModelbinConversionService.GetModelbinTargetVersions(target);

            var bundle = new Bundle
            {
                VersionMajor = targetBundleVer.maj,
                VersionMinor = targetBundleVer.min
            };

            // Define Metadata IDs for Linking
            int idLink_LayoutFull = 0;
            int idLink_LayoutPos = 1;
            int idLink_IB = 0;
            int idLink_VB_Pos = 1;
            
            int nextVBNormID = 2; 


            var skeletonBlob = new SkeletonBlob { Tag = Bundle.TAG_BLOB_Skeleton, VersionMajor = 1, VersionMinor = 0 };
            skeletonBlob.Bones.Add(new Bone { Name = "root", ParentId = -1, Matrix = Matrix4x4.Identity });
            bundle.Blobs.Add(skeletonBlob);


            bundle.Blobs.Add(new MorphBlob { Tag = Bundle.TAG_BLOB_Morph, VersionMajor = 0, VersionMinor = 0 });


            var materialIndexMap = new Dictionary<string, short>();
            var materialsToAdd = new List<MaterialBlob>();
            
            foreach (var obj in objects)
            {
                string matName = obj.MaterialName ?? "Default";
                
                if (!materialIndexMap.ContainsKey(matName))
                {
                    short index = (short)materialIndexMap.Count;
                    materialIndexMap[matName] = index;
                    MaterialBlob matBlob = obj.MaterialBlob ?? CreateDefaultMaterialBlob(matName, obj.MaterialRelativePath);
                    materialsToAdd.Add(matBlob);
                }
            }
            
            for (short i = 0; i < materialsToAdd.Count; i++)
            {
                var materialBlobClone = CloneMaterialBlob(materialsToAdd[i]);
                materialBlobClone.Tag = Bundle.TAG_BLOB_MaterialInstance;
                materialBlobClone.VersionMajor = 1;
                materialBlobClone.VersionMinor = 0;
                
                if (!materialBlobClone.Metadatas.Any(m => m.Tag == BundleMetadata.TAG_METADATA_Identifier))
                {
                    materialBlobClone.Metadatas.Add(new IdentifierMetadata { Tag = BundleMetadata.TAG_METADATA_Identifier, Id = (uint)i });
                }
                
                bundle.Blobs.Add(materialBlobClone);
            }

            // Get game-specific layout info
            var layoutInfo = GetGameLayoutInfoInternal(target);
            int slot1Stride = layoutInfo.Stride;

            // Pack Geometry Buffers
            var globalIndices = new List<byte[]>();
            var globalPosData = new List<byte[]>();
            
            int runningIndexCount = 0;
            int runningVertexCount = 0;

            ushort meshCount = 0;
            
            var perObjectNormVBs = new List<BundleBlob>();

            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                var geo = obj.Geometry;
                
                // 1. Create Per-Object VertexBuffer Blob (Norm/UV)
                int perObjectVBNormID = nextVBNormID++; 

                var normHeader = new BufferHeader
                {
                    Stride = (ushort)slot1Stride,
                    SubElementCount = 1,
                    Format = layoutInfo.FirstElementFormat
                };
                normHeader.Data = geo.NormalUVData;

                var vbNorm = new VertexBufferBlob
                {
                    Tag = Bundle.TAG_BLOB_VertexBuffer,
                    VersionMajor = 1,
                    VersionMinor = 0,
                    Header = normHeader
                };
                vbNorm.Metadatas.Add(new IdentifierMetadata { Tag = BundleMetadata.TAG_METADATA_Identifier, Id = unchecked((uint)perObjectVBNormID) });
                
                perObjectNormVBs.Add(vbNorm);


                globalIndices.AddRange(geo.IndexData);
                globalPosData.AddRange(geo.PositionData);

                uint positionVertexBufferOffset = checked((uint)(runningVertexCount * 8));

                string matName = obj.MaterialName ?? "Default";
                short materialId = materialIndexMap[matName];


                MeshBlob BuildMeshBlob(int lod, bool isShadow)
                {
                    short meshMaterialId = isShadow ? (short)-1 : materialId;

                    var meshBlob = new MeshBlob
                    {
                        Tag = Bundle.TAG_BLOB_Mesh,
                        VersionMajor = targetMeshVer.maj,
                        VersionMinor = targetMeshVer.min,
                        IndexBufferIndex = idLink_IB,
                        VertexLayoutIndex = isShadow ? idLink_LayoutPos : idLink_LayoutFull,

                        IndexCount = geo.IndexData.Length,
                        PrimCount = geo.IndexData.Length / 3,

                        IndexBufferOffset = 0,
                        IndexBufferDrawOffset = runningIndexCount,
                        IndexedVertexOffset = 0,

                        ReferencedVertexCount = (uint)geo.PositionData.Length,

                        // Bucket flags: shadow meshes are always Opaque + Shadow; regular meshes use per-object flags.
                        IsOpaque = isShadow ? true : obj.IsOpaque,
                        IsDecal = isShadow ? false : obj.IsDecal,
                        IsTransparent = isShadow ? false : obj.IsTransparent,
                        IsShadow = isShadow ? true : obj.IsShadow,
                        IsNotShadow = isShadow ? false : obj.IsNotShadow,
                        IsAlphaToCoverage = isShadow ? false : obj.IsAlphaToCoverage,
                        IsMorphDamage = obj.IsMorphDamage,

                        Is32BitIndices = true,
                        Topology = 4,

                        MaterialId = meshMaterialId,

                        PositionScale = geo.PositionScale,
                        PositionTranslate = geo.PositionTranslate,

                        NameSuffix = lod.ToString()
                    };

                    switch (lod)
                    {
                        case 0: meshBlob.LOD_LOD0 = true; break;
                        case 1: meshBlob.LOD_LOD1 = true; break;
                        case 2: meshBlob.LOD_LOD2 = true; break;
                        case 3: meshBlob.LOD_LOD3 = true; break;
                        case 4: meshBlob.LOD_LOD4 = true; break;
                        case 5: meshBlob.LOD_LOD5 = true; break;
                    }

                    // Position buffer
                    meshBlob.VertexBuffers.Add(new MeshBlob.VertexBufferUsage
                    {
                        Index = idLink_VB_Pos,
                        InputSlot = 0,
                        Stride = 8,
                        Offset = positionVertexBufferOffset
                    });

                    if (!isShadow)
                    {
                        // Full meshes use the per-object slot 1 stream for normals/UVs/tangents.
                        meshBlob.VertexBuffers.Add(new MeshBlob.VertexBufferUsage
                        {
                            Index = perObjectVBNormID,
                            InputSlot = 1,
                            Stride = (uint)slot1Stride,
                            Offset = 0
                        });
                    }

                    // Set version-dependent defaults
                    if (meshBlob.IsAtLeastVersion(1, 5))
                    {
                        meshBlob.TexCoordTransforms = new Vector4[5];
                        for (int tc = 0; tc < 5; tc++)
                            meshBlob.TexCoordTransforms[tc] = new Vector4(0, 1, 0, 1);
                    }

                    if (meshBlob.IsAtLeastVersion(1, 4))
                    {
                        meshBlob.MorphDataBufferIndex = -1;
                        meshBlob.SkinningDataBufferIndex = -1;
                    }

                    // Set v1.9 material IDs array
                    if (meshBlob.IsAtLeastVersion(1, 9))
                    {
                        meshBlob.MaterialIds = new short[] { -1, meshMaterialId, -1, -1 };
                    }

                    string meshName = isShadow ? "Shadow" : $"{obj.ObjectName}_LOD{lod}";
                    meshBlob.Metadatas.Add(new NameMetadata { Tag = BundleMetadata.TAG_METADATA_Name, Name = meshName });
                    meshBlob.Metadatas.Add(new BoundaryBoxMetadata { Tag = BundleMetadata.TAG_METADATA_BBox, Min = geo.BoundingBoxMin, Max = geo.BoundingBoxMax });

                    return meshBlob;
                }

                // Create 6 MeshBlobs per object (LOD0-5). For each valid mesh blob, also create a
                // shadow-only counterpart.
                for (int lod = 0; lod < 6; lod++)
                {
                    bundle.Blobs.Add(BuildMeshBlob(lod, isShadow: false));
                    meshCount++;

                    bundle.Blobs.Add(BuildMeshBlob(lod, isShadow: true));
                    meshCount++;
                }

                runningIndexCount += geo.IndexData.Length;
                runningVertexCount += geo.PositionData.Length;
            }

            // Create Final Buffer Blobs

            // Index Buffer (One Shared)
            var ibHeader = new BufferHeader
            {
                Stride = 4,
                SubElementCount = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_R32_UINT
            };
            ibHeader.Data = globalIndices.ToArray();

            var ibBlob = new IndexBufferBlob
            {
                Tag = Bundle.TAG_BLOB_IndexBuffer,
                VersionMajor = 1,
                VersionMinor = 0,
                Header = ibHeader
            };
            ibBlob.Metadatas.Add(new IdentifierMetadata { Tag = BundleMetadata.TAG_METADATA_Identifier, Id = unchecked((uint)idLink_IB) });
            bundle.Blobs.Add(ibBlob);

            // VLay Full (game-specific)
            bundle.Blobs.Add(CreateLayoutFull(idLink_LayoutFull, target, targetVlayVer));
            // VLay Pos
            bundle.Blobs.Add(CreateLayoutPosOnly(idLink_LayoutPos, targetVlayVer));

            // VB Pos (One Shared)
            var posHeader = new BufferHeader
            {
                Stride = 8,
                SubElementCount = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM
            };
            posHeader.Data = globalPosData.ToArray();

            var vbPos = new VertexBufferBlob
            {
                Tag = Bundle.TAG_BLOB_VertexBuffer,
                VersionMajor = 1,
                VersionMinor = 0,
                Header = posHeader
            };
            vbPos.Metadatas.Add(new IdentifierMetadata { Tag = BundleMetadata.TAG_METADATA_Identifier, Id = unchecked((uint)idLink_VB_Pos) });
            bundle.Blobs.Add(vbPos);

            // VB Norm Per Object
            foreach (var vb in perObjectNormVBs)
            {
                bundle.Blobs.Add(vb);
            }


            var modelBlob = new ModelBlob
            {
                Tag = Bundle.TAG_BLOB_Model,
                VersionMajor = targetModlVer.maj,
                VersionMinor = targetModlVer.min,
                MeshCount = meshCount,
                BuffersCount = (ushort)(2 + objects.Count),
                VertexLayoutCount = 2,
                MaterialCount = (ushort)materialIndexMap.Count,
                HasLOD = true,
                MinLOD = 5,
                MaxLOD = -1,
                LODFlags = 0x7F,
                DecompressFlags = targetModlVer.min >= 2 ? (byte)0x01 : (byte)0x00
            };

            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            foreach (var o in objects)
            {
                min = Vector3.Min(min, o.Geometry.BoundingBoxMin);
                max = Vector3.Max(max, o.Geometry.BoundingBoxMax);
            }
            modelBlob.Metadatas.Add(new BoundaryBoxMetadata { Tag = BundleMetadata.TAG_METADATA_BBox, Min = min, Max = max });
            bundle.Blobs.Add(modelBlob);

            return bundle;
        }

        public void SaveBundle(Bundle bundle, string outputPath)
        {
            using (var fs = new FileStream(outputPath, FileMode.Create))
            {
                bundle.CreateModelBin(fs);
            }
        }

        public void BuildCompatibleModelBin(string outputPath, GeometryInput input, string materialName)
        {
            var processed = ProcessGeometry(input);
            var bundle = CreateBundleInMemory(processed, materialName);
            SaveBundle(bundle, outputPath);
        }

        public static GameLayoutInfo GetGameLayoutInfo(ForzaGameTarget target)
        {
            var info = GetGameLayoutInfoInternal(target);
            return new GameLayoutInfo
            {
                ElementCount = info.ElementCount,
                Stride = info.Stride,
                NormalFormat = info.NormalFormatName,
                TangentFormat = info.TangentFormatName,
                TexcoordCount = info.TexcoordCount,
                TangentCount = info.TangentCount,
                HasColor = info.HasColor,
            };
        }

        // Internal layout info struct with all the details needed for building VLay + vertex data.
        internal class InternalLayoutInfo
        {
            public int ElementCount;
            public int Stride;
            public int TexcoordCount;
            public int TangentCount;
            public bool HasColor;
            public string NormalFormatName = string.Empty;
            public string TangentFormatName = string.Empty;

            // DXGI formats for vertex encoding
            public DXGI_FORMAT NormalFormat;
            public DXGI_FORMAT TangentFormat;
            public int NormalByteSize;
            public int TangentByteSize;

            // Packed format values for the VLay blob
            public DXGI_FORMAT NormalPackedFormat;
            public DXGI_FORMAT TangentPackedFormat;

            // First element format for the VerB header
            public DXGI_FORMAT FirstElementFormat;

            // VLay Flags 
            public uint VlayFlags;
        }

        // Returns the full internal layout info for a game target
        internal static InternalLayoutInfo GetGameLayoutInfoInternal(ForzaGameTarget target)
        {
            return target switch
            {
                ForzaGameTarget.FH2 or ForzaGameTarget.FM5 => new InternalLayoutInfo
                {
                    // FH2/FM5: NORMAL(R16G16B16A16_FLOAT 8B), TANGENT(R16G16B16A16_FLOAT 8B), TEXCOORD(R16G16_UNORM 4B)
                    // 7 elements: NORMAL + TANGENT0 + TANGENT1 + TEXCOORD0-3 = 8+4+4+4+4+8+8 = 40B
                    ElementCount = 7, Stride = 40,
                    TexcoordCount = 4, TangentCount = 2, HasColor = false,
                    NormalFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT,
                    TangentFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT,
                    NormalByteSize = 8, TangentByteSize = 8,
                    NormalFormatName = "R16G16B16A16_FLOAT",
                    TangentFormatName = "R16G16B16A16_FLOAT",
                    NormalPackedFormat = (DXGI_FORMAT)9,  // Forza ElementFormats
                    TangentPackedFormat = (DXGI_FORMAT)9,
                    FirstElementFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT,
                    VlayFlags = 0x0000006F, // TEXCOORD0-3 + TANGENT0-1
                },

                ForzaGameTarget.FM6 or ForzaGameTarget.FM7 => new InternalLayoutInfo
                {
                    // FM6/FM7: NORMAL(R16G16_SNORM 4B), TANGENT(R10G10B10A2_UNORM 4B), TEXCOORD(R16G16_UNORM 4B)
                    // 7 elements: NORMAL + TEXCOORD0-3 + TANGENT0 + TANGENT1 = 4+4+4+4+4+4+4 = 28B
                    ElementCount = 7, Stride = 28,
                    TexcoordCount = 4, TangentCount = 2, HasColor = false,
                    NormalFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM,
                    TangentFormat = DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM,
                    NormalByteSize = 4, TangentByteSize = 4,
                    NormalFormatName = "R16G16_SNORM",
                    TangentFormatName = "R10G10B10A2_UNORM",
                    NormalPackedFormat = (DXGI_FORMAT)52,
                    TangentPackedFormat = (DXGI_FORMAT)48,
                    FirstElementFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM,
                    VlayFlags = 0x0000006F,
                },

                ForzaGameTarget.FH3 or ForzaGameTarget.FH4 => new InternalLayoutInfo
                {
                    // FH3/FH4: same formats as FM6+, but adds TEXCOORD4 + COLOR0
                    // 9 elements: NORMAL + TEXCOORD0-4 + TANGENT0 + TANGENT1 + COLOR0 = 4+4+4+4+4+4+4+4+4 = 36B
                    ElementCount = 9, Stride = 36,
                    TexcoordCount = 5, TangentCount = 2, HasColor = true,
                    NormalFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM,
                    TangentFormat = DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM,
                    NormalByteSize = 4, TangentByteSize = 4,
                    NormalFormatName = "R16G16_SNORM",
                    TangentFormatName = "R10G10B10A2_UNORM",
                    NormalPackedFormat = (DXGI_FORMAT)52,
                    TangentPackedFormat = (DXGI_FORMAT)48,
                    FirstElementFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM,
                    VlayFlags = 0x000004FF,
                },

                ForzaGameTarget.FH5 or ForzaGameTarget.FH6 => new InternalLayoutInfo
                {
                    // FH5/FH6: adds TANGENT2, full 10 elements
                    // 10 elements: NORMAL + TEXCOORD0-4 + TANGENT0-2 + COLOR0 = 4+4+4+4+4+4+4+4+4+4 = 40B
                    ElementCount = 10, Stride = 40,
                    TexcoordCount = 5, TangentCount = 3, HasColor = true,
                    NormalFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM,
                    TangentFormat = DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM,
                    NormalByteSize = 4, TangentByteSize = 4,
                    NormalFormatName = "R16G16_SNORM",
                    TangentFormatName = "R10G10B10A2_UNORM",
                    NormalPackedFormat = (DXGI_FORMAT)52,
                    TangentPackedFormat = (DXGI_FORMAT)48,
                    FirstElementFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM,
                    VlayFlags = 0x000004FF,
                },

                ForzaGameTarget.FM2023 => new InternalLayoutInfo
                {
                    // FM2023: has TANGENT2 like FH5, but typically NO COLOR0
                    // 8 elements: NORMAL + TEXCOORD0-3 + TANGENT0-2 + TEXCOORD4 = 4+4+4+4+4+4+4+4 = 32B
                    // Note: FM2023 full pattern omits COLOR0 but has TEXCOORD4
                    ElementCount = 9, Stride = 36,
                    TexcoordCount = 5, TangentCount = 3, HasColor = false,
                    NormalFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM,
                    TangentFormat = DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM,
                    NormalByteSize = 4, TangentByteSize = 4,
                    NormalFormatName = "R16G16_SNORM",
                    TangentFormatName = "R10G10B10A2_UNORM",
                    NormalPackedFormat = (DXGI_FORMAT)52,
                    TangentPackedFormat = (DXGI_FORMAT)48,
                    FirstElementFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM,
                    VlayFlags = 0x000000FF, // No COLOR bit (0x400)
                },

                _ => GetGameLayoutInfoInternal(ForzaGameTarget.FH5),
            };
        }

        // Creates a game-specific full VLay layout blob.
        // The element order follows the canonical order observed in real game data:
        // POSITION(slot0), NORMAL(slot1), TEXCOORD0-N(slot1), TANGENT0-N(slot1), COLOR0(slot1)
        private VertexLayoutBlob CreateLayoutFull(int id, ForzaGameTarget target, (byte maj, byte min) vlayVer)
        {
            var layout = GetGameLayoutInfoInternal(target);

            var blob = new VertexLayoutBlob
            {
                Tag = Bundle.TAG_BLOB_VertexLayout,
                VersionMajor = vlayVer.maj,
                VersionMinor = vlayVer.min
            };

            // Build semantic names list, order matters for index references
            var semanticNames = new List<string> { "POSITION", "NORMAL", "TEXCOORD", "TANGENT" };
            if (layout.HasColor)
                semanticNames.Add("COLOR");
            blob.SemanticNames.AddRange(semanticNames);

            short posIdx = 0;
            short normIdx = 1;
            short texIdx = 2;
            short tanIdx = 3;
            short colIdx = layout.HasColor ? (short)4 : (short)-1;

            // POSITION (Slot 0)
            blob.Elements.Add(CreateElement(posIdx, 0, 0, 0, (int)DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM));
            blob.PackedFormats.Add((DXGI_FORMAT)49); // R16G16B16A16_UNORM packed

            // NORMAL (Slot 1)
            blob.Elements.Add(CreateElement(normIdx, 0, 1, 0, (int)layout.NormalFormat));
            blob.PackedFormats.Add(layout.NormalPackedFormat);

            // TEXCOORDs (Slot 1)
            for (int i = 0; i < layout.TexcoordCount; i++)
            {
                blob.Elements.Add(CreateElement(texIdx, (short)i, 1, 0, (int)DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM));
                blob.PackedFormats.Add((DXGI_FORMAT)46);
            }

            // TANGENTs (Slot 1)
            for (int i = 0; i < layout.TangentCount; i++)
            {
                blob.Elements.Add(CreateElement(tanIdx, (short)i, 1, 0, (int)layout.TangentFormat));
                blob.PackedFormats.Add(layout.TangentPackedFormat);
            }

            // COLOR (Slot 1) only if the game supports it
            if (layout.HasColor)
            {
                blob.Elements.Add(CreateElement(colIdx, 0, 1, 0, (int)DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM));
                blob.PackedFormats.Add((DXGI_FORMAT)22);
            }

            blob.Flags = layout.VlayFlags;
            blob.Metadatas.Add(new IdentifierMetadata { Tag = BundleMetadata.TAG_METADATA_Identifier, Id = unchecked((uint)id) });

            return blob;
        }

        private VertexLayoutBlob CreateLayoutPosOnly(int id, (byte maj, byte min) vlayVer)
        {
            var blob = new VertexLayoutBlob
            {
                Tag = Bundle.TAG_BLOB_VertexLayout,
                VersionMajor = vlayVer.maj,
                VersionMinor = vlayVer.min
            };
            blob.SemanticNames.Add("POSITION");
            blob.Elements.Add(CreateElement(0, 0, 0, 0, (int)DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM));
            blob.PackedFormats.Add((DXGI_FORMAT)49);
            blob.Flags = 0;
            blob.Metadatas.Add(new IdentifierMetadata { Tag = BundleMetadata.TAG_METADATA_Identifier, Id = unchecked((uint)id) });
            return blob;
        }

        private D3D12_INPUT_LAYOUT_DESC CreateElement(short nameIdx, short semIdx, short slot, short slotClass, int format, int offset = -1, int step = 0)
        {
            return new D3D12_INPUT_LAYOUT_DESC
            {
                SemanticNameIndex = nameIdx,
                SemanticIndex = semIdx,
                InputSlot = slot,
                InputSlotClass = slotClass,
                Format = (DXGI_FORMAT)format,
                AlignedByteOffset = offset,
                InstanceDataStepRate = step
            };
        }


        private MaterialBlob CreateDefaultMaterialBlob(string materialName, string? relativePath = null)
        {
            var materialBlob = new MaterialBlob
            {
                Tag = Bundle.TAG_BLOB_MaterialInstance,
                VersionMajor = 1,
                VersionMinor = 0
            };
            
            // Create nested bundle structure 
            var nestedBundle = new Bundle
            {
                VersionMajor = 1,
                VersionMinor = 1
            };

            // Create MATI blob with material path
            var matiBlob = new MaterialResourceBlob
            {
                Tag = Bundle.TAG_BLOB_MaterialResource, // 0x4D415449 'MATI'
                VersionMajor = 1,
                VersionMinor = 0
            };
            
            // Construct the material path using the relative path if available
            matiBlob.Path = BuildMaterialLibraryGamePath(materialName, relativePath);
            
            // Add Name metadata
            matiBlob.Metadatas.Add(new NameMetadata 
            { 
                Tag = BundleMetadata.TAG_METADATA_Name, 
                Name = materialName ?? "Default" 
            });
            
            matiBlob.Metadatas.Add(new AtlasMetadata
            {
                Tag = BundleMetadata.TAG_METADATA_Atlas,
                Version = 2,
                Unk = false,
                UnkV2 = false
            });
            
            nestedBundle.Blobs.Add(matiBlob);
            
            // Create MaterialShaderParameterBlob (MTPR) with version 2.0 and 0 parameters
            var shaderParamBlob = new MaterialShaderParameterBlob
            {
                Tag = Bundle.TAG_BLOB_MaterialShaderParameter, // 0x4D545052 'MTPR'
                VersionMajor = 2,
                VersionMinor = 0
                // Parameters collection is already initialized as empty
            };
            
            nestedBundle.Blobs.Add(shaderParamBlob);
            
            materialBlob.Bundle = nestedBundle;
            
            materialBlob.Metadatas.Add(new NameMetadata 
            { 
                Tag = BundleMetadata.TAG_METADATA_Name, 
                Name = materialName ?? "Default" 
            });
            
            return materialBlob;
        }

        public static string BuildMaterialLibraryGamePath(string? materialName, string? relativePath = null)
        {
            string path = string.IsNullOrWhiteSpace(relativePath)
                ? (string.IsNullOrWhiteSpace(materialName) ? "error" : materialName.Trim())
                : relativePath.Trim();

            path = path.Replace('/', '\\').TrimStart('\\');

            if (path.StartsWith(GamePathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string gameRelativePath = path.Substring(GamePathPrefix.Length).TrimStart('\\');
                if (gameRelativePath.StartsWith(MaterialLibraryRelativePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string materialRelativePath = gameRelativePath.Substring(MaterialLibraryRelativePrefix.Length);
                    return MaterialLibraryGamePathPrefix + EnsureMaterialbinExtension(materialRelativePath);
                }

                return EnsureMaterialbinExtension(path);
            }

            if (path.StartsWith(MaterialLibraryRelativePrefix, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(MaterialLibraryRelativePrefix.Length);
            }

            return MaterialLibraryGamePathPrefix + EnsureMaterialbinExtension(path);
        }

        private static string EnsureMaterialbinExtension(string path)
        {
            path = path.TrimStart('\\');
            return path.EndsWith(".materialbin", StringComparison.OrdinalIgnoreCase)
                ? path
                : $"{path}.materialbin";
        }

        private MaterialBlob CloneMaterialBlob(MaterialBlob source)
        {
            var clone = new MaterialBlob
            {
                Tag = source.Tag,
                VersionMajor = source.VersionMajor,
                VersionMinor = source.VersionMinor
            };
            
            if (source.CustomBlobData != null && source.CustomBlobData.Length > 0)
            {
                clone.CustomBlobData = new byte[source.CustomBlobData.Length];
                Array.Copy(source.CustomBlobData, clone.CustomBlobData, source.CustomBlobData.Length);
            }
            
            if (source.Bundle != null)
            {
                using (var ms = new MemoryStream())
                {
                    source.Bundle.Serialize(ms);
                    ms.Position = 0;
                    clone.Bundle = new Bundle();
                    clone.Bundle.Load(ms);
                }
            }
            
            foreach (var meta in source.Metadatas)
            {
                clone.Metadatas.Add(CloneMetadata(meta));
            }
            
            return clone;
        }

        private static BundleMetadata CloneMetadata(BundleMetadata source)
        {
            switch (source)
            {
                case NameMetadata nameMeta:
                    return new NameMetadata
                    {
                        Tag = nameMeta.Tag,
                        Version = nameMeta.Version,
                        Name = nameMeta.Name
                    };
                case IdentifierMetadata idMeta:
                    return new IdentifierMetadata
                    {
                        Tag = idMeta.Tag,
                        Version = idMeta.Version,
                        Id = idMeta.Id
                    };
                case AtlasMetadata atlasMeta:
                    return new AtlasMetadata
                    {
                        Tag = atlasMeta.Tag,
                        Version = atlasMeta.Version,
                        Unk = atlasMeta.Unk,
                        UnkV2 = atlasMeta.UnkV2
                    };
                case BlendMetadata blendMeta:
                    return new BlendMetadata
                    {
                        Tag = blendMeta.Tag,
                        Version = blendMeta.Version,
                        Unk1 = blendMeta.Unk1,
                        Unk2 = blendMeta.Unk2
                    };
                case RawMetadata rawMeta:
                    return new RawMetadata
                    {
                        Tag = rawMeta.Tag,
                        Version = rawMeta.Version,
                        RawData = rawMeta.RawData?.ToArray() ?? Array.Empty<byte>()
                    };
                default:
                    return CloneAsRawMetadata(source);
            }
        }

        private static RawMetadata CloneAsRawMetadata(BundleMetadata source)
        {
            using var stream = new MemoryStream();
            var binaryStream = new BinaryStream(stream);
            source.CreateModelBinMetadataData(binaryStream);

            return new RawMetadata
            {
                Tag = source.Tag,
                Version = source.Version,
                RawData = stream.ToArray()
            };
        }
    }
}
