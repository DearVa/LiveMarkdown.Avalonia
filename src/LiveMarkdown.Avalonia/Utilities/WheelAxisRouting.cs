using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Dominant-axis wheel routing for the HORIZONTAL scrollers inside rendered markdown
/// (tables, code blocks).
///
/// <para>Avalonia's ScrollViewer maps a vertical wheel onto the horizontal axis when horizontal is the only
/// direction it can move — a reasonable default for a standalone scroller, and exactly wrong for a wide
/// table INSIDE a vertically scrolling transcript: the reader's scroll-through slides the table sideways and
/// the page stalls under the pointer. Routing by dominant axis restores the reading contract: a mostly
/// vertical gesture belongs to the nearest ancestor that scrolls vertically; a mostly horizontal gesture
/// (trackpad pan, shift+wheel) belongs to the element under the pointer.</para>
///
/// <para>Public because the same contract holds for every horizontal scroller a host embeds beside the
/// rendered markdown, not just the ones this library creates.</para>
/// </summary>
public static class WheelAxisRouting
{
    /// <summary>Matches the wheel step ScrollViewer uses for a mouse notch; trackpads deliver fractional
    /// deltas and scale through the same factor.</summary>
    private const double StepPx = 48;

    /// <summary>Route wheel gestures over <paramref name="inner"/> by dominant axis. Idempotent.</summary>
    public static void Attach(ScrollViewer inner)
    {
        // Template re-application can resolve the same handler target twice — make attach idempotent.
        inner.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        inner.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer inner) return;
        // Explicit horizontal intent stays with the element under the pointer.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        if (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)) return;
        if (e.Delta.Y == 0) return;

        // Vertical intent: hand it to the nearest ancestor that can actually move vertically. If none can
        // (a wide table in a chat shorter than its viewport), leave the event alone rather than deadening
        // the wheel over the element.
        for (var a = inner.FindAncestorOfType<ScrollViewer>(); a is not null; a = a.FindAncestorOfType<ScrollViewer>())
        {
            if (a.Extent.Height > a.Viewport.Height + 0.5)
            {
                a.Offset = new Vector(a.Offset.X,
                    Math.Clamp(a.Offset.Y - e.Delta.Y * StepPx, 0, Math.Max(0, a.Extent.Height - a.Viewport.Height)));
                e.Handled = true;
                return;
            }
        }
    }
}
