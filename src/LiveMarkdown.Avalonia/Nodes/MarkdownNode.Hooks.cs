using System.Collections.Immutable;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

public abstract partial class MarkdownNode
{
    /// <summary>
    /// Gets the process-wide node hook scope. Registrations do not keep participating
    /// renderers alive; dispose registration handles when their application scope ends.
    /// </summary>
    public static MarkdownNodeHookRegistry GlobalHooks { get; } = new();

    private NodeHookState? _hookState;
    private bool _isReleased;

    /// <summary>
    /// Ends this node's hook lifetime, including owned child nodes. Safe to call repeatedly.
    /// Ownership proxies call this on removal/replacement; it is not tied to visual detach.
    /// A later Update can begin a new lifetime for a reused node.
    /// </summary>
    public void Release()
    {
        if (_isReleased) return;

        _isReleased = true;
        Exception? error = null;
        try
        {
            ReleaseChildren();
        }
        catch (Exception exception)
        {
            error = exception;
        }

        var state = _hookState;
        _hookState = null;
        try
        {
            state?.Release();
        }
        catch (Exception exception)
        {
            NodeHookErrors.Add(ref error, exception);
        }

        NodeHookErrors.Throw(error);
    }

    /// <summary>
    /// Releases child nodes owned by a custom composite node. Override when retaining child
    /// MarkdownNodes outside the standard ownership proxies; release every child even if one fails.
    /// </summary>
    protected virtual void ReleaseChildren() { }

    internal void SetHookState(NodeHookState state)
    {
        _hookState = state;
    }

    internal void ClearHookState(NodeHookState state)
    {
        if (ReferenceEquals(_hookState, state)) _hookState = null;
    }

    private bool UpdateWithHooks(
        DocumentNode documentNode,
        MarkdownObject markdownObject,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        var owner = documentNode.Owner;
        if (owner.IsNodeHookPassActive)
        {
            return SynchronizeWithHooks(documentNode, markdownObject, change, cancellationToken);
        }

        // Renderer updates already own a pass. Only standalone node updates need a boundary here.
        var pass = owner.BeginNodeHookPass();
        var result = false;
        Exception? error = null;
        try
        {
            result = SynchronizeWithHooks(documentNode, markdownObject, change, cancellationToken);
        }
        catch (Exception exception)
        {
            error = exception;
        }

        pass.Complete(ref error);
        NodeHookErrors.Throw(error);
        return result;
    }

    private bool SynchronizeWithHooks(
        DocumentNode documentNode,
        MarkdownObject markdownObject,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        var owner = documentNode.Owner;
        if (!owner.HasActiveNodeHooks && _hookState is null)
        {
            return UpdateCore(documentNode, markdownObject, change, cancellationToken);
        }

        owner.Dispatcher.VerifyAccess();
        var actualType = GetType();
        var global = owner.ResolveGlobalHooks(actualType);
        var local = owner.ResolveLocalHooks(actualType);
        if (global.IsEmpty && local.IsEmpty && _hookState is null)
        {
            return UpdateCore(documentNode, markdownObject, change, cancellationToken);
        }

        if (_hookState is { } previous && !ReferenceEquals(previous.Renderer, owner)) previous.Release();
        var state = _hookState ??= new NodeHookState(this, owner);
        var context = new NodeBeforeUpdateContext<MarkdownNode>(
            new NodeHookContext<MarkdownNode>(this, owner),
            markdownObject,
            change,
            cancellationToken);

        var globalEntered = 0;
        var localEntered = 0;
        var hasPrepared = false;
        var result = false;
        Exception? error = null;
        try
        {
            state.Prepare(global, local);
            hasPrepared = true;
            InvokeBeforeUpdate(global, context, ref globalEntered);
            InvokeBeforeUpdate(local, context, ref localEntered);
            result = UpdateCore(documentNode, markdownObject, change, cancellationToken);
        }
        catch (Exception exception)
        {
            error = exception;
        }

        var updateResult = error switch
        {
            OperationCanceledException => NodeUpdateResult.Canceled,
            not null => NodeUpdateResult.Failed,
            _ => result ? NodeUpdateResult.Applied : NodeUpdateResult.Rejected
        };
        if (hasPrepared)
        {
            var completed = new NodeAfterUpdateContext<MarkdownNode>(context, updateResult, error);
            InvokeAfterUpdate(local, localEntered, completed, ref error);
            InvokeAfterUpdate(global, globalEntered, completed, ref error);
        }
        if (updateResult == NodeUpdateResult.Rejected)
        {
            try
            {
                Release();
            }
            catch (Exception exception)
            {
                NodeHookErrors.Add(ref error, exception);
            }
        }

        NodeHookErrors.Throw(error);
        return result;
    }

    private static void InvokeBeforeUpdate(
        ImmutableArray<NodeHookRegistration> registrations,
        NodeBeforeUpdateContext<MarkdownNode> context,
        ref int entered)
    {
        for (var index = 0; index < registrations.Length; index++)
        {
            // Include a throwing callback so its paired afterUpdate still gets a chance to unwind.
            entered = index + 1;
            registrations[index].BeforeUpdate?.Invoke(context);
        }
    }

    private static void InvokeAfterUpdate(
        ImmutableArray<NodeHookRegistration> registrations,
        int entered,
        NodeAfterUpdateContext<MarkdownNode> context,
        ref Exception? error)
    {
        for (var index = registrations.Length - 1; index >= 0; index--)
        {
            var registration = registrations[index];
            if (registration.AfterUpdate is not { } afterUpdate)
            {
                continue;
            }

            // Completion-only observers run even when an earlier beforeUpdate stopped entry.
            if (index >= entered && registration.BeforeUpdate is not null)
            {
                continue;
            }

            try
            {
                afterUpdate(context);
            }
            catch (Exception exception)
            {
                NodeHookErrors.Add(ref error, exception);
            }
        }
    }
}

internal sealed class NodeHookState(MarkdownNode node, MarkdownRenderer renderer)
{
    public MarkdownRenderer Renderer { get; } = renderer;

    private readonly List<NodeHookRegistration> _attached = [];
    private ImmutableArray<NodeHookRegistration> _globalRegistrations;
    private ImmutableArray<NodeHookRegistration> _localRegistrations;

    public void Prepare(ImmutableArray<NodeHookRegistration> global, ImmutableArray<NodeHookRegistration> local)
    {
        if (_globalRegistrations == global && _localRegistrations == local)
        {
            return;
        }

        // ImmutableArray equality compares backing-array identity, not registrations one by one.
        for (var index = _attached.Count - 1; index >= 0; index--)
        {
            var registration = _attached[index];
            if (!global.Contains(registration) && !local.Contains(registration))
            {
                Detach(registration);
            }
        }

        if (!global.IsEmpty)
        {
            MarkdownNode.GlobalHooks.Track(this);
        }

        if (!local.IsEmpty)
        {
            Renderer.LocalNodeHooks?.Track(this);
        }

        Attach(global);
        Attach(local);

        // Late global registrations still precede local ones when the lifetime unwinds.
        _attached.Clear();
        _attached.AddRange(global);
        _attached.AddRange(local);

        if (_attached.Count > 0)
        {
            node.SetHookState(this);
        }

        _globalRegistrations = global;
        _localRegistrations = local;
    }

    public void Detach(NodeHookRegistration registration)
    {
        if (!_attached.Remove(registration)) return;

        _globalRegistrations = default;
        _localRegistrations = default;

        if (_attached.Count == 0)
        {
            MarkdownNode.GlobalHooks.Untrack(this);
            Renderer.LocalNodeHooks?.Untrack(this);
            node.ClearHookState(this);
        }

        registration.Cleanup?.Invoke(new NodeHookContext<MarkdownNode>(node, Renderer));
    }

    public void Release()
    {
        Exception? error = null;
        while (_attached.Count > 0)
        {
            try
            {
                Detach(_attached[^1]);
            }
            catch (Exception exception)
            {
                NodeHookErrors.Add(ref error, exception);
            }
        }

        NodeHookErrors.Throw(error);
    }

    private void Attach(ImmutableArray<NodeHookRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            if (_attached.Contains(registration)) continue;
            _attached.Add(registration);
            registration.Initialize?.Invoke(new NodeHookContext<MarkdownNode>(node, Renderer));
        }
    }
}
