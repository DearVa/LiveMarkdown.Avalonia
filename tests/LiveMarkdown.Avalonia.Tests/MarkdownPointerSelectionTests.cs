using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
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

    [Test]
    public async Task TextRangeBounds_UseVisualLineCoordinates()
    {
        var result = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock();
                var inlines = block.Inlines!;
                inlines.Add(new Run("first"));
                inlines.Add(new LineBreak());
                inlines.Add(new CodeInline
                {
                    Text = "second",
                    Background = Brushes.Gray,
                });

                var window = new Window
                {
                    Width = 300,
                    Height = 120,
                    Content = block,
                };

                try
                {
                    block.Measure(new Size(300, 120));
                    block.Arrange(new Rect(0, 0, 300, 120));
                    window.Show();
                    var start = block.ActualText.IndexOf("second", StringComparison.Ordinal);
                    return block.GetTextRangeBounds(start, "second".Length).ToArray();
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(result, Is.Not.Empty);
        Assert.That(result[0].Y, Is.GreaterThan(0));
    }

    [Test]
    public async Task CodeInline_MarginReservesOuterSpaceAndMovesLeadingGlyph()
    {
        var result = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock();
                block.Inlines!.Add(new Run("prefix"));
                var code = new CodeInline
                {
                    Text = "code",
                    Padding = new Thickness(2, 0),
                    Margin = new Thickness(12, 0),
                };
                block.Inlines.Add(code);
                block.Inlines.Add(new Run("suffix"));

                var window = new Window
                {
                    Width = 300,
                    Height = 120,
                    Content = block,
                };

                try
                {
                    window.Show();
                    block.Measure(new Size(300, 120));
                    block.Arrange(new Rect(0, 0, 300, 120));

                    var codeRun = block.TextLayout.TextLines
                        .SelectMany(static line => line.TextRuns)
                        .OfType<ShapedTextRun>()
                        .Single(static run => run.Text.ToString() == "code");
                    var widthWithSpacing = block.TextLayout.WidthIncludingTrailingWhitespace;
                    var leadingGlyphOffset = codeRun.GlyphRun.GlyphInfos[0].GlyphOffset.X;

                    code.Padding = new Thickness(0);
                    code.Margin = new Thickness(0);
                    block.Measure(new Size(300, 120));
                    block.Arrange(new Rect(0, 0, 300, 120));

                    return (
                        widthWithSpacing,
                        widthWithoutSpacing: block.TextLayout.WidthIncludingTrailingWhitespace,
                        leadingGlyphOffset);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(result.widthWithSpacing - result.widthWithoutSpacing, Is.EqualTo(28).Within(0.01));
        Assert.That(result.leadingGlyphOffset, Is.EqualTo(14).Within(0.01));
    }

    [Test]
    public async Task CodeInline_StyledMarginIsAppliedBeforeFirstLayout()
    {
        var result = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock();
                var code = new CodeInline
                {
                    Text = "code",
                };
                block.Inlines!.Add(code);

                var style = new Style(static selector => selector.OfType<CodeInline>());
                style.Setters.Add(new Setter(CodeInline.PaddingProperty, new Thickness(2, 0)));
                style.Setters.Add(new Setter(CodeInline.MarginProperty, new Thickness(12, 0)));
                block.Styles.Add(style);

                var window = new Window
                {
                    Width = 300,
                    Height = 120,
                    Content = block,
                };

                try
                {
                    window.Show();
                    block.Measure(new Size(300, 120));
                    block.Arrange(new Rect(0, 0, 300, 120));

                    var codeRun = block.TextLayout.TextLines
                        .SelectMany(static line => line.TextRuns)
                        .OfType<ShapedTextRun>()
                        .Single(static run => run.Text.ToString() == "code");

                    return (
                        margin: code.Margin,
                        padding: code.Padding,
                        width: block.TextLayout.WidthIncludingTrailingWhitespace,
                        leadingGlyphOffset: codeRun.GlyphRun.GlyphInfos[0].GlyphOffset.X);
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
                Assert.That(result.margin, Is.EqualTo(new Thickness(12, 0)));
                Assert.That(result.padding, Is.EqualTo(new Thickness(2, 0)));
                Assert.That(result.width, Is.GreaterThan(28));
                Assert.That(result.leadingGlyphOffset, Is.EqualTo(14).Within(0.01));
            });
    }

    [Test]
    public async Task CodeInline_RtlMarginReservesPhysicalOuterSpace()
    {
        var result = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock
                {
                    FlowDirection = FlowDirection.RightToLeft,
                };
                block.Inlines!.Add(new CodeInline
                {
                    Text = "אבג",
                    Padding = new Thickness(2, 0),
                    Margin = new Thickness(12, 0),
                });

                var window = new Window
                {
                    Width = 300,
                    Height = 120,
                    Content = block,
                };

                try
                {
                    window.Show();
                    block.Measure(new Size(300, 120));
                    block.Arrange(new Rect(0, 0, 300, 120));

                    var codeRun = block.TextLayout.TextLines
                        .SelectMany(static line => line.TextRuns)
                        .OfType<ShapedTextRun>()
                        .Single(static run => run.Text.ToString() == "אבג");
                    var widthWithSpacing = block.TextLayout.WidthIncludingTrailingWhitespace;
                    var leadingGlyphOffset = codeRun.GlyphRun.GlyphInfos
                        .Single(static glyph => glyph.GlyphCluster == 2)
                        .GlyphOffset.X;

                    return (widthWithSpacing, leadingGlyphOffset);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(result.widthWithSpacing, Is.GreaterThan(28));
        Assert.That(result.leadingGlyphOffset, Is.EqualTo(14).Within(0.01));
    }

    [Test]
    public async Task CodeInline_MixedBidiKeepsLogicalCoverageAndPhysicalSpacing()
    {
        const string codeText = "abc 👩‍💻 אבג";
        var result = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock();
                var code = new CodeInline
                {
                    Text = codeText,
                    Padding = new Thickness(2, 0),
                    Margin = new Thickness(12, 0),
                };
                block.Inlines!.Add(code);

                var window = new Window
                {
                    Width = 400,
                    Height = 120,
                    Content = block,
                };

                try
                {
                    window.Show();
                    block.Measure(new Size(400, 120));
                    block.Arrange(new Rect(0, 0, 400, 120));
                    var widthWithSpacing = block.TextLayout.WidthIncludingTrailingWhitespace;
                    var shapedLength = block.TextLayout.TextLines
                        .SelectMany(static line => line.TextRuns)
                        .OfType<ShapedTextRun>()
                        .Sum(static run => run.Length);

                    code.Padding = new Thickness(0);
                    code.Margin = new Thickness(0);
                    block.Measure(new Size(400, 120));
                    block.Arrange(new Rect(0, 0, 400, 120));

                    return (
                        widthWithSpacing,
                        widthWithoutSpacing: block.TextLayout.WidthIncludingTrailingWhitespace,
                        shapedLength);
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
                Assert.That(result.shapedLength, Is.EqualTo(codeText.Length));
                Assert.That(result.widthWithSpacing - result.widthWithoutSpacing, Is.EqualTo(28).Within(0.01));
            });
    }

    [Test]
    public async Task CodeInline_BackgroundUsesLayoutMarginWithoutCompressingContent()
    {
        var result = await session.Dispatch(
            () =>
            {
                var background = new SolidColorBrush(Colors.Magenta);
                var code = new CodeInline
                {
                    Text = "code",
                    Background = background,
                    Padding = new Thickness(2, 0),
                    Margin = new Thickness(12, 0),
                };
                var block = new MarkdownTextBlock();
                block.Inlines!.Add(code);

                var window = new Window
                {
                    Width = 300,
                    Height = 120,
                    Content = block,
                };

                try
                {
                    window.Show();
                    block.Measure(new Size(300, 120));
                    block.Arrange(new Rect(0, 0, 300, 120));

                    var rangeBounds = block.GetTextRangeBounds(0, code.Text!.Length).Single();
                    var drawingGroup = new DrawingGroup();
                    using (var drawingContext = drawingGroup.Open())
                    {
                        block.Render(drawingContext);
                    }

                    var backgroundBounds = EnumerateGeometryDrawings(drawingGroup)
                        .Single(drawing => ReferenceEquals(drawing.Brush, background))
                        .Geometry!
                        .Bounds;
                    return (rangeBounds, backgroundBounds);
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
                Assert.That(result.backgroundBounds.Left, Is.EqualTo(result.rangeBounds.Left + 12).Within(0.01));
                Assert.That(result.backgroundBounds.Right, Is.EqualTo(result.rangeBounds.Right - 12).Within(0.01));
            });
    }

    [Test]
    public async Task Selection_DoesNotChangeMixedRunLayout()
    {
        var result = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock
                {
                    SelectionBrush = Brushes.CornflowerBlue,
                    SelectionForegroundBrush = new SolidColorBrush(Colors.White, 0.5),
                };
                block.Inlines!.Add(new Run("prefix "));
                block.Inlines.Add(new Run("Bold and large")
                {
                    FontSize = 26,
                    FontWeight = FontWeight.Bold,
                    TextDecorations = TextDecorations.Underline,
                });
                block.Inlines.Add(new Run(" 👩‍💻 suffix"));

                var window = new Window
                {
                    Width = 500,
                    Height = 160,
                    Content = block,
                };

                try
                {
                    window.Show();
                    block.Measure(new Size(500, 160));
                    block.Arrange(new Rect(0, 0, 500, 160));
                    var before = CaptureLayout(block);

                    block.SelectionStart = 2;
                    block.SelectionEnd = block.EscapedTextLength - 2;
                    block.Measure(new Size(500, 160));
                    block.Arrange(new Rect(0, 0, 500, 160));
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                    return (before, CaptureLayout(block));
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(result.Item2.Width, Is.EqualTo(result.Item1.Width).Within(1e-9));
        Assert.That(result.Item2.Height, Is.EqualTo(result.Item1.Height).Within(1e-9));
        Assert.That(result.Item2.Lines, Is.EqualTo(result.Item1.Lines));
    }

    [Test]
    public async Task NamedHighlightForeground_RendersWithSelectionOverride()
    {
        var layout = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock
                {
                    Text = "prefix highlighted suffix",
                    SelectionBrush = Brushes.CornflowerBlue,
                    SelectionForegroundBrush = new SolidColorBrush(Colors.White, 0.5),
                    HighlightStyles = new TextHighlightStyles(),
                };
                block.HighlightStyles.Set(
                    "match",
                    new TextHighlightStyle
                    {
                        Foreground = Brushes.DarkRed,
                        Background = Brushes.LightYellow,
                    });
                block.Highlights.Set("match", [new TextHighlightRange(7, 11)], priority: 1);
                block.SelectionStart = 10;
                block.SelectionEnd = 18;

                var window = new Window
                {
                    Width = 400,
                    Height = 120,
                    Content = block,
                };

                try
                {
                    window.Show();
                    block.Measure(new Size(400, 120));
                    block.Arrange(new Rect(0, 0, 400, 120));
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    return CaptureLayout(block);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(layout.Width, Is.GreaterThan(0));
        Assert.That(layout.Height, Is.GreaterThan(0));
    }

    [Test]
    public async Task TextBackgrounds_AreRemovedFromNativeRunsForUnifiedPainting()
    {
        var hasNativeBackground = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock
                {
                    SelectionBrush = Brushes.CornflowerBlue,
                };
                block.Inlines!.Add(new Run("normal")
                {
                    Background = Brushes.LightYellow,
                });
                block.Inlines.Add(new CodeInline
                {
                    Text = "code",
                    Background = Brushes.LightGray,
                });

                var window = new Window
                {
                    Width = 300,
                    Height = 120,
                    Content = block,
                };

                try
                {
                    window.Show();
                    block.Measure(new Size(300, 120));
                    block.Arrange(new Rect(0, 0, 300, 120));
                    block.SelectionStart = 1;
                    block.SelectionEnd = 7;
                    block.Measure(new Size(300, 120));
                    block.Arrange(new Rect(0, 0, 300, 120));

                    return block.TextLayout.TextLines
                        .SelectMany(static line => line.TextRuns)
                        .Any(static run => run.Properties?.BackgroundBrush is not null);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(hasNativeBackground, Is.False);
    }

    private static LayoutSnapshot CaptureLayout(MarkdownTextBlock block) =>
        new(
            block.TextLayout.Width,
            block.TextLayout.Height,
            block.TextLayout.TextLines
                .Select(line => new LineSnapshot(
                    line.FirstTextSourceIndex,
                    line.Length,
                    line.Width,
                    line.Height))
            .ToArray());

    private static IEnumerable<GeometryDrawing> EnumerateGeometryDrawings(Drawing drawing)
    {
        if (drawing is GeometryDrawing geometryDrawing)
        {
            yield return geometryDrawing;
        }

        if (drawing is not DrawingGroup group)
        {
            yield break;
        }

        foreach (var child in group.Children)
        {
            foreach (var geometry in EnumerateGeometryDrawings(child))
            {
                yield return geometry;
            }
        }
    }

    private readonly record struct LayoutSnapshot(double Width, double Height, IReadOnlyList<LineSnapshot> Lines);

    private readonly record struct LineSnapshot(int FirstTextSourceIndex, int Length, double Width, double Height);

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
