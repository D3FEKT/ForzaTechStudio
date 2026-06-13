using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ForzaTechStudio.ViewModels
{
    // State snapshot for one open carbin file.
    public sealed class CarbinEditorTab : ObservableObject
    {
        private string _name = "New";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        // Scalar state
        public string FilePath = "";
        public string FileName = "";
        public string ArchivePath = "";
        public string ArchiveEntryName = "";
        public string NonUpgradableModelSearchText = "";
        public string UpgradableModelSearchText = "";
        public bool IsContentVisible;
        public string StatusMessage = "Waiting..";
        public ushort DetectedSceneVersion;
        public ushort DetectedModelVersion;
        public bool IsHorizon = true;
        public bool SceneUnkV6 = true;
        public bool SceneUnkV7 = true;
        public int SelectedVersionIndex = 5;
        public string SceneName = "";
        public string MediaName = "";
        public string SkeletonPath = "";
        public uint Ordinal;
        public bool BuildStrict;
        public Guid BuildGuid = Guid.Empty;
        public bool LodFlagLODS;
        public bool LodFlagLOD0;
        public bool LodFlagLOD1;
        public bool LodFlagLOD2;
        public bool LodFlagLOD3;
        public bool LodFlagLOD4;
        public bool LodFlagLOD5;

        // Part snapshots 
        public List<CarbinPartEntry> NonUpgradablePartsSnapshot = new();
        public List<CarbinPartEntry> UpgradablePartsSnapshot = new();
        public int SelectedNonUpgradablePartIndex = -1;
        public int SelectedNonUpgradableModelIndex = -1;
        public int SelectedUpgradablePartIndex = -1;
        public int SelectedUpgradableModelIndex = -1;
        public int SelectedUpgradeIndex = -1;

        // Per-tab undo/redo history
        public Stack<(Action Undo, Action Redo)> UndoHistory = new();
        public Stack<(Action Undo, Action Redo)> RedoHistory = new();
    }

    public partial class CarbinEditorViewModel
    {
        public ObservableCollection<CarbinEditorTab> Tabs { get; } = new();

        private CarbinEditorTab? _activeTab;
        public CarbinEditorTab? ActiveTab
        {
            get => _activeTab;
            set => SetProperty(ref _activeTab, value);
        }

        internal CarbinEditorTab CreateEmptyTab()
        {
            return new CarbinEditorTab
            {
                Name = "New",
                StatusMessage = "Waiting..",
                SelectedVersionIndex = 5,
                IsHorizon = true,
                SceneUnkV6 = true,
                SceneUnkV7 = true,
                LodFlagLODS = true,
                LodFlagLOD0 = true,
                LodFlagLOD1 = true,
                LodFlagLOD2 = true,
                LodFlagLOD3 = true,
                LodFlagLOD4 = true,
                LodFlagLOD5 = false,
                UndoHistory = new Stack<(Action, Action)>(),
                RedoHistory = new Stack<(Action, Action)>(),
            };
        }

        // Persist current ViewModel state into a tab snapshot.
        internal void SaveCurrentStateToTab(CarbinEditorTab tab)
        {
            tab.FilePath = LoadedFilePath;
            tab.FileName = LoadedFileName;
            tab.ArchivePath = LoadedArchivePath;
            tab.ArchiveEntryName = LoadedArchiveEntryName;
            tab.NonUpgradableModelSearchText = NonUpgradableModelSearchText;
            tab.UpgradableModelSearchText = UpgradableModelSearchText;
            tab.Name = string.IsNullOrEmpty(LoadedFileName) ? "New" : LoadedFileName;
            tab.IsContentVisible = IsContentVisible;
            tab.StatusMessage = StatusMessage;
            tab.DetectedSceneVersion = DetectedSceneVersion;
            tab.DetectedModelVersion = DetectedModelVersion;
            tab.IsHorizon = IsHorizon;
            tab.SceneUnkV6 = _sceneUnkV6;
            tab.SceneUnkV7 = _sceneUnkV7;
            tab.SelectedVersionIndex = SelectedVersionIndex;
            tab.SceneName = SceneName;
            tab.MediaName = MediaName;
            tab.SkeletonPath = SkeletonPath;
            tab.Ordinal = Ordinal;
            tab.BuildStrict = BuildStrict;
            tab.BuildGuid = BuildGuid;
            tab.LodFlagLODS = LodFlagLODS;
            tab.LodFlagLOD0 = LodFlagLOD0;
            tab.LodFlagLOD1 = LodFlagLOD1;
            tab.LodFlagLOD2 = LodFlagLOD2;
            tab.LodFlagLOD3 = LodFlagLOD3;
            tab.LodFlagLOD4 = LodFlagLOD4;
            tab.LodFlagLOD5 = LodFlagLOD5;
            tab.NonUpgradablePartsSnapshot = NonUpgradableParts.Select(DeepClonePartEntry).ToList();
            tab.UpgradablePartsSnapshot = UpgradableParts.Select(DeepClonePartEntry).ToList();
            tab.SelectedNonUpgradablePartIndex = SelectedNonUpgradablePart != null
                ? NonUpgradableParts.IndexOf(SelectedNonUpgradablePart)
                : -1;
            tab.SelectedNonUpgradableModelIndex = SelectedNonUpgradablePart != null && SelectedNonUpgradableModel != null
                ? SelectedNonUpgradablePart.Models.IndexOf(SelectedNonUpgradableModel)
                : -1;
            tab.SelectedUpgradablePartIndex = SelectedUpgradablePart != null
                ? UpgradableParts.IndexOf(SelectedUpgradablePart)
                : -1;
            tab.SelectedUpgradableModelIndex = SelectedUpgradablePart != null && SelectedUpgradableModel != null
                ? SelectedUpgradablePart.Models.IndexOf(SelectedUpgradableModel)
                : -1;
            tab.SelectedUpgradeIndex = SelectedUpgradablePart != null && SelectedUpgrade != null
                ? SelectedUpgradablePart.Upgrades.IndexOf(SelectedUpgrade)
                : -1;
            tab.UndoHistory = _undoStack;
            tab.RedoHistory = _redoStack;
        }

        private static T? GetItemAtOrDefault<T>(IList<T> items, int index) where T : class
        {
            return index >= 0 && index < items.Count ? items[index] : null;
        }

        private static UpgradeEntry DeepCloneUpgradeEntry(UpgradeEntry src)
        {
            return new UpgradeEntry
            {
                Version = src.Version,
                Level = src.Level,
                IsStock = src.IsStock,
                Id = src.Id,
                CarBodyId = src.CarBodyId,
                ParentIsStock = src.ParentIsStock,
                BoundsMinX = src.BoundsMinX,
                BoundsMinY = src.BoundsMinY,
                BoundsMinZ = src.BoundsMinZ,
                BoundsMinW = src.BoundsMinW,
                BoundsMaxX = src.BoundsMaxX,
                BoundsMaxY = src.BoundsMaxY,
                BoundsMaxZ = src.BoundsMaxZ,
                BoundsMaxW = src.BoundsMaxW,
            };
        }

        private static CarbinPartEntry DeepClonePartEntry(CarbinPartEntry src)
        {
            var dst = new CarbinPartEntry(name => { })
            {
                PartTypeName = src.PartTypeName,
                PartType = src.PartType,
                BoundsMinX = src.BoundsMinX,
                BoundsMinY = src.BoundsMinY,
                BoundsMinZ = src.BoundsMinZ,
                BoundsMinW = src.BoundsMinW,
                BoundsMaxX = src.BoundsMaxX,
                BoundsMaxY = src.BoundsMaxY,
                BoundsMaxZ = src.BoundsMaxZ,
                BoundsMaxW = src.BoundsMaxW,
                OriginalPartVersion = src.OriginalPartVersion,
                OriginalUpgradablePartVersion = src.OriginalUpgradablePartVersion,
                OriginalPartTypeUint = src.OriginalPartTypeUint,
            };

            foreach (var model in src.Models)
                dst.Models.Add(DeepCloneModelEntry(model, preserveIdentityValues: true));

            foreach (var upgrade in src.Upgrades)
                dst.Upgrades.Add(DeepCloneUpgradeEntry(upgrade));

            return dst;
        }

        // Restore ViewModel state from a tab snapshot (does NOT call ClearAll to avoid wiping part data).
        internal void RestoreTabState(CarbinEditorTab tab)
        {
            // Clear selections before swapping collections
            SelectedNonUpgradablePart = null;
            SelectedUpgradablePart = null;
            SelectedNonUpgradableModel = null;
            SelectedUpgradableModel = null;
            SelectedUpgrade = null;

            NonUpgradableParts.Clear();
            UpgradableParts.Clear();
            foreach (var p in tab.NonUpgradablePartsSnapshot) NonUpgradableParts.Add(p);
            foreach (var p in tab.UpgradablePartsSnapshot) UpgradableParts.Add(p);

            // Restore scalar state
            LoadedFilePath = tab.FilePath;
            LoadedFileName = tab.FileName;
            LoadedArchivePath = tab.ArchivePath;
            LoadedArchiveEntryName = tab.ArchiveEntryName;
            NonUpgradableModelSearchText = tab.NonUpgradableModelSearchText;
            UpgradableModelSearchText = tab.UpgradableModelSearchText;
            IsContentVisible = tab.IsContentVisible;
            StatusMessage = tab.StatusMessage;
            DetectedSceneVersion = tab.DetectedSceneVersion;
            DetectedModelVersion = tab.DetectedModelVersion;
            IsHorizon = tab.IsHorizon;
            _sceneUnkV6 = tab.SceneUnkV6;
            _sceneUnkV7 = tab.SceneUnkV7;
            SelectedVersionIndex = tab.SelectedVersionIndex;
            SceneName = tab.SceneName;
            MediaName = tab.MediaName;
            SkeletonPath = tab.SkeletonPath;
            Ordinal = tab.Ordinal;
            BuildStrict = tab.BuildStrict;
            BuildGuid = tab.BuildGuid;
            LodFlagLODS = tab.LodFlagLODS;
            LodFlagLOD0 = tab.LodFlagLOD0;
            LodFlagLOD1 = tab.LodFlagLOD1;
            LodFlagLOD2 = tab.LodFlagLOD2;
            LodFlagLOD3 = tab.LodFlagLOD3;
            LodFlagLOD4 = tab.LodFlagLOD4;
            LodFlagLOD5 = tab.LodFlagLOD5;

            SelectedNonUpgradablePart = GetItemAtOrDefault(NonUpgradableParts, tab.SelectedNonUpgradablePartIndex)
                ?? NonUpgradableParts.FirstOrDefault();
            if (SelectedNonUpgradablePart != null)
            {
                RefreshFilteredNonUpgradableModels(preserveSelection: false);
                SelectedNonUpgradableModel = GetItemAtOrDefault(FilteredNonUpgradableModels, tab.SelectedNonUpgradableModelIndex)
                    ?? FilteredNonUpgradableModels.FirstOrDefault();
            }
            else
            {
                RefreshFilteredNonUpgradableModels(preserveSelection: false);
            }

            SelectedUpgradablePart = GetItemAtOrDefault(UpgradableParts, tab.SelectedUpgradablePartIndex)
                ?? UpgradableParts.FirstOrDefault();
            if (SelectedUpgradablePart != null)
            {
                RefreshFilteredUpgradableModels(preserveSelection: false);
                SelectedUpgradableModel = GetItemAtOrDefault(FilteredUpgradableModels, tab.SelectedUpgradableModelIndex)
                    ?? FilteredUpgradableModels.FirstOrDefault();
                SelectedUpgrade = GetItemAtOrDefault(SelectedUpgradablePart.Upgrades, tab.SelectedUpgradeIndex)
                    ?? SelectedUpgradablePart.Upgrades.FirstOrDefault();
            }
            else
            {
                RefreshFilteredUpgradableModels(preserveSelection: false);
            }

            // Restore per-tab undo/redo stacks
            _undoStack = tab.UndoHistory;
            _redoStack = tab.RedoHistory;
            NotifyUndoRedo();
        }

        // Close a tab; always keeps at least one blank tab open.
        internal void CloseTab(CarbinEditorTab tab)
        {
            int idx = Tabs.IndexOf(tab);
            if (idx < 0) return;

            Tabs.Remove(tab);

            if (Tabs.Count == 0)
            {
                var blank = CreateEmptyTab();
                Tabs.Add(blank);
                ActiveTab = blank;
                RestoreTabState(blank);
            }
            else
            {
                int newIdx = Math.Max(0, idx - 1);
                var next = Tabs[newIdx];
                ActiveTab = next;
                RestoreTabState(next);
            }
        }
    }
}
