using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

/// <summary>
/// A wide table or code block owns a HORIZONTAL scroller. Avalonia maps a vertical wheel onto the horizontal
/// axis when horizontal is the only direction that scroller can move, so scrolling the document with the
/// pointer over one slides it sideways and the page stalls. These cover the routing that restores it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class WheelAxisRoutingTests
{
    private HeadlessUnitTestSession session = null!;

    [OneTimeSetUp]
    public void StartSession() => session = HeadlessSession.Current;

    /// <summary>The headless test app carries no control theme, so a bare ScrollViewer comes up
    /// template-less and never builds a presenter — its Extent and Viewport stay 0 and the routing has
    /// nothing to hand the gesture to. Supply the one part ScrollViewer looks for.</summary>
    private static ScrollViewer Scroller() => new()
    {
        Template = new FuncControlTemplate<ScrollViewer>((_, ns) =>
            new ScrollContentPresenter { Name = "PART_ContentPresenter" }.RegisterInNameScope(ns)),
    };

    /// <summary>An outer vertical scroller with a wide, horizontally scrolling inner one inside it —
    /// the shape a table makes inside a rendered document.</summary>
    private static (Window Window, ScrollViewer Outer, ScrollViewer Inner) Build()
    {
        var inner = Scroller();
        inner.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        inner.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        inner.Width = 200;
        inner.Height = 60;
        inner.Content = new Border { Width = 2000, Height = 40 };

        var outer = Scroller();
        outer.Width = 220;
        outer.Height = 200;
        outer.Content = new StackPanel { Children = { new Border { Height = 400 }, inner, new Border { Height = 400 } } };

        var window = new Window { Width = 300, Height = 260, Content = outer };
        window.Show();
        window.UpdateLayout();

        WheelAxisRouting.Attach(inner);
        return (window, outer, inner);
    }

    private static void Wheel(ScrollViewer target, double dx, double dy, KeyModifiers mods = KeyModifiers.None) =>
        target.RaiseEvent(WheelArgs(target, dx, dy, mods));

    private static PointerWheelEventArgs WheelArgs(ScrollViewer target, double dx, double dy, KeyModifiers mods) =>
        new(target, new Pointer(0, PointerType.Mouse, true), target, default, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), mods, new Vector(dx, dy))
        {
            RoutedEvent = InputElement.PointerWheelChangedEvent,
            Source = target,
        };

    [Test]
    public void A_Vertical_Wheel_Over_The_Inner_Scroller_Moves_The_DOCUMENT() => session.Dispatch(() =>
    {
        var (window, outer, inner) = Build();
        var innerBefore = inner.Offset.X;

        Wheel(inner, 0, -1);

        Assert.That(outer.Offset.Y, Is.GreaterThan(0),
            "a vertical gesture belongs to the nearest ancestor that scrolls vertically");
        Assert.That(inner.Offset.X, Is.EqualTo(innerBefore).Within(0.01),
            "and must not be turned sideways into the table");
        window.Close();
    }, CancellationToken.None).GetAwaiter().GetResult();

    [Test]
    public void A_Horizontal_Wheel_Stays_With_The_Element_Under_The_Pointer() => session.Dispatch(() =>
    {
        var (window, outer, inner) = Build();

        Wheel(inner, -1, 0);

        Assert.That(outer.Offset.Y, Is.EqualTo(0).Within(0.01),
            "explicit horizontal intent is not the document's to consume");
        window.Close();
    }, CancellationToken.None).GetAwaiter().GetResult();

    [Test]
    public void Shift_Wheel_Stays_With_The_Element_Under_The_Pointer() => session.Dispatch(() =>
    {
        var (window, outer, inner) = Build();

        Wheel(inner, 0, -1, KeyModifiers.Shift);

        Assert.That(outer.Offset.Y, Is.EqualTo(0).Within(0.01),
            "shift+wheel is the standard gesture for scrolling a thing sideways on purpose");
        window.Close();
    }, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>With nothing above able to move vertically, leave the event alone rather than deadening the
    /// wheel over the element.</summary>
    [Test]
    public void With_No_Scrollable_Ancestor_The_Event_Is_Left_Alone() => session.Dispatch(() =>
    {
        var inner = Scroller();
        inner.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        inner.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        inner.Width = 200;
        inner.Height = 60;
        inner.Content = new Border { Width = 2000, Height = 40 };
        var window = new Window { Width = 300, Height = 260, Content = inner };
        window.Show();
        window.UpdateLayout();
        WheelAxisRouting.Attach(inner);

        var args = WheelArgs(inner, 0, -1, KeyModifiers.None);
        inner.RaiseEvent(args);

        Assert.That(args.Handled, Is.False);
        window.Close();
    }, CancellationToken.None).GetAwaiter().GetResult();

    [Test]
    public void Attach_Is_Idempotent() => session.Dispatch(() =>
    {
        var (window, outer, inner) = Build();
        WheelAxisRouting.Attach(inner);   // e.g. a template re-application
        WheelAxisRouting.Attach(inner);

        Wheel(inner, 0, -1);

        Assert.That(outer.Offset.Y, Is.EqualTo(48).Within(0.01),
            "a doubly-attached handler would move the document two steps for one notch");
        window.Close();
    }, CancellationToken.None).GetAwaiter().GetResult();
}
