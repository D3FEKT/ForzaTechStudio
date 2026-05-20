using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using ForzaTechStudio.ViewModels;

namespace ForzaTechStudio.Views
{
    public sealed partial class ModelBinCreatorPage : Page
    {
        public ModelBinCreatorViewModel ViewModel => App.ModelBinCreatorViewModel;

        public ModelBinCreatorPage()
        {
            this.InitializeComponent();
            this.DataContext = App.ModelBinCreatorViewModel;
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            this.Loaded += ModelBinCreatorPage_Loaded;
        }

        private async void ModelBinCreatorPage_Loaded(object sender, RoutedEventArgs e)
        {
            await ViewModel.InitializeAsync();
        }

        private void Step_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                SetStep(tag);
            }
        }

        private void SetStep(string stepTag)
        {
            ImportContent.Visibility = Visibility.Collapsed;
            MaterialsContent.Visibility = Visibility.Collapsed;
            ConfigContent.Visibility = Visibility.Collapsed;

            if (Resources.TryGetValue("StepperButtonStyle", out object defaultStyleObj) && defaultStyleObj is Style defaultStyle)
            {
                Step1Btn.Style = defaultStyle;
                Step2Btn.Style = defaultStyle;
                Step3Btn.Style = defaultStyle;
            }

            if (Resources.TryGetValue("StepperButtonActiveStyle", out object activeStyleObj) && activeStyleObj is Style activeStyle)
            {
                if (stepTag == "Import")
                {
                    ImportContent.Visibility = Visibility.Visible;
                    Step1Btn.Style = activeStyle;
                }
                else if (stepTag == "Materials")
                {
                    MaterialsContent.Visibility = Visibility.Visible;
                    Step2Btn.Style = activeStyle;
                }
                else if (stepTag == "Config")
                {
                    ConfigContent.Visibility = Visibility.Visible;
                    Step3Btn.Style = activeStyle;
                }
            }
        }

        private void NextToMaterials_Click(object sender, RoutedEventArgs e)
        {
            SetStep("Materials");
        }

        private void NextToConfig_Click(object sender, RoutedEventArgs e)
        {
            SetStep("Config");
        }
    }
}