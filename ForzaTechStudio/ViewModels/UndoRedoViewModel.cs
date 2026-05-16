using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ForzaTechStudio.ViewModels
{
    /// <summary>
    /// Abstract base class that provides a unified undo/redo stack to any ViewModel.
    /// Inherit from this instead of ObservableObject to get UndoCommand, RedoCommand,
    /// CanUndo/CanRedo properties, PushUndo support, and an IsUndoRedoInProgress guard.
    ///
    /// Call TrackPropertyChanges(newObj, oldObj) whenever your selected-item property
    /// changes to automatically intercept every NumberBox / TextBox / CheckBox edit on
    /// that model object and push a corresponding undo entry — no per-property boilerplate
    /// required.
    /// </summary>
    public abstract partial class UndoRedoViewModel : ObservableObject
    {
        protected Stack<(Action Undo, Action Redo)> _undoStack = new();
        protected Stack<(Action Undo, Action Redo)> _redoStack = new();

        // Stores old property values captured by PropertyChanging, keyed by (sender, propertyName).
        private readonly Dictionary<(object Sender, string PropName), object?> _pendingOldValues = new();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// True while an Undo or Redo action is executing. Use this to suppress
        /// re-entrant PushUndo calls triggered by binding updates during undo/redo.
        /// </summary>
        public bool IsUndoRedoInProgress { get; private set; }

        internal void PushUndo(Action undo, Action redo)
        {
            if (IsUndoRedoInProgress) return;
            _undoStack.Push((undo, redo));
            _redoStack.Clear();
            NotifyUndoRedo();
        }

        [RelayCommand(CanExecute = nameof(CanUndo))]
        private void Undo()
        {
            if (_undoStack.Count == 0) return;
            var (undo, redo) = _undoStack.Pop();
            IsUndoRedoInProgress = true;
            try { undo(); }
            finally { IsUndoRedoInProgress = false; }
            _redoStack.Push((undo, redo));
            NotifyUndoRedo();
        }

        [RelayCommand(CanExecute = nameof(CanRedo))]
        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            var (undo, redo) = _redoStack.Pop();
            IsUndoRedoInProgress = true;
            try { redo(); }
            finally { IsUndoRedoInProgress = false; }
            _undoStack.Push((undo, redo));
            NotifyUndoRedo();
        }

        protected void NotifyUndoRedo()
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        }

        // ── Property-change tracking ──────────────────────────────────────────────
        // Subscribe to newObj and unsubscribe from oldObj so that every editable
        // property change on the tracked model object is automatically turned into
        // a reversible undo/redo entry via PropertyChanging + PropertyChanged.

        /// <summary>
        /// Begins tracking property changes on <paramref name="newObj"/> and stops
        /// tracking <paramref name="oldObj"/>.  Pass <c>null</c> for either argument
        /// to only subscribe or only unsubscribe.
        /// </summary>
        protected void TrackPropertyChanges(INotifyPropertyChanging? newObj,
                                            INotifyPropertyChanging? oldObj)
        {
            if (oldObj != null)
            {
                oldObj.PropertyChanging -= OnTrackedPropertyChanging;
                ((INotifyPropertyChanged)oldObj).PropertyChanged -= OnTrackedPropertyChanged;
            }
            if (newObj != null)
            {
                newObj.PropertyChanging += OnTrackedPropertyChanging;
                ((INotifyPropertyChanged)newObj).PropertyChanged += OnTrackedPropertyChanged;
            }
        }

        private void OnTrackedPropertyChanging(object? sender, PropertyChangingEventArgs e)
        {
            if (IsUndoRedoInProgress || sender == null || e.PropertyName == null) return;
            var prop = sender.GetType().GetProperty(e.PropertyName);
            if (prop?.CanRead != true || prop.CanWrite != true) return;
            _pendingOldValues[(sender, e.PropertyName)] = prop.GetValue(sender);
        }

        private void OnTrackedPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (IsUndoRedoInProgress || sender == null || e.PropertyName == null) return;
            var key = (sender, e.PropertyName);
            if (!_pendingOldValues.Remove(key, out var oldVal)) return;
            var prop = sender.GetType().GetProperty(e.PropertyName);
            if (prop?.CanRead != true) return;
            var newVal = prop.GetValue(sender);
            if (Equals(oldVal, newVal)) return;

            var capturedProp   = prop;
            var capturedSender = sender;
            PushUndo(
                () => capturedProp.SetValue(capturedSender, oldVal),
                () => capturedProp.SetValue(capturedSender, newVal));
        }
    }
}
