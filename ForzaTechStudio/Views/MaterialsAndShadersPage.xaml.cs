using System;
using System.IO;
using System.Threading.Tasks;
using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ForzaTechStudio.Views;

public sealed partial class MaterialsAndShadersPage : Page
{
    public MaterialsAndShadersViewModel ViewModel { get; } = new();

    public MaterialsAndShadersPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync();
            MaterialEditorControl.ParameterValueChanged += (_, _) => ViewModel.NotifyMaterialParametersEdited();
        };
    }

    private async void BrowseLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LibraryBrowserDialog { XamlRoot = this.XamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog.SelectedItem != null)
        {
            ViewModel.SelectedGameId = dialog.BrowserViewModel.SelectedGameId;
            ViewModel.SelectedLibraryItem = dialog.SelectedItem;
            await OpenSelectedLibraryAssetAsync();
        }
    }

    private void OpenTextureInSwatchbinViewer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        string? resolvedSource = element.Tag as string;
        if (string.IsNullOrWhiteSpace(resolvedSource) || !Path.IsPathRooted(resolvedSource) || !File.Exists(resolvedSource))
            return;

        NavigateToPage(typeof(SwatchbinEditorPage), resolvedSource);
    }

    private async Task OpenSelectedLibraryAssetAsync()
    {
        if (ViewModel.SelectedLibraryItem == null)
            return;

        if (ViewModel.SelectedLibraryItemIsSwatchbin)
        {
            string? preparedPath = await ViewModel.ResolveSelectedLibrarySwatchPathAsync();
            if (!string.IsNullOrWhiteSpace(preparedPath) && File.Exists(preparedPath))
                NavigateToPage(typeof(SwatchbinEditorPage), preparedPath);

            return;
        }

        await ViewModel.OpenSelectedLibraryAssetCommand.ExecuteAsync(null);
    }

    private void NavigateToPage(Type pageType, object? parameter = null)
    {
        if (Frame != null)
        {
            Frame.Navigate(pageType, parameter);
            return;
        }

        if (App.MainWindow.Content is Frame rootFrame && rootFrame.Content is ShellPage shell)
            shell.NavigateToPage(pageType, parameter);
    }
}