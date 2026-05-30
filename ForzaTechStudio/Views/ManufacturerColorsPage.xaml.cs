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

    private async void NewFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "New ManufacturerColors",
            PrimaryButtonText = "v1 (FH3–FH5)",
            SecondaryButtonText = "v2 (FH6)",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
            Content = new TextBlock
            {
                Text = "Choose the format version for the new file.",
                TextWrapping = TextWrapping.WrapWholeWords
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
            return;

        bool isFh6 = result == ContentDialogResult.Secondary;
        ViewModel.NewFileWithVersion(isFh6);
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
        object selectedItem = args.AddedItems.Count > 0 ? args.AddedItems[0] : null;
        ViewModel.SelectedGroup = selectedItem as ManufacturerColorGroupViewModel;
        ViewModel.SelectedEntry = selectedItem as ManufacturerColorEntryViewModel;
    }
}
