using Avalonia.Controls;
using Avalonia.Layout;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Base class for block nodes that contain and render child block nodes.
/// </summary>
/// <typeparam name="TContainerBlock">The Markdig container block type.</typeparam>
public abstract class ContainerBlockNode<TContainerBlock> : BlockNode<TContainerBlock> where TContainerBlock : ContainerBlock
{
    /// <summary>
    /// Gets the child container control.
    /// </summary>
    public override Control Control => container;

    /// <summary>
    /// The container that holds the child block nodes.
    /// </summary>
    protected readonly StackPanel container;

    /// <summary>
    /// The proxy that manages the child block nodes.
    /// </summary>
    protected readonly MarkdownRenderer.BlocksProxy proxy;

    /// <summary>
    /// Initializes the child control collection and synchronization proxy.
    /// </summary>
    protected ContainerBlockNode()
    {
        container = new StackPanel
        {
            Orientation = Orientation.Vertical
        };
        proxy = new MarkdownRenderer.BlocksProxy(container.Children);
    }

    /// <summary>
    /// Synchronizes child block nodes with the Markdig container.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="containerBlock">The Markdig container block.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> when the container remains valid.</returns>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        TContainerBlock containerBlock,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        if (containerBlock.Count == 0) return false;

        var i = 0;
        for (; i < containerBlock.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = containerBlock[i];

            if (i < proxy.Count)
            {
                var oldNode = proxy[i];
                if (oldNode.Update(documentNode, block, change, cancellationToken) is not false) continue;

                // if Update returned false, it means the block needs to be removed
                var newNode = CreateBlockNode(documentNode, block, change, cancellationToken);
                proxy[i] = newNode;
            }
            else
            {
                var newNode = CreateBlockNode(documentNode, block, change, cancellationToken);
                proxy.Add(newNode);
            }
        }

        for (var j = proxy.Count - 1; j >= i; j--)
        {
            cancellationToken.ThrowIfCancellationRequested();

            proxy.RemoveAt(j);
        }

        return true;
    }
}