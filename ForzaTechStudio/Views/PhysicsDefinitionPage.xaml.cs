using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using System.Linq;

namespace ForzaTechStudio.Views
{
    public sealed partial class PhysicsDefinitionPage : Page
    {
        public PhysicsDefinitionViewModel ViewModel { get; } = new PhysicsDefinitionViewModel();

        public PhysicsDefinitionPage()
        {
            this.InitializeComponent();
        }

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            }
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var file = items.FirstOrDefault() as Windows.Storage.StorageFile;
                if (file != null && file.Name.Equals("physicsdefinition.bin", StringComparison.OrdinalIgnoreCase))
                {
                    await ViewModel.LoadPhysicsFileAsync(file.Path);
                }
                else if (file != null && file.Path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    await ViewModel.LoadPhysicsFileAsync(file.Path);
                }
            }
        }

        private void RemoveModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                ViewModel.SourceModels.Remove(path);
            }
        }
    }
}
