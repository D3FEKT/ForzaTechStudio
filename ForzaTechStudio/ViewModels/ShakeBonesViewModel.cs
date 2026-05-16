using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTechStudio.Models;
using ForzaTechStudio.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Storage.Pickers;

namespace ForzaTechStudio.ViewModels
{
    // Supported car-scene XML file types
    public enum CarSceneXmlType
    {
        Unknown,
        ShakeBones,          // root = <ShakeBoneSettings>
        Locators,            // root = <CarLocators>
        CarAttributes,       // root = <CarAttributes>  (per-car)
        GlobalCarAttributes, // root = <GlobalCarAttributes>  (global library)
        IKAnchorBones,       // root = <IKAnchorBones>
        AvPins,              // root = <PointsOfInterest>  / *.avpins extension
    }

    public partial class ShakeBonesViewModel : UndoRedoViewModel
    {
        // Sub-ViewModels for each supported format
        public LocatorsViewModel         Locators      { get; } = new();

        public CarAttributesViewModel    CarAttr       { get; } = new();
        public GlobalCarAttributesViewModel GlobalCarAttr { get; } = new();
        public IKAnchorBonesViewModel    IKAnchorBones { get; } = new();
        public AvPinsViewModel           AvPins        { get; } = new();

        // File-type mode booleans (drive XAML panel visibility)
        public bool IsShakeBonesMode       => DetectedFileType == CarSceneXmlType.ShakeBones;
        public bool IsLocatorsMode         => DetectedFileType == CarSceneXmlType.Locators;
        public bool IsCarAttributesMode    => DetectedFileType == CarSceneXmlType.CarAttributes;
        public bool IsGlobalCarAttrMode    => DetectedFileType == CarSceneXmlType.GlobalCarAttributes;
        public bool IsIKAnchorBonesMode    => DetectedFileType == CarSceneXmlType.IKAnchorBones;
        public bool IsAvPinsMode           => DetectedFileType == CarSceneXmlType.AvPins;

        private CarSceneXmlType _detectedFileType = CarSceneXmlType.Unknown;
        public CarSceneXmlType DetectedFileType
        {
            get => _detectedFileType;
            private set
            {
                if (SetProperty(ref _detectedFileType, value))
                {
                    OnPropertyChanged(nameof(IsShakeBonesMode));
                    OnPropertyChanged(nameof(IsLocatorsMode));
                    OnPropertyChanged(nameof(IsCarAttributesMode));
                    OnPropertyChanged(nameof(IsGlobalCarAttrMode));
                    OnPropertyChanged(nameof(IsIKAnchorBonesMode));
                    OnPropertyChanged(nameof(IsAvPinsMode));
                }
            }
        }

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

        // data

        public ObservableCollection<ShakeBonesCamera> Cameras { get; } = new();

        private string _xmlText = "";
        public string XmlText
        {
            get => _xmlText;
            set => SetProperty(ref _xmlText, value);
        }

        private string? _xmlError;
        public string? XmlError
        {
            get => _xmlError;
            set => SetProperty(ref _xmlError, value);
        }

        private bool _useVersion2;
        public bool UseVersion2
        {
            get => _useVersion2;
            set
            {
                if (SetProperty(ref _useVersion2, value))
                    RefreshXmlText();
            }
        }

        // selection

        private ShakeBonesCamera? _selectedCamera;
        public ShakeBonesCamera? SelectedCamera
        {
            get => _selectedCamera;
            set
            {
                if (SetProperty(ref _selectedCamera, value))
                {
                    SelectedBone = null;
                    OnPropertyChanged(nameof(IsCameraSelected));
                    OnPropertyChanged(nameof(IsBoneSelected));
                    AddBoneCommand.NotifyCanExecuteChanged();
                    DeleteCameraCommand.NotifyCanExecuteChanged();
                }
            }
        }

        private ShakeBonesbone? _selectedBone;
        public ShakeBonesbone? SelectedBone
        {
            get => _selectedBone;
            set
            {
                if (SetProperty(ref _selectedBone, value))
                {
                    SelectedTransform = null;
                    OnPropertyChanged(nameof(IsBoneSelected));
                    OnPropertyChanged(nameof(IsTransformSelected));
                    AddTransformCommand.NotifyCanExecuteChanged();
                    DeleteBoneCommand.NotifyCanExecuteChanged();
                }
            }
        }

        private ShakeBonesTransform? _selectedTransform;
        public ShakeBonesTransform? SelectedTransform
        {
            get => _selectedTransform;
            set
            {
                if (SetProperty(ref _selectedTransform, value))
                {
                    OnPropertyChanged(nameof(IsTransformSelected));
                    DeleteTransformCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsCameraSelected => SelectedCamera != null;
        public bool IsBoneSelected => SelectedBone != null;
        public bool IsTransformSelected => SelectedTransform != null;

        // window handle (needed for file pickers)

        private IntPtr _hwnd;
        public void SetWindowHandle(IntPtr hwnd) => _hwnd = hwnd;

        // Open

        [RelayCommand]
        private async Task OpenFileAsync()
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            picker.FileTypeFilter.Add(".xml");
            picker.FileTypeFilter.Add(".avpins");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            await LoadFileAsync(file.Path);
        }

        public async Task LoadFileAsync(string path)
        {
            IsBusy = true;
            StatusMessage = "Loading...";
            try
            {
                var text = await Task.Run(() => File.ReadAllText(path));

                // .avpins files are always AvPins regardless of root element name
                CarSceneXmlType type;
                if (Path.GetExtension(path).Equals(".avpins", StringComparison.OrdinalIgnoreCase))
                    type = CarSceneXmlType.AvPins;
                else
                    type = DetectFileType(text, path);

                if (type == CarSceneXmlType.Unknown)
                {
                    StatusMessage = "Unsupported file. Expected one of: ShakeBones.xml, Locators.xml, " +
                                    "CarAttributes.xml, GlobalCarAttributes.xml, IKAnchorBones.xml, *.avpins";
                    return;
                }

                DetectedFileType = type;

                switch (type)
                {
                    case CarSceneXmlType.ShakeBones:
                        ApplyParsedData(text, path);
                        break;

                    case CarSceneXmlType.Locators:
                        var locParser = new LocatorsXmlParser();
                        Locators.Load(locParser.ParseFromString(text, path));
                        LoadedFilePath   = path;
                        LoadedFileName   = Path.GetFileName(path);
                        StatusMessage    = $"{Locators.Locators.Count} locator(s) loaded";
                        XmlText          = text;
                        IsContentVisible = true;
                        break;

                    case CarSceneXmlType.CarAttributes:
                        CarAttr.Load(CarAttributesParser.Parse(text));
                        LoadedFilePath   = path;
                        LoadedFileName   = Path.GetFileName(path);
                        StatusMessage    = "CarAttributes loaded";
                        XmlText          = CarAttr.Serialize();
                        IsContentVisible = true;
                        break;

                    case CarSceneXmlType.GlobalCarAttributes:
                        GlobalCarAttr.Load(GlobalCarAttributesParser.Parse(text));
                        LoadedFilePath   = path;
                        LoadedFileName   = Path.GetFileName(path);
                        StatusMessage    = $"{GlobalCarAttr.Data?.Sections.Count ?? 0} section(s) loaded";
                        XmlText          = text;
                        IsContentVisible = true;
                        break;

                    case CarSceneXmlType.IKAnchorBones:
                        IKAnchorBones.Load(IKAnchorBonesParser.Parse(text));
                        LoadedFilePath   = path;
                        LoadedFileName   = Path.GetFileName(path);
                        StatusMessage    = $"{IKAnchorBones.Data?.Bones.Count ?? 0} bone(s) loaded";
                        XmlText          = IKAnchorBones.Serialize();
                        IsContentVisible = true;
                        break;

                    case CarSceneXmlType.AvPins:
                        AvPins.Load(AvPinsParser.Parse(text));
                        LoadedFilePath   = path;
                        LoadedFileName   = Path.GetFileName(path);
                        StatusMessage    = $"{AvPins.Data?.POIs.Count ?? 0} POI(s) loaded";
                        XmlText          = AvPins.Serialize();
                        IsContentVisible = true;
                        break;
                }
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

        // File-type detection

        // Peeks at the XML root element to determine which car-scene XML type this is.
        // Falls back to sanitizing malformed attribute values, then to a filename hint.
        private static CarSceneXmlType DetectFileType(string xmlText, string filePath = "")
        {
            // First try: raw parse (fast path for well-formed XML)
            var type = TryParseRootName(xmlText);
            if (type != CarSceneXmlType.Unknown)
                return type;

            // Second try: sanitise bare angle-brackets inside attribute values
            // (Forza locators.xml uses BoneName="<root>" which is malformed XML)
            try
            {
                string sanitized = SanitizeAttributeValues(xmlText);
                type = TryParseRootName(sanitized);
                if (type != CarSceneXmlType.Unknown)
                    return type;
            }
            catch { /* ignore */ }

            // Third try: filename-based hint for completely unparseable documents
            if (!string.IsNullOrEmpty(filePath))
            {
                string name = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
                if (name.Contains("locator"))             return CarSceneXmlType.Locators;
                if (name.Contains("globalcarattr"))       return CarSceneXmlType.GlobalCarAttributes;
                if (name.Contains("carattrib"))           return CarSceneXmlType.CarAttributes;
                if (name.Contains("ikanchor") ||
                    name.Contains("anchorbone"))          return CarSceneXmlType.IKAnchorBones;
                if (name.Contains("shakebones") ||
                    name.Contains("shake_bone"))          return CarSceneXmlType.ShakeBones;
            }

            return CarSceneXmlType.Unknown;
        }

        private static CarSceneXmlType TryParseRootName(string xmlText)
        {
            try
            {
                var root = XDocument.Parse(xmlText).Root;
                if (root == null) return CarSceneXmlType.Unknown;

                return root.Name.LocalName switch
                {
                    "ShakeBoneSettings"   => CarSceneXmlType.ShakeBones,
                    "CarLocators"         => CarSceneXmlType.Locators,
                    "CarAttributes"       => CarSceneXmlType.CarAttributes,
                    "GlobalCarAttributes" => CarSceneXmlType.GlobalCarAttributes,
                    "IKAnchorBones"       => CarSceneXmlType.IKAnchorBones,
                    "PointsOfInterest"    => CarSceneXmlType.AvPins,
                    _                    => CarSceneXmlType.Unknown
                };
            }
            catch
            {
                return CarSceneXmlType.Unknown;
            }
        }

        // Replaces bare <c>&lt;</c>/<c>&gt;</c> characters inside XML attribute values
        // with their entity equivalents so that XDocument can parse the file.
        private static readonly System.Text.RegularExpressions.Regex _attrValueRegex =
            new(@"=""([^""]*)""", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string SanitizeAttributeValues(string xml)
        {
            return _attrValueRegex.Replace(xml, m =>
            {
                string content = m.Groups[1].Value;
                if (content.Contains('<') || content.Contains('>'))
                {
                    content = content.Replace("<", "&lt;").Replace(">", "&gt;");
                }
                return $"=\"{content}\"";
            });
        }

        private void ApplyParsedData(string xmlText, string filePath)
        {
            var parsed = ShakeBonesParser.Parse(xmlText, out int detectedVersion);
            UseVersion2 = detectedVersion == 2;

            Cameras.Clear();
            foreach (var c in parsed)
                Cameras.Add(c);

            XmlText = ShakeBonesParser.Serialize(Cameras, UseVersion2);
            LoadedFilePath = filePath;
            LoadedFileName = Path.GetFileName(filePath);
            StatusMessage = $"{Cameras.Count} camera group(s) loaded";
            IsContentVisible = true;
        }

        // Save

        [RelayCommand(CanExecute = nameof(IsNotBusy))]
        private async Task SaveAsync()
        {
            if (string.IsNullOrEmpty(LoadedFilePath)) { await SaveAsAsync(); return; }
            await WriteToPath(LoadedFilePath);
        }

        [RelayCommand(CanExecute = nameof(IsNotBusy))]
        private async Task SaveAsAsync()
        {
            var picker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

            if (DetectedFileType == CarSceneXmlType.AvPins)
                picker.FileTypeChoices.Add("Autovista Pins File", new System.Collections.Generic.List<string> { ".avpins" });
            else
                picker.FileTypeChoices.Add("XML File", new System.Collections.Generic.List<string> { ".xml" });

            picker.SuggestedFileName = string.IsNullOrEmpty(LoadedFileName) ? "output" : LoadedFileName;

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            LoadedFilePath = file.Path;
            LoadedFileName = file.Name;
            await WriteToPath(file.Path);
        }

        private async Task WriteToPath(string path)
        {
            IsBusy = true;
            StatusMessage = "Saving...";
            try
            {
                string xml = DetectedFileType switch
                {
                    CarSceneXmlType.ShakeBones         => ShakeBonesParser.Serialize(Cameras, UseVersion2),
                    CarSceneXmlType.Locators           => Locators.Serialize(),
                    CarSceneXmlType.CarAttributes      => CarAttr.Serialize(),
                    CarSceneXmlType.GlobalCarAttributes => GlobalCarAttr.Serialize(),
                    CarSceneXmlType.IKAnchorBones      => IKAnchorBones.Serialize(),
                    CarSceneXmlType.AvPins             => AvPins.Serialize(),
                    _                                  => XmlText,
                };
                await Task.Run(() => File.WriteAllText(path, xml, new System.Text.UTF8Encoding(false)));
                XmlText = xml;
                StatusMessage = "Saved.";
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

        // Close

        [RelayCommand]
        private void CloseFile()
        {
            Cameras.Clear();
            Locators.Clear();
            CarAttr.Clear();
            GlobalCarAttr.Clear();
            IKAnchorBones.Clear();
            AvPins.Clear();
            XmlText          = "";
            LoadedFilePath   = "";
            LoadedFileName   = "";
            StatusMessage    = "Waiting..";
            IsContentVisible = false;
            DetectedFileType = CarSceneXmlType.Unknown;
            SelectedCamera   = null;
            SelectedBone     = null;
            SelectedTransform = null;
        }

        // Camera CRUD

        [RelayCommand]
        private void AddCamera()
        {
            var cam = new ShakeBonesCamera("NewCamera");
            Cameras.Add(cam);
            SelectedCamera = cam;
            RefreshXmlText();
            PushUndo(
                () => { Cameras.Remove(cam); SelectedCamera = null; RefreshXmlText(); },
                () => { Cameras.Add(cam); SelectedCamera = cam; RefreshXmlText(); });
        }

        [RelayCommand(CanExecute = nameof(IsCameraSelected))]
        private void DeleteCamera()
        {
            if (SelectedCamera == null) return;
            var cam = SelectedCamera;
            var idx = Cameras.IndexOf(cam);
            Cameras.Remove(cam);
            SelectedCamera = null;
            RefreshXmlText();
            PushUndo(
                () => { Cameras.Insert(Math.Min(idx, Cameras.Count), cam); SelectedCamera = cam; RefreshXmlText(); },
                () => { Cameras.Remove(cam); SelectedCamera = null; RefreshXmlText(); });
        }

        // Bone CRUD

        [RelayCommand(CanExecute = nameof(IsCameraSelected))]
        private void AddBone()
        {
            if (SelectedCamera == null) return;
            var cam = SelectedCamera;
            var bone = new ShakeBonesbone("bone_name", 0, 0, 0.05f, 0.03f);
            cam.Bones.Add(bone);
            SelectedBone = bone;
            RefreshXmlText();
            PushUndo(
                () => { cam.Bones.Remove(bone); SelectedBone = null; RefreshXmlText(); },
                () => { cam.Bones.Add(bone); SelectedBone = bone; RefreshXmlText(); });
        }

        [RelayCommand(CanExecute = nameof(IsBoneSelected))]
        private void DeleteBone()
        {
            if (SelectedCamera == null || SelectedBone == null) return;
            var cam = SelectedCamera;
            var bone = SelectedBone;
            var idx = cam.Bones.IndexOf(bone);
            cam.Bones.Remove(bone);
            SelectedBone = null;
            RefreshXmlText();
            PushUndo(
                () => { cam.Bones.Insert(Math.Min(idx, cam.Bones.Count), bone); SelectedBone = bone; RefreshXmlText(); },
                () => { cam.Bones.Remove(bone); SelectedBone = null; RefreshXmlText(); });
        }

        // Transform CRUD

        [RelayCommand(CanExecute = nameof(IsBoneSelected))]
        private void AddTransform()
        {
            if (SelectedBone == null) return;
            var bone = SelectedBone;
            var xf = new ShakeBonesTransform("translateX", -0.005, 0.005);
            bone.Transforms.Add(xf);
            SelectedTransform = xf;
            RefreshXmlText();
            PushUndo(
                () => { bone.Transforms.Remove(xf); SelectedTransform = null; RefreshXmlText(); },
                () => { bone.Transforms.Add(xf); SelectedTransform = xf; RefreshXmlText(); });
        }

        [RelayCommand(CanExecute = nameof(IsTransformSelected))]
        private void DeleteTransform()
        {
            if (SelectedBone == null || SelectedTransform == null) return;
            var bone = SelectedBone;
            var xf = SelectedTransform;
            var idx = bone.Transforms.IndexOf(xf);
            bone.Transforms.Remove(xf);
            SelectedTransform = null;
            RefreshXmlText();
            PushUndo(
                () => { bone.Transforms.Insert(Math.Min(idx, bone.Transforms.Count), xf); SelectedTransform = xf; RefreshXmlText(); },
                () => { bone.Transforms.Remove(xf); SelectedTransform = null; RefreshXmlText(); });
        }

        // Refresh XML text

        public void RefreshXmlText()
        {
            XmlText = DetectedFileType switch
            {
                CarSceneXmlType.ShakeBones         => ShakeBonesParser.Serialize(Cameras, UseVersion2),
                CarSceneXmlType.Locators           => Locators.Serialize(),
                CarSceneXmlType.CarAttributes      => CarAttr.Serialize(),
                CarSceneXmlType.GlobalCarAttributes => GlobalCarAttr.Serialize(),
                CarSceneXmlType.IKAnchorBones      => IKAnchorBones.Serialize(),
                CarSceneXmlType.AvPins             => AvPins.Serialize(),
                _                                  => XmlText,
            };
        }

        // Remove helpers (called from code-behind inline controls)

        public void RemoveTransform(ShakeBonesTransform xf)
        {
            if (SelectedBone == null) return;
            SelectedBone.Transforms.Remove(xf);
            if (SelectedTransform == xf) SelectedTransform = null;
            RefreshXmlText();
        }

        // Apply raw XML edits back to the model

        // Parses the supplied XML text into the Cameras collection.
        // Returns null on success, or an error message string on failure.
        public string? ApplyXmlFromText(string xmlText)
        {
            switch (DetectedFileType)
            {
                case CarSceneXmlType.ShakeBones:
                    try
                    {
                        var parsed = ShakeBonesParser.Parse(xmlText, out int detectedVersion);
                        UseVersion2 = detectedVersion == 2;
                        Cameras.Clear();
                        foreach (var c in parsed) Cameras.Add(c);
                        SelectedCamera    = null;
                        SelectedBone      = null;
                        SelectedTransform = null;
                        XmlText           = ShakeBonesParser.Serialize(Cameras, UseVersion2);
                        StatusMessage     = $"Applied. {Cameras.Count} camera group(s).";
                        XmlError          = null;
                        return null;
                    }
                    catch (Exception ex)
                    {
                        XmlError = $"Parse error: {ex.Message}";
                        return XmlError;
                    }

                case CarSceneXmlType.Locators:
                    var lerr = Locators.ApplyFromText(xmlText);
                    if (lerr == null) { XmlError = null; XmlText = Locators.Serialize(); } else XmlError = lerr;
                    return lerr;

                case CarSceneXmlType.CarAttributes:
                    var err = CarAttr.ApplyFromText(xmlText);
                    if (err == null) { XmlError = null; XmlText = CarAttr.Serialize(); } else XmlError = err;
                    return err;

                case CarSceneXmlType.GlobalCarAttributes:
                    var gcerr = GlobalCarAttr.ApplyFromText(xmlText);
                    if (gcerr == null) { XmlError = null; XmlText = GlobalCarAttr.Serialize(); } else XmlError = gcerr;
                    return gcerr;

                case CarSceneXmlType.IKAnchorBones:
                    var ikerr = IKAnchorBones.ApplyFromText(xmlText);
                    if (ikerr == null) { XmlError = null; XmlText = IKAnchorBones.Serialize(); } else XmlError = ikerr;
                    return ikerr;

                case CarSceneXmlType.AvPins:
                    var averr = AvPins.ApplyFromText(xmlText);
                    if (averr == null) { XmlError = null; XmlText = AvPins.Serialize(); } else XmlError = averr;
                    return averr;

                default:
                    return null;
            }
        }
    }
}
