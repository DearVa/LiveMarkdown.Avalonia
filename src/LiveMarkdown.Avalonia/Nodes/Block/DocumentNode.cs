using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A node that represents the entire Markdown document (Root).
/// </summary>
public sealed class DocumentNode : ContainerBlockNode<MarkdownDocument>
{
    /// <summary>
    /// Gets the renderer that owns the Markdown document.
    /// </summary>
    public MarkdownRenderer Owner { get; }

    /// <summary>
    /// Initializes a document node for the specified renderer.
    /// </summary>
    /// <param name="owner">The owning renderer.</param>
    public DocumentNode(MarkdownRenderer owner)
    {
        Owner = owner;
        Control.Classes.Add("MarkdownDocument");
    }

    /// <remarks>
    /// This method always returns true because the DocumentNode is the root node and should always be considered dirty when any change occurs in the document.
    /// </remarks>
    /// <param name="markdownObject"></param>
    /// <param name="change"></param>
    /// <returns></returns>
    protected override bool IsDirty(MarkdownObject markdownObject, in ObservableStringBuilderChangedEventArgs change) => true;

    /// <summary>
    /// Updates the document's child block nodes and clears them when the document is empty.
    /// </summary>
    /// <param name="documentNode">The root document node.</param>
    /// <param name="markdownObject">The parsed Markdown document.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> when the document contains renderable blocks.</returns>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        MarkdownDocument markdownObject,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        var result = base.UpdateCore(documentNode, markdownObject, in change, cancellationToken);

        // (#1) DocumentNode is the outest node, so if it has no children, we clear the proxy
        if (!result) proxy.Clear();

        return result;
    }

    /// <summary>
    /// Removes all rendered block nodes from the document.
    /// </summary>
    public void Clear() => proxy.Clear();
}