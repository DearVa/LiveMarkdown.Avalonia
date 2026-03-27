using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Metadata;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
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

    public string ActualText
    {
        get
        {
            if (Inlines is not { Count: > 0 } inlines) return Text ?? string.Empty;
            return inlines.ActualText;
        }
    }

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
                        length++;
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

    // As a workaround to fix selection rendering bugs in SelectableTextBlock,
    // we override CreateTextLayout methods to handle selection rendering ourselves.
    private readonly PropertyInfo _lineSpacingPropertyInfo =
        typeof(TextParagraphProperties).GetProperty("LineSpacing", BindingFlags.Instance | BindingFlags.NonPublic)!;

    protected override TextLayout CreateTextLayout(string? text)
    {
        if (_textRuns is null) return base.CreateTextLayout(text);

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
        _lineSpacingPropertyInfo.SetValue(paragraphProperties, LineSpacing);

        List<ValueSpan<TextRunProperties>>? textStyleOverrides = null;
        var selectionStart = SelectionStart;
        var selectionEnd = SelectionEnd;
        var start = Math.Min(selectionStart, selectionEnd);
        var length = Math.Max(selectionStart, selectionEnd) - start;

        if (length > 0 && SelectionForegroundBrush != null)
        {
            // This is a fixture to apply selection foreground color.
            // Original implementation in SelectableTextBlock has some bugs that
            // cause incorrect rendering when selection and SelectionForegroundBrush exists.
            // It overrides original typeface, fontFeatures and fontSize which causes issues like missing italic/bold style.

            var accumulatedLength = 0;
            foreach (var textRun in _textRuns)
            {
                var runLength = textRun.Text.Length;
                if (accumulatedLength + runLength <= start ||
                    accumulatedLength >= start + length)
                {
                    accumulatedLength += runLength;
                    continue;
                }

                var overlapStart = Math.Max(start, accumulatedLength);
                var overlapEnd = Math.Min(start + length, accumulatedLength + runLength);
                var overlapLength = overlapEnd - overlapStart;

                textStyleOverrides ??= [];

                textStyleOverrides.Add(
                    new ValueSpan<TextRunProperties>(
                        overlapStart,
                        overlapLength,
                        new GenericTextRunProperties(
                            textRun.Properties?.Typeface ?? typeface,
                            FontSize,
                            textRun.Properties?.TextDecorations ?? TextDecorations,
                            SelectionForegroundBrush,
                            fontFeatures: textRun.Properties?.FontFeatures ?? FontFeatures)
                    ));

                accumulatedLength += runLength;
            }
        }

        var textSource = new InlinesTextSource(_textRuns, textStyleOverrides);
        var maxWidth = double.IsNaN(_constraint.Width) ? 0.0 : _constraint.Width;
        var maxHeight = double.IsNaN(_constraint.Height) ? 0.0 : _constraint.Height;
        var maxSize = new Size(maxWidth, maxHeight);

        return new TextLayout(
            textSource,
            paragraphProperties,
            TextTrimming,
            maxSize.Width,
            maxSize.Height,
            MaxLines);
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        var selectionStart = SelectionStart;
        var selectionEnd = SelectionEnd;
        var selectionBrush = SelectionBrush;

        if (selectionStart != selectionEnd && selectionBrush != null)
        {
            var start = Math.Min(selectionStart, selectionEnd);
            var length = Math.Max(selectionStart, selectionEnd) - start;

            using (context.PushTransform(Matrix.CreateTranslation(origin)))
            {
                foreach (var rect in TextLayoutHitTestTextRange(start, length))
                {
                    context.FillRectangle(selectionBrush, PixelRect.FromRect(rect, 1).ToRect(1));
                }
            }
        }

        TextLayout.Draw(context, origin);
    }

    private IEnumerable<Rect> TextLayoutHitTestTextRange(int start, int length)
    {
        if (start + length <= 0) yield break;

        var currentY = 0d;
        const double epsilon = 0.5; // Tolerance for floating point comparison
        foreach (var textLine in TextLayout.TextLines)
        {
            // Current line isn't covered.
            if (textLine.FirstTextSourceIndex + textLine.Length <= start)
            {
                currentY += textLine.Height;
                continue;
            }

            var textBounds = textLine.GetTextBounds(start, length);
            if (textBounds.Count > 0)
            {
                Rect? last = null;
                foreach (var bounds in textBounds)
                {
                    if (last.HasValue &&
                        Math.Abs(last.Value.Right - bounds.Rectangle.Left) < epsilon &&
                        Math.Abs(last.Value.Top - currentY) < epsilon)
                    {
                        last = last.Value.WithWidth(last.Value.Width + bounds.Rectangle.Width);
                    }
                    else
                    {
                        if (last.HasValue) yield return last.Value;
                        last = bounds.Rectangle.WithY(currentY);
                    }

                    foreach (var runBounds in bounds.TextRunBounds)
                    {
                        start += runBounds.Length;
                        length -= runBounds.Length;
                    }
                }

                if (last.HasValue) yield return last.Value;
            }

            if (textLine.FirstTextSourceIndex + textLine.Length >= start + length) break;
            currentY += textLine.Height;
        }
    }

    protected override void OnMeasureInvalidated()
    {
        var textRuns = _textRuns;
        base.OnMeasureInvalidated();
        _textRuns = textRuns;
    }

    private Link? pointerLink;
    private Link? pressingLink;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        HitTestLink(e.GetPosition(this));
        pressingLink = pointerLink;

        if (this.GetVisualAncestors().OfType<MarkdownRenderer>().FirstOrDefault() is not null)
        {
            return;
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
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

        if (this.GetVisualAncestors().OfType<MarkdownRenderer>().FirstOrDefault() is not null)
        {
            return;
        }

        base.OnPointerReleased(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        HitTestLink(e.GetPosition(this));

        if (this.GetVisualAncestors().OfType<MarkdownRenderer>().FirstOrDefault() is not null)
        {
            return;
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        pointerLink = null;

        base.OnPointerExited(e);
    }

    public new void SelectAll()
    {
        SetCurrentValue(SelectionStartProperty, 0);
        SetCurrentValue(SelectionEndProperty, EscapedTextLength);
    }

    private void HitTestLink(Point point)
    {
        if (Link.HitTestPoint(TextLayout, point) is { } link)
        {
            pointerLink = link;
        }
        else
        {
            pointerLink = null;
        }

        UpdatePseudoClass();
    }

    private void UpdatePseudoClass()
    {
        PseudoClasses.Set(":pointerover-link", pointerLink is not null);
    }
}

