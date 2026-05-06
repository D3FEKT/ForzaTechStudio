using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using System;

namespace ForzaTechStudio.Views;

public sealed partial class ConversionToolPage : Page
{
    public ConversionToolPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        RadioOutputFolder.IsChecked = true;
        RadioOutputFolder.Checked += (s, e) => ViewModel.BatchOutputAsZip = false;
        RadioOutputZip.Checked += (s, e) => ViewModel.BatchOutputAsZip = true;
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
            if (file != null)
            {
                await ViewModel.ProcessDroppedFileAsync(file.Path);
            }
        }
    }

    public string GetResultTitle(bool success)
    {
        return success ? "Conversion Successful" : "Conversion Failed";
    }

    public Visibility ShowEmptyLog(int count)
    {
        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
