using System;
using System.Threading;
using System.Threading.Tasks;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using ForzaTechStudio.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ForzaTechStudio.Services;

public sealed class SwatchbinPreviewService
{
    private readonly SwatchbinConversionService _conversionService = new();

    public async Task<BitmapImage?> CreatePreviewImageAsync(
        SwatchbinInfo? info,
        int maxDimension = 160,
        CancellationToken cancellationToken = default)
    {
        if (info?.RawTextureData == null || info.RawTextureData.Length == 0 || info.Width == 0 || info.Height == 0)
            return null;

        byte[]? rgbaData = await Task.Run(() => DecodeTextureToRgba(info), cancellationToken);
        if (cancellationToken.IsCancellationRequested || rgbaData == null || rgbaData.Length == 0)
            return null;

        return await CreateBitmapImageFromRgbaAsync(rgbaData, (int)info.Width, (int)info.Height, maxDimension, cancellationToken);
    }

    private byte[]? DecodeTextureToRgba(SwatchbinInfo info)
    {
        byte[] linearData;

        try
        {
            linearData = _conversionService.GetLinearTextureDataForDisplay(info);
        }
        catch
        {
            return null;
        }

        if (linearData.Length == 0)
            return null;

        CompressionFormat format = GetBcnFormat(info.DxgiFormat);
        if (format == CompressionFormat.Unknown)
            return TryDecodeUncompressed(info, linearData);

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

    private static byte[]? TryDecodeUncompressed(SwatchbinInfo info, byte[] linearData)
    {
        int width = (int)info.Width;
        int height = (int)info.Height;
        int expectedSize = width * height * 4;

        if (info.DxgiFormat == 28 || info.DxgiFormat == 29)
        {
            if (linearData.Length >= expectedSize)
            {
                byte[] result = new byte[expectedSize];
                Array.Copy(linearData, result, expectedSize);
                return result;
            }
        }

        if (info.DxgiFormat == 87)
        {
            if (linearData.Length >= expectedSize)
            {
                byte[] result = new byte[expectedSize];
                for (int i = 0; i < width * height; i++)
                {
                    int index = i * 4;
                    result[index + 0] = linearData[index + 2];
                    result[index + 1] = linearData[index + 1];
                    result[index + 2] = linearData[index + 0];
                    result[index + 3] = linearData[index + 3];
                }

                return result;
            }
        }

        if (info.DxgiFormat == 61)
        {
            int grayscaleSize = width * height;
            if (linearData.Length >= grayscaleSize)
            {
                byte[] result = new byte[expectedSize];
                for (int i = 0; i < width * height; i++)
                {
                    byte gray = linearData[i];
                    result[i * 4 + 0] = gray;
                    result[i * 4 + 1] = gray;
                    result[i * 4 + 2] = gray;
                    result[i * 4 + 3] = 255;
                }

                return result;
            }
        }

        if (info.DxgiFormat == 65)
        {
            int alphaSize = width * height;
            if (linearData.Length >= alphaSize)
            {
                byte[] result = new byte[expectedSize];
                for (int i = 0; i < width * height; i++)
                {
                    byte alpha = linearData[i];
                    result[i * 4 + 0] = alpha;
                    result[i * 4 + 1] = alpha;
                    result[i * 4 + 2] = alpha;
                    result[i * 4 + 3] = 255;
                }

                return result;
            }
        }

        if (info.DxgiFormat == 49)
        {
            int rgSize = width * height * 2;
            if (linearData.Length >= rgSize)
            {
                byte[] result = new byte[expectedSize];
                for (int i = 0; i < width * height; i++)
                {
                    result[i * 4 + 0] = linearData[i * 2 + 0];
                    result[i * 4 + 1] = linearData[i * 2 + 1];
                    result[i * 4 + 2] = 0;
                    result[i * 4 + 3] = 255;
                }

                return result;
            }
        }

        return null;
    }

    private static CompressionFormat GetBcnFormat(uint dxgiFormat)
    {
        return dxgiFormat switch
        {
            71 or 72 => CompressionFormat.Bc1,
            74 or 75 => CompressionFormat.Bc2,
            77 or 78 => CompressionFormat.Bc3,
            80 or 81 => CompressionFormat.Bc4,
            83 or 84 => CompressionFormat.Bc5,
            98 or 99 => CompressionFormat.Bc7,
            _ => CompressionFormat.Unknown,
        };
    }

    private static async Task<BitmapImage?> CreateBitmapImageFromRgbaAsync(
        byte[] rgbaData,
        int width,
        int height,
        int maxDimension,
        CancellationToken cancellationToken)
    {
        if (rgbaData.Length == 0 || width <= 0 || height <= 0)
            return null;

        int expectedSize = width * height * 4;
        if (rgbaData.Length < expectedSize)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Straight,
            (uint)width,
            (uint)height,
            96,
            96,
            rgbaData);

        await encoder.FlushAsync();
        stream.Seek(0);

        cancellationToken.ThrowIfCancellationRequested();

        int decodeWidth = width;
        int decodeHeight = height;
        if (maxDimension > 0 && (width > maxDimension || height > maxDimension))
        {
            if (width >= height)
            {
                decodeWidth = maxDimension;
                decodeHeight = Math.Max(1, (int)Math.Round(height * (maxDimension / (double)width)));
            }
            else
            {
                decodeHeight = maxDimension;
                decodeWidth = Math.Max(1, (int)Math.Round(width * (maxDimension / (double)height)));
            }
        }

        var bitmapImage = new BitmapImage
        {
            DecodePixelWidth = decodeWidth,
            DecodePixelHeight = decodeHeight,
        };

        await bitmapImage.SetSourceAsync(stream);
        return bitmapImage;
    }
}