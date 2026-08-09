using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders one item in a Markdown list.
/// </summary>
public sealed class ListItemBlockNode : ContainerBlockNode<ListItemBlock>
{
    /// <summary>
    /// Initializes a new list item block node.
    /// </summary>
    public ListItemBlockNode()
    {
        container.Classes.Add("ListItemBlock");
    }
}