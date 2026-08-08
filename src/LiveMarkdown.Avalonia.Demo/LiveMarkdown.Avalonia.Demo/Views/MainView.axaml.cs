using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LiveMarkdown.Avalonia.Demo.ViewModels;

namespace LiveMarkdown.Avalonia.Demo.Views;

public partial class MainView : UserControl
{
    private const string SearchResultsHighlightName = "search-results";
    private const string SearchCurrentHighlightName = "search-current";

    private MainViewModel viewModel = null!;
    private IReadOnlyList<TextHighlightMatch> searchMatches = [];
    private TextHighlightMatch? currentSearchMatch;

    public MainView()
    {
        InitializeComponent();

        MarkdownRenderer.ImageBasePath = Path.Combine(AppContext.BaseDirectory, "samples");
        MarkdownTextBlock.SetHighlightStyles(RootPanel, CreateSearchHighlightStyles());

        MarkdownRenderer.LayoutUpdated += HandleMarkdownRendererLayoutUpdated;

        var rawScrollHelper = new AutoScrollHelper(RawMarkdownTextBlockScrollViewer);
        var renderScrollHelper = new AutoScrollHelper(MarkdownRendererScrollViewer);

        DataContext = viewModel = new MainViewModel();
        viewModel.AutoScrollEnabledChanged += OnAutoScrollEnabledChanged;
        viewModel.PropertyChanged += HandleViewModelPropertyChanged;
        viewModel.SearchChanged += HandleSearchChanged;
        viewModel.SearchNavigationRequested += HandleSearchNavigationRequested;

        void OnAutoScrollEnabledChanged(object? sender, bool enabled)
        {
            rawScrollHelper.IsEnabled = enabled;
            renderScrollHelper.IsEnabled = enabled;

            if (!enabled)
            {
                RawMarkdownTextBlockScrollViewer.Offset = Vector.Zero;
                MarkdownRendererScrollViewer.Offset = Vector.Zero;
            }
        }
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSearchOpen) && viewModel.IsSearchOpen)
        {
            Dispatcher.UIThread.Post(FocusSearchTextBox);
        }
    }

    private void HandleSearchChanged(object? sender, EventArgs e) => ApplySearch(viewModel.SearchQuery);

    private void HandleSearchNavigationRequested(object? sender, EventArgs e) => ApplyCurrentSearchMatch(scrollIntoView: true);

    private static TextHighlightStyles CreateSearchHighlightStyles()
    {
        var styles = new TextHighlightStyles();
        styles.Set(
            SearchResultsHighlightName,
            new TextHighlightStyle
            {
                Background = new SolidColorBrush(Color.FromArgb(96, 255, 193, 7)),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(1, 0),
            });
        styles.Set(
            SearchCurrentHighlightName,
            new TextHighlightStyle
            {
                Background = new SolidColorBrush(Color.FromArgb(192, 255, 152, 0)),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(1, 0),
            });
        return styles;
    }

    private void HandleMarkdownRendererLayoutUpdated(object? sender, EventArgs e)
    {
        if (!viewModel.IsSearchOpen || string.IsNullOrEmpty(viewModel.SearchQuery))
        {
            return;
        }

        var updatedMatches = MarkdownRenderer.TextSearchMatches;
        if (ReferenceEquals(searchMatches, updatedMatches))
        {
            return;
        }

        ClearCurrentSearchHighlight();
        searchMatches = updatedMatches;
        viewModel.SetSearchMatchCount(updatedMatches.Count);
        ApplyCurrentSearchMatch(scrollIntoView: false);
    }

    private void FocusSearchTextBox()
    {
        if (!viewModel.IsSearchOpen)
        {
            return;
        }

        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void ApplySearch(string? query)
    {
        ClearCurrentSearchHighlight();
        var options = TextSearchOptions.None;
        if (viewModel.SearchWholeWord)
        {
            options |= TextSearchOptions.WholeWord;
        }

        if (viewModel.SearchMatchCase)
        {
            options |= TextSearchOptions.MatchCase;
        }

        searchMatches = MarkdownRenderer.ApplyTextSearch(query, options);
        viewModel.SetSearchMatchCount(searchMatches.Count, resetCurrentIndex: true);
        ApplyCurrentSearchMatch(scrollIntoView: true);
    }

    private void ApplyCurrentSearchMatch(bool scrollIntoView)
    {
        ClearCurrentSearchHighlight();

        if (viewModel.SearchCurrentIndex < 0 || viewModel.SearchCurrentIndex >= searchMatches.Count)
        {
            return;
        }

        var match = searchMatches[viewModel.SearchCurrentIndex];
        match.Block.Highlights.Set(SearchCurrentHighlightName, [match.Range], priority: 1);
        currentSearchMatch = match;

        if (scrollIntoView)
        {
            BringSearchMatchIntoView(match);
        }
    }

    private void ClearCurrentSearchHighlight()
    {
        if (currentSearchMatch is not { } match)
        {
            return;
        }

        match.Block.Highlights.Remove(SearchCurrentHighlightName);
        currentSearchMatch = null;
    }

    private void BringSearchMatchIntoView(TextHighlightMatch match)
    {
        var bounds = match.Block.GetTextRangeBoundsInControl(match.Range.Start, match.Range.Length);
        if (bounds.Count == 0)
        {
            return;
        }

        var target = bounds[0];
        for (var i = 1; i < bounds.Count; i++)
        {
            target = target.Union(bounds[i]);
        }

        var targetCenter = match.Block.TranslatePoint(target.Center, MarkdownRendererScrollViewer);
        if (targetCenter is null)
        {
            return;
        }

        var scrollViewer = MarkdownRendererScrollViewer;
        var maximumOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var targetOffset = scrollViewer.Offset.Y + targetCenter.Value.Y - scrollViewer.Viewport.Height / 2;
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, Math.Clamp(targetOffset, 0, maximumOffset));
    }

    private async void HandleMarkdownRendererLinkClick(object? sender, LinkClickedEventArgs e)
    {
        try
        {
            if (e.HRef is { IsAbsoluteUri: true, Scheme: "http" or "https" } url)
            {
                var launcher = TopLevel.GetTopLevel(this)?.Launcher;
                if (launcher is not null)
                {
                    await launcher.LaunchUriAsync(url);
                }
            }
        }
        catch
        {
            // Ignore any exceptions when trying to open the link
        }
    }
}

public class AutoScrollHelper
{
    private bool isAtEnd = true;

    public bool IsEnabled 
    { 
        get;
        set
        {
            field = value;
            if (value)
            {
                // Whenever enabled again (e.g. Reset), assume we are starting from top but want tracking
                isAtEnd = true;
            }
        }
    }

    public AutoScrollHelper(ScrollViewer scrollViewer)
    {
        scrollViewer.PropertyChanged += OnScrollViewerPropertyChanged;
    }

    private void OnScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!IsEnabled) return;

        if (sender is not ScrollViewer scrollViewer) return;

        if (e.Property != ScrollViewer.OffsetProperty &&
            e.Property != ScrollViewer.ViewportProperty &&
            e.Property != ScrollViewer.ExtentProperty) return;

        if (e.Property == ScrollViewer.OffsetProperty)
        {
            isAtEnd = ((Vector)e.NewValue!).Y >= scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
        }

        if (isAtEnd)
        {
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, double.PositiveInfinity);
        }
    }
}