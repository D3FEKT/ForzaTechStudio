using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ForzaTechStudio.Views
{

    public sealed class DocEntry
    {
        public required string Title    { get; init; }
        public required string Glyph    { get; init; }
        public required string FileName { get; init; } 
        public required string GithubUrl { get; init; }
    }


    public sealed partial class DocumentationPage : Page
    {


        private static readonly DocEntry[] _entries =
        [
            // General
            new DocEntry { Title = "Getting Started",    Glyph = "\uE80F", FileName = "getting-started.md",   GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/getting-started.md"   },
            // Tools
            new DocEntry { Title = "3D Viewer",          Glyph = "\uF158", FileName = "3d-viewer.md",          GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/3d-viewer.md"          },
            new DocEntry { Title = "Carbin Editor",      Glyph = "\uE81F", FileName = "carbin-editor.md",      GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/carbin-editor.md"      },
            new DocEntry { Title = "Modelbin Editor",    Glyph = "\uE8E5", FileName = "modelbin-editor.md",    GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/modelbin-editor.md"    },
            new DocEntry { Title = "Swatchbin Viewer",   Glyph = "\uEB9F", FileName = "swatchbin-viewer.md",   GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/swatchbin-viewer.md"   },
            new DocEntry { Title = "Conversion Tool",    Glyph = "\uE8AB", FileName = "conversion-tool.md",    GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/conversion-tool.md"    },
            new DocEntry { Title = "Create Zip",         Glyph = "\uF012", FileName = "create-zip.md",         GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/create-zip.md"         },
            new DocEntry { Title = "Lights Editor",      Glyph = "\uEA80", FileName = "lights-editor.md",      GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/lights-editor.md"      },
            new DocEntry { Title = "Manufacturer Colors",Glyph = "\uE790", FileName = "manufacturer-colors.md",GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/manufacturer-colors.md"},
            new DocEntry { Title = "Physics Definition", Glyph = "\uECAA", FileName = "physics-definition.md", GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/physics-definition.md" },
            new DocEntry { Title = "FXB Viewer",         Glyph = "\uE945", FileName = "fxb-viewer.md",         GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/fxb-viewer.md"         },
            new DocEntry { Title = "BXML Editor",        Glyph = "\uE8A5", FileName = "bxml-editor.md",        GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/bxml-editor.md"        },
            new DocEntry { Title = "Car Scene XML",      Glyph = "\uE8F4", FileName = "car-scene-xml.md",      GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/car-scene-xml.md"      },
            new DocEntry { Title = "String Tables",      Glyph = "\uE8BD", FileName = "stringtable.md",         GithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/blob/Main/docs/stringtable.md"         },
        ];

        // Index of the first "tool" entry so we can insert a section header in-between
        private const int ToolsStartIndex = 2;

        // Track the GitHub URL for the currently viewed page
        private string _currentGithubUrl = "https://github.com/D3FEKT/ForzaTechStudio/tree/Main/docs";


        public DocumentationPage()
        {
            this.InitializeComponent();
            BuildNavList();
        }

        private void BuildNavList()
        {


            var items = new List<object>();
            bool toolsHeaderInserted = false;

            items.Add(new DocSectionHeader { Label = "GENERAL" });

            for (int i = 0; i < _entries.Length; i++)
            {
                if (i == ToolsStartIndex && !toolsHeaderInserted)
                {
                    items.Add(new DocSectionHeader { Label = "TOOLS" });
                    toolsHeaderInserted = true;
                }
                items.Add(_entries[i]);
            }

            NavList.ItemsSource = items;
            NavList.ItemTemplateSelector = new DocNavTemplateSelector(
                (DataTemplate)Resources["EntryTemplate"],
                (DataTemplate)Resources["SectionTemplate"]);
        }



        private async void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is not DocEntry entry) return;
            await LoadDocAsync(entry);
        }

        private void NavList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Item is DocSectionHeader)
            {
                args.ItemContainer.IsEnabled        = false;
                args.ItemContainer.IsHitTestVisible = false;
                args.ItemContainer.Opacity          = 1.0;
            }
        }


        private async Task LoadDocAsync(DocEntry entry)
        {
            _currentGithubUrl = entry.GithubUrl;
            BreadcrumbTitle.Text = entry.Title;

            PlaceholderPanel.Visibility = Visibility.Collapsed;
            ContentScroller.Visibility  = Visibility.Collapsed;
            LoadingOverlay.Visibility   = Visibility.Visible;

            string? markdown = await Task.Run(() => ReadDocFile(entry.FileName));

            LoadingOverlay.Visibility  = Visibility.Collapsed;

            if (markdown is null)
            {
                MdBlock.Text = $"# {entry.Title}\n\n> Could not load documentation file `docs/{entry.FileName}`.\n\nMake sure the application is running from the correct directory.";
            }
            else
            {
                MdBlock.Text = markdown;
            }

            ContentScroller.Visibility = Visibility.Visible;
            ContentScroller.ScrollToVerticalOffset(0);
        }

        private static string? ReadDocFile(string fileName)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "docs", fileName);
                if (File.Exists(path))
                    return File.ReadAllText(path);

                // Fallback: look two directories up (running from repo root in dev)
                string devPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", fileName);
                if (File.Exists(devPath))
                    return File.ReadAllText(devPath);

                return null;
            }
            catch
            {
                return null;
            }
        }

        // GitHub button 

        private async void GithubButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_currentGithubUrl));
        }

        // Markdown link clicks

        private async void MdBlock_LinkClicked(object sender, CommunityToolkit.WinUI.UI.Controls.LinkClickedEventArgs e)
        {
            if (Uri.TryCreate(e.Link, UriKind.Absolute, out Uri? uri))
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }

        // Navigate to a specific doc by title (called externally) 

        public async Task NavigateToDocAsync(string title)
        {
            foreach (var item in NavList.Items)
            {
                if (item is DocEntry entry && string.Equals(entry.Title, title, StringComparison.OrdinalIgnoreCase))
                {
                    NavList.SelectedItem = entry;
                    NavList.ScrollIntoView(entry);
                    await LoadDocAsync(entry);
                    return;
                }
            }
        }
    }

    // Section header placeholder 

    internal sealed class DocSectionHeader
    {
        public required string Label { get; init; }
    }


    internal sealed class DocNavTemplateSelector : DataTemplateSelector
    {
        private readonly DataTemplate _entryTemplate;
        private readonly DataTemplate _sectionTemplate;

        public DocNavTemplateSelector(DataTemplate entryTemplate, DataTemplate sectionTemplate)
        {
            _entryTemplate   = entryTemplate;
            _sectionTemplate = sectionTemplate;
        }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
            => item is DocSectionHeader ? _sectionTemplate : _entryTemplate;
    }
}
