using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using ForzaTools.Shared;
using System;
using System.Collections.Generic;
using System.Numerics;
using DurangoTypes;

namespace ForzaTechStudio.Services
{
    public class BundleBuilderService
    {
        public Bundle CreatePlaceholderBundle()
        {
            var bundle = new Bundle();
            bundle.VersionMajor = 1;
            bundle.VersionMinor = 1; // the standard version it seems for later forzas

            // IDs 
            int layoutId = 1000;
            int vbId = 2000;
            int ibId = 3000;

            // Create Blobs 
            var modelBlob = new ModelBlob();
            var meshBlob = new MeshBlob();
            var vertexLayoutBlob = new VertexLayoutBlob();
            var vertexBufferBlob = new VertexBufferBlob();
            var indexBufferBlob = new IndexBufferBlob();

            // Configure ModelBlob (Root) ---
            modelBlob.Metadatas.Add(new BoundaryBoxMetadata
            {
                Min = new Vector3(-1, -1, -1),
                Max = new Vector3(1, 1, 1)
            });

            // Configure VertexLayout (Standard PBR)
            int offset = 0;
            vertexLayoutBlob.Elements.Add(CreateElement("POSITION", 0, DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM, 0, ref offset));
            vertexLayoutBlob.Elements.Add(CreateElement("NORMAL", 0, DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM, 0, ref offset));
            vertexLayoutBlob.Elements.Add(CreateElement("TANGENT", 0, DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM, 0, ref offset));
            vertexLayoutBlob.Elements.Add(CreateElement("TANGENT", 1, DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM, 0, ref offset));
            vertexLayoutBlob.Elements.Add(CreateElement("TEXCOORD", 0, DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM, 0, ref offset));
            vertexLayoutBlob.Elements.Add(CreateElement("COLOR", 0, DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM, 0, ref offset));

            // Populate PackedFormats and Names
            vertexLayoutBlob.PackedFormats = new List<DXGI_FORMAT>();
            foreach (var el in vertexLayoutBlob.Elements) vertexLayoutBlob.PackedFormats.Add(el.Format);

            vertexLayoutBlob.SemanticNames = new List<string> { "POSITION", "NORMAL", "TANGENT", "TEXCOORD", "COLOR" };
            vertexLayoutBlob.Elements[0].SemanticNameIndex = 0;
            vertexLayoutBlob.Elements[1].SemanticNameIndex = 1;
            vertexLayoutBlob.Elements[2].SemanticNameIndex = 2;
            vertexLayoutBlob.Elements[3].SemanticNameIndex = 2;
            vertexLayoutBlob.Elements[4].SemanticNameIndex = 3;
            vertexLayoutBlob.Elements[5].SemanticNameIndex = 4;

            AddId(vertexLayoutBlob, layoutId);

            // Configure Buffers (Empty Placeholder Data) ---
            // Vertex Buffer
            vertexBufferBlob.Header = new BufferHeader
            {
                SubElementCount = 1,
                Stride = (ushort)offset,
                Length = 0,
                Data = Array.Empty<byte[]>(),
                Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN
            };
            AddId(vertexBufferBlob, vbId);

            // Index Buffer
            indexBufferBlob.Header = new BufferHeader
            {
                SubElementCount = 1,
                Stride = 2,
                Length = 0,
                Data = Array.Empty<byte[]>(),
                Format = DXGI_FORMAT.DXGI_FORMAT_R16_UINT
            };
            AddId(indexBufferBlob, ibId);

            // Configure MeshBlob 
            meshBlob.VertexLayoutIndex = 0; // Relative to Bundle list (excluding Model)
            meshBlob.IndexBufferIndex = ibId;
            meshBlob.VertexBuffers = new List<MeshBlob.VertexBufferUsage>
            {
                new MeshBlob.VertexBufferUsage { Index = vbId, InputSlot = 0, Offset = 0, Stride = (uint)offset }
            };
            meshBlob.PositionScale = new Vector4(1, 1, 1, 0);
            meshBlob.PositionTranslate = new Vector4(0, 0, 0, 0);
            AddName(meshBlob, "Placeholder_Mesh");

            // Assemble Bundle
            // Order matters for Index referencing if using relative indices, but ID linking is safer
            bundle.Blobs.Add(modelBlob);
            bundle.Blobs.Add(vertexLayoutBlob);
            bundle.Blobs.Add(vertexBufferBlob);
            bundle.Blobs.Add(indexBufferBlob);
            bundle.Blobs.Add(meshBlob);

            return bundle;
        }

        // Helpers 
        private D3D12_INPUT_LAYOUT_DESC CreateElement(string semantic, int index, DXGI_FORMAT format, int slot, ref int currentOffset)
        {
            var el = new D3D12_INPUT_LAYOUT_DESC
            {
                SemanticNameIndex = (short)-1,
                SemanticIndex = (short)index,
                Format = format,
                InputSlot = (short)slot,
                AlignedByteOffset = currentOffset,
                InputSlotClass = 0,
                InstanceDataStepRate = 0
            };
            currentOffset += GetSize(format);
            return el;
        }

        private static int GetSize(DXGI_FORMAT fmt) => fmt switch
        {
            DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT => 16,
            DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM => 8,
            DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT => 12,
            DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM => 4,
            DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM => 4,
            DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM => 4,
            _ => 4
        };

        private void AddId(BundleBlob blob, int id) => blob.Metadatas.Add(new IdentifierMetadata { Id = (uint)id });
        private void AddName(BundleBlob blob, string name) => blob.Metadatas.Add(new NameMetadata { Name = name });
    }
}