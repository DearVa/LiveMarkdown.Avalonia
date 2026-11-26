using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LiveMarkdown.Avalonia.Demo.Links;
using LiveMarkdown.Avalonia.Demo.Links.Preview;
using LiveMarkdown.Avalonia.Demo.ViewModels;

namespace LiveMarkdown.Avalonia.Demo.Views;

public partial class MainView : UserControl
{
    private const string SearchResultsHighlightName = "search-results";
    private const string SearchCurrentHighlightName = "search-current";

    private readonly MainViewModel viewModel;
    private IReadOnlyList<TextHighlightMatch> searchMatches = [];
    private TextHighlightMatch? currentSearchMatch;
    private DemoLinkInteraction? _linkInteraction;
    private WebPreviewService? _previews;

    public MainView()
    {
        InitializeComponent();

        MarkdownRenderer.ImageBasePath = Path.Combine(AppContext.BaseDirectory, "samples");
        MarkdownTextBlock.SetHighlightStyles(RootPanel, CreateSearchHighlightStyles());

        MarkdownRenderer.LayoutUpdated += HandleMarkdownRendererLayoutUpdated;
        AttachedToVisualTree += HandleAttached;
        DetachedFromVisualTree += HandleDetached;

        var rawScrollHelper = new AutoScrollHelper(RawMarkdownTextBlockScrollViewer);
        var renderScrollHelper = new AutoScrollHelper(MarkdownRendererScrollViewer);

        DataContext = viewModel = new MainViewModel();
        viewModel.AutoScrollEnabledChanged += OnAutoScrollEnabledChanged;
        viewModel.PropertyChanged += HandleViewModelPropertyChanged;
        viewModel.SearchChanged += HandleSearchChanged;
        viewModel.SearchNavigationRequested += HandleSearchNavigationRequested;
        viewModel.SearchFocusRequested += (_, _) => Dispatcher.UIThread.Post(FocusSearchTextBox);

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

    private void HandleAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            _previews = new WebPreviewService();
        }

        _linkInteraction = new DemoLinkInteraction(this, MarkdownRenderer, (_, result) =>
        {
            LinkOperationStatus.Text = result.Message;
            LinkStatusBar.IsVisible = true;
        }, _previews);
        // Install the icon hook before the streaming source can render its first document.
        MarkdownRenderer.MarkdownBuilder = viewModel.MarkdownBuilder;
        ShowSelectedPage();
    }

    private void HandleDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        InteractiveContent.Content = null;
        _linkInteraction?.Dispose();
        _linkInteraction = null;
        MarkdownRenderer.MarkdownBuilder = null;
        MarkdownRenderer.DocumentUpdate = null;
        _previews?.Dispose();
        _previews = null;
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.MarkdownBuilder) && _linkInteraction is not null)
        {
            MarkdownRenderer.MarkdownBuilder = viewModel.MarkdownBuilder;
        }

        if (e.PropertyName == nameof(MainViewModel.SelectedMarkdown) && _linkInteraction is not null)
        {
            _linkInteraction.Dismiss();
            ShowSelectedPage();
        }

        if (e.PropertyName == nameof(MainViewModel.IsSearchOpen) && viewModel.IsSearchOpen)
        {
            Dispatcher.UIThread.Post(FocusSearchTextBox);
        }
    }

    private void ShowSelectedPage()
    {
        LinkStatusBar.IsVisible = false;
        InteractiveContent.Content = viewModel.SelectedMarkdown switch
        {
            "Link" => new LinkDemoView { Previews = _previews },
            "Hooks" => new HooksDemoView { Previews = _previews },
            _ => null
        };
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
