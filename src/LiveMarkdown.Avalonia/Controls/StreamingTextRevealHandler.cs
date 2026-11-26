using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace LiveMarkdown.Avalonia;

// Timestamps live in the UI's monotonic time domain. Line mode indexes visual lines;
// character mode indexes UTF-16 positions in the formatted layout.
internal readonly record struct StreamingRevealSpan(int Start, int Length, long Timestamp);

internal sealed class StreamingTextRevealHandler(Action<StreamingRevealSnapshot> completed) : CompositionCustomVisualHandler
{
    internal static readonly object Clear = new();
    private StreamingRevealSnapshot? _snapshot;
    private TimeSpan _latestStart;

    public override void OnMessage(object message)
    {
        _snapshot?.Dispose();
        _snapshot = message as StreamingRevealSnapshot;
        if (_snapshot is { } snapshot)
        {
            // Include the time spent waiting for the compositor batch. Subsequent frames
            // need only the compositor clock, even if the UI thread is blocked.
            _latestStart = CompositionNow - Stopwatch.GetElapsedTime(snapshot.LatestTimestamp);
            RegisterForNextAnimationFrameUpdate();
        }

        Invalidate();
    }

    public override Rect GetRenderBounds() => _snapshot?.Bounds ?? default;

    public override void OnAnimationFrameUpdate()
    {
        if (_snapshot is not { } snapshot) return;

        Invalidate();
        if (CompositionNow - _latestStart < snapshot.TotalDuration)
        {
            RegisterForNextAnimationFrameUpdate();
        }
        else
        {
            Dispatcher.UIThread.Post(() => completed(snapshot));
        }
    }

    public override void OnRender(ImmediateDrawingContext context) =>
        _snapshot?.Draw(context, CompositionNow - _latestStart);
}

internal readonly record struct RevealBand(double Left, double Right, TimeSpan Age);

internal readonly record struct RevealBackground(
    Rect Bounds,
    double Radius,
    IImmutableBrush? Fill,
    ImmutablePen? Pen,
    ISceneBrushContent? Scene
);

internal readonly record struct RevealStroke(Point Start, Point End, ImmutablePen Pen);

internal sealed record RevealGlyph(
    Point Origin,
    Rect Bounds,
    Rect Ink,
    IImmutableBrush Foreground,
    IImmutableBrush? Background,
    IImmutableGlyphRunReference Reference,
    RevealStroke[] Decorations
);

internal sealed record RevealLine(
    Rect Bounds,
    RevealGlyph[] Glyphs,
    RevealBackground[] Backgrounds,
    RevealBand[] Bands,
    TimeSpan? LineAge,
    bool NeedsMask
);

internal sealed class StreamingRevealSnapshot(
    RevealLine[] lines,
    Rect bounds,
    TimeSpan fadeDuration,
    TimeSpan riseDuration,
    TimeSpan totalDuration,
    double riseOffset,
    long latestTimestamp,
    Point layoutOrigin,
    List<IDisposable> resources
) : IDisposable
{
    internal RevealLine[] Lines { get; } = lines;
    internal Rect Bounds { get; } = bounds;
    internal TimeSpan FadeDuration { get; } = fadeDuration;
    internal TimeSpan RiseDuration { get; } = riseDuration;
    internal TimeSpan TotalDuration { get; } = totalDuration;
    internal double RiseDistance { get; } = riseOffset;
    internal long LatestTimestamp { get; } = latestTimestamp;
    internal bool IsDisposed { get; private set; }

    public void Dispose()
    {
        foreach (var resource in resources) resource.Dispose();
        resources.Clear();
        IsDisposed = true;
    }

    internal void Draw(ImmediateDrawingContext context, TimeSpan elapsed)
    {
        foreach (var line in Lines)
        {
            var rise = RiseOffset(line.LineAge, elapsed);
            using var translate = rise > 0.001 ? context.PushPreTransform(Matrix.CreateTranslation(0, rise)) : default;
            if (line.Bands.Length != 0)
            {
                DrawCharacterLine(context, line, elapsed);
            }
            else if (line.LineAge is { } age && FadeDuration > TimeSpan.Zero && elapsed + age < FadeDuration)
            {
                DrawLineWithAlpha(context, line, Progress(elapsed + age, FadeDuration));
            }
            else
            {
                DrawLine(context, line, 1);
            }
        }
    }

    private double RiseOffset(TimeSpan? lineAge, TimeSpan elapsed)
    {
        if (RiseDistance <= 0 || RiseDuration <= TimeSpan.Zero || lineAge is not { } age) return 0;
        var total = elapsed + age;
        return total >= RiseDuration ? 0 : (1 - Progress(total, RiseDuration)) * RiseDistance;
    }

    private void DrawCharacterLine(ImmediateDrawingContext context, RevealLine line, TimeSpan elapsed)
    {
        // Keep settled prefixes on their original solid paint so subpixel antialiasing
        // and unaffected ink remain untouched; only newly streamed chunks fade in uniformly.
        var cursor = line.Bounds.X;
        double? activeStart = null;
        var activeEnd = cursor;
        var activeAlpha = 1d;

        foreach (var band in line.Bands)
        {
            var age = elapsed + band.Age;
            if (age >= FadeDuration) continue;
            var bandLeft = Math.Max(band.Left, cursor);
            var bandRight = band.Right;
            if (bandRight <= bandLeft) continue;

            var alpha = Progress(age, FadeDuration);
            if (activeStart is { } start)
            {
                if (bandLeft <= activeEnd + 0.001 && Math.Abs(alpha - activeAlpha) < 0.002)
                {
                    activeEnd = Math.Max(activeEnd, bandRight);
                    cursor = activeEnd;
                    continue;
                }

                DrawSlice(start, activeEnd, activeAlpha);
                if (bandLeft > activeEnd + 0.001)
                {
                    DrawSlice(activeEnd, bandLeft, 1);
                }
            }
            else if (bandLeft > cursor + 0.001)
            {
                DrawSlice(cursor, bandLeft, 1);
            }

            activeStart = bandLeft;
            activeEnd = bandRight;
            activeAlpha = alpha;
            cursor = bandRight;
        }

        if (activeStart is { } tailStart)
        {
            DrawSlice(tailStart, activeEnd, activeAlpha);
            DrawSlice(activeEnd, line.Bounds.Right, 1);
        }
        else
        {
            DrawSlice(cursor, line.Bounds.Right, 1);
        }

        void DrawSlice(double start, double end, double alpha)
        {
            if (end <= start) return;
            // Clip only at the split, not at an outer antialiased edge of the ink.
            var x1 = start <= line.Bounds.X ? start - 1 : start;
            var x2 = end >= line.Bounds.Right ? end + 1 : end;
            var rect = new Rect(x1, line.Bounds.Top - 1, x2 - x1, line.Bounds.Height + 2);
            using var clip = start <= line.Bounds.X && end >= line.Bounds.Right ? default : context.PushClip(rect);
            DrawLineWithAlpha(context, line, alpha);
        }
    }

    private void DrawLineWithAlpha(ImmediateDrawingContext context, RevealLine line, double alpha)
    {
        switch (alpha)
        {
            case >= 0.999:
                DrawLine(context, line, 1);
                return;
            case <= 0.001:
                return;
        }

        if (line.NeedsMask)
        {
            using (context.PushOpacity(alpha, line.Bounds))
            {
                DrawLine(context, line, 1);
            }
        }
        else
        {
            DrawLine(context, line, alpha);
        }
    }

    private void DrawLine(ImmediateDrawingContext context, RevealLine line, double alpha)
    {
        if (line.Backgrounds.Length > 0)
        {
            // MarkdownTextBlock paints backgrounds in layout coordinates, not in
            // glyph-local coordinates. Preserve absolute brush mappings as well.
            using var transform = context.PushPreTransform(Matrix.CreateTranslation(layoutOrigin));
            foreach (var background in line.Backgrounds)
            {
                if (background.Scene is { } scene)
                {
                    scene.Render(context.PlatformImpl, Matrix.CreateTranslation(background.Bounds.Position));
                }
                else
                {
                    context.DrawRectangle(background.Fill, background.Pen, background.Bounds, background.Radius, background.Radius);
                }
            }
        }

        foreach (var glyph in line.Glyphs)
        {
            using var transform = context.PushPreTransform(Matrix.CreateTranslation(glyph.Origin));
            if (glyph.Background is { } bg)
            {
                context.DrawRectangle(bg, null, glyph.Bounds);
            }

            var paint = alpha < 0.999 ? ScaleBrush((IImmutableSolidColorBrush)glyph.Foreground, alpha) : glyph.Foreground;
            context.DrawGlyphRun(paint, glyph.Reference);
            foreach (var stroke in glyph.Decorations)
            {
                var pen = stroke.Pen;
                if (alpha < 0.999)
                {
                    var strokeBrush = ReferenceEquals(pen.Brush, glyph.Foreground) ? paint : ScaleBrush((IImmutableSolidColorBrush)pen.Brush!, alpha);
                    pen = new ImmutablePen(strokeBrush, pen.Thickness, (ImmutableDashStyle?)pen.DashStyle, pen.LineCap, pen.LineJoin, pen.MiterLimit);
                }
                context.DrawLine(pen, stroke.Start, stroke.End);
            }
        }
    }

    private static IImmutableSolidColorBrush ScaleBrush(IImmutableSolidColorBrush source, double alpha) =>
        new ImmutableSolidColorBrush(source.Color, source.Opacity * alpha);

    private static double Progress(TimeSpan age, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return 1;
        var remaining = 1 - Math.Clamp(age.TotalSeconds / duration.TotalSeconds, 0, 1);
        return 1 - remaining * remaining * remaining;
    }
}