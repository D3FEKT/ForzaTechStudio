using ForzaTechStudio.ViewModels.ThreeDViewer;
using Microsoft.UI.Xaml.Data;
using System;

namespace ForzaTechStudio.Converters
{
    public class NodeTypeIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is NodeType type)
            {
                return type switch
                {
                    NodeType.Zip => "\uF012", // ZipFolder
                    NodeType.Folder => "\uE8B7", // Folder
                    NodeType.ModelBin => "\uF2E4", // File
                    NodeType.Mesh => "\uF133", // Mesh
                    NodeType.PhysicsDefinition => "\uE9A6", // Physics
                    NodeType.LightsBin => "\uE706", // Lightbulb
                    NodeType.LightGroup => "\uEA80", // Point
                    NodeType.LightRow => "\uE913", // Dot/bullet
                    NodeType.LocatorsXml => "\uE81E",  // Map/pin icon
                    NodeType.Locator => "\uE707",       // Location pin
                    NodeType.GrannyFile => "\uE8B8",   // Animation file
                    NodeType.Skeleton => "\uE716",      // Person/skeleton
                    NodeType.Bone => "\uE734",          // Pin/bone
                    NodeType.AnimationClip => "\uE768", // Play/animation
                    NodeType.GsfInfo => "\uE71D",       // Info
                    NodeType.AvPinsFile => "\uE81E",    // Map/pin (same as LocatorsXml)
                    NodeType.AvPin => "\uEF3C",          // Location / POI pin
                    NodeType.DamageMesh => "\uE946",     // Wrench/repair icon
                    _ => "\uE71D"
                };
            }
            return "\uE71D";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
