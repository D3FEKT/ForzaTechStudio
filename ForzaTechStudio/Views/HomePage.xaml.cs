using System;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel;
using Windows.System;

namespace ForzaTechStudio.Views
{
    public sealed partial class HomePage : Page
    {
        public MainViewModel ViewModel { get; set; }

        private static bool _welcomeShownThisSession = false;

        public HomePage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (ViewModel == null && e.Parameter is MainViewModel vm)
                ViewModel = vm;

            if (ViewModel == null)
            {
                Frame rootFrame = App.MainWindow.Content as Frame;
                if (rootFrame?.Content is ShellPage shell)
                    ViewModel = shell.ViewModel;
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Schedule dialog after the current layout pass completes
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    await ShowWelcomeDialogIfNeededAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Welcome dialog failed: {ex.Message}");
                }
            });
        }

        private async Task ShowWelcomeDialogIfNeededAsync()
        {
            var settingsService = new SettingsService();
            var settings = await settingsService.LoadAsync();

            if (settings.HasShownWelcome || _welcomeShownThisSession)
                return;

            _welcomeShownThisSession = true;

            var dialog = new WelcomeDialog();
            

            if (App.MainWindow?.Content?.XamlRoot != null)
                dialog.XamlRoot = App.MainWindow.Content.XamlRoot;
            else if (this.XamlRoot != null)
                dialog.XamlRoot = this.XamlRoot;
            else
                return;

            var result = await dialog.ShowAsync();

            if (dialog.DontShowAgain)
            {
                settings.HasShownWelcome = true;
                await settingsService.SaveAsync(settings);
            }

            if (result == ContentDialogResult.Primary)
            {
                ViewModel?.NavigateToCommand.Execute("SetupPage");
            }
        }

        // -- Tile click --
        private void Tile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string pageName)
            {
                ViewModel?.NavigateToCommand.Execute(pageName);
            }
        }

        private void Tile_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button btn)
                AnimateTile(btn, true);
        }

        private void Tile_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button btn)
                AnimateTile(btn, false);
        }

        // -- Animation: tile scale + border hover --
        private void AnimateTile(Button tile, bool active)
        {
            var visual = ElementCompositionPreview.GetElementVisual(tile);
            var compositor = visual.Compositor;

            float targetScale = active ? 1.02f : 1.0f;

            // Scale spring
            var scaleAnim = compositor.CreateSpringVector3Animation();
            scaleAnim.Target = "Scale";
            scaleAnim.FinalValue = new Vector3(targetScale, targetScale, 1.0f);
            scaleAnim.DampingRatio = 0.6f;
            scaleAnim.Period = TimeSpan.FromMilliseconds(50);

            visual.CenterPoint = new Vector3(
                (float)tile.ActualWidth / 2f,
                (float)tile.ActualHeight / 2f,
                0);

            visual.StartAnimation("Scale", scaleAnim);

            if (active)
            {
                tile.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            }
            else
            {
                tile.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
            }
        }

        // Footer buttons 
        private async void DiscordButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://discord.gg/forzamods"));
        }

        private void DocsButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.NavigateToCommand.Execute("DocumentationPage");
                return;
            }

            Frame rootFrame = App.MainWindow.Content as Frame;
            if (rootFrame?.Content is ShellPage shell)
                shell.NavigateToPage(typeof(DocumentationPage));
        }

        private void SetupButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.NavigateToCommand.Execute("SetupPage");
                return;
            }

            if (this.Frame != null)
            {
                this.Frame.Navigate(typeof(SetupPage));
                return;
            }

            Frame rootFrame = App.MainWindow.Content as Frame;
            if (rootFrame?.Content is ShellPage shell)
            {
                shell.NavigateToPage(typeof(SetupPage));
            }
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesButton.IsEnabled = false;
            try
            {
                var pkg = Package.Current;
                var v = pkg.Id.Version;
                string currentVersion = $"{v.Major}.{v.Minor}.{v.Build}";

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ForzaTechStudio");
                var json = await client.GetStringAsync("https://api.github.com/repos/D3FEKT/ForzaTechStudio/releases/latest");

                using var doc = JsonDocument.Parse(json);
                var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
                var latestVersion = tagName.TrimStart('v');

                var dialog = new ContentDialog
                {
                    Title = "Check for Updates",
                    CloseButtonText = "Close",
                    XamlRoot = App.MainWindow.Content.XamlRoot
                };

                if (string.IsNullOrEmpty(latestVersion) || latestVersion == currentVersion)
                {
                    dialog.Content = $"You're up to date! (v{currentVersion})";
                }
                else
                {
                    dialog.Content = $"Update available: v{latestVersion}\nYou have v{currentVersion}";
                    dialog.PrimaryButtonText = "Open Releases Page";
                    dialog.PrimaryButtonClick += async (s, args) =>
                    {
                        await Launcher.LaunchUriAsync(new Uri("https://github.com/D3FEKT/ForzaTechStudio/releases"));
                    };
                }

                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                var errDialog = new ContentDialog
                {
                    Title = "Check for Updates",
                    Content = "Could not reach GitHub. Please check your internet connection.",
                    CloseButtonText = "OK",
                    XamlRoot = App.MainWindow.Content.XamlRoot
                };
                await errDialog.ShowAsync();
            }
            finally
            {
                CheckUpdatesButton.IsEnabled = true;
            }
        }
    }
}
