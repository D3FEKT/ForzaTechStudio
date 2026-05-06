using System;
using ForzaTechStudio.Models;
using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ForzaTechStudio.Views
{
    public sealed partial class CarSceneXmlPage : Page
    {
        public ShakeBonesViewModel ViewModel { get; } = new();

        public CarSceneXmlPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var window = App.MainWindow;
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                ViewModel.SetWindowHandle(hwnd);
            }
        }

        // Drag & drop

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0 && items[0] is Windows.Storage.StorageFile file)
                {
                    await ViewModel.LoadFileAsync(file.Path);
                }
            }
        }

        // Raw XML tab buttons

        private void RefreshXml_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RefreshXmlText();
        }

        private void ApplyXml_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ApplyXmlFromText(XmlTextBox.Text);
        }

        // Property change handlers

        private void CameraName_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.RefreshXmlText();
        }

        private void BoneProperty_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.RefreshXmlText();
        }

        private void BoneNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (!double.IsNaN(args.NewValue))
                ViewModel.RefreshXmlText();
        }

        // Inline transform row handlers

        private void InlineAxis_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ViewModel.RefreshXmlText();
        }

        private void InlineTransformValue_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (!double.IsNaN(args.NewValue))
                ViewModel.RefreshXmlText();
        }

        private void InlineDeleteTransform_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ShakeBonesTransform xf)
                ViewModel.RemoveTransform(xf);
        }

        // Generic refresh handlers (reused across all new format panels)

        private void AnyText_Changed(object sender, TextChangedEventArgs e)
            => ViewModel.RefreshXmlText();

        private void AnyNumber_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (!double.IsNaN(args.NewValue))
                ViewModel.RefreshXmlText();
        }

        private void AnyToggle_Toggled(object sender, RoutedEventArgs e)
            => ViewModel.RefreshXmlText();

        private void AnyCombo_Changed(object sender, SelectionChangedEventArgs e)
            => ViewModel.RefreshXmlText();
    }
}
