using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using System;

namespace ForzaTechStudio.ViewModels
{
    public partial class IKAnchorBonesViewModel : ObservableObject
    {
        [ObservableProperty] private IKAnchorBonesData? _data;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteBoneCommand))]
        private IKAnchorBone? _selectedBone;

        [ObservableProperty] private string? _xmlError;

        // Proxy properties for root floats (Data is a plain POCO)

        private double _handGripAmount = 0.8;
        public double HandGripAmount
        {
            get => _handGripAmount;
            set
            {
                if (SetProperty(ref _handGripAmount, value) && Data != null)
                    Data.HandGripAmount = value;
            }
        }

        private double _reclineAmount;
        public double ReclineAmount
        {
            get => _reclineAmount;
            set
            {
                if (SetProperty(ref _reclineAmount, value) && Data != null)
                    Data.ReclineAmount = value;
            }
        }

        private double _steeringWheelMaxDegrees;
        public double SteeringWheelMaxDegrees
        {
            get => _steeringWheelMaxDegrees;
            set
            {
                if (SetProperty(ref _steeringWheelMaxDegrees, value) && Data != null)
                    Data.SteeringWheelMaxDegrees = value;
            }
        }

        private double _shiftingFamily;
        public double ShiftingFamily
        {
            get => _shiftingFamily;
            set
            {
                if (SetProperty(ref _shiftingFamily, value) && Data != null)
                    Data.ShiftingFamily = (int)value;
            }
        }

        // Load / Clear / IO

        public void Load(IKAnchorBonesData data)
        {
            Data                   = data;
            HandGripAmount         = data.HandGripAmount;
            ReclineAmount          = data.ReclineAmount;
            SteeringWheelMaxDegrees = data.SteeringWheelMaxDegrees;
            ShiftingFamily         = data.ShiftingFamily;
            SelectedBone           = null;
            XmlError               = null;
        }

        public void Clear()
        {
            Data                   = null;
            HandGripAmount         = 0.8;
            ReclineAmount          = 0;
            SteeringWheelMaxDegrees = 0;
            ShiftingFamily         = 0;
            SelectedBone           = null;
            XmlError               = null;
        }

        public string Serialize() => Data != null ? IKAnchorBonesParser.Serialize(Data) : "";

        public string? ApplyFromText(string xmlText)
        {
            try
            {
                Load(IKAnchorBonesParser.Parse(xmlText));
                return null;
            }
            catch (Exception ex)
            {
                XmlError = $"Parse error: {ex.Message}";
                return XmlError;
            }
        }

        // Commands

        private bool IsBoneSelected => SelectedBone != null;

        [RelayCommand]
        private void AddBone()
        {
            if (Data == null) return;
            var bone = new IKAnchorBone { Name = "new_bone", BoneVersion = Data.Version };
            Data.Bones.Add(bone);
            SelectedBone = bone;
        }

        [RelayCommand(CanExecute = nameof(IsBoneSelected))]
        private void DeleteBone()
        {
            if (Data == null || SelectedBone == null) return;
            Data.Bones.Remove(SelectedBone);
            SelectedBone = null;
        }
    }
}
