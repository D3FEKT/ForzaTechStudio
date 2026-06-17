using System;
using System.IO;
using System.Runtime.InteropServices;
using DurangoTypes;
using ForzaTechStudio.Models;
using ForzaTools.Shared;

namespace ForzaTechStudio.Services;


// Lightweight mip layout info read directly from native memory.
internal struct MipLayoutInfo
{
    public ulong SizeBytes;
    public ulong OffsetBytes;
    public uint PitchBytes;
}

// Lightweight resource layout info with manually-read mip layouts.
internal struct ResourceLayoutInfo
{
    public ulong SizeBytes;
    public uint MipLevels;
    public MipLayoutInfo[] MipLayouts;
}

// Converts Durango (Xbox) swatchbin bundles to PC format: detiles via XG native, dealigns mips, builds DDS, then writes a PC swatchbin.
public class SwatchbinConversionService
{
    private readonly SwatchbinService _swatchbinService = new();

    // ? Platform probe 

    // Quickly probes a swatchbin stream to decide whether it is a Durango (Xbox)
    public bool? IsDurango(Stream stream)
    {
        long originalPos = stream.Position;
        try
        {
            var info = _swatchbinService.LoadSwatchbin(stream);
            return info.IsDurangoFormat;
        }
        catch
        {
            return null;
        }
        finally
        {
            stream.Position = originalPos;
        }
    }

    // Probes a swatchbin file path.  Returns <c>null</c> on error.
    public bool? IsDurango(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            return IsDurango(fs);
        }
        catch { return null; }
    }

    // Loads a <see cref="SwatchbinInfo"/> from a stream without resetting position.
    public SwatchbinInfo LoadInfo(Stream stream) => _swatchbinService.LoadSwatchbin(stream);


    public byte[]? GetRenderReadyDdsData(SwatchbinInfo info)
    {
        if (info?.DdsData == null && info?.RawTextureData == null)
            return null;

        if (!info.IsDurangoFormat)
            return info.DdsData;

        if (info.TileMode == XG_TILE_MODE.XG_TILE_MODE_2D_THIN ||
            info.TileMode == XG_TILE_MODE.XG_TILE_MODE_1D_THIN)
        {
            var detiledResult = DurangoDetile(info, info.RawTextureData!);
            if (detiledResult == null)
                return null;

            var format = (XG_FORMAT)info.DxgiFormat;
            byte[] dealigned = DealignDurangoTextureData(info, format, detiledResult.Value);

            return CreateDdsFromLinearData(
                dealigned,
                (int)info.Width, (int)info.Height,
                info.MipLevels, info.DxgiFormat,
                info.IsTextureCube, info.IsTexture3D, info.Depth);
        }

        return null; // unsupported tile mode — caller should fall back to raw
    }

    // Loads a swatchbin from raw bytes and returns render-ready DDS data,
    // detiling Durango textures automatically.

    public byte[]? LoadRenderReadyDdsFromBytes(byte[] swatchbinBytes)
    {
        if (swatchbinBytes == null || swatchbinBytes.Length == 0)
            return null;

        using var stream = new MemoryStream(swatchbinBytes, writable: false);
        var info = _swatchbinService.LoadSwatchbin(stream);
        return GetRenderReadyDdsData(info);
    }

    // Public conversion entry-points 

 
    public void ConvertDurangoToPc(Stream inputStream, Stream outputStream)
    {
        var info = _swatchbinService.LoadSwatchbin(inputStream);

        if (!info.IsDurangoFormat)
            throw new InvalidOperationException("Source swatchbin is not in Durango/Xbox format.");

        byte[] linearDdsData = DetileAndBuildDds(info);

        // Write via a temp file so SwatchbinService.CreateSwatchbin can seek
        string tempDds = Path.GetTempFileName() + ".dds";
        string tempOut = Path.GetTempFileName() + ".swatchbin";
        try
        {
            File.WriteAllBytes(tempDds, linearDdsData);
            // Preserve the source texture GUID so the game can look it up correctly.
            _swatchbinService.CreateSwatchbin(tempDds, tempOut, info.TextureId);

            using var fs = File.OpenRead(tempOut);
            fs.CopyTo(outputStream);
        }
        finally
        {
            try { if (File.Exists(tempDds)) File.Delete(tempDds); } catch { }
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    // File-path overload used by the single-file conversion path.
    public void ConvertDurangoToPc(string inputPath, string outputPath)
    {
        var info = _swatchbinService.LoadSwatchbin(inputPath);

        if (!info.IsDurangoFormat)
            throw new InvalidOperationException("Source swatchbin is not in Durango/Xbox format.");

        byte[] linearDdsData = DetileAndBuildDds(info);

        string tempDds = Path.GetTempFileName() + ".dds";
        try
        {
            File.WriteAllBytes(tempDds, linearDdsData);
            // Preserve the source texture GUID so the game can look it up correctly.
            _swatchbinService.CreateSwatchbin(tempDds, outputPath, info.TextureId);
        }
        finally
        {
            try { if (File.Exists(tempDds)) File.Delete(tempDds); } catch { }
        }
    }

    //  Internal pipeline

    internal byte[] DetileAndBuildDds(SwatchbinInfo info)
    {
        if (info.TileMode == XG_TILE_MODE.XG_TILE_MODE_2D_THIN ||
            info.TileMode == XG_TILE_MODE.XG_TILE_MODE_1D_THIN)
        {
            var detiledData = DurangoDetile(info, info.RawTextureData)
                ?? throw new Exception("Failed to detile Xbox texture data.");

            var format = (XG_FORMAT)info.DxgiFormat;
            byte[] dealigned = DealignDurangoTextureData(info, format, detiledData);

            return CreateDdsFromLinearData(
                dealigned,
                (int)info.Width, (int)info.Height,
                info.MipLevels, info.DxgiFormat,
                info.IsTextureCube, info.IsTexture3D, info.Depth);
        }

        throw new NotSupportedException($"Tile mode {info.TileMode} is not supported for conversion.");
    }

    internal byte[] GetLinearTextureDataForDisplay(SwatchbinInfo info)
    {
        if (info.RawTextureData == null || info.RawTextureData.Length == 0)
            return [];

        if (!info.IsDurangoFormat)
            return info.RawTextureData;

        if (info.TileMode == XG_TILE_MODE.XG_TILE_MODE_2D_THIN ||
            info.TileMode == XG_TILE_MODE.XG_TILE_MODE_1D_THIN)
        {
            var detiledData = DurangoDetile(info, info.RawTextureData)
                ?? throw new Exception("Failed to detile Xbox texture data.");

            var format = (XG_FORMAT)info.DxgiFormat;
            return DealignDurangoTextureData(info, format, detiledData);
        }

        throw new NotSupportedException($"Tile mode {info.TileMode} is not supported for preview.");
    }

    //  Detiling 

    private unsafe (ResourceLayoutInfo Layout, byte[] Data)? DurangoDetile(SwatchbinInfo info, byte[] tiledData)
    {
        if (info == null || !info.IsDurangoFormat || tiledData == null || tiledData.Length == 0)
            return null;

        XG_FORMAT format = (XG_FORMAT)info.DxgiFormat;

        XG_TEXTURE2D_DESC desc = default;
        desc.Width = info.Width;
        desc.Height = info.Height;
        desc.Format = format;
        desc.Usage = XG_USAGE.XG_USAGE_DEFAULT;
        desc.SampleDesc.Count = 1;
        desc.SampleDesc.Quality = 0;
        desc.ArraySize = info.Depth;
        desc.MipLevels = info.MipLevels;
        desc.BindFlags = (uint)XG_BIND_FLAG.XG_BIND_SHADER_RESOURCE;
        desc.MiscFlags = 0;
        desc.TileMode = info.TileMode ?? XG_TILE_MODE.XG_TILE_MODE_INVALID;
        desc.Pitch = 0;
        desc.CPUAccessFlags = 0;
        desc.ESRAMOffsetBytes = 0;
        desc.ESRAMUsageBytes = 0;

        XGTextureAddressComputer* compWrapper;
        int result = XGImports.XGCreateTexture2DComputer(&desc, &compWrapper);
        if (result != 0 || compWrapper == null)
            return null;

        XGTextureAddressComputer computer = *compWrapper;

        const int layoutBufferSize = 128 * 1024;
        nint arrPtr = Marshal.AllocHGlobal(layoutBufferSize);
        new Span<byte>((void*)arrPtr, layoutBufferSize).Clear();

        result = computer.vt->GetResourceLayout(compWrapper, (XG_RESOURCE_LAYOUT*)arrPtr);
        if (result != 0)
        {
            Marshal.FreeHGlobal(arrPtr);
            return null;
        }

        try
        {
            ResourceLayoutInfo layoutInfo = ReadResourceLayoutFromNative(arrPtr, desc.MipLevels);

            if (layoutInfo.SizeBytes == 0 || layoutInfo.SizeBytes > int.MaxValue)
                return null;

            byte[] outputFile = new byte[layoutInfo.SizeBytes];

            for (uint nSlice = 0; nSlice < 1; nSlice++)
            {
                for (uint nMip = 0; nMip < desc.MipLevels; nMip++)
                {
                    if (nMip >= (uint)layoutInfo.MipLayouts.Length) break;

                    MipLayoutInfo mipLayout = layoutInfo.MipLayouts[nMip];
                    if (mipLayout.SizeBytes == 0 || mipLayout.SizeBytes > int.MaxValue)
                        return null;

                    uint nDstSubResIdx = nMip + info.TileRelativeMipLevels * nSlice;
                    uint nRowPitch = mipLayout.PitchBytes;
                    byte[] outputBytes = new byte[mipLayout.SizeBytes];

                    fixed (byte* outputPtr = outputBytes)
                    fixed (byte* inputPtr = tiledData)
                    {
                        result = computer.vt->CopyFromSubresource(
                            compWrapper, outputPtr, 0u, nDstSubResIdx, inputPtr, nRowPitch, 0);

                        if (result != 0)
                            return null;

                        if ((int)mipLayout.OffsetBytes + outputBytes.Length <= outputFile.Length)
                            outputBytes.AsSpan().CopyTo(outputFile.AsSpan((int)mipLayout.OffsetBytes));
                        else
                            return null;
                    }
                }
            }

            return (layoutInfo, outputFile);
        }
        catch { return null; }
        finally { Marshal.FreeHGlobal(arrPtr); }
    }

    private unsafe ResourceLayoutInfo ReadResourceLayoutFromNative(nint ptr, uint expectedMips)
    {
        var info = new ResourceLayoutInfo();
        byte* p = (byte*)ptr;

        info.SizeBytes = *(ulong*)(p + 0);
        info.MipLevels = *(uint*)(p + 16);

        int planeOffset = 24;
        int mipArrayOffset = 40;
        byte* mipBase = p + planeOffset + mipArrayOffset;

        ulong mip0Size = *(ulong*)(mipBase + 0);
        int nativeMipStride = Marshal.SizeOf<XG_MIPLEVEL_LAYOUT>();

        if (expectedMips >= 2)
        {
            int detectedStride = DetectNativeMipLayoutStride(mipBase, mip0Size, expectedMips);
            if (detectedStride > 0)
                nativeMipStride = detectedStride;
        }

        info.MipLayouts = new MipLayoutInfo[expectedMips];
        for (uint i = 0; i < expectedMips; i++)
        {
            byte* mipPtr = mipBase + (i * nativeMipStride);
            info.MipLayouts[i].SizeBytes   = *(ulong*)(mipPtr + 0);
            info.MipLayouts[i].OffsetBytes = *(ulong*)(mipPtr + 8);
            info.MipLayouts[i].PitchBytes  = *(uint*)(mipPtr + 28);
        }

        return info;
    }

    private unsafe int DetectNativeMipLayoutStride(byte* mipBase, ulong mip0Size, uint expectedMips)
    {
        int[] candidateStrides = [72, 80, 88, 96, 104, 112, 120];

        foreach (int stride in candidateStrides)
        {
            byte* mip1Ptr = mipBase + stride;
            ulong candidateSize   = *(ulong*)(mip1Ptr + 0);
            ulong candidateOffset = *(ulong*)(mip1Ptr + 8);

            if (candidateSize > 0 && candidateSize <= mip0Size &&
                candidateOffset >= mip0Size && candidateOffset < mip0Size * 4 &&
                candidateSize < (ulong)int.MaxValue && candidateOffset < (ulong)int.MaxValue)
            {
                if (expectedMips >= 3)
                {
                    byte* mip2Ptr  = mipBase + (2 * stride);
                    ulong mip2Size   = *(ulong*)(mip2Ptr + 0);
                    ulong mip2Offset = *(ulong*)(mip2Ptr + 8);

                    if (mip2Size > 0 && mip2Size <= candidateSize &&
                        mip2Offset >= candidateOffset + candidateSize &&
                        mip2Size < (ulong)int.MaxValue && mip2Offset < (ulong)int.MaxValue)
                        return stride;
                }
                else
                {
                    return stride;
                }
            }
        }

        return -1;
    }

    private byte[] DealignDurangoTextureData(
        SwatchbinInfo info,
        XG_FORMAT format,
        (ResourceLayoutInfo Layout, byte[] Data) detiledData)
    {
        bool isBCn = DxgiUtils.IsBCnFormat((DXGI_FORMAT)format);

        uint totalSize = 0;
        uint w = (uint)info.Width;
        uint h = (uint)info.Height;

        for (int i = 0; i < info.TileRelativeMipLevels; i++)
        {
            DxgiUtils.ComputePitch((DXGI_FORMAT)format, w, h,
                out _, out ulong slicePitch, out _);
            totalSize += (uint)slicePitch;
            w >>= 1; h >>= 1;
        }

        byte[] outputData = new byte[totalSize];
        w = (uint)info.Width;
        h = (uint)info.Height;
        ulong offset = 0;

        for (int i = 0; i < info.MipLevels; i++)
        {
            DxgiUtils.ComputePitch((DXGI_FORMAT)format, w, h,
                out ulong rowPitch, out ulong slicePitch, out _);

            if (i >= detiledData.Layout.MipLayouts.Length) break;

            MipLayoutInfo mipLayout = detiledData.Layout.MipLayouts[i];

            if ((long)mipLayout.OffsetBytes + (long)mipLayout.SizeBytes > detiledData.Data.Length) break;
            if ((long)offset + (long)slicePitch > outputData.Length) break;

            Span<byte> inputMip  = detiledData.Data.AsSpan((int)mipLayout.OffsetBytes, (int)mipLayout.SizeBytes);
            Span<byte> outputMip = outputData.AsSpan((int)offset, (int)slicePitch);

            uint rowCount = isBCn ? Math.Max(1, h / 4) : h;

            for (uint y = 0; y < rowCount; y++)
            {
                int srcRowOffset = (int)(y * mipLayout.PitchBytes);
                int dstRowOffset = (int)(y * rowPitch);

                if (srcRowOffset + (int)rowPitch > inputMip.Length) break;
                if (dstRowOffset + (int)rowPitch > outputMip.Length) break;

                inputMip.Slice(srcRowOffset, (int)rowPitch)
                        .CopyTo(outputMip.Slice(dstRowOffset, (int)rowPitch));
            }

            w >>= 1; h >>= 1;
            offset += slicePitch;
        }

        return outputData;
    }

    //  DDS builder 

    internal byte[] CreateDdsFromLinearData(
        byte[] linearData, int width, int height, byte mipLevels,
        uint dxgiFormat, bool isCube, bool is3D, uint depth)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        uint linearSize = CalculateLinearSize(dxgiFormat, (uint)width, (uint)height);

        w.Write(0x20534444u);       // 'DDS '
        w.Write(124);               // dwSize
        w.Write(0x000A1007);        // dwFlags
        w.Write((uint)height);
        w.Write((uint)width);
        w.Write(linearSize);
        w.Write(depth > 1 ? depth : 1u);
        w.Write((uint)mipLevels);
        for (int i = 0; i < 11; i++) w.Write(0);

        // DDS_PIXELFORMAT
        w.Write(32);
        w.Write(0x4);
        w.Write(0x30315844u);       // 'DX10'
        w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);

        uint caps = 0x1000u;
        if (mipLevels > 1) caps |= 0x400008u;
        w.Write(caps);
        w.Write(isCube ? 0xFE00u : 0u);
        w.Write(0); w.Write(0); w.Write(0);

        // DDS_HEADER_DXT10
        w.Write(dxgiFormat);
        w.Write(is3D ? 4u : 3u);    // resourceDimension
        w.Write(isCube ? 0x4u : 0u);
        w.Write(1u);
        w.Write(0u);

        w.Write(linearData);
        return ms.ToArray();
    }

    //  Format helpers 

    internal uint CalculateLinearSize(uint dxgiFormat, uint width, uint height)
    {
        uint blockSize = GetBlockSize(dxgiFormat);
        if (blockSize > 0)
        {
            uint bw = Math.Max(1, (width + 3) / 4);
            uint bh = Math.Max(1, (height + 3) / 4);
            return bw * bh * blockSize;
        }
        uint bpp = GetBitsPerPixel(dxgiFormat);
        return (width * bpp + 7) / 8 * height;
    }

    internal uint GetBlockSize(uint dxgiFormat) => dxgiFormat switch
    {
        71 or 72 => 8,
        74 or 75 => 16,
        77 or 78 => 16,
        80 or 81 => 8,
        83 or 84 => 16,
        95 or 96 => 16,
        98 or 99 => 16,
        _ => 0
    };

    internal uint GetBitsPerPixel(uint dxgiFormat) => dxgiFormat switch
    {
        2 => 128,
        10 => 64,
        11 => 64,
        28 or 29 => 32,
        49 => 16,
        61 => 8,
        65 => 8,
        85 => 16,
        86 => 16,
        87 => 32,
        _ => 32
    };
}
