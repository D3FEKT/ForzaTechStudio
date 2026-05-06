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

        internal int BoneVersion { get; set; } = 2;
    }

    public class IKAnchorBonesData
    {
        public int    Version        { get; set; } = 2;
        public double HandGripAmount { get; set; } = 0.8;
        public double ReclineAmount  { get; set; }

        public ObservableCollection<IKAnchorBone> Bones { get; } = new();
        internal XDocument? SourceDoc { get; set; }
    }
}
