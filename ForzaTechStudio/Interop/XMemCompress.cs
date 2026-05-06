using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ForzaTechStudio
{
    public static class XCompression
    {

        private const int LZX_CODEC = 1;

        // Decompresses XMem (LZX) data using the native DLL
        public static byte[] Decompress(byte[] compressedData, int uncompressedSize, int windowSize = 0)
        {
            if (compressedData == null || compressedData.Length == 0)
                return Array.Empty<byte>();

            IntPtr context = IntPtr.Zero;
            byte[] decompressedBuffer = new byte[uncompressedSize];
            int finalSize = uncompressedSize;

            try
            {
                // 1. Create Decompression Context
                // NOTE: codecParams should be 0 (NULL) for default LZX behavior (64KB window).

                int result = NativeMethods.XMemCreateDecompressionContext(
                    LZX_CODEC,
                    IntPtr.Zero, 
                    0,
                    out context);

                if (result != 0)
                    throw new Exception($"Failed to create decompression context. HRESULT: {result:X}");

                // 2. Perform Decompression
                result = NativeMethods.XMemDecompress(
                    context,
                    decompressedBuffer,
                    ref finalSize,
                    compressedData,
                    compressedData.Length);

                if (result != 0)
                    throw new Exception($"Decompression failed. HRESULT: {result:X}");
            }
            finally
            {
                // 3. Cleanup
                if (context != IntPtr.Zero)
                    NativeMethods.XMemDestroyDecompressionContext(context);
            }

            return decompressedBuffer;
        }


        // Lets tryyyy compresses data into XMem using native DLL.. no guarantees
        public static byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<byte>();

            IntPtr context = IntPtr.Zero;

            // Allocate a safe buffer (Size + Overhead)
            int maxCompressedSize = data.Length + (data.Length / 8) + 256;
            byte[] compressedBuffer = new byte[maxCompressedSize];
            int compressedSize = maxCompressedSize;

            try
            {
                // 1. Create Compression Context
                int result = NativeMethods.XMemCreateCompressionContext(
                    LZX_CODEC,
                    IntPtr.Zero,
                    0,
                    out context);

                if (result != 0)
                    throw new Exception($"Failed to create compression context. HRESULT: {result:X}");

                // 2. Perform Compression
                result = NativeMethods.XMemCompress(
                    context,
                    compressedBuffer,
                    ref compressedSize,
                    data,
                    data.Length);

                if (result != 0)
                    throw new Exception($"Compression failed. HRESULT: {result:X}");
            }
            finally
            {
                // 3. Cleanup
                if (context != IntPtr.Zero)
                    NativeMethods.XMemDestroyCompressionContext(context);
            }

            // 4. Resize output to actual compressed size
            byte[] finalOutput = new byte[compressedSize];
            Array.Copy(compressedBuffer, finalOutput, compressedSize);

            return finalOutput;
        }

        // P/Invoke Definitions
        private static class NativeMethods
        {
            private const string DllName = "xcompress64.dll";

            [DllImport(DllName)]
            public static extern int XMemCreateCompressionContext(
                int codecType,
                IntPtr codecParams,
                int flags,
                out IntPtr context);

            [DllImport(DllName)]
            public static extern void XMemDestroyCompressionContext(IntPtr context);

            [DllImport(DllName)]
            public static extern int XMemCompress(
                IntPtr context,
                byte[] destination,
                ref int destSize,
                byte[] source,
                int srcSize);

            [DllImport(DllName)]
            public static extern int XMemCreateDecompressionContext(
                int codecType,
                IntPtr codecParams,
                int flags,
                out IntPtr context);

            [DllImport(DllName)]
            public static extern void XMemDestroyDecompressionContext(IntPtr context);

            [DllImport(DllName)]
            public static extern int XMemDecompress(
                IntPtr context,
                byte[] destination,
                ref int destSize,
                byte[] source,
                int srcSize);
        }
    }
}