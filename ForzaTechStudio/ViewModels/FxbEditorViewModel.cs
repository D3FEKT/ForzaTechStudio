using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTechStudio.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.ViewModels
{
    public partial class FxbEditorViewModel : ObservableObject
    {
        // file state
        private string _loadedFilePath = "";
        public string LoadedFilePath
        {
            get => _loadedFilePath;
            set => SetProperty(ref _loadedFilePath, value);
        }

        private string _loadedFileName = "";
        public string LoadedFileName
        {
            get => _loadedFileName;
            set => SetProperty(ref _loadedFileName, value);
        }

        private bool _isContentVisible;
        public bool IsContentVisible
        {
            get => _isContentVisible;
            set => SetProperty(ref _isContentVisible, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                    OnPropertyChanged(nameof(IsNotBusy));
            }
        }

        public bool IsNotBusy => !IsBusy;

        private string _statusMessage = "Waiting..";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // bank node
        private FxbBankNode? _bank;
        public FxbBankNode? Bank
        {
            get => _bank;
            set => SetProperty(ref _bank, value);
        }

        // tree selection
        private object? _selectedNode;
        public object? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetProperty(ref _selectedNode, value))
                {
                    OnPropertyChanged(nameof(SelectedEffect));
                    OnPropertyChanged(nameof(SelectedPhase));
                    OnPropertyChanged(nameof(SelectedComponent));
                    OnPropertyChanged(nameof(SelectedProperty));
                    OnPropertyChanged(nameof(IsEffectSelected));
                    OnPropertyChanged(nameof(IsPhaseSelected));
                    OnPropertyChanged(nameof(IsComponentSelected));
                    OnPropertyChanged(nameof(IsPropertySelected));
                    OnPropertyChanged(nameof(ComponentCountsLabel));
                    // Re-notify all per-type visibility helpers so the property detail
                    // panel shows the correct editor card for the newly selected property.
                    OnPropertyChanged(nameof(PropIsFloatKeyframe));
                    OnPropertyChanged(nameof(PropIsColorKeyframe));
                    OnPropertyChanged(nameof(PropIsFloatRange));
                    OnPropertyChanged(nameof(PropIsIntegerRange));
                    OnPropertyChanged(nameof(PropIsIntegerInline));
                    OnPropertyChanged(nameof(PropIsFloatInline));
                    OnPropertyChanged(nameof(PropIsStringProperty));
                    OnPropertyChanged(nameof(PropIsVector3));
                    OnPropertyChanged(nameof(PropIsVector4));
                    OnPropertyChanged(nameof(PropHasLinearKeyframes));
                    OnPropertyChanged(nameof(PropHasCubicKeyframes));
                }
            }
        }

        public FxbEffectNode?        SelectedEffect    => SelectedNode as FxbEffectNode;
        public FxbPhaseNode?         SelectedPhase     => SelectedNode as FxbPhaseNode;
        public FxbComponentNode?     SelectedComponent => SelectedNode as FxbComponentNode;
        public FxbPropertyValueNode? SelectedProperty  => SelectedNode as FxbPropertyValueNode;

        public bool IsEffectSelected    => SelectedEffect    != null;
        public bool IsPhaseSelected     => SelectedPhase     != null;
        public bool IsComponentSelected => SelectedComponent != null;
        public bool IsPropertySelected  => SelectedProperty  != null;

        // Per-type visibility helpers for the property detail panel, computed from SelectedProperty for x:Bind re-evaluation.
        public bool PropIsFloatKeyframe    => SelectedProperty?.IsFloatKeyframe    ?? false;
        public bool PropIsColorKeyframe    => SelectedProperty?.IsColorKeyframe    ?? false;
        public bool PropIsFloatRange       => SelectedProperty?.IsFloatRange       ?? false;
        public bool PropIsIntegerRange     => SelectedProperty?.IsIntegerRange     ?? false;
        public bool PropIsIntegerInline    => SelectedProperty?.IsIntegerInline    ?? false;
        public bool PropIsFloatInline      => SelectedProperty?.IsFloatInline      ?? false;
        public bool PropIsStringProperty   => SelectedProperty?.IsStringProperty   ?? false;
        public bool PropIsVector3          => SelectedProperty?.IsVector3          ?? false;
        public bool PropIsVector4          => SelectedProperty?.IsVector4          ?? false;
        public bool PropHasLinearKeyframes => SelectedProperty?.HasLinearKeyframes ?? false;
        public bool PropHasCubicKeyframes  => SelectedProperty?.HasCubicKeyframes  ?? false;

        // Formatted "prop / input / dynamic" counts for the selected component.
        public string ComponentCountsLabel =>
            SelectedComponent is { } c
                ? $"{c.NumPropertyValues} / {c.NumInputValues} / {c.NumDynamicValues}"
                : "";

        // Commands

        [RelayCommand]
        private async Task OpenFxbAsync()
        {
            var picker = new FileOpenPicker();
            var hWnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".fxb");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            await LoadFxbAsync(file.Path);
        }

        public async Task LoadFxbAsync(string filePath)
        {
            IsBusy = true;
            IsContentVisible = false;
            StatusMessage = "Parsing...";
            Bank = null;
            SelectedNode = null;

            try
            {
                LoadedFilePath = filePath;
                LoadedFileName = Path.GetFileName(filePath);

                FxbBankNode bank = await Task.Run(() =>
                {
                    // Stream-based read ? avoids LOH allocation for large files
                    using var fs     = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: false);
                    using var parser = new FxbParser(fs);
                    return parser.Parse();
                });

                bank.FilePath    = filePath;
                Bank             = bank;
                IsContentVisible = true;
                StatusMessage    = $"Loaded: {LoadedFileName} - {bank.NumberEffects} effect(s)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                IsContentVisible = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void CloseFile()
        {
            Bank             = null;
            SelectedNode     = null;
            LoadedFilePath   = "";
            LoadedFileName   = "";
            IsContentVisible = false;
            StatusMessage    = "File closed.";
        }

        // In-place save (float / color patches only)

        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync()
        {
            if (string.IsNullOrEmpty(LoadedFilePath)) return;

            IsBusy = true;
            StatusMessage = "Saving...";
            try
            {
                // Collect all dirty nodes. For the MVP we assume the UI has already
                // written back values to the row objects via TwoWay bindings, and we
                // re-apply all known offsets to the file.
                await Task.Run(() => ApplyAllPatchesToFile(LoadedFilePath));
                StatusMessage = $"Saved: {LoadedFileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanSave() => IsContentVisible && !string.IsNullOrEmpty(LoadedFilePath);

        private void ApplyAllPatchesToFile(string filePath)
        {
            if (Bank == null) return;

            using var fs     = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var writer = new FxbWriter(fs);

            foreach (var effect in Bank.Effects)
            {
                writer.PatchEffectDuration(effect, effect.FDuration);

                foreach (var phase in effect.Phases)
                {
                    writer.PatchPhaseDuration(phase, phase.FDuration);
                    writer.PatchPhasePlayCount(phase, phase.NPlayCount);
                }

                foreach (var comp in effect.Components)
                {
                    writer.PatchComponentStartTime(comp, comp.FStartTime);
                    writer.PatchComponentEndTime(comp, comp.FEndTime);

                    foreach (var prop in comp.PropertyValues)
                    {
                        if (prop.IsIntegerInline && prop.IntValueFieldOffset > 0)
                            writer.PatchIntegerInline(prop, prop.IntValue);

                        if (prop.IsFloatInline && prop.FloatValueFieldOffset > 0)
                            writer.PatchFloatInline(prop, prop.FloatValue);

                        if (prop.IsFloatRange && prop.FloatMinOffset > 0)
                            writer.PatchFloatRange(prop, prop.FloatMin, prop.FloatMax);

                        if (prop.IsIntegerRange && prop.IntMinOffset > 0)
                            writer.PatchIntegerRange(prop, prop.IntMin, prop.IntMax);

                        if (prop.IsVector3 && prop.VecOffset > 0)
                            writer.PatchVector3(prop, prop.VecX, prop.VecY, prop.VecZ);

                        if (prop.IsVector4 && prop.VecOffset > 0)
                            writer.PatchVector4(prop, prop.VecX, prop.VecY, prop.VecZ, prop.VecW);

                        foreach (var kf in prop.LinearKeyframes)
                            writer.PatchLinearKeyframe(kf, kf.UnitTime, kf.Value);

                        foreach (var kf in prop.CubicKeyframes)
                            writer.PatchCubicKeyframe(kf, kf.EndUnitTime, kf.A, kf.B, kf.C, kf.D);

                        foreach (var kf in prop.ColorKeyframes)
                            writer.PatchColorKeyframe(kf, kf.UnitTime, kf.Red, kf.Green, kf.Blue, kf.Alpha);
                    }
                }
            }
        }

        [RelayCommand]
        private async Task SaveAsAsync()
        {
            if (Bank == null) return;

            var picker = new FileSavePicker();
            var hWnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.SuggestedFileName      = LoadedFileName;
            picker.FileTypeChoices.Add("FxStudio Bank", new[] { ".fxb" });

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            IsBusy = true;
            StatusMessage = "Saving as...";
            try
            {
                string destPath = file.Path;
                await Task.Run(() =>
                {
                    File.Copy(LoadedFilePath, destPath, overwrite: true);
                    ApplyAllPatchesToFile(destPath);
                });

                LoadedFilePath = file.Path;
                LoadedFileName = file.Name;
                StatusMessage  = $"Saved as: {file.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
