namespace LiveMarkdown.Avalonia;

/// <summary>
/// Provides a builder for configuring the pipeline of Markdown node factories in Fluent style. This allows for easy registration and unregistration of node factories, enabling customization of the Markdown parsing process.
/// </summary>
public readonly ref struct MarkdownNodePipelineBuilder
{
    public HashSet<IMarkdownNodeFactory> RegisteredNodeFactories { get; }

    internal MarkdownNodePipelineBuilder(HashSet<IMarkdownNodeFactory> registeredNodeFactories)
    {
        RegisteredNodeFactories = registeredNodeFactories;
    }

    /// <summary>
    /// Registers a single node factory to the pipeline. This allows for custom node factories to be added to the pipeline, enabling support for custom Markdown syntax or behavior.
    /// </summary>
    /// <typeparam name="TNode"></typeparam>
    /// <param name="factory"></param>
    /// <returns></returns>
    public MarkdownNodePipelineBuilder Register<TNode>(IMarkdownNodeFactory<TNode> factory) where TNode : MarkdownNode
    {
        RegisteredNodeFactories.Add(factory);
        return this;
    }

    /// <summary>
    /// Registers a new instance of a node factory to the pipeline. This uses the standard <see cref="MarkdownNodeFactory{TNode}"/> implementation to create a factory based on the generic type parameter.
    /// </summary>
    /// <typeparam name="TNode"></typeparam>
    /// <returns></returns>
    public MarkdownNodePipelineBuilder Register<TNode>() where TNode : MarkdownNode, new()
    {
        RegisteredNodeFactories.Add(new MarkdownNodeFactory<TNode>());
        return this;
    }

    /// <summary>
    /// Unregisters a node factory from the pipeline. This allows for custom node factories to be removed from the pipeline, enabling support for custom Markdown syntax or behavior.
    /// </summary>
    /// <param name="factory"></param>
    /// <returns></returns>
    public MarkdownNodePipelineBuilder Unregister(IMarkdownNodeFactory factory)
    {
        RegisteredNodeFactories.Remove(factory);
        return this;
    }

    /// <summary>
    /// Unregisters a node factory of the specified type from the pipeline. This uses the standard <see cref="MarkdownNodeFactory{TNode}"/> implementation to identify the factory to remove based on the generic type parameter.
    /// </summary>
    public MarkdownNodePipelineBuilder Unregister<TNode>() where TNode : MarkdownNode, new()
    {
        RegisteredNodeFactories.RemoveWhere(f => f is MarkdownNodeFactory<TNode>);
        return this;
    }

    /// <summary>
    /// Unregisters node factories from the pipeline based on a predicate. This allows for more complex logic to determine which factories to remove, such as removing all factories that handle a certain type of Markdown object.
    /// </summary>
    /// <param name="predicate"></param>
    /// <returns></returns>
    public MarkdownNodePipelineBuilder UnregisterWhere(Predicate<IMarkdownNodeFactory> predicate)
    {
        RegisteredNodeFactories.RemoveWhere(predicate);
        return this;
    }
}