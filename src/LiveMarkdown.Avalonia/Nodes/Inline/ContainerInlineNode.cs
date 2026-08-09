using Avalonia.Controls.Documents;
using Markdig.Syntax.Inlines;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A node that represents a container inline (e.g., Span).
/// </summary>
public class ContainerInlineNode<TContainerInline> : InlinesNode<TContainerInline> where TContainerInline : ContainerInline
{
    /// <summary>
    /// Initializes a new container inline node with an unstyled span.
    /// </summary>
    public ContainerInlineNode() : base(new Span())
    {
    }

    /// <summary>
    /// Initializes a new container inline node with the specified CSS-style class.
    /// </summary>
    /// <param name="className">The class to apply to the rendered span.</param>
    public ContainerInlineNode(string className) : base(new Span { Classes = { className }})
    {
    }
}