using System;
using System.IO;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ForzaTechStudio.Views
{
    public sealed partial class WelcomeDialog : ContentDialog
    {
        public bool DontShowAgain => DontShowAgainCheckBox.IsChecked == true;

        public WelcomeDialog()
        {
            this.InitializeComponent();
            LoadVersion();
        }

        private void LoadVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionBadge.Text = version != null ? $"v{version}" : "v?.?.?.?";
        }

        private async void ChangelogButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();

            var dialog = new ChangelogDialog();
            dialog.XamlRoot = this.XamlRoot;
            await dialog.ShowAsync();
        }
    }
}