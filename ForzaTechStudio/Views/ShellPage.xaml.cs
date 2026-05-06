using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Xaml.Hosting;

namespace ForzaTechStudio.Views
{
    public sealed partial class ShellPage : Page
    {
        public static ShellPage Current { get; private set; } = null!;

        public MainViewModel ViewModel { get; } = new MainViewModel();

        private bool _isFirstLoad = true;

        public ShellPage()
        {
            this.InitializeComponent();
            Current = this;

            ViewModel.NavigationRequested += (pageType) =>
            {
                ContentFrame.Navigate(pageType);
                
                // Sync Shell Navigation Selection
                string? targetTag = null;
                if (pageType == typeof(HomePage)) targetTag = "Home";
                else if (pageType == typeof(MaterialsPage)) targetTag = "Materials";
                else if (pageType == typeof(MaterialsAndShadersPage)) targetTag = "MaterialsAndShaders";
                else if (pageType == typeof(ViewportPage)) targetTag = "ModelView";
                else if (pageType == typeof(ModelBinEditorPage)) targetTag = "ModelBinViewer";
                else if (pageType == typeof(ModelBinCreatorPage)) targetTag = "CreateModelBinPage";
                else if (pageType == typeof(CarbinEditorPage)) targetTag = "CarbinEditorPage";
                else if (pageType == typeof(CreateZipPage)) targetTag = "CreateZip";
                else if (pageType == typeof(SwatchbinEditorPage)) targetTag = "SwatchbinViewer";
                else if (pageType == typeof(ManufacturerColorsPage)) targetTag = "ManufacturerColors";
                else if (pageType == typeof(PhysicsDefinitionPage)) targetTag = "PhysicsDefinitionPage";
                else if (pageType == typeof(ConversionToolPage)) targetTag = "ConversionTool";
                else if (pageType == typeof(SetupPage)) targetTag = "SettingsPage"; // Sync with settings icon
                else if (pageType == typeof(LightsPage)) targetTag = "LightsPage";
                else if (pageType == typeof(FxbEditorPage)) targetTag = "FxbViewer";
                else if (pageType == typeof(StringTablesPage)) targetTag = "StringTables";
                else if (pageType == typeof(BXMLEditorPage)) targetTag = "BXMLEditor";
                else if (pageType == typeof(CarSceneXmlPage)) targetTag = "CarSceneXml";
                else if (pageType == typeof(DocumentationPage)) targetTag = null; // no nav item for docs

                if (targetTag != null)
                {
                    if (targetTag == "SettingsPage")
                    {
                        NavView.SelectedItem = NavView.SettingsItem;
                    }
                    else
                    {
                        var item = FindItemByTag(NavView.MenuItems, targetTag);
                        if (item != null)
                        {
                            NavView.SelectedItem = item;
                        }
                    }
                }
                else
                {
                    NavView.SelectedItem = null;
                }

                NavView.IsBackEnabled = ContentFrame.CanGoBack;
            };

            this.Loaded += ShellPage_Loaded;
            ContentFrame.Navigated += ShellPage_Navigated;
        }

        private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
            }
        }

        private void ShellPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize ViewModel only once
            if (!ViewModel.IsInitialized)
            {
                var window = App.MainWindow;
                if (window != null)
                {
                    var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    ViewModel.Initialize(windowHandle);
                }
            }
            
            // Attach animation handlers to static menu items (only once)
            if (_isFirstLoad)
            {
                AttachAnimationHandlers(NavView.MenuItems);
                
                // Also footer items if any (Settings is separate)
                if (NavView.SettingsItem is NavigationViewItem settingsItem)
                {
                    settingsItem.PointerEntered += NavItem_PointerEntered;
                    settingsItem.PointerExited += NavItem_PointerExited;
                }
                
                // Now that NavView is loaded, set the initial selection and navigate to home
                if (NavView.MenuItems.Count > 0)
                {
                    NavView.SelectedItem = NavView.MenuItems[0];
                    
                    // Navigate to home page
                    if (NavView.MenuItems[0] is NavigationViewItem firstItem && firstItem.Tag?.ToString() == "Home")
                    {
                        ContentFrame.Navigate(typeof(HomePage));
                        if (ContentFrame.Content is HomePage home)
                        {
                            home.ViewModel = this.ViewModel;
                        }
                    }
                }
                
                _isFirstLoad = false;
            }
        }

        private void NavItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is NavigationViewItem item && item.Icon != null)
            {
                AnimateIconScale(item.Icon, 1.2f);
            }
        }

        private void NavItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is NavigationViewItem item && item.Icon != null)
            {
                AnimateIconScale(item.Icon, 1.0f);
            }
        }

        private void AttachAnimationHandlers(IList<object> items)
        {
            foreach (var item in items)
            {
                if (item is NavigationViewItem navItem)
                {
                    navItem.PointerEntered += NavItem_PointerEntered;
                    navItem.PointerExited += NavItem_PointerExited;
                    AttachAnimationHandlers(navItem.MenuItems);
                }
            }
        }

        private NavigationViewItem FindItemByTag(IList<object> items, string tag)
        {
            foreach (var obj in items)
            {
                if (obj is NavigationViewItem item)
                {
                    if (item.Tag?.ToString() == tag) return item;
                    var child = FindItemByTag(item.MenuItems, tag);
                    if (child != null) return child;
                }
            }
            return null;
        }

        private void AnimateIconScale(UIElement icon, float scale)
        {
            // Ensure visual
            var visual = ElementCompositionPreview.GetElementVisual(icon);
            var compositor = visual.Compositor;
            
            // Create Spring Animation
            var springAnim = compositor.CreateSpringVector3Animation();
            springAnim.Target = "Scale";
            springAnim.FinalValue = new Vector3(scale, scale, 1.0f);
            springAnim.DampingRatio = 0.6f;
            springAnim.Period = TimeSpan.FromMilliseconds(50);
            
            // Set CenterPoint for scaling from center
            if (icon is FrameworkElement fe)
            {
                 visual.CenterPoint = new Vector3((float)fe.ActualWidth / 2f, (float)fe.ActualHeight / 2f, 0);
            }
            
            visual.StartAnimation("Scale", springAnim);
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            // Note: Also handled in NavView_ItemInvoked for case where it's already selected
            if (args.IsSettingsSelected)
            {
                if (ContentFrame.SourcePageType != typeof(SettingsPage))
                {
                    ContentFrame.Navigate(typeof(SettingsPage));
                }
                return;
            }

            if (args.SelectedItem is NavigationViewItem item)
            {
                string tag = item.Tag?.ToString();

                if (tag == "Home")
                {
                    ContentFrame.Navigate(typeof(HomePage));
                    if (ContentFrame.Content is HomePage home) home.ViewModel = this.ViewModel;
                }
                else if (tag == "Materials") ContentFrame.Navigate(typeof(MaterialsPage));
                else if (tag == "MaterialsAndShaders") ContentFrame.Navigate(typeof(MaterialsAndShadersPage));
                else if (tag == "ModelView") ContentFrame.Navigate(typeof(ViewportPage));
                else if (tag == "ModelBinViewer") ContentFrame.Navigate(typeof(ModelBinEditorPage));
                else if (tag == "CreateModelBinPage") ContentFrame.Navigate(typeof(ModelBinCreatorPage));
                else if (tag == "CarbinEditorPage") ContentFrame.Navigate(typeof(CarbinEditorPage));
                else if (tag == "CreateZip") ContentFrame.Navigate(typeof(CreateZipPage));
                else if (tag == "SwatchbinViewer") ContentFrame.Navigate(typeof(SwatchbinEditorPage));
                else if (tag == "ManufacturerColors") ContentFrame.Navigate(typeof(ManufacturerColorsPage));
                else if (tag == "PhysicsDefinitionPage") ContentFrame.Navigate(typeof(PhysicsDefinitionPage));
                else if (tag == "ConversionTool") ContentFrame.Navigate(typeof(ConversionToolPage));
                else if (tag == "SettingsPage") ContentFrame.Navigate(typeof(SettingsPage));
                else if (tag == "LightsPage") ContentFrame.Navigate(typeof(LightsPage));
                else if (tag == "FxbViewer") ContentFrame.Navigate(typeof(FxbEditorPage));
                else if (tag == "StringTables") ContentFrame.Navigate(typeof(StringTablesPage));
                else if (tag == "BXMLEditor") ContentFrame.Navigate(typeof(BXMLEditorPage));
                else if (tag == "CarSceneXml") ContentFrame.Navigate(typeof(CarSceneXmlPage));
                else if (tag == "DocumentationPage") ContentFrame.Navigate(typeof(DocumentationPage));
            }
        }

        public bool NavigateToPage(Type pageType, object? parameter = null)
        {
            return ContentFrame.Navigate(pageType, parameter);
        }

        public void ApplyNavigationStyle(string style)
        {
            switch (style)
            {
                case "Compact":
                    NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
                    break;
                case "Top":
                    NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Top;
                    break;
                default: // "Expanded"
                    NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
                    NavView.IsPaneOpen = true;
                    break;
            }
        }

        private void ShellPage_Navigated(object sender, NavigationEventArgs e)
        {
            // Update the back button visibility
            NavView.IsBackEnabled = ContentFrame.CanGoBack;
            
            // Sync selection even for navigations not via ViewModel
            if (e.SourcePageType == typeof(SettingsPage) || 
                e.SourcePageType == typeof(SetupPage))
            {
                NavView.SelectedItem = NavView.SettingsItem;
            }
            else
            {
                // Find tag for menu items and set selection
                // (Already partially handled in NavigationRequested, but good to have fallback)
                string tag = e.SourcePageType switch
                {
                    Type t when t == typeof(HomePage) => "Home",
                    Type t when t == typeof(MaterialsPage) => "Materials",
                    Type t when t == typeof(MaterialsAndShadersPage) => "MaterialsAndShaders",
                    Type t when t == typeof(ViewportPage) => "ModelView",
                    Type t when t == typeof(ModelBinEditorPage) => "ModelBinViewer",
                    Type t when t == typeof(CarbinEditorPage) => "CarbinEditorPage",
                    Type t when t == typeof(CreateZipPage) => "CreateZip",
                    Type t when t == typeof(SwatchbinEditorPage) => "SwatchbinViewer",
                    Type t when t == typeof(ManufacturerColorsPage) => "ManufacturerColors",
                    Type t when t == typeof(PhysicsDefinitionPage) => "PhysicsDefinitionPage",
                    Type t when t == typeof(ConversionToolPage) => "ConversionTool",
                    Type t when t == typeof(LightsPage) => "LightsPage",
                    Type t when t == typeof(BXMLEditorPage) => "BXMLEditor",
                    Type t when t == typeof(CarSceneXmlPage) => "CarSceneXml",
                    Type t when t == typeof(FxbEditorPage) => "FxbViewer",
                    Type t when t == typeof(StringTablesPage) => "StringTables",
                    _ => null
                };

                if (tag != null)
                {
                    var item = FindItemByTag(NavView.MenuItems, tag);
                    if (item != null)
                    {
                        NavView.SelectedItem = item;
                    }
                }
            }
        }

    }
}