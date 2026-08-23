using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Markdig.Helpers;
using NUnit.Framework;
using MarkdigInline = Markdig.Syntax.Inlines.Inline;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
public class MarkdownTextSearchTests
{
    [Test]
    public void MarkdownTextProjector_FlattensVisualTextBlocksWithoutMarkdownFormatting()
    {
        var projector = new MarkdownTextProjector();
        var projection = projector.Project(
            new ObservableStringBuilderSnapshot(
                "# Hel**lo**\n\n- [x] task\n\n```text\ncode line\n```",
                7));

        Assert.Multiple(() =>
        {
            Assert.That(projection.SourceVersion, Is.EqualTo(7));
            Assert.That(projection.Buffers.Select(buffer => buffer.Text.ToString()), Is.EqualTo(new[]
            {
                "Hello",
                "\uFFFC task",
                "code line",
            }));
        });
    }

    [Test]
    public void MarkdownTextProjector_WhenLeafContainsOneLiteral_ReusesSourceStorage()
    {
        var markdown = "# Heading";
        var projection = new MarkdownTextProjector().Project(
            new ObservableStringBuilderSnapshot(markdown, 1));
        var text = projection.Buffers.Single().Text;

        Assert.Multiple(() =>
        {
            Assert.That(text.Text, Is.SameAs(markdown));
            Assert.That(text.Start, Is.EqualTo(2));
            Assert.That(text.ToString(), Is.EqualTo("Heading"));
        });
    }

    [Test]
    public void MarkdownTextProjector_DefaultTraversalDispatchesToVirtualInlineHook()
    {
        var projector = new TrackingMarkdownTextProjector();

        var projection = projector.Project(
            new ObservableStringBuilderSnapshot("**custom**", 1));

        Assert.Multiple(() =>
        {
            Assert.That(projector.InlineCalls, Is.GreaterThan(0));
            Assert.That(projection.Buffers.Single().Text.ToString(), Is.EqualTo("custom"));
        });
    }

    [Test]
    public void TextSearchPattern_IsSharedByProjectionAndRendererSearch()
    {
        var pattern = new TextSearchPattern("alpha", TextSearchOptions.WholeWord);
        var projectedRanges = pattern.FindRanges("Alpha alphabet ALPHA").ToArray();
        var renderer = new TestMarkdownRenderer();
        renderer.Add(new MarkdownTextBlock { Text = "Alpha alphabet ALPHA" });

        var renderedMatches = renderer.ApplyTextSearch(pattern);

        Assert.That(renderedMatches.Select(match => match.Range), Is.EqualTo(projectedRanges));
    }

    [Test]
    public void TextSearchPattern_DoesNotExposeEmbeddedObjectPositions()
    {
        var pattern = new TextSearchPattern(MarkdownTextProjection.ObjectReplacementCharacter.ToString());

        Assert.That(
            pattern.FindRanges($"before{MarkdownTextProjection.ObjectReplacementCharacter}after"),
            Is.Empty);
    }

    [Test]
    public void TextSearchPattern_WhenWholeWordCandidateIsRejected_ConsidersOverlappingCandidate()
    {
        var pattern = new TextSearchPattern("a-a", TextSearchOptions.WholeWord);
        var ranges = pattern.FindRanges("xa-a-a").ToArray();
        Assert.That(ranges, Is.EqualTo(new[] { new TextHighlightRange(3, 3) }));
    }

    [Test]
    public void TextSearchPattern_StringSliceUsesLocalCoordinatesAndSliceBoundaries()
    {
        var source = "xalphaY";
        var slice = new StringSlice(source, 1, 5);
        var pattern = new TextSearchPattern("alpha", TextSearchOptions.WholeWord);

        Assert.That(
            pattern.FindRanges(slice),
            Is.EqualTo(new[] { new TextHighlightRange(0, 5) }));
    }

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
    public void LayoutText_UsesOneObjectReplacementPositionForEmbeddedControls()
    {
        var nested = new MarkdownTextBlock { Text = "nested" };
        var parent = new MarkdownTextBlock
        {
            Inlines = new InlineCollection
            {
                new Run("before "),
                new InlineUIContainer(nested),
                new Run(" after"),
            },
        };

        Assert.Multiple(() =>
        {
            Assert.That(parent.ActualText, Is.EqualTo("before nested after"));
            Assert.That(
                parent.LayoutText,
                Is.EqualTo($"before {MarkdownTextProjection.ObjectReplacementCharacter} after"));
            Assert.That(parent.EscapedTextLength, Is.EqualTo(parent.LayoutText.Length));
        });
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
    public void ApplyTextSearch_WhenMatchesAreAdjacent_PreservesEachMatchAndHighlightRange()
    {
        var renderer = new TestMarkdownRenderer();
        var block = new MarkdownTextBlock { Text = "aa" };
        renderer.Add(block);

        var matches = renderer.ApplyTextSearch("a");

        Assert.Multiple(() =>
        {
            Assert.That(
                matches.Select(match => match.Range),
                Is.EqualTo(new[]
                {
                    new TextHighlightRange(0, 1),
                    new TextHighlightRange(1, 1),
                }));
            Assert.That(
                block.Highlights.TryGetValue(MarkdownRenderer.DefaultTextSearchHighlightName, out var highlight),
                Is.True);
            Assert.That(
                highlight!.Ranges,
                Is.EqualTo(new[]
                {
                    new TextHighlightRange(0, 1),
                    new TextHighlightRange(1, 1),
                }));
        });
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

    [Test]
    public void ApplyTextSearch_WhenBlockVisibilityChanges_UsesCurrentRenderedBlocks()
    {
        var renderer = new TestMarkdownRenderer();
        var block = new MarkdownTextBlock { Text = "target" };
        renderer.Add(block);

        var visibleMatches = renderer.ApplyTextSearch("target");
        block.IsVisible = false;
        var hiddenMatches = renderer.ApplyTextSearch("target");
        block.IsVisible = true;
        var restoredMatches = renderer.ApplyTextSearch("target");

        Assert.Multiple(() =>
        {
            Assert.That(visibleMatches, Has.Count.EqualTo(1));
            Assert.That(hiddenMatches, Is.Empty);
            Assert.That(restoredMatches, Has.Count.EqualTo(1));
        });
    }

    private sealed class TestMarkdownRenderer : MarkdownRenderer
    {
        public TestMarkdownRenderer()
        {
        }

        public void Add(MarkdownTextBlock block) => ((Panel)VisualChildren[0]).Children.Add(block);

        protected override Type StyleKeyOverride => typeof(MarkdownRenderer);
    }

    private sealed class TrackingMarkdownTextProjector : MarkdownTextProjector
    {
        public int InlineCalls { get; private set; }

        protected override void AppendInline(
            MarkdigInline inline,
            StringBuilder builder,
            CancellationToken cancellationToken)
        {
            InlineCalls++;
            base.AppendInline(inline, builder, cancellationToken);
        }
    }
}
