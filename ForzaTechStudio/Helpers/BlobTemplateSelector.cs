using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ForzaTechStudio.ViewModels;
using ForzaTools.Bundles.Blobs;
using ForzaTools.Bundles;

namespace ForzaTechStudio.Helpers
{
    public class BlobTemplateSelector : DataTemplateSelector
    {
        public DataTemplate DefaultTemplate                  { get; set; } = null!;
        public DataTemplate MeshTemplate                     { get; set; } = null!;
        public DataTemplate SkeletonTemplate                 { get; set; } = null!;
        public DataTemplate MaterialTemplate                 { get; set; } = null!;
        public DataTemplate MaterialShaderParameterTemplate  { get; set; } = null!;
        public DataTemplate ModelTemplate                    { get; set; } = null!;
        public DataTemplate VertexLayoutTemplate             { get; set; } = null!;
        public DataTemplate BufferTemplate                   { get; set; } = null!;
        public DataTemplate BoneNodeTemplate                 { get; set; } = null!;

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is ObjectNode node && node.Data != null)
            {
                var blob = node.Data;

                if (blob is Bone)              return BoneNodeTemplate      ?? DefaultTemplate;
                if (blob is MeshBlob)          return MeshTemplate          ?? DefaultTemplate;
                if (blob is SkeletonBlob)      return SkeletonTemplate      ?? DefaultTemplate;
                if (blob is ModelBlob)         return ModelTemplate         ?? DefaultTemplate;
                if (blob is VertexLayoutBlob)  return VertexLayoutTemplate  ?? DefaultTemplate;

                // Buffer blobs — header-only display
                if (blob is IndexBufferBlob || blob is VertexBufferBlob ||
                    blob is MorphBufferBlob  || blob is SkinBufferBlob)
                    return BufferTemplate ?? DefaultTemplate;

                // Material types
                if (blob is MaterialBlob)         return MaterialTemplate ?? DefaultTemplate;
                if (blob is MaterialResourceBlob) return MaterialTemplate ?? DefaultTemplate;

                // Check for MaterialShaderParameterBlob by type first
                if (blob is MaterialShaderParameterBlob) return MaterialShaderParameterTemplate ?? DefaultTemplate;

                // Fallback: check by Tag property
                if (blob is BundleBlob bundleBlob)
                {
                    if (bundleBlob.Tag == Bundle.TAG_BLOB_MaterialShaderParameter ||
                        bundleBlob.Tag == Bundle.TAG_BLOB_DefaultShaderParameter)
                        return MaterialShaderParameterTemplate ?? DefaultTemplate;
                }
            }
            return DefaultTemplate;
        }
    }
}
