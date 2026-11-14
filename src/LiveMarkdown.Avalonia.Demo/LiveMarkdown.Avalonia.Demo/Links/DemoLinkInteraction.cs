using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LiveMarkdown.Avalonia.Demo.Links.Preview;

namespace LiveMarkdown.Avalonia.Demo.Links;

/// <summary>
/// Host-owned interaction shared by the sample renderer and dedicated playgrounds.
/// One menu and one delayed path tooltip are retained per renderer, not per link.
/// </summary>
public sealed partial class DemoLinkInteraction : IDisposable
{
    private readonly Control _owner;
    private readonly MarkdownRenderer _renderer;
    private readonly DemoLinkActions _actions;
    private readonly Action<DemoLinkTarget, DemoOperationResult> _report;
    private readonly LinkPreviewIcons _icons;
    private readonly CancellationTokenSource _lifetime = new();
    // A background tick can be starved while the pointer keeps generating input.
    private readonly DispatcherTimer _hoverTimer = new(DispatcherPriority.Default);
    private readonly Popup _tooltip = new() { Placement = PlacementMode.BottomEdgeAlignedLeft, IsHitTestVisible = false };
    private readonly ContextMenu _menu = new();
    private readonly ContextMenu? _previousMenu;
    private MarkdownTextBlock? _hoverBlock;
    private Link? _hoverLink;
    private Point _pointerPosition;
    private bool _isDisposed;

    public DemoLinkInteraction(Control owner, MarkdownRenderer renderer, Action<DemoLinkTarget, DemoOperationResult> report)
        : this(owner, renderer, report, null)
    {
    }

    internal DemoLinkInteraction(
        Control owner,
        MarkdownRenderer renderer,
        Action<DemoLinkTarget, DemoOperationResult> report,
        WebPreviewService? previews)
    {
        _owner = owner;
        _renderer = renderer;
        _actions = new DemoLinkActions(owner);
        _report = report;
        _previews = previews;
        _icons = new LinkPreviewIcons(renderer);
        _previousMenu = renderer.LinkContextMenu;
        // PlacementTarget positions a popup; logical ownership also supplies its theme and template.
        ((ISetLogicalParent)_tooltip).SetParent(owner);
        _tooltip.Closed += HandlePopupClosed;
        _menu.DataContextChanged += HandleMenuTargetChanged;
        _menu.Closed += HandleMenuClosed;
        _hoverTimer.Tick += HandleHoverTimer;
        renderer.LinkContextMenu = _menu;
        renderer.LinkClick += HandleLinkClick;
        renderer.LinkPointerOverChanged += HandleHoverChanged;
        renderer.AddHandler(InputElement.PointerMovedEvent, HandlePointerMoved, RoutingStrategies.Tunnel);
        owner.AddHandler(InputElement.PointerPressedEvent, HandlePointerPressed, RoutingStrategies.Tunnel);
        owner.AddHandler(ScrollViewer.ScrollChangedEvent, HandleScrollChanged);
        owner.AddHandler(InputElement.KeyDownEvent, HandlePreviewKeyDown, RoutingStrategies.Tunnel);
        owner.AddHandler(InputElement.PointerWheelChangedEvent, HandlePointerWheel, RoutingStrategies.Tunnel);
    }

    public async Task ExecuteAsync(DemoLinkTarget target, DemoLinkAction action, string label = "")
    {
        HideTooltip();
        var result = await _actions.ExecuteAsync(target, action, label, _lifetime.Token);
        if (!_isDisposed)
        {
            _report(target, result);
        }
    }

    public void OpenContextMenu(Control placementTarget, DemoLinkTarget target, string label)
    {
        _menu.DataContext = new MenuTarget(target, label);
        _menu.Open(placementTarget);
    }

    internal void Dismiss()
    {
        HideTooltip();
        _hoverBlock = null;
        _hoverLink = null;
        _menu.Close();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _lifetime.Cancel();
        Dismiss();
        ((ISetLogicalParent)_tooltip).SetParent(null);
        _tooltip.Closed -= HandlePopupClosed;
        _menu.DataContextChanged -= HandleMenuTargetChanged;
        _menu.Closed -= HandleMenuClosed;
        _hoverTimer.Tick -= HandleHoverTimer;
        _renderer.LinkClick -= HandleLinkClick;
        _renderer.LinkPointerOverChanged -= HandleHoverChanged;
        _renderer.RemoveHandler(InputElement.PointerMovedEvent, HandlePointerMoved);
        _icons.Dispose();
        _owner.RemoveHandler(InputElement.PointerPressedEvent, HandlePointerPressed);
        _owner.RemoveHandler(ScrollViewer.ScrollChangedEvent, HandleScrollChanged);
        _owner.RemoveHandler(InputElement.KeyDownEvent, HandlePreviewKeyDown);
        _owner.RemoveHandler(InputElement.PointerWheelChangedEvent, HandlePointerWheel);
        if (ReferenceEquals(_renderer.LinkContextMenu, _menu))
        {
            _renderer.LinkContextMenu = _previousMenu;
        }

        _lifetime.Dispose();
    }

    private async void HandleLinkClick(object? sender, LinkClickedEventArgs e)
    {
        e.Handled = true;
        var target = DemoLinkTarget.Parse(e.HRef?.OriginalString ?? string.Empty);
        await ExecuteAsync(target, target.Kind == DemoLinkKind.Local ? DemoLinkAction.Reveal : DemoLinkAction.Open);
    }

    private void HandleMenuTargetChanged(object? sender, EventArgs e)
    {
        HideTooltip();
        // Programmatic ContextMenu.Open does not raise Opening. The renderer assigns its
        // clicked Link immediately before opening, so this remains lazy and runs before layout.
        MenuTarget? menuTarget = _menu.DataContext switch
        {
            Link { HRef: { } href } link => new MenuTarget(DemoLinkTarget.Parse(href.OriginalString), string.Concat(link.Inlines.Select(GetText))),
            MenuTarget reference => reference,
            _ => null
        };
        if (menuTarget is null)
        {
            _menu.ItemsSource = null;
            return;
        }

        var target = menuTarget.Target;
        var label = menuTarget.Label;
        var items = new List<object>();
        if (target.Kind == DemoLinkKind.Local)
        {
            items.Add(Item("Reveal in file manager", DemoLinkAction.Reveal));
            items.Add(Item(DemoLinkActions.HasExecutionExtension(target.Address) ? "Run…" : "Open…", DemoLinkAction.Open));
            items.Add(Item("Open with…", DemoLinkAction.OpenWith));
            items.Add(Item("Save a copy…", DemoLinkAction.SaveAs));
            items.Add(new Separator());
            items.Add(Item("Reveal existing parent", DemoLinkAction.RevealParent));
        }
        else if (target.Kind == DemoLinkKind.Web)
        {
            items.Add(Item("Open in browser", DemoLinkAction.Open));
        }

        items.Add(Item(target.Kind == DemoLinkKind.Local ? "Copy path" : "Copy address", DemoLinkAction.CopyAddress));
        items.Add(Item("Copy link text", DemoLinkAction.CopyText));
        _menu.ItemsSource = items;

        MenuItem Item(string title, DemoLinkAction action)
        {
            var item = new MenuItem { Header = title };
            item.Click += async (_, _) => await ExecuteAsync(target, action, label);
            return item;
        }
    }

    private void HandleMenuClosed(object? sender, RoutedEventArgs e)
    {
        // A recycled message must not be retained through the last context menu's DataContext.
        _menu.DataContext = null;
    }

    private static string GetText(Inline inline)
    {
        return inline switch
        {
            Run run => run.Text ?? string.Empty,
            Span span => string.Concat(span.Inlines.Select(GetText)),
            LineBreak => Environment.NewLine,
            _ => string.Empty
        };
    }

    private void HandlePointerMoved(object? sender, PointerEventArgs e)
    {
        _pointerPosition = e.GetPosition(_renderer);
        var properties = e.GetCurrentPoint(_renderer).Properties;
        _isPointerDown = properties.IsLeftButtonPressed || properties.IsRightButtonPressed || properties.IsMiddleButtonPressed;
        if (!_tooltip.IsOpen)
        {
            StartHoverTimer();
        }
    }

    private void HandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isPointerDown = true;
        HideTooltip();
    }

    private void HandleScrollChanged(object? sender, ScrollChangedEventArgs e) => HideTooltip();

    private void HandleHoverChanged(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not MarkdownTextBlock block)
        {
            return;
        }

        var link = block.PointerOverLink;
        if (ReferenceEquals(_hoverLink, link) || link is null && !ReferenceEquals(_hoverBlock, block))
        {
            return;
        }

        HideTooltip();
        _hoverBlock = link is null ? null : block;
        _hoverLink = link;
        StartHoverTimer();
    }

    private void StartHoverTimer()
    {
        if (_hoverTimer.IsEnabled || _isPointerDown || _menu.IsOpen || _hoverLink?.HRef is not { } href)
        {
            return;
        }

        var hasWebPreview = _previews is not null && DemoLinkTarget.Parse(href.OriginalString).Kind == DemoLinkKind.Web;
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(hasWebPreview ? 150 : 500);
        _hoverTimer.Start();
    }

    private void HandleHoverTimer(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (_hoverBlock is not { IsEffectivelyVisible: true } block || _hoverLink is not { HRef: { } href } link ||
            !ReferenceEquals(block.PointerOverLink, link) || block.GetInlineGeometry(link) is not { Rects.Count: > 0 } geometry)
        {
            return;
        }

        var target = DemoLinkTarget.Parse(href.OriginalString);
        var position = _renderer.TranslatePoint(_pointerPosition, block);
        var anchor = geometry.Rects.FirstOrDefault(rect => position is { } point && rect.Contains(point));
        if (anchor == default)
        {
            anchor = geometry.Rects[0];
        }

        if (target.Kind == DemoLinkKind.Web && _previews is { } previews)
        {
            ShowWebPreview(block, link, target, anchor, previews);
            return;
        }

        _tooltip.ShouldUseOverlayLayer = false;
        _tooltip.Placement = PlacementMode.BottomEdgeAlignedLeft;
        _tooltip.ClearValue(Popup.PlacementConstraintAdjustmentProperty);
        var text = target.Kind switch
        {
            DemoLinkKind.Local when target.IsNetworkPath => $"Network location · not probed on hover\n{target.Address}",
            DemoLinkKind.Local => target.Address,
            DemoLinkKind.Web => $"{new Uri(target.Address).Host}\n{target.Address}",
            _ => $"{target.Reason}\n{target.Address}"
        };
        _tooltip.Child = new Border
        {
            Background = ResourceBrush("PopupBackgroundColor", Brushes.DimGray),
            BorderBrush = ResourceBrush("BorderColor", Brushes.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8),
            Child = new TextBlock { Text = text, MaxWidth = 520, FontSize = 13, TextWrapping = TextWrapping.Wrap }
        };
        _tooltip.PlacementTarget = block;
        _tooltip.PlacementRect = anchor;
        _tooltip.IsOpen = true;
    }

    private IBrush ResourceBrush(string key, IBrush fallback)
    {
        return _owner.FindResource(_owner.ActualThemeVariant, key) switch
        {
            IBrush brush => brush,
            Color color => new SolidColorBrush(color),
            _ => fallback
        };
    }

    private void HideTooltip()
    {
        _hoverTimer.Stop();
        _tooltip.IsOpen = false;
        ReleaseWebPreview();
    }

    private sealed record MenuTarget(DemoLinkTarget Target, string Label);
}
