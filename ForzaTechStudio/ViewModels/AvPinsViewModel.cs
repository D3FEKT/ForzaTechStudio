using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using System;
using System.ComponentModel;

namespace ForzaTechStudio.ViewModels
{
    public partial class AvPinsViewModel : ObservableObject
    {
        [ObservableProperty] private AvPinsData? _data;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeletePOICommand))]
        private PointOfInterest? _selectedPOI;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteViewCommand))]
        private PoiViewEntry? _selectedView;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeletePreConditionCommand))]
        private PoiCondition? _selectedPreCondition;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeletePostConditionCommand))]
        private PoiCondition? _selectedPostCondition;

        [ObservableProperty] private string? _xmlError;

        // IsPoiTypeSpace

        public bool IsPoiTypeSpace => SelectedPOI?.PoiType == "Space";

        // Subscribe/unsubscribe to SelectedPOI.PropertyChanged so IsPoiTypeSpace stays in sync
        partial void OnSelectedPOIChanged(PointOfInterest? oldValue, PointOfInterest? newValue)
        {
            if (oldValue != null) oldValue.PropertyChanged -= OnPoiPropertyChanged;
            if (newValue != null) newValue.PropertyChanged += OnPoiPropertyChanged;
            OnPropertyChanged(nameof(IsPoiTypeSpace));
            SelectedPreCondition  = null;
            SelectedPostCondition = null;
        }

        private void OnPoiPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PointOfInterest.PoiType))
                OnPropertyChanged(nameof(IsPoiTypeSpace));
        }

        // Proxy for Template (AvPinsData is a plain POCO)

        private string _template = "Default";
        public string Template
        {
            get => _template;
            set
            {
                if (SetProperty(ref _template, value) && Data != null)
                    Data.Template = value;
            }
        }

        // Load / Clear / IO

        public void Load(AvPinsData data)
        {
            Data         = data;
            Template     = data.Template;
            SelectedPOI  = null;
            SelectedView = null;
            XmlError     = null;
        }

        public void Clear()
        {
            Data         = null;
            Template     = "Default";
            SelectedPOI  = null;
            SelectedView = null;
            XmlError     = null;
        }

        public string Serialize() => Data != null ? AvPinsParser.Serialize(Data) : "";

        public string? ApplyFromText(string xmlText)
        {
            try
            {
                Load(AvPinsParser.Parse(xmlText));
                return null;
            }
            catch (Exception ex)
            {
                XmlError = $"Parse error: {ex.Message}";
                return XmlError;
            }
        }

        // Commands

        private bool IsPOISelected           => SelectedPOI           != null;
        private bool IsViewSelected          => SelectedView          != null;
        private bool IsPreConditionSelected  => SelectedPreCondition  != null;
        private bool IsPostConditionSelected => SelectedPostCondition != null;

        [RelayCommand]
        private void AddPOI()
        {
            if (Data == null) return;
            var poi = new PointOfInterest
            {
                Name       = "NewPOI",
                IconPath   = "Pin_Icon",
                IconType   = "CarInteraction",
                Visibility = new PoiVisibility(),
                Action     = new PoiAction(),
            };
            Data.POIs.Add(poi);
            SelectedPOI = poi;
        }

        [RelayCommand(CanExecute = nameof(IsPOISelected))]
        private void DeletePOI()
        {
            if (Data?.POIs == null || SelectedPOI == null) return;
            Data.POIs.Remove(SelectedPOI);
            SelectedPOI = null;
        }

        [RelayCommand]
        private void AddView()
        {
            if (Data == null) return;
            var view = new PoiViewEntry { Name = "NewView" };
            Data.Views.Add(view);
            SelectedView = view;
        }

        [RelayCommand(CanExecute = nameof(IsViewSelected))]
        private void DeleteView()
        {
            if (Data?.Views == null || SelectedView == null) return;
            Data.Views.Remove(SelectedView);
            SelectedView = null;
        }

        // Condition Commands

        [RelayCommand]
        private void AddPreCondition()
        {
            if (SelectedPOI == null) return;
            var c = new PoiCondition { State = "NewState" };
            SelectedPOI.PreConditions.Add(c);
            SelectedPreCondition = c;
        }

        [RelayCommand(CanExecute = nameof(IsPreConditionSelected))]
        private void DeletePreCondition()
        {
            if (SelectedPOI == null || SelectedPreCondition == null) return;
            SelectedPOI.PreConditions.Remove(SelectedPreCondition);
            SelectedPreCondition = null;
        }

        [RelayCommand]
        private void AddPostCondition()
        {
            if (SelectedPOI == null) return;
            var c = new PoiCondition { State = "NewState" };
            SelectedPOI.PostConditions.Add(c);
            SelectedPostCondition = c;
        }

        [RelayCommand(CanExecute = nameof(IsPostConditionSelected))]
        private void DeletePostCondition()
        {
            if (SelectedPOI == null || SelectedPostCondition == null) return;
            SelectedPOI.PostConditions.Remove(SelectedPostCondition);
            SelectedPostCondition = null;
        }
    }
}
