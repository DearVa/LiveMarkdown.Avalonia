using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
public class BackslashMathBlockParserTests
{
    [Test]
    public void ParseSingleLineBlock_KeepsFollowingMarkdownBlocks()
    {
        var document = Parse(
            """
            \[\frac{d}{dx} f(x)\]

            ### Summary

            | Syntax | Purpose |
            |--------|---------|
            | `$x$`  | Inline  |

            After
            """);

        Assert.Multiple(() =>
        {
            Assert.That(document[0], Is.TypeOf<MathBlock>());
            Assert.That(document[1], Is.TypeOf<HeadingBlock>());
            Assert.That(document[2], Is.TypeOf<Table>());
            Assert.That(document[3], Is.TypeOf<ParagraphBlock>());
        });

        var mathBlock = (MathBlock)document[0];
        Assert.That(mathBlock.Lines.ToString(), Is.EqualTo(@"\frac{d}{dx} f(x)"));
    }

    [Test]
    public void ParseMultilineBlock_AppendsEachContentLineOnce()
    {
        var document = Parse(
            """
            \[
            a + b
            c + d
            \]

            After
            """);

        Assert.That(document, Has.Count.EqualTo(2));
        Assert.That(document[0], Is.TypeOf<MathBlock>());
        Assert.That(document[1], Is.TypeOf<ParagraphBlock>());

        var mathBlock = (MathBlock)document[0];
        Assert.Multiple(() =>
        {
            Assert.That(mathBlock.Lines.Count, Is.EqualTo(2));
            Assert.That(mathBlock.Lines.Lines[0].Slice.ToString(), Is.EqualTo("a + b"));
            Assert.That(mathBlock.Lines.Lines[1].Slice.ToString(), Is.EqualTo("c + d"));
        });
    }

    [Test]
    public void ParseOpenBlock_ReturnsOpenBlockCoveringCurrentContent()
    {
        var markdown =
            """
            \[
            a + b
            c + d
            """;

        var document = Parse(markdown);

        Assert.That(document, Has.Count.EqualTo(1));
        Assert.That(document[0], Is.TypeOf<MathBlock>());

        var mathBlock = (MathBlock)document[0];
        Assert.Multiple(() =>
        {
            Assert.That(mathBlock.IsOpen, Is.True);
            Assert.That(mathBlock.Span.End, Is.EqualTo(markdown.Length - 1));
            Assert.That(mathBlock.Lines.Count, Is.EqualTo(2));
        });
    }

    private static MarkdownDocument Parse(string markdown) =>
        Markdown.Parse(
            markdown,
            new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseExtendedMathematics()
                .Build());
}
