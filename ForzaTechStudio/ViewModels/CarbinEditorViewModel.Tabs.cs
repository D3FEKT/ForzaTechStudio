using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
            tab.NonUpgradablePartsSnapshot = new List<CarbinPartEntry>(NonUpgradableParts);
            tab.UpgradablePartsSnapshot = new List<CarbinPartEntry>(UpgradableParts);
            tab.UndoHistory = _undoStack;
            tab.RedoHistory = _redoStack;
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
