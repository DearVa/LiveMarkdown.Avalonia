using Avalonia;
using Avalonia.Controls.Documents;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A snapshot of an inline's text range and rendered fragments. Obtain a new snapshot after
/// content or layout changes; wrapping and bidirectional text can produce multiple rectangles.
/// </summary>
public sealed class InlineGeometry
{
    /// <summary>
    /// Gets the text block whose coordinate space contains <see cref="Rects"/>.
    /// </summary>
    public MarkdownTextBlock TextBlock { get; }

    /// <summary>
    /// Gets the inline represented by this snapshot.
    /// </summary>
    public Inline Inline { get; }

    /// <summary>
    /// Gets the start in the block's local UTF-16 layout text, not in the Markdown source.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the length in the block's local layout text. Embedded controls occupy one position.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the rendered fragments in text-block coordinates. These are text-range boxes,
    /// not glyph outlines, and do not include custom backgrounds painted outside the range.
    /// </summary>
    public IReadOnlyList<Rect> Rects { get; }

    internal InlineGeometry(MarkdownTextBlock textBlock, Inline inline, int start, int length, IReadOnlyList<Rect> rects)
    {
        TextBlock = textBlock;
        Inline = inline;
        Start = start;
        Length = length;
        Rects = rects;
    }

    /// <summary>
    /// Transforms the snapshot's fragments to another visual using the current visual transform.
    /// Returns an empty list when the visuals do not share a visual root.
    /// </summary>
    public IReadOnlyList<Rect> GetRects(Visual relativeTo)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);
        if (TextBlock.TransformToVisual(relativeTo) is not { } transform) return [];
        return [.. Rects.Select(rect => rect.TransformToAABB(transform))];
    }
}