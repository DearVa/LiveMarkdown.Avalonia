using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Opt-in dominant-axis wheel routing for a horizontal <see cref="ScrollViewer"/> nested inside a vertically
/// scrolling one.
///
/// <para>A <see cref="ScrollViewer"/> that can only move horizontally still consumes a wheel gesture whose
/// delta carries any horizontal component — so a two-finger trackpad scroll over a wide table pans the table
/// sideways while the document under it stands still. A mouse wheel sends a pure vertical delta and does not
/// hit this, which is why it is easy to miss.</para>
///
/// <para>Routing by dominant axis reads the gesture as intent rather than as a vector: a mostly vertical
/// gesture belongs to the nearest ancestor that can actually scroll vertically, and its small horizontal
/// component is hand tremor rather than a request to pan. A mostly horizontal gesture — a deliberate
/// trackpad pan, or shift+wheel — stays with the element under the pointer. This is the same idea platforms
/// ship as directional or axis locking.</para>
///
/// <para>Nothing here is applied by default: the library creates the scrollers, the host decides the policy.
/// Enable it from a style, which needs no code:</para>
///
/// <code>
/// &lt;Style Selector="ScrollViewer.Table"&gt;
///   &lt;Setter Property="md:WheelAxisRouting.Enabled" Value="True"/&gt;
/// &lt;/Style&gt;
///
/// &lt;Style Selector="CodeBlock /template/ ScrollViewer#PART_ScrollViewer"&gt;
///   &lt;Setter Property="md:WheelAxisRouting.Enabled" Value="True"/&gt;
/// &lt;/Style&gt;
/// </code>
///
/// <para>The same contract holds for any horizontal scroller a host embeds beside the rendered markdown, so
/// the property is not restricted to this library's own.</para>
/// </summary>
public class WheelAxisRouting : AvaloniaObject
{
    private WheelAxisRouting() { }

    /// <summary>Route wheel gestures over this <see cref="ScrollViewer"/> by dominant axis.</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<WheelAxisRouting, ScrollViewer, bool>("Enabled");

    /// <summary>Pixels moved per unit of wheel delta when a gesture is handed to an ancestor. Defaults to
    /// 48, matching the step <see cref="ScrollViewer"/> uses for one mouse notch; trackpads deliver
    /// fractional deltas and scale through the same factor.</summary>
    public static readonly AttachedProperty<double> StepProperty =
        AvaloniaProperty.RegisterAttached<WheelAxisRouting, ScrollViewer, double>("Step", 48d);

    /// <summary>How much more vertical than horizontal a gesture must be before it is treated as vertical
    /// intent. 1.0 — the default — is a plain "taller than it is wide" test; raise it to be stricter about
    /// what counts as a read rather than a pan.</summary>
    public static readonly AttachedProperty<double> VerticalBiasProperty =
        AvaloniaProperty.RegisterAttached<WheelAxisRouting, ScrollViewer, double>("VerticalBias", 1d);

    public static void SetEnabled(ScrollViewer element, bool value) => element.SetValue(EnabledProperty, value);
    public static bool GetEnabled(ScrollViewer element) => element.GetValue(EnabledProperty);

    public static void SetStep(ScrollViewer element, double value) => element.SetValue(StepProperty, value);
    public static double GetStep(ScrollViewer element) => element.GetValue(StepProperty);

    public static void SetVerticalBias(ScrollViewer element, double value) => element.SetValue(VerticalBiasProperty, value);
    public static double GetVerticalBias(ScrollViewer element) => element.GetValue(VerticalBiasProperty);

    static WheelAxisRouting() =>
        EnabledProperty.Changed.AddClassHandler<ScrollViewer>((scrollViewer, e) =>
        {
            if (e.GetNewValue<bool>()) Attach(scrollViewer);
            else Detach(scrollViewer);
        });

    /// <summary>Route wheel gestures over <paramref name="inner"/> by dominant axis. Idempotent — a template
    /// re-application can resolve the same target twice, and a doubly-attached handler would move the
    /// document two steps per notch. Equivalent to setting <see cref="EnabledProperty"/>; here for hosts
    /// wiring a scroller up in code rather than from a style.</summary>
    public static void Attach(ScrollViewer inner)
    {
        inner.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        inner.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    /// <summary>Stop routing gestures over <paramref name="inner"/>. Safe to call when never attached.</summary>
    public static void Detach(ScrollViewer inner) =>
        inner.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer inner) return;
        // Explicit horizontal intent stays with the element under the pointer.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        if (e.Delta.Y == 0) return;
        if (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) * GetVerticalBias(inner)) return;

        // Vertical intent: hand it to the nearest ancestor that can actually move vertically. If none can
        // (a wide table in a document shorter than its viewport), leave the event alone rather than deadening
        // the wheel over the element.
        var step = GetStep(inner);
        for (var a = inner.FindAncestorOfType<ScrollViewer>(); a is not null; a = a.FindAncestorOfType<ScrollViewer>())
        {
            if (a.Extent.Height > a.Viewport.Height + 0.5)
            {
                a.Offset = new Vector(a.Offset.X,
                    Math.Clamp(a.Offset.Y - e.Delta.Y * step, 0, Math.Max(0, a.Extent.Height - a.Viewport.Height)));
                e.Handled = true;
                return;
            }
        }
    }
}
