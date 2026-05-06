using ForzaTechStudio.ViewModels.ThreeDViewer;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SDX = SharpDX;

namespace ForzaTechStudio.Views
{
    // Undo/Redo System
    public sealed partial class ViewportPage : Page
    {
        private readonly Stack<IUndoAction> _undoStack = new();
        private readonly Stack<IUndoAction> _redoStack = new();
        private const int MaxUndoHistory = 100;

        private void PushUndo(IUndoAction action)
        {
            _undoStack.Push(action);
            _redoStack.Clear();

            // Trim history
            if (_undoStack.Count > MaxUndoHistory)
            {
                var items = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = 0; i < MaxUndoHistory; i++)
                    _undoStack.Push(items[MaxUndoHistory - 1 - i]);
            }

            UpdateUndoRedoButtons();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0) return;

            var action = _undoStack.Pop();
            action.Undo();
            _redoStack.Push(action);

            UpdateUndoRedoButtons();
            RefreshAfterUndoRedo(action);
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;

            var action = _redoStack.Pop();
            action.Redo();
            _undoStack.Push(action);

            UpdateUndoRedoButtons();
            RefreshAfterUndoRedo(action);
        }

        private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
        private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

        private void UpdateUndoRedoButtons()
        {
            if (UndoBtn != null)
            {
                UndoBtn.IsEnabled = _undoStack.Count > 0;
                ToolTipService.SetToolTip(UndoBtn,
                    _undoStack.Count > 0 ? $"Undo: {_undoStack.Peek().Description}" : "Nothing to undo");
            }
            if (RedoBtn != null)
            {
                RedoBtn.IsEnabled = _redoStack.Count > 0;
                ToolTipService.SetToolTip(RedoBtn,
                    _redoStack.Count > 0 ? $"Redo: {_redoStack.Peek().Description}" : "Nothing to redo");
            }
        }

        private void RefreshAfterUndoRedo(IUndoAction action)
        {
            if (action is MeshTransformAction meshAction)
            {
                foreach (var entry in meshAction.Entries)
                {
                    var mesh = entry.Mesh;
                    if (mesh.GeometryData?.SourceMesh == null) continue;

                    var meshBlob = mesh.GeometryData.SourceMesh;
                    bool hasBone = mesh.GeometryData.SourceBone != null &&
                                   Services.BoneTransformService.IsSignificantBone(mesh.GeometryData.BoneIndex);

                    if (hasBone)
                    {
                        UpdateMeshRenderingWithBoneTransform(mesh, mesh.GeometryData.BoneTransform);
                    }
                    else
                    {
                        UpdateMeshRendering(mesh,
                            meshBlob.PositionScale.X, meshBlob.PositionScale.Y, meshBlob.PositionScale.Z,
                            meshBlob.PositionTranslate.X, meshBlob.PositionTranslate.Y, meshBlob.PositionTranslate.Z);
                    }
                }

                UpdateTransformUI();
                RefreshHighlight();
            }
            else if (action is LocatorTransformAction locatorAction)
            {
                RefreshLocatorCone(locatorAction.Node);
                if (ViewModel.SelectedNode == locatorAction.Node)
                {
                    PopulateMatrixFields(locatorAction.Node.LocatorEntry.SceneTransform);
                }
            }
        }

        // Records the current transform state of the given meshes before a transform operation begins.
        // Call this BEFORE modifying values, then call CommitTransformAction() after.
        private MeshTransformAction BeginTransformAction(IEnumerable<MeshNode> meshes, string description)
        {
            var entries = new List<MeshTransformEntry>();
            foreach (var mesh in meshes)
            {
                if (mesh.GeometryData?.SourceMesh == null) continue;
                entries.Add(new MeshTransformEntry
                {
                    Mesh = mesh,
                    OldScale = mesh.GeometryData.SourceMesh.PositionScale,
                    OldTranslate = mesh.GeometryData.SourceMesh.PositionTranslate,
                    OldRotation = mesh.GeometryData.RotationEulerDegrees
                });
            }
            return new MeshTransformAction(description, entries);
        }

        // Captures the new state after the transform and pushes the action.
        private void CommitTransformAction(MeshTransformAction action)
        {
            bool anyChanged = false;
            foreach (var entry in action.Entries)
            {
                if (entry.Mesh.GeometryData?.SourceMesh == null) continue;
                entry.NewScale = entry.Mesh.GeometryData.SourceMesh.PositionScale;
                entry.NewTranslate = entry.Mesh.GeometryData.SourceMesh.PositionTranslate;
                entry.NewRotation = entry.Mesh.GeometryData.RotationEulerDegrees;

                if (entry.OldScale != entry.NewScale || entry.OldTranslate != entry.NewTranslate || entry.OldRotation != entry.NewRotation)
                    anyChanged = true;
            }

            if (anyChanged)
                PushUndo(action);
        }
    }

    // Undo action interface
    public interface IUndoAction
    {
        string Description { get; }
        void Undo();
        void Redo();
    }

    // Stores before/after transform for a single mesh
    public class MeshTransformEntry
    {
        public MeshNode? Mesh { get; set; }
        public Vector4 OldScale { get; set; }
        public Vector4 OldTranslate { get; set; }
        public Vector3 OldRotation { get; set; }
        public Vector4 NewScale { get; set; }
        public Vector4 NewTranslate { get; set; }
        public Vector3 NewRotation { get; set; }
    }

    // Undo action for mesh transform changes
    public class MeshTransformAction : IUndoAction
    {
        public string Description { get; }
        public List<MeshTransformEntry> Entries { get; }

        public MeshTransformAction(string description, List<MeshTransformEntry> entries)
        {
            Description = description;
            Entries = entries;
        }

        public void Undo()
        {
            foreach (var entry in Entries)
            {
                if (entry.Mesh.GeometryData?.SourceMesh == null) continue;
                entry.Mesh.GeometryData.SourceMesh.PositionScale = entry.OldScale;
                entry.Mesh.GeometryData.SourceMesh.PositionTranslate = entry.OldTranslate;
                entry.Mesh.GeometryData.RotationEulerDegrees = entry.OldRotation;
            }
        }

        public void Redo()
        {
            foreach (var entry in Entries)
            {
                if (entry.Mesh.GeometryData?.SourceMesh == null) continue;
                entry.Mesh.GeometryData.SourceMesh.PositionScale = entry.NewScale;
                entry.Mesh.GeometryData.SourceMesh.PositionTranslate = entry.NewTranslate;
                entry.Mesh.GeometryData.RotationEulerDegrees = entry.NewRotation;
            }
        }
    }

    // Undo action for locator transform
    public class LocatorTransformAction : IUndoAction
    {
        public string Description { get; }
        public LocatorNode Node { get; }
        public Matrix4x4 OldTransform { get; }
        public Matrix4x4 NewTransform { get; set; }

        public LocatorTransformAction(string description, LocatorNode node, Matrix4x4 oldTransform)
        {
            Description = description;
            Node = node;
            OldTransform = oldTransform;
        }

        public void Undo()
        {
            Node.LocatorEntry.SceneTransform = OldTransform;
            if (Node.Parent is LocatorsXmlNode xmlRoot)
                xmlRoot.IsDirty = true;
        }

        public void Redo()
        {
            Node.LocatorEntry.SceneTransform = NewTransform;
            if (Node.Parent is LocatorsXmlNode xmlRoot)
                xmlRoot.IsDirty = true;
        }
    }
}
