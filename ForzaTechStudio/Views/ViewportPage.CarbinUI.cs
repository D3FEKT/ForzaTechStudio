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
                ViewModel.SelectedNode = node;
                if (_treeNodeMap.TryGetValue(node, out var treeViewNode) && FileTree.SelectedItems.Count <= 1)
                    FileTree.SelectedItem = treeViewNode;
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

            CarbinM11.Text = model.Transform.M11.ToString("F6", ci); CarbinM12.Text = model.Transform.M12.ToString("F6", ci);
            CarbinM13.Text = model.Transform.M13.ToString("F6", ci); CarbinM14.Text = model.Transform.M14.ToString("F6", ci);
            CarbinM21.Text = model.Transform.M21.ToString("F6", ci); CarbinM22.Text = model.Transform.M22.ToString("F6", ci);
            CarbinM23.Text = model.Transform.M23.ToString("F6", ci); CarbinM24.Text = model.Transform.M24.ToString("F6", ci);
            CarbinM31.Text = model.Transform.M31.ToString("F6", ci); CarbinM32.Text = model.Transform.M32.ToString("F6", ci);
            CarbinM33.Text = model.Transform.M33.ToString("F6", ci); CarbinM34.Text = model.Transform.M34.ToString("F6", ci);
            CarbinM41.Text = model.Transform.M41.ToString("F6", ci); CarbinM42.Text = model.Transform.M42.ToString("F6", ci);
            CarbinM43.Text = model.Transform.M43.ToString("F6", ci); CarbinM44.Text = model.Transform.M44.ToString("F6", ci);
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
            model.Transform = new Matrix4x4(
                Parse(CarbinM11, current.M11), Parse(CarbinM12, current.M12), Parse(CarbinM13, current.M13), Parse(CarbinM14, current.M14),
                Parse(CarbinM21, current.M21), Parse(CarbinM22, current.M22), Parse(CarbinM23, current.M23), Parse(CarbinM24, current.M24),
                Parse(CarbinM31, current.M31), Parse(CarbinM32, current.M32), Parse(CarbinM33, current.M33), Parse(CarbinM34, current.M34),
                Parse(CarbinM41, current.M41), Parse(CarbinM42, current.M42), Parse(CarbinM43, current.M43), Parse(CarbinM44, current.M44));

            node.Name = BuildCarbinModelDisplayName(model);
            if (FindAncestor<CarbinFileNode>(node) is CarbinFileNode fileNode)
                fileNode.IsDirty = true;

            HideCarbinModel(node);
            if (node.IsChecked == true && node.UseTransforms)
                RenderCarbinModel(node);

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

        private static float Parse(TextBox box, float fallback)
        {
            return float.TryParse(box.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value) ? value : fallback;
        }
    }
}