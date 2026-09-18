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
                Child = new Border
                {
                    Classes = { "TableContent" },
                    Child = container
                }
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

        var rows = table.OfType<TableRow>().ToArray();
        var lastRowIndex = rows.Length - 1;
        var cellIndex = 0;
        foreach (var (row, rowIndex) in rows.Select((r, i) => (r, i)))
        {
            var cells = row.OfType<TableCell>().ToArray();
            foreach (var (cell, columnIndex) in cells.Select((c, i) => (c, i)))
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
                            cellControl = oldCellBlockNode.Control;
                            break;
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

                if (columnIndex + cell.ColumnSpan >= table.ColumnDefinitions.Count)
                {
                    cellControl.Classes.Add("LastColumn");
                }
                else
                {
                    cellControl.Classes.Remove("LastColumn");
                }

                if (rowIndex == lastRowIndex)
                {
                    cellControl.Classes.Add("LastRow");
                }
                else
                {
                    cellControl.Classes.Remove("LastRow");
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

