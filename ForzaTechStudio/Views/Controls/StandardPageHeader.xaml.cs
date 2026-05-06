using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ForzaTechStudio.Views.Controls
{
    public sealed partial class StandardPageHeader : UserControl
    {
        public StandardPageHeader()
        {
            this.InitializeComponent();
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(StandardPageHeader),
                new PropertyMetadata(string.Empty));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(
                nameof(Description),
                typeof(string),
                typeof(StandardPageHeader),
                new PropertyMetadata(string.Empty));

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public static readonly DependencyProperty HeaderCommandsProperty =
            DependencyProperty.Register(
                nameof(HeaderCommands),
                typeof(UIElement),
                typeof(StandardPageHeader),
                new PropertyMetadata(null));

        public UIElement HeaderCommands
        {
            get => (UIElement)GetValue(HeaderCommandsProperty);
            set => SetValue(HeaderCommandsProperty, value);
        }
    }
}
