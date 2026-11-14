using Avalonia;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Identifies a hook's node and owner without retaining a parsed Markdown document.
/// All hook callbacks run synchronously on the Avalonia UI thread.
/// </summary>
public readonly struct NodeHookContext<TNode> where TNode : MarkdownNode
{
    /// <summary>
    /// Gets the node matched by the registration, including derived node types.
    /// </summary>
    public TNode Node { get; }

    /// <summary>
    /// Gets the renderer owning the node.
    /// </summary>
    public MarkdownRenderer Renderer { get; }

    /// <summary>
    /// Gets the node's control or inline, or null for a node without either representation.
    /// The element need not be attached or measured yet.
    /// </summary>
    public StyledElement? Element => Node switch
    {
        BlockNode block => block.Control,
        InlineNode inline => inline.Inline,
        _ => null
    };

    internal NodeHookContext(TNode node, MarkdownRenderer renderer)
    {
        Node = node;
        Renderer = renderer;
    }

    internal NodeHookContext<T> Cast<T>() where T : MarkdownNode => new((T)(MarkdownNode)Node, Renderer);
}

/// <summary>
/// Describes the actual synchronization about to run. MarkdownObject is transient: do not
/// retain this context for a node's lifetime or use it from background work.
/// </summary>
public readonly struct NodeBeforeUpdateContext<TNode> where TNode : MarkdownNode
{
    /// <summary>
    /// Gets the matched node.
    /// </summary>
    public TNode Node => Context.Node;

    /// <summary>
    /// Gets the owning renderer.
    /// </summary>
    public MarkdownRenderer Renderer => Context.Renderer;

    /// <summary>
    /// Gets the control or inline represented by the node.
    /// </summary>
    public StyledElement? Element => Context.Element;

    /// <summary>
    /// Gets the node's lifecycle context.
    /// </summary>
    public NodeHookContext<TNode> Context { get; }

    /// <summary>
    /// Gets the current parsed Markdown object.
    /// </summary>
    public MarkdownObject MarkdownObject { get; }

    /// <summary>
    /// Gets the source change being applied.
    /// </summary>
    public ObservableStringBuilderChangedEventArgs Change { get; }

    /// <summary>
    /// Gets the synchronization cancellation token.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    internal NodeBeforeUpdateContext(
        NodeHookContext<TNode> context,
        MarkdownObject markdownObject,
        ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        Context = context;
        MarkdownObject = markdownObject;
        Change = change;
        CancellationToken = cancellationToken;
    }

    internal NodeBeforeUpdateContext<T> Cast<T>() where T : MarkdownNode => new(Context.Cast<T>(), MarkdownObject, Change, CancellationToken);
}

/// <summary>
/// Describes the result of the before-update callbacks and node synchronization.
/// </summary>
public enum NodeUpdateResult
{
    /// <summary>
    /// The node accepted and synchronized the Markdown object.
    /// </summary>
    Applied,

    /// <summary>
    /// The node rejected the object and must be replaced or removed.
    /// </summary>
    Rejected,

    /// <summary>
    /// Synchronization or a before-update callback threw an exception.
    /// </summary>
    Failed,

    /// <summary>
    /// Synchronization was canceled.
    /// </summary>
    Canceled
}

/// <summary>
/// Describes a completed update attempt, including failure and cancellation. Completion
/// callbacks still unwind when an earlier callback fails; their own exceptions propagate.
/// </summary>
public readonly struct NodeAfterUpdateContext<TNode> where TNode : MarkdownNode
{
    /// <summary>
    /// Gets the matched node.
    /// </summary>
    public TNode Node => Update.Node;

    /// <summary>
    /// Gets the owning renderer.
    /// </summary>
    public MarkdownRenderer Renderer => Update.Renderer;

    /// <summary>
    /// Gets the control or inline represented by the node.
    /// </summary>
    public StyledElement? Element => Update.Element;

    /// <summary>
    /// Gets the transient input to this update.
    /// </summary>
    public NodeBeforeUpdateContext<TNode> Update { get; }

    /// <summary>
    /// Gets the synchronization outcome before completion callbacks ran.
    /// </summary>
    public NodeUpdateResult Result { get; }

    /// <summary>
    /// Gets the original update error, or null for an applied or rejected result.
    /// </summary>
    public Exception? Exception { get; }

    internal NodeAfterUpdateContext(NodeBeforeUpdateContext<TNode> update, NodeUpdateResult result, Exception? exception)
    {
        Update = update;
        Result = result;
        Exception = exception;
    }

    internal NodeAfterUpdateContext<T> Cast<T>() where T : MarkdownNode => new(Update.Cast<T>(), Result, Exception);
}