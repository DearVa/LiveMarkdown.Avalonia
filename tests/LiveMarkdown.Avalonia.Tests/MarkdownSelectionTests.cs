using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
public class MarkdownSelectionTests
{
    [Test]
    public void EscapedTextLength_CountsLineBreakLikeActualText()
    {
        var textBlock = CreateMultilineTextBlock("first", "last");
        var expected = "first" + Environment.NewLine + "last";

        Assert.That(textBlock.ActualText, Is.EqualTo(expected));
        Assert.That(textBlock.EscapedTextLength, Is.EqualTo(expected.Length));
    }

    [Test]
    public void SelectAll_IncludesTextAfterLineBreak()
    {
        var textBlock = CreateMultilineTextBlock("first", "last");

        textBlock.SelectAll();

        Assert.That(textBlock.ActualSelectedText, Is.EqualTo("first" + Environment.NewLine + "last"));
    }

    [Test]
    public void CodeBlockInlines_SelectAllIncludesLastLine()
    {
        var codeBlock = new CodeBlock
        {
            Code = "first\nmiddle\nlast"
        };
        var textBlock = new MarkdownTextBlock
        {
            Inlines = codeBlock.Inlines
        };

        textBlock.SelectAll();

        Assert.That(textBlock.EscapedTextLength, Is.EqualTo(textBlock.ActualText.Length));
        Assert.That(textBlock.ActualSelectedText, Is.EqualTo("first" + Environment.NewLine + "middle" + Environment.NewLine + "last"));
    }

    [Test]
    public void ResolveSelectionScopeRoot_UsesTopmostScope()
    {
        var outer = new StackPanel();
        var inner = new StackPanel();
        var textBlock = new MarkdownTextBlock();
        var fallback = new MarkdownRenderer();

        MarkdownTextBlock.SetIsSelectionScope(outer, true);
        MarkdownTextBlock.SetIsSelectionScope(inner, true);

        outer.Children.Add(inner);
        inner.Children.Add(textBlock);

        Assert.That(MarkdownRenderer.ResolveSelectionScopeRoot(textBlock, fallback), Is.SameAs(outer));
    }

    [Test]
    public void ResolveSelectionScopeRoot_FallsBackToRendererWhenNoScopeExists()
    {
        var renderer = new MarkdownRenderer();
        var textBlock = new MarkdownTextBlock();

        Assert.That(MarkdownRenderer.ResolveSelectionScopeRoot(textBlock, renderer), Is.SameAs(renderer));
    }

    [Test]
    public void GetAllSelectableBlocksInScope_ReturnsBlocksInVisualOrder()
    {
        var root = new StackPanel();
        var first = new MarkdownTextBlock { Text = "first" };
        var second = new MarkdownTextBlock { Text = "second" };

        root.Children.Add(first);
        root.Children.Add(new Border { Child = second });

        Assert.That(MarkdownRenderer.GetAllSelectableBlocksInScope(root).ToArray(), Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void AutoScrollDelta_StartsWhenPointerLeavesBounds()
    {
        var delta = MarkdownRenderer.GetAutoScrollDelta(
            new Size(100, 100),
            new Point(50, 120),
            ScrollBarVisibility.Auto,
            ScrollBarVisibility.Auto);

        Assert.That(delta.X, Is.EqualTo(0));
        Assert.That(delta.Y, Is.GreaterThan(0));
    }

    [Test]
    public void AutoScrollDelta_IgnoresDisabledAxis()
    {
        var delta = MarkdownRenderer.GetAutoScrollDelta(
            new Size(100, 100),
            new Point(120, 120),
            ScrollBarVisibility.Disabled,
            ScrollBarVisibility.Auto);

        Assert.That(delta.X, Is.EqualTo(0));
        Assert.That(delta.Y, Is.GreaterThan(0));
    }

    [Test]
    public void CoerceAutoScrollOffset_ClampsAtExtentBoundary()
    {
        var offset = MarkdownRenderer.CoerceAutoScrollOffset(
            new Vector(0, 95),
            new Size(100, 200),
            new Size(100, 100),
            new Vector(0, 20),
            ScrollBarVisibility.Auto,
            ScrollBarVisibility.Auto);

        Assert.That(offset, Is.EqualTo(new Vector(0, 100)));
    }

    [Test]
    public void ScrollViewer_ChainingIsConfigurableForNestedAutoScroll()
    {
        var scrollViewer = new ScrollViewer();

        Assert.That(scrollViewer.IsScrollChainingEnabled, Is.True);

        scrollViewer.IsScrollChainingEnabled = false;

        Assert.That(scrollViewer.IsScrollChainingEnabled, Is.False);
    }

    [Test]
    public void CodeInline_IsARealTextRunWithDirectTextCoordinates()
    {
        var textBlock = new MarkdownTextBlock();
        textBlock.Inlines!.Add(new Run("before "));
        textBlock.Inlines.Add(new CodeInline
        {
            Text = "code",
            Background = Brushes.Gray,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2, 0),
        });
        textBlock.Inlines.Add(new Run(" after"));

        Assert.That(textBlock.ActualText, Is.EqualTo("before code after"));
        Assert.That(textBlock.EscapedTextLength, Is.EqualTo(textBlock.ActualText.Length));
        Assert.That(textBlock.Inlines[1], Is.TypeOf<CodeInline>());
    }

    [Test]
    public void ActualSelectedText_VisualInlineObjectAdvancesOneLayoutPosition()
    {
        var textBlock = new MarkdownTextBlock
        {
            Inlines = new InlineCollection
            {
                new Run("A"),
                new InlineUIContainer(new Border()),
                new Run("B"),
            },
            SelectionStart = 2,
            SelectionEnd = 3,
        };

        Assert.That(textBlock.ActualSelectedText, Is.EqualTo("B"));
    }

    [Test]
    public void ActualSelectedText_EmbeddedTextUsesChildSelectionAndParentLayoutPosition()
    {
        var nested = new MarkdownTextBlock
        {
            Text = "child",
            SelectionStart = 2,
            SelectionEnd = 5,
        };
        var parent = new MarkdownTextBlock
        {
            Inlines = new InlineCollection
            {
                new Run("A"),
                new InlineUIContainer(nested),
                new Run("B"),
            },
            SelectionStart = 2,
            SelectionEnd = 3,
        };

        Assert.That(parent.ActualSelectedText, Is.EqualTo("ildB"));
    }

    [Test]
    public void TextHighlightRegistry_MergesOverlappingAndAdjacentRanges()
    {
        var registry = new TextHighlightRegistry();

        registry.Set(
            "search-results",
            [
                new TextHighlightRange(3, 4),
                new TextHighlightRange(0, 3),
                new TextHighlightRange(2, 3),
                new TextHighlightRange(10, 2),
            ]);

        var highlight = registry.Values.Single();

        Assert.That(highlight.Ranges, Is.EqualTo([
            new TextHighlightRange(0, 7),
            new TextHighlightRange(10, 2),
        ]));
    }

    [Test]
    public void HighlightStyles_InheritsThroughLogicalTree()
    {
        var styles = new TextHighlightStyles();
        var root = new StackPanel();
        var textBlock = new MarkdownTextBlock();

        root.SetValue(MarkdownTextBlock.HighlightStylesProperty, styles);
        root.Children.Add(textBlock);

        Assert.That(textBlock.HighlightStyles, Is.SameAs(styles));
    }

    private static MarkdownTextBlock CreateMultilineTextBlock(string firstLine, string lastLine)
    {
        var textBlock = new MarkdownTextBlock();
        textBlock.Inlines!.Add(new Run(firstLine));
        textBlock.Inlines.Add(new LineBreak());
        textBlock.Inlines.Add(new Run(lastLine));
        return textBlock;
    }
}
