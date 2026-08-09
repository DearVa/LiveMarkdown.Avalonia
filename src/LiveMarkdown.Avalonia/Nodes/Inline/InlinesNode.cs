using Avalonia.Controls.Documents;
using Inline = Markdig.Syntax.Inlines.Inline;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A node that contains multiple inline nodes (Like a Span or InlineHyperlink).
/// </summary>
public class InlinesNode<TInline> : InlineNode<TInline> where TInline : Inline
{
    /// <summary>
    /// Gets the Avalonia inline represented by this node.
    /// </summary>
    public override global::Avalonia.Controls.Documents.Inline Inline { get; }

    /// <summary>
    /// Gets the collection that contains child Avalonia inlines.
    /// </summary>
    public InlineCollection Inlines { get; }

    private readonly MarkdownRenderer.InlinesProxy proxy;

    /// <summary>
    /// Initializes a node backed by the supplied span.
    /// </summary>
    /// <param name="span">The Avalonia span to synchronize.</param>
    public InlinesNode(Span span) : this(span, span.Inlines) { }

    private InlinesNode(global::Avalonia.Controls.Documents.Inline inline, InlineCollection inlines)
    {
        Inline = inline;
        Inlines = inlines;
        proxy = new MarkdownRenderer.InlinesProxy(inlines);
    }

    /// <summary>
    /// Synchronizes child inline nodes with the Markdig inline container.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="inlines">The Markdig inline container.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> when at least one inline was processed.</returns>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        TInline inlines,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        var i = -1;
        foreach (var inline in (IEnumerable<Inline>)inlines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Add new inline
            if (proxy.Count > ++i)
            {
                // Update existing inline
                var oldInlineNode = proxy[i];

                // if Update returned true, it means the block was updated successfully
                if (oldInlineNode.Update(documentNode, inline, change, cancellationToken) is not false) continue;

                // else, remove the old node and create a new one
                var newInlineNode = CreateInlineNode(documentNode, inline, change, cancellationToken);
                proxy[i] = newInlineNode;
            }
            else
            {
                var newInlineNode = CreateInlineNode(documentNode, inline, change, cancellationToken);
                proxy.Add(newInlineNode);
            }
        }

        while (proxy.Count > i + 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            proxy.RemoveAt(proxy.Count - 1);
        }

        return i >= 0; // Return true if at least one inline was processed
    }
}