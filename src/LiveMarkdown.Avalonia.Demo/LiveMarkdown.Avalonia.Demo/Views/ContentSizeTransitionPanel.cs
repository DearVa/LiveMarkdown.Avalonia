using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;

namespace LiveMarkdown.Avalonia.Demo.Views;

/// <summary>
/// Animates the viewport of a single, stable card. Adapted from Everywhere's
/// ContentSizeTransitionPanel; no outgoing presenter or page-size cache is needed here.
/// </summary>
internal sealed class ContentSizeTransitionPanel : Decorator
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);
    private static readonly CubicEaseOut Easing = new();

    private TopLevel? _topLevel;
    private Size _currentSize;
    private Size _fromSize;
    private Size _targetSize;
    private TimeSpan? _startedAt;
    private bool _hasArranged;
    private bool _isAnimating;
    private bool _hasPendingFrame;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel = null;
        _hasArranged = false;
        _isAnimating = false;
        _startedAt = null;
        base.OnDetachedFromVisualTree(e);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Measure at the actual constraint, never at the animated viewport size.
        var desired = base.MeasureOverride(availableSize);
        if (!_hasArranged || _topLevel is null || !IsEffectivelyVisible)
        {
            _currentSize = _targetSize = desired;
            _isAnimating = false;
        }
        else if (_targetSize != desired)
        {
            _fromSize = _currentSize = Constrain(Bounds.Size, availableSize);
            _targetSize = desired;
            _startedAt = null;
            _isAnimating = _fromSize != _targetSize;
            RequestFrame();
        }

        return Constrain(_currentSize, availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // The stable card content keeps its final layout while the viewport reveals it.
        Child?.Arrange(new Rect(_targetSize));
        _hasArranged = true;
        return finalSize;
    }

    private void RequestFrame()
    {
        if (_isAnimating && !_hasPendingFrame && _topLevel is { } topLevel)
        {
            _hasPendingFrame = true;
            topLevel.RequestAnimationFrame(Advance);
        }
    }

    private void Advance(TimeSpan time)
    {
        _hasPendingFrame = false;
        if (!_isAnimating || _topLevel is null)
        {
            return;
        }

        _startedAt ??= time;
        var progress = Math.Clamp((time - _startedAt.Value) / Duration, 0d, 1d);
        var eased = Easing.Ease(progress);
        _currentSize = new Size(
            _fromSize.Width + (_targetSize.Width - _fromSize.Width) * eased,
            _fromSize.Height + (_targetSize.Height - _fromSize.Height) * eased);
        _isAnimating = progress < 1d;
        InvalidateMeasure();
        RequestFrame();
    }

    private static Size Constrain(Size size, Size available) => new(Math.Min(size.Width, available.Width), Math.Min(size.Height, available.Height));
}
