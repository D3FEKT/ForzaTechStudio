using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ForzaTechStudio.Views
{
    public sealed partial class MaterialShaderParametersPage : Page
    {
        public MaterialShaderParametersViewModel ViewModel { get; private set; }

        public MaterialShaderParametersPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is MaterialShaderParametersViewModel viewModel)
            {
                ViewModel = viewModel;
            }
        }
    }
}
