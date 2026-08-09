using System.Runtime.CompilerServices;
using Avalonia.Controls.Documents;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Base class for nodes that render Markdig inline objects as Avalonia inlines.
/// </summary>
public abstract class InlineNode : MarkdownNode
{
    /// <summary>
    /// Gets the Avalonia inline rendered by this node.
    /// </summary>
    public abstract Inline Inline { get; }

    /// <summary>
    /// Creates and initializes a node for the specified Markdig inline.
    /// </summary>
    /// <param name="documentNode">The document that owns the inline.</param>
    /// <param name="inline">The Markdig inline to render.</param>
    /// <param name="change">The change that caused the update.</param>
    /// <param name="cancellationToken">A token that cancels node creation.</param>
    /// <returns>A node suitable for rendering <paramref name="inline"/>.</returns>
    protected static InlineNode CreateInlineNode(
        DocumentNode documentNode,
        Markdig.Syntax.Inlines.Inline inline,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        var type = inline.GetType();

        // First try to find an exact match, then try to find a compatible type
        var node = NodeFactories
                .OfType<IMarkdownNodeFactory<InlineNode>>()
                .Where(f => f.MarkdownType.IsAssignableFrom(type))
                .OrderBy(f => f)
                .Select(f => f.CreateNode())
                .FirstOrDefault()
            ?? new NotImplementedInlineNode(inline.GetType());

        node.Update(documentNode, inline, change, cancellationToken);
        return node;
    }
}

/// <summary>
/// Base class for inline nodes that handle a specific Markdig inline type.
/// </summary>
/// <typeparam name="TInline">The Markdig inline type handled by the node.</typeparam>
public abstract class InlineNode<TInline> : InlineNode where TInline : Markdig.Syntax.Inlines.Inline
{
    /// <inheritdoc/>
    protected override bool IsDirty(MarkdownObject markdownObject, in ObservableStringBuilderChangedEventArgs change)
    {
        return base.IsDirty(markdownObject, in change) ||
            markdownObject is not TInline inline ||
            !MatchesInline(inline);
    }

    /// <summary>
    /// Determines whether the given inline can be handled by this node.
    /// The default implementation requires an exact type match.
    /// </summary>
    /// <param name="inline">The inline to test.</param>
    /// <returns><see langword="true"/> when the inline can be handled; otherwise, <see langword="false"/>.</returns>
    protected virtual bool MatchesInline(TInline inline) => inline.GetType() == typeof(TInline);

    /// <inheritdoc/>
    protected sealed override bool UpdateCore(
        DocumentNode documentNode,
        MarkdownObject markdownObject,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        return markdownObject is TInline inline &&
            MatchesInline(inline) &&
            UpdateCore(documentNode, Unsafe.As<TInline>(markdownObject), change, cancellationToken);
    }

    /// <summary>
    /// Updates the rendered inline from a typed Markdig inline.
    /// </summary>
    /// <param name="documentNode">The document that owns the inline.</param>
    /// <param name="inline">The Markdig inline to render.</param>
    /// <param name="change">The change that caused the update.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns><see langword="true"/> when the inline was handled successfully.</returns>
    protected abstract bool UpdateCore(
        DocumentNode documentNode,
        TInline inline,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken);
}