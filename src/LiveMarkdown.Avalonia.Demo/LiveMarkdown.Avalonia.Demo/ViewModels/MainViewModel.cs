using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShadUI;
using TextMateSharp.Grammars;

namespace LiveMarkdown.Avalonia.Demo.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial ObservableStringBuilder MarkdownBuilder { get; private set; } = new();

    public ObservableCollection<NavigationBarItem> NavigationItems { get; } = [];

    [ObservableProperty]
    public partial string? RawMarkdownText { get; private set; }

    [ObservableProperty]
    public partial double RenderSpeed { get; set; } = 30d;

    public MarkdownStreamingRevealMode[] RevealModes { get; } = Enum.GetValues<MarkdownStreamingRevealMode>();

    [ObservableProperty]
    public partial MarkdownStreamingRevealMode RevealMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevealDuration))]
    public partial double RevealMilliseconds { get; set; } = 220;

    public TimeSpan RevealDuration => TimeSpan.FromMilliseconds(RevealMilliseconds);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevealRiseDuration))]
    public partial double RevealRiseMilliseconds { get; set; } = 380;

    public TimeSpan RevealRiseDuration => TimeSpan.FromMilliseconds(RevealRiseMilliseconds);

    [ObservableProperty]
    public partial double RevealOffset { get; set; } = 4;

    [ObservableProperty]
    public partial bool IsSidebarExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    [ObservableProperty]
    public partial string? SearchQuery { get; set; }

    [ObservableProperty]
    public partial bool SearchWholeWord { get; set; }

    [ObservableProperty]
    public partial bool SearchMatchCase { get; set; }

    [ObservableProperty]
    public partial int SearchCurrentIndex { get; private set; } = -1;

    [ObservableProperty]
    public partial int SearchMatchCount { get; private set; }

    public string SearchResultCountText => SearchMatchCount == 0 ? "0/0" : $"{SearchCurrentIndex + 1}/{SearchMatchCount}";

    public bool HasSearchMatches => SearchMatchCount > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSamplePage))]
    [NotifyPropertyChangedFor(nameof(IsStreamingSample))]
    public partial string? SelectedMarkdown { get; private set; }

    public bool IsSamplePage => SelectedMarkdown is not ("Link" or "Hooks");

    public bool IsStreamingSample => SelectedMarkdown == "Streaming";

    public NavigationBarItem? SelectedItem
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;

            if (value?.Content is string markdownName)
            {
                SelectedMarkdown = markdownName;
                if (IsSamplePage)
                {
                    RenderMarkdownAsync(markdownName, animate: false).Detach();
                }
                else
                {
                    cancellationTokenSource?.Cancel();
                    ClearMarkdown();
                    IsSearchOpen = false;
                }
            }
        }
    }

    public ThemeName[] AvailableColorThemes { get; } = Enum.GetValues<ThemeName>();

    [ObservableProperty]
    public partial ThemeName SelectedColorTheme { get; set; }

    public event EventHandler<bool>? AutoScrollEnabledChanged;

    /// <summary>
    /// Raised when the search query or its matching options change.
    /// The view handles the renderer-specific search operation.
    /// </summary>
    public event EventHandler? SearchChanged;

    /// <summary>
    /// Raised after a navigation command changes the selected search result.
    /// The view owns highlighting and scrolling because those operations target controls.
    /// </summary>
    public event EventHandler? SearchNavigationRequested;

    /// <summary>
    /// Raised when an already-open search should regain focus, such as a repeated Ctrl+F.
    /// </summary>
    public event EventHandler? SearchFocusRequested;

    private CancellationTokenSource? cancellationTokenSource;

    public MainViewModel()
    {
        // We don't use embedded resources here to allow easy modification of sample files.
        var markdownFolderPath = Path.Combine(AppContext.BaseDirectory, "samples");
        foreach (var markdownFilePath in Directory.EnumerateFiles(markdownFolderPath, "*.md")
                     .OrderByDescending(path => path.EndsWith("README.md", StringComparison.OrdinalIgnoreCase))
                     .ThenBy(path => path, StringComparer.Ordinal))
        {
            var fileName = Path.GetFileNameWithoutExtension(markdownFilePath);
            NavigationItems.Add(
                new NavigationBarItem
                {
                    Content = fileName,
                    Route = fileName
                });
        }
        NavigationItems.Add(new NavigationBarItem { Content = "Hooks", Route = "Hooks" });
        SelectedItem = NavigationItems.FirstOrDefault();
    }

    private void ClearMarkdown()
    {
        RawMarkdownText = string.Empty;
        MarkdownBuilder.Clear();
    }

    [RelayCommand]
    private void ResetMarkdown()
    {
        if (!string.IsNullOrEmpty(SelectedMarkdown))
        {
            RenderMarkdownAsync(SelectedMarkdown, animate: true).Detach();
        }
    }

    [RelayCommand]
    private void OpenSearch()
    {
        if (!IsSamplePage)
        {
            return;
        }

        var wasOpen = IsSearchOpen;
        IsSearchOpen = true;
        if (wasOpen)
        {
            SearchFocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void CloseSearch() => IsSearchOpen = false;

    [RelayCommand]
    private void PreviousSearchResult() => MoveSearchResult(-1);

    [RelayCommand]
    private void NextSearchResult() => MoveSearchResult(1);

    [RelayCommand]
    private static void ToggleTheme()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Light ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    private async Task RenderMarkdownAsync(string? markdownFileName, bool animate = true)
    {
        try
        {
            AutoScrollEnabledChanged?.Invoke(this, animate);

            if (cancellationTokenSource is not null)
                await cancellationTokenSource.CancelAsync();

            if (string.IsNullOrWhiteSpace(markdownFileName))
                return;

            var markdownFilePath = Path.Combine(AppContext.BaseDirectory, "samples", markdownFileName + ".md");
            if (!File.Exists(markdownFilePath)) return;

            cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            if (!animate)
            {
                using var reader = new StreamReader(markdownFilePath);
                var fullText = await reader.ReadToEndAsync(cancellationToken);
                RawMarkdownText = fullText;
                // A sample switch is a full document load, not a chunk of the previous stream.
                MarkdownBuilder = new ObservableStringBuilder(fullText);
                return;
            }

            ClearMarkdown();

            async IAsyncEnumerable<string> ReadBlocksAsync()
            {
                var buffer = Array.Empty<char>();
                using var reader = new StreamReader(markdownFilePath);
                while (!cancellationToken.IsCancellationRequested)
                {
                    var speed = Math.Max((int)RenderSpeed, 1);
                    if (buffer.Length != speed)
                    {
                        // RenderSpeed can be changed dynamically, so adjust buffer size accordingly
                        buffer = new char[speed];
                    }

                    var readCount = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                    if (readCount <= 0) break;

                    var newText = new string(buffer, 0, readCount);
                    RawMarkdownText += newText;
                    yield return newText;
                }
            }

            await MarkdownBuilder.EnumerateAppendAsync(ReadBlocksAsync(), TimeSpan.FromMilliseconds(100), cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error rendering markdown: {ex.Message}");
        }
    }

    internal void SetSearchMatchCount(int count, bool resetCurrentIndex = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var currentIndex = count == 0 ? -1 : resetCurrentIndex ? 0 : Math.Clamp(SearchCurrentIndex, 0, count - 1);

        if (SearchCurrentIndex != currentIndex)
        {
            SearchCurrentIndex = currentIndex;
        }

        if (SearchMatchCount != count)
        {
            SearchMatchCount = count;
        }
    }

    partial void OnIsSearchOpenChanged(bool value)
    {
        if (!value && !string.IsNullOrEmpty(SearchQuery))
        {
            SearchQuery = string.Empty;
        }
    }

    partial void OnSearchQueryChanged(string? value) => SearchChanged?.Invoke(this, EventArgs.Empty);

    partial void OnSearchWholeWordChanged(bool value) => SearchChanged?.Invoke(this, EventArgs.Empty);

    partial void OnSearchMatchCaseChanged(bool value) => SearchChanged?.Invoke(this, EventArgs.Empty);

    partial void OnSearchCurrentIndexChanged(int value) => OnPropertyChanged(nameof(SearchResultCountText));

    partial void OnSearchMatchCountChanged(int value)
    {
        OnPropertyChanged(nameof(SearchResultCountText));
        OnPropertyChanged(nameof(HasSearchMatches));
    }

    private void MoveSearchResult(int delta)
    {
        if (SearchMatchCount == 0)
        {
            return;
        }

        SearchCurrentIndex = (SearchCurrentIndex + delta + SearchMatchCount) % SearchMatchCount;
        SearchNavigationRequested?.Invoke(this, EventArgs.Empty);
    }
}
