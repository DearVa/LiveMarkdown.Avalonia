// @author https://github.com/DearVa
// @author https://github.com/AuroraZiling

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Metadata;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Represents a hyperlink rendered inside a Markdown text block.
/// </summary>
[PseudoClasses(":clicked", ":disabled")]
public class Link : Span
{
    /// <summary>
    /// Gets or sets the image of the link. If set to null, the link will display a selectable text block instead.
    /// </summary>
    public Image? Image
    {
        get;
        set
        {
            if (value == field) return;

            field = value;
            if (value is null) Inlines.Clear();
            else Inlines.Add(new InlineUIContainer(value));
        }
    }

    /// <summary>
    /// Defines the <see cref="HRef"/> property.
    /// </summary>
    public static readonly DirectProperty<Link, Uri?> HRefProperty =
        AvaloniaProperty.RegisterDirect<Link, Uri?>(
            nameof(HRef),
            o => o.HRef,
            (o, v) => o.HRef = v);

    /// <summary>
    /// Gets or sets the link reference (HRef) of the link. This must be called from the UI thread.
    /// If set to null, the link will be disabled and will not respond to clicks.
    /// </summary>
    public Uri? HRef
    {
        get;
        set
        {
            if (!SetAndRaise(HRefProperty, ref field, value)) return;
            UpdatePseudoClasses();
            _openCommand?.NotifyCanExecuteChanged();
            _copyCommand?.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Defines the <see cref="IsClicked"/> property.
    /// </summary>
    public static readonly DirectProperty<Link, bool> IsClickedProperty =
        AvaloniaProperty.RegisterDirect<Link, bool>(
            nameof(IsClicked),
            o => o.IsClicked,
            (o, v) => o.IsClicked = v);

    /// <summary>
    /// Gets or sets whether the link has been clicked.
    /// </summary>
    public bool IsClicked
    {
        get;
        set
        {
            if (!SetAndRaise(IsClickedProperty, ref field, value)) return;
            UpdatePseudoClasses();
        }
    }

    internal string Tag { get; }

    private static long nextTagId;
    private WeakReference<MarkdownTextBlock>? registeredTextBlock;

    /// <summary>
    /// Initializes a new instance of the <see cref="Link"/> class.
    /// </summary>
    public Link()
    {
        Classes.Add("Link");
        FontFeatures =
        [
            new FontFeature
            {
                Tag = Tag = $"LINK:{Interlocked.Increment(ref nextTagId)}"
            },
        ];
        UpdatePseudoClasses();
    }

    /// <summary>
    /// Called when the link is attached to a logical tree.
    /// </summary>
    /// <param name="e">The event arguments for the logical tree attachment.</param>
    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);

        if (this.FindLogicalAncestorOfType<MarkdownTextBlock>() is not { } textBlock)
        {
            return;
        }

        if (registeredTextBlock?.TryGetTarget(out var oldTextBlock) == true && oldTextBlock != textBlock)
        {
            oldTextBlock.UnregisterLink(this);
        }

        registeredTextBlock = new WeakReference<MarkdownTextBlock>(textBlock);
        textBlock.RegisterLink(this);
    }

    /// <summary>
    /// Called when the link is detached from a logical tree.
    /// </summary>
    /// <param name="e">The event arguments for the logical tree detachment.</param>
    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        if (registeredTextBlock?.TryGetTarget(out var textBlock) == true)
        {
            textBlock.UnregisterLink(this);
        }

        registeredTextBlock = null;
        base.OnDetachedFromLogicalTree(e);
    }

    /// <summary>
    /// Gets the command that opens the link in the default web browser.
    /// </summary>
    public ICommand OpenCommand => _openCommand ??= new SimpleCommand(Open, () => HRef is not null);

    private SimpleCommand? _openCommand;

    /// <summary>
    /// Opens the link in the default web browser.
    /// </summary>
    public async void Open()
    {
        if (HRef is null) return;
        if (this.FindLogicalAncestorOfType<TopLevel>() is not { } topLevel) return;

        await topLevel.Launcher.LaunchUriAsync(HRef);
    }

    /// <summary>
    /// Gets the command that copies the link URL to the clipboard.
    /// </summary>
    public ICommand CopyCommand => _copyCommand ??= new SimpleCommand(Copy, () => HRef is not null);

    private SimpleCommand? _copyCommand;

    /// <summary>
    /// Copies the link URL to the clipboard.
    /// </summary>
    public async void Copy()
    {
        if (HRef is null) return;
        if (this.FindLogicalAncestorOfType<TopLevel>() is not { Clipboard: { } clipboard }) return;

        await clipboard.SetTextAsync(HRef.ToString());
    }

    /// <summary>
    /// Gets the command that copies the link text to the clipboard.
    /// </summary>
    public ICommand CopyTextCommand => field ??= new SimpleCommand(CopyText);

    /// <summary>
    /// Copies the link Text (Content) to the clipboard.
    /// </summary>
    public async void CopyText()
    {
        if (Inlines.ActualText is not { Length: > 0 } text) return;
        if (this.FindLogicalAncestorOfType<TopLevel>() is not { Clipboard: { } clipboard }) return;

        await clipboard.SetTextAsync(text);
    }

    /// <summary>
    /// This is a VERY hacky way to hit test an Link from a TextLayout.
    /// Avalonia does not provide a built-in way to do this, so we rely on font features to mark link.
    /// TODO: Remove this method when Avalonia provides a better way to hit test inlines.
    /// </summary>
    /// <param name="textLayout"></param>
    /// <param name="point"></param>
    /// <param name="linksByTag">The link index owned by the containing text block.</param>
    /// <returns></returns>
    internal static Link? HitTestPoint(TextLayout textLayout, Point point, IReadOnlyDictionary<string, Link> linksByTag)
    {
        var textLine = HitTestTextLine(textLayout, point);
        var textRun = HitTestTextRun(textLine, point);
        var tag = textRun?.Properties?.FontFeatures?.FirstOrDefault(f => f.Tag.StartsWith("LINK:", StringComparison.Ordinal))?.Tag;
        return tag is not null && linksByTag.TryGetValue(tag, out var link) ? link : null;
    }

    // Following code is copied from Avalonia's TextLine implementation
    private static TextLine? HitTestTextLine(TextLayout textLayout, Point point)
    {
        var textLines = textLayout.TextLines;
        var currentY = 0d;
        foreach (var currentLine in textLines)
        {
            if (currentY + currentLine.Height > point.Y) return currentLine;

            currentY += currentLine.Height;
        }

        return null;
    }

    private static TextRun? HitTestTextRun(TextLine? textLine, Point point)
    {
        if (textLine?.TextRuns is not { Count: > 0 } textRuns)
        {
            return null;
        }

        var distance = point.X - textLine.Start;
        var lastIndex = textRuns.Count - 1;

        if (textRuns[lastIndex] is TextEndOfLine)
        {
            lastIndex--;
        }

        if (lastIndex < 0)
        {
            return null;
        }

        if (distance <= 0)
        {
            return null;
        }

        if (distance >= textLine.WidthIncludingTrailingWhitespace)
        {
            return null;
        }

        // process hit that happens within the line
        var currentDistance = 0.0;
        TextRun? currentRun = null;
        for (var i = 0; i <= lastIndex; i++)
        {
            currentRun = textRuns[i];

            if (currentRun is ShapedTextRun { ShapedBuffer.IsLeftToRight: false })
            {
                var rightToLeftIndex = i;

                while (rightToLeftIndex + 1 <= textRuns.Count - 1)
                {
                    if (textRuns[++rightToLeftIndex] is not ShapedTextRun nextShaped || nextShaped.ShapedBuffer.IsLeftToRight)
                    {
                        break;
                    }

                    rightToLeftIndex++;
                }

                for (var j = i; j <= rightToLeftIndex; j++)
                {
                    if (j > textRuns.Count - 1)
                    {
                        break;
                    }

                    currentRun = textRuns[j];

                    if (currentRun is not ShapedTextRun shapedTextRun)
                    {
                        continue;
                    }

                    if (currentDistance + shapedTextRun.Size.Width <= distance)
                    {
                        currentDistance += shapedTextRun.Size.Width;
                        continue;
                    }

                    return currentRun;
                }
            }

            if (currentRun is DrawableTextRun drawableTextRun)
            {
                if (i < textRuns.Count - 1 && currentDistance + drawableTextRun.Size.Width < distance)
                {
                    currentDistance += drawableTextRun.Size.Width;
                    continue;
                }
            }
            else
            {
                continue;
            }

            break;
        }

        return currentRun;
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":clicked", IsClicked);
        PseudoClasses.Set(":disabled", HRef is null);
    }
}
