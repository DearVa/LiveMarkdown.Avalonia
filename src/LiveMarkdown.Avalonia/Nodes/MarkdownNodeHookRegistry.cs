using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Avalonia.Threading;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// An ordered, UI-thread registration scope for Markdown node lifecycle callbacks.
/// Registrations match the specified node type and its derived types. Changes publish a
/// new snapshot; an in-progress document synchronization keeps its captured snapshot.
/// </summary>
public sealed class MarkdownNodeHookRegistry
{
    internal NodeHookSnapshot Snapshot { get; private set; } = NodeHookSnapshot.Empty;

    private static readonly object ParticipantMarker = new();
    private readonly ConditionalWeakTable<NodeHookState, object> _participants = new();
    private List<NodeHookRegistration>? _pendingDetach;
    private int _passCount;

    internal MarkdownNodeHookRegistry() { }

    /// <summary>
    /// Registers any combination of lifecycle callbacks as one disposable registration.
    /// <paramref name="initialize"/> runs once per node/registration pair before its first affected update.
    /// <paramref name="beforeUpdate"/> runs before synchronization; <paramref name="afterUpdate"/> unwinds
    /// in reverse order, including a callback whose beforeUpdate delegate threw. <paramref name="cleanup"/>
    /// runs on node release or unregistration. Visual-tree detach alone does not end this lifetime.
    /// </summary>
    /// <remarks>
    /// Register and dispose on the UI thread. Unregistration during a synchronization takes
    /// effect after active passes unwind. It does not implicitly re-render the document.
    /// Duplicate delegates are independent registrations. Callback exceptions propagate.
    /// </remarks>
    public IDisposable Register<TNode>(
        Action<NodeHookContext<TNode>>? initialize = null,
        Action<NodeBeforeUpdateContext<TNode>>? beforeUpdate = null,
        Action<NodeAfterUpdateContext<TNode>>? afterUpdate = null,
        Action<NodeHookContext<TNode>>? cleanup = null
    ) where TNode : MarkdownNode
    {
        Dispatcher.UIThread.VerifyAccess();
        if (initialize is null && beforeUpdate is null && afterUpdate is null && cleanup is null)
        {
            throw new ArgumentException("At least one node hook callback is required.");
        }

        var registration = new NodeHookRegistration(
            this,
            typeof(TNode),
            initialize is null ? null : context => initialize(context.Cast<TNode>()),
            beforeUpdate is null ? null : context => beforeUpdate(context.Cast<TNode>()),
            afterUpdate is null ? null : context => afterUpdate(context.Cast<TNode>()),
            cleanup is null ? null : context => cleanup(context.Cast<TNode>()));
        Snapshot = new NodeHookSnapshot(Snapshot.Registrations.Add(registration));
        return registration;
    }

    internal void EnterPass() => _passCount++;

    internal void ExitPass()
    {
        if (--_passCount != 0) return;

        var pending = _pendingDetach;
        _pendingDetach = null;
        if (pending is null) return;

        Exception? error = null;
        foreach (var registration in pending)
        {
            try
            {
                DetachRegistration(registration);
            }
            catch (Exception exception)
            {
                NodeHookErrors.Add(ref error, exception);
            }
        }

        NodeHookErrors.Throw(error);
    }

    internal void Track(NodeHookState state) => _participants.GetValue(state, static _ => ParticipantMarker);

    internal void Untrack(NodeHookState state) => _participants.Remove(state);

    internal void Unregister(NodeHookRegistration registration)
    {
        Dispatcher.UIThread.VerifyAccess();

        var index = Snapshot.Registrations.IndexOf(registration);
        if (index < 0) return;

        Snapshot = new NodeHookSnapshot(Snapshot.Registrations.RemoveAt(index));
        if (_passCount != 0)
        {
            (_pendingDetach ??= []).Add(registration);
            return;
        }

        DetachRegistration(registration);
    }

    private void DetachRegistration(NodeHookRegistration registration)
    {
        Exception? error = null;
        foreach (var participant in _participants)
        {
            try
            {
                participant.Key.Detach(registration);
            }
            catch (Exception exception)
            {
                NodeHookErrors.Add(ref error, exception);
            }
        }

        NodeHookErrors.Throw(error);
    }
}

internal sealed class NodeHookRegistration(
    MarkdownNodeHookRegistry owner,
    Type targetType,
    Action<NodeHookContext<MarkdownNode>>? initialize,
    Action<NodeBeforeUpdateContext<MarkdownNode>>? beforeUpdate,
    Action<NodeAfterUpdateContext<MarkdownNode>>? afterUpdate,
    Action<NodeHookContext<MarkdownNode>>? cleanup
) : IDisposable
{
    public Type TargetType { get; } = targetType;
    public Action<NodeHookContext<MarkdownNode>>? Initialize { get; } = initialize;
    public Action<NodeBeforeUpdateContext<MarkdownNode>>? BeforeUpdate { get; } = beforeUpdate;
    public Action<NodeAfterUpdateContext<MarkdownNode>>? AfterUpdate { get; } = afterUpdate;
    public Action<NodeHookContext<MarkdownNode>>? Cleanup { get; } = cleanup;

    private bool _isDisposed;

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_isDisposed) return;
        _isDisposed = true;
        owner.Unregister(this);
    }
}

internal sealed class NodeHookSnapshot(ImmutableArray<NodeHookRegistration> registrations)
{
    public static NodeHookSnapshot Empty { get; } = new([]);
    public ImmutableArray<NodeHookRegistration> Registrations { get; } = registrations;

    // Registration data is immutable; this derived cache is confined to the UI thread.
    private readonly Dictionary<Type, ImmutableArray<NodeHookRegistration>> _matchesByType = [];

    public ImmutableArray<NodeHookRegistration> Resolve(Type actualType)
    {
        if (Registrations.IsEmpty)
        {
            return [];
        }

        if (_matchesByType.TryGetValue(actualType, out var matches))
        {
            return matches;
        }

        var builder = ImmutableArray.CreateBuilder<NodeHookRegistration>();
        foreach (var registration in Registrations)
        {
            if (registration.TargetType.IsAssignableFrom(actualType))
            {
                builder.Add(registration);
            }
        }

        matches = builder.ToImmutable();
        _matchesByType.Add(actualType, matches);
        return matches;
    }
}

internal static class NodeHookErrors
{
    public static void Add(ref Exception? error, Exception exception)
    {
        error = error is null ? exception : new AggregateException(error, exception);
    }

    public static void Throw(Exception? error)
    {
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
