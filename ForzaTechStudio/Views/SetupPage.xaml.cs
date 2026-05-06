using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace ForzaTechStudio.Views
{
    public sealed partial class SetupPage : Page
    {
        public SetupViewModel ViewModel { get; private set; }

        public SetupPage()
        {
            try
            {
                this.InitializeComponent();
                ViewModel = new SetupViewModel();
                this.DataContext = ViewModel;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetupPage initialization error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                // Show error to user
                if (App.MainWindow?.Content?.XamlRoot != null)
                {
                    _ = ShowErrorAsync($"Failed to initialize Setup page: {ex.Message}");
                }
                throw;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // Ensure ViewModel is created if it wasn't in constructor
            if (ViewModel == null)
            {
                try
                {
                    ViewModel = new SetupViewModel();
                    this.DataContext = ViewModel;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SetupPage OnNavigatedTo error: {ex.Message}");
                }
            }
        }

        private async System.Threading.Tasks.Task ShowErrorAsync(string message)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = App.MainWindow.Content.XamlRoot,
                    Title = "Setup Error",
                    Content = message,
                    CloseButtonText = "OK"
                };
                await dialog.ShowAsync();
            }
            catch
            {
                // Ignore dialog errors
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame != null && this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
        }
    }
}
