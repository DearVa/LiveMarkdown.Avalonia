using Avalonia.Controls;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A block node for Markdown blocks that are not yet implemented.
/// </summary>
/// <param name="markdownType">The Markdown block type represented by this node.</param>
public class NotImplementedBlockNode(Type markdownType) : BlockNode<Block>
{
    /// <summary>
    /// Gets the placeholder control used for the unsupported block.
    /// </summary>
    public override Control Control { get; } = new()
    {
        Classes = { "NotImplementedBlock" }
    };

    /// <inheritdoc/>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        Block block,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        return block.GetType() == markdownType;
    }
}