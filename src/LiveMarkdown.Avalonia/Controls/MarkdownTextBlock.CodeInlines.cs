using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.TextFormatting;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Locating inline-code chips from a point, and getting their bounds back.
///
/// <para>Since <c>CodeInline</c> became a <see cref="Avalonia.Controls.Documents.Run"/> the chip is drawn
/// inside the block's own text layout, which is the right shape for rendering and leaves a host with no way to
/// make a chip INTERACTIVE: there is no control to attach a handler to, and no bounds to position an
/// affordance against. A run is not an input element.</para>
///
/// <para>Links already have this: <c>Link.HitTestPoint</c> tags the shaped run and finds it again. This is the
/// equivalent for code chips, built on the span table the block already computes in order to paint their
/// backgrounds — a renderer that draws something a host may want to make clickable should let it find out what
/// was clicked.</para>
/// </summary>
public partial class MarkdownTextBlock
{
    /// <summary>
    /// the <see cref="TextLayout"/> property will create a new layout if it is null, may cause a side effect.
    /// </summary>
    /// <param name="block"></param>
    /// <returns></returns>
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_textLayout")]
    private static extern ref TextLayout? GetTextLayout(TextBlock block);

    /// <summary>
    /// The inline-code chip at <paramref name="point"/> (in this block's coordinates), or null when the point
    /// is not on one.
    /// </summary>
    /// <remarks>
    /// Tested against the chip's PAINTED rects, deliberately not via <c>TextLayout.HitTestPoint</c>. That method
    /// has caret semantics — it snaps to the nearest character BOUNDARY — so it reports a chip from up to half a
    /// character before the chip actually starts, and reports the trailing chip of a line for the whole empty
    /// remainder of that line. Measured here: with a chip painted from x=112, HitTestPoint already returned the
    /// chip's first index at x=106. For "is the pointer on the chip" the answer has to be the thing the user can
    /// see, which is the background rect.
    /// </remarks>
    public CodeInline? GetCodeInlineAt(Point point)
    {
        if (GetTextLayout(this) is not { } textLayout) return null;

        foreach (var span in GetCodeInlineSpans())
        {
            foreach (var rect in GetCodeInlineSpanRects(textLayout, span))
            {
                if (rect.Contains(point)) return span.Source;
            }
        }

        return null;
    }

    /// <summary>
    /// Where <paramref name="inline"/> is drawn, in this block's coordinates. Empty when the inline is not in
    /// this block or the layout has not run yet; MORE THAN ONE rect when the chip wrapped across lines, which is
    /// why this returns a list rather than a single rect — a caller positioning an affordance against the chip's
    /// end wants the LAST one, not a union that spans the gutter.
    /// </summary>
    public IReadOnlyList<Rect> GetCodeInlineRects(CodeInline inline)
    {
        if (GetTextLayout(this) is not { } textLayout) return [];

        foreach (var span in GetCodeInlineSpans())
        {
            if (ReferenceEquals(span.Source, inline)) return GetCodeInlineSpanRects(textLayout, span);
        }

        return [];
    }

    /// <summary>The painted rects for one span: the glyph band grown by the chip's own padding, which is what
    /// the block fills when it draws the background — so a caller's hit area and affordance both line up with
    /// what is on screen rather than with the text alone.</summary>
    private static List<Rect> GetCodeInlineSpanRects(TextLayout textLayout, CodeInlineSpan span)
    {
        if (span.Length <= 0) return [];

        List<Rect>? rects = null;
        var y = 0d;
        foreach (var line in textLayout.TextLines)
        {
            var lineStart = line.FirstTextSourceIndex;
            var start = Math.Max(span.Start, lineStart);
            var end = Math.Min(span.End, lineStart + line.Length);

            if (start < end)
            {
                foreach (var bounds in line.GetTextBounds(start, end - start))
                {
                    var rect = bounds.Rectangle.Translate(new Vector(0, y));
                    (rects ??= []).Add(new Rect(
                        rect.X - span.Padding.Left,
                        rect.Y - span.Padding.Top,
                        rect.Width + span.Padding.Left + span.Padding.Right,
                        rect.Height + span.Padding.Top + span.Padding.Bottom));
                }
            }

            y += line.Height;
        }

        return rects ?? [];
    }
}
