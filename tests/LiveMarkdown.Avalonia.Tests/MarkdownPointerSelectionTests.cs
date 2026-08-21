using System.Runtime.InteropServices;
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
using Avalonia.VisualTree;
using Markdig;
using Markdig.Helpers;
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
        session = HeadlessSession.Current;
    }

    [OneTimeTearDown]
    public void StopSession()
    {
        // Deliberately NOT disposed. The session is shared for the whole assembly (see HeadlessSession),
        // and this fixture is not the last one to use it — tearing it down here kills the application
        // that PointerInteractionDiagnosticsTests is about to dispatch onto.
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
        const string codeText = "abc xyz אבג";
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
                    var sourceSlices = block.TextLayout.TextLines
                        .SelectMany(static line => line.TextRuns)
                        .OfType<ShapedTextRun>()
                        .Select(run => GetSourceSlice(run.Text, codeText))
                        .OrderBy(static slice => slice.Start)
                        .ToArray();

                    code.Padding = new Thickness(0);
                    code.Margin = new Thickness(0);
                    block.Measure(new Size(400, 120));
                    block.Arrange(new Rect(0, 0, 400, 120));

                    return (
                        widthWithSpacing,
                        widthWithoutSpacing: block.TextLayout.WidthIncludingTrailingWhitespace,
                        shapedLength,
                        sourceSlices);
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
                Assert.That(result.sourceSlices, Is.Not.Empty);
                Assert.That(result.sourceSlices.All(static slice => slice.MatchesSource), Is.True);
                Assert.That(result.sourceSlices[0].Start, Is.Zero);
                Assert.That(result.sourceSlices[^1].End, Is.EqualTo(codeText.Length));
                Assert.That(
                    result.sourceSlices.Zip(result.sourceSlices.Skip(1))
                        .All(static pair => pair.First.End == pair.Second.Start),
                    Is.True);
            });
    }

    [Test]
    public async Task CodeInline_SupplementaryCharactersFollowedByText_FallsBackWithoutCrashing()
    {
        var result = await session.Dispatch(
            () =>
            {
                var block = new MarkdownTextBlock();
                var code = new CodeInline
                {
                    Text = "😀😀",
                    Padding = new Thickness(2, 0),
                    Margin = new Thickness(12, 0),
                };
                block.Inlines!.Add(code);
                block.Inlines.Add(new Run("x"));

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
                    var widthWithRequestedSpacing = block.TextLayout.WidthIncludingTrailingWhitespace;
                    var lineCount = block.TextLayout.TextLines.Count;

                    code.Padding = new Thickness(0);
                    code.Margin = new Thickness(0);
                    block.Measure(new Size(300, 120));
                    block.Arrange(new Rect(0, 0, 300, 120));

                    return (
                        lineCount,
                        widthWithRequestedSpacing,
                        widthWithoutSpacing: block.TextLayout.WidthIncludingTrailingWhitespace);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(result.lineCount, Is.EqualTo(1));
        Assert.That(result.widthWithRequestedSpacing, Is.EqualTo(result.widthWithoutSpacing).Within(0.01));
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
                var selectionForeground = new SolidColorBrush(Colors.White, 0.5);
                var block = new MarkdownTextBlock
                {
                    SelectionBrush = Brushes.CornflowerBlue,
                    SelectionForegroundBrush = selectionForeground,
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

                    var boldRunProperties = block.TextLayout.TextLines
                        .SelectMany(static line => line.TextRuns)
                        .OfType<ShapedTextRun>()
                        .Where(static run => run.Text.Span.IndexOf("Bold", StringComparison.Ordinal) >= 0)
                        .Select(static run => run.Properties)
                        .ToArray();

                    return (
                        before,
                        after: CaptureLayout(block),
                        boldRunProperties,
                        selectionForeground);
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
                Assert.That(result.after.Width, Is.EqualTo(result.before.Width).Within(1e-9));
                Assert.That(result.after.Height, Is.EqualTo(result.before.Height).Within(1e-9));
                Assert.That(result.after.Lines, Is.EqualTo(result.before.Lines));
                Assert.That(result.boldRunProperties, Is.Not.Empty);
                Assert.That(result.boldRunProperties, Has.All.Property(nameof(TextRunProperties.FontRenderingEmSize)).EqualTo(26));
                Assert.That(result.boldRunProperties.All(properties => properties.Typeface.Weight == FontWeight.Bold), Is.True);
                Assert.That(
                    result.boldRunProperties.All(properties =>
                        ReferenceEquals(properties.TextDecorations, TextDecorations.Underline)),
                    Is.True);
                Assert.That(
                    result.boldRunProperties.All(properties =>
                        ReferenceEquals(properties.ForegroundBrush, result.selectionForeground)),
                    Is.True);
                Assert.That(result.boldRunProperties.All(properties => properties.BackgroundBrush is null), Is.True);
            });
    }

    [Test]
    public async Task MarkdownTextProjector_BuiltInDocumentMatchesRenderedBlockPartitionAndText()
    {
        const string markdown =
            "# Heading\n\nParagraph with **bold** and `code`.\n\n- [x] task\n- second\n\n| A | B |\n| - | - |\n| one | two |\n\n```text\ncode line\n```";
        var renderedBuffers = await session.Dispatch(
            () =>
            {
                var renderer = new MarkdownRenderer();
                var documentNode = new DocumentNode(renderer);
                var window = new Window
                {
                    Width = 800,
                    Height = 600,
                    Content = documentNode.Control,
                };
                try
                {
                    window.Show();
                    var document = Markdown.Parse(markdown, MarkdownRenderer.CreatePipeline());
                    documentNode.Update(
                        documentNode,
                        document,
                        new ObservableStringBuilderChangedEventArgs(0, markdown.Length, markdown.Length, 1),
                        CancellationToken.None);
                    documentNode.Control.Measure(new Size(800, 600));
                    documentNode.Control.Arrange(new Rect(0, 0, 800, 600));
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                    return documentNode.Control
                        .GetSelfAndVisualDescendants()
                        .OfType<MarkdownTextBlock>()
                        .Where(block => block.IsVisible)
                        .Select(block => new MarkdownTextBuffer(
                            block.SourceSpan,
                            new StringSlice(block.LayoutText)))
                        .ToArray();
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        var projectedBuffers = new MarkdownTextProjector()
            .Project(new ObservableStringBuilderSnapshot(markdown, 1))
            .Buffers;

        Assert.That(projectedBuffers, Is.EqualTo(renderedBuffers));
    }

    [Test]
    public async Task NamedHighlightForeground_RendersWithSelectionOverride()
    {
        var result = await session.Dispatch(
            () =>
            {
                var highlightForeground = new SolidColorBrush(Colors.DarkRed);
                var selectionForeground = new SolidColorBrush(Colors.White, 0.5);
                var block = new MarkdownTextBlock
                {
                    Text = "prefix highlighted suffix",
                    SelectionBrush = Brushes.CornflowerBlue,
                    SelectionForegroundBrush = selectionForeground,
                    HighlightStyles = new TextHighlightStyles(),
                };
                block.HighlightStyles.Set(
                    "match",
                    new TextHighlightStyle
                    {
                        Foreground = highlightForeground,
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
                    var runs = block.TextLayout.TextLines
                        .SelectMany(static line => line.TextRuns)
                        .OfType<ShapedTextRun>()
                        .Select(static run => (Text: run.Text.ToString(), run.Properties.ForegroundBrush))
                        .ToArray();
                    return (
                        layout: CaptureLayout(block),
                        runs,
                        highlightForeground,
                        selectionForeground);
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
                Assert.That(result.layout.Width, Is.GreaterThan(0));
                Assert.That(result.layout.Height, Is.GreaterThan(0));
                Assert.That(
                    result.runs.Any(run =>
                        run.Text == "hig" &&
                        ReferenceEquals(run.ForegroundBrush, result.highlightForeground)),
                    Is.True);
                Assert.That(
                    result.runs.Any(run =>
                        run.Text == "hlighted" &&
                        ReferenceEquals(run.ForegroundBrush, result.selectionForeground)),
                    Is.True);
            });
    }

    [Test]
    public async Task OverlappingHighlightForeground_UsesPriorityAndPreservesOuterRanges()
    {
        var result = await session.Dispatch(
            () =>
            {
                var lowPriority = new SolidColorBrush(Colors.DarkRed);
                var highPriority = new SolidColorBrush(Colors.DarkBlue);
                var styles = new TextHighlightStyles();
                styles.Set("low", new TextHighlightStyle { Foreground = lowPriority });
                styles.Set("high", new TextHighlightStyle { Foreground = highPriority });
                var block = new MarkdownTextBlock
                {
                    Text = "abcdef",
                    HighlightStyles = styles,
                };
                block.Highlights.Set("low", [new TextHighlightRange(0, 6)], priority: 0);
                block.Highlights.Set("high", [new TextHighlightRange(2, 2)], priority: 1);

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
                    var runs = block.TextLayout.TextLines
                        .SelectMany(static line => line.TextRuns)
                        .OfType<ShapedTextRun>()
                        .Select(static run => (Text: run.Text.ToString(), run.Properties.ForegroundBrush))
                        .ToArray();
                    return (runs, lowPriority, highPriority);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(
            result.runs,
            Is.EqualTo(new[]
            {
                ("ab", (IBrush?)result.lowPriority),
                ("cd", (IBrush?)result.highPriority),
                ("ef", (IBrush?)result.lowPriority),
            }));
    }

    [Test]
    public async Task HighlightStyleChange_InvalidatesLayoutOnlyWhenForegroundIsInvolved()
    {
        var result = await session.Dispatch(
            () =>
            {
                var styles = new TextHighlightStyles();
                var block = new MarkdownTextBlock
                {
                    Text = "highlighted",
                    HighlightStyles = styles,
                };
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
                    var initialLayout = block.TextLayout;

                    styles.Set("match", new TextHighlightStyle { Background = Brushes.LightYellow });
                    block.Highlights.Set("match", [new TextHighlightRange(0, 4)]);
                    var backgroundLayout = block.TextLayout;

                    styles.Set(
                        "match",
                        new TextHighlightStyle
                        {
                            Background = Brushes.LightYellow,
                            Foreground = Brushes.DarkRed,
                        });
                    var foregroundLayout = block.TextLayout;

                    return (
                        backgroundKeptLayout: ReferenceEquals(initialLayout, backgroundLayout),
                        foregroundRebuiltLayout: !ReferenceEquals(backgroundLayout, foregroundLayout));
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(result.backgroundKeptLayout, Is.True);
        Assert.That(result.foregroundRebuiltLayout, Is.True);
    }

    [Test]
    public async Task CodeInline_ForegroundOverridesPreserveShapedTextAndMetrics()
    {
        var result = await session.Dispatch(
            () =>
            {
                var highlightForeground = new SolidColorBrush(Colors.DarkRed);
                var selectionForeground = new SolidColorBrush(Colors.White, 0.5);
                var block = new MarkdownTextBlock
                {
                    SelectionForegroundBrush = selectionForeground,
                    HighlightStyles = new TextHighlightStyles(),
                };
                block.Inlines!.Add(new CodeInline
                {
                    Text = "code",
                    FontSize = 22,
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
                    var widthBefore = block.TextLayout.WidthIncludingTrailingWhitespace;

                    block.HighlightStyles.Set(
                        "match",
                        new TextHighlightStyle { Foreground = highlightForeground });
                    block.Highlights.Set("match", [new TextHighlightRange(0, 4)]);
                    block.SelectionStart = 1;
                    block.SelectionEnd = 3;
                    block.Measure(new Size(300, 120));
                    block.Arrange(new Rect(0, 0, 300, 120));

                    var runs = block.TextLayout.TextLines
                        .SelectMany(static line => line.TextRuns)
                        .OfType<ShapedTextRun>()
                        .Where(static run => run.Length > 0)
                        .ToArray();
                    return (
                        widthBefore,
                        widthAfter: block.TextLayout.WidthIncludingTrailingWhitespace,
                        text: string.Concat(runs.Select(static run => run.Text.ToString())),
                        properties: runs.Select(static run => run.Properties).ToArray(),
                        brushes: runs.Select(static run => run.Properties.ForegroundBrush).ToArray(),
                        highlightForeground,
                        selectionForeground);
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
                Assert.That(result.widthAfter, Is.EqualTo(result.widthBefore).Within(1e-9));
                Assert.That(result.text, Is.EqualTo("code"));
                Assert.That(result.properties, Has.All.Property(nameof(TextRunProperties.FontRenderingEmSize)).EqualTo(22));
                Assert.That(result.properties.All(properties => properties.BackgroundBrush is null), Is.True);
                Assert.That(result.brushes.Count(brush => ReferenceEquals(brush, result.highlightForeground)), Is.EqualTo(2));
                Assert.That(result.brushes.Count(brush => ReferenceEquals(brush, result.selectionForeground)), Is.EqualTo(1));
            });
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

    private static SourceSlice GetSourceSlice(ReadOnlyMemory<char> text, string expectedSource) =>
        MemoryMarshal.TryGetString(text, out var source, out var start, out var length)
            ? new SourceSlice(start, length, string.Equals(source, expectedSource, StringComparison.Ordinal))
            : new SourceSlice(-1, text.Length, false);

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

    private readonly record struct SourceSlice(int Start, int Length, bool MatchesSource)
    {
        public int End => Start + Length;
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
