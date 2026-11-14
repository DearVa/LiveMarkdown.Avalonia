using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace LiveMarkdown.Avalonia;

public partial class MarkdownTextBlock
{
    /// <summary>
    /// Defines the read-only <see cref="PointerOverLink"/> property.
    /// </summary>
    public static readonly DirectProperty<MarkdownTextBlock, Link?> PointerOverLinkProperty =
        AvaloniaProperty.RegisterDirect<MarkdownTextBlock, Link?>(nameof(PointerOverLink), block => block.PointerOverLink);

    /// <summary>
    /// Gets the link currently under the pointer in this block.
    /// </summary>
    public Link? PointerOverLink
    {
        get;
        private set => SetAndRaise(PointerOverLinkProperty, ref field, value);
    }

    /// <summary>
    /// Defines the bubbling <see cref="LinkPointerOverChanged"/> event.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> LinkPointerOverChangedEvent =
        RoutedEvent.Register<MarkdownTextBlock, RoutedEventArgs>(nameof(LinkPointerOverChanged), RoutingStrategies.Bubble);

    /// <summary>
    /// Raised when the pointer enters or leaves a link, not for every pointer movement.
    /// The source is the text block; read <see cref="PointerOverLink"/> for the current link.
    /// Layout changes can also end a hover. Preview content and timing belong to the host.
    /// </summary>
    public event EventHandler<RoutedEventArgs>? LinkPointerOverChanged
    {
        add => AddHandler(LinkPointerOverChangedEvent, value);
        remove => RemoveHandler(LinkPointerOverChangedEvent, value);
    }

    private Link? _pressingLink;
    private bool _isLinkSelectionDragging;
    private Point _linkPressPoint;
    private TopLevel? _linkPointerRoot;
    private Point _linkPointerPosition;

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.Pointer.IsPrimary) return;
        UpdatePointerOverLink(e.GetPosition(this));
        if (this.FindAncestorOfType<MarkdownRenderer>() is not null) return;

        _pressingLink = PointerOverLink;
        _isLinkSelectionDragging = false;
        _linkPressPoint = e.GetPosition(this);
        _pressingLink?.SetIsPressed(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _pressingLink.HRef is not null);
        base.OnPointerPressed(e);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (this.FindAncestorOfType<MarkdownRenderer>() is not null) return;
        UpdatePointerOverLink(e.GetPosition(this));
        var link = _pressingLink;
        ClearLinkPress();
        _isLinkSelectionDragging = false;

        if (link is not null && ReferenceEquals(PointerOverLink, link) && !HasExceededLinkTapDistance(e.GetPosition(this), e.Pointer.Type))
        {
            switch (e.InitialPressMouseButton)
            {
                case MouseButton.Left when link.HRef is not null:
                {
                    var args = new LinkClickedEventArgs(LinkClickEvent, this, link.HRef) { Link = link };
                    RaiseEvent(args);
                    e.Handled = args.Handled;
                    link.IsClicked = true;
                    break;
                }
                case MouseButton.Right when LinkContextMenu is { } contextMenu:
                    contextMenu.DataContext = link;
                    contextMenu.Open(this);
                    e.Handled = true;
                    break;
            }
        }

        base.OnPointerReleased(e);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!e.Pointer.IsPrimary) return;
        var renderer = this.FindAncestorOfType<MarkdownRenderer>();
        if (renderer is null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            HasExceededLinkTapDistance(e.GetPosition(this), e.Pointer.Type))
        {
            _isLinkSelectionDragging = true;
            ClearLinkPress();
        }
        if (renderer?.IsPointerSelectionDragging == true || _isLinkSelectionDragging)
        {
            ClearPointerOverLink();
        }
        else
        {
            UpdatePointerOverLink(e.GetPosition(this));
        }
        if (renderer is not null) return;

        base.OnPointerMoved(e);
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (e.Pointer.IsPrimary) ClearPointerOverLink();
        base.OnPointerExited(e);
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        ClearLinkPress();
        _isLinkSelectionDragging = false;
        base.OnPointerCaptureLost(e);
    }

    internal Link? UpdatePointerOverLink(Point point)
    {
        _linkPointerRoot = TopLevel.GetTopLevel(this);
        if (_linkPointerRoot is { } root && this.TranslatePoint(point, root) is { } rootPoint) _linkPointerPosition = rootPoint;
        SetPointerOverLink(GetLinkAt(point));
        return PointerOverLink;
    }

    internal void ClearPointerOverLink()
    {
        _linkPointerRoot = null;
        SetPointerOverLink(null);
    }

    internal void ResetLinkInteraction(Link link)
    {
        if (ReferenceEquals(PointerOverLink, link)) ClearPointerOverLink();
        if (ReferenceEquals(_pressingLink, link)) ClearLinkPress();
        link.SetIsPressed(false);
        this.FindAncestorOfType<MarkdownRenderer>()?.CancelLinkInteraction(link);
    }

    private void SetPointerOverLink(Link? link)
    {
        var previous = PointerOverLink;
        if (ReferenceEquals(previous, link)) return;

        previous?.SetIsPointerOver(false);
        PointerOverLink = link;
        link?.SetIsPointerOver(true);
        PseudoClasses.Set(":pointerover-link", link is not null);
        RaiseEvent(new RoutedEventArgs(LinkPointerOverChangedEvent, this));
    }

    private void HandleLinkLayoutUpdated(object? sender, EventArgs e)
    {
        if (_linkPointerRoot is not { } root) return;

        // Keep stationary-pointer hover accurate when reflow or scrolling moves the text.
        // Do not retain hover through a popup or another visual covering the text block.
        var hit = root.InputHitTest(_linkPointerPosition) as Visual;
        var hitBlock = hit as MarkdownTextBlock ?? hit?.FindAncestorOfType<MarkdownTextBlock>();
        if (!ReferenceEquals(hitBlock, this) || root.TranslatePoint(_linkPointerPosition, this) is not { } point)
        {
            ClearPointerOverLink();
            return;
        }

        SetPointerOverLink(GetLinkAt(point));
    }

    private void ClearLinkPress()
    {
        _pressingLink?.SetIsPressed(false);
        _pressingLink = null;
    }

    private void ClearLinkInteraction()
    {
        this.FindAncestorOfType<MarkdownRenderer>()?.CancelLinkInteraction(this);
        _isLinkSelectionDragging = false;
        ClearLinkPress();
        ClearPointerOverLink();
    }

    private bool HasExceededLinkTapDistance(Point point, PointerType pointerType)
    {
        var settings = this.GetPlatformSettings() ?? Application.Current?.PlatformSettings;
        var size = settings?.GetTapSize(pointerType) ?? new Size(4, 4);
        return !new Rect(_linkPressPoint, new Size()).Inflate(new Thickness(size.Width, size.Height)).ContainsExclusive(point);
    }
}