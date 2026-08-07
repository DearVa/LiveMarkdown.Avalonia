using Avalonia.Controls;
using Avalonia.Controls.Documents;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
public class MarkdownTextSearchTests
{
    [Test]
    public void ApplyTextSearch_UsesLocalTextForNestedInlineBlocks()
    {
        var renderer = new TestMarkdownRenderer();
        var nested = new MarkdownTextBlock { Text = "nested target" };
        var parent = new MarkdownTextBlock
        {
            Inlines = new InlineCollection
            {
                new Run("before "),
                new InlineUIContainer(nested),
                new Run(" after"),
            },
        };
        renderer.Add(parent);

        var matches = renderer.ApplyTextSearch("target");

        Assert.That(matches, Has.Count.EqualTo(1));
        Assert.That(matches[0].Block, Is.SameAs(nested));
        Assert.That(matches[0].Range, Is.EqualTo(new TextHighlightRange(7, 6)));
    }

    [Test]
    public void ApplyTextSearch_StringOptionsApplyCaseAndWholeWordRules()
    {
        var renderer = new TestMarkdownRenderer();
        var block = new MarkdownTextBlock { Text = "Alpha alphabet ALPHA" };
        renderer.Add(block);

        var wholeWordMatches = renderer.ApplyTextSearch("alpha", TextSearchOptions.WholeWord);

        Assert.That(wholeWordMatches, Has.Count.EqualTo(2));

        var caseSensitiveMatches = renderer.ApplyTextSearch(
            "alpha",
            TextSearchOptions.MatchCase | TextSearchOptions.WholeWord);

        Assert.That(caseSensitiveMatches, Has.Count.EqualTo(0));

        var uppercaseMatches = renderer.ApplyTextSearch(
            "ALPHA",
            TextSearchOptions.MatchCase | TextSearchOptions.WholeWord);

        Assert.That(uppercaseMatches, Has.Count.EqualTo(1));
        Assert.That(uppercaseMatches[0].Range, Is.EqualTo(new TextHighlightRange(15, 5)));
    }

    [Test]
    public void ApplyTextSearch_DelegateIsRetainedAndCanReturnCustomRanges()
    {
        var renderer = new TestMarkdownRenderer();
        var block = new MarkdownTextBlock { Text = "one two" };
        renderer.Add(block);

        var invocations = 0;
        var matches = renderer.ApplyTextSearch((_, text) =>
        {
            invocations++;
            return [new TextHighlightRange(text.IndexOf("two", StringComparison.Ordinal), 3)];
        });

        Assert.That(invocations, Is.EqualTo(1));
        Assert.That(matches, Has.Count.EqualTo(1));
        Assert.That(matches[0].Range, Is.EqualTo(new TextHighlightRange(4, 3)));
        Assert.That(block.Highlights.Count, Is.EqualTo(1));

        renderer.ClearTextSearch();

        Assert.That(renderer.TextSearchMatches, Is.Empty);
        Assert.That(block.Highlights.Count, Is.Zero);
    }

    [Test]
    public void ApplyTextSearch_ChangingHighlightNameRemovesPreviousRanges()
    {
        var renderer = new TestMarkdownRenderer();
        var block = new MarkdownTextBlock { Text = "target again" };
        renderer.Add(block);

        renderer.ApplyTextSearch("target", "first");
        renderer.ApplyTextSearch("again", "second");

        Assert.That(block.Highlights.Remove("first"), Is.False);
        Assert.That(block.Highlights.Values.Single().Name, Is.EqualTo("second"));
    }

    private sealed class TestMarkdownRenderer : MarkdownRenderer
    {
        public TestMarkdownRenderer()
        {
        }

        public void Add(MarkdownTextBlock block) => ((Panel)VisualChildren[0]).Children.Add(block);

        protected override Type StyleKeyOverride => typeof(MarkdownRenderer);
    }
}
