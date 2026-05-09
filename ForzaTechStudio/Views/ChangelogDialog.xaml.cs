using System;
using System.IO;
using Microsoft.UI.Xaml.Controls;

namespace ForzaTechStudio.Views;

public sealed partial class ChangelogDialog : ContentDialog
{
    public ChangelogDialog()
    {
        this.InitializeComponent();
        LoadChangelog();
    }

    private void LoadChangelog()
    {
        try
        {
            // changelogs.md is copied to the output directory under docs/
            string changelogPath = Path.Combine(AppContext.BaseDirectory, "docs", "changelogs.md");

            if (File.Exists(changelogPath))
            {
                MdBlock.Text = File.ReadAllText(changelogPath);
            }
            else
            {
                MdBlock.Text = "Changelog file not found.";
            }
        }
        catch (Exception ex)
        {
            MdBlock.Text = $"Failed to load changelog: {ex.Message}";
        }
    }
}
