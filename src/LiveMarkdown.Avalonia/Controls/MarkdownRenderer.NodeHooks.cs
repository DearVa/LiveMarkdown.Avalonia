using System.Collections.Immutable;

namespace LiveMarkdown.Avalonia;

public partial class MarkdownRenderer
{
    /// <summary>
    /// Gets this renderer's node hook scope. Global hooks run before local hooks on entry;
    /// completion and node-release cleanup unwind in the reverse order.
    /// </summary>
    public MarkdownNodeHookRegistry NodeHooks => _nodeHooks ??= new MarkdownNodeHookRegistry();

    internal MarkdownNodeHookRegistry? LocalNodeHooks => _nodeHooks;
    internal bool IsNodeHookPassActive => _isNodeHookPassActive;
    internal bool HasActiveNodeHooks => !_globalHookSnapshot.Registrations.IsEmpty || !_localHookSnapshot.Registrations.IsEmpty;

    private MarkdownNodeHookRegistry? _nodeHooks;
    private MarkdownNodeHookRegistry? _passLocalNodeHooks;
    private NodeHookSnapshot _globalHookSnapshot = NodeHookSnapshot.Empty;
    private NodeHookSnapshot _localHookSnapshot = NodeHookSnapshot.Empty;
    private bool _isNodeHookPassActive;

    internal NodeHookPass BeginNodeHookPass()
    {
        if (_isNodeHookPassActive) return default;
        _isNodeHookPassActive = true;
        _passLocalNodeHooks = _nodeHooks;
        _globalHookSnapshot = MarkdownNode.GlobalHooks.Snapshot;
        _localHookSnapshot = _passLocalNodeHooks?.Snapshot ?? NodeHookSnapshot.Empty;
        MarkdownNode.GlobalHooks.EnterPass();
        _passLocalNodeHooks?.EnterPass();
        return new NodeHookPass(this);
    }

    internal ImmutableArray<NodeHookRegistration> ResolveGlobalHooks(Type actualType) => _globalHookSnapshot.Resolve(actualType);

    internal ImmutableArray<NodeHookRegistration> ResolveLocalHooks(Type actualType) => _localHookSnapshot.Resolve(actualType);

    private void EndNodeHookPass()
    {
        var local = _passLocalNodeHooks;
        _isNodeHookPassActive = false;
        _passLocalNodeHooks = null;
        _globalHookSnapshot = NodeHookSnapshot.Empty;
        _localHookSnapshot = NodeHookSnapshot.Empty;

        Exception? error = null;
        try
        {
            local?.ExitPass();
        }
        catch (Exception exception)
        {
            error = exception;
        }

        try
        {
            MarkdownNode.GlobalHooks.ExitPass();
        }
        catch (Exception exception)
        {
            NodeHookErrors.Add(ref error, exception);
        }

        NodeHookErrors.Throw(error);
    }

    internal readonly struct NodeHookPass(MarkdownRenderer renderer)
    {
        private readonly MarkdownRenderer? _renderer = renderer;

        public void Complete(ref Exception? error)
        {
            try
            {
                _renderer?.EndNodeHookPass();
            }
            catch (Exception exception)
            {
                NodeHookErrors.Add(ref error, exception);
            }
        }
    }
}