using CommunityToolkit.Mvvm.ComponentModel;
using ForzaTechStudio.Services;
using System;
using System.Collections.ObjectModel;

namespace ForzaTechStudio.ViewModels
{
    public partial class LocatorsViewModel : ObservableObject
    {
        private LocatorsData? _rawData;
        private readonly LocatorsXmlParser      _parser = new();

        [ObservableProperty] private LocatorEntry? _selectedLocator;
        [ObservableProperty] private string?                           _xmlError;

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    RebuildFilteredList();
            }
        }

        // Filtered list shown in the ListView. Searches by locator name.
        public ObservableCollection<LocatorEntry> Locators { get; } = new();

        // Load / Clear / IO

        public void Load(LocatorsData data)
        {
            _rawData         = data;
            SelectedLocator  = null;
            XmlError         = null;
            RebuildFilteredList();
        }

        public void Clear()
        {
            _rawData         = null;
            SelectedLocator  = null;
            XmlError         = null;
            Locators.Clear();
        }

        public string Serialize()
        {
            if (_rawData == null) return "";
            return _parser.Serialize(_rawData);
        }

        public string? ApplyFromText(string xmlText)
        {
            try
            {
                Load(_parser.ParseFromString(xmlText));
                return null;
            }
            catch (Exception ex)
            {
                XmlError = $"Parse error: {ex.Message}";
                return XmlError;
            }
        }

        // Private

        private void RebuildFilteredList()
        {
            Locators.Clear();
            if (_rawData == null) return;
            var filter = SearchText?.Trim() ?? "";
            foreach (var entry in _rawData.Locators)
            {
                if (string.IsNullOrEmpty(filter) ||
                    entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    Locators.Add(entry);
                }
            }
        }
    }
}
