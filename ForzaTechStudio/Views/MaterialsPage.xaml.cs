using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ForzaTechStudio.Views
{
    public sealed partial class MaterialsPage : Page
    {
        public MaterialsViewModel ViewModel { get; } = new MaterialsViewModel();

        public MaterialsPage()
        {
            this.InitializeComponent();
        }
    }
}