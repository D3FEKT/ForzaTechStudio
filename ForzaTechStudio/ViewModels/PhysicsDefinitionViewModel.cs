using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage.Pickers;
using ForzaTechStudio.Services;
using ForzaTools.Bundles;
using Microsoft.UI.Xaml;

namespace ForzaTechStudio.ViewModels
{
    public partial class PhysicsDefinitionViewModel : ObservableObject
    {
        public enum GenerationMode
        {
            PointCloud,
            Box,
            ConvexHull
        }

        private readonly PhysicsDefinitionParser _parser = new PhysicsDefinitionParser();
        private readonly ModelImporter _importer = new ModelImporter();

        [ObservableProperty]
        private ObservableCollection<string> _sourceModels = new();

        [ObservableProperty]
        private GenerationMode _selectedGenerationMode = GenerationMode.PointCloud;

        [ObservableProperty]
        private int _pointCount = 200;

        [ObservableProperty]
        private uint _targetVersion = 30;

        [ObservableProperty]
        private PhysicsDefinitionParser.PhysicsDefinitionType _selectedType = PhysicsDefinitionParser.PhysicsDefinitionType.Vehicle;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsContentVisible))]
        private PhysicsDefinitionParser.PhysicsDefinitionList _currentList;

        [ObservableProperty]
        private string _statusMessage = "Waiting..";

        public List<PhysicsDefinitionParser.PhysicsDefinitionType> AllTypes { get; } = 
            Enum.GetValues<PhysicsDefinitionParser.PhysicsDefinitionType>().ToList();

        public List<GenerationMode> AllGenerationModes { get; } = 
            Enum.GetValues<GenerationMode>().ToList();

        public bool IsContentVisible => CurrentList != null;

        [RelayCommand]
        private void New()
        {
            CurrentList = new PhysicsDefinitionParser.PhysicsDefinitionList
            {
                Definitions = new List<PhysicsDefinitionParser.PhysicsDefinition>(),
                MaxGlobalIndex = 0
            };
            StatusMessage = "Created new physics definition.";
        }

        [RelayCommand]
        private void Close()
        {
            CurrentList = null;
            SourceModels.Clear();
            StatusMessage = "Ready";
        }

        [RelayCommand]
        private async Task PickModelBinsAsync()
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".modelbin");

            var files = await picker.PickMultipleFilesAsync();
            if (files != null)
            {
                foreach (var file in files)
                {
                    if (!_sourceModels.Contains(file.Path))
                    {
                        _sourceModels.Add(file.Path);
                    }
                }
            }
        }

        [RelayCommand]
        private void RemoveModel(string path)
        {
            _sourceModels.Remove(path);
        }

        [RelayCommand]
        private async Task GenerateAsync()
        {
            if (_sourceModels.Count == 0)
            {
                StatusMessage = "Add at least one .modelbin file.";
                return;
            }

            StatusMessage = "Extracting vertices...";
            var allVertices = new List<Vector3>();

            await Task.Run(() =>
            {
                foreach (var path in _sourceModels)
                {
                    try
                    {
                        var bundle = new Bundle();
                        using (var fs = File.OpenRead(path))
                        {
                            bundle.Load(fs);
                        }
                        var result = _importer.ExtractModels(bundle);
                        foreach (var mesh in result.Meshes)
                        {
                            if (mesh.RawPositions != null && mesh.SourceMesh != null)
                            {
                                var scale = mesh.SourceMesh.PositionScale;
                                var trans = mesh.SourceMesh.PositionTranslate;
                                foreach (var raw in mesh.RawPositions)
                                {
                                    float nx = raw.X * scale.X + trans.X;
                                    float ny = raw.Y * scale.Y + trans.Y;
                                    float nz = raw.Z * scale.Z + trans.Z;
                                    allVertices.Add(Vector3.Transform(new Vector3(nx, ny, nz), mesh.BoneTransform));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading {path}: {ex.Message}");
                    }
                }
            });

            if (allVertices.Count == 0)
            {
                StatusMessage = "No vertices found in selected models.";
                return;
            }

            StatusMessage = "Generating physics definition...";

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var v in allVertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            var center = (min + max) / 2.0f;
            var halfExtents = (max - min) / 2.0f;
            var boundingRadius = halfExtents.Length();

            // Random sampling for point cloud
            var random = new Random();
            var sampledPoints = allVertices
                .OrderBy(x => random.Next())
                .Take(_pointCount)
                .ToList();

            var def = new PhysicsDefinitionParser.PhysicsDefinition
            {
                Version = TargetVersion,
                DefinitionType = _selectedType,
                Mass = 1500.0f,
                HalfExtents = halfExtents,
                BoundingRadius = boundingRadius,
                AabbCentreOffset = center,
                InertiaTensor = new PhysicsDefinitionParser.AABB
                {
                    Min = new Vector4(min, 1.0f),
                    Max = new Vector4(max, 1.0f),
                    Radius = boundingRadius
                },
                InverseInertiaTensor = new PhysicsDefinitionParser.AABB
                {
                    Min = new Vector4(1.0f / min.X, 1.0f / min.Y, 1.0f / min.Z, 1.0f),
                    Max = new Vector4(1.0f / max.X, 1.0f / max.Y, 1.0f / max.Z, 1.0f),
                    Radius = 1.0f / boundingRadius
                }
            };

            def.Shapes.Clear();
            if (_selectedGenerationMode == GenerationMode.PointCloud)
            {
                def.Shapes.Add(new PhysicsDefinitionParser.PhysicsDefinition.CollisionShape
                {
                    Type = PhysicsDefinitionParser.ECollisionShapeType.PointCloud,
                    PointCloud = new PhysicsDefinitionParser.PhysicsDefinition.PointCloudShape
                    {
                        Points = sampledPoints
                    }
                });
            }
            else if (_selectedGenerationMode == GenerationMode.Box)
            {
                def.Shapes.Add(new PhysicsDefinitionParser.PhysicsDefinition.CollisionShape
                {
                    Type = PhysicsDefinitionParser.ECollisionShapeType.Box,
                    Box = new PhysicsDefinitionParser.PhysicsDefinition.BoxShape
                    {
                        HalfExtents = halfExtents
                    }
                });
            }
            else if (_selectedGenerationMode == GenerationMode.ConvexHull)
            {
                def.Shapes.Add(new PhysicsDefinitionParser.PhysicsDefinition.CollisionShape
                {
                    Type = PhysicsDefinitionParser.ECollisionShapeType.ConvexHull,
                    ConvexHull = new PhysicsDefinitionParser.PhysicsDefinition.ConvexHullShape
                    {
                        Vertices = sampledPoints
                    }
                });
            }

            CurrentList = new PhysicsDefinitionParser.PhysicsDefinitionList
            {
                Definitions = new List<PhysicsDefinitionParser.PhysicsDefinition> { def },
                MaxGlobalIndex = 0
            };

            StatusMessage = "Generation complete.";
        }

        [RelayCommand]
        private async Task OpenExistingAsync()
        {
            var picker = new FileOpenPicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".bin");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await LoadPhysicsFileAsync(file.Path);
            }
        }

        public async Task LoadPhysicsFileAsync(string filePath)
        {
            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    CurrentList = _parser.Parse(stream);
                }

                if (CurrentList != null && CurrentList.Definitions.Count > 0)
                {
                    TargetVersion = CurrentList.Definitions[0].Version;
                }
                
                StatusMessage = $"Loaded {Path.GetFileName(filePath)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            if (CurrentList == null) return;

            var picker = new FileSavePicker();
            var window = App.MainWindow;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.SuggestedFileName = "physicsdefinition.bin";
            picker.FileTypeChoices.Add("Binary File", new List<string> { ".bin" });

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    using (var stream = await file.OpenStreamForWriteAsync())
                    {
                        foreach(var def in CurrentList.Definitions)
                        {
                            def.Version = TargetVersion;
                        }

                        _parser.Serialize(stream, CurrentList);
                    }
                    StatusMessage = $"Saved to {file.Name}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error: {ex.Message}";
                }
            }
        }
    }
}
