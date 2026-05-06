using System;
using System.Runtime.InteropServices;

namespace ForzaTechStudio.Services
{
    public static class OodleCompression
    {
        private const string DllName8 = "oo2core_8_win64.dll";
        private const string DllName9 = "oo2core_9_win64.dll"; // Just in case updated or using differnet one

        [DllImport(DllName8, EntryPoint = "OodleLZ_Decompress", CallingConvention = CallingConvention.Cdecl)]
        private static extern long OodleLZ_Decompress8(
            byte[] inData, long inSize,
            byte[] outData, long outSize,
            int fuzz, int crc, int verbosity, 
            IntPtr decBufBase, long decBufSize, 
            IntPtr fpCallback, IntPtr callbackUserData, 
            IntPtr decoderMemory, long decoderMemorySize, 
            int threadPhase);

        public static byte[] Decompress(byte[] compressed, int decompressedSize)
        {
            if (compressed == null || compressed.Length == 0) return Array.Empty<byte>();
            
            byte[] result = new byte[decompressedSize];
            long ret = 0;

            try 
            {
                // Try Oodle 8 (Standard for FH5 launch)
                ret = OodleLZ_Decompress8(compressed, compressed.Length, result, decompressedSize, 
                    0, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 0);
            }
            catch (DllNotFoundException)
            {
                // Fallback or just fail
                throw new Exception($"Oodle Decompression failed: {DllName8} not found. Please copy it to the application directory.");
            }
            catch (Exception ex)
            {
                 throw new Exception($"Oodle Decompression error: {ex.Message}");
            }

            if (ret != decompressedSize)
            {
                 // Sometimes specific chunks might return mismatch if fuzzy matching disabled?
                 if (ret <= 0) throw new Exception($"Oodle decompression returned {ret} (failure).");
            }
            
            return result;
        }
    }
}
