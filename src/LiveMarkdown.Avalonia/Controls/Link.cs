// @author https://github.com/DearVa
// @author https://github.com/AuroraZiling

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Metadata;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Represents a hyperlink rendered inside a Markdown text block.
/// </summary>
[PseudoClasses(":clicked", ":disabled", ":pointerover", ":pressed")]
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
            _textBlock?.ResetLinkInteraction(this);
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

    /// <summary>
    /// Defines the read-only <see cref="IsPointerOver"/> property.
    /// </summary>
    public static readonly DirectProperty<Link, bool> IsPointerOverProperty =
        AvaloniaProperty.RegisterDirect<Link, bool>(nameof(IsPointerOver), link => link.IsPointerOver);

    /// <summary>
    /// Gets whether the pointer is over a rendered fragment of this link.
    /// </summary>
    public bool IsPointerOver
    {
        get;
        private set => SetAndRaise(IsPointerOverProperty, ref field, value);
    }

    /// <summary>
    /// Defines the read-only <see cref="IsPressed"/> property.
    /// </summary>
    public static readonly DirectProperty<Link, bool> IsPressedProperty =
        AvaloniaProperty.RegisterDirect<Link, bool>(nameof(IsPressed), link => link.IsPressed);

    /// <summary>
    /// Gets whether a primary-button activation is pending over this link.
    /// A drag that becomes text selection cancels this state.
    /// </summary>
    public bool IsPressed
    {
        get;
        private set => SetAndRaise(IsPressedProperty, ref field, value);
    }

    private MarkdownTextBlock? _textBlock;

    /// <summary>
    /// Initializes a new instance of the <see cref="Link"/> class.
    /// </summary>
    public Link()
    {
        Classes.Add("Link");
        UpdatePseudoClasses();
    }

    /// <inheritdoc />
    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _textBlock = this.FindLogicalAncestorOfType<MarkdownTextBlock>();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        _textBlock?.ResetLinkInteraction(this);
        _textBlock = null;
        SetIsPointerOver(false);
        SetIsPressed(false);
        base.OnDetachedFromLogicalTree(e);
    }

    internal void SetIsPointerOver(bool value)
    {
        IsPointerOver = value;
        if (!value) SetIsPressed(false);
        PseudoClasses.Set(":pointerover", value);
    }

    internal void SetIsPressed(bool value)
    {
        IsPressed = value;
        PseudoClasses.Set(":pressed", value);
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

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":clicked", IsClicked);
        PseudoClasses.Set(":disabled", HRef is null);
    }
}