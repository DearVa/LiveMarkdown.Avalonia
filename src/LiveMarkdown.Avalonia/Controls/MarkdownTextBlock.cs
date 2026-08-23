using System.Diagnostics.CodeAnalysis;
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
using GraphemeEnumerator = Avalonia.Media.TextFormatting.Unicode.GraphemeEnumerator;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Represents a Markdown text block that can be rendered and interacted with.
/// This class extends <see cref="SelectableTextBlock"/> to fix its selection bugs.
/// </summary>
[PseudoClasses(":pointerover-link")]
public partial class MarkdownTextBlock : SelectableTextBlock
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
        AvaloniaProperty.Register<MarkdownTextBlock, ContextMenu?>(
            nameof(LinkContextMenu),
            inherits: true);

    /// <summary>
    /// Context menu to show when right-clicking a Link.
    /// The value is inherited from an ancestor, including <see cref="MarkdownRenderer.LinkContextMenu"/>.
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

    /// <summary>
    /// Gets the source span in the Markdown document represented by this text block.
    /// </summary>
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
    private string? _layoutText;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarkdownTextBlock"/> class.
    /// </summary>
    public MarkdownTextBlock()
    {
        Highlights.Changed += HandleHighlightsChanged;
    }

    /// <summary>
    /// Gets the complete text represented by this block, including nested inline content.
    /// </summary>
    public string ActualText
    {
        get
        {
            if (Inlines is not { Count: > 0 } inlines) return Text ?? string.Empty;
            return inlines.ActualText;
        }
    }

    /// <summary>
    /// Gets the text represented by this block's own layout coordinate space. Embedded controls
    /// occupy one <see cref="MarkdownTextProjection.ObjectReplacementCharacter"/> position; their
    /// child text blocks use independent layouts.
    /// </summary>
    public string LayoutText => _layoutText ??= Inlines is { Count: > 0 } inlines ? inlines.LayoutText : Text ?? string.Empty;

    /// <summary>
    /// Gets the selected text represented by this block, preserving nested inline content.
    /// </summary>
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
                        currentIndex += TextRun.DefaultTextSourceLength;
                        return;
                    }
                    case InlineUIContainer:
                    {
                        currentIndex += TextRun.DefaultTextSourceLength;
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
                    stringBuilder.Append(textBlock.ActualSelectedText);
                    return;
                }

                foreach (var child in logical.LogicalChildren) AppendLogicalText(child);
            }
        }
    }

    /// <summary>
    /// Gets the length of this block's local layout text.
    /// </summary>
    public int EscapedTextLength => LayoutText.Length;

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

    /// <inheritdoc/>
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);

        if (ContextFlyout is not { IsOpen: true } && ContextMenu is not { IsOpen: true })
        {
            ClearSelection();
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        SubscribeToHighlightStyles(null);
        _layoutText = null;
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

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InvalidateRendererTextState(e.AttachmentPoint);
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        InvalidateRendererTextState(e.AttachmentPoint);
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        SubscribeToHighlightStyles(HighlightStyles);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            InvalidateRendererTextState(this);
        }

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
        InvalidateRegisteredHighlightPaint();
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
        InvalidateRegisteredHighlightPaint();
    }

    private void HandleHighlightStylesChanged(object? sender, EventArgs e)
    {
        InvalidateRegisteredHighlightPaint();
    }

    private void InvalidateRegisteredHighlightPaint()
    {
        var hadForeground = _registeredHighlightPaintSnapshot.ForegroundSpans.Length > 0;
        _registeredHighlightPaintDirty = true;
        var hasForeground = GetRegisteredHighlightPaintSnapshot().ForegroundSpans.Length > 0;

        if (hadForeground || hasForeground)
        {
            _lineGeometryLayout = null;
            InvalidateTextLayout();
            return;
        }

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

    // Selection remains an interaction state owned by SelectableTextBlock. Paint overrides are
    // complete TextRunProperties values, so every metric-affecting property must come from the
    // original run. Only foreground is replaced and native backgrounds are suppressed; shaping
    // and line breaking therefore retain the original run's typography.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_LineSpacing")]
    private extern static void SetLineSpacing(TextParagraphProperties properties, double value);

    /// <inheritdoc/>
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
        var highlightPaint = GetRegisteredHighlightPaintSnapshot();
        var selectionStart = Math.Min(SelectionStart, SelectionEnd);
        var selectionLength = Math.Max(SelectionStart, SelectionEnd) - selectionStart;
        var textStyles = TextStyleSnapshot.Create(
            highlightPaint.ForegroundSpans,
            selectionStart,
            selectionLength,
            SelectionForegroundBrush);

        if (_textRuns is { } textRuns)
        {
            var snapshot = GetPaintSnapshot(textRuns);
            textSource = new MarkdownInlinesTextSource(textRuns, snapshot, textStyles);
        }
        else
        {
            GetPaintSnapshot(null);
            textSource = new MarkdownSimpleTextSource(text ?? string.Empty, defaultProperties, textStyles);
        }

        var maxSize = GetMaxSizeFromConstraint();
        return new TextLayout(textSource, paragraphProperties, TextTrimming, maxSize.Width, maxSize.Height, MaxLines);
    }

    /// <inheritdoc/>
    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        var snapshot = GetPaintSnapshot(_textRuns);
        var highlightPaint = GetRegisteredHighlightPaintSnapshot();

        if (snapshot.BackgroundSpans.Length == 0 && highlightPaint.BackgroundSpans.Length == 0)
        {
            base.RenderTextLayout(context, origin);
            return;
        }

        var lines = GetLineGeometry();
        using (context.PushTransform(Matrix.CreateTranslation(origin)))
        {
            DrawPaintSpans(context, snapshot.BackgroundSpans, lines);
            DrawPaintSpans(context, highlightPaint.BackgroundSpans, lines);
        }

        base.RenderTextLayout(context, origin);
    }

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

        return _registeredHighlightPaintSnapshot = new HighlightPaintSnapshot(
            backgroundSpans is null ? [] : [.. backgroundSpans],
            ResolveForegroundSpans(foregroundSpans));
    }

    private static TextForegroundStyleSpan[] ResolveForegroundSpans(List<TextPaintForegroundSpan>? spans)
    {
        if (spans is not { Count: > 0 })
        {
            return [];
        }

        spans.Sort(static (left, right) =>
        {
            var result = left.Start.CompareTo(right.Start);
            return result != 0 ? result : left.End.CompareTo(right.End);
        });

        var maximumEnd = spans[0].End;
        var hasOverlap = false;
        for (var index = 1; index < spans.Count; index++)
        {
            if (spans[index].Start < maximumEnd)
            {
                hasOverlap = true;
                break;
            }

            maximumEnd = Math.Max(maximumEnd, spans[index].End);
        }

        if (!hasOverlap)
        {
            var result = new List<TextForegroundStyleSpan>(spans.Count);
            foreach (var span in spans)
            {
                AppendForegroundStyle(result, span.Start, span.End, span.Brush);
            }

            return [.. result];
        }

        var boundaries = new List<int>(spans.Count * 2);
        foreach (var span in spans)
        {
            boundaries.Add(span.Start);
            boundaries.Add(span.End);
        }

        boundaries.Sort();
        var resolved = new List<TextForegroundStyleSpan>(boundaries.Count - 1);
        var previousBoundary = boundaries[0];
        for (var boundaryIndex = 1; boundaryIndex < boundaries.Count; boundaryIndex++)
        {
            var boundary = boundaries[boundaryIndex];
            if (boundary == previousBoundary)
            {
                continue;
            }

            TextPaintForegroundSpan? winner = null;
            foreach (var span in spans)
            {
                if (span.Start >= boundary)
                {
                    break;
                }

                if (span.End <= previousBoundary ||
                    winner is { } current && !IsHigherPriority(span, current))
                {
                    continue;
                }

                winner = span;
            }

            if (winner is { } selected)
            {
                AppendForegroundStyle(resolved, previousBoundary, boundary, selected.Brush);
            }

            previousBoundary = boundary;
        }

        return [.. resolved];
    }

    private static void AppendForegroundStyle(List<TextForegroundStyleSpan> spans, int start, int end, IBrush brush)
    {
        if (end <= start)
        {
            return;
        }

        if (spans is { Count: > 0 } && spans[^1].End == start && ReferenceEquals(spans[^1].Brush, brush))
        {
            var previous = spans[^1];
            spans[^1] = previous with { Length = end - previous.Start };
            return;
        }

        spans.Add(new TextForegroundStyleSpan(start, end - start, brush));
    }

    private static bool IsHigherPriority(in TextPaintForegroundSpan candidate, in TextPaintForegroundSpan current) =>
        candidate.Priority > current.Priority ||
        candidate.Priority == current.Priority && candidate.Order > current.Order;

    /// <summary>
    /// Gets the visual rectangles occupied by a text range in this control's text layout.
    /// Coordinates are relative to the text layout origin and are suitable for custom
    /// highlights, search results, and scrolling to a match.
    /// </summary>
    public IReadOnlyList<Rect> GetTextRangeBounds(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, int.MaxValue - length);

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

        _paintSnapshot = textRuns is null ?
            TextPaintSnapshot.Empty :
            TextPaintSnapshot.Create(textRuns, GetCodeInlineSpans(), FlowDirection, LetterSpacing);
        _paintSnapshotTextRuns = textRuns;
        _paintSnapshotDirty = false;
        return _paintSnapshot;
    }

    /// <summary>
    /// Gets the code inline spans in this text block, including nested inline content.
    /// </summary>
    /// <returns></returns>
    public CodeInlineSpan[] GetCodeInlineSpans()
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
                            codeInline,
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

    /// <summary>
    /// Represents a code inline span in the text block, including its source inline and visual properties.
    /// </summary>
    /// <param name="Start"></param>
    /// <param name="Length"></param>
    /// <param name="Source"></param>
    /// <param name="Background"></param>
    /// <param name="CornerRadius"></param>
    /// <param name="Padding"></param>
    /// <param name="Margin"></param>
    public readonly record struct CodeInlineSpan(
        int Start,
        int Length,
        CodeInline Source,
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
        long Order
    )
    {
        public int End => Start + Length;
    }

    private readonly record struct TextForegroundStyleSpan(int Start, int Length, IBrush Brush)
    {
        public int End => Start + Length;
    }

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

    private sealed class HighlightPaintSnapshot(TextPaintSpan[] backgroundSpans, TextForegroundStyleSpan[] foregroundSpans)
    {
        public static readonly HighlightPaintSnapshot Empty = new([], []);

        public TextPaintSpan[] BackgroundSpans { get; } = backgroundSpans;

        public TextForegroundStyleSpan[] ForegroundSpans { get; } = foregroundSpans;
    }

    /// <summary>
    /// Resolves foreground-only paint styles in logical source coordinates. The spans are
    /// non-overlapping and selection has already replaced any lower-priority named highlight.
    /// Native run backgrounds are removed here as well so all background layers share the
    /// rounded-rectangle paint path in <see cref="RenderTextLayout"/>.
    /// </summary>
    private sealed class TextStyleSnapshot
    {
        private static readonly TextStyleSnapshot Empty = new([]);

        private readonly TextForegroundStyleSpan[] _foregroundSpans;

        private TextStyleSnapshot(TextForegroundStyleSpan[] foregroundSpans)
        {
            _foregroundSpans = foregroundSpans;
        }

        public static TextStyleSnapshot Create(
            TextForegroundStyleSpan[] foregroundSpans,
            int selectionStart,
            int selectionLength,
            IBrush? selectionForeground)
        {
            if (selectionLength <= 0 || selectionForeground is null)
            {
                return foregroundSpans.Length == 0 ? Empty : new TextStyleSnapshot(foregroundSpans);
            }

            var start = Math.Max(0, selectionStart);
            var end = selectionStart > int.MaxValue - selectionLength ? int.MaxValue : selectionStart + selectionLength;
            if (end <= start)
            {
                return foregroundSpans.Length == 0 ? Empty : new TextStyleSnapshot(foregroundSpans);
            }

            var result = new List<TextForegroundStyleSpan>(foregroundSpans.Length + 2);
            var selectionAdded = false;
            foreach (var span in foregroundSpans)
            {
                if (span.End <= start)
                {
                    AppendForegroundStyle(result, span.Start, span.End, span.Brush);
                    continue;
                }

                if (span.Start >= end)
                {
                    AddSelection();
                    AppendForegroundStyle(result, span.Start, span.End, span.Brush);
                    continue;
                }

                if (span.Start < start)
                {
                    AppendForegroundStyle(result, span.Start, start, span.Brush);
                }

                AddSelection();
                if (span.End > end)
                {
                    AppendForegroundStyle(result, end, span.End, span.Brush);
                }
            }

            AddSelection();
            return new TextStyleSnapshot([.. result]);

            void AddSelection()
            {
                if (selectionAdded)
                {
                    return;
                }

                AppendForegroundStyle(result, start, end, selectionForeground);
                selectionAdded = true;
            }
        }

        public TextRunProperties GetPropertiesAndLimit(int textSourceIndex, ref int textLength, TextRunProperties properties)
        {
            IBrush? foreground = null;
            var hasForegroundOverride = false;
            if (_foregroundSpans.Length > 0)
            {
                var spanIndex = FindFirstForegroundWithEndAfter(textSourceIndex);
                if (spanIndex < _foregroundSpans.Length)
                {
                    var span = _foregroundSpans[spanIndex];
                    if (textSourceIndex < span.Start)
                    {
                        textLength = Math.Min(textLength, span.Start - textSourceIndex);
                    }
                    else
                    {
                        textLength = Math.Min(textLength, span.End - textSourceIndex);
                        foreground = span.Brush;
                        hasForegroundOverride = true;
                    }
                }
            }

            if (!hasForegroundOverride && properties.BackgroundBrush is null)
            {
                return properties;
            }

            return CreatePaintProperties(
                properties,
                hasForegroundOverride ? foreground : properties.ForegroundBrush);
        }

        private int FindFirstForegroundWithEndAfter(int textSourceIndex)
        {
            var low = 0;
            var high = _foregroundSpans.Length;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (_foregroundSpans[middle].End <= textSourceIndex)
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
    }

    private sealed class TextPaintSnapshot
    {
        public static readonly TextPaintSnapshot Empty = new([], []);

        public TextPaintSpan[] BackgroundSpans { get; }

        private readonly CodeInlineLayout[] _codeInlineLayouts;

        private TextPaintSnapshot(TextPaintSpan[] backgroundSpans, CodeInlineLayout[] codeInlineLayouts)
        {
            BackgroundSpans = backgroundSpans;
            _codeInlineLayouts = codeInlineLayouts;
        }

        public static TextPaintSnapshot Create(
            IReadOnlyList<TextRun> textRuns,
            IReadOnlyList<CodeInlineSpan> codeInlineSpans,
            FlowDirection flowDirection,
            double letterSpacing)
        {
            List<TextPaintSpan>? backgroundSpans = null;
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

                while (codeInlineIndex < codeInlineSpans.Count && codeInlineSpans[codeInlineIndex].End <= runStart)
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
                    if (textRun.Properties is { BackgroundBrush: { } background })
                    {
                        if (hasCodeBackground)
                        {
                            if (runStart < activeCode.Start)
                            {
                                AddBackgroundSpan(
                                    runStart,
                                    Math.Min(runEnd, activeCode.Start) - runStart,
                                    background,
                                    default,
                                    default,
                                    default);
                            }

                            if (runEnd > activeCode.End)
                            {
                                AddBackgroundSpan(
                                    Math.Max(runStart, activeCode.End),
                                    runEnd - Math.Max(runStart, activeCode.End),
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

            var builtBackgroundSpans = backgroundSpans is null ? Array.Empty<TextPaintSpan>() : backgroundSpans.ToArray();
            var builtCodeInlineLayouts = codeInlineLayouts is null ? Array.Empty<CodeInlineLayout>() : codeInlineLayouts.ToArray();
            return builtBackgroundSpans.Length == 0 && builtCodeInlineLayouts.Length == 0 ?
                Empty :
                new TextPaintSnapshot(
                    builtBackgroundSpans,
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
                        CreatePaintProperties(codeProperties, codeProperties.ForegroundBrush),
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
                    var padding = layoutCreated ?
                        new Thickness(0, Math.Max(0, activeCodeInline.Padding.Top), 0, Math.Max(0, activeCodeInline.Padding.Bottom)) :
                        NormalizeThickness(activeCodeInline.Padding);
                    var inset = layoutCreated ?
                        new Thickness(
                            Math.Max(0, activeCodeInline.Margin.Left),
                            0,
                            Math.Max(0, activeCodeInline.Margin.Right),
                            0) :
                        default;
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

        public bool TryGetCodeInlineLayout(int textSourceIndex, [NotNullWhen(true)] out CodeInlineLayout? layout)
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

            layout = null;
            return false;
        }

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
            [NotNullWhen(true)] out CodeInlineLayout? layout)
        {
            // Avalonia's bidi resolver stores one level per Unicode code point, while
            // CoalesceLevels advances a DrawableTextRun by its UTF-16 Length. A pre-shaped
            // run containing a surrogate pair therefore moves past the levels array when a
            // following run is processed. Keep those CodeInlines on the ordinary TextCharacters
            // path until Avalonia uses a consistent unit for drawable runs.
            if (HasCodePointLengthMismatch(text))
            {
                layout = null;
                return false;
            }

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
                layout = null;
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

        private static bool HasCodePointLengthMismatch(ReadOnlySpan<char> text)
        {
            for (var index = 0; index + 1 < text.Length; index++)
            {
                if (char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]))
                {
                    return true;
                }
            }

            return false;
        }

        public TextRun? GetTextRun(int textSourceIndex, TextStyleSnapshot textStyles)
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

            var run = runs[low];
            var length = run.End - localIndex;
            var properties = textStyles.GetPropertiesAndLimit(textSourceIndex, ref length, run.Properties);
            length = CoerceTextRunLength(text.AsSpan(localIndex, run.End - localIndex), length);
            return run.CreateRun(text, localIndex, length, properties);
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

        public int End => Start + field;

        public TextRunProperties Properties { get; }

        private bool IsLeftToRight => (_bidiLevel & 1) == 0;

        private readonly GlyphTypeface _glyphTypeface;
        private readonly double _fontSize;
        private readonly sbyte _bidiLevel;
        private readonly GlyphInfo[] _glyphs;
        private readonly int _baseCluster;

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
            Properties = properties;
            End = length;
            _glyphTypeface = glyphTypeface;
            _fontSize = fontSize;
            _bidiLevel = bidiLevel;
            _glyphs = glyphs;
            _baseCluster = glyphs.Length == 0 ? start : glyphs.Min(glyph => glyph.GlyphCluster);
        }

        public void AddVisualLeadingAdvance(double advance) => AddAdvance(advance, IsLeftToRight);

        public void AddVisualTrailingAdvance(double advance) => AddAdvance(advance, !IsLeftToRight);

        public ShapedTextRun CreateRun(string sourceText, int sourceIndex, int length, TextRunProperties runProperties)
        {
            var localIndex = sourceIndex - Start;
            var firstCluster = _baseCluster + localIndex;
            var lastCluster = firstCluster + length;

            var selectedCount = 0;
            foreach (var glyph in _glyphs)
            {
                if (glyph.GlyphCluster < firstCluster || glyph.GlyphCluster >= lastCluster)
                {
                    continue;
                }

                selectedCount++;
            }

            var buffer = new ShapedBuffer(
                sourceText.AsMemory(sourceIndex, length),
                selectedCount,
                _glyphTypeface,
                _fontSize,
                _bidiLevel);
            var selectedIndex = 0;
            foreach (var glyph in _glyphs)
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

            return new ShapedTextRun(buffer, runProperties);
        }

        private void AddAdvance(double advance, bool logicalLeading)
        {
            if (_glyphs.Length == 0)
            {
                return;
            }

            var isLeftToRight = (_bidiLevel & 1) == 0;
            var index = logicalLeading == isLeftToRight ? 0 : _glyphs.Length - 1;
            var glyph = _glyphs[index];
            var offset = glyph.GlyphOffset;

            // An advance alone reserves space after the glyph. For the visual left edge we
            // also move the edge glyph itself, so the spacing remains outside the inline rather
            // than appearing between its first two glyphs. RTL buffers reverse the logical edge
            // represented by the first glyph, hence the direction-aware condition below.
            if (logicalLeading == isLeftToRight)
            {
                offset = new Vector(offset.X + advance, offset.Y);
            }

            _glyphs[index] = new GlyphInfo(
                glyph.GlyphIndex,
                glyph.GlyphCluster,
                glyph.GlyphAdvance + advance,
                offset);
        }
    }

    private readonly struct CodeInlineTextSource(string text, TextRunProperties properties) : ITextSource
    {
        public TextRun GetTextRun(int textSourceIndex) => textSourceIndex >= text.Length ?
            new TextEndOfParagraph() :
            new TextCharacters(text.AsMemory(textSourceIndex), properties);
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

    private readonly struct MarkdownSimpleTextSource(string text, TextRunProperties defaultProperties, TextStyleSnapshot textStyles) : ITextSource
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

            var availableLength = GetLengthBeforeLineBreak(remaining.Span);
            var textLength = availableLength;
            var properties = textStyles.GetPropertiesAndLimit(textSourceIndex, ref textLength, defaultProperties);
            textLength = CoerceTextRunLength(remaining.Span[..availableLength], textLength);
            return new TextCharacters(remaining[..textLength], properties);
        }
    }

    private readonly struct MarkdownInlinesTextSource(
        IReadOnlyList<TextRun> textRuns,
        TextPaintSnapshot paintSnapshot,
        TextStyleSnapshot textStyles
    ) : ITextSource
    {
        public TextRun GetTextRun(int textSourceIndex)
        {
            if (paintSnapshot.TryGetCodeInlineLayout(textSourceIndex, out var codeInlineLayout) &&
                codeInlineLayout.GetTextRun(textSourceIndex, textStyles) is { } shapedCodeRun)
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

                var availableLength = GetLengthBeforeLineBreak(remaining.Span);
                var textLength = availableLength;
                var properties = textStyles.GetPropertiesAndLimit(
                    textSourceIndex,
                    ref textLength,
                    textCharacters.Properties);
                textLength = CoerceTextRunLength(remaining.Span[..availableLength], textLength);
                return new TextCharacters(remaining[..textLength], properties);
            }

            return new TextEndOfParagraph();
        }
    }

    private static GenericTextRunProperties CreatePaintProperties(TextRunProperties properties, IBrush? foreground) => new(
        properties.Typeface,
        properties.FontRenderingEmSize,
        properties.TextDecorations,
        foreground,
        backgroundBrush: null,
        properties.BaselineAlignment,
        properties.CultureInfo,
        properties.FontFeatures);

    private static int CoerceTextRunLength(ReadOnlySpan<char> text, int length)
    {
        if (length <= 0 || length >= text.Length)
        {
            return Math.Clamp(length, 0, text.Length);
        }

        var finalLength = 0;
        var graphemeEnumerator = new GraphemeEnumerator(text);
        while (graphemeEnumerator.MoveNext(out var grapheme))
        {
            finalLength += grapheme.Length;
            if (finalLength >= length)
            {
                return finalLength;
            }
        }

        return Math.Min(length, text.Length);
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

    /// <inheritdoc/>
    protected override void OnMeasureInvalidated()
    {
        _layoutText = null;
        var textRuns = _textRuns;
        base.OnMeasureInvalidated();
        _textRuns = textRuns;
    }

    private static void InvalidateRendererTextState(Visual? visual)
    {
        for (var current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is MarkdownRenderer renderer)
            {
                renderer.InvalidateRenderedTextState();
            }
        }
    }

    private Link? pointerLink;
    private Link? pressingLink;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        pointerLink = null;
        UpdatePseudoClass();

        base.OnPointerExited(e);
    }

    /// <summary>
    /// Selects all text represented by this block.
    /// </summary>
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

