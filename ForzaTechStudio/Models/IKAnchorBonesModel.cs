using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Xml.Linq;

namespace ForzaTechStudio.Models
{
    public partial class IKAnchorBone : ObservableObject
    {
        [ObservableProperty] private string _name    = "";
        [ObservableProperty] private double _offsetX;
        [ObservableProperty] private double _offsetY;
        [ObservableProperty] private double _offsetZ;
        [ObservableProperty] private double _rotX;
        [ObservableProperty] private double _rotY;
        [ObservableProperty] private double _rotZ;
        [ObservableProperty] private double _scaleX;
        [ObservableProperty] private double _scaleY;
        [ObservableProperty] private double _scaleZ;

        internal int BoneVersion { get; set; } = 103;
    }

    public class IKAnchorBonesData
    {
        public int    Version                 { get; set; } = 103;
        public int    ShiftingFamily          { get; set; }        // 0 = absent; written only when > 0
        public double SteeringWheelMaxDegrees { get; set; }
        public double HandGripAmount          { get; set; } = 0.8;
        public double ReclineAmount           { get; set; }

        public ObservableCollection<IKAnchorBone> Bones { get; } = new();
        internal XDocument? SourceDoc { get; set; }
    }
}
