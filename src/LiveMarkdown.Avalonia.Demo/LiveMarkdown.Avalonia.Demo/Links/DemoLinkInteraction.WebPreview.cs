using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using LiveMarkdown.Avalonia.Demo.Links.Preview;
using LiveMarkdown.Avalonia.Demo.Views;

namespace LiveMarkdown.Avalonia.Demo.Links;

public sealed partial class DemoLinkInteraction
{
    private readonly WebPreviewService? _previews;
    private WebPreviewCard? _card;
    private Link? _cardLink;
    private CancellationTokenSource? _previewWait;
    private bool _isPointerDown;

    private void ShowWebPreview(MarkdownTextBlock block, Link link, DemoLinkTarget target, Rect anchor, WebPreviewService previews)
    {
        var address = new Uri(target.Address);
        var cached = previews.GetCached(address);
        var topLevel = TopLevel.GetTopLevel(block);
        var card = new WebPreviewCard
        {
            Width = Math.Min(410, Math.Max(0, (topLevel?.ClientSize.Width ?? 426) - 16)),
        };
        _card = card;
        _cardLink = link;
        link.PropertyChanged += HandleCardLinkChanged;
        card.ShowAddress(address);

        // Keep placement within the window without opening a separate native popup.
        _tooltip.ShouldUseOverlayLayer = true;
        // Pick the side using the expanded card, so loading a cover does not suddenly flip the popup.
        var anchorOnWindow = topLevel is null ? null : block.TranslatePoint(anchor.BottomLeft, topLevel);
        var placeAbove = topLevel is not null && anchorOnWindow is { } location &&
            topLevel.ClientSize.Height - location.Y < 320 && location.Y > topLevel.ClientSize.Height / 2;
        _tooltip.Placement = placeAbove ? PlacementMode.TopEdgeAlignedLeft : PlacementMode.BottomEdgeAlignedLeft;
        _tooltip.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.SlideY;
        _tooltip.Child = card;
        _tooltip.PlacementTarget = block;
        _tooltip.PlacementRect = anchor;
        _tooltip.IsOpen = true;
        if (cached is not null)
        {
            ApplyPreview(card, target, cached);
        }
        else
        {
            LoadPreviewAsync(card, link, target, address, previews).Detach();
        }
    }

    private async Task LoadPreviewAsync(WebPreviewCard card, Link link, DemoLinkTarget target, Uri address, WebPreviewService previews)
    {
        using var wait = new CancellationTokenSource();
        var token = wait.Token;
        _previewWait = wait;
        try
        {
            var progress = new Progress<WebPreview>(preview =>
            {
                if (_previewWait?.Token == token && IsCurrent())
                {
                    ApplyPreview(card, target, preview);
                }
            });
            var preview = await previews.GetAsync(address, token, progress);
            if (IsCurrent())
            {
                _previewWait = null;
                ApplyPreview(card, target, preview);
            }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_previewWait, wait))
            {
                _previewWait = null;
            }
        }

        bool IsCurrent()
        {
            return !token.IsCancellationRequested && ReferenceEquals(_card, card) &&
                link.HRef is { } href && DemoLinkTarget.Parse(href.OriginalString) == target;
        }
    }

    private void ApplyPreview(WebPreviewCard card, DemoLinkTarget target, WebPreview preview)
    {
        card.Show(preview);
        if (preview.Icon is { } icon)
        {
            _icons.SetFavicon(target, icon);
        }
    }

    private void HandlePreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_card is null || e.Key != Key.Escape)
        {
            return;
        }

        HideTooltip();
        e.Handled = true;
    }

    private void HandlePopupClosed(object? sender, EventArgs e) => ReleaseWebPreview();

    private void HandlePointerWheel(object? sender, PointerWheelEventArgs e) => HideTooltip();

    private void HandleCardLinkChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Link.HRefProperty)
        {
            HideTooltip();
        }
    }

    private void ReleaseWebPreview()
    {
        _previewWait?.Cancel();
        _previewWait = null;
        if (_cardLink is { } link)
        {
            link.PropertyChanged -= HandleCardLinkChanged;
            _cardLink = null;
        }

        if (_card is { } card)
        {
            _card = null;
            card.Dispose();
        }

        _tooltip.Child = null;
        _tooltip.PlacementTarget = null;
    }
}
