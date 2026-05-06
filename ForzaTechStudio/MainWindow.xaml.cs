using ForzaTechStudio.Views;
using Microsoft.UI.Xaml;


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


            RootFrame.Navigate(typeof(ShellPage));
        }
    }
}