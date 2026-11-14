using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media.TextFormatting;

namespace LiveMarkdown.Avalonia;

public partial class MarkdownTextBlock
{
    private IReadOnlyList<TextRun>? _inlineRangeTextRuns;
    private InlineCollection? _inlineRangeInlines;
    private readonly List<InlineRange> _inlineRanges = [];
    private readonly Dictionary<Inline, InlineRange> _inlineRangesBySource = [];
    private readonly Dictionary<Inline, InlineGeometry> _inlineGeometries = [];
    private TextLayout? _inlineGeometryLayout;
    private Point _inlineGeometryOrigin;

    /// <summary>
    /// Gets a snapshot of an inline's local text range and rendered fragments. Returns null
    /// when the inline is not owned by this block or layout has not run. This method does
    /// not measure controls or create a text layout. Call it again after layout changes.
    /// </summary>
    public InlineGeometry? GetInlineGeometry(Inline inline)
    {
        ArgumentNullException.ThrowIfNull(inline);
        if (GetTextLayout(this) is not { } layout) return null;

        EnsureInlineRanges(layout);
        if (!_inlineRangesBySource.TryGetValue(inline, out var range)) return null;
        if (_inlineGeometries.TryGetValue(inline, out var geometry)) return geometry;

        geometry = new InlineGeometry(this, inline, range.Start, range.Length, [.. GetTextRangeBoundsInControl(range.Start, range.Length)]);
        _inlineGeometries.Add(inline, geometry);
        return geometry;
    }

    /// <summary>
    /// Gets the deepest inline whose rendered text-range box contains a point in this block's
    /// coordinates. Returns null outside text, including empty space after a line.
    /// </summary>
    public Inline? GetInlineAt(Point point)
    {
        if (GetTextLayout(this) is not { } layout) return null;

        EnsureInlineRanges(layout);
        foreach (var range in _inlineRanges)
        {
            if (GetInlineGeometry(range.Source) is { } geometry && geometry.Rects.Any(rect => rect.Contains(point)))
            {
                return range.Source;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the innermost link at a point in this block's coordinates, independently of font
    /// features or nested formatting. Returns null outside its actual rendered fragments.
    /// </summary>
    public Link? GetLinkAt(Point point)
    {
        if (GetTextLayout(this) is not { } layout) return null;

        EnsureInlineRanges(layout);
        foreach (var range in _inlineRanges)
        {
            if (range.Source is Link link && GetInlineGeometry(link) is { } geometry && geometry.Rects.Any(rect => rect.Contains(point)))
            {
                return link;
            }
        }

        return null;
    }

    private void EnsureInlineRanges(TextLayout layout)
    {
        if (!ReferenceEquals(_inlineRangeTextRuns, _textRuns) || !ReferenceEquals(_inlineRangeInlines, Inlines))
        {
            _inlineRangeTextRuns = _textRuns;
            _inlineRangeInlines = Inlines;
            _inlineRanges.Clear();
            _inlineRangesBySource.Clear();
            _inlineGeometries.Clear();

            var position = 0;
            if (Inlines is { } inlines)
            {
                foreach (var inline in inlines)
                {
                    Append(inline);
                }
            }

            void Append(Inline inline)
            {
                var start = position;
                switch (inline)
                {
                    case Span span:
                        foreach (var child in span.Inlines)
                        {
                            Append(child);
                        }
                        break;
                    case Run run:
                        position += run.Text?.Length ?? 0;
                        break;
                    case LineBreak:
                        position += Environment.NewLine.Length;
                        break;
                    case InlineUIContainer:
                        position += TextRun.DefaultTextSourceLength;
                        break;
                }

                // Children precede their containers, so hit testing finds the deepest range.
                var range = new InlineRange(inline, start, position - start);
                _inlineRanges.Add(range);
                _inlineRangesBySource.Add(inline, range);
            }
        }

        var origin = GetTextLayoutOrigin();
        if (!ReferenceEquals(_inlineGeometryLayout, layout) || _inlineGeometryOrigin != origin)
        {
            _inlineGeometryLayout = layout;
            _inlineGeometryOrigin = origin;
            _inlineGeometries.Clear();
        }
    }

    private void ClearInlineGeometry()
    {
        _inlineRangeTextRuns = null;
        _inlineRangeInlines = null;
        _inlineRanges.Clear();
        _inlineRangesBySource.Clear();
        _inlineGeometries.Clear();
        _inlineGeometryLayout = null;
    }

    private readonly record struct InlineRange(Inline Source, int Start, int Length);
}