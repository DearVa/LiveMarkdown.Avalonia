using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Markdig.Extensions.Tables;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders a Markdown table in a horizontally scrollable container.
/// </summary>
public class TableNode : BlockNode<Table>
{
    /// <summary>
    /// Gets the scrollable control that displays the table.
    /// </summary>
    public override Control Control { get; }

    private readonly MarkdownRenderer.BlocksProxy proxy;

    /// <summary>
    /// Initializes a new table node.
    /// </summary>
    public TableNode()
    {
        var container = new MarkdownTableGrid();
        proxy = new MarkdownRenderer.BlocksProxy(container.Children);
        Control = new ScrollViewer
        {
            Classes = { "Table" },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border
            {
                Classes = { "Table" },
                Child = container
            }
        };
    }

    /// <inheritdoc/>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        Table table,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        if (table.ColumnDefinitions.Count == 0) return false;

        var cellIndex = 0;
        foreach (var (row, rowIndex) in table.OfType<TableRow>().Select((r, i) => (r, i)))
        {
            foreach (var (cell, columnIndex) in row.OfType<TableCell>().Select((c, i) => (c, i)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                Control cellControl;
                if (proxy.Count > cellIndex)
                {
                    // existing item block node, update it
                    var oldCellBlockNode = proxy[cellIndex];
                    var result = oldCellBlockNode.Update(documentNode, cell, change, cancellationToken);

                    switch (result)
                    {
                        case null: // Not dirty
                        {
                            cellIndex++;
                            continue;
                        }
                        case false: // remove the old node and create a new one if false
                        {
                            var newCellBlockNode = CreateBlockNode(documentNode, cell, change, cancellationToken);
                            proxy[cellIndex] = newCellBlockNode;
                            cellControl = newCellBlockNode.Control;
                            break;
                        }
                        default:
                        {
                            cellControl = oldCellBlockNode.Control;
                            break;
                        }
                    }

                }
                else
                {
                    var newCellBlockNode = CreateBlockNode(documentNode, cell, change, cancellationToken);
                    proxy.Add(newCellBlockNode);
                    cellControl = newCellBlockNode.Control;
                }

                cellIndex++;
                Grid.SetRow(cellControl, rowIndex);
                Grid.SetColumnSpan(cellControl, cell.ColumnSpan);
                Grid.SetColumn(cellControl, columnIndex);
                Grid.SetColumnSpan(cellControl, cell.ColumnSpan);

                if (row.IsHeader)
                {
                    if (!cellControl.Classes.Contains("Header"))
                    {
                        cellControl.Classes.Add("Header");
                    }
                }
                else
                {
                    cellControl.Classes.Remove("Header");
                }

                if (columnIndex >= table.ColumnDefinitions.Count) continue;
                if (cellControl is not Border { Child: { } child }) continue;
                var columnDefinition = table.ColumnDefinitions[columnIndex];
                child.HorizontalAlignment = columnDefinition.Alignment switch
                {
                    TableColumnAlign.Left => HorizontalAlignment.Left,
                    TableColumnAlign.Center => HorizontalAlignment.Center,
                    TableColumnAlign.Right => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Stretch
                };
            }
        }

        while (proxy.Count > cellIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            proxy.RemoveAt(proxy.Count - 1);
        }

        return cellIndex > 0;
    }
}