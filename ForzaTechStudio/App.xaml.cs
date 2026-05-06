using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ForzaTechStudio.Services;

namespace ForzaTechStudio
{
    public partial class App : Application
    {

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int MessageBox(IntPtr hWnd, String text, String caption, int options);

        public static Window MainWindow { get; private set; } = null!;
        public static AppearanceService AppearanceService { get; } = AppearanceService.Instance;

        // Persistent ViewModel — survives page re-creation caused by Frame navigation caching limitations.
        public static ModelBinCreatorViewModel ModelBinCreatorViewModel { get; } = new ModelBinCreatorViewModel();

        public static IntPtr MainWindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);


        static App()
        {
        }

        public App()
        {
            this.InitializeComponent();


            this.UnhandledException += App_UnhandledException;

            AppDomain.CurrentDomain.UnhandledException += new System.UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
        }


        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {

            MessageBox(IntPtr.Zero, $"UI Crash: {e.Message}\n\nStack: {e.Exception.StackTrace}", "Critical UI Error", 0x10);


            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            string msg = ex != null ? ex.Message : "Unknown Error";
            string? stack = ex?.StackTrace;

            MessageBox(IntPtr.Zero, $"Background Crash: {msg}\n\nStack: {stack}", "Critical Background Error", 0x10);
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var settingsService = new SettingsService();
            var settings = await settingsService.LoadAsync();

            MainWindow = new MainWindow();

            // Apply appearance before window is shown so first frame is already correct
            AppearanceService.Apply(settings);

            MainWindow.Activate();
        }

        public static void ShowErrorDialog(string message)
        {
            if (MainWindow?.Content?.XamlRoot != null)
            {
                MainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                        {
                            XamlRoot = MainWindow.Content.XamlRoot,
                            Title = "Error",
                            Content = message,
                            CloseButtonText = "OK"
                        };
                        await dialog.ShowAsync();
                    }
                    catch { } // Avoid crashes if dialog fails
                });
            }
        }

        public static void ShowInfoDialog(string message, string title = "Information")
        {
            if (MainWindow?.Content?.XamlRoot != null)
            {
                MainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                        {
                            XamlRoot = MainWindow.Content.XamlRoot,
                            Title = title,
                            Content = message,
                            CloseButtonText = "OK"
                        };
                        await dialog.ShowAsync();
                    }
                    catch { } 
                });
            }
        }
    }
}