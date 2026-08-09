using Avalonia.VisualTree;
using Markdig.Helpers;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Controls the behavior of the convenience string-based text search.
/// </summary>
[Flags]
public enum TextSearchOptions
{
    /// <summary>
    /// Performs a case-insensitive, substring search.
    /// </summary>
    None = 0,

    /// <summary>
    /// Compares query text using ordinal case-sensitive comparison.
    /// </summary>
    MatchCase = 1 << 0,

    /// <summary>
    /// Restricts matches to whole words.
    /// </summary>
    WholeWord = 1 << 1,
}

/// <summary>
/// Produces local UTF-16 ranges for one rendered Markdown text block.
/// </summary>
/// <param name="block">The block currently being searched.</param>
/// <param name="text">The block's local layout text.</param>
public delegate IEnumerable<TextHighlightRange> TextSearchMatcher(MarkdownTextBlock block, string text);

partial class MarkdownRenderer
{
    /// <summary>
    /// The default highlight name used for the convenience string-based text search.
    /// </summary>
    public const string DefaultTextSearchHighlightName = "search-results";

    /// <summary>
    /// Gets the matches produced by the last <see cref="ApplyTextSearch(string?, string, int)"/> call.
    /// Each match points to the concrete text block and uses that block's local UTF-16 coordinates.
    /// </summary>
    public IReadOnlyList<TextHighlightMatch> TextSearchMatches { get; private set; } = [];

    /// <summary>
    /// Raised after the active text search results have been replaced or cleared.
    /// </summary>
    public event EventHandler? TextSearchMatchesChanged;

    private TextSearchMatcher? _textSearchMatcher;
    private string _textSearchHighlightName = DefaultTextSearchHighlightName;
    private string? _textSearchAppliedHighlightName;
    private int _textSearchPriority;
    private MarkdownTextBlock[]? _textBlocksCache;
    private MarkdownTextBlock[]? _selectableBlocksCache;
    private HashSet<MarkdownTextBlock>? _textSearchAppliedBlocks;
    private long? _pendingRenderedTextStateVersion;

    /// <summary>
    /// Finds and paints all matches produced by a caller-supplied matcher.
    /// The matcher is retained and invoked again after the Markdown document changes.
    /// </summary>
    /// <param name="matcher">A matcher that returns ranges in the supplied block-local text.</param>
    /// <param name="highlightName">Registry name used for the result ranges.</param>
    /// <param name="priority">Priority assigned to the result ranges.</param>
    /// <returns>The concrete block/range pairs in visual document order.</returns>
    public IReadOnlyList<TextHighlightMatch> ApplyTextSearch(
        TextSearchMatcher matcher,
        string highlightName = DefaultTextSearchHighlightName,
        int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentException.ThrowIfNullOrEmpty(highlightName);

        _textSearchMatcher = matcher;
        _textSearchHighlightName = highlightName;
        _textSearchPriority = priority;
        ApplyTextSearchCore();
        return TextSearchMatches;
    }

    /// <summary>
    /// Finds and paints all literal matches in the Markdown text blocks owned by this renderer.
    /// The search uses ordinal comparison so every match has a stable UTF-16 length.
    /// </summary>
    /// <param name="query">Literal text to find. An empty or null value clears the active search.</param>
    /// <param name="options">Case and whole-word options for the convenience matcher.</param>
    /// <param name="highlightName">Registry name used for the result ranges.</param>
    /// <param name="priority">Priority assigned to the result ranges.</param>
    /// <returns>The concrete block/range pairs in visual document order.</returns>
    public IReadOnlyList<TextHighlightMatch> ApplyTextSearch(
        string? query,
        TextSearchOptions options = TextSearchOptions.None,
        string highlightName = DefaultTextSearchHighlightName,
        int priority = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(highlightName);

        if (string.IsNullOrEmpty(query))
        {
            ClearTextSearch();
            return TextSearchMatches;
        }

        return ApplyTextSearch(new TextSearchPattern(query, options), highlightName, priority);
    }

    /// <summary>
    /// Finds and paints all matches produced by an immutable literal search pattern.
    /// The same pattern can also search an off-screen <see cref="MarkdownTextProjection"/>.
    /// </summary>
    /// <param name="pattern">The literal search pattern.</param>
    /// <param name="highlightName">Registry name used for the result ranges.</param>
    /// <param name="priority">Priority assigned to the result ranges.</param>
    /// <returns>The concrete block/range pairs in visual document order.</returns>
    public IReadOnlyList<TextHighlightMatch> ApplyTextSearch(
        TextSearchPattern pattern,
        string highlightName = DefaultTextSearchHighlightName,
        int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return ApplyTextSearch(
            static (_, text, state) => state.FindRanges(text),
            pattern,
            highlightName,
            priority);
    }

    /// <summary>
    /// Compatibility overload for callers that supplied the highlight name as the second argument.
    /// </summary>
    public IReadOnlyList<TextHighlightMatch> ApplyTextSearch(string? query, string highlightName, int priority = 0) =>
        ApplyTextSearch(query, TextSearchOptions.None, highlightName, priority);

    /// <summary>
    /// Clears the currently active search and removes its ranges from the blocks it touched.
    /// </summary>
    public void ClearTextSearch()
    {
        _textSearchMatcher = null;

        if (_textSearchAppliedBlocks is { } appliedBlocks)
        {
            foreach (var block in appliedBlocks)
            {
                block.Highlights.Remove(_textSearchAppliedHighlightName ?? _textSearchHighlightName);
            }

            appliedBlocks.Clear();
        }

        _textSearchAppliedHighlightName = null;

        if (TextSearchMatches.Count == 0)
        {
            return;
        }

        TextSearchMatches = [];
        TextSearchMatchesChanged?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<MarkdownTextBlock> GetTextBlocksInRenderer()
    {
        return _textBlocksCache ??=
        [
            .. documentNode.Control
                .GetSelfAndVisualDescendants()
                .OfType<MarkdownTextBlock>()
                .Where(block => block.IsVisible),
        ];
    }

    private IReadOnlyList<MarkdownTextBlock> GetSelectableBlocksInRenderer()
    {
        return _selectableBlocksCache ??= [.. this.GetSelfAndVisualDescendants().OfType<MarkdownTextBlock>(),];
    }

    internal void InvalidateTextBlockCache()
    {
        _textBlocksCache = null;
        _selectableBlocksCache = null;

        if (RenderedTextProjection is { } projection)
        {
            ScheduleRenderedTextStateRefresh(projection.SourceVersion);
        }
    }

    private void ScheduleRenderedTextStateRefresh(long sourceVersion)
    {
        _pendingRenderedTextStateVersion = sourceVersion;
        InvalidateArrange();
    }

    private void SchedulePendingRenderedTextStateRefresh()
    {
        if (_pendingRenderedTextStateVersion is not null) InvalidateArrange();
    }

    private void RefreshRenderedTextState()
    {
        if (VisualRoot is null || _pendingRenderedTextStateVersion is not { } sourceVersion) return;

        if (MarkdownBuilder is not { } builder || builder.Version != sourceVersion)
        {
            _pendingRenderedTextStateVersion = null;
            return;
        }

        _pendingRenderedTextStateVersion = null;
        SetRenderedTextProjection(CreateRenderedTextProjection(sourceVersion));
        ApplyTextSearchCore();
    }

    private MarkdownTextProjection CreateRenderedTextProjection(long sourceVersion)
    {
        var buffers = GetTextBlocksInRenderer()
            .Select(block => new MarkdownTextBuffer(block.SourceSpan, new StringSlice(block.LayoutText)))
            .ToArray();
        return new MarkdownTextProjection(sourceVersion, buffers);
    }

    private void ApplyTextSearchCore()
    {
        if (_textSearchMatcher is not { } matcher)
        {
            return;
        }

        var pendingRanges = new Dictionary<MarkdownTextBlock, IReadOnlyList<TextHighlightRange>>();
        var matches = new List<TextHighlightMatch>();

        foreach (var block in GetTextBlocksInRenderer())
        {
            var text = block.LayoutText;
            var ranges = NormalizeRanges(matcher(block, text), text.Length);
            if (ranges.Count == 0)
            {
                continue;
            }

            pendingRanges.Add(block, ranges);
            matches.AddRange(ranges.Select(range => new TextHighlightMatch(block, range)));
        }

        var previousHighlightName = _textSearchAppliedHighlightName;

        if (_textSearchAppliedBlocks is { } appliedBlocks)
        {
            foreach (var block in appliedBlocks)
            {
                if (previousHighlightName is not null &&
                    (!pendingRanges.ContainsKey(block) || previousHighlightName != _textSearchHighlightName))
                {
                    block.Highlights.Remove(previousHighlightName);
                }
            }
        }

        foreach (var (block, ranges) in pendingRanges)
        {
            if (block.Highlights.TryGetValue(_textSearchHighlightName, out var existing) &&
                existing.Priority == _textSearchPriority &&
                existing.Ranges.SequenceEqual(ranges))
            {
                continue;
            }

            block.Highlights.Set(_textSearchHighlightName, ranges, _textSearchPriority);
        }

        _textSearchAppliedBlocks = [.. pendingRanges.Keys];
        _textSearchAppliedHighlightName = pendingRanges.Count > 0 ? _textSearchHighlightName : null;
        TextSearchMatches = matches;
        TextSearchMatchesChanged?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<TextHighlightMatch> ApplyTextSearch<TState>(
        Func<MarkdownTextBlock, string, TState, IEnumerable<TextHighlightRange>> matcher,
        TState state,
        string highlightName,
        int priority)
    {
        _textSearchMatcher = (block, text) => matcher(block, text, state);
        _textSearchHighlightName = highlightName;
        _textSearchPriority = priority;
        ApplyTextSearchCore();
        return TextSearchMatches;
    }

    private static List<TextHighlightRange> NormalizeRanges(IEnumerable<TextHighlightRange> ranges, int textLength)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        var orderedRanges = new List<TextHighlightRange>();
        foreach (var range in ranges)
        {
            if (range.End > textLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ranges),
                    range,
                    "A text search range must be contained in the block's local text.");
            }

            if (range.Length > 0)
            {
                orderedRanges.Add(range);
            }
        }

        if (orderedRanges.Count < 2)
        {
            return orderedRanges;
        }

        orderedRanges.Sort(static (left, right) =>
        {
            var result = left.Start.CompareTo(right.Start);
            return result != 0 ? result : left.Length.CompareTo(right.Length);
        });

        var normalizedRanges = new List<TextHighlightRange>(orderedRanges.Count);
        var current = orderedRanges[0];

        for (var i = 1; i < orderedRanges.Count; i++)
        {
            var next = orderedRanges[i];
            if (next.Start < current.End)
            {
                current = new TextHighlightRange(current.Start, Math.Max(current.End, next.End) - current.Start);
                continue;
            }

            normalizedRanges.Add(current);
            current = next;
        }

        normalizedRanges.Add(current);
        return normalizedRanges;
    }

}