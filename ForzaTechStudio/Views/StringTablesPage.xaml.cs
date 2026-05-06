using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;

namespace ForzaTechStudio.Views
{
    public sealed partial class StringTablesPage : Page
    {
        public StringTablesViewModel ViewModel { get; } = new StringTablesViewModel();

        public StringTablesPage()
        {
            InitializeComponent();
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        }

        // Drag and drop

        // ListView selection

        private void EntriesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Sync multi-select state to ViewModel without feeding back through the TwoWay binding.
            ViewModel.SetSelectionSilently(EntriesListView.SelectedItems);
        }

        // Captures which entry was right-clicked so context menu handlers can act on it.
        private StrEntryViewModel? _contextEntry;
        private void EntryRow_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: StrEntryViewModel entry })
            {
                _contextEntry = entry;
                // Only switch to single-selection when clicking outside the current selection.
                if (!EntriesListView.SelectedItems.Contains(entry))
                    ViewModel.SelectedEntry = entry;
            }
        }

        // Row context menu

        private void CopyHashHex_Click(object sender, RoutedEventArgs e)
            => CopyToClipboard(_contextEntry?.HashIdHex ?? string.Empty);

        private void CopyHashDec_Click(object sender, RoutedEventArgs e)
            => CopyToClipboard(_contextEntry?.FormattedHashId ?? string.Empty);

        private void CopyKeyName_Click(object sender, RoutedEventArgs e)
            => CopyToClipboard(_contextEntry?.KeyName ?? string.Empty);

        private void CopyContent_Click(object sender, RoutedEventArgs e)
            => CopyToClipboard(_contextEntry?.Content ?? string.Empty);

        private void CopyRow_Click(object sender, RoutedEventArgs e)
        {
            // Copy all selected rows if multiple are selected; otherwise just the right-clicked row.
            string text = ViewModel.SelectedCount > 1
                ? ViewModel.GetSelectedRowsText()
                : $"{_contextEntry?.HashIdHex}\t{_contextEntry?.KeyName}\t{_contextEntry?.Content}";
            CopyToClipboard(text);
        }

        private void CopySelectedRows_Click(object sender, RoutedEventArgs e)
            => CopyToClipboard(ViewModel.GetSelectedRowsText());

        private async void PasteRow_Click(object sender, RoutedEventArgs e)
            => await PasteFromClipboardAsync();

        private async System.Threading.Tasks.Task PasteFromClipboardAsync()
        {
            if (!ViewModel.IsFileLoaded) return;
            try
            {
                var view = Clipboard.GetContent();
                if (!view.Contains(StandardDataFormats.Text)) return;
                string text = await view.GetTextAsync();
                foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    string line  = rawLine.TrimEnd('\r');
                    var    parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    uint hashId; string keyName, content;
                    if (parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        && uint.TryParse(parts[0][2..], System.Globalization.NumberStyles.HexNumber, null, out hashId))
                    {
                        keyName = parts.Length > 1 ? parts[1] : string.Empty;
                        content = parts.Length > 2 ? string.Join("\t", parts[2..]) : string.Empty;
                    }
                    else
                    {
                        keyName = parts[0];
                        content = string.Join("\t", parts[1..]);
                        hashId  = StringTablesViewModel.GetLocIDHash(keyName);
                    }
                    if (!string.IsNullOrEmpty(keyName))
                        ViewModel.AddEntry(hashId, keyName, content);
                }
            }
            catch { /* clipboard empty or format mismatch – silently ignore */ }
        }

        private void DeleteEntryMenu_Click(object sender, RoutedEventArgs e)
            => ViewModel.DeleteEntryCommand.Execute(null);

        // Keyboard shortcuts (Ctrl+C / V / Z / Y)

        private bool IsTextBoxFocused()
            => FocusManager.GetFocusedElement(XamlRoot) is TextBox;

        private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Only handle Ctrl+key combos
            var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control);
            if ((ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0) return;

            // Let focused TextBoxes handle their own built-in clipboard/undo shortcuts
            if (IsTextBoxFocused()) return;

            switch (e.Key)
            {
                case Windows.System.VirtualKey.C:
                    e.Handled = true;
                    if (ViewModel.SelectedCount > 0)
                        CopyToClipboard(ViewModel.GetSelectedRowsText());
                    break;
                case Windows.System.VirtualKey.V:
                    e.Handled = true;
                    await PasteFromClipboardAsync();
                    break;
                case Windows.System.VirtualKey.Z:
                    e.Handled = true;
                    ViewModel.UndoCommand.Execute(null);
                    break;
                case Windows.System.VirtualKey.Y:
                    e.Handled = true;
                    ViewModel.RedoCommand.Execute(null);
                    break;
            }
        }

        // Drag and drop

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption  = "Open .str / .zip file";
            e.DragUIOverride.IsGlyphVisible = true;
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var file in items.OfType<Windows.Storage.StorageFile>())
            {
                if (file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    await ViewModel.LoadZipFileAsync(file.Path);
                else if (file.Name.EndsWith(".str", StringComparison.OrdinalIgnoreCase))
                    await ViewModel.LoadFileAsync(file.Path);
            }
        }

        // New string table

        private async void NewButton_Click(object sender, RoutedEventArgs e)
        {
            var tableNameBox = new TextBox
            {
                PlaceholderText = "e.g. CarNames",
                MinWidth        = 280
            };

            var dialog = new ContentDialog
            {
                Title               = "New String Table",
                Content             = new StackPanel
                {
                    Spacing  = 8,
                    Children =
                    {
                        new TextBlock { Text = "Table name (used as the file base name and embedded in the file header):" },
                        tableNameBox
                    }
                },
                PrimaryButtonText   = "Create",
                CloseButtonText     = "Cancel",
                DefaultButton       = ContentDialogButton.Primary,
                XamlRoot            = XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                string name = tableNameBox.Text.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    await ShowErrorAsync("Invalid Name", "Table name cannot be empty.");
                    return;
                }
                ViewModel.CreateNewFile(name);
            }
        }

        // Add entry

        private async void AddEntryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsFileLoaded) return;

            var keyBox = new TextBox
            {
                PlaceholderText = "e.g. CAR_BMW_M3_NAME",
                MinWidth        = 280
            };
            var contentBox = new TextBox
            {
                PlaceholderText = "Display string value...",
                AcceptsReturn   = true,
                MinHeight       = 80,
                MaxHeight       = 200,
                TextWrapping    = Microsoft.UI.Xaml.TextWrapping.Wrap,
                MinWidth        = 280
            };
            ScrollViewer.SetVerticalScrollBarVisibility(contentBox, ScrollBarVisibility.Auto);
            // Computed hash preview label that updates as the user types
            var hashPreviewLabel = new TextBlock
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas, Courier New"),
                FontSize   = 12,
                Opacity    = 0.7,
                Text       = "Hash: 0xFFFFFFFF"
            };

            keyBox.TextChanged += (s, _) =>
            {
                uint hash = StringTablesViewModel.GetLocIDHash(keyBox.Text.Trim());
                hashPreviewLabel.Text = $"Hash: 0x{hash:X8}";
            };

            var dialog = new ContentDialog
            {
                Title             = "Add Entry",
                Content           = new StackPanel
                {
                    Spacing   = 8,
                    Children  =
                    {
                        new TextBlock { Text = "Key Name (used to compute the Hash ID):" },
                        keyBox,
                        hashPreviewLabel,
                        new TextBlock { Text = "Content (display string):", Margin = new Thickness(0, 4, 0, 0) },
                        contentBox
                    }
                },
                PrimaryButtonText = "Add",
                CloseButtonText   = "Cancel",
                DefaultButton     = ContentDialogButton.Primary,
                XamlRoot          = XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                string keyName = keyBox.Text.Trim();
                if (string.IsNullOrEmpty(keyName))
                {
                    await ShowErrorAsync("Invalid Input", "Key Name is required to compute a Hash ID.");
                    return;
                }
                uint hashId = StringTablesViewModel.GetLocIDHash(keyName);
                ViewModel.AddEntry(hashId, keyName, contentBox.Text);
            }
        }

        // Helpers
        private static void CopyToClipboard(string text)
        {
            var pkg = new DataPackage();
            pkg.SetText(text);
            Clipboard.SetContent(pkg);
        }
        private async System.Threading.Tasks.Task ShowErrorAsync(string title, string message)
        {
            var dlg = new ContentDialog
            {
                Title          = title,
                Content        = message,
                CloseButtonText = "OK",
                XamlRoot       = XamlRoot
            };
            await dlg.ShowAsync();
        }
    }
}
