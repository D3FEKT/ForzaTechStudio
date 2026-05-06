using ForzaTechStudio.Models;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace ForzaTechStudio.Services
{
    public class AppearanceService
    {
        private static AppearanceService? _instance;
        public static AppearanceService Instance => _instance ??= new AppearanceService();

        private static readonly Dictionary<string, (Color Default, Color Dark1, Color Dark2, Color Dark3, Color Light1, Color Light2, Color Light3)> AccentPresets = new()
        {
            ["ForzaRed"]     = (Color.FromArgb(255, 196, 43,  28),  Color.FromArgb(255, 164, 20,  8),   Color.FromArgb(255, 130, 10,  2),   Color.FromArgb(255, 100, 5,   1),   Color.FromArgb(255, 218, 80,  68),  Color.FromArgb(255, 234, 130, 122), Color.FromArgb(255, 246, 178, 173)),
            ["HorizonBlue"]  = (Color.FromArgb(255, 0,   120, 212), Color.FromArgb(255, 0,   99,  177), Color.FromArgb(255, 0,   78,  140), Color.FromArgb(255, 0,   58,  105), Color.FromArgb(255, 40,  153, 232), Color.FromArgb(255, 96,  185, 241), Color.FromArgb(255, 162, 216, 248)),
            ["ElectricGreen"]= (Color.FromArgb(255, 16,  196, 105), Color.FromArgb(255, 8,   162, 84),  Color.FromArgb(255, 3,   128, 65),  Color.FromArgb(255, 0,   96,  48),  Color.FromArgb(255, 52,  220, 130), Color.FromArgb(255, 100, 235, 161), Color.FromArgb(255, 158, 246, 197)),
            ["Gold"]         = (Color.FromArgb(255, 218, 165, 32),  Color.FromArgb(255, 184, 135, 14),  Color.FromArgb(255, 148, 106, 5),   Color.FromArgb(255, 112, 79,  0),   Color.FromArgb(255, 235, 188, 72),  Color.FromArgb(255, 244, 210, 120), Color.FromArgb(255, 251, 230, 172)),
            ["Violet"]       = (Color.FromArgb(255, 136, 23,  152), Color.FromArgb(255, 109, 12,  124), Color.FromArgb(255, 84,  5,   97),  Color.FromArgb(255, 62,  1,   72),  Color.FromArgb(255, 164, 58,  180), Color.FromArgb(255, 194, 102, 210), Color.FromArgb(255, 222, 155, 234)),
        };

        private Color? _systemAccentColor;

        private AppearanceService() { }

        public void Apply(SettingsConfig settings)
        {
            ApplyTheme(settings.Theme);
            ApplyBackdrop(settings.BackdropMaterial);
            ApplyAccentColor(settings.AccentColor);
        }

        public void ApplyTheme(string theme)
        {
            if (App.MainWindow?.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme switch
                {
                    "Light"  => ElementTheme.Light,
                    "Dark"   => ElementTheme.Dark,
                    _        => ElementTheme.Default,
                };
            }
        }

        public void ApplyBackdrop(string material)
        {
            if (App.MainWindow == null) return;

            App.MainWindow.SystemBackdrop = material switch
            {
                "MicaAlt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                "Acrylic" => new DesktopAcrylicBackdrop(),
                "None"    => null,
                _         => new MicaBackdrop { Kind = MicaKind.Base },
            };
        }

        public void ApplyAccentColor(string colorKey)
        {
            var resources = App.Current.Resources;

            if (colorKey == "System" || !AccentPresets.ContainsKey(colorKey))
            {
                if (_systemAccentColor.HasValue)
                {
                    var uiSettings = new UISettings();
                    var sysAccent = uiSettings.GetColorValue(UIColorType.Accent);
                    RestoreAccentFromColor(sysAccent);
                }
                return;
            }

            if (!_systemAccentColor.HasValue)
            {
                var uiSettings = new UISettings();
                _systemAccentColor = uiSettings.GetColorValue(UIColorType.Accent);
            }

            var preset = AccentPresets[colorKey];

            resources["SystemAccentColor"]       = preset.Default;
            resources["SystemAccentColorDark1"]  = preset.Dark1;
            resources["SystemAccentColorDark2"]  = preset.Dark2;
            resources["SystemAccentColorDark3"]  = preset.Dark3;
            resources["SystemAccentColorLight1"] = preset.Light1;
            resources["SystemAccentColorLight2"] = preset.Light2;
            resources["SystemAccentColorLight3"] = preset.Light3;

            resources["SystemControlHighlightAccentBrush"]         = new SolidColorBrush(preset.Default);
            resources["SystemControlHighlightAltHighAccentBrush"]  = new SolidColorBrush(preset.Default);
            resources["AccentFillColorDefaultBrush"]               = new SolidColorBrush(preset.Default);
            resources["AccentFillColorSecondaryBrush"]             = new SolidColorBrush(preset.Dark1);
            resources["AccentFillColorTertiaryBrush"]              = new SolidColorBrush(preset.Dark2);
        }

        private void RestoreAccentFromColor(Color accent)
        {
            var resources = App.Current.Resources;
            resources["SystemAccentColor"]       = accent;
            resources["AccentFillColorDefaultBrush"]               = new SolidColorBrush(accent);
        }

        public void ApplyNavigationStyle(string style)
        {
            var shell = ForzaTechStudio.Views.ShellPage.Current;
            shell?.ApplyNavigationStyle(style);
        }
    }
}
