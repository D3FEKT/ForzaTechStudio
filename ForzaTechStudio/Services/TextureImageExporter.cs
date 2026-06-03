using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using ForzaTechStudio.Models;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ForzaTechStudio.Services
{
    // Converts a loaded swatchbin texture into the user-selected image format.
    public static class TextureImageExporter
    {
        public static string Extension(ExportTextureFormat format) => format switch
        {
            ExportTextureFormat.Png => ".png",
            ExportTextureFormat.Jpg => ".jpg",
            ExportTextureFormat.Tga => ".tga",
            _ => ".dds",
        };

        // Returns the encoded bytes for the chosen format, or null on failure.
        public static async Task<byte[]?> ConvertAsync(SwatchbinInfo info, ExportTextureFormat format)
        {
            if (info == null) return null;

            if (format == ExportTextureFormat.Dds)
                return info.DdsData;

            byte[]? rgba = null;
            await Task.Run(() =>
            {
                var linear = new SwatchbinConversionService().GetLinearTextureDataForDisplay(info);
                rgba = DecodeToRgba(info, linear);
            });

            if (rgba == null || rgba.Length == 0)
                return null;

            int w = (int)info.Width;
            int h = (int)info.Height;

            return format switch
            {
                ExportTextureFormat.Tga => EncodeTga(rgba, w, h),
                ExportTextureFormat.Jpg => await EncodeWicAsync(rgba, w, h, BitmapEncoder.JpegEncoderId, false),
                _ => await EncodeWicAsync(rgba, w, h, BitmapEncoder.PngEncoderId, true),
            };
        }

        private static byte[]? DecodeToRgba(SwatchbinInfo info, byte[] linearData)
        {
            if (linearData == null || linearData.Length == 0)
                return null;

            CompressionFormat format = GetBcnFormat(info.DxgiFormat);
            if (format == CompressionFormat.Unknown)
                return DecodeUncompressed(info, linearData);

            var decoder = new BcDecoder();
            var decoded = decoder.DecodeRaw(linearData, (int)info.Width, (int)info.Height, format);
            if (decoded == null || decoded.Length == 0)
                return null;

            byte[] result = new byte[decoded.Length * 4];
            for (int i = 0; i < decoded.Length; i++)
            {
                result[i * 4 + 0] = decoded[i].r;
                result[i * 4 + 1] = decoded[i].g;
                result[i * 4 + 2] = decoded[i].b;
                result[i * 4 + 3] = decoded[i].a;
            }
            return result;
        }

        private static byte[]? DecodeUncompressed(SwatchbinInfo info, byte[] data)
        {
            int width = (int)info.Width;
            int height = (int)info.Height;
            int expected = width * height * 4;

            if (info.DxgiFormat == 28 || info.DxgiFormat == 29) // R8G8B8A8
            {
                if (data.Length < expected) return null;
                byte[] r = new byte[expected];
                Array.Copy(data, r, expected);
                return r;
            }

            if (info.DxgiFormat == 87) // B8G8R8A8
            {
                if (data.Length < expected) return null;
                byte[] r = new byte[expected];
                for (int i = 0; i < width * height; i++)
                {
                    int idx = i * 4;
                    r[idx + 0] = data[idx + 2];
                    r[idx + 1] = data[idx + 1];
                    r[idx + 2] = data[idx + 0];
                    r[idx + 3] = data[idx + 3];
                }
                return r;
            }

            if (info.DxgiFormat == 61 || info.DxgiFormat == 65) // R8 / A8
            {
                if (data.Length < width * height) return null;
                byte[] r = new byte[expected];
                for (int i = 0; i < width * height; i++)
                {
                    byte g = data[i];
                    r[i * 4 + 0] = g;
                    r[i * 4 + 1] = g;
                    r[i * 4 + 2] = g;
                    r[i * 4 + 3] = 255;
                }
                return r;
            }

            if (info.DxgiFormat == 49) // R8G8
            {
                if (data.Length < width * height * 2) return null;
                byte[] r = new byte[expected];
                for (int i = 0; i < width * height; i++)
                {
                    r[i * 4 + 0] = data[i * 2 + 0];
                    r[i * 4 + 1] = data[i * 2 + 1];
                    r[i * 4 + 2] = 0;
                    r[i * 4 + 3] = 255;
                }
                return r;
            }

            return null;
        }

        private static CompressionFormat GetBcnFormat(uint dxgiFormat) => dxgiFormat switch
        {
            71 or 72 => CompressionFormat.Bc1,
            74 or 75 => CompressionFormat.Bc2,
            77 or 78 => CompressionFormat.Bc3,
            80 or 81 => CompressionFormat.Bc4,
            83 or 84 => CompressionFormat.Bc5,
            98 or 99 => CompressionFormat.Bc7,
            _ => CompressionFormat.Unknown,
        };

        private static async Task<byte[]?> EncodeWicAsync(byte[] rgba, int width, int height, Guid encoderId, bool keepAlpha)
        {
            using var ms = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(encoderId, ms);
            encoder.SetPixelData(
                BitmapPixelFormat.Rgba8,
                keepAlpha ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore,
                (uint)width, (uint)height, 96, 96, rgba);
            await encoder.FlushAsync();

            var bytes = new byte[ms.Size];
            using var reader = new DataReader(ms.GetInputStreamAt(0));
            await reader.LoadAsync((uint)ms.Size);
            reader.ReadBytes(bytes);
            return bytes;
        }

        // Uncompressed 32-bit BGRA TGA, top-left origin.
        private static byte[] EncodeTga(byte[] rgba, int width, int height)
        {
            byte[] header = new byte[18];
            header[2] = 2; // uncompressed true-color
            header[12] = (byte)(width & 0xFF);
            header[13] = (byte)((width >> 8) & 0xFF);
            header[14] = (byte)(height & 0xFF);
            header[15] = (byte)((height >> 8) & 0xFF);
            header[16] = 32;   // bits per pixel
            header[17] = 0x28; // 8-bit alpha + top-left origin

            byte[] result = new byte[18 + width * height * 4];
            Array.Copy(header, result, 18);
            int o = 18;
            for (int i = 0; i < width * height; i++)
            {
                int s = i * 4;
                result[o++] = rgba[s + 2]; // B
                result[o++] = rgba[s + 1]; // G
                result[o++] = rgba[s + 0]; // R
                result[o++] = rgba[s + 3]; // A
            }
            return result;
        }
    }
}
