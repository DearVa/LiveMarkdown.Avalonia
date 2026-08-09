using Avalonia.Controls;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A node that represents a thematic break (horizontal rule).
/// </summary>
public class ThematicBreakBlockNode : BlockNode<ThematicBreakBlock>
{
    /// <summary>
    /// Gets the border used to display the horizontal rule.
    /// </summary>
    public override Control Control { get; } = new Border
    {
        Classes = { "ThematicBreak" }
    };

    /// <inheritdoc/>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        ThematicBreakBlock thematicBreakBlock,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        return true;
    }
}