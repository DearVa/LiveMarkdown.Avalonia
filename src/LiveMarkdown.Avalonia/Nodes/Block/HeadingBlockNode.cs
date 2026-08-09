using Avalonia.Controls;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders a Markdown heading and its inline content.
/// </summary>
public class HeadingBlockNode : BlockNode<HeadingBlock>
{
    /// <summary>
    /// Gets the heading container control.
    /// </summary>
    public override Control Control { get; }

    private readonly InlineCollectionNode<HeadingBlock> headingInlines;

    /// <summary>
    /// Initializes a heading block node.
    /// </summary>
    public HeadingBlockNode()
    {
        headingInlines = new InlineCollectionNode<HeadingBlock>();
        Control = new Border
        {
            Child = headingInlines.Control
        };
    }

    /// <summary>
    /// Updates heading inlines and applies the heading level classes.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="headingBlock">The Markdig heading block.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> when the heading remains valid.</returns>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        HeadingBlock headingBlock,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        if (headingBlock.Inline is null) return false;

        var result = headingInlines.Update(documentNode, headingBlock, change, cancellationToken);
        switch (result)
        {
            case true:
            {
                cancellationToken.ThrowIfCancellationRequested();
                Control.Classes.EnsureClassName("Heading", $"{headingBlock.Level}Block");
                headingInlines.Control.Classes.EnsureClassName("Heading", headingBlock.Level);
                return true;
            }
            case false:
            {
                return false;
            }
            case null: // Not dirty
            {
                return true;
            }
        }
    }
}