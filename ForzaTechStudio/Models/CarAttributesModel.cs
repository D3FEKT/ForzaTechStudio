using CommunityToolkit.Mvvm.ComponentModel;

namespace ForzaTechStudio.Models
{
    public partial class WindshieldReflectionSettings : ObservableObject
    {
        [ObservableProperty] private int    _version      = 1;
        [ObservableProperty] private double _eyeOffset;
        [ObservableProperty] private double _angleOffset;
        [ObservableProperty] private double _shearAmount;
        [ObservableProperty] private double _textureOffsetX;
        [ObservableProperty] private double _textureOffsetY;
        [ObservableProperty] private double _textureScaleX = 1.0;
        [ObservableProperty] private double _textureScaleY = 1.0;
        // v2+ only
        [ObservableProperty] private double _fadeStart;
        [ObservableProperty] private double _fadeEnd   = 1.0;
        [ObservableProperty] private double _maxAmount = 1.0;
    }

    public class CarAttributesData
    {
        public int                          RootVersion         { get; set; }
        public WindshieldReflectionSettings WindshieldReflection { get; } = new();
    }
}
