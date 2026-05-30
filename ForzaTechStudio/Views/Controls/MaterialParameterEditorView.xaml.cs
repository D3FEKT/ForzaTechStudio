using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ForzaTools.Bundles.Blobs;
using ForzaTechStudio.ViewModels;
using System.Windows.Input;

namespace ForzaTechStudio.Views.Controls
{
    public sealed partial class MaterialParameterEditorView : UserControl
    {
        public IList<ShaderParameter>? ParametersSource
        {
            get => (IList<ShaderParameter>?)GetValue(ParametersSourceProperty);
            set => SetValue(ParametersSourceProperty, value);
        }

        public static readonly DependencyProperty ParametersSourceProperty =
            DependencyProperty.Register("ParametersSource", typeof(IList<ShaderParameter>), typeof(MaterialParameterEditorView), new PropertyMetadata(null, OnSourceChanged));

        public ICommand? RemoveParameterCommand
        {
            get => (ICommand?)GetValue(RemoveParameterCommandProperty);
            set => SetValue(RemoveParameterCommandProperty, value);
        }

        public static readonly DependencyProperty RemoveParameterCommandProperty =
            DependencyProperty.Register("RemoveParameterCommand", typeof(ICommand), typeof(MaterialParameterEditorView), new PropertyMetadata(null));

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MaterialParameterEditorView view)
            {
                // Unsubscribe from old
                if (e.OldValue is INotifyCollectionChanged oldColl)
                {
                    oldColl.CollectionChanged -= view.OnCollectionChanged;
                }
                
                // Subscribe to new
                if (e.NewValue is INotifyCollectionChanged newColl)
                {
                    newColl.CollectionChanged += view.OnCollectionChanged;
                }

                view.ReloadParameters();
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // For simplicity, just reload everything when collection changes.
            // Optimization can be added later if performance issues arise.
            ReloadParameters();
        }

        public ObservableCollection<ParameterGroup> GroupedParameters { get; } = new();

        // Raised when any parameter value is edited by the user inside this control.
        public event EventHandler? ParameterValueChanged;

        public MaterialParameterEditorView()
        {
            this.InitializeComponent();
        }

        private void RemoveParameter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: ShaderParameter parameter }) return;

            var command = RemoveParameterCommand;
            if (command?.CanExecute(parameter) == true)
            {
                command.Execute(parameter);
            }
        }

        private void ReloadParameters()
        {
            // Unsubscribe from all previous ShaderParameterViewModels
            foreach (var group in GroupedParameters)
                foreach (var vm in group)
                    vm.PropertyChanged -= OnParameterVmPropertyChanged;

            GroupedParameters.Clear();
            if (ParametersSource == null) return;

            var vms = ParametersSource.Select(p => new ShaderParameterViewModel(p));
            var groups = vms.GroupBy(vm => vm.Category)
                            .OrderBy(g => g.Key)
                            .Select(g => new ParameterGroup(g.Key, g));

            foreach (var g in groups)
            {
                foreach (var vm in g)
                    vm.PropertyChanged += OnParameterVmPropertyChanged;
                GroupedParameters.Add(g);
            }
        }

        private void OnParameterVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            ParameterValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class ParameterGroup : List<ShaderParameterViewModel>
    {
        public string Key { get; }
        public ParameterGroup(string key, IEnumerable<ShaderParameterViewModel> items) : base(items)
        {
            Key = key;
        }
    }
}
