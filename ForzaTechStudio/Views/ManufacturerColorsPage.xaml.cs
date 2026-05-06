using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.Linq;
using System;

namespace ForzaTechStudio.Views;

public sealed partial class ManufacturerColorsPage : Page
{
    public ManufacturerColorsViewModel ViewModel { get; } = new();

    public ManufacturerColorsPage()
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
            if (file != null && file.Path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                await ViewModel.LoadManufacturerColorsFileAsync(file.Path);
            }
        }
    }

    private void TreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (args.AddedItems.Count > 0 && args.AddedItems[0] is ManufacturerColorEntryViewModel entry)
        {
            ViewModel.SelectedEntry = entry;
        }
        else
        {
            ViewModel.SelectedEntry = null;
        }
    }
}
