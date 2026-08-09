using Avalonia.Controls;
using Markdig.Extensions.Tables;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders one cell in a Markdown table.
/// </summary>
public sealed class TableCellNode : ContainerBlockNode<TableCell>
{
    /// <summary>
    /// Gets the bordered control that displays the cell.
    /// </summary>
    public override Control Control { get; }

    /// <summary>
    /// Initializes a new table cell node.
    /// </summary>
    public TableCellNode()
    {
        Control = new Border
        {
            Classes = { "TableCell" },
            Child = container
        };
    }
}