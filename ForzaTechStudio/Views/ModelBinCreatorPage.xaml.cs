using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using ForzaTechStudio.ViewModels;
using System.ComponentModel;
using Microsoft.UI.Xaml.Navigation;

namespace ForzaTechStudio.Views
{
    public sealed partial class ModelBinCreatorPage : Page
    {
        public ModelBinCreatorViewModel ViewModel => App.ModelBinCreatorViewModel;

        public ModelBinCreatorPage()
        {
            this.InitializeComponent();
            this.DataContext = App.ModelBinCreatorViewModel;
            this.NavigationCacheMode = NavigationCacheMode.Required;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            SetStep(ViewModel.CurrentStep);

            if (e.NavigationMode == NavigationMode.Back) return;

            ViewModel.BeginZipSelectionRestore();
            await ViewModel.InitializeAsync();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => ViewModel.EndZipSelectionRestore());
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.CurrentStep))
            {
                SetStep(ViewModel.CurrentStep);
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
    }
}
