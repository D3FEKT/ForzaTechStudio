using ForzaTechStudio.Helpers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System;
using System.Linq;
using ForzaTechStudio.ViewModels;

namespace ForzaTechStudio.Views
{
    public sealed partial class CarbinEditorPage : Page
    {
        // Cached references to named controls ? assigned in Loaded because x:Name inside
        // PivotItem content is not directly accessible as a field in the partial class.
        private TextBox? _nonUpgradableSearchBox;
        private ListView? _nonUpgradableModelsList;
        private TextBox? _upgradableSearchBox;
        private ListView? _upgradableModelsList;
        // Tracks the model entry that was right-clicked for the context flyout.
        // MenuFlyout is a static resource so its items don't inherit DataContext from the
        // ListViewItem we capture it in the Opening event instead.
        private ViewModels.CarbinModelEntry? _contextMenuTarget;

        private FileChangeWatcher? _fileWatcher;
        private bool _changeDialogShowing;
        private string _watchedFilePath = "";

        public CarbinEditorPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

            this.Loaded += OnPageLoaded;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.FileSaved += path => _fileWatcher?.Suppress(path);
            this.Unloaded += (_, _) => _fileWatcher?.UnwatchAll();

            _fileWatcher = new FileChangeWatcher(this.DispatcherQueue);
            _fileWatcher.FileChanged += OnFileChangedExternally;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            _nonUpgradableSearchBox = this.FindName("NonUpgradableModelSearchBox") as TextBox;
            _nonUpgradableModelsList = this.FindName("NonUpgradableModelsList") as ListView;
            _upgradableSearchBox = this.FindName("UpgradableModelSearchBox") as TextBox;
            _upgradableModelsList = this.FindName("UpgradableModelsList") as ListView;

            // Sync tab strip selection to active tab
            _isSwitchingTabs = true;
            CarbinTabListView.SelectedItem = ViewModel.ActiveTab;
            _isSwitchingTabs = false;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CarbinEditorViewModel.ActiveTab) && !_isSwitchingTabs)
            {
                _isSwitchingTabs = true;
                CarbinTabListView.SelectedItem = ViewModel.ActiveTab;
                _isSwitchingTabs = false;
            }
            else if (e.PropertyName == nameof(CarbinEditorViewModel.LoadedFilePath))
            {
                // Update watcher when the loaded file changes
                if (!string.IsNullOrEmpty(_watchedFilePath))
                    _fileWatcher?.Unwatch(_watchedFilePath);
                _watchedFilePath = ViewModel.LoadedFilePath;
                if (!string.IsNullOrEmpty(_watchedFilePath))
                    _fileWatcher?.Watch(_watchedFilePath);
            }
            else if (e.PropertyName == nameof(CarbinEditorViewModel.SelectedNonUpgradablePart))
            {
                if (_nonUpgradableSearchBox != null)
                    _nonUpgradableSearchBox.Text = "";
                if (_nonUpgradableModelsList != null && ViewModel.SelectedNonUpgradablePart != null)
                    _nonUpgradableModelsList.ItemsSource = ViewModel.SelectedNonUpgradablePart.Models;
            }
            else if (e.PropertyName == nameof(CarbinEditorViewModel.SelectedUpgradablePart))
            {
                if (_upgradableSearchBox != null)
                    _upgradableSearchBox.Text = "";
                if (_upgradableModelsList != null && ViewModel.SelectedUpgradablePart != null)
                    _upgradableModelsList.ItemsSource = ViewModel.SelectedUpgradablePart.Models;
            }
        }

        private void NonUpgradableModelSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var part = ViewModel.SelectedNonUpgradablePart;
            if (part == null || _nonUpgradableModelsList == null) return;

            var query = (sender as TextBox)?.Text ?? "";
            if (string.IsNullOrEmpty(query))
            {
                _nonUpgradableModelsList.ItemsSource = part.Models;
            }
            else
            {
                _nonUpgradableModelsList.ItemsSource = part.Models
                    .Where(m => m.ModelFileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                             || m.ModelGamePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        private void UpgradableModelSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var part = ViewModel.SelectedUpgradablePart;
            if (part == null || _upgradableModelsList == null) return;

            var query = (sender as TextBox)?.Text ?? "";
            if (string.IsNullOrEmpty(query))
            {
                _upgradableModelsList.ItemsSource = part.Models;
            }
            else
            {
                _upgradableModelsList.ItemsSource = part.Models
                    .Where(m => m.ModelFileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                             || m.ModelGamePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        private async void OnFileChangedExternally(string path)
        {
            if (_changeDialogShowing) return;
            _changeDialogShowing = true;
            try
            {
                var fileName = System.IO.Path.GetFileName(path);
                var dialog = new ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = "File Changed",
                    Content = $"\"{fileName}\" was modified outside of ForzaTechStudio. Reload it?",
                    PrimaryButtonText = "Reload",
                    CloseButtonText = "Ignore"
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                    await ViewModel.ReloadFileAsync();
            }
            finally
            {
                _changeDialogShowing = false;
            }
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
                if (file != null && file.Path.EndsWith(".carbin", StringComparison.OrdinalIgnoreCase))
                {
                    await ViewModel.LoadCarbinFileAsync(file.Path);
                }
            }
        }

        public Visibility NullToVis(object obj)
        {
            return obj != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ModelContextFlyout_Opening(object sender, object e)
        {
            if (sender is MenuFlyout flyout && flyout.Target is FrameworkElement element)
                _contextMenuTarget = element.DataContext as ViewModels.CarbinModelEntry;
        }

        private async void ReplaceModelFile_Click(object sender, RoutedEventArgs e)
        {
            var model = _contextMenuTarget;
            if (model == null) return;

            await ViewModel.ReplaceModelFileAsync(model);
        }

        private async void EditModelPath_Click(object sender, RoutedEventArgs e)
        {
            var model = _contextMenuTarget;
            if (model == null) return;

            var inputTextBox = new TextBox
            {
                AcceptsReturn = false,
                Height = 32,
                Text = model.ModelGamePath ?? ""
            };

            var dialog = new ContentDialog
            {
                Title = "Edit Model Path",
                Content = inputTextBox,
                PrimaryButtonText = "Update",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                ViewModel.UpdateModelPath(model, inputTextBox.Text);
            }
        }

        private async void EditAoPath_Click(object sender, RoutedEventArgs e)
        {
            var model = _contextMenuTarget;
            if (model == null) return;

            var inputTextBox = new TextBox
            {
                AcceptsReturn = false,
                Height = 32,
                Text = model.AoSwatchbinGamePath ?? ""
            };

            var dialog = new ContentDialog
            {
                Title = "Edit AO Swatchbin Path",
                Content = inputTextBox,
                PrimaryButtonText = "Update",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                string newPath = inputTextBox.Text;
                model.AoSwatchbinGamePath = newPath;
                model.AoSwatchbinFileName = System.IO.Path.GetFileName(newPath.Replace("game:\\", "").Replace("game:/", "").Replace("\\", "/"));

                if (model.AoMapInfos.Count > 0)
                {
                    model.AoMapInfos[0].Path = newPath;
                }
                else if (!string.IsNullOrEmpty(newPath))
                {
                    var newAoInfo = new ViewModels.AOMapInfoEntry
                    {
                        Version = 3,
                        Path = newPath,
                        PartType = 0xFFFFFFFF,
                        PartId = -1,
                        DroppedModelInstanceGuid = Guid.Empty,
                        IsDefault = true,
                        LodTest = 0,
                        LodValue = 31
                    };
                    model.AoMapInfos.Add(newAoInfo);
                }
            }
        }
    }
}
