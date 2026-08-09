using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Base class for nodes that render Markdig block objects as Avalonia controls.
/// </summary>
public abstract class BlockNode : MarkdownNode
{
    /// <summary>
    /// Gets the Avalonia control rendered by this block node.
    /// </summary>
    public abstract Control Control { get; }

    internal static bool HasMoreSpecificBlockNodeFactory(Type blockType, Type fallbackMarkdownType)
    {
        return NodeFactories
            .OfType<IMarkdownNodeFactory<BlockNode>>()
            .Any(factory =>
                factory.MarkdownType != fallbackMarkdownType &&
                fallbackMarkdownType.IsAssignableFrom(factory.MarkdownType) &&
                factory.MarkdownType.IsAssignableFrom(blockType));
    }

    /// <summary>
    /// Creates the most specific registered node for a Markdig block.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="block">The Markdig block to render.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel node creation.</param>
    /// <returns>A node capable of rendering the block.</returns>
    protected static BlockNode CreateBlockNode(
        DocumentNode documentNode,
        Block block,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        var type = block.GetType();

        // First try to find an exact match, then try to find a compatible type
        var node = NodeFactories
                .OfType<IMarkdownNodeFactory<BlockNode>>()
                .Where(f => f.MarkdownType.IsAssignableFrom(type))
                .OrderBy(f => f)
                .Select(f => f.CreateNode())
                .FirstOrDefault()
            ?? new NotImplementedBlockNode(block.GetType());

        node.Update(documentNode, block, change, cancellationToken);
        return node;
    }
}

/// <summary>
/// Base class for block nodes that handle a specific Markdig block type.
/// </summary>
/// <typeparam name="TBlock">The Markdig block type handled by the node.</typeparam>
public abstract class BlockNode<TBlock> : BlockNode where TBlock : Block
{
    /// <summary>
    /// Determines whether the block requires synchronization for the source change.
    /// </summary>
    /// <param name="markdownObject">The current Markdown object.</param>
    /// <param name="change">The source change being applied.</param>
    /// <returns><see langword="true"/> when the block is dirty.</returns>
    protected override bool IsDirty(MarkdownObject markdownObject, in ObservableStringBuilderChangedEventArgs change)
    {
        return base.IsDirty(markdownObject, in change) ||
            markdownObject is not TBlock block ||
            !MatchesBlock(block);
    }

    /// <summary>
    /// Determines whether the given block matches the type TBlock.
    /// Default implementation checks for exact type match.
    /// </summary>
    /// <param name="block"></param>
    /// <returns></returns>
    protected virtual bool MatchesBlock(TBlock block) => block.GetType() == typeof(TBlock);

    /// <inheritdoc/>
    protected sealed override bool UpdateCore(
        DocumentNode documentNode,
        MarkdownObject markdownObject,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        return markdownObject is TBlock block &&
            MatchesBlock(block) &&
            UpdateCore(documentNode, Unsafe.As<TBlock>(markdownObject), change, cancellationToken);
    }

    /// <summary>
    /// Updates the rendered control from a strongly typed Markdig block.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="block">The Markdig block to render.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> when the block remains valid.</returns>
    protected abstract bool UpdateCore(
        DocumentNode documentNode,
        TBlock block,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken);
}