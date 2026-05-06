using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ForzaTechStudio.Views
{
    public sealed partial class AppearancePage : Page
    {
        public AppearanceViewModel ViewModel { get; } = new AppearanceViewModel();

        private bool _isLoading = true;

        public AppearancePage()
        {
            this.InitializeComponent();
            this.Loaded += AppearancePage_Loaded;
        }

        private void AppearancePage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoading = true;

            // Sync radio buttons to current ViewModel values
            SetRadioByTag(ThemeSystem,   ThemeLight,   ThemeDark,    null,         ViewModel.Theme);
            SetRadioByTag(BackdropMica,  BackdropMicaAlt, BackdropAcrylic, BackdropNone, ViewModel.BackdropMaterial);
            SetRadioByTag(NavExpanded,   NavCompact,   NavTop,       null,         ViewModel.NavigationStyle);

            BuildAccentColorSwatches();

            _isLoading = false;
        }

        private void SetRadioByTag(RadioButton r1, RadioButton r2, RadioButton r3, RadioButton? r4, string value)
        {
            foreach (var rb in new[] { r1, r2, r3, r4 })
            {
                if (rb != null && rb.Tag?.ToString() == value)
                    rb.IsChecked = true;
            }
        }

        private void BuildAccentColorSwatches()
        {
            AccentColorPanel.Children.Clear();

            foreach (var preset in AppearanceViewModel.AccentColorPresets)
            {
                var rb = new RadioButton
                {
                    Tag = preset.Key,
                    GroupName = "AccentColor",
                    Background = new SolidColorBrush(preset.PreviewColor),
                    Style = (Style)this.Resources["ColorSwatchRadioButtonStyle"],
                    IsChecked = preset.Key == ViewModel.AccentColor
                };
                ToolTipService.SetToolTip(rb, preset.DisplayName);
                rb.Checked += AccentColor_Checked;

                if (preset.Key == "System")
                {
                    rb.Background = (Brush)App.Current.Resources["AccentFillColorDefaultBrush"];
                }

                AccentColorPanel.Children.Add(rb);
            }
        }

        // Event handlers

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is RadioButton rb) ViewModel.Theme = rb.Tag?.ToString() ?? string.Empty;
        }

        private void Backdrop_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is RadioButton rb) ViewModel.BackdropMaterial = rb.Tag?.ToString() ?? string.Empty;
        }

        private void AccentColor_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is RadioButton rb) ViewModel.AccentColor = rb.Tag?.ToString() ?? string.Empty;
        }

        private void NavStyle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is RadioButton rb) ViewModel.NavigationStyle = rb.Tag?.ToString() ?? string.Empty;
        }
    }
}
