using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.UI;

namespace ForzaTechStudio.ViewModels
{
    public class AccentColorPreset
    {
        public string Key { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public Color PreviewColor { get; set; }
    }

    public class AppearanceViewModel : INotifyPropertyChanged
    {
        private readonly SettingsService _settingsService;
        private readonly AppearanceService _appearanceService;
        private SettingsConfig _settings;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Accent color presets
        public static IReadOnlyList<AccentColorPreset> AccentColorPresets { get; } = new List<AccentColorPreset>
        {
            new() { Key = "System",       DisplayName = "System",        PreviewColor = Color.FromArgb(255, 0,   120, 212) },
            new() { Key = "ForzaRed",     DisplayName = "Forza Red",     PreviewColor = Color.FromArgb(255, 196, 43,  28)  },
            new() { Key = "HorizonBlue",  DisplayName = "Horizon Blue",  PreviewColor = Color.FromArgb(255, 0,   120, 212) },
            new() { Key = "ElectricGreen",DisplayName = "Electric Green",PreviewColor = Color.FromArgb(255, 16,  196, 105) },
            new() { Key = "Gold",         DisplayName = "Gold",          PreviewColor = Color.FromArgb(255, 218, 165, 32)  },
            new() { Key = "Violet",       DisplayName = "Violet",        PreviewColor = Color.FromArgb(255, 136, 23,  152) },
        };

        // Properties

        private string _theme;
        public string Theme
        {
            get => _theme;
            set
            {
                if (_theme == value) return;
                _theme = value;
                OnPropertyChanged();
                _appearanceService.ApplyTheme(value);
                Save();
            }
        }

        private string _backdropMaterial;
        public string BackdropMaterial
        {
            get => _backdropMaterial;
            set
            {
                if (_backdropMaterial == value) return;
                _backdropMaterial = value;
                OnPropertyChanged();
                _appearanceService.ApplyBackdrop(value);
                Save();
            }
        }

        private string _accentColor;
        public string AccentColor
        {
            get => _accentColor;
            set
            {
                if (_accentColor == value) return;
                _accentColor = value;
                OnPropertyChanged();
                _appearanceService.ApplyAccentColor(value);
                Save();
            }
        }

        private string _navigationStyle;
        public string NavigationStyle
        {
            get => _navigationStyle;
            set
            {
                if (_navigationStyle == value) return;
                _navigationStyle = value;
                OnPropertyChanged();
                _appearanceService.ApplyNavigationStyle(value);
                Save();
            }
        }

        // Constructor

        public AppearanceViewModel()
        {
            _settingsService = new SettingsService();
            _appearanceService = AppearanceService.Instance;

            // Task.Run avoids deadlocking the UI thread's SynchronizationContext
            var settings = Task.Run(() => _settingsService.LoadAsync()).GetAwaiter().GetResult();
            _settings = settings;

            // Populate backing fields directly (no save/apply on init)
            _theme           = settings.Theme           ?? "System";
            _backdropMaterial= settings.BackdropMaterial?? "Mica";
            _accentColor     = settings.AccentColor     ?? "System";
            _navigationStyle = settings.NavigationStyle ?? "Expanded";
        }

        private void Save()
        {
            _settings.Theme           = _theme;
            _settings.BackdropMaterial= _backdropMaterial;
            _settings.AccentColor     = _accentColor;
            _settings.NavigationStyle = _navigationStyle;
            _settings.UpdateTimestamp();
            _ = _settingsService.SaveAsync(_settings);
        }
    }
}
