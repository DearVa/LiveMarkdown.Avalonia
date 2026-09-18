using Avalonia.Controls;
using Avalonia.LogicalTree;
using Markdig;
using Markdig.Extensions.Tables;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class MarkdownRendererStreamingTests
{
    [Test]
    public void UpdatingOpenFenceInfo_ReplacesCodeBlockNodeWithMermaidBlockNode()
    {
        MarkdownNode.Register<MermaidBlockNode>();
        var pipeline = new MarkdownPipelineBuilder().UseMermaid().Build();
        var owner = new MarkdownRenderer();
        var documentNode = new DocumentNode(owner);

        var intermediateMarkdown =
            """
            ```m
            graph TD
                A --> B
            """;
        var mermaidMarkdown =
            """
            ```mermaid
            graph TD
                A --> B
            """;

        var intermediateDocument = Markdown.Parse(intermediateMarkdown, pipeline);
        var mermaidDocument = Markdown.Parse(mermaidMarkdown, pipeline);

        documentNode.Update(
            documentNode,
            intermediateDocument,
            new ObservableStringBuilderChangedEventArgs(0, intermediateMarkdown.Length, intermediateMarkdown.Length, 1),
            CancellationToken.None);

        Assert.That(documentNode.Control.GetLogicalDescendants().OfType<CodeBlock>(), Has.Exactly(1).Items);
        Assert.That(documentNode.Control.GetLogicalDescendants().OfType<MermaidPresenter>(), Is.Empty);

        documentNode.Update(
            documentNode,
            mermaidDocument,
            new ObservableStringBuilderChangedEventArgs(3, "ermaid".Length, mermaidMarkdown.Length, 2),
            CancellationToken.None);

        Assert.That(documentNode.Control.GetLogicalDescendants().OfType<CodeBlock>(), Is.Empty);
        Assert.That(documentNode.Control.GetLogicalDescendants().OfType<MermaidPresenter>(), Has.Exactly(1).Items);
    }

    [Test]
    public void TableNode_SynchronizesEdgeClassesAndKeepsContentInsideRoundedContainer()
    {
        const string initialMarkdown =
            "| Element | Purpose |\n" +
            "| --- | --- |\n" +
            "| Heading | Document hierarchy |";
        const string appendedMarkdown = "\n| Link | Related destination |";
        const string updatedMarkdown = initialMarkdown + appendedMarkdown;

        var owner = new MarkdownRenderer();
        var documentNode = new DocumentNode(owner);
        var tableNode = new TableNode();
        var initialDocument = Markdown.Parse(initialMarkdown, MarkdownUpdateProducer.DefaultPipeline);

        tableNode.Update(
            documentNode,
            initialDocument.OfType<Table>().Single(),
            new ObservableStringBuilderChangedEventArgs(0, initialMarkdown.Length, initialMarkdown.Length, 1),
            CancellationToken.None);

        var borders = tableNode.Control.GetLogicalDescendants().OfType<Border>().ToArray();
        var tableBorder = borders.Single(border => border.Classes.Contains("Table"));
        var contentBorder = borders.Single(border => border.Classes.Contains("TableContent"));
        var initialCells = borders.Where(border => border.Classes.Contains("TableCell")).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(tableBorder.Child, Is.SameAs(contentBorder));
            Assert.That(initialCells, Has.Exactly(4).Items);
            Assert.That(initialCells.Where(cell => Grid.GetRow(cell) == 0), Has.All.Matches<Border>(cell => cell.Classes.Contains("Header")));
            Assert.That(initialCells.Count(cell => Grid.GetRow(cell) == 0 && cell.Classes.Contains("LastColumn")), Is.EqualTo(1));
            Assert.That(initialCells.Where(cell => Grid.GetRow(cell) == 1), Has.All.Matches<Border>(cell => cell.Classes.Contains("LastRow")));
        });

        var updatedDocument = Markdown.Parse(updatedMarkdown, MarkdownUpdateProducer.DefaultPipeline);
        tableNode.Update(
            documentNode,
            updatedDocument.OfType<Table>().Single(),
            new ObservableStringBuilderChangedEventArgs(
                initialMarkdown.Length,
                appendedMarkdown.Length,
                updatedMarkdown.Length,
                2),
            CancellationToken.None);

        var updatedCells = tableNode.Control.GetLogicalDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("TableCell"))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(updatedCells, Has.Exactly(6).Items);
            Assert.That(updatedCells.Where(cell => Grid.GetRow(cell) < 2), Has.None.Matches<Border>(cell => cell.Classes.Contains("LastRow")));
            Assert.That(updatedCells.Where(cell => Grid.GetRow(cell) == 2), Has.All.Matches<Border>(cell => cell.Classes.Contains("LastRow")));
        });
    }
}
