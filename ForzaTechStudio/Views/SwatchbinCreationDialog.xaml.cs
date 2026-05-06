using System;
using System.Linq;
using ForzaTechStudio.Models;
using Microsoft.UI.Xaml.Controls;

namespace ForzaTechStudio.Views;

public sealed partial class SwatchbinCreationDialog : ContentDialog
{
    private ComboBox EncodingCombo;
    private ComboBox ColorProfileCombo;
    private ComboBox TranscodingCombo;
    private CheckBox GenerateMipsCheck;
    private CheckBox IsCubeMapCheck;
    private CheckBox Is3DCheck;
    private CheckBox IsPremultipliedAlphaCheck;
    private TextBox GuidText;

    public SwatchbinCreationDialog()
    {
        Title = "Swatchbin Format Options";
        PrimaryButtonText = "Create";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        EncodingCombo = new ComboBox { Header = "Encoding", HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
        ColorProfileCombo = new ComboBox { Header = "Color Profile", HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
        TranscodingCombo = new ComboBox { Header = "Transcoding", HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
        
        GenerateMipsCheck = new CheckBox { Content = "Generate Mip Maps", IsChecked = true };
        IsCubeMapCheck = new CheckBox { Content = "Is Cube Map" };
        Is3DCheck = new CheckBox { Content = "Is 3D Texture" };
        IsPremultipliedAlphaCheck = new CheckBox { Content = "Premultiplied Alpha" };
        
        GuidText = new TextBox { Header = "GUID (Leave blank for new)", PlaceholderText = "00000000-0000-0000-0000-000000000000" };

        var panel = new StackPanel { Spacing = 10, Width = 400 };
        panel.Children.Add(EncodingCombo);
        panel.Children.Add(ColorProfileCombo);
        panel.Children.Add(TranscodingCombo);
        panel.Children.Add(GenerateMipsCheck);
        panel.Children.Add(IsCubeMapCheck);
        panel.Children.Add(Is3DCheck);
        panel.Children.Add(IsPremultipliedAlphaCheck);
        panel.Children.Add(GuidText);
        
        Content = panel;

        // Initialize ComboBoxes
        EncodingCombo.ItemsSource = Enum.GetValues(typeof(TextureEncoding)).Cast<TextureEncoding>().ToList();
        EncodingCombo.SelectedItem = TextureEncoding.R8G8B8A8;
        
        ColorProfileCombo.ItemsSource = Enum.GetValues(typeof(ColorProfile)).Cast<ColorProfile>().ToList();
        ColorProfileCombo.SelectedItem = ColorProfile.Rec709SRgb;
        
        TranscodingCombo.ItemsSource = Enum.GetValues(typeof(TextureTranscoding)).Cast<TextureTranscoding>().ToList();
        TranscodingCombo.SelectedItem = TextureTranscoding.None;
    }

    public TextureEncoding SelectedEncoding => (TextureEncoding)EncodingCombo.SelectedItem;
    public ColorProfile SelectedColorProfile => (ColorProfile)ColorProfileCombo.SelectedItem;
    public TextureTranscoding SelectedTranscoding => (TextureTranscoding)TranscodingCombo.SelectedItem;
    public bool GenerateMipMaps => GenerateMipsCheck.IsChecked ?? true;
    public bool IsCubeMap => IsCubeMapCheck.IsChecked ?? false;
    public bool Is3D => Is3DCheck.IsChecked ?? false;
    public bool IsPremultipliedAlpha => IsPremultipliedAlphaCheck.IsChecked ?? false;
    
    public Guid? TextureGuid
    {
        get
        {
            if (Guid.TryParse(GuidText.Text, out Guid result))
                return result;
            return null;
        }
    }
}