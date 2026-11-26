using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Markdig;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class StreamingRevealTriggerTests
{
    [Test]
    public Task OnlyMatchingIncrementalsAnimate_AndFullReplacementClearsTheReusedBlock() =>
        WithRenderer("history", (renderer, _) =>
        {
            var block = TextBlocks(renderer).Single();
            Assert.That(Ranges(block), Is.Empty);

            Append(renderer, "history", " text");
            Assert.That(Ranges(block), Is.EqualTo(new[] { (7, 5) }));
            Append(renderer, "history text", "!");
            Assert.That(Ranges(block), Is.EqualTo(new[] { (7, 5), (12, 1) }));

            var unrelated = Full("elsewhere");
            renderer.DocumentUpdate = new MarkdownDocumentUpdate.Incremental(unrelated, Parse("other history"), new(0, 13, 13, 1));
            Assert.That(TextBlocks(renderer).Single(), Is.SameAs(block));
            Assert.That(Ranges(block), Is.Empty);
            Append(renderer, "other history", "!");
            Assert.That(Ranges(block), Is.EqualTo(new[] { (13, 1) }));
            renderer.DocumentUpdate = Full("replacement");
            Assert.That(Ranges(block), Is.Empty);
        });

    [TestCase("**hel", "lo**", "hello", 3, 2)]
    [TestCase("Before **hel", "lo**", "Before hello", 10, 2)]
    [TestCase("Before `hel", "lo`", "Before hello", 10, 2)]
    public Task ClosingInlineSyntax_OnlyRevealsNewVisibleLetters(string initial, string append, string visible, int start, int length) =>
        WithRenderer(initial, (renderer, _) =>
        {
            Append(renderer, initial, append);
            var block = TextBlocks(renderer).Single();
            Assert.That(block.LayoutText, Is.EqualTo(visible));
            Assert.That(Ranges(block), Is.EqualTo(new[] { (start, length) }));
        });

    [Test]
    public Task ClosingBold_ClampsAStillActiveRangeInsteadOfReplayingItsLetters() =>
        WithRenderer("**he", (renderer, _) =>
        {
            var block = TextBlocks(renderer).Single();
            Append(renderer, "**he", "l");
            Assert.That(Ranges(block), Is.EqualTo(new[] { (4, 1) }));
            Append(renderer, "**hel", "lo**");
            Assert.That(block.LayoutText, Is.EqualTo("hello"));
            Assert.That(Ranges(block), Is.EqualTo(new[] { (3, 2) }));
        });

    [Test]
    public Task NewParagraphAndFencedCode_ReceiveRevealAfterInheritanceAndTemplateAttach() =>
        WithRenderer("History", (renderer, window) =>
        {
            const string addition = "\n\nNext\n\n```cs\nint value";
            Append(renderer, "History", addition);
            window.UpdateLayout();
            var blocks = TextBlocks(renderer);
            Assert.That(Ranges(blocks.Single(b => b.LayoutText == "History")), Is.Empty);
            Assert.That(Ranges(blocks.Single(b => b.LayoutText == "Next")), Is.EqualTo(new[] { (0, 4) }));
            var code = renderer.GetVisualDescendants().OfType<CodeBlock>().Single().CodeTextBlock!;
            Assert.That(code.StreamingRevealMode, Is.EqualTo(MarkdownStreamingRevealMode.Character));
            Assert.That(code.StreamingRevealDuration, Is.EqualTo(renderer.StreamingRevealDuration));
            Assert.That(Ranges(code), Is.EqualTo(new[] { (0, 9) }));

            Append(renderer, "History" + addition, ";");
            Assert.That(code.LayoutText, Is.EqualTo("int value;"));
            Assert.That(Ranges(code), Is.EqualTo(new[] { (0, 9), (9, 1) }));
        });

    [Test]
    public Task TableCells_AnimateEvenWhenScrollContentIsRealizedAfterTheUpdate() =>
        WithRenderer("Intro", (renderer, window) =>
        {
            Append(renderer, "Intro", "\n\n| Head | Value |\n| --- | --- |\n| A | B |");
            window.UpdateLayout();
            var blocks = TextBlocks(renderer).ToDictionary(b => b.LayoutText);
            Assert.That(Ranges(blocks["Intro"]), Is.Empty);
            foreach (var value in new[] { "Head", "Value", "A", "B" })
                Assert.That(Ranges(blocks[value]), Is.EqualTo(new[] { (0, value.Length) }));
        });

    [Test]
    public Task RecycledRendererDoesNotReplayUpdatesReceivedWhileDetached() =>
        WithRenderer("old", (renderer, window) =>
        {
            Append(renderer, "old", " message");
            window.UpdateLayout();
            Assert.That(TextBlocks(renderer).Single().ActiveRevealSnapshot, Is.Not.Null);
            window.Content = null;
            renderer.DocumentUpdate = Full("other");
            Append(renderer, "other", " history\n\n```\ncode");
            window.Content = renderer;
            window.UpdateLayout();
            var blocks = TextBlocks(renderer).ToArray();
            Assert.That(blocks.Select(b => b.LayoutText), Is.EquivalentTo(new[] { "other history", "code" }));
            Assert.That(blocks.SelectMany(b => b.ActiveRevealSpans), Is.Empty);
            Assert.That(blocks.All(b => b.ActiveRevealSnapshot is null), Is.True);
        });

    [Test]
    public Task FullLoadBeforeCodeTemplateRealization_CancelsThePendingReveal() =>
        WithRenderer("History", (renderer, window) =>
        {
            const string addition = "\n\n```\nfirst";
            Append(renderer, "History", addition);
            renderer.DocumentUpdate = Full("History\n\n```\nreplacement");
            window.UpdateLayout();
            var code = renderer.GetVisualDescendants().OfType<CodeBlock>().Single().CodeTextBlock!;
            Assert.That(code.LayoutText, Is.EqualTo("replacement"));
            Assert.That(Ranges(code), Is.Empty);
        });

    [Test]
    public Task TextBlockStyleCanEnableReveal_WithoutEnablingTheRenderer() =>
        WithRenderer("old", (renderer, _) =>
        {
            Assert.That(renderer.StreamingRevealMode, Is.EqualTo(MarkdownStreamingRevealMode.None));
            Assert.That(TextBlocks(renderer).Single().StreamingRevealMode, Is.EqualTo(MarkdownStreamingRevealMode.Character));
            Append(renderer, "old", " text\n\nnew");
            var blocks = TextBlocks(renderer);
            Assert.That(Ranges(blocks.Single(b => b.LayoutText == "old text")), Is.EqualTo(new[] { (3, 5) }));
            Assert.That(Ranges(blocks.Single(b => b.LayoutText == "new")), Is.EqualTo(new[] { (0, 3) }));
        }, renderer =>
        {
            renderer.StreamingRevealMode = MarkdownStreamingRevealMode.None;
            var style = new Style(s => s.OfType<MarkdownTextBlock>());
            style.Setters.Add(new Setter(MarkdownTextBlock.StreamingRevealModeProperty, MarkdownStreamingRevealMode.Character));
            renderer.Styles.Add(style);
        });

    private static Task WithRenderer(string initial, Action<MarkdownRenderer, Window> test, Action<MarkdownRenderer>? configure = null) =>
        HeadlessSession.Current.Dispatch(() =>
        {
            var renderer = new MarkdownRenderer
            {
                StreamingRevealMode = MarkdownStreamingRevealMode.Character,
                StreamingRevealDuration = TimeSpan.FromMinutes(1),
                DocumentUpdate = Full(initial)
            };
            configure?.Invoke(renderer);
            var window = new Window { Content = renderer, Width = 400, Height = 240 };
            try
            {
                window.Show();
                test(renderer, window);
            }
            finally { window.Close(); }
        }, CancellationToken.None);

    private static IEnumerable<MarkdownTextBlock> TextBlocks(MarkdownRenderer renderer) =>
        renderer.GetVisualDescendants().OfType<MarkdownTextBlock>();

    private static (int, int)[] Ranges(MarkdownTextBlock block) =>
        block.ActiveRevealSpans.Select(s => (s.Start, s.Length)).ToArray();

    private static Markdig.Syntax.MarkdownDocument Parse(string markdown) => Markdown.Parse(markdown, MarkdownUpdateProducer.DefaultPipeline);

    private static MarkdownDocumentUpdate.Full Full(string markdown) => new(Parse(markdown));

    private static void Append(MarkdownRenderer renderer, string previous, string suffix)
    {
        var update = renderer.DocumentUpdate!;
        renderer.DocumentUpdate = new MarkdownDocumentUpdate.Incremental(update, Parse(previous + suffix),
            new(previous.Length, suffix.Length, previous.Length + suffix.Length, update.Version + 1));
    }
}
