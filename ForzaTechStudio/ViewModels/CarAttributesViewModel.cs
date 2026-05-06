using CommunityToolkit.Mvvm.ComponentModel;
using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using System;

namespace ForzaTechStudio.ViewModels
{
    public partial class CarAttributesViewModel : ObservableObject
    {
        [ObservableProperty] private CarAttributesData? _data;
        [ObservableProperty] private string?            _xmlError;

        public void Load(CarAttributesData data)
        {
            Data     = data;
            XmlError = null;
        }

        public void Clear()
        {
            Data     = null;
            XmlError = null;
        }

        public string Serialize() => Data != null ? CarAttributesParser.Serialize(Data) : "";

        public string? ApplyFromText(string xmlText)
        {
            try
            {
                Load(CarAttributesParser.Parse(xmlText));
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
