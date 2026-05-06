using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System;
using System.IO;

namespace ForzaTechStudio.Views;


public class LightFlagItem : INotifyPropertyChanged
{
    private readonly System.Action<bool> _setter;
    private readonly System.Func<bool> _getter;

    public string Label { get; }
    public bool IsEnabled { get; } = true;

    public bool IsSet
    {
        get => _getter();
        set
        {
            _setter(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSet)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LightFlagItem(string label, System.Func<bool> getter, System.Action<bool> setter)
    {
        Label = label;
        _getter = getter;
        _setter = setter;
    }

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSet)));
}

// Page

public sealed partial class LightsPage : Page
{
    // Exposed to XAML via {x:Bind LightFlagItems}
    public ObservableCollection<LightFlagItem> LightFlagItems { get; } = [];

    public LightsPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        // Subscribe to the jump-to-preset event from the ViewModel
        ViewModel.JumpedToPreset += OnJumpedToPreset;

        // When SelectedLight changes, rebuild the flag item list
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
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
                string ext = System.IO.Path.GetExtension(file.Path).ToLowerInvariant();
                string name = file.Name.ToLowerInvariant();

                if (ext == ".zip")
                {
                    await ViewModel.LoadZipFileAsync(file.Path);
                }
                else if (name == "lights.bin")
                {
                    await ViewModel.LoadLightsBinFileAsync(file.Path);
                }
                else if (name == "lightpresets.bin")
                {
                    await ViewModel.LoadPresetsBinFileAsync(file.Path);
                }
                else if (ext == ".bin")
                {
                    // Ambiguous .bin, try to detect or just load as lights
                    await ViewModel.LoadLightsBinFileAsync(file.Path);
                }
            }
        }
    }

    // Tab switching

    private void TabSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!ViewModel.IsContentVisible) return;

        bool isLights = sender.SelectedItem == TabLights;
        bool isPresets = sender.SelectedItem == TabPresets;

        LightsTabContent.Visibility = isLights ? Visibility.Visible : Visibility.Collapsed;
        PresetsTabContent.Visibility = isPresets ? Visibility.Visible : Visibility.Collapsed;
    }

    // Jump-to-preset cross-tab navigation

    private void OnJumpedToPreset(PresetEntryViewModel preset)
    {
        // Switch to the Presets tab
        TabSelector.SelectedItem = TabPresets;

        // Scroll the preset into view
        if (preset != null)
            PresetList.ScrollIntoView(preset);
    }

    // Flag items rebuild

    private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LightsViewModel.SelectedLight))
        {
            if (_previousLight != null)
                _previousLight.PropertyChanged -= SelectedLight_PropertyChanged;

            _previousLight = ViewModel.SelectedLight;

            if (_previousLight != null)
                _previousLight.PropertyChanged += SelectedLight_PropertyChanged;

            RebuildFlagItems();
        }
        else if (e.PropertyName == nameof(LightsViewModel.IsContentVisible))
        {
            if (!ViewModel.IsContentVisible)
            {
                // Reset to Lights tab and hide both content panels cleanly
                TabSelector.SelectedItem = TabLights;
                LightsTabContent.Visibility = Visibility.Collapsed;
                PresetsTabContent.Visibility = Visibility.Collapsed;
            }
        }
    }

    private LightEntryViewModel _previousLight;

    private void SelectedLight_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(LightEntryViewModel.Flags))
        {
            foreach (var item in LightFlagItems)
            {
                item.Refresh();
            }
        }
    }

    private void RebuildFlagItems()
    {
        LightFlagItems.Clear();

        var light = ViewModel.SelectedLight;
        if (light == null) return;

        LightFlagItems.Add(new LightFlagItem("Exterior",
            () => light.IsExterior, v => light.IsExterior = v));
        LightFlagItems.Add(new LightFlagItem("Cockpit",
            () => light.IsCockpit, v => light.IsCockpit = v));
        LightFlagItems.Add(new LightFlagItem("Casts Shadows",
            () => light.CastsShadows, v => light.CastsShadows = v));
        LightFlagItems.Add(new LightFlagItem("Hood",
            () => light.IsHood, v => light.IsHood = v));
        LightFlagItems.Add(new LightFlagItem("Windshield Reflection",
            () => light.IsWindshieldReflection, v => light.IsWindshieldReflection = v));
        LightFlagItems.Add(new LightFlagItem("Driverless Cockpit",
            () => light.IsDriverlessCockpit, v => light.IsDriverlessCockpit = v));
        LightFlagItems.Add(new LightFlagItem("Windshield Reflection (Driverless)",
            () => light.IsWindshieldReflectionDriverlessCockpit, v => light.IsWindshieldReflectionDriverlessCockpit = v));
        LightFlagItems.Add(new LightFlagItem("Proxy LOD",
            () => light.IsProxyLOD, v => light.IsProxyLOD = v));


        Bindings.Update();
    }
}
