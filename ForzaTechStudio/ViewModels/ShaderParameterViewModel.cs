using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForzaTools.Bundles.Blobs;
using ForzaTechStudio.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.UI;

namespace ForzaTechStudio.ViewModels
{
    public partial class ShaderParameterViewModel : ObservableObject
    {
        private readonly ShaderParameter _parameter;

        public ShaderParameterViewModel(ShaderParameter parameter)
        {
            _parameter = parameter;
            UpdateName();
            DetermineCategory();
        }

        [ObservableProperty]
        private string _name;
        
        [ObservableProperty]
        private string _category;

        [ObservableProperty]
        private bool _isColor;

        public ShaderParameter Parameter => _parameter;
        public ShaderParameterType Type => _parameter.Type;

        public uint NameHash => _parameter.NameHash;

        private void UpdateName()
        {
            var knownName = NameHashService.Instance.GetName(_parameter.NameHash);
            if (!string.IsNullOrEmpty(knownName))
            {
                Name = knownName;
            }
            else
            {
                Name = $"Unknown Hash (0x{_parameter.NameHash:X8})";
            }
        }

        private void DetermineCategory()
        {
            if (_parameter.Type == ShaderParameterType.Texture2D || _parameter.Type == ShaderParameterType.Sampler)
                Category = "Textures & Samplers";
            else if (_parameter.Type == ShaderParameterType.Bool)
                Category = "Settings";
            else if (_parameter.Type == ShaderParameterType.Float || _parameter.Type == ShaderParameterType.Int)
                Category = "Scalars";
            else if (_parameter.Type == ShaderParameterType.Color || IsNameColor(Name))
            {
                Category = "Colors";
                IsColor = true;
            }
            else
                Category = "Vectors";
        }

        private bool IsNameColor(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLower();
            return lower.Contains("color") || lower.Contains("tint") || lower.Contains("f0");
        }

        [RelayCommand]
        public async Task BrowseTextureAsync()
        {
            var picker = new FileOpenPicker();
            
            // Get window handle
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".dds");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".tga");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                // Just use the filename for now, as game paths are virtual
                TexturePath = file.Name;
            }
        }

        // Typed Value Accessors

        public bool BoolValue
        {
            get => _parameter.Value is bool b ? b : false;
            set
            {
                if (_parameter.Value is bool old && old == value) return;
                _parameter.Value = value;
                OnPropertyChanged();
            }
        }

        public float FloatValue
        {
            get => _parameter.Value is float f ? f : 0f;
            set
            {
                 if (_parameter.Value is float old && Math.Abs(old - value) < 0.0001f) return;
                _parameter.Value = value;
                OnPropertyChanged();
            }
        }

        public int IntValue
        {
            get => _parameter.Value is int i ? i : 0;
            set
            {
                if (_parameter.Value is int old && old == value) return;
                _parameter.Value = value;
                OnPropertyChanged();
            }
        }

        public string TexturePath
        {
            get
            {
                if (_parameter.Value is TextureParameter tp) return tp.Path;
                return string.Empty;
            }
            set
            {
                 if (_parameter.Value is TextureParameter tp)
                 {
                     if (tp.Path != value)
                     {
                         tp.Path = value;
                         OnPropertyChanged();
                     }
                 }
                 else
                 {
                     // upgrade if null?
                     _parameter.Value = new TextureParameter { Path = value };
                     OnPropertyChanged();
                 }
            }
        }

        // Vector4 components
        public float VectorX
        {
            get => GetVectorComponent(0);
            set => SetVectorComponent(0, value);
        }
        public float VectorY
        {
            get => GetVectorComponent(1);
            set => SetVectorComponent(1, value);
        }
        public float VectorZ
        {
            get => GetVectorComponent(2);
            set => SetVectorComponent(2, value);
        }
        public float VectorW
        {
            get => GetVectorComponent(3);
            set => SetVectorComponent(3, value);
        }

        // Color Handling
        public Color CurrentColor
        {
            get
            {
                if (_parameter.Value is Vector4 v)
                {
                    // Assume linear float in Vector4, convert to sRGB Color
                    return Color.FromArgb(
                        (byte)Clamp(v.W * 255f, 0, 255),
                        (byte)Clamp(LinearToSrgb(v.X) * 255f, 0, 255),
                        (byte)Clamp(LinearToSrgb(v.Y) * 255f, 0, 255),
                        (byte)Clamp(LinearToSrgb(v.Z) * 255f, 0, 255)
                    );
                }
                return Color.FromArgb(255, 255, 255, 255);
            }
            set
            {
                // UI sets sRGB Color, convert back to Linear Vector4
                var v = new Vector4(
                    SrgbToLinear(value.R / 255f),
                    SrgbToLinear(value.G / 255f),
                    SrgbToLinear(value.B / 255f),
                    value.A / 255f
                );
                
                if (!(_parameter.Value is Vector4 old) || old != v)
                {
                    _parameter.Value = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(VectorX));
                    OnPropertyChanged(nameof(VectorY));
                    OnPropertyChanged(nameof(VectorZ));
                    OnPropertyChanged(nameof(VectorW));
                }
            }
        }

        // Sampler
        public int AddressU
        {
            get => _parameter.Value is SamplerParameter sp ? sp.AddressU : 0;
            set
            {
                if (_parameter.Value is SamplerParameter sp && sp.AddressU != value)
                {
                    sp.AddressU = value;
                    OnPropertyChanged();
                }
            }
        }

        public int AddressV
        {
            get => _parameter.Value is SamplerParameter sp ? sp.AddressV : 0;
            set
            {
                if (_parameter.Value is SamplerParameter sp && sp.AddressV != value)
                {
                    sp.AddressV = value;
                    OnPropertyChanged();
                }
            }
        }

        private float GetVectorComponent(int index)
        {
             if (_parameter.Value is Vector4 v)
             {
                 return index switch { 0 => v.X, 1 => v.Y, 2 => v.Z, 3 => v.W, _ => 0 };
             }
             if (_parameter.Value is Vector2 v2)
             {
                 return index switch { 0 => v2.X, 1 => v2.Y, _ => 0 };
             }
             return 0;
        }

        private void SetVectorComponent(int index, float val)
        {
            if (_parameter.Value is Vector4 v)
            {
                 float x=v.X, y=v.Y, z=v.Z, w=v.W;
                 switch(index) { case 0: x=val; break; case 1: y=val; break; case 2: z=val; break; case 3: w=val; break; }
                 _parameter.Value = new Vector4(x, y, z, w);
                 NotifyVectorChanged();
            }
            else if (_parameter.Value is Vector2 v2)
            {
                 float x=v2.X, y=v2.Y;
                 switch(index) { case 0: x=val; break; case 1: y=val; break; }
                 _parameter.Value = new Vector2(x, y);
                 NotifyVectorChanged();
            }
        }

        private void NotifyVectorChanged()
        {
            OnPropertyChanged(nameof(VectorX));
            OnPropertyChanged(nameof(VectorY));
            OnPropertyChanged(nameof(VectorZ));
            OnPropertyChanged(nameof(VectorW));
            OnPropertyChanged(nameof(CurrentColor));
        }
        
        // Helpers
        private float Clamp(float v, float min, float max) => Math.Max(min, Math.Min(max, v));

        // Simple approximate sRGB <-> Linear
        private float LinearToSrgb(float l)
        {
            if (l <= 0.0031308f) return l * 12.92f;
            return 1.055f * (float)Math.Pow(l, 1.0f / 2.4f) - 0.055f;
        }

        private float SrgbToLinear(float s)
        {
            if (s <= 0.04045f) return s / 12.92f;
            return (float)Math.Pow((s + 0.055f) / 1.055f, 2.4f);
        }
    }
}
