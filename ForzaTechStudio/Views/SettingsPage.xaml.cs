using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Reflection;

namespace ForzaTechStudio.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            LoadVersion();
        }

        private void LoadVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"Version Number: {version.Major}.{version.Minor}.{version.Build}";
        }

        private void NavigateToAppearance_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame != null)
            {
                this.Frame.Navigate(typeof(AppearancePage));
            }
        }

        private void NavigateToSetup_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to Setup page - need to find the ShellPage's ContentFrame
            // SettingsPage is inside ContentFrame, so we need to navigate using that Frame
            if (this.Frame != null)
            {
                this.Frame.Navigate(typeof(SetupPage));
            }
            else
            {
                // Fallback: Try to find ShellPage
                Frame rootFrame = App.MainWindow.Content as Frame;
                if (rootFrame?.Content is ShellPage shell)
                {
                    shell.NavigateToPage(typeof(SetupPage));
                }
            }
        }
    }
}
