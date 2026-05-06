using System;
using System.Runtime.InteropServices;
using DurangoTypes;
using ForzaTechStudio.Models;
using ForzaTools.Shared;

namespace ForzaTechStudio.Views;

// Lightweight mip layout info read directly from native memory,
// avoiding Marshal.PtrToStructure alignment issues with XG_MIPLEVEL_LAYOUT arrays.
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

public sealed partial class SwatchbinEditorPage
{
    // Detiles Xbox/Durango tiled texture data using the XG library.
    // Ported from ForzaTools.TexConv.Program.DurangoDetile
    private unsafe (ResourceLayoutInfo Layout, byte[] Data)? DurangoDetile(SwatchbinInfo info, byte[] tiledData)
    {
        if (info == null || !info.IsDurangoFormat || tiledData == null || tiledData.Length == 0)
        {
            System.Diagnostics.Debug.WriteLine("ERROR: Invalid input parameters for DurangoDetile");
            return null;
        }

        XG_FORMAT format = (XG_FORMAT)info.DxgiFormat;

        System.Diagnostics.Debug.WriteLine($"Detiling Xbox texture: {info.Width}x{info.Height}, Format: {format}, Mips: {info.MipLevels}, TileMode: {info.TileMode}");

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
        if (result != 0)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR: Failed to XGCreateTexture2DComputer (0x{result:X8})");
            return null;
        }

        if (compWrapper == null)
        {
            System.Diagnostics.Debug.WriteLine("ERROR: XGCreateTexture2DComputer returned null computer");
            return null;
        }

        XGTextureAddressComputer computer = *compWrapper;

        // Allocate a generous buffer for GetResourceLayout to avoid any buffer overrun.
        // The native XG_RESOURCE_LAYOUT struct may be larger than our C# definition due to
        // alignment/packing differences in nested XG_MIPLEVEL_LAYOUT arrays.
        const int layoutBufferSize = 128 * 1024; // 128 KB - far more than needed
        nint arrPtr = Marshal.AllocHGlobal(layoutBufferSize);

        // Zero the buffer to ensure clean reads
        new Span<byte>((void*)arrPtr, layoutBufferSize).Clear();

        result = computer.vt->GetResourceLayout(compWrapper, (XG_RESOURCE_LAYOUT*)arrPtr);
        if (result != 0)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR: Failed to GetResourceLayout (0x{result:X8})");
            Marshal.FreeHGlobal(arrPtr);
            return null;
        }

        try
        {
            // Read XG_RESOURCE_LAYOUT fields via direct pointer arithmetic to avoid Marshal.PtrToStructure alignment issues with nested mip arrays.
            ResourceLayoutInfo layoutInfo = ReadResourceLayoutFromNative(arrPtr, desc.MipLevels);

            System.Diagnostics.Debug.WriteLine($"Resource layout: Size={layoutInfo.SizeBytes}, Mips={layoutInfo.MipLevels}");

            if (layoutInfo.SizeBytes == 0 || layoutInfo.SizeBytes > int.MaxValue)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: Invalid layout size: {layoutInfo.SizeBytes}");
                return null;
            }

            byte[] outputFile = new byte[layoutInfo.SizeBytes];

            for (uint nSlice = 0; nSlice < 1; nSlice++)
            {
                for (uint nMip = 0; nMip < desc.MipLevels; nMip++)
                {
                    if (nMip >= (uint)layoutInfo.MipLayouts.Length)
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR: Mip index {nMip} out of range (max: {layoutInfo.MipLayouts.Length})");
                        break;
                    }

                    MipLayoutInfo mipLayout = layoutInfo.MipLayouts[nMip];

                    if (mipLayout.SizeBytes == 0 || mipLayout.SizeBytes > int.MaxValue)
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR: Invalid mip {nMip} size: {mipLayout.SizeBytes}");
                        return null;
                    }

                    ulong mipSizeBytes = mipLayout.SizeBytes;
                    ulong mipOffset = mipLayout.OffsetBytes;

                    uint nDstSubResIdx = nMip + info.TileRelativeMipLevels * nSlice;
                    uint nRowPitch = mipLayout.PitchBytes;

                    System.Diagnostics.Debug.WriteLine($"  Mip {nMip}: Size={mipSizeBytes}, Offset={mipOffset}, SubResIdx={nDstSubResIdx}, RowPitch={nRowPitch}");

                    byte[] outputBytes = new byte[mipSizeBytes];

                    fixed (byte* outputPtr = outputBytes)
                    fixed (byte* inputPtr = tiledData)
                    {
                        result = computer.vt->CopyFromSubresource(compWrapper, outputPtr, 0u, nDstSubResIdx, inputPtr, nRowPitch, 0);
                        if (result != 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"ERROR: Failed to CopyFromSubresource at mip {nMip} (0x{result:X8})");
                            return null;
                        }

                        if ((int)mipOffset + outputBytes.Length <= outputFile.Length)
                        {
                            outputBytes.AsSpan().CopyTo(outputFile.AsSpan((int)mipOffset));
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"ERROR: Mip {nMip} would overflow output buffer");
                            return null;
                        }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine("Detiling completed successfully");
            return (layoutInfo, outputFile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR in DurangoDetile: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        finally
        {
            Marshal.FreeHGlobal(arrPtr);
        }

        return null;
    }

    // Reads XG_RESOURCE_LAYOUT and mip layouts directly from native memory via unsafe pointer arithmetic (avoids Marshal.PtrToStructure alignment issues with nested fixed-size arrays).
    private unsafe ResourceLayoutInfo ReadResourceLayoutFromNative(nint ptr, uint expectedMips)
    {
        var info = new ResourceLayoutInfo();

        // XG_RESOURCE_LAYOUT fields: [0] SizeBytes, [8] BaseAlignmentBytes, [16] MipLevels, [20] Planes, [24] XG_PLANE_LAYOUT[4] Plane.
        byte* p = (byte*)ptr;

        info.SizeBytes = *(ulong*)(p + 0);
        info.MipLevels = *(uint*)(p + 16);

        System.Diagnostics.Debug.WriteLine($"Native layout read: SizeBytes={info.SizeBytes}, MipLevels={info.MipLevels}");

        // Probe native XG_MIPLEVEL_LAYOUT stride: XG_PLANE_LAYOUT header is 40 bytes (Usage+pad+SizeBytes+BaseOffsetBytes+BaseAlignmentBytes+BytesPerElement+pad), so Plane[0].MipLayout starts at offset 64 in XG_RESOURCE_LAYOUT.

        int planeOffset = 24;      // Plane[0] starts here within XG_RESOURCE_LAYOUT
        int mipArrayOffset = 40;   // MipLayout[0] starts here within XG_PLANE_LAYOUT
        byte* mipBase = p + planeOffset + mipArrayOffset;

        // Read mip 0 from the known correct location
        ulong mip0Size = *(ulong*)(mipBase + 0);
        ulong mip0Offset = *(ulong*)(mipBase + 8);

        System.Diagnostics.Debug.WriteLine($"Mip 0 probe: Size={mip0Size}, Offset={mip0Offset}");

        // Detect native XG_MIPLEVEL_LAYOUT stride (C# Marshal.SizeOf may differ): probe successive mip offsets until mip 1's SizeBytes and OffsetBytes look valid relative to mip 0.
        int nativeMipStride = Marshal.SizeOf<XG_MIPLEVEL_LAYOUT>();
        System.Diagnostics.Debug.WriteLine($"C# Marshal.SizeOf<XG_MIPLEVEL_LAYOUT> = {nativeMipStride}");

        if (expectedMips >= 2)
        {
            // Try to find the correct stride by probing at different offsets
            int detectedStride = DetectNativeMipLayoutStride(mipBase, mip0Size, expectedMips);
            if (detectedStride > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Detected native MipLayout stride: {detectedStride} bytes (C# expected: {nativeMipStride})");
                nativeMipStride = detectedStride;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Could not detect native stride, using C# default: {nativeMipStride}");
            }
        }

        // Read all mip layouts using the determined stride
        info.MipLayouts = new MipLayoutInfo[expectedMips];
        for (uint i = 0; i < expectedMips; i++)
        {
            byte* mipPtr = mipBase + (i * nativeMipStride);

            // XG_MIPLEVEL_LAYOUT fields: [0] SizeBytes, [8] OffsetBytes, [16] Slice2DSizeBytes, [24] PitchPixels, [28] PitchBytes.
            info.MipLayouts[i].SizeBytes = *(ulong*)(mipPtr + 0);
            info.MipLayouts[i].OffsetBytes = *(ulong*)(mipPtr + 8);
            info.MipLayouts[i].PitchBytes = *(uint*)(mipPtr + 28); // PitchBytes is at offset 28

            System.Diagnostics.Debug.WriteLine($"  Native Mip {i}: Size={info.MipLayouts[i].SizeBytes}, Offset={info.MipLayouts[i].OffsetBytes}, PitchBytes={info.MipLayouts[i].PitchBytes}");
        }

        return info;
    }

    // Detects the actual native XG_MIPLEVEL_LAYOUT element stride by probing the memory
    // at different offsets to find where mip 1's data starts.
    private unsafe int DetectNativeMipLayoutStride(byte* mipBase, ulong mip0Size, uint expectedMips)
    {
        // Candidate strides to probe (72–120 bytes, covering known XG SDK variants with/without BankRotation, SliceDepthElements, and extra padding).
        int[] candidateStrides = [72, 80, 88, 96, 104, 112, 120];

        foreach (int stride in candidateStrides)
        {
            byte* mip1Ptr = mipBase + stride;

            // Read what would be SizeBytes and OffsetBytes for mip 1 at this stride
            ulong candidateSize = *(ulong*)(mip1Ptr + 0);
            ulong candidateOffset = *(ulong*)(mip1Ptr + 8);

            // Valid mip 1 candidate: SizeBytes <= mip0 and > 0, OffsetBytes >= mip0 and < mip0*4, both within int range.
            if (candidateSize > 0 && candidateSize <= mip0Size &&
                candidateOffset >= mip0Size && candidateOffset < mip0Size * 4 &&
                candidateSize < (ulong)int.MaxValue && candidateOffset < (ulong)int.MaxValue)
            {
                // Additional validation if we have 3+ mips: check mip 2 as well
                if (expectedMips >= 3)
                {
                    byte* mip2Ptr = mipBase + (2 * stride);
                    ulong mip2Size = *(ulong*)(mip2Ptr + 0);
                    ulong mip2Offset = *(ulong*)(mip2Ptr + 8);

                    if (mip2Size > 0 && mip2Size <= candidateSize &&
                        mip2Offset >= candidateOffset + candidateSize &&
                        mip2Size < (ulong)int.MaxValue && mip2Offset < (ulong)int.MaxValue)
                    {
                        System.Diagnostics.Debug.WriteLine($"  Stride {stride}: Mip1 Size={candidateSize}, Offset={candidateOffset}, Mip2 Size={mip2Size}, Offset={mip2Offset} -> MATCH");
                        return stride;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  Stride {stride}: Mip1 Size={candidateSize}, Offset={candidateOffset} -> MATCH (2 mips only)");
                    return stride;
                }
            }
        }

        return -1; // Could not determine
    }

    private byte[] DealignDurangoTextureData(
        SwatchbinInfo info,
        XG_FORMAT format,
        (ResourceLayoutInfo Layout, byte[] Data)? detiledData)
    {
        if (detiledData == null || !detiledData.HasValue)
            throw new ArgumentNullException(nameof(detiledData));

        bool isBCn = DxgiUtils.IsBCnFormat((DXGI_FORMAT)format);

        // Calculate total size for the output based on TileRelativeMipLevels
        uint totalSize = 0;
        uint w = (uint)info.Width;
        uint h = (uint)info.Height;
        
        System.Diagnostics.Debug.WriteLine($"DealignDurangoTextureData: Processing {info.TileRelativeMipLevels} mip levels, NumMips={info.MipLevels}");
        
        for (int i = 0; i < info.TileRelativeMipLevels; i++)
        {
            DxgiUtils.ComputePitch((DXGI_FORMAT)format, w, h, out ulong rowPitch, out ulong slicePitch, out ulong alignedSlicePitch);
            totalSize += (uint)slicePitch;

            w >>= 1;
            h >>= 1;
        }

        System.Diagnostics.Debug.WriteLine($"Total output size: {totalSize} bytes");
        byte[] outputData = new byte[totalSize];
        w = (uint)info.Width;
        h = (uint)info.Height;
        ulong offset = 0;
        
        // Match TexConv: iterate NumMips (info.MipLevels) for the actual data processing
        for (int i = 0; i < info.MipLevels; i++)
        {
            DxgiUtils.ComputePitch((DXGI_FORMAT)format, w, h, out ulong rowPitch, out ulong slicePitch, out ulong alignedSlicePitch);

            if (i >= detiledData.Value.Layout.MipLayouts.Length)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: Mip {i} not present in layout");
                break;
            }

            MipLayoutInfo mipLayout = detiledData.Value.Layout.MipLayouts[i];

            // Validate indices
            if ((long)mipLayout.OffsetBytes + (long)mipLayout.SizeBytes > detiledData.Value.Data.Length)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: Mip {i} exceeds detiled data bounds (offset={mipLayout.OffsetBytes}, size={mipLayout.SizeBytes}, dataLen={detiledData.Value.Data.Length})");
                break;
            }

            if ((long)offset + (long)slicePitch > outputData.Length)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: Mip {i} would exceed output bounds (offset={offset}, slicePitch={slicePitch}, outputLen={outputData.Length})");
                break;
            }

            Span<byte> inputMip = detiledData.Value.Data.AsSpan((int)mipLayout.OffsetBytes, (int)mipLayout.SizeBytes);
            Span<byte> outputMip = outputData.AsSpan((int)offset, (int)slicePitch);

            // For BCn formats, height is in blocks (height / 4), but must be at least 1
            uint rowCount = isBCn ? Math.Max(1, h / 4) : h;

            System.Diagnostics.Debug.WriteLine($"  Mip {i}: {w}x{h}, RowPitch={rowPitch}, SlicePitch={slicePitch}, RowCount={rowCount}, MipPitchBytes={mipLayout.PitchBytes}");

            for (uint y = 0; y < rowCount; y++)
            {
                int srcRowOffset = (int)(y * mipLayout.PitchBytes);
                if (srcRowOffset + (int)rowPitch > inputMip.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"  WARNING: Row {y} of mip {i} exceeds input bounds, stopping row copy");
                    break;
                }

                int dstRowOffset = (int)(y * rowPitch);
                if (dstRowOffset + (int)rowPitch > outputMip.Length)
                {
                    System.Diagnostics.Debug.WriteLine($"  WARNING: Row {y} of mip {i} exceeds output bounds, stopping row copy");
                    break;
                }

                Span<byte> row = inputMip.Slice(srcRowOffset, (int)rowPitch);
                Span<byte> outputRow = outputMip.Slice(dstRowOffset, (int)rowPitch);
                row.CopyTo(outputRow);
            }

            w >>= 1;
            h >>= 1;
            offset += slicePitch;
        }

        System.Diagnostics.Debug.WriteLine($"Dealignment completed, output size: {outputData.Length} bytes");
        return outputData;
    }
}
