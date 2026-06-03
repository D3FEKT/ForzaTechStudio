using ForzaTechStudio.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;


namespace ForzaTechStudio
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            Title = "ForzaTech Studio";


            ExtendsContentIntoTitleBar = true;
            SetTitleBar(SimpleTitleBar);

            RootFrame.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RootFrame_PointerPressed), true);

            RootFrame.Navigate(typeof(ShellPage));
        }

        private void RootFrame_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (RootFrame.XamlRoot == null || e.OriginalSource is not DependencyObject source)
                return;

            var focusedTextInput = FindFocusedTextInput(RootFrame.XamlRoot);
            if (focusedTextInput == null)
                return;

            if (FindTextInputAncestor(source) != null)
                return;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (RootFrame.XamlRoot == null)
                    return;

                var focusedAfterClick = FindFocusedTextInput(RootFrame.XamlRoot);
                if (focusedAfterClick != null && ReferenceEquals(focusedAfterClick, focusedTextInput))
                    FocusParkingElement.Focus(FocusState.Programmatic);
            });
        }

        private static DependencyObject? FindFocusedTextInput(XamlRoot xamlRoot)
        {
            return FindTextInputAncestor(FocusManager.GetFocusedElement(xamlRoot) as DependencyObject);
        }

        private static DependencyObject? FindTextInputAncestor(DependencyObject? source)
        {
            var current = source;
            while (current != null)
            {
                if (current is NumberBox or TextBox or PasswordBox or RichEditBox or AutoSuggestBox)
                    return current;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}