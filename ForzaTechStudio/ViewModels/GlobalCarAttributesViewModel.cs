using CommunityToolkit.Mvvm.ComponentModel;
using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using System;
using System.Linq;

namespace ForzaTechStudio.ViewModels
{
    public partial class GlobalCarAttributesViewModel : ObservableObject
    {
        [ObservableProperty] private GlobalCarAttributesData?    _data;
        [ObservableProperty] private GlobalCarAttributeSection?  _selectedSection;
        [ObservableProperty] private string?                      _xmlError;

        public bool IsSectionSelected => SelectedSection != null;

        public void Load(GlobalCarAttributesData data)
        {
            Data            = data;
            SelectedSection = data.Sections.FirstOrDefault();
            XmlError        = null;
        }

        public void Clear()
        {
            Data            = null;
            SelectedSection = null;
            XmlError        = null;
        }

        public string Serialize() => Data != null ? GlobalCarAttributesParser.Serialize(Data) : "";

        public string? ApplyFromText(string xmlText)
        {
            try
            {
                Load(GlobalCarAttributesParser.Parse(xmlText));
                return null;
            }
            catch (Exception ex)
            {
                XmlError = $"Parse error: {ex.Message}";
                return XmlError;
            }
        }
    }
}
