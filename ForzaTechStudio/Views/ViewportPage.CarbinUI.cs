using ForzaTechStudio.ViewModels.ThreeDViewer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace ForzaTechStudio.Views
{
    public sealed partial class ViewportPage : Page
    {
        private bool _isUpdatingCarbinUi = false;

        private void UpdateCarbinExpanderVisibility()
        {
            var carbinModels = EnumerateViewerNodes<CarbinModelNode>(ViewModel.Roots).ToList();
            bool hasCarbin = carbinModels.Count > 0;
            CarbinExpander.Visibility = hasCarbin ? Visibility.Visible : Visibility.Collapsed;

            if (!hasCarbin)
            {
                CarbinModelSelector.ItemsSource = null;
                return;
            }

            var current = CarbinModelSelector.SelectedItem as CarbinModelNode;
            _isUpdatingCarbinUi = true;
            CarbinModelSelector.ItemsSource = carbinModels;
            if (current != null && carbinModels.Contains(current))
                CarbinModelSelector.SelectedItem = current;
            else if (CarbinModelSelector.SelectedItem == null && carbinModels.Count > 0)
                CarbinModelSelector.SelectedIndex = 0;
            _isUpdatingCarbinUi = false;

            if (CarbinModelSelector.SelectedItem is CarbinModelNode selected)
                PopulateCarbinFields(selected);
        }

        private void ShowCarbinModelUI(CarbinModelNode node)
        {
            var carbinModels = EnumerateViewerNodes<CarbinModelNode>(ViewModel.Roots).ToList();
            _isUpdatingCarbinUi = true;
            CarbinModelSelector.ItemsSource = carbinModels;
            CarbinModelSelector.SelectedItem = node;
            _isUpdatingCarbinUi = false;
            PopulateCarbinFields(node);
        }

        private void CarbinModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingCarbinUi) return;
            if (CarbinModelSelector.SelectedItem is CarbinModelNode node)
            {
                ApplySingleSelection(node, syncTree: true);
            }
        }

        private void PopulateCarbinFields(CarbinModelNode node)
        {
            _isUpdatingCarbinUi = true;
            var ci = CultureInfo.InvariantCulture;
            var model = node.Model;

            CarbinModelPathBox.Text = model.Path ?? string.Empty;
            CarbinBoneNameBox.Text = model.BoneName ?? string.Empty;
            CarbinBoneIdBox.Text = model.BoneId.ToString(ci);
            CarbinUseTransformsCheckBox.IsChecked = node.UseTransforms;

            PopulateTransformEditorFields(
                CarbinPosX,
                CarbinPosY,
                CarbinPosZ,
                CarbinRotX,
                CarbinRotY,
                CarbinRotZ,
                CarbinScaleX,
                CarbinScaleY,
                CarbinScaleZ,
                model.Transform);
            _isUpdatingCarbinUi = false;
        }

        private void CarbinField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingCarbinUi) return;
            var node = ViewModel.SelectedNode as CarbinModelNode ?? CarbinModelSelector.SelectedItem as CarbinModelNode;
            if (node?.Model == null) return;

            var model = node.Model;
            model.Path = CarbinModelPathBox.Text ?? string.Empty;
            model.BoneName = CarbinBoneNameBox.Text ?? string.Empty;
            if (short.TryParse(CarbinBoneIdBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out short boneId))
                model.BoneId = boneId;

            var current = model.Transform;
            DecomposeTransformMatrix(current, out var currentPosition, out var currentRotation, out var currentScale);
            model.Transform = ComposeTransformMatrix(
                ReadVector3Fields(CarbinPosX, CarbinPosY, CarbinPosZ, currentPosition),
                ReadVector3Fields(CarbinRotX, CarbinRotY, CarbinRotZ, currentRotation),
                ReadVector3Fields(CarbinScaleX, CarbinScaleY, CarbinScaleZ, currentScale),
                current);

            node.Name = BuildCarbinModelDisplayName(model);
            if (FindAncestor<CarbinFileNode>(node) is CarbinFileNode fileNode)
                fileNode.IsDirty = true;

            _carbinModelBinCache.Remove(node);
            RebuildCarbinModel(node);

            if (ReferenceEquals(ViewModel.SelectedNode, node))
                UpdateHighlight(node);
        }

        private void CarbinUseTransforms_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingCarbinUi) return;

            var node = ViewModel.SelectedNode as CarbinModelNode ?? CarbinModelSelector.SelectedItem as CarbinModelNode;
            if (node == null)
                return;

            node.UseTransforms = CarbinUseTransformsCheckBox.IsChecked == true;
        }

        private async void SaveCarbin_Click(object sender, RoutedEventArgs e)
        {
            var modelNode = ViewModel.SelectedNode as CarbinModelNode ?? CarbinModelSelector.SelectedItem as CarbinModelNode;
            if (modelNode == null || FindAncestor<CarbinFileNode>(modelNode) is not CarbinFileNode fileNode)
            {
                await ShowError("No .carbin file is currently active.");
                return;
            }

            await SaveSelectedFileNodesAsync(new[] { fileNode }, saveAsFolder: false);
        }
    }
}
