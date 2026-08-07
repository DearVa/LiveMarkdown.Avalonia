using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Metadata;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Represents a Markdown text block that can be rendered and interacted with.
/// This class extends <see cref="SelectableTextBlock"/> to fix its selection bugs.
/// </summary>
[PseudoClasses(":pointerover-link")]
public class MarkdownTextBlock : SelectableTextBlock
{
    /// <summary>
    /// Defines whether the target visual is a shared text selection scope.
    /// </summary>
    public static readonly AttachedProperty<bool> IsSelectionScopeProperty =
        AvaloniaProperty.RegisterAttached<MarkdownTextBlock, Visual, bool>("IsSelectionScope");

    /// <summary>
    /// Defines the inherited named highlight style table.
    /// </summary>
    public static readonly AttachedProperty<TextHighlightStyles?> HighlightStylesProperty =
        AvaloniaProperty.RegisterAttached<MarkdownTextBlock, StyledElement, TextHighlightStyles?>(
            nameof(HighlightStyles),
            inherits: true);

    /// <summary>
    /// Sets whether the target visual is a shared text selection scope.
    /// </summary>
    public static void SetIsSelectionScope(Visual obj, bool value) => obj.SetValue(IsSelectionScopeProperty, value);

    /// <summary>
    /// Gets whether the target visual is a shared text selection scope.
    /// </summary>
    public static bool GetIsSelectionScope(Visual obj) => obj.GetValue(IsSelectionScopeProperty);

    /// <summary>
    /// Sets the inherited named highlight styles on a styled element.
    /// </summary>
    public static void SetHighlightStyles(StyledElement obj, TextHighlightStyles? value) =>
        obj.SetValue(HighlightStylesProperty, value);

    /// <summary>
    /// Gets the inherited named highlight styles from a styled element.
    /// </summary>
    public static TextHighlightStyles? GetHighlightStyles(StyledElement obj) =>
        obj.GetValue(HighlightStylesProperty);

    /// <summary>
    /// Gets or sets the named highlight styles inherited by this text block and its descendants.
    /// </summary>
    public TextHighlightStyles? HighlightStyles
    {
        get => GetValue(HighlightStylesProperty);
        set => SetValue(HighlightStylesProperty, value);
    }

    /// <summary>
    /// Gets the text ranges registered for this text block.
    /// </summary>
    public TextHighlightRegistry Highlights { get; } = new();

    /// <summary>
    /// Defines the <see cref="LinkContextMenu"/> property.
    /// </summary>
    public static readonly StyledProperty<ContextMenu?> LinkContextMenuProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, ContextMenu?>(nameof(LinkContextMenu));

    /// <summary>
    /// Context menu to show when right-clicking a Link.
    /// </summary>
    public ContextMenu? LinkContextMenu
    {
        get => GetValue(LinkContextMenuProperty);
        set => SetValue(LinkContextMenuProperty, value);
    }

    /// <summary>
    /// Routed event that is raised when a Link is clicked.
    /// </summary>
    public static readonly RoutedEvent<LinkClickedEventArgs> LinkClickEvent =
        RoutedEvent.Register<Link, LinkClickedEventArgs>(
            nameof(LinkClick),
            RoutingStrategies.Bubble);

    /// <summary>
    /// Raised when a Link is clicked.
    /// </summary>
    public event EventHandler<LinkClickedEventArgs>? LinkClick
    {
        add => AddHandler(LinkClickEvent, value);
        remove => RemoveHandler(LinkClickEvent, value);
    }

    public SourceSpan SourceSpan { get; internal set; }

    // Link markers are scoped to this text block. The dictionary is maintained by
    // Link's logical-tree lifetime callbacks, so rebuilding a TextLayout does not
    // require walking the inline tree again.
    private readonly Dictionary<string, Link> linksByTag = [];

    private TextHighlightStyles? _subscribedHighlightStyles;
    private TextPaintSnapshot _paintSnapshot = TextPaintSnapshot.Empty;
    private IReadOnlyList<TextRun>? _paintSnapshotTextRuns;
    private bool _paintSnapshotDirty = true;
    private bool _registeredHighlightPaintDirty = true;
    private HighlightPaintSnapshot _registeredHighlightPaintSnapshot = HighlightPaintSnapshot.Empty;
    private TextLayout? _lineGeometryLayout;
    private TextLineGeometry[] _lineGeometry = [];
    private string? _searchText;

    public MarkdownTextBlock()
    {
        Highlights.Changed += HandleHighlightsChanged;
    }

    public string ActualText
    {
        get
        {
            if (Inlines is not { Count: > 0 } inlines) return Text ?? string.Empty;
            return inlines.ActualText;
        }
    }

    /// <summary>
    /// Gets the text represented by this block's own layout. Embedded controls are represented by
    /// the single object-replacement character used by Avalonia's text formatter; their child text
    /// blocks are searched independently.
    /// </summary>
    internal string SearchText => _searchText ??= Inlines is { Count: > 0 } inlines
        ? inlines.Text ?? string.Empty
        : Text ?? string.Empty;

    public string ActualSelectedText
    {
        get
        {
            var selectionStart = SelectionStart;
            var selectionEnd = SelectionEnd;
            (selectionStart, selectionEnd) = (Math.Min(selectionStart, selectionEnd), Math.Max(selectionStart, selectionEnd));

            var stringBuilder = new StringBuilder();
            var currentIndex = 0;
            if (Inlines is not { Count: > 0 } inlines)
            {
                AppendText(Text);
            }
            else
            {
                foreach (var inline in inlines) AppendInline(inline);
            }

            return stringBuilder.ToString();

            void AppendInline(Inline inline)
            {
                switch (inline)
                {
                    case Run run:
                    {
                        AppendText(run.Text);
                        break;
                    }
                    case Span span:
                    {
                        foreach (var childInline in span.Inlines) AppendInline(childInline);
                        return;
                    }
                    case LineBreak:
                    {
                        AppendText(Environment.NewLine);
                        break;
                    }
                    case InlineUIContainer { Child: { } logicalChild }:
                    {
                        AppendLogicalText(logicalChild);
                        return;
                    }
                    default:
                    {
                        return;
                    }
                }
            }

            void AppendText(string? text)
            {
                if (currentIndex >= selectionEnd)
                {
                    // Already passed the selection range
                    return;
                }

                text ??= string.Empty;

                if (currentIndex + text.Length <= selectionStart)
                {
                    // This run is before the selection range
                    currentIndex += text.Length;
                    return;
                }

                var start = Math.Max(selectionStart - currentIndex, 0);
                var end = Math.Min(selectionEnd - currentIndex, text.Length);
                stringBuilder.Append(text[start..end]);
                currentIndex += text.Length;
            }

            void AppendLogicalText(ILogical logical)
            {
                if (logical is MarkdownTextBlock textBlock)
                {
                    var actualText = textBlock.ActualText;
                    var actualSelectedText = textBlock.ActualSelectedText;

                    if (actualText.Equals(actualSelectedText, StringComparison.Ordinal))
                    {
                        selectionEnd += actualText.Length - 1;
                    }
                    else if (actualText.StartsWith(actualSelectedText, StringComparison.Ordinal))
                    {
                        selectionEnd += actualText.Length - actualSelectedText.Length - 1;
                    }
                    else
                    {
                        selectionEnd += actualText.Length - 1;
                    }

                    if (actualText.EndsWith(actualSelectedText, StringComparison.Ordinal))
                    {
                        selectionStart += actualText.Length - actualSelectedText.Length - 1;
                    }

                    stringBuilder.Append(actualSelectedText);
                    currentIndex += actualText.Length;
                    return; // no need to traverse its children, because ActualSelectedText will handle that
                }

                foreach (var child in logical.LogicalChildren) AppendLogicalText(child);
            }
        }
    }

    /// <summary>
    /// Gets the length of the text content, counting inline elements escaped as single characters.
    /// </summary>
    public int EscapedTextLength
    {
        get
        {
            if (Text is { } text) return text.Length;
            if (Inlines is not { Count: > 0 } inlines) return 0;

            var length = 0;
            foreach (var inline in inlines) CalculateInlineLength(inline);
            return length;

            void CalculateInlineLength(Inline inline)
            {
                switch (inline)
                {
                    case Run run:
                    {
                        length += run.Text?.Length ?? 0;
                        break;
                    }
                    case Span span:
                    {
                        foreach (var childInline in span.Inlines) CalculateInlineLength(childInline);
                        break;
                    }
                    case LineBreak:
                    case InlineUIContainer:
                    {
                        length += inline is LineBreak ? Environment.NewLine.Length : 1;
                        break;
                    }
                }
            }
        }
    }

    static MarkdownTextBlock()
    {
        CopyingToClipboardEvent.AddClassHandler<MarkdownTextBlock>(
            async void (o, e) =>
            {
                try
                {
                    e.Handled = true;

                    if (TopLevel.GetTopLevel(o) is not { Clipboard: { } clipboard }) return;
                    var selectedText = o.ActualSelectedText;
                    if (!string.IsNullOrEmpty(selectedText)) await clipboard.SetTextAsync(selectedText);
                }
                catch
                {
                    // Ignore clipboard exceptions
                }
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);

        if (ContextFlyout is not { IsOpen: true } && ContextMenu is not { IsOpen: true })
        {
            ClearSelection();
        }
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        SubscribeToHighlightStyles(null);
        _searchText = null;
        _paintSnapshot = TextPaintSnapshot.Empty;
        _paintSnapshotTextRuns = null;
        _paintSnapshotDirty = true;
        _registeredHighlightPaintDirty = true;
        _registeredHighlightPaintSnapshot = HighlightPaintSnapshot.Empty;
        _lineGeometryLayout = null;
        _lineGeometry = [];
        linksByTag.Clear();
        pointerLink = null;
        pressingLink = null;
        UpdatePseudoClass();

        base.OnDetachedFromLogicalTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InvalidateRendererTextBlockCache(e.AttachmentPoint);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        InvalidateRendererTextBlockCache(e.AttachmentPoint);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        SubscribeToHighlightStyles(HighlightStyles);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FontFamilyProperty ||
            change.Property == FontSizeProperty ||
            change.Property == FontStyleProperty ||
            change.Property == FontWeightProperty ||
            change.Property == FontStretchProperty ||
            change.Property == ForegroundProperty ||
            change.Property == TextDecorationsProperty ||
            change.Property == FontFeaturesProperty ||
            change.Property == LetterSpacingProperty ||
            change.Property == FlowDirectionProperty)
        {
            _paintSnapshotDirty = true;
            _lineGeometryLayout = null;
        }

        if (change.Property != HighlightStylesProperty)
        {
            return;
        }

        SubscribeToHighlightStyles(change.GetNewValue<TextHighlightStyles?>());
        _registeredHighlightPaintDirty = true;
        InvalidateVisual();
    }

    private void SubscribeToHighlightStyles(TextHighlightStyles? styles)
    {
        if (ReferenceEquals(_subscribedHighlightStyles, styles))
        {
            return;
        }

        if (_subscribedHighlightStyles is not null)
        {
            _subscribedHighlightStyles.Changed -= HandleHighlightStylesChanged;
        }

        _subscribedHighlightStyles = styles;

        if (styles is not null)
        {
            styles.Changed += HandleHighlightStylesChanged;
        }
    }

    private void HandleHighlightsChanged(object? sender, EventArgs e)
    {
        _registeredHighlightPaintDirty = true;
        InvalidateVisual();
    }

    private void HandleHighlightStylesChanged(object? sender, EventArgs e)
    {
        _registeredHighlightPaintDirty = true;
        InvalidateVisual();
    }

    internal void RegisterLink(Link link)
    {
        if (linksByTag.TryGetValue(link.Tag, out var existing) && !ReferenceEquals(existing, link))
        {
            throw new InvalidOperationException($"Duplicate link marker '{link.Tag}'.");
        }

        linksByTag[link.Tag] = link;
    }

    internal void UnregisterLink(Link link)
    {
        if (linksByTag.TryGetValue(link.Tag, out var existing) && ReferenceEquals(existing, link))
        {
            linksByTag.Remove(link.Tag);
        }

        if (ReferenceEquals(pointerLink, link))
        {
            pointerLink = null;
            UpdatePseudoClass();
        }
    }

    // Selection remains an interaction state owned by SelectableTextBlock, but it must not be
    // represented by TextRunProperties overrides. Those overrides are complete replacement
    // values and can change shaping and line breaking. Selection and inline decorations are
    // therefore painted in RenderTextLayout over a stable, selection-independent layout.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_LineSpacing")]
    private extern static void SetLineSpacing(TextParagraphProperties properties, double value);

    protected override TextLayout CreateTextLayout(string? text)
    {
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);

        var defaultProperties = new GenericTextRunProperties(
            typeface,
            FontSize,
            TextDecorations,
            Foreground,
            fontFeatures: FontFeatures);

        var paragraphProperties = new GenericTextParagraphProperties(
            FlowDirection,
            TextAlignment,
            true,
            false,
            defaultProperties,
            TextWrapping,
            LineHeight,
            0,
            LetterSpacing);
        SetLineSpacing(paragraphProperties, LineSpacing);

        ITextSource textSource;
        if (_textRuns is { } textRuns)
        {
            var snapshot = GetPaintSnapshot(textRuns);
            textSource = new MarkdownInlinesTextSource(textRuns, snapshot);
        }
        else
        {
            GetPaintSnapshot(null);
            textSource = new MarkdownSimpleTextSource(text ?? string.Empty, defaultProperties);
        }

        var maxSize = GetMaxSizeFromConstraint();
        return new TextLayout(textSource, paragraphProperties, TextTrimming, maxSize.Width, maxSize.Height, MaxLines);
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        var snapshot = GetPaintSnapshot(_textRuns);
        var selectionStart = Math.Min(SelectionStart, SelectionEnd);
        var selectionLength = Math.Max(SelectionStart, SelectionEnd) - selectionStart;
        var hasSelection = SelectionBrush is not null && selectionLength > 0;
        var hasSelectionForeground = SelectionForegroundBrush is not null && selectionLength > 0;
        var highlightPaint = GetRegisteredHighlightPaintSnapshot();
        var hasHighlights = highlightPaint.HasPaint;
        var registeredForegroundSpans = highlightPaint.ForegroundSpans;
        var hasForegroundOverrides = hasSelectionForeground || registeredForegroundSpans.Length > 0;

        if (snapshot.BackgroundSpans.Length == 0 && !hasHighlights && !hasSelection && !hasForegroundOverrides)
        {
            TextLayout.Draw(context, origin);
            return;
        }

        var lines = GetLineGeometry();
        using (context.PushTransform(Matrix.CreateTranslation(origin)))
        {
            DrawPaintSpans(context, snapshot.BackgroundSpans, lines);
            DrawPaintSpans(context, highlightPaint.BackgroundSpans, lines);

            if (hasSelection)
            {
                DrawPaintSpan(
                    context,
                    new TextPaintSpan(
                        selectionStart,
                        selectionLength,
                        SelectionBrush!,
                        default,
                        default),
                    lines);
            }
        }

        if (hasForegroundOverrides)
        {
            DrawTextLayoutWithForegroundOverrides(
                context,
                origin,
                lines,
                registeredForegroundSpans,
                hasSelectionForeground
                    ? new TextPaintForegroundSpan(
                        selectionStart,
                        selectionLength,
                        SelectionForegroundBrush!,
                        int.MaxValue,
                        long.MaxValue)
                    : null);
        }
        else
        {
            // Do not call SelectableTextBlock.RenderTextLayout here: it draws its own selection
            // layer and assumes that the layout contains selection foreground overrides.
            TextLayout.Draw(context, origin);
        }
    }

    private static void DrawTextLayoutWithForegroundOverrides(
        DrawingContext context,
        Point origin,
        TextLineGeometry[] lines,
        IReadOnlyList<TextPaintForegroundSpan> foregroundSpans,
        TextPaintForegroundSpan? selectionForegroundSpan)
    {
        var layoutHeight = lines.Length == 0 ? 1 : lines[^1].Y + lines[^1].TextLine.Height;
        var clipTop = -Math.Max(1, layoutHeight + 32);
        var clipHeight = Math.Max(1, layoutHeight * 3 + 64);

        using (context.PushTransform(Matrix.CreateTranslation(origin)))
        {
            foreach (var line in lines)
            {
                DrawTextLineWithForegroundOverrides(
                    context,
                    line,
                    foregroundSpans,
                    selectionForegroundSpan,
                    clipTop,
                    clipHeight);
            }
        }
    }

    private static void DrawTextLineWithForegroundOverrides(
        DrawingContext context,
        in TextLineGeometry line,
        IReadOnlyList<TextPaintForegroundSpan> foregroundSpans,
        TextPaintForegroundSpan? selectionForegroundSpan,
        double clipTop,
        double clipHeight)
    {
        var foregroundIntervals = GetForegroundIntervals(line, foregroundSpans, selectionForegroundSpan);
        var currentX = line.TextLine.Start;

        foreach (var textRun in line.TextLine.TextRuns)
        {
            switch (textRun)
            {
                case ShapedTextRun shapedRun:
                {
                    var baselineOffset = GetBaselineOffset(line.TextLine, shapedRun);
                    var runOrigin = new Point(currentX, line.Y + baselineOffset);
                    DrawShapedRunWithForegroundOverrides(
                        context,
                        shapedRun,
                        runOrigin,
                        foregroundIntervals,
                        clipTop,
                        clipHeight);
                    currentX += shapedRun.Size.Width;
                    break;
                }
                case DrawableTextRun drawableRun:
                {
                    var baselineOffset = GetBaselineOffset(line.TextLine, drawableRun);
                    drawableRun.Draw(context, new Point(currentX, line.Y + baselineOffset));
                    currentX += drawableRun.Size.Width;
                    break;
                }
            }
        }
    }

    private static void DrawShapedRunWithForegroundOverrides(
        DrawingContext context,
        ShapedTextRun shapedRun,
        Point origin,
        IReadOnlyList<TextPaintInterval> foregroundIntervals,
        double clipTop,
        double clipHeight)
    {
        if (shapedRun.GlyphRun.GlyphInfos.Count == 0 || shapedRun.Properties.Typeface == default)
        {
            return;
        }

        var runStart = origin.X;
        var runEnd = runStart + shapedRun.Size.Width;
        var currentX = runStart;
        var foreground = shapedRun.Properties.ForegroundBrush;
        var hasForegroundOverlap = false;

        foreach (var interval in foregroundIntervals)
        {
            if (interval.Right <= runStart)
            {
                continue;
            }

            if (interval.Left >= runEnd)
            {
                break;
            }

            hasForegroundOverlap = true;
            break;
        }

        if (!hasForegroundOverlap)
        {
            DrawShapedRun(context, shapedRun, origin, foreground);
            return;
        }

        var visualStart = origin.X + shapedRun.GlyphRun.Bounds.Left - 1;
        var visualEnd = origin.X + shapedRun.GlyphRun.Bounds.Right + 1;

        foreach (var interval in foregroundIntervals)
        {
            if (interval.Right <= runStart)
            {
                continue;
            }

            if (interval.Left >= runEnd)
            {
                break;
            }

            var selectedStart = Math.Max(interval.Left, runStart);
            var selectedEnd = Math.Min(interval.Right, runEnd);

            if (selectedStart > currentX)
            {
                var unselectedStart = Math.Abs(currentX - runStart) < 0.001d ? visualStart : currentX;
                DrawShapedRunSlice(
                    context,
                    shapedRun,
                    origin,
                    foreground,
                    unselectedStart,
                    selectedStart,
                    clipTop,
                    clipHeight);
            }

            if (selectedEnd > selectedStart)
            {
                DrawShapedRunSlice(
                    context,
                    shapedRun,
                    origin,
                    interval.Brush,
                    selectedStart,
                    selectedEnd,
                    clipTop,
                    clipHeight);
            }

            currentX = Math.Max(currentX, selectedEnd);
        }

        if (currentX < runEnd)
        {
            DrawShapedRunSlice(
                context,
                shapedRun,
                origin,
                foreground,
                currentX,
                visualEnd,
                clipTop,
                clipHeight);
        }
    }

    private static void DrawShapedRun(DrawingContext context, ShapedTextRun shapedRun, Point origin, IBrush? foreground)
    {
        if (foreground is null)
        {
            return;
        }

        using (context.PushTransform(Matrix.CreateTranslation(origin)))
        {
            context.DrawGlyphRun(foreground, shapedRun.GlyphRun);

            if (shapedRun.Properties.TextDecorations is not { } decorations)
            {
                return;
            }

            foreach (var decoration in decorations)
            {
                TextDecoration_Draw(decoration, context, shapedRun.GlyphRun, shapedRun.TextMetrics, foreground);
            }
        }
    }

    private static void DrawShapedRunSlice(
        DrawingContext context,
        ShapedTextRun shapedRun,
        Point origin,
        IBrush? foreground,
        double clipLeft,
        double clipRight,
        double clipTop,
        double clipHeight)
    {
        if (foreground is null || clipRight <= clipLeft)
        {
            return;
        }

        // The clip is intentionally horizontal-only. Glyph overhang and text decorations can
        // extend beyond the TextLine's nominal vertical bounds, and the control's own render
        // clip already limits drawing to the visible control.
        using (context.PushClip(new Rect(clipLeft, clipTop, clipRight - clipLeft, clipHeight)))
        using (context.PushTransform(Matrix.CreateTranslation(origin)))
        {
            context.DrawGlyphRun(foreground, shapedRun.GlyphRun);

            if (shapedRun.Properties.TextDecorations is not { } decorations)
            {
                return;
            }

            foreach (var decoration in decorations)
            {
                TextDecoration_Draw(decoration, context, shapedRun.GlyphRun, shapedRun.TextMetrics, foreground);
            }
        }
    }

    private static List<TextPaintInterval> GetForegroundIntervals(
        in TextLineGeometry line,
        IReadOnlyList<TextPaintForegroundSpan> foregroundSpans,
        TextPaintForegroundSpan? selectionForegroundSpan)
    {
        if (foregroundSpans.Count == 0 && selectionForegroundSpan is null)
        {
            return [];
        }

        if (foregroundSpans.Count == 0 && selectionForegroundSpan is { } selectionOnly)
        {
            List<TextPaintInterval>? selectionIntervals = null;
            AppendForegroundIntervals(line, selectionOnly, ref selectionIntervals);
            return selectionIntervals ?? [];
        }

        List<TextPaintInterval>? rawIntervals = null;

        foreach (var span in foregroundSpans)
        {
            AppendForegroundIntervals(line, span, ref rawIntervals);
        }

        if (selectionForegroundSpan is { } selection)
        {
            AppendForegroundIntervals(line, selection, ref rawIntervals);
        }

        if (rawIntervals is null)
        {
            return [];
        }

        if (rawIntervals.Count == 1)
        {
            return rawIntervals;
        }

        rawIntervals.Sort(static (left, right) =>
        {
            var result = left.Left.CompareTo(right.Left);
            return result != 0 ? result : left.Right.CompareTo(right.Right);
        });

        var hasOverlap = false;
        for (var index = 1; index < rawIntervals.Count; index++)
        {
            if (rawIntervals[index].Left < rawIntervals[index - 1].Right - 0.001)
            {
                hasOverlap = true;
                break;
            }
        }

        if (!hasOverlap)
        {
            return rawIntervals;
        }

        var boundaries = new List<double>(rawIntervals.Count * 2);
        foreach (var interval in rawIntervals)
        {
            boundaries.Add(interval.Left);
            boundaries.Add(interval.Right);
        }

        boundaries.Sort();
        var uniqueBoundaries = new List<double>(boundaries.Count);
        foreach (var boundary in boundaries)
        {
            if (uniqueBoundaries.Count == 0 || Math.Abs(boundary - uniqueBoundaries[^1]) > 0.001)
            {
                uniqueBoundaries.Add(boundary);
            }
        }

        List<TextPaintInterval>? resolvedIntervals = null;
        for (var index = 0; index + 1 < uniqueBoundaries.Count; index++)
        {
            var left = uniqueBoundaries[index];
            var right = uniqueBoundaries[index + 1];
            if (right - left <= 0.001)
            {
                continue;
            }

            var midpoint = (left + right) / 2;
            TextPaintInterval? winningInterval = null;
            foreach (var interval in rawIntervals)
            {
                if (midpoint < interval.Left || midpoint >= interval.Right ||
                    winningInterval is { } current && !IsHigherPriority(interval, current))
                {
                    continue;
                }

                winningInterval = interval;
            }

            if (winningInterval is not { } winner)
            {
                continue;
            }

            if (resolvedIntervals is { Count: > 0 } &&
                ReferenceEquals(resolvedIntervals[^1].Brush, winner.Brush) &&
                resolvedIntervals[^1].Priority == winner.Priority &&
                resolvedIntervals[^1].Order == winner.Order &&
                Math.Abs(resolvedIntervals[^1].Right - left) <= 0.001)
            {
                var last = resolvedIntervals[^1];
                resolvedIntervals[^1] = last with { Right = right };
            }
            else
            {
                (resolvedIntervals ??= []).Add(winner with
                {
                    Left = left,
                    Right = right
                });
            }
        }

        return resolvedIntervals ?? [];
    }

    private static void AppendForegroundIntervals(in TextLineGeometry line, in TextPaintForegroundSpan span, ref List<TextPaintInterval>? intervals)
    {
        var segmentStart = Math.Max(span.Start, line.Start);
        var segmentEnd = Math.Min(span.End, line.End);
        if (segmentEnd <= segmentStart)
        {
            return;
        }

        foreach (var bounds in line.TextLine.GetTextBounds(segmentStart, segmentEnd - segmentStart))
        {
            if (bounds.Rectangle.Width <= 0)
            {
                continue;
            }

            (intervals ??= []).Add(
                new TextPaintInterval(
                    bounds.Rectangle.Left,
                    bounds.Rectangle.Right,
                    span.Brush,
                    span.Priority,
                    span.Order));
        }
    }

    private static bool IsHigherPriority(in TextPaintInterval candidate, in TextPaintInterval current) =>
        candidate.Priority > current.Priority ||
        candidate.Priority == current.Priority && candidate.Order > current.Order;

    private static double GetBaselineOffset(TextLine textLine, DrawableTextRun textRun)
    {
        var baseline = textRun.Baseline;
        var baselineAlignment = textRun.Properties?.BaselineAlignment;
        var baselineOffset = -baseline;

        switch (baselineAlignment)
        {
            case BaselineAlignment.Baseline:
                baselineOffset += textLine.Baseline;
                break;
            case BaselineAlignment.Top:
            case BaselineAlignment.TextTop:
                baselineOffset += textLine.Height - textLine.Extent + textRun.Size.Height / 2;
                break;
            case BaselineAlignment.Center:
                baselineOffset += textLine.Height / 2 + baseline - textRun.Size.Height / 2;
                break;
            case BaselineAlignment.Subscript:
            case BaselineAlignment.Bottom:
            case BaselineAlignment.TextBottom:
                baselineOffset += textLine.Height - textRun.Size.Height + baseline;
                break;
            case BaselineAlignment.Superscript:
                baselineOffset += baseline;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(baselineAlignment), baselineAlignment, null);
        }

        return baselineOffset;
    }

    // internal void Draw
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Draw")]
    private extern static void TextDecoration_Draw(
        TextDecoration decoration,
        DrawingContext drawingContext,
        GlyphRun glyphRun,
        TextMetrics textMetrics,
        IBrush defaultBrush);

    private HighlightPaintSnapshot GetRegisteredHighlightPaintSnapshot()
    {
        if (!_registeredHighlightPaintDirty)
        {
            return _registeredHighlightPaintSnapshot;
        }

        _registeredHighlightPaintDirty = false;
        if (HighlightStyles is not { } styles || Highlights.Count == 0)
        {
            return _registeredHighlightPaintSnapshot = HighlightPaintSnapshot.Empty;
        }

        List<TextPaintSpan>? backgroundSpans = null;
        List<TextPaintForegroundSpan>? foregroundSpans = null;
        foreach (var highlight in Highlights.GetOrderedHighlights())
        {
            if (!styles.TryGetValue(highlight.Name, out var style))
            {
                continue;
            }

            foreach (var range in highlight.Ranges)
            {
                if (style.Background is { } background)
                {
                    (backgroundSpans ??= []).Add(
                        new TextPaintSpan(
                            range.Start,
                            range.Length,
                            background,
                            style.Padding,
                            style.CornerRadius));
                }

                if (style.Foreground is { } foreground)
                {
                    (foregroundSpans ??= []).Add(
                        new TextPaintForegroundSpan(
                            range.Start,
                            range.Length,
                            foreground,
                            highlight.Priority,
                            highlight.Order));
                }
            }
        }

        if (foregroundSpans is not null)
        {
            foregroundSpans.Sort(static (left, right) =>
            {
                var result = left.Start.CompareTo(right.Start);
                return result != 0 ? result : left.End.CompareTo(right.End);
            });
        }

        return _registeredHighlightPaintSnapshot = new HighlightPaintSnapshot(
            backgroundSpans is null ? [] : [.. backgroundSpans],
            foregroundSpans is null ? [] : [.. foregroundSpans]);
    }

    /// <summary>
    /// Gets the visual rectangles occupied by a text range in this control's text layout.
    /// Coordinates are relative to the text layout origin and are suitable for custom
    /// highlights, search results, and scrolling to a match.
    /// </summary>
    public IReadOnlyList<Rect> GetTextRangeBounds(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (start > int.MaxValue - length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length == 0)
        {
            return [];
        }

        var result = new List<Rect>();
        AppendTextRangeBounds(result, GetLineGeometry(), start, length);
        return result;
    }

    /// <summary>
    /// Gets the visual rectangles occupied by a text range in this control's coordinate space.
    /// These rectangles can be transformed to an ancestor viewport for precise scrolling.
    /// </summary>
    public IReadOnlyList<Rect> GetTextRangeBoundsInControl(int start, int length)
    {
        var bounds = GetTextRangeBounds(start, length);
        if (bounds.Count == 0)
        {
            return bounds;
        }

        var origin = GetTextLayoutOrigin();
        var offset = new Vector(origin.X, origin.Y);
        return bounds.Select(rectangle => rectangle.Translate(offset)).ToArray();
    }

    private Point GetTextLayoutOrigin()
    {
        var padding = Padding;
        if (UseLayoutRounding)
        {
            var scale = LayoutHelper.GetLayoutScale(this);
            padding = LayoutHelper.RoundLayoutThickness(padding, scale);
        }

        var top = padding.Top;
        var textHeight = TextLayout.Height;
        if (Bounds.Height < textHeight)
        {
            top += VerticalAlignment switch
            {
                VerticalAlignment.Center => (Bounds.Height - textHeight) / 2,
                VerticalAlignment.Bottom => Bounds.Height - textHeight,
                _ => 0,
            };
        }

        return new Point(padding.Left, top);
    }

    private static void DrawPaintSpans(DrawingContext context, IReadOnlyList<TextPaintSpan> spans, TextLineGeometry[] lines)
    {
        foreach (var span in spans)
        {
            DrawPaintSpan(context, span, lines);
        }
    }

    private static void DrawPaintSpan(DrawingContext context, in TextPaintSpan span, TextLineGeometry[] lines)
    {
        if (span.Length <= 0)
        {
            return;
        }

        var rangeEnd = span.Start + span.Length;
        var firstLine = FindFirstLine(lines, span.Start);

        for (var lineIndex = firstLine; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (line.Start >= rangeEnd)
            {
                break;
            }

            var segmentStart = Math.Max(span.Start, line.Start);
            var segmentEnd = Math.Min(rangeEnd, line.End);
            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            Rect? last = null;
            var hasPaintedRectangle = false;
            foreach (var bounds in line.TextLine.GetTextBounds(segmentStart, segmentEnd - segmentStart))
            {
                var rectangle = bounds.Rectangle.WithY(line.Y);
                if (last.HasValue && CanMergeBounds(last.Value, rectangle))
                {
                    last = last.Value.WithWidth(last.Value.Width + rectangle.Width);
                }
                else
                {
                    DrawPaintRectangle(
                        context,
                        last,
                        span,
                        !hasPaintedRectangle && segmentStart == span.Start ? span.BackgroundInset.Left : 0,
                        0);
                    hasPaintedRectangle |= last.HasValue;
                    last = rectangle;
                }
            }

            DrawPaintRectangle(
                context,
                last,
                span,
                !hasPaintedRectangle && segmentStart == span.Start ? span.BackgroundInset.Left : 0,
                segmentEnd == rangeEnd ? span.BackgroundInset.Right : 0);
        }
    }

    private static bool CanMergeBounds(Rect first, Rect second)
    {
        const double epsilon = 0.5;
        return Math.Abs(first.Right - second.Left) < epsilon &&
            Math.Abs(first.Top - second.Top) < epsilon &&
            Math.Abs(first.Height - second.Height) < epsilon;
    }

    private static void DrawPaintRectangle(
        DrawingContext context,
        Rect? rectangle,
        in TextPaintSpan span,
        double leadingMargin,
        double trailingMargin)
    {
        if (rectangle is not { } value)
        {
            return;
        }

        var contentRect = new Rect(
            value.X + leadingMargin,
            value.Y,
            Math.Max(0, value.Width - leadingMargin - trailingMargin),
            value.Height);

        var paddedRect = new Rect(
            contentRect.X - span.Padding.Left,
            contentRect.Y - span.Padding.Top,
            contentRect.Width + span.Padding.Left + span.Padding.Right,
            contentRect.Height + span.Padding.Top + span.Padding.Bottom);

        context.DrawRectangle(span.Brush, null, new RoundedRect(paddedRect, span.CornerRadius));
    }

    private TextPaintSnapshot GetPaintSnapshot(IReadOnlyList<TextRun>? textRuns)
    {
        if (!_paintSnapshotDirty && ReferenceEquals(_paintSnapshotTextRuns, textRuns))
        {
            return _paintSnapshot;
        }

        _paintSnapshot = textRuns is null
            ? TextPaintSnapshot.Empty
            : TextPaintSnapshot.Create(textRuns, GetCodeInlineSpans(), FlowDirection, LetterSpacing);
        _paintSnapshotTextRuns = textRuns;
        _paintSnapshotDirty = false;
        return _paintSnapshot;
    }

    private CodeInlineSpan[] GetCodeInlineSpans()
    {
        if (Inlines is not { Count: > 0 } inlines)
        {
            return [];
        }

        List<CodeInlineSpan>? spans = null;
        var currentIndex = 0;

        foreach (var inline in inlines)
        {
            AppendInline(inline);
        }

        return spans is null ? [] : spans.ToArray();

        void AppendInline(Inline inline)
        {
            switch (inline)
            {
                case CodeInline codeInline:
                {
                    var text = codeInline.Text ?? string.Empty;
                    var start = currentIndex;
                    currentIndex += text.Length;
                    spans ??= [];
                    spans.Add(
                        new CodeInlineSpan(
                            start,
                            text.Length,
                            codeInline.Background,
                            codeInline.CornerRadius,
                            codeInline.Padding,
                            codeInline.Margin));
                    break;
                }
                case Run run:
                    currentIndex += run.Text?.Length ?? 0;
                    break;
                case Span span:
                    foreach (var childInline in span.Inlines)
                    {
                        AppendInline(childInline);
                    }

                    break;
                case LineBreak:
                    currentIndex += Environment.NewLine.Length;
                    break;
                case InlineUIContainer:
                    // InlineUIContainer is represented by an object replacement character in
                    // the parent TextLayout. Its child remains a separate text layout.
                    currentIndex += TextRun.DefaultTextSourceLength;
                    break;
            }
        }
    }

    private TextLineGeometry[] GetLineGeometry()
    {
        var layout = TextLayout;
        if (ReferenceEquals(_lineGeometryLayout, layout))
        {
            return _lineGeometry;
        }

        var textLines = layout.TextLines;
        var result = new TextLineGeometry[textLines.Count];
        var currentY = 0d;

        for (var index = 0; index < textLines.Count; index++)
        {
            var textLine = textLines[index];
            result[index] = new TextLineGeometry(
                textLine,
                textLine.FirstTextSourceIndex,
                textLine.FirstTextSourceIndex + textLine.Length,
                currentY);
            currentY += textLine.Height;
        }

        _lineGeometryLayout = layout;
        _lineGeometry = result;
        return result;
    }

    private static int FindFirstLine(TextLineGeometry[] lines, int start)
    {
        var low = 0;
        var high = lines.Length;

        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (lines[middle].End <= start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static void AppendTextRangeBounds(List<Rect> result, TextLineGeometry[] lines, int start, int length)
    {
        var rangeEnd = start + length;
        var firstLine = FindFirstLine(lines, start);

        for (var lineIndex = firstLine; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (line.Start >= rangeEnd)
            {
                break;
            }

            var segmentStart = Math.Max(start, line.Start);
            var segmentEnd = Math.Min(rangeEnd, line.End);
            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            Rect? last = null;
            foreach (var bounds in line.TextLine.GetTextBounds(segmentStart, segmentEnd - segmentStart))
            {
                var rectangle = bounds.Rectangle.WithY(line.Y);
                if (last.HasValue && CanMergeBounds(last.Value, rectangle))
                {
                    last = last.Value.WithWidth(last.Value.Width + rectangle.Width);
                }
                else
                {
                    if (last.HasValue)
                    {
                        result.Add(last.Value);
                    }

                    last = rectangle;
                }
            }

            if (last.HasValue)
            {
                result.Add(last.Value);
            }
        }
    }

    private readonly record struct CodeInlineSpan(
        int Start,
        int Length,
        IBrush? Background,
        CornerRadius CornerRadius,
        Thickness Padding,
        Thickness Margin
    )
    {
        public int End => Start + Length;
    }

    private readonly record struct TextLineGeometry(
        TextLine TextLine,
        int Start,
        int End,
        double Y
    );

    private readonly record struct TextPaintForegroundSpan(
        int Start,
        int Length,
        IBrush Brush,
        int Priority,
        long Order)
    {
        public int End => Start + Length;
    }

    private readonly record struct TextPropertyOverrideSpan(
        int Start,
        int Length,
        TextRunProperties Properties)
    {
        public int End => Start + Length;
    }

    private readonly record struct TextPaintInterval(
        double Left,
        double Right,
        IBrush Brush,
        int Priority,
        long Order);

    private readonly struct TextPaintSpan(
        int start,
        int length,
        IBrush brush,
        Thickness padding,
        CornerRadius cornerRadius,
        Thickness backgroundInset = default
    )
    {
        public int Start { get; } = start;

        public int Length { get; } = length;

        public IBrush Brush { get; } = brush;

        public Thickness Padding { get; } = NormalizeThickness(padding);

        public CornerRadius CornerRadius { get; } = cornerRadius;

        public Thickness BackgroundInset { get; } = NormalizeThickness(backgroundInset);

        private static Thickness NormalizeThickness(Thickness value) => new(
            Math.Max(0, value.Left),
            Math.Max(0, value.Top),
            Math.Max(0, value.Right),
            Math.Max(0, value.Bottom));
    }

    private sealed class HighlightPaintSnapshot(
        TextPaintSpan[] backgroundSpans,
        TextPaintForegroundSpan[] foregroundSpans)
    {
        public static readonly HighlightPaintSnapshot Empty = new([], []);

        public TextPaintSpan[] BackgroundSpans { get; } = backgroundSpans;

        public TextPaintForegroundSpan[] ForegroundSpans { get; } = foregroundSpans;

        public bool HasPaint => BackgroundSpans.Length > 0 || ForegroundSpans.Length > 0;
    }

    private sealed class TextPaintSnapshot
    {
        public static readonly TextPaintSnapshot Empty = new([], [], []);

        public TextPaintSpan[] BackgroundSpans { get; }

        private readonly TextPropertyOverrideSpan[] _propertyOverrides;
        private readonly CodeInlineLayout[] _codeInlineLayouts;

        private TextPaintSnapshot(
            TextPaintSpan[] backgroundSpans,
            TextPropertyOverrideSpan[] propertyOverrides,
            CodeInlineLayout[] codeInlineLayouts)
        {
            BackgroundSpans = backgroundSpans;
            _propertyOverrides = propertyOverrides;
            _codeInlineLayouts = codeInlineLayouts;
        }

        public static TextPaintSnapshot Create(
            IReadOnlyList<TextRun> textRuns,
            IReadOnlyList<CodeInlineSpan> codeInlineSpans,
            FlowDirection flowDirection,
            double letterSpacing)
        {
            List<TextPaintSpan>? backgroundSpans = null;
            List<TextPropertyOverrideSpan>? propertyOverrides = null;
            List<CodeInlineLayout>? codeInlineLayouts = null;
            StringBuilder? codeText = null;
            TextRunProperties? codeProperties = null;
            CodeInlineSpan activeCodeInline = default;
            var hasActiveCodeInline = false;
            var codeTextValid = true;
            var codeInlineIndex = 0;
            var currentIndex = 0;

            foreach (var textRun in textRuns)
            {
                var runLength = textRun.Length;
                if (runLength <= 0)
                {
                    continue;
                }

                var runStart = currentIndex;
                var runEnd = runStart + runLength;

                while (codeInlineIndex < codeInlineSpans.Count &&
                       codeInlineSpans[codeInlineIndex].End <= runStart)
                {
                    FinishCodeInline();
                    codeInlineIndex++;
                }

                if (!hasActiveCodeInline &&
                    codeInlineIndex < codeInlineSpans.Count &&
                    codeInlineSpans[codeInlineIndex].Start < runEnd &&
                    codeInlineSpans[codeInlineIndex].End > runStart)
                {
                    activeCodeInline = codeInlineSpans[codeInlineIndex];
                    hasActiveCodeInline = true;
                    codeText = new StringBuilder(activeCodeInline.Length);
                    codeProperties = null;
                    codeTextValid = true;
                }

                var activeCode = hasActiveCodeInline ? activeCodeInline : default;
                var hasCodeBackground = hasActiveCodeInline && activeCode.Background is not null;
                if (textRun is TextCharacters characters)
                {
                    if (textRun.Properties is { BackgroundBrush: { } background } properties)
                    {
                        var backgroundStart = runStart;
                        var backgroundEnd = runEnd;
                        if (hasCodeBackground)
                        {
                            if (backgroundStart < activeCode.Start)
                            {
                                AddBackgroundSpan(
                                    backgroundStart,
                                    Math.Min(backgroundEnd, activeCode.Start) - backgroundStart,
                                    background,
                                    default,
                                    default,
                                    default);
                            }

                            if (backgroundEnd > activeCode.End)
                            {
                                AddBackgroundSpan(
                                    Math.Max(backgroundStart, activeCode.End),
                                    backgroundEnd - Math.Max(backgroundStart, activeCode.End),
                                    background,
                                    default,
                                    default,
                                    default);
                            }
                        }
                        else
                        {
                            AddBackgroundSpan(runStart, runLength, background, default, default, default);
                        }

                        (propertyOverrides ??= []).Add(
                            new TextPropertyOverrideSpan(
                                runStart,
                                runLength,
                                CloneWithoutBackground(properties)));
                    }

                    if (hasActiveCodeInline)
                    {
                        var overlapStart = Math.Max(runStart, activeCode.Start);
                        var overlapEnd = Math.Min(runEnd, activeCode.End);
                        if (characters.Text.Length < runLength)
                        {
                            codeTextValid = false;
                        }
                        else if (overlapEnd > overlapStart)
                        {
                            var localStart = overlapStart - runStart;
                            codeText!.Append(characters.Text.Span.Slice(localStart, overlapEnd - overlapStart));
                            codeProperties ??= characters.Properties;
                        }
                    }
                }
                else if (hasActiveCodeInline && runStart < activeCode.End && runEnd > activeCode.Start)
                {
                    codeTextValid = false;
                }

                currentIndex = runEnd;
                if (hasActiveCodeInline && currentIndex >= activeCode.End)
                {
                    FinishCodeInline();
                    codeInlineIndex++;
                }
            }

            FinishCodeInline();

            var builtBackgroundSpans = backgroundSpans is null
                ? Array.Empty<TextPaintSpan>()
                : backgroundSpans.ToArray();
            var builtPropertyOverrides = propertyOverrides is null
                ? Array.Empty<TextPropertyOverrideSpan>()
                : propertyOverrides.ToArray();
            var builtCodeInlineLayouts = codeInlineLayouts is null
                ? Array.Empty<CodeInlineLayout>()
                : codeInlineLayouts.ToArray();
            return builtBackgroundSpans.Length == 0 &&
                   builtPropertyOverrides.Length == 0 &&
                   builtCodeInlineLayouts.Length == 0
                ? Empty
                : new TextPaintSnapshot(
                    builtBackgroundSpans,
                    builtPropertyOverrides,
                    builtCodeInlineLayouts);

            void FinishCodeInline()
            {
                if (!hasActiveCodeInline)
                {
                    return;
                }

                var text = codeText?.ToString() ?? string.Empty;
                var leftSpacing = GetHorizontalLayoutSpacing(activeCodeInline.Padding.Left, activeCodeInline.Margin.Left);
                var rightSpacing = GetHorizontalLayoutSpacing(activeCodeInline.Padding.Right, activeCodeInline.Margin.Right);
                var layoutCreated = false;
                if (codeTextValid &&
                    activeCodeInline.Length > 0 &&
                    text.Length == activeCodeInline.Length &&
                    codeProperties is not null &&
                    !ContainsLineBreak(text) &&
                    (leftSpacing > 0 || rightSpacing > 0) &&
                    CodeInlineLayout.TryCreate(
                        activeCodeInline.Start,
                        text,
                        CloneWithoutBackground(codeProperties),
                        flowDirection,
                        letterSpacing,
                        leftSpacing,
                        rightSpacing,
                        out var layout))
                {
                    (codeInlineLayouts ??= []).Add(layout);
                    layoutCreated = true;
                }

                if (activeCodeInline.Background is { } background)
                {
                    var padding = layoutCreated
                        ? new Thickness(0, Math.Max(0, activeCodeInline.Padding.Top), 0, Math.Max(0, activeCodeInline.Padding.Bottom))
                        : NormalizeThickness(activeCodeInline.Padding);
                    var inset = layoutCreated
                        ? new Thickness(
                            Math.Max(0, activeCodeInline.Margin.Left),
                            0,
                            Math.Max(0, activeCodeInline.Margin.Right),
                            0)
                        : default;
                    AddBackgroundSpan(
                        activeCodeInline.Start,
                        activeCodeInline.Length,
                        background,
                        padding,
                        activeCodeInline.CornerRadius,
                        inset);
                }

                hasActiveCodeInline = false;
                codeText = null;
                codeProperties = null;
            }

            void AddBackgroundSpan(
                int start,
                int length,
                IBrush brush,
                Thickness padding,
                CornerRadius cornerRadius,
                Thickness backgroundInset)
            {
                if (length > 0)
                {
                    (backgroundSpans ??= []).Add(
                        new TextPaintSpan(
                            start,
                            length,
                            brush,
                            padding,
                            cornerRadius,
                            backgroundInset));
                }
            }
        }

        public bool TryGetCodeInlineLayout(int textSourceIndex, out CodeInlineLayout layout)
        {
            var low = 0;
            var high = _codeInlineLayouts.Length;

            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (_codeInlineLayouts[middle].End <= textSourceIndex)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            if (low < _codeInlineLayouts.Length && textSourceIndex >= _codeInlineLayouts[low].Start)
            {
                layout = _codeInlineLayouts[low];
                return true;
            }

            layout = null!;
            return false;
        }

        public TextRunProperties GetPropertiesAndLimit(int textSourceIndex, ref int textLength, TextRunProperties properties)
        {
            if (_propertyOverrides.Length == 0)
            {
                return properties;
            }

            var spanIndex = FindFirstPropertyOverrideWithEndAfter(textSourceIndex);
            if (spanIndex == _propertyOverrides.Length)
            {
                return properties;
            }

            var span = _propertyOverrides[spanIndex];
            if (textSourceIndex < span.Start)
            {
                textLength = Math.Min(textLength, span.Start - textSourceIndex);
                return properties;
            }

            textLength = Math.Min(textLength, span.End - textSourceIndex);
            return span.Properties;
        }

        private int FindFirstPropertyOverrideWithEndAfter(int textSourceIndex)
        {
            var low = 0;
            var high = _propertyOverrides.Length;

            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (_propertyOverrides[middle].End <= textSourceIndex)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private static GenericTextRunProperties CloneWithoutBackground(TextRunProperties properties) => new(
            properties.Typeface,
            properties.FontRenderingEmSize,
            properties.TextDecorations,
            properties.ForegroundBrush,
            backgroundBrush: null,
            properties.BaselineAlignment,
            properties.CultureInfo,
            properties.FontFeatures);

        private static double GetHorizontalLayoutSpacing(double primary, double secondary) =>
            Math.Max(0, primary) + Math.Max(0, secondary);

        private static Thickness NormalizeThickness(Thickness value) => new(
            Math.Max(0, value.Left),
            Math.Max(0, value.Top),
            Math.Max(0, value.Right),
            Math.Max(0, value.Bottom));

        private static bool ContainsLineBreak(string text)
        {
            foreach (var character in text)
            {
                if (character is '\r' or '\n' or '\u2028' or '\u2029')
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Holds a fallback-shaped CodeInline and creates disposable run slices for the formatter.
    /// The catalog is immutable; each source request receives a fresh buffer so Avalonia can
    /// split and dispose it while wrapping without mutating a cached glyph array.
    /// </summary>
    private sealed class CodeInlineLayout
    {
        public int Start { get; }

        public int End => Start + text.Length;

        private readonly string text;
        private readonly ShapedCodeInlineRun[] runs;

        private CodeInlineLayout(int start, string text, ShapedCodeInlineRun[] runs)
        {
            Start = start;
            this.text = text;
            this.runs = runs;
        }

        public static bool TryCreate(
            int start,
            string text,
            TextRunProperties properties,
            FlowDirection flowDirection,
            double letterSpacing,
            double leftSpacing,
            double rightSpacing,
            out CodeInlineLayout layout)
        {
            var source = new CodeInlineTextSource(text, properties);
            var paragraphProperties = new GenericTextParagraphProperties(
                flowDirection,
                TextAlignment.Left,
                true,
                false,
                properties,
                TextWrapping.NoWrap,
                0,
                0,
                letterSpacing);

            using var nestedLayout = new TextLayout(source, paragraphProperties, TextTrimming.None);
            List<ShapedCodeInlineRun>? shapedRuns = null;
            ShapedCodeInlineRun? leftmostRun = null;
            ShapedCodeInlineRun? rightmostRun = null;
            var leftmostX = double.PositiveInfinity;
            var rightmostX = double.NegativeInfinity;

            foreach (var line in nestedLayout.TextLines)
            {
                foreach (var bounds in line.GetTextBounds(line.FirstTextSourceIndex, line.Length))
                {
                    foreach (var runBounds in bounds.TextRunBounds)
                    {
                        if (runBounds.TextRun is not ShapedTextRun shapedRun || runBounds.Length <= 0)
                        {
                            // TextLayout includes a one-character TextEndOfParagraph marker in
                            // TextLines. It is a formatter sentinel, not part of the CodeInline
                            // source text, so it must not enter the shaped-run catalog.
                            continue;
                        }

                        var runStart = runBounds.TextSourceCharacterIndex;
                        var runLength = runBounds.Length;
                        if (MemoryMarshal.TryGetString(
                                shapedRun.Text,
                                out var shapedText,
                                out var shapedTextStart,
                                out var shapedTextLength) &&
                            ReferenceEquals(shapedText, text) &&
                            shapedTextLength == shapedRun.Length)
                        {
                            // TextRunBounds uses GlyphRun character hits. For RTL buffers,
                            // Avalonia's hit conversion can include the first glyph cluster as
                            // an offset. The shaped run's source memory is the authoritative
                            // logical position and remains stable across visual reordering.
                            runStart = shapedTextStart;
                            runLength = shapedTextLength;
                        }

                        var runEnd = runStart + runLength;
                        if (runStart < 0 || runEnd > text.Length)
                        {
                            layout = null!;
                            return false;
                        }

                        var glyphs = new GlyphInfo[shapedRun.ShapedBuffer.Length];
                        for (var index = 0; index < glyphs.Length; index++)
                        {
                            glyphs[index] = shapedRun.ShapedBuffer[index];
                        }

                        var run = new ShapedCodeInlineRun(
                            runStart,
                            runLength,
                            shapedRun.Properties,
                            shapedRun.ShapedBuffer.GlyphTypeface,
                            shapedRun.ShapedBuffer.FontRenderingEmSize,
                            shapedRun.ShapedBuffer.BidiLevel,
                            glyphs);
                        (shapedRuns ??= []).Add(run);

                        if (runBounds.Rectangle.Left < leftmostX)
                        {
                            leftmostX = runBounds.Rectangle.Left;
                            leftmostRun = run;
                        }

                        if (runBounds.Rectangle.Right > rightmostX)
                        {
                            rightmostX = runBounds.Rectangle.Right;
                            rightmostRun = run;
                        }
                    }
                }
            }

            if (shapedRuns is null)
            {
                layout = null!;
                return false;
            }

            var builtRuns = shapedRuns
                .OrderBy(static run => run.Start)
                .ToArray();
            var expectedStart = 0;
            foreach (var run in builtRuns)
            {
                if (run.Start != expectedStart)
                {
                    layout = null!;
                    return false;
                }

                expectedStart = run.End;
            }

            if (expectedStart != text.Length || leftmostRun is null || rightmostRun is null)
            {
                layout = null!;
                return false;
            }

            AddEdgeSpacing(leftmostRun, rightmostRun, leftSpacing, rightSpacing);
            layout = new CodeInlineLayout(start, text, builtRuns);
            return true;
        }

        public TextRun? GetTextRun(int textSourceIndex)
        {
            var localIndex = textSourceIndex - Start;
            if ((uint)localIndex >= (uint)text.Length)
            {
                return null;
            }

            var low = 0;
            var high = runs.Length;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (runs[middle].End <= localIndex)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            if (low == runs.Length || localIndex < runs[low].Start)
            {
                return null;
            }

            return runs[low].CreateRun(text, localIndex);
        }

        private static void AddEdgeSpacing(
            ShapedCodeInlineRun leftmostRun,
            ShapedCodeInlineRun rightmostRun,
            double leftSpacing,
            double rightSpacing)
        {
            if (leftSpacing > 0)
            {
                leftmostRun.AddVisualLeadingAdvance(leftSpacing);
            }

            if (rightSpacing > 0)
            {
                rightmostRun.AddVisualTrailingAdvance(rightSpacing);
            }
        }
    }

    private sealed class ShapedCodeInlineRun
    {
        public int Start { get; }

        public int End => Start + _length;

        private bool IsLeftToRight => (bidiLevel & 1) == 0;

        private readonly int _length;
        private readonly TextRunProperties properties;
        private readonly GlyphTypeface glyphTypeface;
        private readonly double fontSize;
        private readonly sbyte bidiLevel;
        private readonly GlyphInfo[] glyphs;
        private readonly int baseCluster;

        public ShapedCodeInlineRun(
            int start,
            int length,
            TextRunProperties properties,
            GlyphTypeface glyphTypeface,
            double fontSize,
            sbyte bidiLevel,
            GlyphInfo[] glyphs)
        {
            Start = start;
            _length = length;
            this.properties = properties;
            this.glyphTypeface = glyphTypeface;
            this.fontSize = fontSize;
            this.bidiLevel = bidiLevel;
            this.glyphs = glyphs;
            baseCluster = glyphs.Length == 0 ? start : glyphs.Min(glyph => glyph.GlyphCluster);
        }

        public void AddVisualLeadingAdvance(double advance) => AddAdvance(advance, IsLeftToRight);

        public void AddVisualTrailingAdvance(double advance) => AddAdvance(advance, !IsLeftToRight);

        public ShapedTextRun CreateRun(string sourceText, int sourceIndex)
        {
            var localIndex = sourceIndex - Start;
            var length = End - sourceIndex;
            var firstCluster = baseCluster + localIndex;
            var lastCluster = baseCluster + _length;

            var selectedCount = 0;
            foreach (var glyph in glyphs)
            {
                if (glyph.GlyphCluster < firstCluster || glyph.GlyphCluster >= lastCluster)
                {
                    continue;
                }

                selectedCount++;
            }

            var buffer = new ShapedBuffer(
                sourceText.AsMemory(sourceIndex - Start, length),
                selectedCount,
                glyphTypeface,
                fontSize,
                bidiLevel);
            var selectedIndex = 0;
            foreach (var glyph in glyphs)
            {
                if (glyph.GlyphCluster < firstCluster || glyph.GlyphCluster >= lastCluster)
                {
                    continue;
                }

                buffer[selectedIndex++] = new GlyphInfo(
                    glyph.GlyphIndex,
                    glyph.GlyphCluster - firstCluster,
                    glyph.GlyphAdvance,
                    glyph.GlyphOffset);
            }

            return new ShapedTextRun(buffer, properties);
        }

        private void AddAdvance(double advance, bool logicalLeading)
        {
            if (glyphs.Length == 0)
            {
                return;
            }

            var isLeftToRight = (bidiLevel & 1) == 0;
            var index = logicalLeading == isLeftToRight ? 0 : glyphs.Length - 1;
            var glyph = glyphs[index];
            var offset = glyph.GlyphOffset;

            // An advance alone reserves space after the glyph. For the visual left edge we
            // also move the edge glyph itself, so the spacing remains outside the inline rather
            // than appearing between its first two glyphs. RTL buffers reverse the logical edge
            // represented by the first glyph, hence the direction-aware condition below.
            if (logicalLeading == isLeftToRight)
            {
                offset = new Vector(offset.X + advance, offset.Y);
            }

            glyphs[index] = new GlyphInfo(
                glyph.GlyphIndex,
                glyph.GlyphCluster,
                glyph.GlyphAdvance + advance,
                offset);
        }
    }

    private readonly struct CodeInlineTextSource(string text, TextRunProperties properties) : ITextSource
    {
        public TextRun GetTextRun(int textSourceIndex) => textSourceIndex >= text.Length
            ? new TextEndOfParagraph()
            : new TextCharacters(text.AsMemory(textSourceIndex), properties);
    }

    internal void InvalidateInlineDecorations(bool affectsLayout = false)
    {
        _paintSnapshotDirty = true;

        if (affectsLayout)
        {
            _lineGeometryLayout = null;
            InvalidateTextLayout();
        }
        else
        {
            InvalidateVisual();
        }
    }

    private readonly struct MarkdownSimpleTextSource(string text, TextRunProperties defaultProperties) : ITextSource
    {
        public TextRun GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= text.Length)
            {
                return new TextEndOfParagraph();
            }

            var remaining = text.AsMemory(textSourceIndex);
            var lineBreakLength = GetLineBreakLength(remaining.Span);
            if (lineBreakLength > 0)
            {
                return new TextEndOfLine(lineBreakLength);
            }

            var textLength = GetLengthBeforeLineBreak(remaining.Span);
            return new TextCharacters(remaining[..textLength], defaultProperties);
        }
    }

    private readonly struct MarkdownInlinesTextSource(IReadOnlyList<TextRun> textRuns, TextPaintSnapshot paintSnapshot) : ITextSource
    {
        public TextRun GetTextRun(int textSourceIndex)
        {
            if (paintSnapshot.TryGetCodeInlineLayout(textSourceIndex, out var codeInlineLayout) &&
                codeInlineLayout.GetTextRun(textSourceIndex) is { } shapedCodeRun)
            {
                return shapedCodeRun;
            }

            var currentPosition = 0;
            foreach (var textRun in textRuns)
            {
                if (textRun.Length <= 0)
                {
                    continue;
                }

                if (textSourceIndex >= currentPosition + textRun.Length)
                {
                    currentPosition += textRun.Length;
                    continue;
                }

                if (textRun is not TextCharacters textCharacters)
                {
                    return textRun;
                }

                var skip = Math.Max(0, textSourceIndex - currentPosition);
                var remaining = textCharacters.Text[skip..];
                var lineBreakLength = GetLineBreakLength(remaining.Span);
                if (lineBreakLength > 0)
                {
                    // Avalonia's LineBreak currently reaches this source as a CRLF
                    // TextCharacters run. Returning TextEndOfLine is important: passing that
                    // run through InlinesTextSource leaves the formatter at the same source
                    // index and can produce repeated zero-length visual lines.
                    return new TextEndOfLine(lineBreakLength);
                }

                var textLength = GetLengthBeforeLineBreak(remaining.Span);
                var properties = paintSnapshot.GetPropertiesAndLimit(
                    textSourceIndex,
                    ref textLength,
                    textCharacters.Properties);

                return new TextCharacters(remaining[..textLength], properties);
            }

            return new TextEndOfParagraph();
        }
    }

    private static int GetLineBreakLength(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return 0;
        }

        if (text[0] == '\r')
        {
            return text.Length > 1 && text[1] == '\n' ? 2 : 1;
        }

        return text[0] is '\n' or '\u2028' or '\u2029' ? 1 : 0;
    }

    private static int GetLengthBeforeLineBreak(ReadOnlySpan<char> text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (GetLineBreakLength(text[index..]) > 0)
            {
                return index;
            }
        }

        return text.Length;
    }

    protected override void OnMeasureInvalidated()
    {
        _searchText = null;
        var textRuns = _textRuns;
        base.OnMeasureInvalidated();
        _textRuns = textRuns;
    }

    private static void InvalidateRendererTextBlockCache(Visual? visual)
    {
        for (var current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is MarkdownRenderer renderer)
            {
                renderer.InvalidateTextBlockCache();
            }
        }
    }

    private Link? pointerLink;
    private Link? pressingLink;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        UpdatePointerOverLink(e.GetPosition(this));

        if (this.GetVisualAncestors().OfType<MarkdownRenderer>().FirstOrDefault() is not null)
        {
            pressingLink = null;
            return;
        }

        pressingLink = pointerLink;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (this.GetVisualAncestors().OfType<MarkdownRenderer>().FirstOrDefault() is not null)
        {
            pressingLink = null;
            return;
        }

        if (pressingLink is not null && pointerLink == pressingLink)
        {
            switch (e.InitialPressMouseButton)
            {
                case MouseButton.Left when pressingLink.HRef is not null:
                {
                    var args = new LinkClickedEventArgs(LinkClickEvent, this, pressingLink.HRef);
                    RaiseEvent(args);
                    e.Handled = args.Handled;
                    pressingLink.IsClicked = true;
                    break;
                }
                case MouseButton.Right when LinkContextMenu is { } contextMenu:
                {
                    contextMenu.DataContext = pointerLink;
                    contextMenu.Open(this);
                    e.Handled = true;
                    break;
                }
            }
        }

        pressingLink = null;

        base.OnPointerReleased(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var renderer = this.GetVisualAncestors().OfType<MarkdownRenderer>().FirstOrDefault();
        if (renderer?.IsPointerSelectionDragging != true)
        {
            UpdatePointerOverLink(e.GetPosition(this));
        }

        if (renderer is not null)
        {
            return;
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        pointerLink = null;
        UpdatePseudoClass();

        base.OnPointerExited(e);
    }

    public new void SelectAll()
    {
        SetCurrentValue(SelectionStartProperty, 0);
        SetCurrentValue(SelectionEndProperty, EscapedTextLength);
    }

    internal Link? UpdatePointerOverLink(Point point)
    {
        pointerLink = Link.HitTestPoint(TextLayout, point, linksByTag);

        UpdatePseudoClass();
        return pointerLink;
    }

    internal void ClearPointerOverLink()
    {
        if (pointerLink is null)
        {
            return;
        }

        pointerLink = null;
        UpdatePseudoClass();
    }

    private void UpdatePseudoClass()
    {
        PseudoClasses.Set(":pointerover-link", pointerLink is not null);
    }
}

