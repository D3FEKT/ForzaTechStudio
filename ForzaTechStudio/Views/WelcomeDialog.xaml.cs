using System;
using Microsoft.UI.Xaml.Controls;

namespace ForzaTechStudio.Views
{
    public sealed partial class WelcomeDialog : ContentDialog
    {
        public bool DontShowAgain => DontShowAgainCheckBox.IsChecked == true;

        public WelcomeDialog()
        {
            this.InitializeComponent();
        }
    }
}