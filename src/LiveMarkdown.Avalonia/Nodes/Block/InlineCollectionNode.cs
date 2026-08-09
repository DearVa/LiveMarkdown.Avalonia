using Avalonia.Controls;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Works as <see cref="MarkdownTextBlock"/>
/// </summary>
public class InlineCollectionNode<TBlock> : BlockNode<TBlock> where TBlock : LeafBlock
{
    /// <summary>
    /// Gets the text block that renders the inline collection.
    /// </summary>
    public override Control Control => textBlock;

    private readonly InlinesNode<ContainerInline> inlinesNode;
    private readonly MarkdownTextBlock textBlock;

    /// <summary>
    /// Initializes an inline collection node and its text block.
    /// </summary>
    public InlineCollectionNode()
    {
        inlinesNode = new InlinesNode<ContainerInline>(new global::Avalonia.Controls.Documents.Span());
        textBlock = new MarkdownTextBlock
        {
            Inlines = inlinesNode.Inlines
        };
    }

    /// <summary>
    /// Synchronizes the inline content of a leaf block.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="block">The Markdig leaf block.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> when inline content is available.</returns>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        TBlock block,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        return block.Inline is { } inline &&
            inlinesNode.Update(
                documentNode,
                inline,
                change,
                cancellationToken) is not false;
    }
}