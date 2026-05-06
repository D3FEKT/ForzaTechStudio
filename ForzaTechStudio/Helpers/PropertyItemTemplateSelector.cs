using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ForzaTechStudio.ViewModels;

namespace ForzaTechStudio.Helpers
{

    public class PropertyItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate BoolTemplate      { get; set; } = null!;
        public DataTemplate FloatTemplate     { get; set; } = null!;
        public DataTemplate IntTemplate       { get; set; } = null!;
        public DataTemplate UIntTemplate      { get; set; } = null!;
        public DataTemplate StringTemplate    { get; set; } = null!;
        public DataTemplate TagTemplate       { get; set; } = null!;
        public DataTemplate EnumTemplate      { get; set; } = null!;
        public DataTemplate Vector4Template   { get; set; } = null!;
        public DataTemplate Vector3Template   { get; set; } = null!;
        public DataTemplate Vector2Template   { get; set; } = null!;
        public DataTemplate HexTemplate       { get; set; } = null!;
        public DataTemplate ReadOnlyTemplate  { get; set; } = null!;

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is not PropertyItem pi) return ReadOnlyTemplate;

            return pi.DataType switch
            {
                PropertyDataType.Bool     => BoolTemplate    ?? ReadOnlyTemplate,
                PropertyDataType.Float    => FloatTemplate   ?? ReadOnlyTemplate,
                PropertyDataType.Int      => IntTemplate     ?? ReadOnlyTemplate,
                PropertyDataType.UInt     => UIntTemplate    ?? ReadOnlyTemplate,
                PropertyDataType.String   => StringTemplate  ?? ReadOnlyTemplate,
                PropertyDataType.Tag4CC   => TagTemplate     ?? ReadOnlyTemplate,
                PropertyDataType.Enum     => EnumTemplate    ?? ReadOnlyTemplate,
                PropertyDataType.Vector4  => Vector4Template ?? ReadOnlyTemplate,
                PropertyDataType.Vector3  => Vector3Template ?? ReadOnlyTemplate,
                PropertyDataType.Vector2  => Vector2Template ?? ReadOnlyTemplate,
                PropertyDataType.HexUInt  => HexTemplate     ?? ReadOnlyTemplate,
                _                         => ReadOnlyTemplate,
            };
        }
    }
}
