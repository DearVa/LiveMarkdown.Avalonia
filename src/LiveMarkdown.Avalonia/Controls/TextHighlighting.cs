using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Media;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A UTF-16 text range used by <see cref="TextHighlightRegistry"/>.
/// </summary>
public readonly record struct TextHighlightRange
{
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

    public int Start { get; }

    public int Length { get; }

    public int End => Start + Length;
}

/// <summary>
/// Identifies one search match in a rendered Markdown tree.
/// </summary>
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

    public string Name { get; }

    public IReadOnlyList<TextHighlightRange> Ranges { get; }

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

    public event EventHandler? Changed;

    public int Count => highlights.Count;

    public IReadOnlyCollection<TextHighlight> Values => highlights.Values;

    public bool TryGetValue(string name, [NotNullWhen(true)] out TextHighlight? highlight)
    {
        ArgumentNullException.ThrowIfNull(name);
        return highlights.TryGetValue(name, out highlight);
    }

    public void Set(string name, IEnumerable<TextHighlightRange> ranges, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(ranges);

        var normalizedRanges = NormalizeRanges(ranges);
        highlights[name] = new TextHighlight(name, normalizedRanges, priority, nextOrder++);
        orderedHighlights = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

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
            if (next.Start <= current.End)
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
    public IBrush? Background { get; init; }

    public IBrush? Foreground { get; init; }

    public CornerRadius CornerRadius { get; init; }

    public Thickness Padding { get; init; }
}

/// <summary>
/// A named style table that can be inherited by descendant Markdown text blocks.
/// </summary>
public sealed class TextHighlightStyles
{
    private readonly Dictionary<string, TextHighlightStyle> styles = new(StringComparer.Ordinal);

    public event EventHandler? Changed;

    public bool TryGetValue(string name, out TextHighlightStyle style) => styles.TryGetValue(name, out style!);

    public void Set(string name, TextHighlightStyle style)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(style);

        styles[name] = style;
        Changed?.Invoke(this, EventArgs.Empty);
    }

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