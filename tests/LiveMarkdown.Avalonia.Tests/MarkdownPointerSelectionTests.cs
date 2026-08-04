using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class MarkdownPointerSelectionTests
{
    private HeadlessUnitTestSession session = null!;

    [OneTimeSetUp]
    public void StartSession()
    {
        session = HeadlessUnitTestSession.StartNew(typeof(StyledTestApplication));
    }

    [OneTimeTearDown]
    public void StopSession()
    {
        session.Dispose();
    }

    [TestCase(2, "doubleclick")]
    [TestCase(3, "whole sentence")]
    public async Task MultiClickWithoutDrag_PreservesClickSelection(int clickCount, string text)
    {
        var selectedText = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock
                {
                    Text = text,
                    FontSize = 20,
                    Foreground = Brushes.Black,
                };
                var renderer = new TestMarkdownRenderer(block);
                var window = new Window
                {
                    Width = 400,
                    Height = 120,
                    Content = renderer,
                };

                try
                {
                    window.Show();

                    var clickPoint = new Point(30, 15);
                    window.MouseMove(clickPoint);
                    for (var i = 0; i < clickCount; i++)
                    {
                        window.MouseDown(clickPoint, MouseButton.Left);
                        window.MouseUp(clickPoint, MouseButton.Left);
                    }

                    return block.ActualSelectedText;
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(selectedText, Is.EqualTo(text));
    }

    [Test]
    public async Task DragBeyondTapDistance_UpdatesSelection()
    {
        var selectedText = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock
                {
                    Text = "drag selection",
                    FontSize = 20,
                    Foreground = Brushes.Black,
                };
                var renderer = new TestMarkdownRenderer(block);
                var window = new Window
                {
                    Width = 400,
                    Height = 120,
                    Content = renderer,
                };

                try
                {
                    window.Show();

                    var start = new Point(5, 15);
                    var end = new Point(80, 15);
                    window.MouseMove(start);
                    window.MouseDown(start, MouseButton.Left);
                    window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                    window.MouseUp(end, MouseButton.Left);

                    return block.ActualSelectedText;
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(selectedText, Is.Not.Empty);
    }

    [Test]
    public async Task DragAcrossTextBlocks_UsesPointerPositionInsteadOfCaptureSource()
    {
        var selectedText = await session.Dispatch(
            () =>
            {
                var first = new MarkdownTextBlock
                {
                    Text = "first block",
                    FontSize = 20,
                    Foreground = Brushes.Black,
                };
                var second = new MarkdownTextBlock
                {
                    Text = "second block",
                    FontSize = 20,
                    Foreground = Brushes.Black,
                };
                var renderer = new TestMarkdownRenderer(first, second);
                var window = new Window
                {
                    Width = 400,
                    Height = 160,
                    Content = renderer,
                };

                try
                {
                    window.Show();

                    var start = first.TranslatePoint(new Point(5, first.Bounds.Height / 2), window) ?? new Point(5, 15);
                    var end = second.TranslatePoint(new Point(second.Bounds.Width + 10, second.Bounds.Height / 2), window) ?? new Point(180, 55);
                    window.MouseMove(start);
                    window.MouseDown(start, MouseButton.Left);
                    window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                    window.MouseUp(end, MouseButton.Left);

                    return renderer.SelectedText;
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(selectedText, Does.Contain("first block"));
        Assert.That(selectedText, Does.Contain("second block"));
    }

    [Test]
    public async Task SelectingCursorCanBeCustomizedOnCapturedTextBlock()
    {
        var cursor = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock
                {
                    Text = "custom cursor selection",
                    FontSize = 20,
                    Foreground = Brushes.Black,
                };
                var renderer = new TestMarkdownRenderer(block);
                var selectingStyle = new Style(
                    selector => selector
                        .OfType<MarkdownRenderer>()
                        .Class(":selecting")
                        .Descendant()
                        .OfType<MarkdownTextBlock>());
                selectingStyle.Setters.Add(
                    new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Cross)));
                renderer.Styles.Add(selectingStyle);

                IPointer? pointer = null;
                renderer.AddHandler(
                    InputElement.PointerPressedEvent,
                    (_, e) => pointer = e.Pointer,
                    RoutingStrategies.Bubble,
                    handledEventsToo: true);

                var window = new Window
                {
                    Width = 400,
                    Height = 120,
                    Content = renderer,
                };

                try
                {
                    window.Show();

                    var start = new Point(5, 15);
                    var end = new Point(140, 15);
                    window.MouseMove(start);
                    window.MouseDown(start, MouseButton.Left);
                    window.MouseMove(end, RawInputModifiers.LeftMouseButton);

                    return pointer?.Captured?.Cursor?.ToString();
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(cursor, Is.EqualTo(nameof(StandardCursorType.Cross)));
    }

    [Test]
    public async Task DetachingDuringDrag_ClearsPointerInteractionState()
    {
        var result = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock
                {
                    Text = "detach during selection",
                    FontSize = 20,
                    Foreground = Brushes.Black,
                };
                var renderer = new TestMarkdownRenderer(block);
                var window = new Window
                {
                    Width = 400,
                    Height = 120,
                    Content = renderer,
                };

                try
                {
                    window.Show();

                    var start = new Point(5, 15);
                    var end = new Point(140, 15);
                    window.MouseMove(start);
                    window.MouseDown(start, MouseButton.Left);
                    window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                    var selectingBeforeDetach = renderer.Classes.Contains(":selecting");

                    window.Close();

                    return (
                        selectingBeforeDetach,
                        renderer.Classes.Contains(":selecting"),
                        renderer.Classes.Contains(":link-pending"));
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.Multiple(
            () =>
            {
                Assert.That(result.selectingBeforeDetach, Is.True);
                Assert.That(result.Item2, Is.False);
                Assert.That(result.Item3, Is.False);
            });
    }

    [Test]
    public async Task ClickSelection_KeepsIBeamForEntireImplicitPointerCapture()
    {
        var result = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock
                {
                    Text = "cursor",
                    FontSize = 20,
                    Foreground = Brushes.Black,
                };
                var renderer = new TestMarkdownRenderer(block);
                IPointer? pointer = null;
                var lostIBeamWhileCaptured = false;
                renderer.AddHandler(
                    InputElement.PointerPressedEvent,
                    (_, e) => pointer = e.Pointer,
                    RoutingStrategies.Bubble,
                    handledEventsToo: true);
                renderer.PropertyChanged += (_, e) =>
                {
                    if (e.Property == InputElement.CursorProperty &&
                        pointer?.Captured is { } captured &&
                        captured.Cursor?.ToString() != nameof(StandardCursorType.Ibeam))
                    {
                        lostIBeamWhileCaptured = true;
                    }
                };
                string? implicitCaptureCursor = null;
                var window = new Window
                {
                    Width = 400,
                    Height = 120,
                    Content = renderer,
                };
                window.AddHandler(
                    InputElement.PointerPressedEvent,
                    (_, e) => implicitCaptureCursor = e.Pointer.Captured?.Cursor?.ToString(),
                    RoutingStrategies.Tunnel,
                    handledEventsToo: true);

                try
                {
                    window.Show();

                    var point = new Point(30, 15);
                    window.MouseMove(point);
                    window.MouseDown(point, MouseButton.Left);
                    var cursorWhilePressed = pointer?.Captured?.Cursor?.ToString();
                    var captureWhilePressed = pointer?.Captured?.GetType().Name;
                    window.MouseUp(point, MouseButton.Left);

                    return (
                        implicitCaptureCursor,
                        cursorWhilePressed,
                        lostIBeamWhileCaptured,
                        captureWhilePressed);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.Multiple(
            () =>
            {
                Assert.That(result.implicitCaptureCursor, Is.EqualTo(nameof(StandardCursorType.Ibeam)));
                Assert.That(result.cursorWhilePressed, Is.EqualTo(nameof(StandardCursorType.Ibeam)));
                Assert.That(result.lostIBeamWhileCaptured, Is.False);
                Assert.That(result.Item4, Is.EqualTo(nameof(MarkdownTextBlock)));
            });
    }

    private sealed class TestMarkdownRenderer : MarkdownRenderer
    {
        public TestMarkdownRenderer(params MarkdownTextBlock[] blocks)
        {
            var panel = (Panel)VisualChildren[0];
            foreach (var block in blocks)
            {
                panel.Children.Add(block);
            }
        }

        protected override Type StyleKeyOverride => typeof(MarkdownRenderer);
    }

    public sealed class StyledTestApplication : Application
    {
        public override void Initialize()
        {
            Styles.Add(
                new StyleInclude(new Uri("avares://LiveMarkdown.Avalonia.Tests/"))
                {
                    Source = new Uri("avares://LiveMarkdown.Avalonia/Styles.axaml"),
                });
        }
    }
}
