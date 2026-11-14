using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class LinkInteractionTests
{
    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public Task Geometry_WithNestedFormattingAndFontFeatures_FollowsWrappedText(FlowDirection direction) => Run(() =>
    {
        var label = "中文 e\u0301 😀 abc אבג and more linked text";
        var child = new Run(label) { FontFeatures = FontFeatureCollection.Parse("liga 0") };
        var bold = new Bold { Inlines = new InlineCollection { child } };
        var link = new Link { HRef = new Uri("https://example.com"), Inlines = new InlineCollection { bold } };
        var block = new MarkdownTextBlock
        {
            FontSize = 20, FlowDirection = direction, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(11, 7),
            Inlines = new InlineCollection { new Run("prefix "), link, new Run(" suffix") }
        };
        using var view = new TestView(block, false, 180);
        var geometry = GeometryOf(block, link);
        Assert.That(geometry, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(geometry.Start, Is.EqualTo(7));
            Assert.That(geometry.Length, Is.EqualTo(label.Length));
            Assert.That(geometry.Rects.Count, Is.GreaterThan(1));
            Assert.That(block.ActualText, Is.EqualTo("prefix " + label + " suffix"));
            Assert.That(link.FontFeatures?.Any(feature => feature.Tag.StartsWith("LINK:")) ?? false, Is.False);
        });
        foreach (var rect in geometry.Rects)
        {
            Assert.That(block.GetLinkAt(rect.Center), Is.SameAs(link));
            Assert.That(block.GetInlineAt(rect.Center), Is.SameAs(child));
        }
        Assert.That(block.GetLinkAt(new Point(-1, -1)), Is.Null);
        Assert.That(block.GetLinkAt(new Point(block.Bounds.Width + 100, geometry.Rects[0].Center.Y)), Is.Null);
        Assert.That(block.GetInlineGeometry(new Run("other")), Is.Null);
        block.SelectAll();
        Assert.That(block.ActualSelectedText, Is.EqualTo(block.ActualText));
        var windowRects = geometry.GetRects(view.Window);
        Assert.That(windowRects[0].Center, Is.EqualTo(block.TranslatePoint(geometry.Rects[0].Center, view.Window)));
    });

    [Test]
    public Task Geometry_BeforeLayout_DoesNotCreateLayoutOrClaimAHit() => Run(() =>
    {
        var link = CreateLink("link");
        var block = new MarkdownTextBlock { Inlines = new InlineCollection { link } };
        Assert.That(block.GetInlineGeometry(link), Is.Null);
        Assert.That(block.GetLinkAt(new Point(5, 5)), Is.Null);
    });

    [Test]
    public Task Geometry_AfterContentChangesAndReflow_UpdatesRangesWithoutChangingSnapshots() => Run(() =>
    {
        var before = new Run("a ");
        var link = CreateLink("a link that can wrap across multiple lines");
        var block = new MarkdownTextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap, Inlines = new InlineCollection { before, link } };
        using var view = new TestView(block, false);
        var initial = GeometryOf(block, link);
        before.Text = "longer ";
        before.Text = "a much longer prefix ";
        view.Window.Width = 160;
        view.Window.UpdateLayout();
        var current = GeometryOf(block, link);
        Assert.Multiple(() =>
        {
            Assert.That(initial.Start, Is.EqualTo(2));
            Assert.That(current.Start, Is.EqualTo(before.Text.Length));
            Assert.That(current.Rects.Count, Is.GreaterThan(1));
            Assert.That(current, Is.Not.SameAs(initial));
        });
        link.Inlines.Clear();
        link.Inlines.Add(new Run("new"));
        view.Window.UpdateLayout();
        Assert.That(GeometryOf(block, link).Length, Is.EqualTo(3));
        block.Inlines?.Remove(link);
        view.Window.UpdateLayout();
        Assert.That(block.GetInlineGeometry(link), Is.Null);
    });

    [Test]
    public Task Geometry_EmbeddedImageLink_UsesTheExistingObjectPosition() => Run(() =>
    {
        var link = new Link { HRef = new Uri("https://example.com"), Image = new Image { Width = 30, Height = 24 } };
        var block = new MarkdownTextBlock { Inlines = new InlineCollection { new Run("a"), link, new Run("b") } };
        using var view = new TestView(block, false);
        var geometry = GeometryOf(block, link);
        Assert.That(geometry.Start, Is.EqualTo(1));
        Assert.That(geometry.Length, Is.EqualTo(1));
        Assert.That(block.GetLinkAt(geometry.Rects.Single().Center), Is.SameAs(link));
    });

    [TestCase(false)]
    [TestCase(true)]
    public Task HoverAndPress_WithStyles_ExposeStatesWithoutChangingTextOrWrapping(bool hasRenderer) => Run(() =>
    {
        var link = CreateLink("selectable linked text that wraps");
        var block = new MarkdownTextBlock { FontSize = 20, TextWrapping = TextWrapping.Wrap, Inlines = new InlineCollection { link, new Run(" prose") } };
        var decoration = new TextDecorationCollection
        {
            new TextDecoration { Location = TextDecorationLocation.Underline, StrokeThicknessUnit = TextDecorationUnit.Pixel,
                StrokeThickness = 1, StrokeDashArray = new AvaloniaList<double> { 2, 2 } }
        };
        block.Styles.Add(new Style(selector => selector.OfType<Link>().Class(":pointerover"))
        {
            Setters = { new Setter(Inline.TextDecorationsProperty, decoration) }
        });
        using var view = new TestView(block, hasRenderer, 210);
        var widths = block.TextLayout.TextLines.Select(line => line.Width).ToArray();
        var point = view.PointOn(link);
        var changes = new List<(object? Source, Link? Link)>();
        view.Window.AddHandler(MarkdownTextBlock.LinkPointerOverChangedEvent, (_, e) => changes.Add((e.Source, block.PointerOverLink)));
        view.Window.MouseMove(point);
        view.Window.UpdateLayout();
        view.Window.MouseMove(point + new Vector(0.2, 0));
        Assert.Multiple(() =>
        {
            Assert.That(link.IsPointerOver, Is.True);
            Assert.That(link.TextDecorations, Is.SameAs(decoration));
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Link, Is.SameAs(link));
            Assert.That(changes[0].Source, Is.SameAs(block));
            Assert.That(block.TextLayout.TextLines.Select(line => line.Width), Is.EqualTo(widths));
        });
        LinkClickedEventArgs? click = null;
        view.Window.AddHandler(MarkdownTextBlock.LinkClickEvent, (_, e) => click = e, RoutingStrategies.Bubble, true);
        view.Window.MouseDown(point, MouseButton.Left);
        Assert.That(link.IsPressed, Is.True);
        view.Window.MouseUp(point, MouseButton.Left);
        var clicked = click ?? throw new AssertionException("The click event was not raised.");
        Assert.Multiple(() =>
        {
            Assert.That(link.IsPressed, Is.False);
            Assert.That(link.IsClicked, Is.True);
            Assert.That(clicked.Link, Is.SameAs(link));
            Assert.That(clicked.HRef, Is.EqualTo(link.HRef));
            Assert.That(clicked.Source, Is.SameAs(block));
        });
        view.Window.MouseMove(new Point(350, 200));
        Assert.That(link.IsPointerOver, Is.False);
        Assert.That(changes.Last().Link, Is.Null);
    });

    [TestCase(false)]
    [TestCase(true)]
    public Task DragStartingOnLink_CancelsPressAndActivationAndPreservesPartialSelection(bool hasRenderer) => Run(() =>
    {
        var link = CreateLink("selectable linked text");
        var block = new MarkdownTextBlock { FontSize = 20, Inlines = new InlineCollection { link } };
        using var view = new TestView(block, hasRenderer);
        var count = 0;
        block.LinkClick += (_, _) => count++;
        var start = view.PointOn(link);
        view.Window.MouseMove(start);
        view.Window.MouseDown(start, MouseButton.Left);
        view.Window.MouseMove(start + new Vector(50, 0), RawInputModifiers.LeftMouseButton);
        Assert.That(link.IsPressed, Is.False);
        Assert.That(link.IsPointerOver, Is.False);
        view.Window.MouseUp(start + new Vector(50, 0), MouseButton.Left);
        Assert.That(count, Is.Zero);
        Assert.That(block.ActualSelectedText, Is.Not.Empty.And.Not.EqualTo(block.ActualText));
    });

    [TestCase(false)]
    [TestCase(true)]
    public Task TargetChangeOrRemoval_EndsHoverAndCancelsPendingActivation(bool hasRenderer) => Run(() =>
    {
        var link = CreateLink("link");
        var block = new MarkdownTextBlock { FontSize = 20, Inlines = new InlineCollection { link } };
        using var view = new TestView(block, hasRenderer);
        var count = 0;
        block.LinkClick += (_, _) => count++;
        var point = view.PointOn(link);
        view.Window.MouseMove(point);
        view.Window.MouseDown(point, MouseButton.Left);
        link.HRef = new Uri("https://example.org/changed");
        Assert.That(link.IsPointerOver, Is.False);
        Assert.That(link.IsPressed, Is.False);
        view.Window.MouseUp(point, MouseButton.Left);
        Assert.That(count, Is.Zero);
        view.Window.MouseMove(point);
        Assert.That(link.IsPointerOver, Is.True);
        view.Window.MouseDown(point, MouseButton.Left);
        block.Inlines?.Clear();
        Assert.That(link.IsPointerOver, Is.False);
        Assert.That(link.IsPressed, Is.False);
        view.Window.MouseUp(point, MouseButton.Left);
        Assert.That(count, Is.Zero);
    });

    [TestCase(false)]
    [TestCase(true)]
    public Task HidingOrMovingText_EndsStationaryHoverAndPendingPress(bool hasRenderer) => Run(() =>
    {
        var link = CreateLink("link");
        var block = new MarkdownTextBlock { FontSize = 20, Inlines = new InlineCollection { link } };
        using var view = new TestView(block, hasRenderer);
        var point = view.PointOn(link);
        view.Window.MouseMove(point);
        Assert.That(link.IsPointerOver, Is.True);
        block.Margin = new Thickness(0, 80, 0, 0);
        view.Window.UpdateLayout();
        Assert.That(link.IsPointerOver, Is.False);
        point = view.PointOn(link);
        view.Window.MouseMove(point);
        view.Window.MouseDown(point, MouseButton.Left);
        block.IsVisible = false;
        Assert.That(link.IsPointerOver, Is.False);
        Assert.That(link.IsPressed, Is.False);
        block.IsVisible = true;
        var count = 0;
        block.LinkClick += (_, _) => count++;
        view.Window.UpdateLayout();
        view.Window.MouseUp(point, MouseButton.Left);
        Assert.That(count, Is.Zero);
    });

    [Test]
    public Task LegacyCommandsAndContextMenu_KeepTheirExistingContracts() => HeadlessSession.Current.Dispatch(async () =>
    {
        var link = CreateLink("copy this text");
        var block = new MarkdownTextBlock { FontSize = 20, Inlines = new InlineCollection { link } };
        using var view = new TestView(block, true);
        var command = new RecordingCommand();
        var renderer = view.Window.Content as MarkdownRenderer ?? throw new AssertionException("Expected a renderer.");
        renderer.LinkCommand = command;
        var menu = new ContextMenu { Items = { new MenuItem { Header = "Copy", Command = link.CopyCommand } } };
        renderer.LinkContextMenu = menu;
        var point = view.PointOn(link);
        view.Window.MouseMove(point);
        view.Window.MouseDown(point, MouseButton.Left);
        view.Window.MouseUp(point, MouseButton.Left);
        Assert.That(command.Parameter, Is.TypeOf<LinkClickedEventArgs>());
        var clicked = command.Parameter as LinkClickedEventArgs ?? throw new AssertionException("Expected click arguments.");
        Assert.That(clicked.Link, Is.SameAs(link));
        Assert.That(new LinkClickedEventArgs(MarkdownTextBlock.LinkClickEvent, block, link.HRef).Link, Is.Null);
        view.Window.MouseDown(point, MouseButton.Right);
        Assert.That(link.IsPressed, Is.False);
        view.Window.MouseUp(point, MouseButton.Right);
        Assert.That(menu.IsOpen, Is.True);
        Assert.That(menu.DataContext, Is.SameAs(link));
        menu.Close();
        Assert.That(link.OpenCommand.CanExecute(null), Is.True);
        link.CopyTextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var clipboard = view.Window.Clipboard ?? throw new AssertionException("Clipboard is unavailable.");
        Assert.That(await clipboard.TryGetTextAsync(), Is.EqualTo("copy this text"));
    }, CancellationToken.None);

    [Test]
    public Task MovingBetweenLinks_UsesNativeEventsAndPropertyOldNewValues() => Run(() =>
    {
        var first = CreateLink("first");
        var second = CreateLink("second");
        var block = new MarkdownTextBlock { Inlines = new InlineCollection { first, new Run(" "), second } };
        using var view = new TestView(block, true);
        var changes = new List<(Link? OldValue, Link? NewValue)>();
        block.PropertyChanged += (_, e) =>
        {
            if (e.Property == MarkdownTextBlock.PointerOverLinkProperty)
            {
                changes.Add((e.GetOldValue<Link?>(), e.GetNewValue<Link?>()));
            }
        };
        var eventCount = 0;
        (view.Window.Content as MarkdownRenderer ?? throw new AssertionException("Expected a renderer.")).LinkPointerOverChanged += (_, e) =>
        {
            Assert.That(e.GetType(), Is.EqualTo(typeof(RoutedEventArgs)));
            Assert.That(e.Source, Is.SameAs(block));
            eventCount++;
        };
        view.Window.MouseMove(view.PointOn(first));
        view.Window.MouseMove(view.PointOn(second));
        Assert.That(changes, Is.EqualTo(new (Link?, Link?)[] { (null, first), (first, second) }));
        Assert.That(eventCount, Is.EqualTo(2));
        Assert.That(first.IsPointerOver, Is.False);
        Assert.That(second.IsPointerOver, Is.True);
    });

    private static InlineGeometry GeometryOf(MarkdownTextBlock block, Inline inline)
    {
        return block.GetInlineGeometry(inline) ?? throw new AssertionException("Expected measured inline geometry.");
    }

    private static Task Run(Action action) => HeadlessSession.Current.Dispatch(action, CancellationToken.None);

    private static Link CreateLink(string text) => new() { HRef = new Uri("https://example.com"), Inlines = new InlineCollection { new Run(text) } };

    private sealed class TestView : IDisposable
    {
        public Window Window { get; }
        private readonly MarkdownTextBlock _block;

        public TestView(MarkdownTextBlock block, bool hasRenderer, double width = 400)
        {
            _block = block;
            Window = new Window { Width = width, Height = 300, Content = hasRenderer ? new TestRenderer(block) : block };
            Window.Show();
        }

        public Point PointOn(Link link)
        {
            return _block.TranslatePoint(GeometryOf(_block, link).Rects[0].Center, Window)
                ?? throw new AssertionException("The block is not in the test window.");
        }

        public void Dispose() => Window.Close();
    }

    private sealed class TestRenderer : MarkdownRenderer
    {
        public TestRenderer(MarkdownTextBlock block) => ((Panel)VisualChildren[0]).Children.Add(block);

        protected override Type StyleKeyOverride => typeof(MarkdownRenderer);
    }

    private sealed class RecordingCommand : ICommand
    {
        public object? Parameter { get; private set; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Parameter = parameter;
    }
}