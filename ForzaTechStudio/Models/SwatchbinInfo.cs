using System;
using DurangoTypes;

namespace ForzaTechStudio.Models;


// Represents parsed information from a swatchbin texture file.
// Swatchbin files are essentially DDS textures with a custom Grub bundle header.

public class SwatchbinInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    
    // Texture properties from TXCH metadata
    public Guid TextureId { get; set; }
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint Depth { get; set; }
    public byte MipLevels { get; set; }
    
    // Format information
    public TextureEncoding Encoding { get; set; }
    public TextureTranscoding Transcoding { get; set; }
    public ColorProfile ColorProfile { get; set; }
    public string DxgiFormatName { get; set; } = string.Empty;
    public uint DxgiFormat { get; set; }
    
    // Flags
    public bool IsTextureCube { get; set; }
    public bool IsTexture3D { get; set; }
    public bool IsPremultipliedAlpha { get; set; }
    
    // Raw texture pixel data (without DDS header)
    public byte[]? RawTextureData { get; set; }
    
    // Complete DDS data (header + pixel data combined) for export
    public byte[]? DdsData { get; set; }
    
    // Bundle version info
    public byte BundleVersionMajor { get; set; }
    public byte BundleVersionMinor { get; set; }
    public byte BlobVersionMajor { get; set; }
    public byte BlobVersionMinor { get; set; }
    
    // Xbox/Durango-specific properties
    public bool IsDurangoFormat { get; set; }  // True if BlobVersionMajor == 2
    public XG_TILE_MODE? TileMode { get; set; }  // Nullable, only for Durango
    public byte TileRelativeMipLevels { get; set; }
    public byte TileRelativeMipOffset { get; set; }
    public ushort TileRelativeWidth { get; set; }
    public ushort TileRelativeHeight { get; set; }
    public ushort TileRelativeDepth { get; set; }
}


// Texture encoding formats used in swatchbin files.
// Maps to ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureEncoding

public enum TextureEncoding
{
    Bc1 = 0,
    Bc2 = 1,
    Bc3 = 2,
    UnsignedBc4 = 3,
    SignedBc4 = 4,
    UnsignedBc5 = 5,
    SignedBc5 = 6,
    UnsignedBc6H = 7,
    SignedBc6H = 8,
    Bc7 = 9,
    R32G32B32A32Float = 10,
    R16G16B16A16 = 11,
    R16G16B16A16Float = 12,
    R8G8B8A8 = 13,
    B5G6R5 = 14,
    B5G5R5A1 = 15,
    Dct = 16,
    IntegerDct = 17,
    Procedural = 18,
    R8 = 19,
    A8 = 20,
    R8G8 = 21,
    Bc7_HighQuality = 22
}


// Texture transcoding formats

public enum TextureTranscoding
{
    None = 0,
    BcBlockRle = 1,
    Bc1 = 2,
    Bc2 = 3,
    Bc3 = 4,
    UnsignedBc4 = 5,
    SignedBc4 = 6,
    UnsignedBc5 = 7,
    SignedBc5 = 8,
    UnsignedBc6H = 9,
    SignedBc6H = 10,
    Bc7 = 11
}


// Color profile formats

public enum ColorProfile
{
    Rec709Linear = 0,
    Rec709SRgb = 1,
    Rec709Gamma2 = 2,
    XvYccLinear = 3
}
