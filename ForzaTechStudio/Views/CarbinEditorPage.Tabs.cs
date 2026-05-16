using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ForzaTechStudio.Views
{
    public sealed partial class CarbinEditorPage : Page
    {
        private bool _isSwitchingTabs;

        private void CarbinTabListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSwitchingTabs) return;
            if (CarbinTabListView.SelectedItem is not CarbinEditorTab newTab) return;
            if (newTab == ViewModel.ActiveTab) return;

            _isSwitchingTabs = true;
            try
            {
                // Save current tab state before switching
                if (ViewModel.ActiveTab != null)
                    ViewModel.SaveCurrentStateToTab(ViewModel.ActiveTab);

                ViewModel.ActiveTab = newTab;
                ViewModel.RestoreTabState(newTab);
            }
            finally
            {
                _isSwitchingTabs = false;
            }
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CarbinEditorTab tab)
            {
                _isSwitchingTabs = true;
                try
                {
                    ViewModel.CloseTab(tab);
                    // Sync list view selection to new active tab
                    CarbinTabListView.SelectedItem = ViewModel.ActiveTab;
                }
                finally
                {
                    _isSwitchingTabs = false;
                }
            }
        }

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            _isSwitchingTabs = true;
            try
            {
                // Save current state before opening blank tab
                if (ViewModel.ActiveTab != null)
                    ViewModel.SaveCurrentStateToTab(ViewModel.ActiveTab);

                var blank = ViewModel.CreateEmptyTab();
                ViewModel.Tabs.Add(blank);
                ViewModel.ActiveTab = blank;
                ViewModel.RestoreTabState(blank);

                CarbinTabListView.SelectedItem = blank;
            }
            finally
            {
                _isSwitchingTabs = false;
            }
        }
    }
}
