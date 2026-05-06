using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ForzaTechStudio.ViewModels;
using ForzaTools.Bundles.Blobs;

namespace ForzaTechStudio.Helpers
{
    public class ShaderParameterTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TextureTemplate { get; set; } = null!;
        public DataTemplate BoolTemplate { get; set; } = null!;
        public DataTemplate FloatTemplate { get; set; } = null!;
        public DataTemplate IntTemplate { get; set; } = null!;
        public DataTemplate VectorTemplate { get; set; } = null!;
        public DataTemplate ColorTemplate { get; set; } = null!;
        public DataTemplate SamplerTemplate { get; set; } = null!;
        public DataTemplate DefaultTemplate { get; set; } = null!;

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is ShaderParameterViewModel vm)
            {
                if (vm.IsColor) return ColorTemplate ?? VectorTemplate ?? DefaultTemplate;

                return vm.Type switch
                {
                    ShaderParameterType.Texture2D => TextureTemplate ?? DefaultTemplate,
                    ShaderParameterType.Bool => BoolTemplate ?? DefaultTemplate,
                    ShaderParameterType.Float => FloatTemplate ?? DefaultTemplate,
                    ShaderParameterType.Int => IntTemplate ?? DefaultTemplate,
                    ShaderParameterType.Vector => VectorTemplate ?? DefaultTemplate,
                    ShaderParameterType.Vector2 => VectorTemplate ?? DefaultTemplate,
                    ShaderParameterType.Color => ColorTemplate ?? VectorTemplate ?? DefaultTemplate,
                    ShaderParameterType.Sampler => SamplerTemplate ?? DefaultTemplate,
                    _ => DefaultTemplate
                };
            }
            return DefaultTemplate;
        }
    }
}
