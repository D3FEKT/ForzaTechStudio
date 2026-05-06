using Syroot.BinaryData;
using System.Collections.Generic;

namespace ForzaTools.Bundles.Metadata;

public class VDCLMetadata : BundleMetadata
{
    public List<VDCLEntry> Entries { get; set; } = new();

    public override void ReadMetadataData(BinaryStream bs)
    {
        if (Version >= 2)
        {
            int count = 1;
            if (Version >= 3)
                count = bs.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                Entries.Add(new VDCLEntry
                {
                    NameHash = bs.ReadUInt32(),
                    VertexInputFlags = bs.ReadUInt32()
                });
            }
        }
    }

    public override void SerializeMetadataData(BinaryStream bs)
    {
        if (Version >= 2)
        {
            if (Version >= 3)
                bs.WriteInt32(Entries.Count);

            foreach (var entry in Entries)
            {
                bs.WriteUInt32(entry.NameHash);
                bs.WriteUInt32(entry.VertexInputFlags);
            }
        }
    }

    public override void CreateModelBinMetadataData(BinaryStream bs)
    {
        if (Version >= 2)
        {
            var safeEntries = Entries ?? new List<VDCLEntry>();
            if (Version >= 3)
                bs.WriteInt32(safeEntries.Count);

            foreach (var entry in safeEntries)
            {
                bs.WriteUInt32(entry.NameHash);
                bs.WriteUInt32(entry.VertexInputFlags);
            }
        }
    }
}

public class VDCLEntry
{
    public uint NameHash { get; set; }
    //  bitfield of required vertex semantics
    // Bit 0=TEXCOORD0, 1=TEXCOORD1, ... 4=TEXCOORD4, 5=TEXCOORD5/TANGENT0,
    // 6=TANGENT1, 7=TANGENT2, 8=TANGENT3, 9=TANGENT4, 10=COLOR0.
    public uint VertexInputFlags { get; set; }
}
