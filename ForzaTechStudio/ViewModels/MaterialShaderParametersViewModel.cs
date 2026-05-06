using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTools.Bundles;
using ForzaTools.Bundles.Blobs;
using ForzaTechStudio.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.ViewModels
{
    public partial class MaterialShaderParametersViewModel : ObservableObject
    {
        private readonly MaterialShaderParameterBlob _blob;
        private readonly Action _onSaveCallback;

        [ObservableProperty]
        private string _materialName;

        public ObservableCollection<ShaderParameter> Parameters { get; } = new();

        public MaterialShaderParametersViewModel(MaterialShaderParameterBlob blob, string materialName, Action? onSaveCallback = null)
        {
            _blob = blob;
            _materialName = materialName;
            _onSaveCallback = onSaveCallback;
            LoadParameters();
        }

        private void LoadParameters()
        {
            Parameters.Clear();
            if (_blob?.Parameters != null)
            {
                foreach (var param in _blob.Parameters)
                {
                    Parameters.Add(param);
                }
            }
        }

        [RelayCommand]
        public async System.Threading.Tasks.Task AddParameterAsync()
        {
            if (_blob == null) return;

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Add Shader Parameter",
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var stackPanel = new StackPanel { Spacing = 12 };

            var nameBox = new AutoSuggestBox
            {
                Header = "Parameter Name (or Hash)",
                PlaceholderText = "Type to search known names...",
                ItemsSource = NameHashService.Instance.GetAll().Values.OrderBy(x => x).ToList()
            };

            nameBox.TextChanged += (s, e) =>
            {
                if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    var query = s.Text.ToLower();
                    s.ItemsSource = NameHashService.Instance.GetAll().Values
                        .Where(x => x.ToLower().Contains(query))
                        .OrderBy(x => x)
                        .ToList();
                }
            };

            var typeBox = new ComboBox
            {
                Header = "Parameter Type",
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                ItemsSource = Enum.GetValues<ShaderParameterType>()
            };
            typeBox.SelectedIndex = 0;

            stackPanel.Children.Add(nameBox);
            stackPanel.Children.Add(typeBox);
            dialog.Content = stackPanel;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                uint hash = 0;
                string inputName = nameBox.Text;

                var knownHash = NameHashService.Instance.GetHash(inputName);
                if (knownHash.HasValue)
                {
                    hash = knownHash.Value;
                }
                else
                {
                    string hexText = inputName.StartsWith("0x") ? inputName.Substring(2) : inputName;
                    if (!uint.TryParse(hexText, System.Globalization.NumberStyles.HexNumber, null, out hash))
                    {
                        App.ShowErrorDialog($"Invalid hash or unknown name: {inputName}");
                        return;
                    }
                }

                var type = (ShaderParameterType)typeBox.SelectedItem;

                var newParam = new ShaderParameter
                {
                    VersionMajor = 2,
                    VersionMinor = 0,
                    NameHash = hash,
                    Type = type,
                    Value = GetDefaultValueForType(type)
                };

                _blob.Parameters.Add(newParam);
                Parameters.Add(newParam);
            }
        }

        [RelayCommand]
        public void RemoveParameter(ShaderParameter param)
        {
            if (_blob != null && param != null)
            {
                _blob.Parameters.Remove(param);
                Parameters.Remove(param);
            }
        }

        [RelayCommand]
        public async System.Threading.Tasks.Task ImportParametersAsync()
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".json");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    string json = await File.ReadAllTextAsync(file.Path);
                    var entries = JsonSerializer.Deserialize(json, MaterialJsonContext.Default.DictionaryStringMaterialEntry);

                    if (entries != null)
                    {
                        foreach (var kvp in entries)
                        {
                            var entry = kvp.Value;
                            if (!string.IsNullOrEmpty(entry.MaterialBlob))
                            {
                                byte[] data = HexToBytes(entry.MaterialBlob);
                                using (var ms = new MemoryStream(data))
                                {
                                    try
                                    {
                                        var bundle = new Bundle();
                                        bundle.Load(ms);

                                        var paramBlob = bundle.Blobs.OfType<MaterialShaderParameterBlob>().FirstOrDefault();

                                        if (paramBlob != null)
                                        {
                                            ImportBlobParameters(paramBlob);
                                            // Only import from the first valid material found
                                            return;
                                        }
                                    }
                                    catch
                                    {
                                        // Skip if bundle load fails
                                    }
                                }
                            }
                        }

                        App.ShowErrorDialog("No valid shader parameters found in the selected file.");
                    }
                }
                catch (Exception ex)
                {
                    App.ShowErrorDialog($"Error importing parameters: {ex.Message}");
                }
            }
        }

        private void ImportBlobParameters(MaterialShaderParameterBlob sourceBlob)
        {
            if (sourceBlob?.Parameters == null) return;

            foreach (var p in sourceBlob.Parameters)
            {
                var existing = _blob.Parameters.FirstOrDefault(x => x.NameHash == p.NameHash);
                if (existing != null)
                {
                    // To ensure clean update, replace the object in the blobs collection
                    int blobIndex = _blob.Parameters.IndexOf(existing);
                    if (blobIndex != -1)
                        _blob.Parameters[blobIndex] = p;

                    // Update UI collection
                    int index = Parameters.IndexOf(existing);
                    if (index != -1)
                        Parameters[index] = p;
                }
                else
                {
                    _blob.Parameters.Add(p);
                    Parameters.Add(p);
                }
            }
        }

        private byte[] HexToBytes(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        [RelayCommand]
        public async System.Threading.Tasks.Task ViewKnownParametersAsync()
        {
            var listBox = new ListBox();

            var allParams = NameHashService.Instance.GetAll()
                                .OrderBy(kv => kv.Value)
                                .Select(kv => $"{kv.Value} (0x{kv.Key:X8})")
                                .ToList();

            foreach (var p in allParams)
            {
                listBox.Items.Add(p);
            }

            var dialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Known Parameters Reference",
                Content = new ScrollViewer { Content = listBox, Height = 400 },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close
            };

            await dialog.ShowAsync();
        }

        [RelayCommand]
        public void Save()
        {
            _onSaveCallback?.Invoke();
        }

        [RelayCommand]
        public void GoBack()
        {
            // Find the ShellPage frame to trigger back navigation
            if (App.MainWindow?.Content is Microsoft.UI.Xaml.Controls.Panel rootPanel)
            {
                var rootFrame = rootPanel.Children.OfType<Frame>().FirstOrDefault();
                if (rootFrame?.Content is Views.ShellPage shellPage)
                {
                    
                    try 
                    {
                        var navView = shellPage.FindName("NavView") as Microsoft.UI.Xaml.Controls.NavigationView;
                        var contentFrame = shellPage.FindName("ContentFrame") as Frame;
                        
                        if (contentFrame != null && contentFrame.CanGoBack)
                        {
                            contentFrame.GoBack();
                        }
                    }
                    catch 
                    {
                        // Fallback or ignore
                    }
                }
            }
        }

        private object GetDefaultValueForType(ShaderParameterType type)
        {
            return type switch
            {
                ShaderParameterType.Vector => new Vector4(0, 0, 0, 0),
                ShaderParameterType.Color => new Vector4(1, 1, 1, 1),
                ShaderParameterType.Float => 0.0f,
                ShaderParameterType.Bool => false,
                ShaderParameterType.Int => 0,
                ShaderParameterType.Texture2D => new TextureParameter { Path = "" },
                ShaderParameterType.Sampler => new SamplerParameter(),
                ShaderParameterType.Vector2 => new Vector2(0, 0),
                _ => null
            };
        }
    }
}
