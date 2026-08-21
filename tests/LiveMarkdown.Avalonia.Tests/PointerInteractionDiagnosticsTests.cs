using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using LiveMarkdown.Avalonia;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public sealed class PointerInteractionDiagnosticsTests
{
    private HeadlessUnitTestSession session = null!;

    [SetUp]
    public void SetUp()
    {
        session = HeadlessSession.Current;
    }

    [TearDown]
    public void TearDown()
    {
        // Deliberately NOT disposed: the session is shared for the whole assembly.
    }

    [Test]
    public async Task CaptureTimeline_FirstQuickClick_LongHold_AndSecondClick()
    {
        PointerInteractionTrace? trace = null;
        Window? window = null;

        await session.Dispatch(
            () =>
            {
                trace = new PointerInteractionTrace();
                var block = new MarkdownTextBlock
                {
                    Text = "diagnostic text for cursor tracing",
                    FontSize = 20,
                };
                var renderer = new TraceRenderer(trace, block);
                window = new Window
                {
                    Width = 640,
                    Height = 120,
                    Content = renderer,
                };

                trace.Attach(window, renderer, block);
                trace.InstallPlatformCursorTrace(window);
                window.Show();

                var point = new Point(25, 20);
                window.MouseMove(point);

                trace.Mark("first quick click");
                window!.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);

                trace.Mark("long hold");
                window!.MouseDown(point, MouseButton.Left);
            },
            CancellationToken.None);

        await Task.Delay(1000);

        var result = await session.Dispatch(
            () =>
            {
                var currentWindow = window!;
                var point = new Point(25, 20);
                currentWindow.MouseUp(point, MouseButton.Left);

                trace!.Mark("second quick click");
                currentWindow.MouseDown(point, MouseButton.Left);
                currentWindow.MouseUp(point, MouseButton.Left);

                trace.Mark("triple click");
                currentWindow.MouseDown(point, MouseButton.Left);
                currentWindow.MouseUp(point, MouseButton.Left);
                currentWindow.MouseDown(point, MouseButton.Left);
                currentWindow.MouseUp(point, MouseButton.Left);
                currentWindow.MouseDown(point, MouseButton.Left);
                currentWindow.MouseUp(point, MouseButton.Left);

                var output = trace.ToString();
                currentWindow.Close();
                return output;
            },
            CancellationToken.None);

        TestContext.Progress.WriteLine(result);
        Assert.That(result, Does.Contain("Window.Tunnel.Pressed"));
        Assert.That(result, Does.Contain("Renderer.Override.Pressed.Before"));
        Assert.That(result, Does.Contain("Window.Tunnel.Released"));
        Assert.That(result, Does.Contain("cursorElement="));
        Assert.That(result, Does.Contain("presentationCursor="));
    }

    [Test]
    public async Task CaptureTimeline_LinkClick_AndDragStartingOnLink()
    {
        var result = await session.Dispatch(
            () =>
            {
                var trace = new PointerInteractionTrace();
                var link = new Link
                {
                    HRef = new Uri("https://example.com/diagnostic"),
                };
                link.Inlines.Add(new Run("diagnostic link text"));

                var block = new MarkdownTextBlock
                {
                    FontSize = 20,
                };
                block.Inlines!.Add(link);

                var renderer = new TraceRenderer(trace, block);
                var linkClickCount = 0;
                block.LinkClick += (_, _) =>
                {
                    linkClickCount++;
                    trace.Mark("LinkClick raised");
                };

                var window = new Window
                {
                    Width = 640,
                    Height = 120,
                    Content = renderer,
                };

                trace.Attach(window, renderer, block);
                trace.InstallPlatformCursorTrace(window);
                window.Show();

                var linkPoint = new Point(20, 20);
                window.MouseMove(linkPoint);

                trace.Mark("link quick click");
                window.MouseDown(linkPoint, MouseButton.Left);
                window.MouseUp(linkPoint, MouseButton.Left);

                trace.Mark("drag starting on link");
                window.MouseDown(linkPoint, MouseButton.Left);
                var dragEnd = new Point(400, 20);
                window.MouseMove(dragEnd, RawInputModifiers.LeftMouseButton);
                window.MouseMove(new Point(100, 20), RawInputModifiers.LeftMouseButton);
                window.MouseMove(dragEnd, RawInputModifiers.LeftMouseButton);
                window.MouseUp(dragEnd, MouseButton.Left);

                var output = trace.ToString();
                var selectedText = block.ActualSelectedText;
                window.Close();
                return (linkClickCount, selectedText, output);
            },
            CancellationToken.None);

        TestContext.Progress.WriteLine(result.output);
        var dragStart = result.output.IndexOf("MARK drag starting on link", StringComparison.Ordinal);
        var selectingStart = result.output.IndexOf("renderer(selecting=True", dragStart, StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(result.linkClickCount, Is.EqualTo(1));
            Assert.That(result.selectedText, Is.Not.Empty);
            Assert.That(result.output, Does.Contain("block(link=True)"));
            Assert.That(result.output, Does.Contain("drag starting on link"));
            Assert.That(result.output, Does.Contain("captured=MarkdownTextBlock[Ibeam] captureSource=Implicit"));
            Assert.That(result.output, Does.Contain("renderer(selecting=True"));
            Assert.That(result.output, Does.Not.Contain("Platform.SetCursor cursor=null"));
            Assert.That(dragStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(selectingStart, Is.GreaterThan(dragStart));
            Assert.That(result.output[selectingStart..], Does.Not.Contain("Platform.SetCursor cursor=Hand"));
        });
    }

    private sealed class TraceRenderer : MarkdownRenderer
    {
        private readonly PointerInteractionTrace trace;

        public TraceRenderer(PointerInteractionTrace trace, MarkdownTextBlock block)
        {
            this.trace = trace;
            ((Panel)VisualChildren[0]).Children.Add(block);
        }

        protected override Type StyleKeyOverride => typeof(MarkdownRenderer);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            trace.Record("Renderer.Override.Pressed.Before", e);
            base.OnPointerPressed(e);
            trace.Record("Renderer.Override.Pressed.After", e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            trace.Record("Renderer.Override.Released.Before", e);
            base.OnPointerReleased(e);
            trace.Record("Renderer.Override.Released.After", e);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            trace.Record("Renderer.Override.Moved.Before", e);
            base.OnPointerMoved(e);
            trace.Record("Renderer.Override.Moved.After", e);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            trace.RecordCaptureLost("Renderer.Override.CaptureLost.Before", e);
            base.OnPointerCaptureLost(e);
            trace.RecordCaptureLost("Renderer.Override.CaptureLost.After", e);
        }
    }
}
