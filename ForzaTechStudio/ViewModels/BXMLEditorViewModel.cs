using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFormats;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.ViewModels
{
    public partial class BXMLEditorViewModel : ObservableObject
    {
        // file state

        private BXMLFile? _bxmlFile;

        // True when the loaded file was parsed as BXML binary (even if it carries
        // a .xml extension, which is normal for Forza game files).
        private bool _loadedAsBxml;

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

        private bool _isContentVisible;
        public bool IsContentVisible
        {
            get => _isContentVisible;
            set => SetProperty(ref _isContentVisible, value);
        }

        private string _statusMessage = "Waiting..";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // BXML data

        private string _xmlText = "";
        public string XmlText
        {
            get => _xmlText;
            set => SetProperty(ref _xmlText, value);
        }

        private BXMLNodeViewModel? _treeRoot;
        public BXMLNodeViewModel? TreeRoot
        {
            get => _treeRoot;
            set => SetProperty(ref _treeRoot, value);
        }

        private BXMLNodeViewModel? _selectedNode;
        public BXMLNodeViewModel? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetProperty(ref _selectedNode, value))
                {
                    OnPropertyChanged(nameof(IsNodeSelected));
                    OnPropertyChanged(nameof(IsNodeNotSelected));
                }
            }
        }

        public bool IsNodeSelected => SelectedNode != null;
        public bool IsNodeNotSelected => SelectedNode == null;

        // Open

        [RelayCommand]
        private async Task OpenBxmlAsync()
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".bxml");
            picker.FileTypeFilter.Add(".xml");
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            await LoadFileAsync(file.Path);
        }

        public async Task LoadFileAsync(string path)
        {
            IsBusy = true;
            IsContentVisible = false;
            StatusMessage = "Parsing...";
            TreeRoot = null;
            SelectedNode = null;
            _bxmlFile = null;
            _loadedAsBxml = false;

            // Exit ZIP mode
            IsZipMode = false;
            ZipEntries.Clear();
            _selectedZipEntry = null;
            _loadedZipPath = "";
            LoadedZipName = "";
            ZipEntryCount = 0;

            try
            {
                LoadedFilePath = path;
                LoadedFileName = Path.GetFileName(path);

                var (bxmlFile, xmlText, treeRoot, isBxml) = await Task.Run(() =>
                {
                    BXMLFile file;
                    bool parsedAsBxml;

                    // Forza uses .xml as the extension for BXML binary files.
                    // Read the first 4 bytes to check for the BXML magic before
                    // deciding how to parse — do not rely on extension alone.
                    if (HasBxmlMagic(path))
                    {
                        file = BXMLParser.FromFile(path);
                        parsedAsBxml = true;
                    }
                    else
                    {
                        // Plain XML — load and convert into the BXML in-memory model
                        // so the tree view works for both formats.
                        var doc = new XmlDocument();
                        doc.Load(path);
                        file = BXMLConverter.FromXmlDocument(doc);
                        parsedAsBxml = false;
                    }

                    string xml = BxmlFileToXmlString(file);
                    var root = BuildTreeViewModel(file.Root);
                    return (file, xml, root, parsedAsBxml);
                });

                _bxmlFile = bxmlFile;
                _loadedAsBxml = isBxml;
                XmlText = xmlText;
                TreeRoot = treeRoot;
                IsContentVisible = true;
                string kind = isBxml ? "BXML (binary)" : "XML";
                StatusMessage = $"Loaded {kind} \u2014 {LoadedFileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading file: {ex.Message}";
                IsContentVisible = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Save

        [RelayCommand]
        private async Task SaveAsync()
        {
            if (string.IsNullOrEmpty(LoadedFilePath))
            {
                await SaveAsBxmlAsync();
                return;
            }

            // Use the in-memory flag, not the file extension — Forza BXML files
            // carry a .xml extension but must be written as BXML binary.
            if (_loadedAsBxml)
                await WriteBxmlFileAsync(LoadedFilePath);
            else
                await WriteXmlFileAsync(LoadedFilePath);
        }

        [RelayCommand]
        private async Task SaveAsBxmlAsync()
        {
            // Forza stores BXML binary files with a .xml extension — match that.
            var path = await PickSavePathAsync(".xml", "Forza BXML File (*.xml)");
            if (path == null) return;
            await WriteBxmlFileAsync(path);
        }

        [RelayCommand]
        private async Task SaveAsXmlAsync()
        {
            var path = await PickSavePathAsync(".xml", "XML File");
            if (path == null) return;
            await WriteXmlFileAsync(path);
        }

        private async Task WriteBxmlFileAsync(string path)
        {
            IsBusy = true;
            StatusMessage = "Saving BXML\u2026";
            try
            {
                string xml = XmlText;
                await Task.Run(() =>
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(xml);
                    var bxmlFile = BXMLConverter.FromXmlDocument(doc);
                    BXMLWriter.ToFile(bxmlFile, path);
                });
                LoadedFilePath = path;
                LoadedFileName = Path.GetFileName(path);
                StatusMessage = $"Saved \u2014 {LoadedFileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task WriteXmlFileAsync(string path)
        {
            IsBusy = true;
            StatusMessage = "Saving XML\u2026";
            try
            {
                string xml = XmlText;
                await Task.Run(() => File.WriteAllText(path, xml, Encoding.UTF8));
                LoadedFilePath = path;
                LoadedFileName = Path.GetFileName(path);
                StatusMessage = $"Saved \u2014 {LoadedFileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // XML ↔ Tree sync

        [RelayCommand]
        private void ApplyXmlChanges()
        {
            if (string.IsNullOrWhiteSpace(XmlText)) return;
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(XmlText);
                _bxmlFile = BXMLConverter.FromXmlDocument(doc);
                TreeRoot = BuildTreeViewModel(_bxmlFile.Root);
                SelectedNode = null;
                StatusMessage = "XML applied \u2014 tree refreshed.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"XML parse error: {ex.Message}";
            }
        }

        [RelayCommand]
        private void SyncTreeToXml()
        {
            if (TreeRoot == null) return;
            try
            {
                XmlText = TreeViewModelToXmlString(TreeRoot);
                StatusMessage = "XML refreshed from tree edits.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Sync error: {ex.Message}";
            }
        }

        // Tree node editing

        [RelayCommand]
        private void AddAttribute()
        {
            if (SelectedNode == null) return;
            SelectedNode.Attributes.Add(new BXMLAttributeViewModel("attr", "value"));
        }

        [RelayCommand]
        private void RemoveAttribute(BXMLAttributeViewModel attr)
        {
            if (SelectedNode == null) return;
            SelectedNode.Attributes.Remove(attr);
        }

        // Close

        [RelayCommand]
        private void CloseFile()
        {
            _bxmlFile = null;
            _loadedAsBxml = false;
            TreeRoot = null;
            SelectedNode = null;
            XmlText = "";
            LoadedFilePath = "";
            LoadedFileName = "";
            IsContentVisible = false;
            StatusMessage = "File closed.";

            // Reset ZIP state
            IsZipMode = false;
            ZipEntries.Clear();
            _selectedZipEntry = null;
            _loadedZipPath = "";
            LoadedZipName = "";
            ZipEntryCount = 0;
        }

        // ZIP support

        private string _loadedZipPath = "";

        private string _loadedZipName = "";
        public string LoadedZipName
        {
            get => _loadedZipName;
            set => SetProperty(ref _loadedZipName, value);
        }

        private bool _isZipMode;
        public bool IsZipMode
        {
            get => _isZipMode;
            set
            {
                if (SetProperty(ref _isZipMode, value))
                    OnPropertyChanged(nameof(IsNotZipMode));
            }
        }
        public bool IsNotZipMode => !IsZipMode;

        private int _zipEntryCount;
        public int ZipEntryCount
        {
            get => _zipEntryCount;
            private set
            {
                if (SetProperty(ref _zipEntryCount, value))
                    OnPropertyChanged(nameof(ZipEntryCountText));
            }
        }
        public string ZipEntryCountText => ZipEntryCount == 1 ? "(1 file)" : $"({ZipEntryCount} files)";

        public ObservableCollection<ZipEntryViewModel> ZipEntries { get; } = new();

        private ZipEntryViewModel? _selectedZipEntry;
        public ZipEntryViewModel? SelectedZipEntry
        {
            get => _selectedZipEntry;
            set
            {
                if (SetProperty(ref _selectedZipEntry, value) && value != null)
                    SelectZipEntry(value);
            }
        }

        [RelayCommand]
        private async Task OpenZipAsync()
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".zip");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            await LoadZipAsync(file.Path);
        }

        public async Task LoadZipAsync(string zipPath)
        {
            IsBusy = true;
            IsContentVisible = false;
            StatusMessage = "Opening ZIP\u2026";
            ZipEntries.Clear();
            IsZipMode = false;
            TreeRoot = null;
            SelectedNode = null;
            _bxmlFile = null;
            _loadedAsBxml = false;
            _loadedZipPath = zipPath;
            LoadedZipName = Path.GetFileName(zipPath);

            try
            {
                var entries = await Task.Run(() =>
                {
                    var result = new List<ZipEntryViewModel>();
                    using var archive = ZipFile.OpenRead(zipPath);
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.FullName.EndsWith(".bxml", StringComparison.OrdinalIgnoreCase) &&
                            !entry.FullName.EndsWith(".xml",  StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            using var entryStream = entry.Open();
                            using var ms = new MemoryStream();
                            entryStream.CopyTo(ms);
                            byte[] data = ms.ToArray();

                            bool isBxml = HasBxmlMagicBytes(data);
                            BXMLFile bxmlFile;
                            if (isBxml)
                            {
                                using var parseStream = new MemoryStream(data);
                                bxmlFile = BXMLParser.FromStream(parseStream);
                            }
                            else
                            {
                                var doc = new XmlDocument();
                                using var xmlStream = new MemoryStream(data);
                                doc.Load(xmlStream);
                                bxmlFile = BXMLConverter.FromXmlDocument(doc);
                            }

                            string xmlText = BxmlFileToXmlString(bxmlFile);
                            result.Add(new ZipEntryViewModel(entry.FullName, bxmlFile, xmlText, isBxml));
                        }
                        catch
                        {
                            // Skip entries that cannot be parsed
                        }
                    }
                    return result;
                });

                if (entries.Count == 0)
                {
                    StatusMessage = "No parseable BXML/XML files found in ZIP.";
                    IsBusy = false;
                    return;
                }

                foreach (var e in entries)
                    ZipEntries.Add(e);

                ZipEntryCount = entries.Count;
                IsZipMode = true;
                LoadedFilePath = "";
                LoadedFileName = LoadedZipName;
                IsContentVisible = true;
                StatusMessage = $"ZIP loaded \u2014 {entries.Count} file(s) \u2014 {LoadedZipName}";

                // Auto-select first entry (triggers SelectZipEntry via setter)
                SelectedZipEntry = entries[0];
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening ZIP: {ex.Message}";
                IsContentVisible = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void SelectZipEntry(ZipEntryViewModel entry)
        {
            _bxmlFile     = entry.BxmlFile;
            _loadedAsBxml = entry.IsBxml;
            XmlText       = entry.XmlText;
            TreeRoot      = BuildTreeViewModel(entry.BxmlFile.Root);
            SelectedNode  = null;
            LoadedFilePath = "";
            LoadedFileName = entry.DisplayName;
            StatusMessage  = $"Viewing: {entry.DisplayName}";
        }

        private static bool HasBxmlMagicBytes(byte[] data) =>
            data.Length >= 4 && data[0] == 0x42 && data[1] == 0x58 && data[2] == 0x4D && data[3] == 0x4C;

        // Helpers

        private static string BxmlFileToXmlString(BXMLFile file)
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = false,
                Encoding = Encoding.UTF8,
            };
            using var sw = new StringWriter(sb);
            using var writer = XmlWriter.Create(sw, settings);
            WriteXmlNode(writer, file.Root);
            writer.Flush();
            return sb.ToString();
        }

        private static void WriteXmlNode(XmlWriter w, BXMLNode node)
        {
            w.WriteStartElement(node.Name);
            foreach (var attr in node.Attributes)
                w.WriteAttributeString(attr.Name, attr.Value);
            foreach (var child in node.Children)
                WriteXmlNode(w, child);
            w.WriteEndElement();
        }

        private static BXMLNodeViewModel BuildTreeViewModel(BXMLNode node)
        {
            var vm = new BXMLNodeViewModel(node.Name);
            foreach (var attr in node.Attributes)
                vm.Attributes.Add(new BXMLAttributeViewModel(attr.Name, attr.Value));
            foreach (var child in node.Children)
                vm.Children.Add(BuildTreeViewModel(child));
            return vm;
        }

        private static string TreeViewModelToXmlString(BXMLNodeViewModel root)
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = false,
                Encoding = Encoding.UTF8,
            };
            using var sw = new StringWriter(sb);
            using var writer = XmlWriter.Create(sw, settings);
            WriteTreeNodeXml(writer, root);
            writer.Flush();
            return sb.ToString();
        }

        private static void WriteTreeNodeXml(XmlWriter w, BXMLNodeViewModel vm)
        {
            w.WriteStartElement(vm.Name);
            foreach (var attr in vm.Attributes)
                w.WriteAttributeString(attr.Name, attr.Value);
            foreach (var child in vm.Children)
                WriteTreeNodeXml(w, child);
            w.WriteEndElement();
        }

        // Returns true if the file starts with the BXML magic bytes. Returns false on short files or I/O errors.
        private static bool HasBxmlMagic(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4, false);
                Span<byte> buf = stackalloc byte[4];
                if (fs.Read(buf) < 4) return false;
                // BXML magic little-endian: bytes 42 58 4D 4C
                return buf[0] == 0x42 && buf[1] == 0x58 && buf[2] == 0x4D && buf[3] == 0x4C;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string?> PickSavePathAsync(string extension, string description)
        {
            var picker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeChoices.Add(description, new List<string> { extension });
            if (!string.IsNullOrEmpty(LoadedFileName))
                picker.SuggestedFileName = Path.GetFileNameWithoutExtension(LoadedFileName);
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }
    }

    // BXMLNodeViewModel

    public partial class BXMLNodeViewModel : ObservableObject
    {
        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public ObservableCollection<BXMLAttributeViewModel> Attributes { get; } = new();
        public List<BXMLNodeViewModel> Children { get; } = new();

        public string SubtextInfo
        {
            get
            {
                var attrPart = Attributes.Count == 1 ? "1 attr" : $"{Attributes.Count} attrs";
                if (Children.Count == 0) return attrPart;
                var childPart = Children.Count == 1 ? "1 child" : $"{Children.Count} children";
                return $"{attrPart} \u00b7 {childPart}";
            }
        }

        public BXMLNodeViewModel(string name) { _name = name; }
    }

    // BXMLAttributeViewModel

    public partial class BXMLAttributeViewModel : ObservableObject
    {
        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _value;
        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        public BXMLAttributeViewModel(string name, string value)
        {
            _name  = name;
            _value = value;
        }
    }

    // ZipEntryViewModel — holds one parsed entry from a loaded ZIP archive

    public sealed class ZipEntryViewModel
    {
        /// <summary>Full path of the entry inside the ZIP (e.g. "data/cars/car.xml").</summary>
        public string FullName { get; }

        /// <summary>Just the filename portion, displayed in the picker list.</summary>
        public string DisplayName { get; }

        public BXMLFile BxmlFile { get; }
        public string   XmlText  { get; }
        public bool     IsBxml   { get; }

        public ZipEntryViewModel(string fullName, BXMLFile bxmlFile, string xmlText, bool isBxml)
        {
            FullName    = fullName;
            DisplayName = Path.GetFileName(fullName);
            BxmlFile    = bxmlFile;
            XmlText     = xmlText;
            IsBxml      = isBxml;
        }
    }
}
