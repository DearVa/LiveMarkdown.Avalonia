namespace LiveMarkdown.Avalonia;

/// <summary>
/// A factory for creating MarkdownNode instances for a specific MarkdownObject type.
/// It implements IComparable to allow sorting based on type compatibility.
/// </summary>
/// <typeparam name="TNode"></typeparam>
public class MarkdownNodeFactory<TNode> : IMarkdownNodeFactory<TNode>, IComparable where TNode : MarkdownNode, new()
{
    /// <summary>
    /// Gets the Markdig type handled by <typeparamref name="TNode"/>.
    /// </summary>
    public Type MarkdownType { get; } =
        typeof(TNode).BaseType?.GetGenericArguments()[0] ??
        throw new InvalidOperationException($"Cannot determine MarkdownType for {typeof(TNode).FullName}");

    /// <summary>
    /// Creates a new node instance.
    /// </summary>
    /// <returns>A new <typeparamref name="TNode"/>.</returns>
    public TNode CreateNode() => new();

    /// <summary>
    /// Compares this factory with another factory by Markdown type specificity.
    /// </summary>
    /// <param name="other">The object to compare with.</param>
    /// <returns>A value indicating the relative ordering.</returns>
    public int CompareTo(object? other)
    {
        if (other is not IMarkdownNodeFactory otherFactory) return 1;
        if (ReferenceEquals(this, other)) return 0;
        if (MarkdownType == otherFactory.MarkdownType) return 0;
        if (MarkdownType.IsAssignableFrom(otherFactory.MarkdownType)) return 1;
        if (otherFactory.MarkdownType.IsAssignableFrom(MarkdownType)) return -1;
        return MarkdownType.FullName?.CompareTo(otherFactory.MarkdownType.FullName) ?? -1;
    }

    /// <summary>
    /// Determines whether another object represents the same Markdown type ordering.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns><see langword="true"/> when the objects compare equally.</returns>
    public override bool Equals(object? obj) => CompareTo(obj) == 0;

    /// <summary>
    /// Returns a hash code based on the handled Markdown type.
    /// </summary>
    /// <returns>The type hash code.</returns>
    public override int GetHashCode() => MarkdownType.GetHashCode();
}