using System;
using System.Threading.Tasks;
using ForzaTechStudio.Services;
using ForzaTechStudio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ForzaTechStudio.Views;

public sealed class LibraryBrowserDialog : ContentDialog
{
    public LibraryBrowserViewModel BrowserViewModel { get; }

    private readonly ListView _listView;
    private readonly TextBox _searchBox;
    private readonly Button _clearButton;
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly TextBlock _summaryText;
    private readonly ProgressRing _progressRing;
    private readonly ComboBox _gameCombo;
    private readonly ComboBox _filterCombo;

    public LibraryBrowserDialog()
    {
        BrowserViewModel = new LibraryBrowserViewModel();

        Title = "Browse Game Library";
        PrimaryButtonText = "Open Selected";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.None;
        IsPrimaryButtonEnabled = false;

        // Override ContentDialog's built-in MaxWidth constraint (theme resource)
        this.Resources["ContentDialogMaxWidth"] = 1400.0;
        this.Resources["ContentDialogMinWidth"] = 1120.0;

        // Game + filter row
        _gameCombo = new ComboBox
        {
            Header = "Game",
            DisplayMemberPath = "DisplayName",
            SelectedValuePath = "GameId",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = BrowserViewModel.AvailableGames,
        };
        _gameCombo.SelectionChanged += (_, _) =>
        {
            if (_gameCombo.SelectedValue is string id)
                BrowserViewModel.SelectedGameId = id;
        };

        _filterCombo = new ComboBox
        {
            Header = "Asset Type",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = BrowserViewModel.LibraryFilters,
            SelectedItem = BrowserViewModel.SelectedLibraryFilter,
        };
        _filterCombo.SelectionChanged += (_, _) =>
        {
            if (_filterCombo.SelectedItem is string f)
                BrowserViewModel.SelectedLibraryFilter = f;
        };

        var gameRow = BuildTwoColumnRow(_gameCombo, _filterCombo, bottomMargin: 10);

        // Search row
        _searchBox = new TextBox
        {
            PlaceholderText = "Search by name or path…",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _searchBox.TextChanged += (_, _) => BrowserViewModel.SearchQuery = _searchBox.Text;

        _clearButton = new Button
        {
            Content = "×",
            Width = 32,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
        };
        _clearButton.Click += (_, _) =>
        {
            _searchBox.Text = string.Empty;
            BrowserViewModel.SearchQuery = string.Empty;
        };

        var searchRow = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(_searchBox);
        Grid.SetColumn(_clearButton, 1);
        searchRow.Children.Add(_clearButton);

        // Summary / spinner row
        _summaryText = new TextBlock
        {
            Text = "Loading…",
            Opacity = 0.7,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _progressRing = new ProgressRing
        {
            Width = 16,
            Height = 16,
            IsActive = false,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var infoRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 6),
        };
        infoRow.Children.Add(_progressRing);
        infoRow.Children.Add(_summaryText);

        // List
        _listView = new ListView
        {
            Height = 360,
            ItemsSource = BrowserViewModel.PagedItems,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <StackPanel Margin="0,4,0,4">
                        <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold"/>
                        <TextBlock Text="{Binding SecondaryText}" TextWrapping="Wrap"
                                   Opacity="0.65" FontSize="11" FontFamily="Consolas"/>
                    </StackPanel>
                </DataTemplate>
                """),
        };
        _listView.SelectionChanged += OnListSelectionChanged;
        _listView.DoubleTapped += OnListDoubleTapped;

        // Pagination row
        _prevButton = new Button { Content = "← Previous", IsEnabled = false };
        _prevButton.Click += (_, _) => { BrowserViewModel.GoToPreviousPageCommand.Execute(null); };

        _nextButton = new Button { Content = "Next →", IsEnabled = false };
        _nextButton.Click += (_, _) => { BrowserViewModel.GoToNextPageCommand.Execute(null); };

        var pageLabel = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.65,
            FontSize = 12,
        };

        var paginationRow = new Grid { Margin = new Thickness(0, 6, 0, 0), ColumnSpacing = 8 };
        paginationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        paginationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        paginationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        paginationRow.Children.Add(_prevButton);
        Grid.SetColumn(pageLabel, 1);
        paginationRow.Children.Add(pageLabel);
        Grid.SetColumn(_nextButton, 2);
        paginationRow.Children.Add(_nextButton);

        // Wire VM → UI
        BrowserViewModel.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(LibraryBrowserViewModel.PageSummaryText):
                case nameof(LibraryBrowserViewModel.CurrentPage):
                case nameof(LibraryBrowserViewModel.TotalPages):
                    pageLabel.Text = BrowserViewModel.PageSummaryText;
                    _summaryText.Text = BrowserViewModel.IsBusy ? "Loading…" : BrowserViewModel.PageSummaryText;
                    _prevButton.IsEnabled = BrowserViewModel.CurrentPage > 1;
                    _nextButton.IsEnabled = BrowserViewModel.CurrentPage < BrowserViewModel.TotalPages;
                    break;

                case nameof(LibraryBrowserViewModel.IsBusy):
                    _progressRing.IsActive = BrowserViewModel.IsBusy;
                    _progressRing.Visibility = BrowserViewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
                    if (BrowserViewModel.IsBusy) _summaryText.Text = "Loading…";
                    break;

                case nameof(LibraryBrowserViewModel.StatusMessage):
                    if (!BrowserViewModel.IsBusy)
                        _summaryText.Text = BrowserViewModel.PageSummaryText;
                    break;

                case nameof(LibraryBrowserViewModel.SearchQuery):
                    _clearButton.Visibility = string.IsNullOrEmpty(BrowserViewModel.SearchQuery)
                        ? Visibility.Collapsed : Visibility.Visible;
                    break;
            }
        };

        // Root panel
        var root = new StackPanel { Spacing = 0, MinWidth = 1120, MaxWidth = 1360 };
        root.Children.Add(gameRow);
        root.Children.Add(searchRow);
        root.Children.Add(infoRow);
        root.Children.Add(_listView);
        root.Children.Add(paginationRow);

        Content = root;

        Opened += async (_, _) => await BrowserViewModel.InitializeAsync();
    }

    public MaterialShaderLibraryAsset? SelectedItem => BrowserViewModel.SelectedItem;

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BrowserViewModel.SelectedItem = _listView.SelectedItem as MaterialShaderLibraryAsset;
        IsPrimaryButtonEnabled = BrowserViewModel.HasSelectedItem;
    }

    private void OnListDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (BrowserViewModel.HasSelectedItem)
            Hide();
    }

    private static Grid BuildTwoColumnRow(FrameworkElement left, FrameworkElement right, double bottomMargin = 0)
    {
        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, bottomMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }
}
