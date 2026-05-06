using Syroot.BinaryData;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ForzaTools.Bundles.Blobs;

public class ManufacturerColorsBlob : BundleBlob
{
    public List<ManufacturerColorGroup> Groups { get; set; } = new();

    public override void ReadBlobData(BinaryStream bs)
    {
        byte groupCount = bs.Read1Byte();
        for (int i = 0; i < groupCount; i++)
        {
            var group = new ManufacturerColorGroup();
            byte matCount = bs.Read1Byte();

            for (int m = 0; m < matCount; m++)
            {
                var entry = new ManufacturerColorEntry();

                if (IsAtLeastVersion(1, 1))
                    entry.MaterialIndexMask = bs.ReadUInt32();
                else
                    entry.MaterialIndexMask = bs.ReadUInt16();

                // float[3]
                entry.PreviewColor = new Vector3(bs.ReadSingle(), bs.ReadSingle(), bs.ReadSingle());

                entry.Path = bs.ReadString(StringCoding.VariableByteCount);

                group.Entries.Add(entry);
            }
            Groups.Add(group);
        }
    }

    public override void SerializeBlobData(BinaryStream bs)
    {
        bs.WriteByte((byte)Groups.Count);
        foreach (var group in Groups)
        {
            bs.WriteByte((byte)group.Entries.Count);
            foreach (var entry in group.Entries)
            {
                if (IsAtLeastVersion(1, 1))
                    bs.WriteUInt32(entry.MaterialIndexMask);
                else
                    bs.WriteUInt16((ushort)entry.MaterialIndexMask);

                bs.WriteSingle(entry.PreviewColor.X);
                bs.WriteSingle(entry.PreviewColor.Y);
                bs.WriteSingle(entry.PreviewColor.Z);

                bs.WriteString(entry.Path, StringCoding.VariableByteCount);
            }
        }
    }
    //not needed for modelbin
    public override void CreateModelBinBlobData(BinaryStream bs)
    {
        bs.WriteByte((byte)Groups.Count);
        foreach (var group in Groups)
        {
            bs.WriteByte((byte)group.Entries.Count);
            foreach (var entry in group.Entries)
            {
                if (IsAtLeastVersion(1, 1))
                    bs.WriteUInt32(entry.MaterialIndexMask);
                else
                    bs.WriteUInt16((ushort)entry.MaterialIndexMask);

                bs.WriteSingle(entry.PreviewColor.X);
                bs.WriteSingle(entry.PreviewColor.Y);
                bs.WriteSingle(entry.PreviewColor.Z);

                bs.WriteString(entry.Path ?? "", StringCoding.VariableByteCount);
            }
        }
    }
}

public class ManufacturerColorGroup
{
    public List<ManufacturerColorEntry> Entries { get; set; } = new();
}

public class ManufacturerColorEntry
{
    public uint MaterialIndexMask { get; set; }
    public Vector3 PreviewColor { get; set; }
    public string Path { get; set; }
}