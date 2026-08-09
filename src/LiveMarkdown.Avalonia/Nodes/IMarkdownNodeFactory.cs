namespace LiveMarkdown.Avalonia;

/// <summary>
/// Describes a factory that creates a node for a Markdown object type.
/// </summary>
public interface IMarkdownNodeFactory
{
    /// <summary>
    /// Gets the Markdown type handled by the factory.
    /// </summary>
    Type MarkdownType { get; }
}

/// <summary>
/// Describes a factory that creates nodes of a specific type.
/// </summary>
/// <typeparam name="TNode">The node type created by the factory.</typeparam>
public interface IMarkdownNodeFactory<out TNode> : IMarkdownNodeFactory where TNode : MarkdownNode
{
    /// <summary>
    /// Creates a new node instance.
    /// </summary>
    /// <returns>A new node handled by the factory.</returns>
    TNode CreateNode();
}
