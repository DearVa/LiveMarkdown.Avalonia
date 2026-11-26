using System.Diagnostics;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;

namespace LiveMarkdown.Avalonia;

public partial class MarkdownTextBlock
{
    internal StreamingRevealSnapshot? CreateRevealSnapshot(
        int firstActiveLineIndex,
        Point origin,
        IReadOnlyList<StreamingRevealSpan> spans,
        MarkdownStreamingRevealMode mode,
        TimeSpan fadeDuration,
        TimeSpan? riseDuration = null,
        double riseOffset = 0)
    {
        var effectiveRiseDuration = riseOffset > 0 ? riseDuration ?? fadeDuration : TimeSpan.Zero;
        if (fadeDuration <= TimeSpan.Zero && effectiveRiseDuration <= TimeSpan.Zero) return null;

        var geometry = GetLineGeometry();
        if (firstActiveLineIndex >= geometry.Length || spans.Count == 0) return null;

        var paint = GetPaintSnapshot(_textRuns);
        var highlights = GetRegisteredHighlightPaintSnapshot();
        var latest = spans.Max(s => s.Timestamp);
        var lines = new RevealLine[geometry.Length - firstActiveLineIndex];
        var bounds = default(Rect);
        var totalDuration = TimeSpan.Zero;
        List<IDisposable>? resources = [];
        try
        {
            for (var i = firstActiveLineIndex; i < geometry.Length; i++)
            {
                var geo = geometry[i];
                var line = geo.TextLine;
                var x = origin.X + line.Start;
                var y = origin.Y + geo.Y;
                var textBounds = new Rect(x, y, line.WidthIncludingTrailingWhitespace, line.Height);
                var lineBounds = textBounds;
                var glyphs = new List<RevealGlyph>();
                var backgrounds = new List<RevealBackground>();
                var positions = mode == MarkdownStreamingRevealMode.Character ? new List<(ShapedTextRun Run, double X)>() : null;
                var needsMask = false;

                CollectBackgrounds(paint.BackgroundSpans);
                CollectBackgrounds(highlights.BackgroundSpans);

                foreach (var run in line.TextRuns)
                {
                    if (run is not DrawableTextRun drawable) continue;
                    if (run is ShapedTextRun positioned) positions?.Add((positioned, x));
                    if (run is ShapedTextRun { Properties.ForegroundBrush: { } brush } shaped &&
                        shaped.Properties.Typeface != default && shaped.GlyphRun.GlyphInfos.Count != 0)
                    {
                        var glyph = shaped.GlyphRun;
                        var foreground = Freeze(brush, resources);
                        var background = Freeze(shaped.Properties.BackgroundBrush, resources);
                        if (foreground != null)
                        {
                            var reference = glyph.TryCreateImmutableGlyphRunReference();
                            if (reference == null) return null;
                            resources.Add(reference);
                            var at = new Point(x, y + GetRevealBaselineOffset(line, shaped));
                            var strokes = GetRevealDecorations(shaped, foreground, resources);
                            var offset = new Vector(at.X, at.Y);
                            var ink = glyph.InkBounds.Union(glyph.Bounds);
                            foreach (var stroke in strokes)
                            {
                                ink = ink.Union(new Rect(stroke.Start, stroke.End).Inflate(stroke.Pen.Thickness / 2));
                            }

                            ink = ink.Translate(offset);
                            lineBounds = lineBounds.Union(ink);

                            needsMask |= foreground is not IImmutableSolidColorBrush ||
                                background != null ||
                                strokes.Any(stroke => stroke.Pen.Brush is not IImmutableSolidColorBrush);
                            glyphs.Add(new RevealGlyph(at, glyph.Bounds, ink, foreground, background, reference, strokes));
                        }
                    }

                    // An embedded inline control is already drawn as a visual child, but
                    // its advance is part of TextLine.Draw's positioning of later runs.
                    x += drawable.Size.Width;
                }

                var bands = new List<RevealBand>();
                TimeSpan? lineAge = null;
                if (positions is not null)
                {
                    if (fadeDuration > TimeSpan.Zero)
                    {
                        CollectRevealBands(positions, spans, LayoutText, geo.Start, latest, textBounds, lineBounds, bands);
                    }

                    var lineStart = geo.Start;
                    var text = LayoutText;
                    var lineEnd = Math.Min(geo.End, text.Length);
                    while (lineStart < lineEnd && text[lineStart] is '\r' or '\n' or '\u2028' or '\u2029')
                    {
                        lineStart++;
                    }

                    if (lineStart < lineEnd)
                    {
                        foreach (var span in spans)
                        {
                            if (span.Start <= lineStart && lineStart < span.Start + span.Length)
                            {
                                lineAge = Stopwatch.GetElapsedTime(span.Timestamp, latest);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    foreach (var span in spans)
                    {
                        if (i >= span.Start && i < span.Start + span.Length)
                        {
                            lineAge = Stopwatch.GetElapsedTime(span.Timestamp, latest);
                        }
                    }
                }

                for (var j = bands.Count - 1; j >= 0; j--)
                {
                    if (bands[j].Age >= fadeDuration)
                    {
                        bands.RemoveAt(j);
                    }
                }

                bands.Sort(static (a, b) => a.Left.CompareTo(b.Left));
                for (var j = bands.Count - 1; j > 0; j--)
                {
                    var left = bands[j - 1];
                    var right = bands[j];
                    if (left.Age == right.Age && right.Left <= left.Right + 0.001)
                    {
                        bands[j - 1] = left with { Right = Math.Max(left.Right, right.Right) };
                        bands.RemoveAt(j);
                    }
                }

                foreach (var band in bands)
                {
                    var remainingFade = fadeDuration - band.Age;
                    if (remainingFade > totalDuration) totalDuration = remainingFade;
                }

                if (lineAge is { } currentAge)
                {
                    if (mode == MarkdownStreamingRevealMode.Line && fadeDuration > currentAge)
                    {
                        var remainingFade = fadeDuration - currentAge;
                        if (remainingFade > totalDuration) totalDuration = remainingFade;
                    }
                    if (effectiveRiseDuration > currentAge)
                    {
                        var remainingRise = effectiveRiseDuration - currentAge;
                        if (remainingRise > totalDuration) totalDuration = remainingRise;
                    }
                }

                lines[i - firstActiveLineIndex] = new RevealLine(
                    lineBounds,
                    [.. glyphs],
                    [.. backgrounds],
                    [.. bands],
                    lineAge,
                    needsMask || backgrounds.Count != 0);
                bounds = bounds.Union(lineBounds);
                if (riseOffset > 0 && effectiveRiseDuration > TimeSpan.Zero &&
                    lineAge is { } riseAge && riseAge < effectiveRiseDuration)
                {
                    bounds = bounds.Union(lineBounds.Translate(new Vector(0, riseOffset)));
                }

                void CollectBackgrounds(TextPaintSpan[] paintSpans)
                {
                    foreach (var span in paintSpans)
                    {
                        var start = Math.Max(geo.Start, span.Start);
                        var end = Math.Min(geo.End, span.Start + span.Length);
                        if (end <= start) continue;

                        var fill = Freeze(span.Brush, resources);
                        var pen = span.BorderPen is { } p ? new ImmutablePen(Freeze(p.Brush, resources), p.Thickness) : null;
                        Rect? pending = null;
                        var leading = start == span.Start ? span.BackgroundInset.Left : 0;
                        foreach (var part in line.GetTextBounds(start, end - start))
                        {
                            var rect = part.Rectangle.WithY(geo.Y);
                            if (pending is { } last && CanMergeBounds(last, rect))
                            {
                                pending = last.WithWidth(last.Width + rect.Width);
                            }
                            else
                            {
                                if (pending is { } prev)
                                {
                                    Add(prev, leading, 0);
                                    leading = 0;
                                }

                                pending = rect;
                            }
                        }

                        if (pending is { } tail)
                        {
                            Add(tail, leading, end == span.Start + span.Length ? span.BackgroundInset.Right : 0);
                        }

                        void Add(Rect value, double leadingMargin, double trailingMargin)
                        {
                            var rect = GetPaintRectangle(value, span, leadingMargin, trailingMargin);
                            var ink = rect.Inflate((pen?.Thickness ?? 0) / 2);
                            if (span.CornerRadius.IsUniform)
                            {
                                backgrounds.Add(new RevealBackground(rect, span.CornerRadius.TopLeft, fill, pen, null));
                            }
                            else
                            {
                                // ImmediateDrawingContext exposes only uniform radii. Record
                                // the original vector primitive for asymmetric corners, including
                                // translucent borders; no rasterization or backend-specific API.
                                // VisualBrush clips its recording to the visual's bounds.
                                // Leave a device pixel for antialiasing, or the stroke's
                                // fractional edge would be clipped a second time.
                                var padded = ink.Inflate(1 / LayoutHelper.GetLayoutScale(this));
                                var recordingBounds = new Rect(
                                    new Point(Math.Floor(padded.X), Math.Floor(padded.Y)),
                                    new Point(Math.Ceiling(padded.Right), Math.Ceiling(padded.Bottom)));
                                var stamp = new PillStamp(recordingBounds, new RoundedRect(rect, span.CornerRadius), fill, pen);
                                if (((ISceneBrush)new VisualBrush(stamp)).CreateContent() is { } scene)
                                {
                                    resources.Add(scene);
                                    backgrounds.Add(new RevealBackground(recordingBounds, 0, null, null, scene));
                                }
                            }

                            lineBounds = lineBounds.Union(ink.Translate(new Vector(origin.X, origin.Y)));
                        }
                    }
                }
            }

            if (totalDuration <= TimeSpan.Zero) return null;

            var result = new StreamingRevealSnapshot(
                lines,
                bounds,
                fadeDuration,
                effectiveRiseDuration,
                totalDuration,
                riseOffset,
                latest,
                origin,
                resources);
            resources = null; // Ownership transfers with the message.
            return result;
        }
        finally
        {
            // Construction can stop before a snapshot exists. Sent snapshots, including
            // ones superseded in the same compositor batch, are disposed by the handler.
            if (resources != null)
            {
                foreach (var resource in resources)
                {
                    resource.Dispose();
                }
            }
        }
    }

    private static void CollectRevealBands(
        List<(ShapedTextRun Run, double X)> runs,
        IReadOnlyList<StreamingRevealSpan> spans,
        string text,
        int source,
        long latest,
        Rect textBounds,
        Rect ink,
        List<RevealBand> bands)
    {
        // 12.0's TextLine.GetTextBounds can anchor *every* RTL substring at the
        // right edge of the run. Use the shaped glyphs' actual visual advances.
        // TextLine exposes runs in visual order; invert Unicode bidi rule L2
        // (reverse each >=level range, lowest level first) to assign source offsets.
        var maxLevel = 0;
        foreach (var (run, _) in runs) maxLevel = Math.Max(maxLevel, (int)run.BidiLevel);
        for (var level = 1; level <= maxLevel; level++)
        {
            for (var i = 0; i < runs.Count;)
            {
                if (runs[i].Run.BidiLevel < level)
                {
                    i++;
                    continue;
                }

                var first = i++;
                while (i < runs.Count && runs[i].Run.BidiLevel >= level) i++;
                runs.Reverse(first, i - first);
            }
        }

        foreach (var (run, x) in runs)
        {
            // Embedded controls occupy a layout character but have no shaped glyphs.
            while (source < text.Length && text[source] == '\uFFFC' && !run.Text.Span.StartsWith("\uFFFC"))
            {
                source++;
            }

            var glyphs = run.GlyphRun.GlyphInfos;
            if (glyphs.Count > 0)
            {
                // Use actual visual advances. In particular, a pre-shaped CodeInline
                // can have ascending clusters even when its bidi level is odd.
                var first = glyphs[0].GlyphCluster;
                var last = glyphs[^1].GlyphCluster;
                var clusterOrigin = Math.Min(first, last);
                var descending = first > last;
                var spanIndex = descending ? spans.Count - 1 : 0;
                var previous = -1;
                var left = x;
                foreach (var glyph in glyphs)
                {
                    var right = left + glyph.GlyphAdvance;
                    var index = source + glyph.GlyphCluster - clusterOrigin;
                    while (spanIndex >= 0 && spanIndex < spans.Count &&
                           (descending ? index < spans[spanIndex].Start : index >= spans[spanIndex].Start + spans[spanIndex].Length))
                    {
                        spanIndex += descending ? -1 : 1;
                    }

                    if (spanIndex < 0 || spanIndex >= spans.Count) break;
                    var span = spans[spanIndex];
                    if (index >= span.Start && index < span.Start + span.Length && right > left)
                    {
                        var edge = right >= textBounds.Right - 0.001 ? ink.Right : right;
                        if (previous == spanIndex)
                        {
                            bands[^1] = bands[^1] with { Right = edge };
                        }
                        else
                        {
                            bands.Add(
                                new RevealBand(
                                    left <= textBounds.X + 0.001 ? ink.X : left,
                                    edge,
                                    Stopwatch.GetElapsedTime(span.Timestamp, latest)));
                        }

                        previous = spanIndex;
                    }
                    left = right;
                }
            }
            source += run.Length;
        }
    }

    private sealed class PillStamp : Visual
    {
        private readonly Rect _ink;
        private readonly RoundedRect _shape;
        private readonly IImmutableBrush? _fill;
        private readonly ImmutablePen? _pen;

        public PillStamp(Rect ink, RoundedRect shape, IImmutableBrush? fill, ImmutablePen? pen)
        {
            Bounds = new Rect(ink.Size);
            (_ink, _shape, _fill, _pen) = (ink, shape, fill, pen);
        }

        public override void Render(DrawingContext context)
        {
            using (context.PushTransform(Matrix.CreateTranslation(-_ink.X, -_ink.Y)))
            {
                context.DrawRectangle(_fill, _pen, _shape);
            }
        }
    }

    private static IImmutableBrush? Freeze(IBrush? brush, List<IDisposable> resources)
    {
        if (brush is not ISceneBrush scene) return brush?.ToImmutable();
        var content = scene.CreateContent();
        if (content != null) resources.Add(content);
        return content;
    }

    // Match Avalonia 12.0's TextLineImpl.GetBaselineOffset, including inline
    // superscript/subscript and spans with font metrics different from the line.
    private static double GetRevealBaselineOffset(TextLine line, ShapedTextRun run) => run.Properties.BaselineAlignment switch
    {
        BaselineAlignment.Baseline => line.Baseline - run.Baseline,
        BaselineAlignment.Top or BaselineAlignment.TextTop => line.Height - line.Extent + run.Size.Height / 2 - run.Baseline,
        BaselineAlignment.Center => (line.Height - run.Size.Height) / 2,
        BaselineAlignment.Subscript or BaselineAlignment.Bottom or BaselineAlignment.TextBottom => line.Height - run.Size.Height,
        BaselineAlignment.Superscript => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(run))
    };

    private static RevealStroke[] GetRevealDecorations(ShapedTextRun run, IImmutableBrush foreground, List<IDisposable> resources)
    {
        if (run.Properties.TextDecorations is not { Count: > 0 } decorations) return [];

        var result = new List<RevealStroke>();
        var glyph = run.GlyphRun;
        var metrics = run.TextMetrics;
        foreach (var decoration in decorations)
        {
            var thickness = (decoration.StrokeThicknessUnit, decoration.Location) switch
            {
                (TextDecorationUnit.FontRenderingEmSize, _) => decoration.StrokeThickness * metrics.FontRenderingEmSize,
                (TextDecorationUnit.FontRecommended, TextDecorationLocation.Underline) => metrics.UnderlineThickness,
                (TextDecorationUnit.FontRecommended, TextDecorationLocation.Strikethrough) => metrics.StrikethroughThickness,
                _ => decoration.StrokeThickness
            };
            var dy = decoration.Location switch
            {
                TextDecorationLocation.Overline => metrics.Ascent,
                TextDecorationLocation.Underline => metrics.UnderlinePosition,
                TextDecorationLocation.Strikethrough => metrics.StrikethroughPosition,
                _ => 0
            };
            dy += decoration.StrokeOffsetUnit switch
            {
                TextDecorationUnit.FontRenderingEmSize => decoration.StrokeOffset * metrics.FontRenderingEmSize,
                TextDecorationUnit.Pixel => decoration.StrokeOffset,
                _ => 0
            };
            var y = glyph.BaselineOrigin.Y + dy;
            var x = glyph.BaselineOrigin.X;
            var pen = new ImmutablePen(
                decoration.Stroke is { } stroke ? Freeze(stroke, resources) : foreground,
                thickness,
                new ImmutableDashStyle(decoration.StrokeDashArray, decoration.StrokeDashOffset),
                decoration.StrokeLineCap);

            // Avalonia skips descenders for underlines/overlines on backends which
            // report glyph intersections. Keep those gaps when handing the line back.
            if (decoration.Location != TextDecorationLocation.Strikethrough)
            {
                var cuts = glyph.GetIntersections((float)(dy + thickness * 0.5), (float)(dy + thickness * 1.5));
                if (cuts.Count != 0)
                {
                    var last = x;
                    for (var i = 0; i < cuts.Count; i += 2)
                    {
                        var start = cuts[i] - thickness;
                        if (last + metrics.FontRenderingEmSize / 12 < start)
                        {
                            result.Add(new RevealStroke(new Point(last, y), new Point(start, y), pen));
                        }

                        last = cuts[i + 1] + thickness;
                    }

                    if (last < x + glyph.Bounds.Width)
                    {
                        result.Add(new RevealStroke(new Point(last, y), new Point(x + glyph.Bounds.Width, y), pen));
                    }

                    continue;
                }
            }
            result.Add(new RevealStroke(new Point(x, y), new Point(x + glyph.Metrics.Width, y), pen));
        }

        return [.. result];
    }
}
