using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Media;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A UTF-16 text range used by <see cref="TextHighlightRegistry"/>.
/// </summary>
public readonly record struct TextHighlightRange
{
    /// <summary>
    /// Initializes a text range and validates its bounds.
    /// </summary>
    /// <param name="start">The zero-based UTF-16 start index.</param>
    /// <param name="length">The non-negative UTF-16 length.</param>
    public TextHighlightRange(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (start > int.MaxValue - length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Start = start;
        Length = length;
    }

    /// <summary>
    /// Gets the zero-based UTF-16 start index.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the UTF-16 length of the range.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the exclusive UTF-16 end index.
    /// </summary>
    public int End => Start + Length;
}

/// <summary>
/// Identifies one search match in a rendered Markdown tree.
/// </summary>
/// <param name="Block">The text block containing the match.</param>
/// <param name="Range">The local UTF-16 range of the match.</param>
public readonly record struct TextHighlightMatch(MarkdownTextBlock Block, TextHighlightRange Range);

/// <summary>
/// Describes one named highlight and its ranges in a single <see cref="MarkdownTextBlock"/>.
/// </summary>
public sealed class TextHighlight
{
    internal TextHighlight(string name, IReadOnlyList<TextHighlightRange> ranges, int priority, long order)
    {
        Name = name;
        Ranges = ranges;
        Priority = priority;
        Order = order;
    }

    /// <summary>
    /// Gets the registry name of the highlight.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the normalized, non-overlapping ranges in the highlight.
    /// </summary>
    public IReadOnlyList<TextHighlightRange> Ranges { get; }

    /// <summary>
    /// Gets the priority used when overlapping highlights are painted.
    /// </summary>
    public int Priority { get; }

    internal long Order { get; }
}

/// <summary>
/// Stores named text ranges for one <see cref="MarkdownTextBlock"/>.
/// </summary>
public sealed class TextHighlightRegistry
{
    private readonly Dictionary<string, TextHighlight> highlights = new(StringComparer.Ordinal);
    private TextHighlight[]? orderedHighlights;
    private long nextOrder;

    /// <summary>
    /// Raised after a named highlight is added, replaced, or removed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the number of named highlights in the registry.
    /// </summary>
    public int Count => highlights.Count;

    /// <summary>
    /// Gets the named highlights currently registered.
    /// </summary>
    public IReadOnlyCollection<TextHighlight> Values => highlights.Values;

    /// <summary>
    /// Tries to retrieve a named highlight.
    /// </summary>
    /// <param name="name">The highlight name.</param>
    /// <param name="highlight">The matching highlight, when found.</param>
    /// <returns><see langword="true"/> when the name is registered.</returns>
    public bool TryGetValue(string name, [NotNullWhen(true)] out TextHighlight? highlight)
    {
        ArgumentNullException.ThrowIfNull(name);
        return highlights.TryGetValue(name, out highlight);
    }

    /// <summary>
    /// Adds or replaces a named highlight and merges overlapping ranges.
    /// </summary>
    /// <param name="name">The highlight name.</param>
    /// <param name="ranges">The ranges in the block's local UTF-16 coordinates.</param>
    /// <param name="priority">The paint priority; lower values are painted first.</param>
    public void Set(string name, IEnumerable<TextHighlightRange> ranges, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(ranges);

        var normalizedRanges = NormalizeRanges(ranges);
        highlights[name] = new TextHighlight(name, normalizedRanges, priority, nextOrder++);
        orderedHighlights = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes a named highlight.
    /// </summary>
    /// <param name="name">The highlight name.</param>
    /// <returns><see langword="true"/> when a highlight was removed.</returns>
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!highlights.Remove(name))
        {
            return false;
        }

        orderedHighlights = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Removes all named highlights from the registry.
    /// </summary>
    public void Clear()
    {
        if (highlights.Count == 0)
        {
            return;
        }

        highlights.Clear();
        orderedHighlights = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal IReadOnlyList<TextHighlight> GetOrderedHighlights()
    {
        if (highlights.Count == 0)
        {
            return [];
        }

        return orderedHighlights ??=
        [
            .. highlights.Values.OrderBy(static highlight => highlight.Priority).ThenBy(static highlight => highlight.Order),
        ];
    }

    private static IReadOnlyList<TextHighlightRange> NormalizeRanges(IEnumerable<TextHighlightRange> ranges)
    {
        var orderedRanges = ranges
            .Where(static range => range.Length > 0)
            .OrderBy(static range => range.Start)
            .ThenBy(static range => range.Length)
            .ToArray();

        if (orderedRanges.Length < 2)
        {
            return orderedRanges;
        }

        var normalizedRanges = new List<TextHighlightRange>(orderedRanges.Length);
        var current = orderedRanges[0];

        for (var i = 1; i < orderedRanges.Length; i++)
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

/// <summary>
/// Paint-only properties for one named highlight. These properties never participate in text
/// shaping or line breaking.
/// </summary>
public sealed record TextHighlightStyle
{
    /// <summary>
    /// Gets or initializes the background brush used for the highlight.
    /// </summary>
    public IBrush? Background { get; init; }

    /// <summary>
    /// Gets or initializes the foreground brush used for the highlight.
    /// </summary>
    public IBrush? Foreground { get; init; }

    /// <summary>
    /// Gets or initializes the corner radius of the background paint.
    /// </summary>
    public CornerRadius CornerRadius { get; init; }

    /// <summary>
    /// Gets or initializes the padding applied to the background paint.
    /// </summary>
    public Thickness Padding { get; init; }
}

/// <summary>
/// A named style table that can be inherited by descendant Markdown text blocks.
/// </summary>
public sealed class TextHighlightStyles
{
    private readonly Dictionary<string, TextHighlightStyle> styles = new(StringComparer.Ordinal);

    /// <summary>
    /// Raised after a named style is added, replaced, or removed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Tries to retrieve a named highlight style.
    /// </summary>
    /// <param name="name">The style name.</param>
    /// <param name="style">The matching style, when found.</param>
    /// <returns><see langword="true"/> when the name is registered.</returns>
    public bool TryGetValue(string name, out TextHighlightStyle style) => styles.TryGetValue(name, out style!);

    /// <summary>
    /// Adds or replaces a named highlight style.
    /// </summary>
    /// <param name="name">The style name.</param>
    /// <param name="style">The paint-only style to associate with the name.</param>
    public void Set(string name, TextHighlightStyle style)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(style);

        styles[name] = style;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes a named highlight style.
    /// </summary>
    /// <param name="name">The style name.</param>
    /// <returns><see langword="true"/> when a style was removed.</returns>
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!styles.Remove(name))
        {
            return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Removes all named highlight styles.
    /// </summary>
    public void Clear()
    {
        if (styles.Count == 0)
        {
            return;
        }

        styles.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}