using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Xml.Linq;

namespace ForzaTechStudio.Models
{
    public partial class SectionAttribute : ObservableObject
    {
        public string Key { get; init; } = "";
        [ObservableProperty] private string _value = "";
    }

    public partial class GlobalCarAttributeSection : ObservableObject
    {
        [ObservableProperty] private string _displayName = "";

        [ObservableProperty] private string _sectionVersion = "";

        public ObservableCollection<SectionAttribute> Attributes { get; } = new();

        public bool HasChildElements { get; set; }

        internal XElement? RawElement { get; set; }
    }

    public class GlobalCarAttributesData
    {
        public int                                      RootVersion { get; set; }
        public ObservableCollection<GlobalCarAttributeSection> Sections { get; } = new();
        internal XDocument? SourceDoc { get; set; }
    }
}
