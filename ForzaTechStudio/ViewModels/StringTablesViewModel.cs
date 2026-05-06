using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.ViewModels
{
    // Entry view model

    // Observable item representing one entry in a Forza .str string table.
    public partial class StrEntryViewModel : ObservableObject
    {
        private uint _hashId;
        public uint HashId
        {
            get => _hashId;
            set
            {
                if (SetProperty(ref _hashId, value))
                {
                    OnPropertyChanged(nameof(HashIdHex));
                    OnPropertyChanged(nameof(FormattedHashId));
                }
            }
        }

        private uint _tableHashId;
        public uint TableHashId
        {
            get => _tableHashId;
            set
            {
                if (SetProperty(ref _tableHashId, value))
                    OnPropertyChanged(nameof(FormattedHashId));
            }
        }

        public string HashIdHex       => $"0x{HashId:X8}";
        public string FormattedHashId => $"_&{((ulong)TableHashId << 32) | (ulong)HashId}";

        private string _keyName = string.Empty;
        public string KeyName
        {
            get => _keyName;
            set => SetProperty(ref _keyName, value);
        }

        private string _content = string.Empty;
        public string Content
        {
            get => _content;
            set => SetProperty(ref _content, value);
        }
    }

    // Holds per-file state so multiple .str files can be open simultaneously.
    public partial class StrOpenFile : ObservableObject
    {
        public string FilePath  { get; set; } = string.Empty;

        private string _fileName = string.Empty;
        public string FileName
        {
            get => _fileName;
            set { if (SetProperty(ref _fileName, value)) OnPropertyChanged(nameof(DisplayName)); }
        }

        private bool _isModified;
        public bool IsModified
        {
            get => _isModified;
            set { if (SetProperty(ref _isModified, value)) OnPropertyChanged(nameof(DisplayName)); }
        }

        public string TableName   { get; set; } = string.Empty;

        private string? _sourceZipPath;
        // Set when this file was extracted from a zip archive.
        public string? SourceZipPath
        {
            get => _sourceZipPath;
            set { if (SetProperty(ref _sourceZipPath, value)) OnPropertyChanged(nameof(DisplayName)); }
        }
        // The entry path within the zip (e.g. "content/strings/CarNames.str").
        public string? ZipEntryName { get; set; }
        public bool    IsFromZip    => SourceZipPath != null;

        public string DisplayName =>
            IsFromZip
                ? (IsModified ? $"[{Path.GetFileName(SourceZipPath!)}] {FileName} *"
                              : $"[{Path.GetFileName(SourceZipPath!)}] {FileName}")
                : (IsModified ? $"{FileName} *" : FileName);

        public List<StrEntryViewModel> Entries { get; } = new();
    }

    // Page view model

    // ViewModel for the String Tables page.
    // Handles opening, editing, saving and exporting Forza .str string table files.
    public partial class StringTablesViewModel : ObservableObject
    {
        // File / load state

        private string _loadedFilePath = string.Empty;
        public string LoadedFilePath
        {
            get => _loadedFilePath;
            set => SetProperty(ref _loadedFilePath, value);
        }

        private string _loadedFileName = string.Empty;
        public string LoadedFileName
        {
            get => _loadedFileName;
            set => SetProperty(ref _loadedFileName, value);
        }

        private string _tableName = string.Empty;
        public string TableName
        {
            get => _tableName;
            set => SetProperty(ref _tableName, value);
        }

        private bool _isFileLoaded;
        public bool IsFileLoaded
        {
            get => _isFileLoaded;
            set
            {
                if (SetProperty(ref _isFileLoaded, value))
                {
                    OnPropertyChanged(nameof(IsNotFileLoaded));
                    SaveCommand.NotifyCanExecuteChanged();
                    SaveAsCommand.NotifyCanExecuteChanged();
                    SaveCsvCommand.NotifyCanExecuteChanged();
                    RecomputeHashCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsNotFileLoaded => !IsFileLoaded;

        private bool _isModified;
        public bool IsModified
        {
            get => _isModified;
            set
            {
                if (SetProperty(ref _isModified, value) && _activeFile != null)
                    _activeFile.IsModified = value;
            }
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

        // Entry selection / edit bridge

        private StrEntryViewModel? _selectedEntry;
        private List<StrEntryViewModel> _selectedEntries = new();

        public StrEntryViewModel? SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                _selectedEntries = value != null
                    ? new List<StrEntryViewModel> { value }
                    : new List<StrEntryViewModel>();
                if (SetProperty(ref _selectedEntry, value))   // fires SelectedEntry notify via SetProperty
                    FireSelectionNotifications();
            }
        }

        // Called from code-behind's SelectionChanged to sync multi-select state without
        // pushing back through the TwoWay SelectedItem binding (which would clear multi-select).
        public void SetSelectionSilently(IList<object> items)
        {
            _selectedEntries = items.OfType<StrEntryViewModel>().ToList();
            if (_selectedEntries.Count <= 1)
                _selectedEntry = _selectedEntries.Count == 1 ? _selectedEntries[0] : null;
            // When count > 1 leave _selectedEntry as-is — firing SelectedEntry notify would
            // push null to ListView.SelectedItem via TwoWay binding, clearing multi-select.
            FireSelectionNotifications();
        }

        private void FireSelectionNotifications()
        {
            // Reset edit-merge tracking so the next text edit on a new selection starts a fresh undo entry
            _lastUndoEditTarget = null;
            _lastUndoEditField  = null;
            OnPropertyChanged(nameof(IsNothingSelected));
            OnPropertyChanged(nameof(IsEntrySelected));
            OnPropertyChanged(nameof(IsMultipleSelected));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectedCountText));
            OnPropertyChanged(nameof(EditHashIdHex));
            OnPropertyChanged(nameof(EditFormattedHashId));
            OnPropertyChanged(nameof(EditKeyName));
            OnPropertyChanged(nameof(EditContent));
            DeleteEntryCommand.NotifyCanExecuteChanged();
            RecomputeHashCommand.NotifyCanExecuteChanged();
        }

        public bool   IsNothingSelected  => _selectedEntries.Count == 0;
        public bool   IsEntrySelected    => _selectedEntries.Count == 1;
        public bool   IsMultipleSelected => _selectedEntries.Count > 1;
        public int    SelectedCount      => _selectedEntries.Count;
        public string SelectedCountText  => $"{SelectedCount} entries selected";

        // Returns all selected rows as tab-separated lines for the clipboard.
        public string GetSelectedRowsText() =>
            string.Join("\n", _selectedEntries.Select(e => $"{e.HashIdHex}\t{e.KeyName}\t{e.Content}"));

        // Read-only hash display for the selected entry.
        public string EditHashIdHex        => _selectedEntry?.HashIdHex        ?? string.Empty;
        // Read-only combined decimal hash display for the selected entry.
        public string EditFormattedHashId  => _selectedEntry?.FormattedHashId  ?? string.Empty;

        // Editable key name for the selected entry; writes back immediately.
        public string EditKeyName
        {
            get => _selectedEntry?.KeyName ?? string.Empty;
            set
            {
                if (_selectedEntry != null && _selectedEntry.KeyName != value)
                {
                    var entry    = _selectedEntry;
                    var oldValue = entry.KeyName;
                    entry.KeyName = value;
                    IsModified = true;
                    PushUndo(
                        undo: () => { entry.KeyName = oldValue; if (_selectedEntry == entry) OnPropertyChanged(nameof(EditKeyName)); IsModified = true; },
                        redo: () => { entry.KeyName = value;    if (_selectedEntry == entry) OnPropertyChanged(nameof(EditKeyName)); IsModified = true; },
                        editTarget: entry, editField: "KeyName");
                }
            }
        }

        // Editable display content for the selected entry; writes back immediately.
        public string EditContent
        {
            get => _selectedEntry?.Content ?? string.Empty;
            set
            {
                if (_selectedEntry != null && _selectedEntry.Content != value)
                {
                    var entry    = _selectedEntry;
                    var oldValue = entry.Content;
                    entry.Content = value;
                    IsModified = true;
                    PushUndo(
                        undo: () => { entry.Content = oldValue; if (_selectedEntry == entry) OnPropertyChanged(nameof(EditContent)); IsModified = true; },
                        redo: () => { entry.Content = value;    if (_selectedEntry == entry) OnPropertyChanged(nameof(EditContent)); IsModified = true; },
                        editTarget: entry, editField: "Content");
                }
            }
        }

        public ObservableCollection<StrEntryViewModel> Entries { get; } = new();
        public ObservableCollection<StrEntryViewModel> FilteredEntries { get; } = new();

        // Multi-file management

        public ObservableCollection<StrOpenFile> OpenFiles { get; } = new();
        public bool HasOpenFiles => OpenFiles.Count > 0;

        private StrOpenFile? _activeFile;
        public StrOpenFile? ActiveFile
        {
            get => _activeFile;
            set
            {
                var old = _activeFile;
                if (SetProperty(ref _activeFile, value))
                    ActivateFile(old, value);
            }
        }

        private void ActivateFile(StrOpenFile? old, StrOpenFile? next)
        {
            // Persist current working entries back into the file being left
            if (old != null)
            {
                old.Entries.Clear();
                foreach (var e in Entries) old.Entries.Add(e);
            }

            Entries.Clear();
            SelectedEntry = null;
            _searchText   = string.Empty;
            OnPropertyChanged(nameof(SearchText));

            if (next != null)
            {
                foreach (var e in next.Entries) Entries.Add(e);
                TableName      = next.TableName;
                LoadedFilePath = next.FilePath;
                LoadedFileName = next.FileName;
                _isModified    = next.IsModified;
                OnPropertyChanged(nameof(IsModified));
                IsFileLoaded   = true;
            }
            else
            {
                TableName      = string.Empty;
                LoadedFilePath = string.Empty;
                LoadedFileName = string.Empty;
                _isModified    = false;
                OnPropertyChanged(nameof(IsModified));
                IsFileLoaded   = false;
            }

            _sortColumn    = string.Empty;
            _sortAscending = true;
            OnPropertyChanged(nameof(SortIndicatorHashIdHex));
            OnPropertyChanged(nameof(SortIndicatorFormattedHashId));
            OnPropertyChanged(nameof(SortIndicatorKeyName));
            OnPropertyChanged(nameof(SortIndicatorContent));
            // Clear undo/redo history when switching files
            _undoStack.Clear();
            _redoStack.Clear();
            _lastUndoEditTarget = null;
            _lastUndoEditField  = null;
            NotifyUndoRedo();
            RefreshView();
        }

        // Search & sort

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) RefreshView(); }
        }

        private string _sortColumn    = string.Empty;
        private bool   _sortAscending = true;

        public string SortIndicatorHashIdHex       => SortIndicator("HashIdHex");
        public string SortIndicatorFormattedHashId => SortIndicator("FormattedHashId");
        public string SortIndicatorKeyName         => SortIndicator("KeyName");
        public string SortIndicatorContent         => SortIndicator("Content");

        private string SortIndicator(string col) =>
            _sortColumn == col ? (_sortAscending ? " ↑" : " ↓") : string.Empty;

        [RelayCommand]
        private void Sort(string? column)
        {
            if (string.IsNullOrEmpty(column)) return;
            if (_sortColumn == column)
                _sortAscending = !_sortAscending;
            else
            {
                _sortColumn    = column;
                _sortAscending = true;
            }
            OnPropertyChanged(nameof(SortIndicatorHashIdHex));
            OnPropertyChanged(nameof(SortIndicatorFormattedHashId));
            OnPropertyChanged(nameof(SortIndicatorKeyName));
            OnPropertyChanged(nameof(SortIndicatorContent));
            RefreshView();
        }

        private void RefreshView()
        {
            IEnumerable<StrEntryViewModel> q = Entries;
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string term = _searchText.Trim();
                q = q.Where(e =>
                    e.HashIdHex.Contains(term, StringComparison.OrdinalIgnoreCase)       ||
                    e.FormattedHashId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.KeyName.Contains(term, StringComparison.OrdinalIgnoreCase)         ||
                    e.Content.Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            q = _sortColumn switch
            {
                "HashIdHex"       => _sortAscending ? q.OrderBy(e => e.HashId)          : q.OrderByDescending(e => e.HashId),
                "FormattedHashId" => _sortAscending ? q.OrderBy(e => e.FormattedHashId) : q.OrderByDescending(e => e.FormattedHashId),
                "KeyName"         => _sortAscending ? q.OrderBy(e => e.KeyName)         : q.OrderByDescending(e => e.KeyName),
                "Content"         => _sortAscending ? q.OrderBy(e => e.Content)         : q.OrderByDescending(e => e.Content),
                _                 => q
            };
            FilteredEntries.Clear();
            foreach (var e in q)
                FilteredEntries.Add(e);
        }

        // Commands

        // Create a new empty string table (called from code-behind after dialog).
        public void CreateNewFile(string tableName)
        {
            var openFile = new StrOpenFile
            {
                FilePath   = string.Empty,
                FileName   = $"{tableName}.str (new)",
                TableName  = tableName,
                IsModified = true
            };
            OpenFiles.Add(openFile);
            OnPropertyChanged(nameof(HasOpenFiles));
            ActiveFile    = openFile;
            StatusMessage = $"New string table: {tableName}  —  0 entries";
        }

        [RelayCommand]
        private async Task OpenFileAsync()
        {
            var picker = new FileOpenPicker();
            var hWnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".str");
            picker.FileTypeFilter.Add(".zip");

            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files)
            {
                if (file.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    await LoadZipFileAsync(file.Path);
                else
                    await LoadFileAsync(file.Path);
            }
        }

        // Extracts every .str from a Forza-format zip (including LZX-compressed) to a
        // temp folder and opens each one as a normal file tab.
        public async Task LoadZipFileAsync(string zipPath)
        {
            IsBusy        = true;
            StatusMessage = $"Opening zip: {Path.GetFileName(zipPath)}…";
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(),
                    "ForzaStr_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                var extractedFiles = new List<(string TempPath, string EntryName)>();

                await Task.Run(() =>
                {
                    using var zip = new CustomZipFile(zipPath);
                    var strEntries = zip.GetEntries()
                        .Where(e => !e.IsDirectory &&
                                    e.Name.EndsWith(".str", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var entry in strEntries)
                    {
                        string relDir  = Path.GetDirectoryName(
                            entry.Name.Replace('/', Path.DirectorySeparatorChar)) ?? "";
                        string outDir  = Path.Combine(tempDir, relDir);
                        Directory.CreateDirectory(outDir);

                        string outPath = Path.Combine(outDir, Path.GetFileName(entry.Name));
                        byte[] data    = zip.ExtractToMemory(entry);
                        File.WriteAllBytes(outPath, data);
                        extractedFiles.Add((outPath, entry.Name));
                    }
                });

                if (extractedFiles.Count == 0)
                {
                    StatusMessage = $"No .str files found in {Path.GetFileName(zipPath)}";
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                    return;
                }

                foreach (var (tempPath, entryName) in extractedFiles)
                {
                    await LoadFileAsync(tempPath);
                    // Tag with zip provenance so Save knows to write back to the original zip
                    if (_activeFile != null &&
                        string.Equals(_activeFile.FilePath, tempPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _activeFile.SourceZipPath = zipPath;
                        _activeFile.ZipEntryName  = entryName;
                    }
                }

                StatusMessage = $"Opened {extractedFiles.Count} .str file(s) from {Path.GetFileName(zipPath)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Zip error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task LoadFileAsync(string filePath)
        {
            // Switch to the file if it's already open
            var existing = OpenFiles.FirstOrDefault(f =>
                string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { ActiveFile = existing; return; }

            IsBusy        = true;
            StatusMessage = "Parsing...";
            try
            {
                var data = await Task.Run(() =>
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                                  FileShare.Read, 4096, useAsync: false);
                    return StrFileParser.Parse(fs);
                });

                uint tableHash = GetLocIDHash(data.TableName);
                var openFile = new StrOpenFile
                {
                    FilePath  = filePath,
                    FileName  = Path.GetFileName(filePath),
                    TableName = data.TableName
                };
                foreach (var e in data.Entries)
                    openFile.Entries.Add(new StrEntryViewModel
                    {
                        HashId      = e.HashId,
                        TableHashId = tableHash,
                        KeyName     = e.KeyName ?? string.Empty,
                        Content     = e.Content
                    });

                OpenFiles.Add(openFile);
                OnPropertyChanged(nameof(HasOpenFiles));
                ActiveFile    = openFile;
                StatusMessage = $"{openFile.Entries.Count} entries  ·  {openFile.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanSaveDirect))]
        private async Task SaveAsync()
        {
            await WriteToFileAsync(LoadedFilePath);
        }

        private bool CanSaveDirect() => IsFileLoaded && !string.IsNullOrEmpty(LoadedFilePath);

        [RelayCommand(CanExecute = nameof(CanExecuteWhenLoaded))]
        private async Task SaveAsAsync()
        {
            var picker = new FileSavePicker();
            var hWnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(LoadedFileName);
            picker.FileTypeChoices.Add("Forza String Table", new List<string> { ".str" });

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            await WriteToFileAsync(file.Path);
            LoadedFilePath = file.Path;
            LoadedFileName = Path.GetFileName(file.Path);
            if (_activeFile != null)
            {
                _activeFile.FilePath    = file.Path;
                _activeFile.FileName    = Path.GetFileName(file.Path);
                // Detach from source zip — file is now a standalone .str
                _activeFile.SourceZipPath = null;
                _activeFile.ZipEntryName  = null;
            }
            SaveCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanExecuteWhenLoaded))]
        private async Task SaveCsvAsync()
        {
            var picker = new FileSavePicker();
            var hWnd   = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(LoadedFileName);
            picker.FileTypeChoices.Add("CSV File", new List<string> { ".csv" });

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var snapshot = Entries.Select(e => (e.HashId, e.HashIdHex, e.KeyName, e.Content)).ToList();
            await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("HashId,HashIdHex,KeyName,Content");
                foreach (var (id, hex, key, val) in snapshot)
                    sb.AppendLine($"{id},\"{hex}\",\"{CsvEscape(key)}\",\"{CsvEscape(val)}\"");
                File.WriteAllText(file.Path, sb.ToString(), Encoding.UTF8);
            });

            StatusMessage = $"Exported {Entries.Count} entries to {Path.GetFileName(file.Path)}";
        }

        private static string CsvEscape(string s) => (s ?? string.Empty).Replace("\"", "\"\"");

        // Add a new entry (called from code-behind after dialog).
        public void AddEntry(uint hashId, string keyName, string content)
        {
            var entry = new StrEntryViewModel { HashId = hashId, TableHashId = GetLocIDHash(TableName), KeyName = keyName, Content = content };
            Entries.Add(entry);
            RefreshView();
            SelectedEntry = FilteredEntries.Contains(entry) ? entry : null;
            IsModified    = true;
            StatusMessage = $"Added 0x{hashId:X8}  ·  {Entries.Count} total entries";
            PushUndo(
                undo: () => { Entries.Remove(entry); RefreshView(); IsModified = Entries.Count > 0 || _isModified; StatusMessage = $"{Entries.Count} entries"; },
                redo: () => { if (!Entries.Contains(entry)) Entries.Add(entry); RefreshView(); IsModified = true; StatusMessage = $"{Entries.Count} entries"; });
        }

        [RelayCommand(CanExecute = nameof(CanExecuteWhenEntrySelected))]
        private void DeleteEntry()
        {
            if (_selectedEntries.Count == 0) return;
            int minIdx = _selectedEntries
                .Select(e => FilteredEntries.IndexOf(e))
                .Where(i => i >= 0)
                .DefaultIfEmpty(0)
                .Min();
            var removed = _selectedEntries.ToList();
            foreach (var e in removed)
                Entries.Remove(e);
            _selectedEntries.Clear();
            RefreshView();
            SelectedEntry = minIdx < FilteredEntries.Count
                ? FilteredEntries[minIdx]
                : FilteredEntries.LastOrDefault();
            IsModified    = true;
            StatusMessage = $"{Entries.Count} entries";
            PushUndo(
                undo: () => { foreach (var e in removed) if (!Entries.Contains(e)) Entries.Add(e); RefreshView(); IsModified = true; StatusMessage = $"{Entries.Count} entries"; },
                redo: () => { foreach (var e in removed) Entries.Remove(e); RefreshView(); IsModified = true; StatusMessage = $"{Entries.Count} entries"; });
        }

        [RelayCommand(CanExecute = nameof(CanExecuteWhenSingleEntrySelected))]
        private void RecomputeHash()
        {
            if (_selectedEntry == null || string.IsNullOrEmpty(_selectedEntry.KeyName)) return;
            var entry   = _selectedEntry;
            var oldHash = entry.HashId;
            entry.HashId = GetLocIDHash(entry.KeyName);
            var newHash  = entry.HashId;
            OnPropertyChanged(nameof(EditHashIdHex));
            OnPropertyChanged(nameof(EditFormattedHashId));
            IsModified = true;
            PushUndo(
                undo: () => { entry.HashId = oldHash; if (_selectedEntry == entry) { OnPropertyChanged(nameof(EditHashIdHex)); OnPropertyChanged(nameof(EditFormattedHashId)); } IsModified = true; },
                redo: () => { entry.HashId = newHash; if (_selectedEntry == entry) { OnPropertyChanged(nameof(EditHashIdHex)); OnPropertyChanged(nameof(EditFormattedHashId)); } IsModified = true; });
        }

        [RelayCommand]
        private void CloseFile()
        {
            if (_activeFile == null) return;
            int idx = OpenFiles.IndexOf(_activeFile);
            OpenFiles.Remove(_activeFile);
            OnPropertyChanged(nameof(HasOpenFiles));
            ActiveFile    = OpenFiles.Count > 0
                ? OpenFiles[Math.Min(idx, OpenFiles.Count - 1)]
                : null;
            StatusMessage = "File closed.";
        }

        [RelayCommand]
        private void CloseAllFiles()
        {
            // Bypass the property setter to avoid persisting entries back to discarded files
            _activeFile = null;
            OnPropertyChanged(nameof(ActiveFile));
            OpenFiles.Clear();
            OnPropertyChanged(nameof(HasOpenFiles));
            Entries.Clear();
            FilteredEntries.Clear();
            SelectedEntry  = null;
            TableName      = string.Empty;
            LoadedFilePath = string.Empty;
            LoadedFileName = string.Empty;
            _isModified    = false;
            OnPropertyChanged(nameof(IsModified));
            IsFileLoaded   = false;
            StatusMessage  = "All files closed.";
        }

        private bool CanExecuteWhenLoaded()              => IsFileLoaded;
        private bool CanExecuteWhenEntrySelected()        => _selectedEntries.Count > 0;
        private bool CanExecuteWhenSingleEntrySelected()  => _selectedEntries.Count == 1;
        private bool CanUndo() => _undoStack.Count > 0;
        private bool CanRedo() => _redoStack.Count > 0;

        // Undo / Redo

        private readonly Stack<(Action Undo, Action Redo)> _undoStack = new();
        private readonly Stack<(Action Undo, Action Redo)> _redoStack = new();
        // Used to merge consecutive keystrokes on the same field into one undo entry
        private object? _lastUndoEditTarget;
        private string? _lastUndoEditField;

        // Pushes an undoable operation. Consecutive edits to the same field/entry are merged.
        private void PushUndo(Action undo, Action redo, object? editTarget = null, string? editField = null)
        {
            if (editTarget != null && editTarget == _lastUndoEditTarget && editField == _lastUndoEditField
                && _undoStack.Count > 0)
            {
                // Keep the original undo (captures the value before typing started),
                // but update redo to apply the latest value.
                var (existingUndo, _) = _undoStack.Pop();
                _undoStack.Push((existingUndo, redo));
            }
            else
            {
                _undoStack.Push((undo, redo));
                _lastUndoEditTarget = editTarget;
                _lastUndoEditField  = editField;
            }
            _redoStack.Clear();
            NotifyUndoRedo();
        }

        private void NotifyUndoRedo()
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanUndo))]
        private void Undo()
        {
            if (_undoStack.Count == 0) return;
            var (undo, redo) = _undoStack.Pop();
            _redoStack.Push((undo, redo));
            _lastUndoEditTarget = null;
            _lastUndoEditField  = null;
            undo();
            NotifyUndoRedo();
        }

        [RelayCommand(CanExecute = nameof(CanRedo))]
        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            var (undo, redo) = _redoStack.Pop();
            _undoStack.Push((undo, redo));
            _lastUndoEditTarget = null;
            _lastUndoEditField  = null;
            redo();
            NotifyUndoRedo();
        }

        // File write

        private async Task WriteToFileAsync(string path)
        {
            IsBusy        = true;
            StatusMessage = "Saving...";
            try
            {
                var snapshot  = Entries.Select(e => (e.HashId, e.KeyName, e.Content)).ToList();
                var tableName = TableName;
                await Task.Run(() => StrFileWriter.Write(path, tableName, snapshot));

                // If the file was extracted from a zip and we're saving to the same temp path,
                // write the updated .str back into the original zip entry.
                var activeFile = _activeFile;
                if (activeFile is { SourceZipPath: { } srcZip, ZipEntryName: { } entryName }
                    && string.Equals(path, activeFile.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Run(() =>
                    {
                        using var archive = ZipFile.Open(srcZip, ZipArchiveMode.Update);
                        string normalised = entryName.Replace(Path.DirectorySeparatorChar, '/');
                        (archive.GetEntry(normalised) ?? archive.GetEntry(entryName))?.Delete();
                        var newEntry = archive.CreateEntry(normalised, CompressionLevel.Optimal);
                        using var dst = newEntry.Open();
                        using var src = File.OpenRead(path);
                        src.CopyTo(dst);
                    });
                    IsModified    = false;
                    StatusMessage = $"Saved to zip: {Path.GetFileName(srcZip)}";
                }
                else
                {
                    IsModified    = false;
                    StatusMessage = $"Saved: {Path.GetFileName(path)}";
                }
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

        // Hash utility (public for code-behind dialog use)

        // Computes the 32-bit Forza localization hash for an ASCII key string.
        // Rolling XOR + ROL32-by-7 over each byte; empty string returns 0xFFFFFFFF.
        public static uint GetLocIDHash(string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0xFFFFFFFF;
            uint acc = Rol32(~(uint)s[0] & 0xFFFFFFFFu, 7);
            for (int i = 1; i < s.Length; i++)
                acc = Rol32(acc ^ (uint)s[i], 7);
            return acc;
        }

        private static uint Rol32(uint v, int n) { n &= 31; return (v << n) | (v >> (32 - n)); }
    }

    // Embedded .str file parser

    internal static class StrFileParser
    {
        public static StrTableData Parse(Stream stream)
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            // BLOBFILEHEADER (140 bytes)
            ushort version = r.ReadUInt16();
            if (version != 0x0400)
                throw new InvalidDataException($"Unsupported .str version 0x{version:X4}; expected 0x0400.");

            byte[] nameBytes = r.ReadBytes(128);
            nameBytes[127] = 0;
            string tableName = Encoding.ASCII.GetString(nameBytes, 0, IndexOfZero(nameBytes));

            ushort numSections = r.ReadUInt16();
            uint   secOffset0  = r.ReadUInt32();
            uint   secOffset1  = r.ReadUInt32();

            // Content section
            /*uint contentSize    =*/ r.ReadUInt32(); // SectionSize (informational)
            uint contentDataSz  = r.ReadUInt32();
            uint contentCount   = r.ReadUInt32();
            var  contentSyms    = ReadSymbols(r, contentCount);
            byte[] contentData  = r.ReadBytes((int)contentDataSz);
            var    contentMap   = DecodeStrings(contentSyms, contentData);

            // Names section (optional)
            Dictionary<uint, string>? nameMap = null;
            if (numSections >= 2 && stream.Position < stream.Length)
            {
                /*uint namesSize    =*/ r.ReadUInt32();
                uint namesDataSz  = r.ReadUInt32();
                uint namesCount   = r.ReadUInt32();
                var  namesSyms    = ReadSymbols(r, namesCount);
                byte[] namesData  = r.ReadBytes((int)namesDataSz);
                nameMap           = DecodeStrings(namesSyms, namesData);
            }

            // Build entry list
            var entries = new List<StrRawEntry>(contentMap.Count);
            foreach (var (hash, content) in contentMap)
            {
                string? keyName = null;
                nameMap?.TryGetValue(hash, out keyName);
                entries.Add(new StrRawEntry { HashId = hash, Content = content, KeyName = keyName });
            }

            return new StrTableData { TableName = tableName, Version = version, Entries = entries };
        }

        private static (uint hash, uint offset)[] ReadSymbols(BinaryReader r, uint count)
        {
            var syms = new (uint, uint)[count];
            for (int i = 0; i < count; i++)
                syms[i] = (r.ReadUInt32(), r.ReadUInt32());
            return syms;
        }

        private static Dictionary<uint, string> DecodeStrings((uint hash, uint offset)[] syms, byte[] data)
        {
            var map = new Dictionary<uint, string>(syms.Length);
            foreach (var (hash, off) in syms)
            {
                int end = (int)off;
                while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0))
                    end += 2;
                map[hash] = Encoding.Unicode.GetString(data, (int)off, end - (int)off);
            }
            return map;
        }

        private static int IndexOfZero(byte[] arr)
        {
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == 0) return i;
            return arr.Length;
        }
    }

    internal sealed class StrTableData
    {
        public string          TableName { get; set; } = string.Empty;
        public ushort          Version   { get; set; }
        public List<StrRawEntry> Entries { get; set; } = new();
    }

    internal sealed class StrRawEntry
    {
        public uint    HashId  { get; set; }
        public string  Content { get; set; } = string.Empty;
        public string? KeyName { get; set; }
    }

    // .str file writer

    internal static class StrFileWriter
    {
        public static void Write(string path, string tableName,
            IList<(uint HashId, string KeyName, string Content)> entries)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var w  = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false);
            WriteToStream(w, tableName, entries);
        }

        private static void WriteToStream(BinaryWriter w, string tableName,
            IList<(uint HashId, string KeyName, string Content)> entries)
        {
            bool hasNames = entries.Any(e => !string.IsNullOrEmpty(e.KeyName));

            // Build content data buffer
            var contentDataMs = new MemoryStream();
            var contentSyms   = new List<(uint hash, uint offset)>(entries.Count);
            using (var dw = new BinaryWriter(contentDataMs, Encoding.UTF8, leaveOpen: true))
            {
                foreach (var (hashId, _, content) in entries)
                {
                    contentSyms.Add((hashId, (uint)contentDataMs.Position));
                    dw.Write(Encoding.Unicode.GetBytes(content));
                    dw.Write((ushort)0); // UTF-16 LE null terminator
                }
            }
            byte[] contentData = contentDataMs.ToArray();

            // Build names data buffer
            byte[] namesData = Array.Empty<byte>();
            var    namesSyms = new List<(uint hash, uint offset)>(entries.Count);
            if (hasNames)
            {
                var namesDataMs = new MemoryStream();
                using (var dw = new BinaryWriter(namesDataMs, Encoding.UTF8, leaveOpen: true))
                {
                    foreach (var (hashId, keyName, _) in entries)
                    {
                        namesSyms.Add((hashId, (uint)namesDataMs.Position));
                        string name = keyName ?? string.Empty;
                        dw.Write(Encoding.Unicode.GetBytes(name));
                        dw.Write((ushort)0);
                    }
                }
                namesData = namesDataMs.ToArray();
            }

            // Compute offsets
            //   BLOBFILEHEADER = 140 bytes
            //   Section n total = 12 (SectionHeader) + 8*count (symbols) + dataLen
            const uint HeaderSize   = 140u;
            uint sec0SymSize   = (uint)(8 * contentSyms.Count);
            uint sec0TotalSize = 12u + sec0SymSize + (uint)contentData.Length;
            uint sec0Offset    = HeaderSize;
            uint sec1Offset    = HeaderSize + sec0TotalSize;

            // BLOBFILEHEADER
            w.Write((ushort)0x0400);                           // Version

            byte[] nameField = new byte[128];
            byte[] tableAscii = Encoding.ASCII.GetBytes(tableName);
            Array.Copy(tableAscii, nameField, Math.Min(tableAscii.Length, 127));
            w.Write(nameField);                                // TableName[128]

            w.Write((ushort)(hasNames ? 2 : 1));              // NumberOfSections
            w.Write(sec0Offset);                               // SectionOffset0
            w.Write(hasNames ? sec1Offset : 0u);              // SectionOffset1

            // Section 0: content strings
            w.Write(sec0TotalSize);                            // SectionSize
            w.Write((uint)contentData.Length);                 // DataSectionSize
            w.Write((uint)contentSyms.Count);                  // NumberOfEntries
            foreach (var (hash, off) in contentSyms) { w.Write(hash); w.Write(off); }
            w.Write(contentData);

            // Section 1: key names
            if (hasNames)
            {
                uint sec1SymSize   = (uint)(8 * namesSyms.Count);
                uint sec1TotalSize = 12u + sec1SymSize + (uint)namesData.Length;
                w.Write(sec1TotalSize);                        // SectionSize
                w.Write((uint)namesData.Length);               // DataSectionSize
                w.Write((uint)namesSyms.Count);                // NumberOfEntries
                foreach (var (hash, off) in namesSyms) { w.Write(hash); w.Write(off); }
                w.Write(namesData);
            }
        }
    }
}
