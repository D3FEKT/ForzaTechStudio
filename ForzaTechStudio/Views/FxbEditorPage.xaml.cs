using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;

namespace ForzaTechStudio.Views
{
    public sealed partial class FxbEditorPage : Page
    {
        public FxbEditorViewModel ViewModel { get; } = new FxbEditorViewModel();

        // Guard flag to prevent cascading selection-changed events from overwriting
        // SelectedNode while we are programmatically clearing sub-lists.
        private bool _suppressSelectionChanged;

        public FxbEditorPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        }

        // x:Bind function ? returns Collapsed when object is non-null (hides placeholder)
        private Visibility NullToVis(object? value)
            => value == null ? Visibility.Visible : Visibility.Collapsed;

        // ?? Tree: effect clicked ???????????????????????????????????????????????

        private void EffectTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            // args.InvokedItem is the data object (FxbEffectNode) ? no wrapper needed
            // because the DataTemplate does NOT contain a TreeViewItem.
            if (args.InvokedItem is not FxbEffectNode effect) return;

            _suppressSelectionChanged = true;
            try
            {
                PhasesList.SelectedItem    = null;
                ComponentsList.SelectedItem = null;
            }
            finally
            {
                _suppressSelectionChanged = false;
            }

            ViewModel.SelectedNode = effect;
        }

        // ?? Sub-list: phase selected ???????????????????????????????????????????

        private void PhasesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionChanged) return;
            if (PhasesList.SelectedItem is FxbPhaseNode phase)
                ViewModel.SelectedNode = phase;
        }

        // ?? Sub-list: component selected ??????????????????????????????????????

        private void ComponentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionChanged) return;
            if (ComponentsList.SelectedItem is FxbComponentNode comp)
            {
                _suppressSelectionChanged = true;
                try { PropertiesList.SelectedItem = null; }
                finally { _suppressSelectionChanged = false; }

                ViewModel.SelectedNode = comp;
            }
        }

        // ?? Sub-list: property selected ???????????????????????????????????????

        private void PropertiesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionChanged) return;
            if (PropertiesList.SelectedItem is FxbPropertyValueNode prop)
                ViewModel.SelectedNode = prop;
        }

        // ?? Drag & drop ???????????????????????????????????????????????????????

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();
            var fxb = items
                .OfType<Windows.Storage.StorageFile>()
                .FirstOrDefault(f => f.Path.EndsWith(".fxb", StringComparison.OrdinalIgnoreCase));

            if (fxb != null)
                await ViewModel.LoadFxbAsync(fxb.Path);
        }

        // ?? Back button: property ? component ????????????????????????????

        private void PropertyBackButton_Click(object sender, RoutedEventArgs e)
        {
            // Re-select the component that owns the currently selected property.
            // Walk all effects/components to find the parent.
            if (ViewModel.Bank == null || ViewModel.SelectedProperty == null) return;

            var prop = ViewModel.SelectedProperty;
            foreach (var effect in ViewModel.Bank.Effects)
            {
                foreach (var comp in effect.Components)
                {
                    if (comp.PropertyValues.Contains(prop))
                    {
                        _suppressSelectionChanged = true;
                        try { PropertiesList.SelectedItem = null; }
                        finally { _suppressSelectionChanged = false; }

                        ViewModel.SelectedNode = comp;
                        return;
                    }
                }
            }
        }
    }
}
