using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using ForzaTechStudio.Services;

namespace ForzaTechStudio.ViewModels
{
    public partial class CarbinEditorViewModel
    {
        [RelayCommand]
        private void NewCarbin()
        {
            // Save current tab and open in new tab if it has content
            if (IsContentVisible && ActiveTab != null)
            {
                SaveCurrentStateToTab(ActiveTab);
                var newTab = CreateEmptyTab();
                Tabs.Add(newTab);
                ActiveTab = newTab;
            }

            IsBusy = true;
            ClearAll();
            StatusMessage = "Initializing new carbin...";

            try
            {
                LoadedFilePath = "";
                LoadedFileName = "New_Car.carbin";

                SelectedVersionIndex = 5; // FH5
                var (sceneVer, modelVer, isHz) = GetVersionInfo();
                DetectedSceneVersion = sceneVer;
                DetectedModelVersion = modelVer;
                IsHorizon = isHz;

                SceneName = "CUSTOM_CAR";
                MediaName = "CUSTOM_CAR";
                SkeletonPath = @"game:\media\cars\CUSTOM_CAR\scene\_skeleton.modelbin";
                Ordinal = 0;
                BuildStrict = false;
                BuildGuid = Guid.NewGuid();

                LodFlagLODS = true;
                LodFlagLOD0 = true;
                LodFlagLOD1 = true;
                LodFlagLOD2 = true;
                LodFlagLOD3 = true;
                LodFlagLOD4 = true;
                LodFlagLOD5 = false;

                IsContentVisible = true;
                StatusMessage = "New carbin initialized. Ready to edit.";

                // Update tab name
                if (ActiveTab != null) ActiveTab.Name = LoadedFileName;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task OpenCarbinAsync()
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".carbin");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            await LoadCarbinFileAsync(file.Path);
        }

        public async Task LoadCarbinFileAsync(string filePath, bool isReload = false)
        {
            if (!isReload)
            {
                // Open in new tab if current tab already has content
                if (IsContentVisible && ActiveTab != null)
                {
                    SaveCurrentStateToTab(ActiveTab);
                    var newTab = CreateEmptyTab();
                    Tabs.Add(newTab);
                    ActiveTab = newTab;
                }
                else if (Tabs.Count == 0)
                {
                    var firstTab = CreateEmptyTab();
                    Tabs.Add(firstTab);
                    ActiveTab = firstTab;
                }
            }

            IsBusy = true;
            StatusMessage = "Loading carbin file...";
            ClearAll();
            _lastParsingContext = "Starting";
            _lastFilePosition = 0;

            try
            {
                LoadedFilePath = filePath;
                LoadedFileName = Path.GetFileName(filePath);

                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);

                if (fileBytes.Length < 2)
                {
                    StatusMessage = $"Error: File is too small ({fileBytes.Length} bytes)";
                    return;
                }

                using var ms = new MemoryStream(fileBytes);
                using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);
                ParseCarbinFile(reader, fileBytes.Length);

                IsContentVisible = true;
                StatusMessage = $"Loaded: {LoadedFileName} (Scene v{DetectedSceneVersion}, Model v{DetectedModelVersion}, {(IsHorizon ? "Horizon" : "Motorsport")})"; 

                
                if (ActiveTab != null) ActiveTab.Name = LoadedFileName;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Parse error: {ex.Message} | Pos: 0x{_lastFilePosition:X} | {_lastParsingContext}";
                IsContentVisible = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void CloseFile()
        {
            if (ActiveTab != null)
                CloseTab(ActiveTab);
            else
            {
                ClearAll();
                StatusMessage = "File closed.";
            }
        }

        // Reload current file from disk
        [RelayCommand]
        public async Task ReloadFileAsync()
        {
            if (string.IsNullOrEmpty(LoadedFilePath) || !File.Exists(LoadedFilePath))
            {
                StatusMessage = "No file to reload.";
                return;
            }
            var path = LoadedFilePath;
            await LoadCarbinFileAsync(path, isReload: true);
        }

        [RelayCommand]
        public void CloseAllFiles()
        {
            Tabs.Clear();
            ClearAll();
            var blank = CreateEmptyTab();
            Tabs.Add(blank);
            ActiveTab = blank;
            StatusMessage = "All files closed.";
        }

        [RelayCommand]
        private async Task SaveCarbinAsync()
        {
            if (!IsContentVisible)
            {
                StatusMessage = "Please open a carbin file first.";
                return;
            }

            var savePicker = new FileSavePicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);

            savePicker.SuggestedStartLocation = PickerLocationId.Desktop;
            savePicker.SuggestedFileName = LoadedFileName;
            savePicker.FileTypeChoices.Add("Forza Carbin", new[] { ".carbin" });

            var outputFile = await savePicker.PickSaveFileAsync();
            if (outputFile == null) return;

            IsBusy = true;
            StatusMessage = "Saving carbin file...";

            PrepareForSave();

            try
            {
                var (sceneVersion, modelVersion, isHorizon) = GetVersionInfo();

                await Task.Run(() =>
                {
                    using var fs = new FileStream(outputFile.Path, FileMode.Create, FileAccess.Write);
                    using var writer = new BinaryWriter(fs);
                    WriteCarbinFile(writer, sceneVersion, modelVersion, isHorizon);
                });

                StatusMessage = $"Success! Saved to {outputFile.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task SaveCarbinInPlaceAsync()
        {
            if (!IsContentVisible)
            {
                StatusMessage = "Please open a carbin file first.";
                return;
            }

            if (string.IsNullOrEmpty(LoadedFilePath))
            {
                StatusMessage = "No file path available. Use Save As instead.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Saving carbin file...";

            PrepareForSave();

            try
            {
                var (sceneVersion, modelVersion, isHorizon) = GetVersionInfo();

                await Task.Run(() =>
                {
                    using var fs = new FileStream(LoadedFilePath, FileMode.Create, FileAccess.Write);
                    using var writer = new BinaryWriter(fs);
                    WriteCarbinFile(writer, sceneVersion, modelVersion, isHorizon);
                });

                FileSaved?.Invoke(LoadedFilePath);
                StatusMessage = $"Saved: {LoadedFileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Non-Upgradable Part Actions

        [RelayCommand]
        private void AddNonUpgradablePart()
        {
            var part = new CarbinPartEntry(name => { })
            {
                PartTypeName = "CCarParts_CarBody",
                PartType = CCarPartsEnum.CCarParts_CarBody
            };
            NonUpgradableParts.Add(part);
            SelectedNonUpgradablePart = part;
            StatusMessage = $"Added new non-upgradable part. Total: {NonUpgradableParts.Count}";
            PushUndo(
                () => { NonUpgradableParts.Remove(part); SelectedNonUpgradablePart = NonUpgradableParts.FirstOrDefault(); StatusMessage = "Undo: add part."; },
                () => { NonUpgradableParts.Add(part); SelectedNonUpgradablePart = part; StatusMessage = "Redo: add part."; });
        }

        [RelayCommand]
        private void RemoveNonUpgradablePart()
        {
            if (SelectedNonUpgradablePart != null)
            {
                var removed = SelectedNonUpgradablePart;
                var idx = NonUpgradableParts.IndexOf(removed);
                NonUpgradableParts.Remove(removed);
                SelectedNonUpgradablePart = NonUpgradableParts.FirstOrDefault();
                StatusMessage = $"Removed part. Remaining: {NonUpgradableParts.Count}";
                PushUndo(
                    () => { NonUpgradableParts.Insert(Math.Min(idx, NonUpgradableParts.Count), removed); SelectedNonUpgradablePart = removed; StatusMessage = "Undo: remove part."; },
                    () => { NonUpgradableParts.Remove(removed); SelectedNonUpgradablePart = NonUpgradableParts.FirstOrDefault(); StatusMessage = "Redo: remove part."; });
            }
        }

        [RelayCommand]
        private void ConvertToUpgradablePart()
        {
            if (SelectedNonUpgradablePart == null) return;

            var source = SelectedNonUpgradablePart;
            var newPart = new CarbinPartEntry(name => { })
            {
                PartTypeName = source.PartTypeName,
                PartType = source.PartType,
                BoundsMinX = source.BoundsMinX,
                BoundsMinY = source.BoundsMinY,
                BoundsMinZ = source.BoundsMinZ,
                BoundsMinW = source.BoundsMinW,
                BoundsMaxX = source.BoundsMaxX,
                BoundsMaxY = source.BoundsMaxY,
                BoundsMaxZ = source.BoundsMaxZ,
                BoundsMaxW = source.BoundsMaxW,
                OriginalPartVersion = source.OriginalPartVersion,
                OriginalUpgradablePartVersion = source.OriginalUpgradablePartVersion,
                OriginalPartTypeUint = source.OriginalPartTypeUint,
            };

            foreach (var model in source.Models)
                newPart.Models.Add(model);

            // Add a default stock upgrade entry so the part is valid
            newPart.Upgrades.Add(new UpgradeEntry(0, 0, true)
            {
                Version = 3,
                CarBodyId = 0,
                ParentIsStock = true
            });

            NonUpgradableParts.Remove(source);
            UpgradableParts.Add(newPart);
            SelectedUpgradablePart = newPart;
            StatusMessage = $"Converted '{newPart.PartTypeName}' to upgradable part.";
        }

        [RelayCommand]
        private async Task AddModelToNonUpgradablePartAsync()
        {
            if (SelectedNonUpgradablePart == null)
            {
                StatusMessage = "Please select a part first.";
                return;
            }
            await AddModelsToPartAsync(SelectedNonUpgradablePart);
        }

        [RelayCommand]
        private void RemoveModelFromNonUpgradablePart()
        {
            if (SelectedNonUpgradablePart != null && SelectedNonUpgradableModel != null)
            {
                var part = SelectedNonUpgradablePart;
                var model = SelectedNonUpgradableModel;
                var idx = part.Models.IndexOf(model);
                part.Models.Remove(model);
                SelectedNonUpgradableModel = part.Models.FirstOrDefault();
                StatusMessage = "Model removed.";
                PushUndo(
                    () => { part.Models.Insert(Math.Min(idx, part.Models.Count), model); SelectedNonUpgradableModel = model; StatusMessage = "Undo: remove model."; },
                    () => { part.Models.Remove(model); SelectedNonUpgradableModel = part.Models.FirstOrDefault(); StatusMessage = "Redo: remove model."; });
            }
        }

        [RelayCommand]
        private async Task BrowseAoSwatchbinAsync()
        {
            if (SelectedNonUpgradableModel == null) return;
            await BrowseSwatchbinForModelAsync(SelectedNonUpgradableModel);
        }

        [RelayCommand]
        private async Task LoadMaterialsForNonUpgradableModelAsync()
        {
            if (SelectedNonUpgradableModel == null)
            {
                StatusMessage = "Please select a model first.";
                return;
            }
            await LoadMaterialsForModelAsync(SelectedNonUpgradableModel);
        }

        [RelayCommand]
        private void AddMaterialIndexToNonUpgradable()
        {
            if (SelectedNonUpgradableModel != null)
            {
                SelectedNonUpgradableModel.MaterialIndexes.Add(new MaterialIndexEntry("material_name", 0, UsesFh6MaterialHashEditorForModel(SelectedNonUpgradableModel)));
                StatusMessage = $"Added material index. Total: {SelectedNonUpgradableModel.MaterialIndexes.Count}";
            }
        }

        [RelayCommand]
        private void RemoveMaterialIndexFromNonUpgradable()
        {
            if (SelectedNonUpgradableModel != null && SelectedNonUpgradableModel.SelectedMaterialIndex != null)
            {
                SelectedNonUpgradableModel.MaterialIndexes.Remove(SelectedNonUpgradableModel.SelectedMaterialIndex);
                SelectedNonUpgradableModel.SelectedMaterialIndex = SelectedNonUpgradableModel.MaterialIndexes.FirstOrDefault();
                StatusMessage = $"Removed material index. Remaining: {SelectedNonUpgradableModel.MaterialIndexes.Count}";
            }
        }

        // Upgradable Part Actions

        [RelayCommand]
        private void AddUpgradablePart()
        {
            var part = new CarbinPartEntry(name => { })
            {
                PartTypeName = "CCarParts_FrontBumper",
                PartType = CCarPartsEnum.CCarParts_FrontBumper
            };
            UpgradableParts.Add(part);
            SelectedUpgradablePart = part;
            StatusMessage = $"Added new upgradable part. Total: {UpgradableParts.Count}";
            PushUndo(
                () => { UpgradableParts.Remove(part); SelectedUpgradablePart = UpgradableParts.FirstOrDefault(); StatusMessage = "Undo: add part."; },
                () => { UpgradableParts.Add(part); SelectedUpgradablePart = part; StatusMessage = "Redo: add part."; });
        }

        [RelayCommand]
        private void RemoveUpgradablePart()
        {
            if (SelectedUpgradablePart != null)
            {
                var removed = SelectedUpgradablePart;
                var idx = UpgradableParts.IndexOf(removed);
                UpgradableParts.Remove(removed);
                SelectedUpgradablePart = UpgradableParts.FirstOrDefault();
                StatusMessage = $"Removed part. Remaining: {UpgradableParts.Count}";
                PushUndo(
                    () => { UpgradableParts.Insert(Math.Min(idx, UpgradableParts.Count), removed); SelectedUpgradablePart = removed; StatusMessage = "Undo: remove part."; },
                    () => { UpgradableParts.Remove(removed); SelectedUpgradablePart = UpgradableParts.FirstOrDefault(); StatusMessage = "Redo: remove part."; });
            }
        }

        [RelayCommand]
        private void ConvertToNonUpgradablePart()
        {
            if (SelectedUpgradablePart == null) return;

            var source = SelectedUpgradablePart;
            var newPart = new CarbinPartEntry(name => { })
            {
                PartTypeName = source.PartTypeName,
                PartType = source.PartType,
                BoundsMinX = source.BoundsMinX,
                BoundsMinY = source.BoundsMinY,
                BoundsMinZ = source.BoundsMinZ,
                BoundsMinW = source.BoundsMinW,
                BoundsMaxX = source.BoundsMaxX,
                BoundsMaxY = source.BoundsMaxY,
                BoundsMaxZ = source.BoundsMaxZ,
                BoundsMaxW = source.BoundsMaxW,
                OriginalPartVersion = source.OriginalPartVersion,
                OriginalUpgradablePartVersion = source.OriginalUpgradablePartVersion,
                OriginalPartTypeUint = source.OriginalPartTypeUint,
            };

            // Copy models; clear upgrade IDs since non-upgradable parts don't use them
            foreach (var model in source.Models)
            {
                model.UpgradeIds.Clear();
                model.UpgradeIdWrappers.Clear();
                newPart.Models.Add(model);
            }

            UpgradableParts.Remove(source);
            NonUpgradableParts.Add(newPart);
            SelectedNonUpgradablePart = newPart;
            StatusMessage = $"Converted '{newPart.PartTypeName}' to standard (non-upgradable) part.";
        }

        [RelayCommand]
        private async Task AddModelToUpgradablePartAsync()
        {
            if (SelectedUpgradablePart == null)
            {
                StatusMessage = "Please select a part first.";
                return;
            }
            await AddModelsToPartAsync(SelectedUpgradablePart);
        }

        [RelayCommand]
        private void RemoveModelFromUpgradablePart()
        {
            if (SelectedUpgradablePart != null && SelectedUpgradableModel != null)
            {
                var part = SelectedUpgradablePart;
                var model = SelectedUpgradableModel;
                var idx = part.Models.IndexOf(model);
                part.Models.Remove(model);
                SelectedUpgradableModel = part.Models.FirstOrDefault();
                StatusMessage = "Model removed.";
                PushUndo(
                    () => { part.Models.Insert(Math.Min(idx, part.Models.Count), model); SelectedUpgradableModel = model; StatusMessage = "Undo: remove model."; },
                    () => { part.Models.Remove(model); SelectedUpgradableModel = part.Models.FirstOrDefault(); StatusMessage = "Redo: remove model."; });
            }
        }

        [RelayCommand]
        private async Task BrowseAoSwatchbinForUpgradableAsync()
        {
            if (SelectedUpgradableModel == null) return;
            await BrowseSwatchbinForModelAsync(SelectedUpgradableModel);
        }

        [RelayCommand]
        private async Task LoadMaterialsForUpgradableModelAsync()
        {
            if (SelectedUpgradableModel == null)
            {
                StatusMessage = "Please select a model first.";
                return;
            }
            await LoadMaterialsForModelAsync(SelectedUpgradableModel);
        }

        [RelayCommand]
        private void AddMaterialIndexToUpgradable()
        {
            if (SelectedUpgradableModel != null)
            {
                SelectedUpgradableModel.MaterialIndexes.Add(new MaterialIndexEntry("material_name", 0, UsesFh6MaterialHashEditorForModel(SelectedUpgradableModel)));
                StatusMessage = $"Added material index. Total: {SelectedUpgradableModel.MaterialIndexes.Count}";
            }
        }

        [RelayCommand]
        private void RemoveMaterialIndexFromUpgradable()
        {
            if (SelectedUpgradableModel != null && SelectedUpgradableModel.SelectedMaterialIndex != null)
            {
                SelectedUpgradableModel.MaterialIndexes.Remove(SelectedUpgradableModel.SelectedMaterialIndex);
                SelectedUpgradableModel.SelectedMaterialIndex = SelectedUpgradableModel.MaterialIndexes.FirstOrDefault();
                StatusMessage = $"Removed material index. Remaining: {SelectedUpgradableModel.MaterialIndexes.Count}";
            }
        }

        // Upgrade Actions

        [RelayCommand]
        private void AddUpgrade()
        {
            if (SelectedUpgradablePart != null)
            {
                var part = SelectedUpgradablePart;
                var upgrade = new UpgradeEntry(part.Upgrades.Count, (byte)part.Upgrades.Count, part.Upgrades.Count == 0)
                {
                    Version = 3,
                    CarBodyId = 0,
                    ParentIsStock = true
                };
                part.Upgrades.Add(upgrade);
                SelectedUpgrade = upgrade;
                StatusMessage = $"Added upgrade. Total: {part.Upgrades.Count}";
                PushUndo(
                    () => { part.Upgrades.Remove(upgrade); SelectedUpgrade = part.Upgrades.FirstOrDefault(); StatusMessage = "Undo: add upgrade."; },
                    () => { part.Upgrades.Add(upgrade); SelectedUpgrade = upgrade; StatusMessage = "Redo: add upgrade."; });
            }
        }

        [RelayCommand]
        private void RemoveUpgrade()
        {
            if (SelectedUpgradablePart != null && SelectedUpgrade != null)
            {
                var part = SelectedUpgradablePart;
                var upgrade = SelectedUpgrade;
                var idx = part.Upgrades.IndexOf(upgrade);
                part.Upgrades.Remove(upgrade);
                SelectedUpgrade = part.Upgrades.FirstOrDefault();
                StatusMessage = $"Removed upgrade. Remaining: {part.Upgrades.Count}";
                PushUndo(
                    () => { part.Upgrades.Insert(Math.Min(idx, part.Upgrades.Count), upgrade); SelectedUpgrade = upgrade; StatusMessage = "Undo: remove upgrade."; },
                    () => { part.Upgrades.Remove(upgrade); SelectedUpgrade = part.Upgrades.FirstOrDefault(); StatusMessage = "Redo: remove upgrade."; });
            }
        }

        [RelayCommand]
        private void AddUpgradeIdToModel()
        {
            if (SelectedUpgradableModel != null)
            {
                SelectedUpgradableModel.UpgradeIds.Add(0);
                SelectedUpgradableModel.UpgradeIdWrappers.Add(new UpgradeIdWrapper(0));
                StatusMessage = $"Added upgrade ID. Total: {SelectedUpgradableModel.UpgradeIds.Count}";
            }
        }

        [RelayCommand]
        private void RemoveUpgradeIdFromModel()
        {
            if (SelectedUpgradableModel == null) return;

            var model = SelectedUpgradableModel;
            var selected = model.SelectedUpgradeIdWrapper;

            if (selected != null)
            {
                int idx = model.UpgradeIdWrappers.IndexOf(selected);
                model.UpgradeIdWrappers.Remove(selected);
                if (idx < model.UpgradeIds.Count)
                    model.UpgradeIds.RemoveAt(idx);
                model.SelectedUpgradeIdWrapper = model.UpgradeIdWrappers.Count > 0
                    ? model.UpgradeIdWrappers[Math.Min(idx, model.UpgradeIdWrappers.Count - 1)]
                    : null;
                StatusMessage = $"Removed upgrade ID. Remaining: {model.UpgradeIdWrappers.Count}";
            }
        }

        [RelayCommand]
        private async Task AddUpgradeIdToAllModelsAsync()
        {
            if (SelectedUpgradablePart == null) return;

            var numberBox = new NumberBox
            {
                Value = 0,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Minimum = int.MinValue,
                Maximum = int.MaxValue,
                Header = "Upgrade ID"
            };

            var dialog = new ContentDialog
            {
                Title = "Add Upgrade ID to All Models",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Enter an upgrade ID to add to all {SelectedUpgradablePart.Models.Count} model(s) in '{SelectedUpgradablePart.PartTypeName}'.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        numberBox
                    }
                },
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            int id = (int)numberBox.Value;
            int count = 0;
            foreach (var model in SelectedUpgradablePart.Models)
            {
                model.UpgradeIds.Add(id);
                model.UpgradeIdWrappers.Add(new UpgradeIdWrapper(id));
                count++;
            }

            StatusMessage = $"Added upgrade ID {id} to {count} model(s).";
        }

        [RelayCommand]
        private async Task RemoveUpgradeIdFromAllModelsAsync()
        {
            if (SelectedUpgradablePart == null) return;

            var numberBox = new NumberBox
            {
                Value = 0,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Minimum = int.MinValue,
                Maximum = int.MaxValue,
                Header = "Upgrade ID"
            };

            var dialog = new ContentDialog
            {
                Title = "Remove Upgrade ID from All Models",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Enter an upgrade ID to remove from all {SelectedUpgradablePart.Models.Count} model(s) in '{SelectedUpgradablePart.PartTypeName}'.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        numberBox
                    }
                },
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            int id = (int)numberBox.Value;
            int affectedModels = 0;
            int removedTotal = 0;

            foreach (var model in SelectedUpgradablePart.Models)
            {
                var toRemove = model.UpgradeIdWrappers
                    .Select((w, i) => (wrapper: w, index: i))
                    .Where(x => x.wrapper.Value == id)
                    .OrderByDescending(x => x.index)
                    .ToList();

                if (toRemove.Count == 0) continue;

                foreach (var (wrapper, index) in toRemove)
                {
                    model.UpgradeIdWrappers.Remove(wrapper);
                    if (index < model.UpgradeIds.Count)
                        model.UpgradeIds.RemoveAt(index);
                }

                if (model.SelectedUpgradeIdWrapper != null && !model.UpgradeIdWrappers.Contains(model.SelectedUpgradeIdWrapper))
                    model.SelectedUpgradeIdWrapper = model.UpgradeIdWrappers.FirstOrDefault();

                affectedModels++;
                removedTotal += toRemove.Count;
            }

            StatusMessage = removedTotal > 0
                ? $"Removed upgrade ID {id} ({removedTotal} entr{(removedTotal == 1 ? "y" : "ies")}) from {affectedModels} model(s)."
                : $"No entries with upgrade ID {id} found.";
        }

        // Shared Helpers

        private static CarbinModelEntry DeepCloneModelEntry(CarbinModelEntry src)
        {
            var dst = new CarbinModelEntry
            {
                ModelFileName = src.ModelFileName,
                ModelFullPath = src.ModelFullPath,
                ModelGamePath = src.ModelGamePath,
                AoSwatchbinFileName = src.AoSwatchbinFileName,
                AoSwatchbinFullPath = src.AoSwatchbinFullPath,
                AoSwatchbinGamePath = src.AoSwatchbinGamePath,
                BoneName = src.BoneName,
                BoneId = src.BoneId,
                SnapToParent = src.SnapToParent,
                DrawGroupExterior = src.DrawGroupExterior,
                DrawGroupCockpit = src.DrawGroupCockpit,
                DrawGroupShadow = src.DrawGroupShadow,
                DrawGroupHood = src.DrawGroupHood,
                DrawGroupWindshieldReflection = src.DrawGroupWindshieldReflection,
                DrawGroupDriverlessCockpit = src.DrawGroupDriverlessCockpit,
                DrawGroupWindshieldReflectionDriverlessCockpit = src.DrawGroupWindshieldReflectionDriverlessCockpit,
                DrawGroupProxyLOD = src.DrawGroupProxyLOD,
                TransformMatrix = src.TransformMatrix,
                IsDroppable = src.IsDroppable,
                DropValue = src.DropValue,
                DropPartId = src.DropPartId,
                BreakAmount = src.BreakAmount,
                IsInteriorWindshield = src.IsInteriorWindshield,
                ReceivesImpactMask = src.ReceivesImpactMask,
                ReceivesSplatterMask = src.ReceivesSplatterMask,
                ReceivesDamage = src.ReceivesDamage,
                ReceivesDirt = src.ReceivesDirt,
                ReceivesOil = src.ReceivesOil,
                ReceivesRubber = src.ReceivesRubber,
                ReceivesRain = src.ReceivesRain,
                AssemblyName = src.AssemblyName,
                GuidV13 = Guid.NewGuid(),
                DropGuidV14 = Guid.Empty,
                AoMapInfoIdV14 = src.AoMapInfoIdV14,
                IsInterior = src.IsInterior,
                IsLeftSideWindow = src.IsLeftSideWindow,
                IsRightSideWindow = src.IsRightSideWindow,
                IsNascarWiper = src.IsNascarWiper,
                IsLicensePlate = src.IsLicensePlate,
                ModelLodFlagLODS = src.ModelLodFlagLODS,
                ModelLodFlagLOD0 = src.ModelLodFlagLOD0,
                ModelLodFlagLOD1 = src.ModelLodFlagLOD1,
                ModelLodFlagLOD2 = src.ModelLodFlagLOD2,
                ModelLodFlagLOD3 = src.ModelLodFlagLOD3,
                ModelLodFlagLOD4 = src.ModelLodFlagLOD4,
                ModelLodFlagLOD5 = src.ModelLodFlagLOD5,
                HorizonId = src.HorizonId,
                HorizonUnkV18 = src.HorizonUnkV18,
                HorizonUnkV21Flag = src.HorizonUnkV21Flag,
                HorizonUnkV21Path = src.HorizonUnkV21Path,
                HorizonUnkV15 = src.HorizonUnkV15,
                MotorsportUnkV18 = src.MotorsportUnkV18,
                MotorsportUnkV19 = src.MotorsportUnkV19,
                ProxyLodId = src.ProxyLodId,
                RawDrawGroupsValue = src.RawDrawGroupsValue,
                OriginalModelVersion = src.OriginalModelVersion,
                InternalId = src.InternalId,
            };

            foreach (var mat in src.MaterialIndexes)
                dst.MaterialIndexes.Add(new MaterialIndexEntry(mat.Key, mat.Value, mat.UseHexValue));

            foreach (var ao in src.AoMapInfos)
                dst.AoMapInfos.Add(new AOMapInfoEntry
                {
                    Version = ao.Version,
                    Path = ao.Path,
                    PartType = ao.PartType,
                    PartId = ao.PartId,
                    DroppedModelInstanceGuid = ao.DroppedModelInstanceGuid,
                    BoneIndex = ao.BoneIndex,
                    IsDropped = ao.IsDropped,
                    IsDefault = ao.IsDefault,
                    LodTest = ao.LodTest,
                    LodValue = ao.LodValue,
                });

            foreach (var id in src.UpgradeIds)
                dst.UpgradeIds.Add(id);

            foreach (var w in src.UpgradeIdWrappers)
                dst.UpgradeIdWrappers.Add(new UpgradeIdWrapper(w.Value));

            foreach (var guid in src.DamageGuids)
                dst.DamageGuids.Add(guid);

            foreach (var kvp in src.MaterialOverrides)
                dst.MaterialOverrides[kvp.Key] = (byte[])kvp.Value.Clone();

            return dst;
        }

        private bool TryAssignNextDuplicateHorizonId(CarbinModelEntry clone, out bool assignedHorizonId)
        {
            assignedHorizonId = false;
            var (_, modelVersion, isHorizon) = GetVersionInfo();
            if (!isHorizon || modelVersion < 17) return true;

            int maxHorizonId = NonUpgradableParts.SelectMany(part => part.Models)
                .Concat(UpgradableParts.SelectMany(part => part.Models))
                .Select(model => (int)model.HorizonId)
                .DefaultIfEmpty(0)
                .Max();

            if (maxHorizonId >= byte.MaxValue)
            {
                StatusMessage = "Cannot duplicate model: all Horizon ID values are in use.";
                return false;
            }

            clone.HorizonId = (byte)(maxHorizonId + 1);
            assignedHorizonId = true;
            return true;
        }

        private (ushort modelVersion, bool isHorizon) GetNewModelDefaultsProfile()
        {
            ushort existingModelVersion = NonUpgradableParts.SelectMany(part => part.Models)
                .Concat(UpgradableParts.SelectMany(part => part.Models))
                .Select(model => (int)model.OriginalModelVersion)
                .Where(version => version > 0)
                .GroupBy(version => version)
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.Key)
                .Select(group => (ushort)group.Key)
                .FirstOrDefault();

            if (existingModelVersion > 0)
                return (existingModelVersion, IsHorizon);

            if (DetectedModelVersion > 0)
                return (DetectedModelVersion, IsHorizon);

            var (_, targetModelVersion, targetIsHorizon) = GetVersionInfo();
            return (targetModelVersion, targetIsHorizon);
        }

        private static string BuildDefaultAssemblyName(string? modelFileName)
        {
            string baseName = Path.GetFileNameWithoutExtension(modelFileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(baseName))
                return string.Empty;

            int suffixSeparatorIndex = baseName.LastIndexOf('_');
            if (suffixSeparatorIndex > 0)
            {
                string suffix = baseName[(suffixSeparatorIndex + 1)..];
                if (suffix.Length == 1 && char.IsLetterOrDigit(suffix[0]))
                    return baseName[..suffixSeparatorIndex];
            }

            return baseName;
        }

        private bool TryAssignNextAddedModelHorizonId(CarbinModelEntry entry, ushort modelVersion, bool isHorizon)
        {
            if (!isHorizon || modelVersion < 17)
            {
                entry.HorizonId = 0;
                return true;
            }

            int maxHorizonId = NonUpgradableParts.SelectMany(part => part.Models)
                .Concat(UpgradableParts.SelectMany(part => part.Models))
                .Select(model => (int)model.HorizonId)
                .DefaultIfEmpty(0)
                .Max();

            if (maxHorizonId >= byte.MaxValue)
            {
                StatusMessage = "Cannot add model: all Horizon ID values are in use.";
                return false;
            }

            entry.HorizonId = (byte)(maxHorizonId + 1);
            return true;
        }

        private bool InitializeAddedModelEntry(CarbinModelEntry entry)
        {
            var (modelVersion, isHorizon) = GetNewModelDefaultsProfile();

            entry.OriginalModelVersion = modelVersion;
            entry.AssemblyName = BuildDefaultAssemblyName(entry.ModelFileName);

            return TryAssignNextAddedModelHorizonId(entry, modelVersion, isHorizon);
        }

        [RelayCommand]
        private void DuplicateNonUpgradableModel()
        {
            if (SelectedNonUpgradablePart == null || SelectedNonUpgradableModel == null) return;

            var clone = DeepCloneModelEntry(SelectedNonUpgradableModel);
            if (!TryAssignNextDuplicateHorizonId(clone, out bool assignedHorizonId)) return;

            int idx = SelectedNonUpgradablePart.Models.IndexOf(SelectedNonUpgradableModel);
            SelectedNonUpgradablePart.Models.Insert(idx + 1, clone);
            SelectedNonUpgradableModel = clone;
            StatusMessage = assignedHorizonId
                ? $"Duplicated '{clone.ModelFileName}' with Horizon ID {clone.HorizonId}."
                : $"Duplicated '{clone.ModelFileName}'.";
        }

        [RelayCommand]
        private void DuplicateUpgradableModel()
        {
            if (SelectedUpgradablePart == null || SelectedUpgradableModel == null) return;

            var clone = DeepCloneModelEntry(SelectedUpgradableModel);
            if (!TryAssignNextDuplicateHorizonId(clone, out bool assignedHorizonId)) return;

            int idx = SelectedUpgradablePart.Models.IndexOf(SelectedUpgradableModel);
            SelectedUpgradablePart.Models.Insert(idx + 1, clone);
            SelectedUpgradableModel = clone;
            StatusMessage = assignedHorizonId
                ? $"Duplicated '{clone.ModelFileName}' with Horizon ID {clone.HorizonId}."
                : $"Duplicated '{clone.ModelFileName}'.";
        }

        private async Task AddModelsToPartAsync(CarbinPartEntry part)
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".modelbin");
            picker.FileTypeFilter.Add(".carbin");

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0)
                return;

            int addedCount = 0;
            var (_, targetModelVersion, targetIsHorizon) = GetVersionInfo();

            foreach (var file in files)
            {
                string ext = Path.GetExtension(file.Path).ToLowerInvariant();

                if (ext == ".carbin")
                {
                    // Import from carbin: show searchable model selector
                    try
                    {
                        byte[] fileBytes = await File.ReadAllBytesAsync(file.Path);
                        if (fileBytes.Length < 2)
                        {
                            StatusMessage = $"Error: {file.Name} is too small to be a valid carbin.";
                            continue;
                        }

                        var allModels = ParseCarbinForAllModels(fileBytes);
                        if (allModels.Count == 0)
                        {
                            StatusMessage = $"No models found in {file.Name}.";
                            continue;
                        }

                        var selected = await ShowAddModelFromCarbinDialogAsync(allModels);
                        if (selected == null)
                            continue; // user cancelled this file

                        var entry = CreateModelEntryFromCarbinSource(selected, targetModelVersion, targetIsHorizon);
                        part.Models.Add(entry);
                        addedCount++;
                        StatusMessage = $"Added '{selected.DisplayName}' from {file.Name}.";
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Error reading {file.Name}: {ex.Message}";
                    }
                }
                else
                {
                    var entry = new CarbinModelEntry(file.Path, SceneName);
                    if (!await PromptForModelPathAsync(entry))
                        continue;
                    if (!InitializeAddedModelEntry(entry))
                        continue;

                    var materials = MaterialExtractionService.GetMaterialNames(file.Path);
                    bool useHexValue = UsesFh6MaterialHashEditorForModel(entry);
                    foreach (var mat in materials)
                        entry.MaterialIndexes.Add(new MaterialIndexEntry(mat, 0, useHexValue));

                    part.Models.Add(entry);
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                if (addedCount > 1)
                    StatusMessage = $"Added {addedCount} model(s) to part.";

                if (part == SelectedNonUpgradablePart && SelectedNonUpgradableModel == null)
                    SelectedNonUpgradableModel = part.Models.FirstOrDefault();
                else if (part == SelectedUpgradablePart && SelectedUpgradableModel == null)
                    SelectedUpgradableModel = part.Models.FirstOrDefault();
            }
        }

        private async Task<bool> PromptForModelPathAsync(CarbinModelEntry entry)
        {
            if (App.MainWindow.Content is not FrameworkElement root || root.XamlRoot == null)
                return true;

            var inputTextBox = new TextBox
            {
                AcceptsReturn = false,
                Height = 32,
                Text = entry.ModelGamePath ?? ""
            };

            var dialog = new ContentDialog
            {
                Title = "Set Model Path",
                Content = inputTextBox,
                PrimaryButtonText = "Add Model",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(inputTextBox.Text),
                XamlRoot = root.XamlRoot
            };

            inputTextBox.TextChanged += (_, _) =>
            {
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(inputTextBox.Text);
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return false;

            string newPath = inputTextBox.Text.Trim();
            entry.ModelGamePath = newPath;
            entry.ModelFileName = Path.GetFileName(newPath
                .Replace("game:\\", "")
                .Replace("game:/", "")
                .Replace("\\", "/"));

            return true;
        }

        private async Task BrowseSwatchbinForModelAsync(CarbinModelEntry model)
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".swatchbin");
            picker.FileTypeFilter.Add(".carbin");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            if (Path.GetExtension(file.Path).Equals(".carbin", StringComparison.OrdinalIgnoreCase))
            {
                await BrowseAoFromCarbinAsync(model, file.Path);
                return;
            }

            model.AoSwatchbinFullPath = file.Path;
            model.AoSwatchbinFileName = Path.GetFileName(file.Path);
            model.AoSwatchbinGamePath = $@"game:\media\cars\{SceneName}\textures\ao\swatches\{model.AoSwatchbinFileName}";
            model.AoMapInfos.Clear();
            StatusMessage = $"AO Swatchbin set: {model.AoSwatchbinFileName}";
        }

        private async Task BrowseAoFromCarbinAsync(CarbinModelEntry target, string carbinPath)
        {
            try
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(carbinPath);
                if (fileBytes.Length < 2)
                {
                    StatusMessage = "Error: Selected carbin is too small.";
                    return;
                }

                var allModels = ParseCarbinForAllModels(fileBytes);
                if (allModels.Count == 0)
                {
                    StatusMessage = "No models found in the selected carbin.";
                    return;
                }

                var selected = await ShowAddModelFromCarbinDialogAsync(allModels, "Select Model – Copy AO Path", "Copy AO Path");
                if (selected == null) return;

                // Prefer AoMapInfos (v9+), fall back to legacy AO path
                if (selected.AoMapInfos.Count > 0)
                {
                    target.AoMapInfos.Clear();
                    foreach (var ao in selected.AoMapInfos)
                        target.AoMapInfos.Add(new AOMapInfoEntry
                        {
                            Version = ao.Version,
                            Path = ao.Path,
                            PartType = ao.PartType,
                            PartId = ao.PartId,
                            DroppedModelInstanceGuid = ao.DroppedModelInstanceGuid,
                            BoneIndex = ao.BoneIndex,
                            IsDropped = ao.IsDropped,
                            IsDefault = ao.IsDefault,
                            LodTest = ao.LodTest,
                            LodValue = ao.LodValue,
                        });

                    string firstPath = selected.AoMapInfos[0].Path;
                    target.AoSwatchbinGamePath = firstPath;
                    target.AoSwatchbinFileName = Path.GetFileName(firstPath.Replace(@"game:\", "").Replace("game:/", ""));
                    target.AoSwatchbinFullPath = "";
                    StatusMessage = $"AO path copied from '{selected.DisplayName}'.";
                }
                else if (!string.IsNullOrEmpty(selected.AoSwatchPathLegacy))
                {
                    target.AoMapInfos.Clear();
                    target.AoSwatchbinGamePath = selected.AoSwatchPathLegacy;
                    target.AoSwatchbinFileName = Path.GetFileName(selected.AoSwatchPathLegacy.Replace(@"game:\", "").Replace("game:/", ""));
                    target.AoSwatchbinFullPath = "";
                    StatusMessage = $"AO path copied from '{selected.DisplayName}'.";
                }
                else
                {
                    StatusMessage = $"Selected model '{selected.DisplayName}' has no AO path to copy.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error reading carbin: {ex.Message}";
            }
        }

        // Move Model to Part

        [RelayCommand]
        private async Task MoveNonUpgradableModelAsync()
        {
            if (SelectedNonUpgradablePart == null || SelectedNonUpgradableModel == null)
            {
                StatusMessage = "Please select a model first.";
                return;
            }
            await MoveModelAsync(SelectedNonUpgradableModel, SelectedNonUpgradablePart, isSourceUpgradable: false);
        }

        [RelayCommand]
        private async Task MoveUpgradableModelAsync()
        {
            if (SelectedUpgradablePart == null || SelectedUpgradableModel == null)
            {
                StatusMessage = "Please select a model first.";
                return;
            }
            await MoveModelAsync(SelectedUpgradableModel, SelectedUpgradablePart, isSourceUpgradable: true);
        }

        private async Task MoveModelAsync(CarbinModelEntry model, CarbinPartEntry sourcePart, bool isSourceUpgradable)
        {
            var targets = new System.Collections.Generic.List<MoveModelTarget>();
            foreach (var p in NonUpgradableParts)
                if (p != sourcePart) targets.Add(new MoveModelTarget(p, false));
            foreach (var p in UpgradableParts)
                if (p != sourcePart) targets.Add(new MoveModelTarget(p, true));

            if (targets.Count == 0)
            {
                StatusMessage = "No other parts available to move this model into.";
                return;
            }

            var listView = new Microsoft.UI.Xaml.Controls.ListView
            {
                SelectionMode = Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Single,
                MaxHeight = 400,
                MinWidth = 480,
                ItemTemplate = CreateMoveTargetTemplate()
            };
            foreach (var t in targets)
                listView.Items.Add(t);
            listView.SelectedIndex = 0;

            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = $"Move \"{model.ModelFileName}\"",
                Content = new Microsoft.UI.Xaml.Controls.StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new Microsoft.UI.Xaml.Controls.TextBlock
                        {
                            Text = "Select the destination part:",
                            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                        },
                        listView
                    }
                },
                PrimaryButtonText = "Move",
                CloseButtonText = "Cancel",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;

            var target = listView.SelectedItem as MoveModelTarget;
            if (target == null) return;

            // Remove from source
            sourcePart.Models.Remove(model);

            // Clear upgrade IDs when moving into a non-upgradable part
            if (!target.IsUpgradable)
            {
                model.UpgradeIds.Clear();
                model.UpgradeIdWrappers.Clear();
            }

            // Add to destination
            target.Part.Models.Add(model);

            // Refresh selection in source list
            if (isSourceUpgradable)
                SelectedUpgradableModel = sourcePart.Models.FirstOrDefault();
            else
                SelectedNonUpgradableModel = sourcePart.Models.FirstOrDefault();

            StatusMessage = $"Moved \"{model.ModelFileName}\" to \"{target.Part.PartTypeName}\" ({(target.IsUpgradable ? "Upgradable" : "Standard")}).";
        }

        private sealed class MoveModelTarget
        {
            public CarbinPartEntry Part { get; }
            public bool IsUpgradable { get; }
            public string DisplayName => Part.PartTypeName;
            public string SubInfo => IsUpgradable
                ? $"Upgradable  {Part.Models.Count} model(s)  {Part.Upgrades.Count} upgrade(s)"
                : $"Standard   {Part.Models.Count} model(s)";

            public MoveModelTarget(CarbinPartEntry part, bool isUpgradable)
            {
                Part = part;
                IsUpgradable = isUpgradable;
            }
        }

        private static Microsoft.UI.Xaml.DataTemplate CreateMoveTargetTemplate()
        {
            var xaml = @"
                <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                    <StackPanel Padding='4,8' Spacing='2'>
                        <TextBlock Text='{Binding DisplayName}' FontWeight='SemiBold'/>
                        <TextBlock Text='{Binding SubInfo}' FontSize='11' Foreground='{ThemeResource TextFillColorSecondaryBrush}'/>
                    </StackPanel>
                </DataTemplate>";
            return (Microsoft.UI.Xaml.DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
        }
    }
}
