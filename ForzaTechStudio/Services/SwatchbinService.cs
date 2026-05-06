using System;
using System.IO;
using System.Threading.Tasks;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles.Metadata;
using ForzaTools.Bundles.Metadata.TextureContentHeaders;
using ForzaTechStudio.Models;

namespace ForzaTechStudio.Services;

// Service for parsing and handling swatchbin texture files.
// Uses the ForzaTools.Bundles library for parsing the Grub bundle format.
public class SwatchbinService
{
    public async Task<SwatchbinInfo> LoadSwatchbinAsync(string filePath)
    {
        return await Task.Run(() => LoadSwatchbin(filePath));
    }

    public SwatchbinInfo LoadSwatchbin(string filePath)
    {
        var info = new SwatchbinInfo
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath)
        };

        var bundle = new Bundle();
        using (var stream = File.OpenRead(filePath))
        {
            bundle.Load(stream);
        }

        return LoadSwatchbinFromBundle(bundle, info);
    }

    // Loads a swatchbin from an already-open stream.
    public SwatchbinInfo LoadSwatchbin(Stream stream)
    {
        var info = new SwatchbinInfo
        {
            FilePath = string.Empty,
            FileName = "(stream)"
        };

        var bundle = new Bundle();
        bundle.Load(stream);

        return LoadSwatchbinFromBundle(bundle, info);
    }

    private SwatchbinInfo LoadSwatchbinFromBundle(Bundle bundle, SwatchbinInfo info)
    {
        info.BundleVersionMajor = bundle.VersionMajor;
        info.BundleVersionMinor = bundle.VersionMinor;

        // Find the TXCB (Texture Content Blob)
        TextureContentBlob? txcbBlob = null;
        foreach (var blob in bundle.Blobs)
        {
            if (blob is TextureContentBlob tcb)
            {
                txcbBlob = tcb;
                break;
            }
        }

        if (txcbBlob == null)
        {
            throw new InvalidDataException("No TXCB (Texture Content Blob) found in swatchbin file.");
        }

        info.BlobVersionMajor = txcbBlob.VersionMajor;
        info.BlobVersionMinor = txcbBlob.VersionMinor;

        // Get the TXCH metadata (Texture Content Header)
        var txchMetadata = txcbBlob.GetMetadataByTag<TextureContentHeaderMetadata>(BundleMetadata.TAG_METADATA_TextureContentHeader);
        
        if (txchMetadata == null)
        {
            throw new InvalidDataException("No TXCH (Texture Content Header) metadata found in TXCB blob.");
        }

        // Parse the header with the correct blob version
        txchMetadata.ParseWithBlobVersion(txcbBlob.VersionMajor, txcbBlob.VersionMinor);

        // Parse based on platform (PC vs Durango/Xbox)
        if (txchMetadata.PCHeader != null)
        {
            ParsePCHeader(txchMetadata.PCHeader, info);
        }
        else if (txchMetadata.DurangoHeader != null)
        {
            ParseDurangoHeader(txchMetadata.DurangoHeader, info);
        }
        else
        {
            throw new InvalidDataException("Could not parse texture content header (neither PC nor Durango format detected).");
        }

        // Get the raw texture data from the blob
        byte[] textureData = txcbBlob.Data;
        
        if (textureData == null || textureData.Length == 0)
        {
            throw new InvalidDataException("No texture data found in TXCB blob.");
        }

        // Store raw texture data for decoding
        info.RawTextureData = textureData;

        // Create DDS file for export
        info.DdsData = CreateDdsData(info, textureData);

        return info;
    }

    private void ParsePCHeader(PCTextureContentHeader header, SwatchbinInfo info)
    {
        info.TextureId = header.Id;
        info.Width = header.Width;
        info.Height = header.Height;
        info.Depth = header.Depth;
        info.MipLevels = header.NumMips;
        info.IsTextureCube = header.IsCubeMap;
        info.IsPremultipliedAlpha = header.IsPremultipliedAlpha;
        info.Transcoding = (Models.TextureTranscoding)(int)header.Transcoding;
        info.ColorProfile = (Models.ColorProfile)(int)header.TargetColorProfile;

        // Get encoding from the first slice if available
        if (header.Slices.Count > 0)
        {
            info.Encoding = (Models.TextureEncoding)(int)header.Slices[0].Encoding;
        }

        // Calculate DXGI format
        info.DxgiFormat = GetDxgiFormat(info.Encoding, info.Transcoding, info.ColorProfile);
        info.DxgiFormatName = GetDxgiFormatName(info.DxgiFormat);
    }

    private void ParseDurangoHeader(DurangoTextureContentHeader header, SwatchbinInfo info)
    {
        info.IsDurangoFormat = true;
        info.TextureId = header.Id;
        info.Width = header.Width;
        info.Height = header.Height;
        info.Depth = header.Depth;
        info.MipLevels = header.NumMips;
        info.IsTextureCube = header.IsCubeMap;
        info.IsTexture3D = header.Is3DTexture;
        info.IsPremultipliedAlpha = header.IsPremultipliedAlpha;
        info.Encoding = (Models.TextureEncoding)header.Encoding;
        info.Transcoding = (Models.TextureTranscoding)header.Transcoding;
        info.ColorProfile = (Models.ColorProfile)header.TargetColorProfile;
        
        // Xbox/Durango-specific fields
        info.TileMode = header.TileMode;
        info.TileRelativeMipLevels = header.TileRelativeMipLevels;
        info.TileRelativeMipOffset = header.TileRelativeMipOffset;
        info.TileRelativeWidth = header.TileRelativeWidth;
        info.TileRelativeHeight = header.TileRelativeHeight;
        info.TileRelativeDepth = header.TileRelativeDepth;

        // Calculate DXGI format
        info.DxgiFormat = GetDxgiFormat(info.Encoding, info.Transcoding, info.ColorProfile);
        info.DxgiFormatName = GetDxgiFormatName(info.DxgiFormat);
    }

    private byte[] CreateDdsData(SwatchbinInfo info, byte[] textureData)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Calculate linear size
        uint linearSize = CalculateLinearSize(info);

        // DDS Magic
        writer.Write(0x20534444); // 'DDS '

        // DDS_HEADER - 124 bytes
        writer.Write(124); // dwSize
        writer.Write(0x000A1007); // dwFlags: CAPS | HEIGHT | WIDTH | PIXELFORMAT | MIPMAPCOUNT | LINEARSIZE
        writer.Write(info.Height);
        writer.Write(info.Width);
        writer.Write(linearSize); // dwPitchOrLinearSize
        writer.Write(info.Depth > 1 ? info.Depth : 1u); // dwDepth
        writer.Write((uint)info.MipLevels); // dwMipMapCount

        // dwReserved1[11]
        for (int i = 0; i < 11; i++)
            writer.Write(0);

        // DDS_PIXELFORMAT - 32 bytes
        writer.Write(32); // dwSize
        writer.Write(0x4); // dwFlags: FOURCC
        writer.Write(0x30315844); // dwFourCC: 'DX10'
        writer.Write(0); // dwRGBBitCount
        writer.Write(0); // dwRBitMask
        writer.Write(0); // dwGBitMask
        writer.Write(0); // dwBBitMask
        writer.Write(0); // dwABitMask

        // dwCaps
        uint caps = 0x1000; // DDSCAPS_TEXTURE
        if (info.MipLevels > 1)
            caps |= 0x400008; // DDSCAPS_COMPLEX | DDSCAPS_MIPMAP
        writer.Write(caps);
        
        // dwCaps2
        uint caps2 = 0;
        if (info.IsTextureCube)
            caps2 = 0xFE00; // All cubemap faces
        writer.Write(caps2);
        
        // dwCaps3, dwCaps4, dwReserved2
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        // DDS_HEADER_DXT10 - 20 bytes
        writer.Write(info.DxgiFormat); // dxgiFormat
        
        // resourceDimension
        uint resourceDimension = 3; // D3D10_RESOURCE_DIMENSION_TEXTURE2D
        if (info.IsTexture3D)
            resourceDimension = 4; // D3D10_RESOURCE_DIMENSION_TEXTURE3D
        writer.Write(resourceDimension);
        
        // miscFlag
        uint miscFlag = 0;
        if (info.IsTextureCube)
            miscFlag = 0x4; // D3D11_RESOURCE_MISC_TEXTURECUBE
        writer.Write(miscFlag);
        
        writer.Write(1u); // arraySize
        writer.Write(0u); // miscFlags2: DDS_ALPHA_MODE_UNKNOWN

        // Texture data
        writer.Write(textureData);

        return ms.ToArray();
    }

    private uint CalculateLinearSize(SwatchbinInfo info)
    {
        uint blockSize = GetBlockSize(info.DxgiFormat);
        if (blockSize > 0)
        {
            // Block compressed format
            uint blocksWide = Math.Max(1, (info.Width + 3) / 4);
            uint blocksHigh = Math.Max(1, (info.Height + 3) / 4);
            return blocksWide * blocksHigh * blockSize;
        }
        else
        {
            // Uncompressed - calculate based on format
            uint bpp = GetBitsPerPixel(info.DxgiFormat);
            return (info.Width * bpp + 7) / 8 * info.Height;
        }
    }

    private uint GetBlockSize(uint dxgiFormat)
    {
        return dxgiFormat switch
        {
            71 or 72 => 8,  // BC1
            74 or 75 => 16, // BC2
            77 or 78 => 16, // BC3
            80 or 81 => 8,  // BC4
            83 or 84 => 16, // BC5
            95 or 96 => 16, // BC6H
            98 or 99 => 16, // BC7
            _ => 0
        };
    }

    private uint GetBitsPerPixel(uint dxgiFormat)
    {
        return dxgiFormat switch
        {
            2 => 128,  // R32G32B32A32_FLOAT
            10 => 64,  // R16G16B16A16_FLOAT
            11 => 64,  // R16G16B16A16_UNORM
            28 or 29 => 32, // R8G8B8A8_UNORM / R8G8B8A8_UNORM_SRGB
            49 => 16,  // R8G8_UNORM
            61 => 8,   // R8_UNORM
            65 => 8,   // A8_UNORM
            85 => 16,  // B5G6R5_UNORM
            86 => 16,  // B5G5R5A1_UNORM
            87 => 32,  // B8G8R8A8_UNORM
            _ => 32    // Default to 32bpp
        };
    }

    private uint GetDxgiFormat(Models.TextureEncoding encoding, Models.TextureTranscoding transcoding, Models.ColorProfile colorProfile)
    {
        bool isSrgb = colorProfile == Models.ColorProfile.Rec709SRgb;
        
        // Based on Python: format_encoded = encoding if transcoding <= 1 else transcoding - 2
        int formatEncoded;
        if ((int)transcoding <= 1)
        {
            formatEncoded = (int)encoding;
        }
        else
        {
            formatEncoded = (int)transcoding - 2;
        }

        return formatEncoded switch
        {
            0 => isSrgb ? 72u : 71u,   // BC1_UNORM_SRGB / BC1_UNORM
            1 => isSrgb ? 75u : 74u,   // BC2_UNORM_SRGB / BC2_UNORM
            2 => isSrgb ? 78u : 77u,   // BC3_UNORM_SRGB / BC3_UNORM
            3 => 80u,                   // BC4_UNORM
            4 => 81u,                   // BC4_SNORM
            5 => 83u,                   // BC5_UNORM
            6 => 84u,                   // BC5_SNORM
            7 => 95u,                   // BC6H_UF16
            8 => 96u,                   // BC6H_SF16
            9 => isSrgb ? 99u : 98u,   // BC7_UNORM_SRGB / BC7_UNORM
            10 => 2u,                   // R32G32B32A32_FLOAT
            11 => 11u,                  // R16G16B16A16_UNORM
            12 => 10u,                  // R16G16B16A16_FLOAT
            13 => isSrgb ? 29u : 28u,  // R8G8B8A8_UNORM_SRGB / R8G8B8A8_UNORM
            14 => 85u,                  // B5G6R5_UNORM
            15 => 86u,                  // B5G5R5A1_UNORM
            19 => 61u,                  // R8_UNORM
            20 => 65u,                  // A8_UNORM
            21 => 49u,                  // R8G8_UNORM
            22 => isSrgb ? 99u : 98u,  // BC7_HighQuality -> BC7_UNORM_SRGB / BC7_UNORM
            _ => 0u // UNKNOWN
        };
    }

    private string GetDxgiFormatName(uint format)
    {
        return format switch
        {
            0 => "DXGI_FORMAT_UNKNOWN",
            2 => "DXGI_FORMAT_R32G32B32A32_FLOAT",
            10 => "DXGI_FORMAT_R16G16B16A16_FLOAT",
            11 => "DXGI_FORMAT_R16G16B16A16_UNORM",
            28 => "DXGI_FORMAT_R8G8B8A8_UNORM",
            29 => "DXGI_FORMAT_R8G8B8A8_UNORM_SRGB",
            49 => "DXGI_FORMAT_R8G8_UNORM",
            61 => "DXGI_FORMAT_R8_UNORM",
            65 => "DXGI_FORMAT_A8_UNORM",
            71 => "DXGI_FORMAT_BC1_UNORM",
            72 => "DXGI_FORMAT_BC1_UNORM_SRGB",
            74 => "DXGI_FORMAT_BC2_UNORM",
            75 => "DXGI_FORMAT_BC2_UNORM_SRGB",
            77 => "DXGI_FORMAT_BC3_UNORM",
            78 => "DXGI_FORMAT_BC3_UNORM_SRGB",
            80 => "DXGI_FORMAT_BC4_UNORM",
            81 => "DXGI_FORMAT_BC4_SNORM",
            83 => "DXGI_FORMAT_BC5_UNORM",
            84 => "DXGI_FORMAT_BC5_SNORM",
            85 => "DXGI_FORMAT_B5G6R5_UNORM",
            86 => "DXGI_FORMAT_B5G5R5A1_UNORM",
            87 => "DXGI_FORMAT_B8G8R8A8_UNORM",
            95 => "DXGI_FORMAT_BC6H_UF16",
            96 => "DXGI_FORMAT_BC6H_SF16",
            98 => "DXGI_FORMAT_BC7_UNORM",
            99 => "DXGI_FORMAT_BC7_UNORM_SRGB",
            _ => $"DXGI_FORMAT_{format}"
        };
    }

    public async Task ReplaceSwatchbinAsync(string swatchbinPath, string ddsPath, string outputPath)
    {
        await Task.Run(() => ReplaceSwatchbin(swatchbinPath, ddsPath, outputPath));
    }

    public async Task ReplaceSwatchbinFromImageAsync(
        string swatchbinPath,
        string imagePath,
        string outputPath,
        ForzaTechStudio.Models.TextureEncoding encoding,
        ForzaTechStudio.Models.ColorProfile colorProfile,
        ForzaTechStudio.Models.TextureTranscoding transcoding,
        bool generateMipMaps,
        bool isPremultipliedAlpha)
    {
        await Task.Run(async () =>
        {
            using var fileStream = File.OpenRead(imagePath);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(fileStream.AsRandomAccessStream());
            var bitmap = await decoder.GetSoftwareBitmapAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Rgba8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

            uint width = (uint)bitmap.PixelWidth;
            uint height = (uint)bitmap.PixelHeight;

            byte[] pixelData = new byte[width * height * 4];
            bitmap.CopyToBuffer(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(pixelData));

            var bcEncoder = new BCnEncoder.Encoder.BcEncoder();
            bcEncoder.OutputOptions.GenerateMipMaps = generateMipMaps;
            bcEncoder.OutputOptions.Quality = BCnEncoder.Encoder.CompressionQuality.Balanced;
            bcEncoder.OutputOptions.FileFormat = BCnEncoder.Shared.OutputFileFormat.Dds;

            switch (encoding)
            {
                case ForzaTechStudio.Models.TextureEncoding.Bc1: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc1; break;
                case ForzaTechStudio.Models.TextureEncoding.Bc2: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc2; break;
                case ForzaTechStudio.Models.TextureEncoding.Bc3: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc3; break;
                case ForzaTechStudio.Models.TextureEncoding.Bc7: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc7; break;
                case ForzaTechStudio.Models.TextureEncoding.SignedBc4:
                case ForzaTechStudio.Models.TextureEncoding.UnsignedBc4: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc4; break;
                case ForzaTechStudio.Models.TextureEncoding.SignedBc5:
                case ForzaTechStudio.Models.TextureEncoding.UnsignedBc5: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc5; break;
                case ForzaTechStudio.Models.TextureEncoding.R8G8B8A8: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.R; break;
                default: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc7; break;
            }

            var ddsStream = new System.IO.MemoryStream();
            bcEncoder.EncodeToStream(pixelData, (int)width, (int)height, BCnEncoder.Encoder.PixelFormat.Rgba32, ddsStream);
            byte[] ddsBytes = ddsStream.ToArray();

            ReplaceSwatchbinCore(swatchbinPath, ddsBytes, outputPath);
        });
    }

    public void ReplaceSwatchbin(string swatchbinPath, string ddsPath, string outputPath)
    {
        byte[] ddsBytes = File.ReadAllBytes(ddsPath);
        ReplaceSwatchbinCore(swatchbinPath, ddsBytes, outputPath);
    }

    private void ReplaceSwatchbinCore(string swatchbinPath, byte[] ddsBytes, string outputPath)
    {
         var ddsInfo = ParseDdsHeader(ddsBytes);

         // Load Bundle
         var bundle = new Bundle();
         using (var fs = File.OpenRead(swatchbinPath))
         {
             bundle.Load(fs);
         }

         // Find TXCB
         TextureContentBlob? txcb = null;
         foreach (var blob in bundle.Blobs)
         {
             if (blob is TextureContentBlob t)
             {
                 txcb = t;
                 break;
             }
         }
         
         if (txcb == null) throw new InvalidDataException("No TXCB blob found.");

         // Update Metadata
         var meta = txcb.GetMetadataByTag<TextureContentHeaderMetadata>(BundleMetadata.TAG_METADATA_TextureContentHeader);
         if (meta == null) throw new InvalidDataException("No TXCH metadata found.");
         
         meta.ParseWithBlobVersion(txcb.VersionMajor, txcb.VersionMinor);
         
         // Map Formats
         MapDxgiToInternal(ddsInfo.DxgiFormat, out var encoding, out var transcoding, out var colorProfile);
         
        
        if (meta.PCHeader != null)
         {
             meta.PCHeader.Width = ddsInfo.Width;
             meta.PCHeader.Height = ddsInfo.Height;
             meta.PCHeader.NumMips = (byte)ddsInfo.MipMapCount;
             meta.PCHeader.Transcoding = (ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureTranscoding)transcoding;
             meta.PCHeader.TargetColorProfile = (ForzaTools.Bundles.Metadata.TextureContentHeaders.ColorProfile)colorProfile;
             meta.PCHeader.IsPremultipliedAlpha = ddsInfo.IsPremultipliedAlpha;

             // Handle Slices/Mips
             meta.PCHeader.Slices.Clear();
             var slice = new ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureContentSlice
             {
                 Encoding = (ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureEncoding)encoding
             };
             
             // Calculate Mip Offsets/Sizes
             uint currentOffset = 0;
             uint currentW = ddsInfo.Width;
             uint currentH = ddsInfo.Height;
             uint bpp = GetBitsPerPixel(ddsInfo.DxgiFormat);
             bool isBlockCompressed = GetBlockSize(ddsInfo.DxgiFormat) > 0;
             uint blockSize = GetBlockSize(ddsInfo.DxgiFormat);

             for (int i = 0; i < ddsInfo.MipMapCount; i++)
             {
                 uint size = 0;
                 if (isBlockCompressed)
                 {
                     uint bw = Math.Max(1, (currentW + 3) / 4);
                     uint bh = Math.Max(1, (currentH + 3) / 4);
                     size = bw * bh * blockSize;
                 }
                 else
                 {
                     size = (currentW * bpp + 7) / 8 * currentH;
                 }

                 slice.Mips.Add(new ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureContentMip 
                 {
                     BlobOffset = currentOffset,
                     BlobSize = size
                 });
                 
                 currentOffset += size;
                 if(currentW > 1) currentW /= 2;
                 if(currentH > 1) currentH /= 2;
             }
             
             meta.PCHeader.Slices.Add(slice);
         }
         else if (meta.DurangoHeader != null)
         {
             throw new NotSupportedException("Replacing Durango textures is not yet supported.");
         }

         // Replace Data
         int headerSize = 124 + 4; // Magic + Header
         if (ddsInfo.HasDx10Header) headerSize += 20;

         if (ddsBytes.Length < headerSize) throw new InvalidDataException("DDS file too small.");

         byte[] rawData = new byte[ddsBytes.Length - headerSize];
         Array.Copy(ddsBytes, headerSize, rawData, 0, rawData.Length);
         
         txcb.Data = rawData;
         
         // Save
         using (var fs = File.Create(outputPath))
         {
             bundle.SerializeConverted(fs);
         }
    }

    public async Task CreateSwatchbinAsync(string ddsPath, string outputPath)
    {
        await Task.Run(() => CreateSwatchbin(ddsPath, outputPath));
    }

    public async Task CreateSwatchbinFromImageAsync(
        string imagePath, 
        string outputPath, 
        ForzaTechStudio.Models.TextureEncoding encoding, 
        ForzaTechStudio.Models.ColorProfile colorProfile, 
        ForzaTechStudio.Models.TextureTranscoding transcoding, 
        bool generateMipMaps, 
        bool isCubeMap, 
        bool is3D, 
        bool isPremultipliedAlpha,
        Guid? textureGuid)
    {
        await Task.Run(async () => 
        {
            using var fileStream = File.OpenRead(imagePath);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(fileStream.AsRandomAccessStream());
            var bitmap = await decoder.GetSoftwareBitmapAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Rgba8, 
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

            uint width = (uint)bitmap.PixelWidth;
            uint height = (uint)bitmap.PixelHeight;
            
            byte[] pixelData = new byte[width * height * 4];
            bitmap.CopyToBuffer(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(pixelData));

            var bcEncoder = new BCnEncoder.Encoder.BcEncoder();
            bcEncoder.OutputOptions.GenerateMipMaps = generateMipMaps;
            bcEncoder.OutputOptions.Quality = BCnEncoder.Encoder.CompressionQuality.Balanced;
            bcEncoder.OutputOptions.FileFormat = BCnEncoder.Shared.OutputFileFormat.Dds;
            bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc1;
            
            switch (encoding)
            {
                case ForzaTechStudio.Models.TextureEncoding.Bc1: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc1; break;
                case ForzaTechStudio.Models.TextureEncoding.Bc2: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc2; break;
                case ForzaTechStudio.Models.TextureEncoding.Bc3: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc3; break;
                case ForzaTechStudio.Models.TextureEncoding.Bc7: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc7; break;
                case ForzaTechStudio.Models.TextureEncoding.SignedBc4:
                case ForzaTechStudio.Models.TextureEncoding.UnsignedBc4: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc4; break;
                case ForzaTechStudio.Models.TextureEncoding.SignedBc5:
                case ForzaTechStudio.Models.TextureEncoding.UnsignedBc5: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc5; break;
                case ForzaTechStudio.Models.TextureEncoding.R8G8B8A8: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.R; break;
                default: bcEncoder.OutputOptions.Format = BCnEncoder.Shared.CompressionFormat.Bc7; break;
            }

            var ddsHeaderStream = new System.IO.MemoryStream();
            var dxgiFormat = GetDxgiFormat(encoding, transcoding, colorProfile);
            
            bcEncoder.EncodeToStream(pixelData, (int)width, (int)height, BCnEncoder.Encoder.PixelFormat.Rgba32, ddsHeaderStream);

            byte[] ddsBytes = ddsHeaderStream.ToArray();

            var ddsInfo = ParseDdsHeader(ddsBytes);

            var bundle = new Bundle 
            {
                VersionMajor = 1,
                VersionMinor = 1 
            };

            var txcb = new TextureContentBlob
            {
                Tag = Bundle.TAG_BLOB_TextureContentBlob,
                VersionMajor = 0,
                VersionMinor = 0
            };
            
            var meta = new TextureContentHeaderMetadata
            {
                Tag = BundleMetadata.TAG_METADATA_TextureContentHeader,
                Version = 0
            };
            
            meta.PCHeader = new PCTextureContentHeader();
            meta.PCHeader.Id = textureGuid ?? Guid.NewGuid();
            meta.PCHeader.Width = width;
            meta.PCHeader.Height = height;
            meta.PCHeader.Depth = is3D ? ddsInfo.Depth : 1;
            meta.PCHeader.NumMips = generateMipMaps ? (byte)ddsInfo.MipMapCount : (byte)1;
            meta.PCHeader.Transcoding = (ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureTranscoding)(int)transcoding;
            meta.PCHeader.TargetColorProfile = (ForzaTools.Bundles.Metadata.TextureContentHeaders.ColorProfile)(int)colorProfile;
            meta.PCHeader.Domain = ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureDomain.Wrap; 
            meta.PCHeader.IsCubeMap = isCubeMap;
            meta.PCHeader.IsPremultipliedAlpha = isPremultipliedAlpha;

            meta.PCHeader.Slices.Clear();

            int numSlices = isCubeMap ? 6 : 1;
            
            uint currentOffset = 0;
            uint bpp = GetBitsPerPixel(dxgiFormat);
            bool isBlockCompressed = GetBlockSize(dxgiFormat) > 0;
            uint blockSize = GetBlockSize(dxgiFormat);

            for (int s = 0; s < numSlices; s++)
            {
                var slice = new ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureContentSlice
                {
                    Encoding = (ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureEncoding)(int)encoding
                };

                uint currentW = width;
                uint currentH = height;

                for (int i = 0; i < meta.PCHeader.NumMips; i++)
                {
                    uint size = 0;
                    if (isBlockCompressed)
                    {
                        uint bw = Math.Max(1, (currentW + 3) / 4);
                        uint bh = Math.Max(1, (currentH + 3) / 4);
                        size = bw * bh * blockSize;
                    }
                    else
                    {
                        size = (currentW * bpp + 7) / 8 * currentH;
                    }

                    slice.Mips.Add(new ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureContentMip 
                    {
                        BlobOffset = currentOffset,
                        BlobSize = size
                    });
                    
                    currentOffset += size;
                    if(currentW > 1) currentW /= 2;
                    if(currentH > 1) currentH /= 2;
                }
                
                meta.PCHeader.Slices.Add(slice);
            }
            
            txcb.Metadatas.Add(meta);
            bundle.Blobs.Add(txcb);

            int headerSize = 124 + 4; 
            if (ddsInfo.HasDx10Header) headerSize += 20;

            byte[] rawData = new byte[ddsBytes.Length - headerSize];
            Array.Copy(ddsBytes, headerSize, rawData, 0, rawData.Length);
            
            txcb.Data = rawData;

            using (var outStream = File.Create(outputPath))
            {
                bundle.SerializeConverted(outStream);
            }
        });
    }

    public void CreateSwatchbin(string ddsPath, string outputPath, Guid? sourceId = null)
    {
         byte[] ddsBytes = File.ReadAllBytes(ddsPath);
         var ddsInfo = ParseDdsHeader(ddsBytes);

         // Create new Bundle
         var bundle = new Bundle 
         {
             VersionMajor = 1,
             VersionMinor = 1 
         };

         // Create TXCB Blob
         var txcb = new TextureContentBlob
         {
             Tag = Bundle.TAG_BLOB_TextureContentBlob,
             VersionMajor = 0,
             VersionMinor = 0
         };
         
         // Create Metadata
         var meta = new TextureContentHeaderMetadata
         {
             Tag = BundleMetadata.TAG_METADATA_TextureContentHeader,
             Version = 0
         };
         
         // Configure PC Header
         meta.PCHeader = new PCTextureContentHeader();
         
         MapDxgiToInternal(ddsInfo.DxgiFormat, out var encoding, out var transcoding, out var colorProfile);
         
         // Preserve the source texture's GUID if provided (required for game to locate the texture).
         // Generating a random GUID causes game crashes because the asset look-up fails.
         meta.PCHeader.Id = sourceId ?? Guid.NewGuid();
         meta.PCHeader.Width = ddsInfo.Width;
         meta.PCHeader.Height = ddsInfo.Height;
         meta.PCHeader.Depth = ddsInfo.Depth == 0 ? 1 : ddsInfo.Depth;
         meta.PCHeader.NumMips = (byte)ddsInfo.MipMapCount;
         meta.PCHeader.Transcoding = (ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureTranscoding)transcoding;
         meta.PCHeader.TargetColorProfile = (ForzaTools.Bundles.Metadata.TextureContentHeaders.ColorProfile)colorProfile;
         // Default others
         meta.PCHeader.Domain = ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureDomain.Wrap; 
         meta.PCHeader.IsCubeMap = ddsInfo.IsCubeMap;
         meta.PCHeader.IsPremultipliedAlpha = ddsInfo.IsPremultipliedAlpha;

         // Handle Slices/Mips
         meta.PCHeader.Slices.Clear();

         int numSlices = ddsInfo.IsCubeMap ? 6 : 1;
         
         // Calculate Mip Offsets/Sizes
         uint currentOffset = 0;
         uint bpp = GetBitsPerPixel(ddsInfo.DxgiFormat);
         bool isBlockCompressed = GetBlockSize(ddsInfo.DxgiFormat) > 0;
         uint blockSize = GetBlockSize(ddsInfo.DxgiFormat);

         for (int s = 0; s < numSlices; s++)
         {
             var slice = new ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureContentSlice
             {
                 Encoding = (ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureEncoding)encoding
             };

             uint currentW = ddsInfo.Width;
             uint currentH = ddsInfo.Height;

             for (int i = 0; i < ddsInfo.MipMapCount; i++)
             {
                 uint size = 0;
                 if (isBlockCompressed)
                 {
                     uint bw = Math.Max(1, (currentW + 3) / 4);
                     uint bh = Math.Max(1, (currentH + 3) / 4);
                     size = bw * bh * blockSize;
                 }
                 else
                 {
                     size = (currentW * bpp + 7) / 8 * currentH;
                 }

                 slice.Mips.Add(new ForzaTools.Bundles.Metadata.TextureContentHeaders.TextureContentMip 
                 {
                     BlobOffset = currentOffset,
                     BlobSize = size
                 });
                 
                 currentOffset += size;
                 if(currentW > 1) currentW /= 2;
                 if(currentH > 1) currentH /= 2;
             }
             
             meta.PCHeader.Slices.Add(slice);
         }
         
         txcb.Metadatas.Add(meta);
         bundle.Blobs.Add(txcb);

         // Set Blob Data from DDS (Skipping header)
         int headerSize = 124 + 4; 
         if (ddsInfo.HasDx10Header) headerSize += 20;

         if (ddsBytes.Length < headerSize) throw new InvalidDataException("DDS file too small.");

         byte[] rawData = new byte[ddsBytes.Length - headerSize];
         Array.Copy(ddsBytes, headerSize, rawData, 0, rawData.Length);
         
         txcb.Data = rawData;
         
         // Save
         using (var fs = File.Create(outputPath))
         {
             bundle.SerializeConverted(fs);
         }
    }

    private void MapDxgiToInternal(uint dxgiFormat, out int encoding, out int transcoding, out int colorProfile)
    {
        // Force Transcoding 0 (None)
        transcoding = 0; 
        
        bool isSrgb = dxgiFormat is 72 or 75 or 78 or 99 or 29;
        colorProfile = isSrgb ? 1 : 0; 
        
        encoding = dxgiFormat switch
        {
            71 or 72 => 0, // Bc1
            74 or 75 => 1, // Bc2
            77 or 78 => 2, // Bc3
            80 => 3, // Bc4 Unsigned
            81 => 4, // Bc4 Signed
            83 => 5, // Bc5 Unsigned
            84 => 6, // Bc5 Signed
            95 => 7, // Bc6H Unsigned
            96 => 8, // Bc6H Signed
            98 or 99 => 9, // Bc7
            2 => 10, // Float4
            11 => 11, // Short4
            10 => 12, // Half4
            28 or 29 => 13, // Byte4
            85 => 14, // B5G6R5
            86 => 15, // B5G5R5A1
            61 => 19, // R8
            65 => 20, // A8
            49 => 21, // R8G8
            _ => 13 // Default
        };
    }

    private DdsHeaderInfo ParseDdsHeader(byte[] data)
    {
        if (data == null || data.Length < 128) throw new InvalidDataException("DDS file is too small or invalid.");

        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        
        if (br.ReadUInt32() != 0x20534444) throw new InvalidDataException("Not a DDS file (Missing Magic).");
        
        var info = new DdsHeaderInfo();
        uint dwSize = br.ReadUInt32(); // dwSize
        if (dwSize != 124) throw new InvalidDataException($"Invalid DDS Header Size: {dwSize} (Expected 124).");

        br.ReadUInt32(); // dwFlags
        info.Height = br.ReadUInt32();
        info.Width = br.ReadUInt32();
        br.ReadUInt32(); // Pitch/Linear
        info.Depth = br.ReadUInt32(); // Depth
        info.MipMapCount = br.ReadUInt32();
        if (info.MipMapCount == 0) info.MipMapCount = 1;

        // Skip Reserved1 (11 * 4 bytes)
        for (int i = 0; i < 11; i++) br.ReadUInt32();
        
        // Pixel Format
        uint pfSize = br.ReadUInt32(); // Size (struct size, typically 32)
        if (pfSize != 32) throw new InvalidDataException($"Invalid DDS PixelFormat Size: {pfSize} (Expected 32).");

        uint flags = br.ReadUInt32(); // Flags (DDPF_FOURCC, DDPF_RGB, etc.)
        uint fourCC = br.ReadUInt32();
        uint bitCount = br.ReadUInt32();
        uint rMask = br.ReadUInt32();
        uint gMask = br.ReadUInt32();
        uint bMask = br.ReadUInt32();
        uint aMask = br.ReadUInt32();
        
        // Read Caps
        uint caps1 = br.ReadUInt32();
        uint caps2 = br.ReadUInt32();
        br.ReadUInt32(); // Caps3
        br.ReadUInt32(); // Caps4
        br.ReadUInt32(); // Reserved2
        
        if ((caps2 & 0xFE00) != 0) // DDSCAPS2_CUBEMAP_ALLFACES
        {
            info.IsCubeMap = true;
        }
        
        if (fourCC == 0x30315844) // 'DX10'
        {
            info.HasDx10Header = true;
            info.DxgiFormat = br.ReadUInt32();
            br.ReadUInt32(); // resourceDimension
            uint miscFlag = br.ReadUInt32();
            if ((miscFlag & 0x4) != 0) info.IsCubeMap = true; // D3D11_RESOURCE_MISC_TEXTURECUBE
            br.ReadUInt32(); // arraySize
            uint miscFlags2 = br.ReadUInt32();
            if (miscFlags2 == 2) info.IsPremultipliedAlpha = true; // DDS_ALPHA_MODE_PREMULTIPLIED
        }
        else
        {
            // Try FourCC first if flag is set OR if fourCC looks valid (heuristic)
            if ((flags & 0x4) != 0 || fourCC != 0) 
            {
                info.DxgiFormat = FourCCToDxgi(fourCC);
            }
            
            // If No FourCC matched (0), try RGB
            if (info.DxgiFormat == 0 && (flags & 0x40) != 0) // DDPF_RGB
            {
                info.DxgiFormat = RgbMasksToDxgi(bitCount, rMask, gMask, bMask, aMask);
            }
        }
        
        if (info.DxgiFormat == 0)
        {
            throw new NotSupportedException($"Unsupported DDS Format. Flags: {flags:X}, FourCC: {fourCC:X}, BitCount: {bitCount}");
        }

        return info;
    }
    
    private uint RgbMasksToDxgi(uint bitCount, uint rMask, uint gMask, uint bMask, uint aMask)
    {
         if (bitCount == 32)
         {
             if (rMask == 0xFF && gMask == 0xFF00 && bMask == 0xFF0000 && aMask == 0xFF000000) return 28; // R8G8B8A8_UNORM
             if (rMask == 0xFF0000 && gMask == 0xFF00 && bMask == 0xFF && aMask == 0xFF000000) return 87; // B8G8R8A8_UNORM
             if (rMask == 0xFFFF && gMask == 0xFFFF0000) return 49; // R16G16_UNORM ?? Not really standard masks for R16G16
         }
         if (bitCount == 16)
         {
             if (rMask == 0xF800 && gMask == 0x7E0 && bMask == 0x1F) return 85; // B5G6R5_UNORM
             if (rMask == 0x7C00 && gMask == 0x3E0 && bMask == 0x1F && aMask == 0x8000) return 86; // B5G5R5A1_UNORM
             if (aMask == 0xFF00 && rMask == 0xFF) return 49; // R8G8_UNORM (Maybe? rare in DDS)
             if (rMask == 0xFFFF) return 56; // R16_UNORM
         }
         if (bitCount == 8)
         {
             if (rMask == 0xFF) return 61; // R8_UNORM
             if (aMask == 0xFF) return 65; // A8_UNORM
         }
         return 0; // Unknown
    }

    private uint FourCCToDxgi(uint fourCC)
    {
         if (fourCC == 0x31545844) return 71; // DXT1
         if (fourCC == 0x33545844) return 74; // DXT3
         if (fourCC == 0x35545844) return 77; // DXT5
         if (fourCC == 0x55344342) return 80; // BC4U
         if (fourCC == 0x53344342) return 81; // BC4S
         if (fourCC == 0x55354342) return 83; // BC5U
         if (fourCC == 0x53354342) return 84; // BC5S
         if (fourCC == 0x31495441) return 80; // ATI1 -> BC4
         if (fourCC == 0x32495441) return 83; // ATI2 -> BC5
         return 0;
    }

    private class DdsHeaderInfo
    {
        public uint Width;
        public uint Height;
        public uint Depth;
        public uint MipMapCount;
        public uint DxgiFormat;
        public bool HasDx10Header;
        public bool IsCubeMap;
        public bool IsPremultipliedAlpha;
    }
}
