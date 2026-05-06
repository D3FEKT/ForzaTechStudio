using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using ForzaTechStudio.ViewModels;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using System;

namespace ForzaTechStudio.Converters
{
    // Converts the <see cref="ObjectNode.Data"/> object to a Segoe MDL2 glyph string
    // for display in the tree view item.
    public class BlobTypeIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value switch
            {
                SkeletonBlob          => "\uE8A4",   // Person/skeleton
                MeshBlob              => "\uF158",   // 3D cube shape
                MaterialBlob          => "\uE771",   // Paint bucket
                MaterialResourceBlob  => "\uE771",
                MaterialShaderParameterBlob => "\uE790",  // Sparkle/shader
                ModelBlob             => "\uE81F",   // View model indicator
                VertexLayoutBlob      => "\uEA5C",   // Grid/layout 
                IndexBufferBlob       => "\uE838",   // Data/index buffer
                VertexBufferBlob      => "\uE838",
                MorphBufferBlob       => "\uE8AB",   // Transform/morph
                SkinBufferBlob        => "\uE7FD",   // Skin
                LightScenarioBlob     => "\uE781",   // Brightness/light
                MatLBlob              => "\uE771",
                MorphBlob             => "\uE8AB",
                ManufacturerColorsBlob => "\uE790",
                Bundle                => "\uE8B7",   // Folder/container (used for nested bundles)
                null                  => "\uE9D9",   // Generic data
                _                     => "\uE9D9",
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    // Converts the <see cref="ObjectNode.Data"/> object to a foreground color brush
    // based on blob category.
    public class BlobTypeColorConverter : IValueConverter
    {
        // Geometry: accent blue
        private static readonly SolidColorBrush Geometry   = new(Windows.UI.Color.FromArgb(255, 96,  165, 250));
        // Materials: green
        private static readonly SolidColorBrush Material   = new(Windows.UI.Color.FromArgb(255, 52,  211, 153));
        // Skeleton: orange
        private static readonly SolidColorBrush Skeleton   = new(Windows.UI.Color.FromArgb(255, 251, 146,  60));
        // Buffers: gray
        private static readonly SolidColorBrush Buffer     = new(Windows.UI.Color.FromArgb(255, 148, 163, 184));
        // Lighting: yellow
        private static readonly SolidColorBrush Light      = new(Windows.UI.Color.FromArgb(255, 250, 204,  21));
        // Default: muted
        private static readonly SolidColorBrush DefaultBrush = new(Windows.UI.Color.FromArgb(180, 148, 163, 184));

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value switch
            {
                MeshBlob              => Geometry,
                ModelBlob             => Geometry,
                VertexLayoutBlob      => Geometry,
                SkeletonBlob          => Skeleton,
                MaterialBlob          => Material,
                MaterialResourceBlob  => Material,
                MaterialShaderParameterBlob => Material,
                MatLBlob              => Material,
                IndexBufferBlob       => Buffer,
                VertexBufferBlob      => Buffer,
                MorphBufferBlob       => Buffer,
                SkinBufferBlob        => Buffer,
                MorphBlob             => Buffer,
                LightScenarioBlob     => Light,
                ManufacturerColorsBlob => Material,
                _                     => DefaultBrush,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
