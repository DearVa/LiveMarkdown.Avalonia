using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Markdig;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class StreamingRevealRenderingTests
{
    [TestCase(FlowDirection.LeftToRight, TextAlignment.Center)]
    [TestCase(FlowDirection.RightToLeft, TextAlignment.Right)]
    public Task ActiveGlyphOriginsExactlyMatchStaticTextLineDrawing(FlowDirection direction, TextAlignment alignment) =>
        HeadlessSession.Current.Dispatch(() =>
        {
            var block = new MarkdownTextBlock
            {
                FlowDirection = direction, TextAlignment = alignment, FontSize = 18, Padding = new Thickness(7, 4),
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black,
                Inlines = new InlineCollection
                {
                    new Run("中文 mixed אבג "), new Run("larger ") { FontSize = 26 },
                    new CodeInline { Text = "inline", Background = Brushes.Gold },
                    new Run(" link and wrapped text") { TextDecorations = TextDecorations.Underline }
                }
            };
            var window = new Window { Width = 170, Height = 360, Content = block };
            try
            {
                window.Show();
                Assert.That(block.TextLayout.TextLines.Count, Is.GreaterThan(1));
                var origin = new Point(block.Padding.Left, block.Padding.Top);
                var staticDrawing = new DrawingGroup();
                using (var context = staticDrawing.Open()) block.Render(context);
                var native = GlyphOrigins(staticDrawing, Matrix.Identity).ToArray();
                Assert.That(native, Has.Length.GreaterThan(2));

                // Keep the default mode while recording the reference drawing. Creating the
                // candidate explicitly lets us compare the two paths for the exact same layout.
                using var snapshot = block.CreateRevealSnapshot(0, origin,
                    [new(0, block.LayoutText.Length, Stopwatch.GetTimestamp())],
                    MarkdownStreamingRevealMode.Character, TimeSpan.FromSeconds(1));
                Assert.That(snapshot, Is.Not.Null);
                var lines = snapshot!.Lines;
                Assert.That(lines.SelectMany(l => l.Glyphs).Select(g => g.Origin), Is.EqualTo(native));
                Assert.That(lines.SelectMany(l => l.Backgrounds), Is.Not.Empty);
                Assert.That(lines.SelectMany(l => l.Glyphs).SelectMany(g => g.Decorations), Is.Not.Empty);
            }
            finally { window.Close(); }
        }, CancellationToken.None);

    private static IEnumerable<Point> GlyphOrigins(Drawing drawing, Matrix transform)
    {
        if (drawing is GlyphRunDrawing) yield return transform.Transform(default);
        if (drawing is not DrawingGroup group) yield break;
        transform = (group.Transform?.Value ?? Matrix.Identity) * transform;
        foreach (var child in group.Children)
        foreach (var point in GlyphOrigins(child, transform))
            yield return point;
    }

    [Test]
    public Task NoneUsesTheStaticDrawingAndCreatesNoChildVisual() => HeadlessSession.Current.Dispatch(() =>
    {
        var block = new MarkdownTextBlock { Text = "settled", Foreground = Brushes.Black };
        var window = new Window { Content = block };
        try
        {
            window.Show();
            block.Text += " delta";
            block.ApplyRevealUpdate(("settled", 1));
            window.UpdateLayout();
            var actual = new DrawingGroup();
            var baseline = new DrawingGroup();
            using (var dc = actual.Open()) block.Render(dc);
            using (var dc = baseline.Open()) block.TextLayout.Draw(dc, default);
            var expected = GlyphOrigins(baseline, Matrix.Identity).ToArray();
            Assert.That(expected, Is.Not.Empty);
            Assert.That(GlyphOrigins(actual, Matrix.Identity), Is.EqualTo(expected));
            Assert.That(block.ActiveRevealSpans, Is.Empty);
            Assert.That(ElementComposition.GetElementChildVisual(block), Is.Null);
        }
        finally { window.Close(); }
    }, CancellationToken.None);

    [TestCase(MarkdownStreamingRevealMode.Character)]
    [TestCase(MarkdownStreamingRevealMode.Line)]
    public Task CompletionReturnsLinesToStaticDrawingAndDisposesSnapshot(MarkdownStreamingRevealMode mode) =>
        HeadlessSession.Current.Dispatch<int>(async () =>
        {
            var block = new MarkdownTextBlock
            {
                Text = "history", Foreground = Brushes.Black, StreamingRevealMode = mode,
                StreamingRevealDuration = TimeSpan.FromMilliseconds(240)
            };
            var window = new Window { Width = 300, Height = 160, Content = block };
            try
            {
                window.Show();
                Append(block, "\nnew line");
                window.UpdateLayout();
                var snapshot = block.ActiveRevealSnapshot;
                Assert.That(snapshot, Is.Not.Null);
                Assert.That(snapshot!.Lines, Has.Length.EqualTo(1));
                Assert.That(snapshot.Lines[0].Glyphs, Is.Not.Empty);
                var timeout = Stopwatch.StartNew();
                while (block.ActiveRevealSnapshot is not null && timeout.Elapsed < TimeSpan.FromSeconds(3))
                {
                    await Task.Delay(15);
                    Tick();
                }
                await Commit(ElementComposition.GetElementVisual(block)!.Compositor);
                Assert.That(block.ActiveRevealSnapshot, Is.Null);
                Assert.That(block.ActiveRevealSpans, Is.Empty);
                Assert.That(ElementComposition.GetElementChildVisual(block), Is.Null);
                Assert.That(snapshot.IsDisposed, Is.True);
                return 0;
            }
            finally { window.Close(); }
        }, CancellationToken.None);

    [TestCase("selection")]
    [TestCase("detach")]
    [TestCase("disable")]
    public Task ReflowReplacesGlyphsAndInterruptionClearsTheirReplacements(string interruption) =>
        HeadlessSession.Current.Dispatch<int>(async () =>
        {
            var block = new MarkdownTextBlock
            {
                Text = "prefix ", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black,
                StreamingRevealMode = MarkdownStreamingRevealMode.Character, StreamingRevealDuration = TimeSpan.FromMinutes(1)
            };
            var window = new Window { Width = 450, Height = 300, Content = block };
            try
            {
                window.Show();
                Append(block, "words that move when this streaming paragraph wraps onto a narrower line");
                window.UpdateLayout();
                var before = block.ActiveRevealSnapshot!;
                Assert.That(before, Is.Not.Null);
                var compositor = ElementComposition.GetElementVisual(block)!.Compositor;
                block.Width = 120;
                window.UpdateLayout();
                await Commit(compositor);
                var after = block.ActiveRevealSnapshot!;
                Assert.That(after.Lines.Length, Is.GreaterThan(before.Lines.Length));
                Assert.That(before.IsDisposed, Is.True);
                Assert.That(after.IsDisposed, Is.False);
                switch (interruption)
                {
                    case "selection": block.SelectionEnd = 1; break;
                    case "detach": window.Content = null; break;
                    case "disable": block.StreamingRevealMode = MarkdownStreamingRevealMode.None; break;
                }
                Assert.That(block.ActiveRevealSnapshot, Is.Null);
                Assert.That(ElementComposition.GetElementChildVisual(block), Is.Null);
                await Commit(compositor);
                Assert.That(after.IsDisposed, Is.True);
                return 0;
            }
            finally { window.Close(); }
        }, CancellationToken.None);

    [Test]
    public Task LineModeKeepsAnAlreadyVisibleLineSettled() => HeadlessSession.Current.Dispatch(() =>
    {
        var block = new MarkdownTextBlock { Text = "history", StreamingRevealMode = MarkdownStreamingRevealMode.Line };
        var window = new Window { Width = 360, Height = 120, Content = block };
        try
        {
            window.Show();
            Append(block, " on the same line");
            window.UpdateLayout();
            Assert.That(block.ActiveRevealSpans, Is.Empty);
            Assert.That(ElementComposition.GetElementChildVisual(block), Is.Null);
        }
        finally { window.Close(); }
    }, CancellationToken.None);

    [Test]
    public Task ForegroundChangeDuringActiveReveal_RefreshesSnapshotAndDisposesPrevious() =>
        HeadlessSession.Current.Dispatch<int>(async () =>
        {
            var block = new MarkdownTextBlock
            {
                Text = "prefix ", Foreground = Brushes.Black,
                StreamingRevealMode = MarkdownStreamingRevealMode.Character,
                StreamingRevealDuration = TimeSpan.FromMinutes(1)
            };
            var window = new Window { Width = 360, Height = 120, Content = block };
            try
            {
                window.Show();
                Append(block, "streaming text");
                window.UpdateLayout();
                var before = block.ActiveRevealSnapshot;
                Assert.That(before, Is.Not.Null);
                var compositor = ElementComposition.GetElementVisual(block)!.Compositor;
                await Commit(compositor);

                block.Foreground = Brushes.White;
                window.UpdateLayout();
                await Commit(compositor);

                var after = block.ActiveRevealSnapshot;
                Assert.That(after, Is.Not.Null);
                Assert.That(after, Is.Not.SameAs(before));
                Assert.That(before!.IsDisposed, Is.True);
                Assert.That(after!.IsDisposed, Is.False);
                var brush = after.Lines.SelectMany(l => l.Glyphs).Select(g => g.Foreground).First();
                Assert.That((brush as ISolidColorBrush)?.Color, Is.EqualTo(Colors.White));
                return 0;
            }
            finally { window.Close(); }
        }, CancellationToken.None);

    [Test]
    public Task StreamingRevealOffset_ValidatesInheritsAndExpandsBoundsOnlyForNewLines() =>
        HeadlessSession.Current.Dispatch(() =>
        {
            var block = new MarkdownTextBlock { Text = "history", Foreground = Brushes.Black };
            Assert.That(block.StreamingRevealDuration, Is.EqualTo(TimeSpan.FromMilliseconds(220)));
            Assert.That(block.StreamingRevealRiseDuration, Is.EqualTo(TimeSpan.FromMilliseconds(380)));
            Assert.That(block.StreamingRevealOffset, Is.EqualTo(4d));
            Assert.Throws<ArgumentException>(() => block.StreamingRevealRiseDuration = TimeSpan.FromMilliseconds(-1));
            Assert.Throws<ArgumentException>(() => block.StreamingRevealOffset = -1);
            Assert.Throws<ArgumentException>(() => block.StreamingRevealOffset = double.NaN);
            Assert.Throws<ArgumentException>(() => block.StreamingRevealOffset = double.PositiveInfinity);

            const string initial = "```cs\nline\n```\n\nfirst";
            var parsed = Markdown.Parse(initial, MarkdownUpdateProducer.DefaultPipeline);
            var renderer = new MarkdownRenderer
            {
                StreamingRevealMode = MarkdownStreamingRevealMode.Character,
                StreamingRevealDuration = TimeSpan.FromMinutes(1),
                StreamingRevealRiseDuration = TimeSpan.FromMinutes(2),
                StreamingRevealOffset = 6d,
                DocumentUpdate = new MarkdownDocumentUpdate.Full(parsed)
            };
            var window = new Window { Width = 360, Height = 240, Content = renderer };
            try
            {
                window.Show();
                var textBlock = renderer.GetVisualDescendants().OfType<MarkdownTextBlock>()
                    .Single(b => b.TemplatedParent is not CodeBlock);
                var codeTextBlock = renderer.GetVisualDescendants().OfType<CodeBlock>().Single().CodeTextBlock!;
                Assert.That(textBlock.StreamingRevealOffset, Is.EqualTo(6d));
                Assert.That(codeTextBlock.StreamingRevealOffset, Is.EqualTo(6d));
                Assert.That(textBlock.StreamingRevealRiseDuration, Is.EqualTo(TimeSpan.FromMinutes(2)));
                Assert.That(codeTextBlock.StreamingRevealRiseDuration, Is.EqualTo(TimeSpan.FromMinutes(2)));

                // Same-line continuation keeps LineAge unset so settled prefixes never shift vertically.
                var markdown = initial;
                AppendMarkdown(" tail");
                window.UpdateLayout();
                var sameLineSnapshot = textBlock.ActiveRevealSnapshot!;
                Assert.That(sameLineSnapshot, Is.Not.Null);
                Assert.That(sameLineSnapshot.Lines.Single().LineAge, Is.Null);
                Assert.That(sameLineSnapshot.Bounds, Is.EqualTo(sameLineSnapshot.Lines.Single().Bounds));

                // Appending onto a newly started line sets LineAge only on the new line and keeps its earliest timestamp monotonic.
                AppendMarkdown("  \nnext");
                window.UpdateLayout();
                var firstNewLineSnapshot = textBlock.ActiveRevealSnapshot!;
                Assert.That(firstNewLineSnapshot.Lines, Has.Length.EqualTo(2));
                Assert.That(firstNewLineSnapshot.Lines[0].LineAge, Is.Null);
                Assert.That(firstNewLineSnapshot.Lines[1].LineAge, Is.EqualTo(TimeSpan.Zero));
                var expectedExpanded = firstNewLineSnapshot.Lines[0].Bounds
                    .Union(firstNewLineSnapshot.Lines[1].Bounds)
                    .Union(firstNewLineSnapshot.Lines[1].Bounds.Translate(new Vector(0, 6)));
                Assert.That(firstNewLineSnapshot.Bounds, Is.EqualTo(expectedExpanded));

                var firstTimestamp = textBlock.ActiveRevealSpans[^1].Timestamp;
                Thread.Sleep(20);
                AppendMarkdown(" more");
                window.UpdateLayout();
                var continuedNewLineSnapshot = textBlock.ActiveRevealSnapshot!;
                Assert.That(continuedNewLineSnapshot.Lines, Has.Length.EqualTo(2));
                Assert.That(continuedNewLineSnapshot.Lines[0].LineAge, Is.Null);
                Assert.That(continuedNewLineSnapshot.Lines[1].LineAge, Is.GreaterThan(TimeSpan.Zero));
                Assert.That(Stopwatch.GetElapsedTime(firstTimestamp, continuedNewLineSnapshot.LatestTimestamp),
                    Is.EqualTo(continuedNewLineSnapshot.Lines[1].LineAge!.Value));
                Assert.That(continuedNewLineSnapshot.RiseDistance, Is.EqualTo(6d));

                // Zero offset disables bounds expansion without disabling Character reveal.
                renderer.StreamingRevealOffset = 0d;
                window.UpdateLayout();
                var zeroOffsetSnapshot = textBlock.ActiveRevealSnapshot!;
                Assert.That(zeroOffsetSnapshot.Bounds,
                    Is.EqualTo(zeroOffsetSnapshot.Lines[0].Bounds.Union(zeroOffsetSnapshot.Lines[1].Bounds)));

                // Line mode also expands snapshot bounds by StreamingRevealOffset.
                renderer.StreamingRevealMode = MarkdownStreamingRevealMode.Line;
                renderer.StreamingRevealOffset = 5d;
                AppendMarkdown("  \nthird");
                window.UpdateLayout();
                var lineSnapshot = textBlock.ActiveRevealSnapshot!;
                Assert.That(lineSnapshot.Bounds,
                    Is.EqualTo(lineSnapshot.Lines[0].Bounds.Union(lineSnapshot.Lines[0].Bounds.Translate(new Vector(0, 5)))));

                void AppendMarkdown(string suffix)
                {
                    var previous = renderer.DocumentUpdate!;
                    var next = markdown + suffix;
                    renderer.DocumentUpdate = new MarkdownDocumentUpdate.Incremental(
                        previous,
                        Markdown.Parse(next, MarkdownUpdateProducer.DefaultPipeline),
                        new(markdown.Length, suffix.Length, next.Length, previous.Version + 1));
                    markdown = next;
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);

    [Test]
    public Task StreamingRevealRiseDuration_SeparatesFadeAndRiseDurations() =>
        HeadlessSession.Current.Dispatch(() =>
        {
            var fade = TimeSpan.FromMilliseconds(100);
            var rise = TimeSpan.FromMilliseconds(400);
            var block = new MarkdownTextBlock
            {
                Text = "first", Foreground = Brushes.Black,
                StreamingRevealMode = MarkdownStreamingRevealMode.Character,
                StreamingRevealDuration = fade,
                StreamingRevealRiseDuration = rise,
                StreamingRevealOffset = 4d
            };
            var window = new Window { Width = 360, Height = 160, Content = block };
            try
            {
                window.Show();
                Append(block, " tail");
                window.UpdateLayout();
                var sameLineSnapshot = block.ActiveRevealSnapshot!;
                Assert.That(sameLineSnapshot, Is.Not.Null);
                Assert.That(sameLineSnapshot.FadeDuration, Is.EqualTo(fade));
                Assert.That(sameLineSnapshot.RiseDuration, Is.EqualTo(rise));
                Assert.That(sameLineSnapshot.TotalDuration, Is.LessThanOrEqualTo(fade));
                Assert.That(sameLineSnapshot.Lines.Single().LineAge, Is.Null);

                Append(block, "\nsecond");
                window.UpdateLayout();
                var newLineSnapshot = block.ActiveRevealSnapshot!;
                Assert.That(newLineSnapshot, Is.Not.Null);
                Assert.That(newLineSnapshot.TotalDuration, Is.GreaterThan(TimeSpan.FromMilliseconds(250)));
                Assert.That(newLineSnapshot.TotalDuration, Is.LessThanOrEqualTo(rise));
            }
            finally { window.Close(); }
        }, CancellationToken.None);

    [Test]
    public Task StreamingReveal_ZeroFadeWithPositiveRise_AnimatesNewLinesAndSkipsSameLine() =>
        HeadlessSession.Current.Dispatch(() =>
        {
            var rise = TimeSpan.FromMilliseconds(380);
            var block = new MarkdownTextBlock
            {
                Text = "first", Foreground = Brushes.Black,
                StreamingRevealMode = MarkdownStreamingRevealMode.Character,
                StreamingRevealDuration = TimeSpan.Zero,
                StreamingRevealRiseDuration = rise,
                StreamingRevealOffset = 4d
            };
            var window = new Window { Width = 360, Height = 160, Content = block };
            try
            {
                window.Show();
                Append(block, " tail");
                window.UpdateLayout();
                Assert.That(block.ActiveRevealSnapshot, Is.Null);
                Assert.That(block.ActiveRevealSpans, Is.Empty);

                Append(block, "\nsecond");
                window.UpdateLayout();
                var snapshot = block.ActiveRevealSnapshot!;
                Assert.That(snapshot, Is.Not.Null);
                Assert.That(snapshot.FadeDuration, Is.EqualTo(TimeSpan.Zero));
                Assert.That(snapshot.RiseDuration, Is.EqualTo(rise));
                Assert.That(snapshot.TotalDuration, Is.GreaterThan(TimeSpan.FromMilliseconds(200)));
                Assert.That(snapshot.Lines, Has.Length.EqualTo(1));
                Assert.That(snapshot.Lines[0].Bands, Is.Empty);
                Assert.That(snapshot.Lines[0].LineAge, Is.Not.Null);
            }
            finally { window.Close(); }
        }, CancellationToken.None);

    private static void Append(MarkdownTextBlock block, string chunk)
    {
        var before = block.CaptureRevealBaseline();
        block.Text += chunk;
        block.ApplyRevealUpdate(before);
    }

    private static void Tick()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Commit(Compositor compositor)
    {
        var applied = compositor.RequestCommitAsync();
        Tick();
        await applied.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
