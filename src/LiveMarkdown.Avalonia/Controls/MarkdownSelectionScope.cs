using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;

namespace LiveMarkdown.Avalonia;

/// <summary>Owns shared selection state; visual order is captured per operation.</summary>
internal sealed class MarkdownSelectionScope(Visual root)
{
    private static readonly AttachedProperty<MarkdownSelectionScope?> StateProperty =
        AvaloniaProperty.RegisterAttached<MarkdownRenderer, Visual, MarkdownSelectionScope?>("SelectionState");

    private readonly HashSet<MarkdownRenderer> renderers = [];
    private readonly HashSet<MarkdownTextBlock> selectedBlocks = [];
    private MarkdownRenderer? interactionOwner;
    private Dictionary<MarkdownTextBlock, int>? blockIndices;
    private int updateDepth;
    private bool canCopy;

    internal MarkdownTextBlock[]? ActiveBlocks { get; private set; }
    internal (MarkdownTextBlock Block, int Offset)? Anchor { get; set; }

    static MarkdownSelectionScope()
    {
        MarkdownTextBlock.IsSelectionScopeProperty.Changed.AddClassHandler<Visual>((visual, _) =>
        {
            foreach (var descendant in visual.GetSelfAndVisualDescendants())
            {
                switch (descendant)
                {
                    case MarkdownRenderer renderer:
                        renderer.RefreshSelectionScope();
                        break;
                    case MarkdownTextBlock block:
                        block.RefreshSelectionScope();
                        break;
                }
            }
        });
        Visual.IsVisibleProperty.Changed.AddClassHandler<Visual>((visual, _) =>
        {
            // Ancestor visibility changes do not change the descendants' own IsVisible properties.
            foreach (var block in visual.GetSelfAndVisualDescendants().OfType<MarkdownTextBlock>())
            {
                if (block.SelectionScope?.blockIndices?.ContainsKey(block) == true)
                {
                    block.SelectionScope.interactionOwner?.CancelSelectionInteraction();
                }
                block.SelectionScope?.UpdateSelection(block);
            }
        });
        Visual.VisualParentProperty.Changed.AddClassHandler<Visual>((visual, change) =>
        {
            // Unrooted trees have no detach callbacks, but commands are usable before attachment.
            if (change.OldValue is not Visual) return;

            foreach (var descendant in visual.GetSelfAndVisualDescendants())
            {
                switch (descendant)
                {
                    case MarkdownRenderer renderer:
                        renderer.ReleaseSelectionScopeIfOutsideRoot();
                        break;
                    case MarkdownTextBlock { SelectionScope: { } scope } block when !scope.Contains(block):
                        block.ReleaseSelectionScope();
                        break;
                }
            }
        });
    }

    internal static MarkdownSelectionScope Get(Visual root)
    {
        if (root.GetValue(StateProperty) is { } scope) return scope;
        scope = new MarkdownSelectionScope(root);
        root.SetValue(StateProperty, scope);
        return scope;
    }

    internal bool Contains(Visual visual) => IsVisualAncestor(root, visual);

    internal void Add(MarkdownRenderer renderer)
    {
        renderers.Add(renderer);
        renderer.SetCanCopy(canCopy);
    }

    internal void Remove(MarkdownRenderer renderer)
    {
        EndInteractionFor(renderer);
        renderers.Remove(renderer);
        renderer.SetCanCopy(false);
    }

    internal void UpdateSelection(MarkdownTextBlock block)
    {
        if (block.IsEffectivelyVisible && block.SelectionStart != block.SelectionEnd)
        {
            selectedBlocks.Add(block);
        }
        else
        {
            selectedBlocks.Remove(block);
        }
        Publish();
    }

    internal void Remove(MarkdownTextBlock block)
    {
        if (blockIndices?.ContainsKey(block) == true)
        {
            interactionOwner?.CancelSelectionInteraction();
        }
        selectedBlocks.Remove(block);
        Publish();
    }

    internal string GetSelectedText()
    {
        var text = new StringBuilder();
        foreach (var block in MarkdownRenderer.GetVisibleMarkdownTextBlockDescendants(root))
        {
            if (block.FindAncestorOfType<MarkdownTextBlock>() is not null) continue;
            var selected = block.ActualSelectedText;
            if (selected.Length == 0) continue;
            if (text.Length > 0) text.AppendLine();
            text.Append(selected);
        }
        return text.ToString();
    }

    private MarkdownTextBlock[] GetVisibleBlocks()
    {
        MarkdownTextBlock[] blocks = [.. MarkdownRenderer.GetVisibleMarkdownTextBlockDescendants(root)];
        // Detached renderers remain usable without requiring visual-tree attachment first.
        foreach (var block in blocks)
        {
            if (block.SelectionScope != this) block.RefreshSelectionScope();
        }
        return blocks;
    }

    internal void BeginInteraction(MarkdownRenderer owner)
    {
        interactionOwner?.CancelSelectionInteraction();
        ActiveBlocks = GetVisibleBlocks();
        blockIndices = new Dictionary<MarkdownTextBlock, int>(ActiveBlocks.Length);
        for (var i = 0; i < ActiveBlocks.Length; i++) blockIndices.Add(ActiveBlocks[i], i);
        interactionOwner = owner;
    }

    internal void EndInteractionFor(MarkdownRenderer owner)
    {
        if (interactionOwner != owner) return;
        interactionOwner = null;
        ActiveBlocks = null;
        blockIndices = null;
        Anchor = null;
    }

    // -----------------------------------------------------------------------
    // Core Logic: Selection Range Calculation
    // -----------------------------------------------------------------------

    internal void UpdateRange((MarkdownTextBlock Block, int Offset) anchor, (MarkdownTextBlock Block, int Offset) focus)
    {
        if (ActiveBlocks == null) return;
        using var update = BeginUpdate();

        var (startNode, endNode) = OrderPositions(anchor, focus);

        foreach (var block in ActiveBlocks)
        {
            var selectionStart = GetEffectiveStart(block, startNode);
            var selectionEnd = GetEffectiveEnd(block, endNode);

            if (selectionStart < selectionEnd)
            {
                block.SetCurrentValue(SelectableTextBlock.SelectionStartProperty, selectionStart);
                block.SetCurrentValue(SelectableTextBlock.SelectionEndProperty, selectionEnd);
            }
            else
            {
                block.ClearSelection();
            }
        }
    }

    private int GetEffectiveStart(MarkdownTextBlock block, (MarkdownTextBlock Block, int Offset) startNode)
    {
        if (block == startNode.Block) return startNode.Offset;

        // If block contains startNode (Parent of Start)
        var pIndex = GetPlaceholderIndex(block, startNode.Block);
        if (pIndex != -1) return pIndex + 1;

        // If startNode contains block (Child of Start)
        var parentIndex = GetPlaceholderIndex(startNode.Block, block);
        if (parentIndex != -1)
        {
            // If start is before the placeholder for this block, then this block is fully after start.
            if (startNode.Offset <= parentIndex) return 0;

            // If start is after the placeholder, this block is fully before start.
            return block.EscapedTextLength;
        }

        // Unrelated
        return ComparePositions(block, 0, startNode.Block, startNode.Offset) >= 0
            ? 0
            : block.EscapedTextLength;
    }

    private int GetEffectiveEnd(MarkdownTextBlock block, (MarkdownTextBlock Block, int Offset) endNode)
    {
        if (block == endNode.Block) return endNode.Offset;

        // If block contains endNode (Parent of End)
        var pIndex = GetPlaceholderIndex(block, endNode.Block);
        if (pIndex != -1) return pIndex;

        // If endNode contains block (Child of End)
        var parentIndex = GetPlaceholderIndex(endNode.Block, block);
        if (parentIndex != -1)
        {
            // If end is after the placeholder, this block is fully before end.
            return endNode.Offset > parentIndex
                ? block.EscapedTextLength
                // If end is before the placeholder, this block is fully after end.
                : 0;
        }

        // Unrelated
        var textLength = block.EscapedTextLength;
        return ComparePositions(block, textLength, endNode.Block, endNode.Offset) <= 0 ? textLength : 0;
    }

    private ((MarkdownTextBlock Block, int Offset) Start, (MarkdownTextBlock Block, int Offset) End) OrderPositions(
        (MarkdownTextBlock Block, int Offset) a,
        (MarkdownTextBlock Block, int Offset) b)
        => ComparePositions(a.Block, a.Offset, b.Block, b.Offset) <= 0 ? (a, b) : (b, a);

    private int ComparePositions(MarkdownTextBlock blockA, int offsetA, MarkdownTextBlock blockB, int offsetB)
    {
        if (blockA == blockB) return offsetA.CompareTo(offsetB);

        // Check hierarchy
        // If A contains B
        var placeholderInA = GetPlaceholderIndex(blockA, blockB);
        if (placeholderInA != -1)
        {
            return offsetA <= placeholderInA ? -1 : 1;
        }

        // If B contains A
        var placeholderInB = GetPlaceholderIndex(blockB, blockA);
        if (placeholderInB != -1)
        {
            return offsetB <= placeholderInB ? 1 : -1;
        }

        // Fallback to list index
        var idxA = blockIndices![blockA];
        var idxB = blockIndices![blockB];
        return idxA.CompareTo(idxB);
    }

    private static int GetPlaceholderIndex(MarkdownTextBlock parent, MarkdownTextBlock child)
    {
        if (parent.Inlines == null) return -1;

        var currentOffset = 0;
        if (parent.Inlines.Any(ProcessInlineOffset))
        {
            return currentOffset;
        }

        return -1;

        // Helper to process inlines recursively
        // returns true if child found
        bool ProcessInlineOffset(Inline inline)
        {
            switch (inline)
            {
                case InlineUIContainer { Child: Visual childVisual } when IsVisualAncestor(childVisual, child):
                    return true;
                case InlineUIContainer:
                    currentOffset++;
                    break;
                case Run run:
                    currentOffset += run.Text?.Length ?? 0;
                    break;
                case LineBreak:
                    currentOffset += Environment.NewLine.Length;
                    break;
                case Span span:
                {
                    if (span.Inlines.Any(ProcessInlineOffset)) return true;
                    break;
                }
            }

            return false;
        }
    }

    private static bool IsVisualAncestor(Visual? ancestor, Visual? target)
    {
        while (target != null)
        {
            if (target == ancestor) return true;
            target = target.GetVisualParent();
        }
        return false;
    }

    internal void SelectAll()
    {
        using var update = BeginUpdate();
        foreach (var block in GetVisibleBlocks()) block.SelectAll();
    }

    internal void ClearSelection()
    {
        using var update = BeginUpdate();
        foreach (var block in GetVisibleBlocks()) block.ClearSelection();
    }

    internal Update BeginUpdate()
    {
        updateDepth++;
        return new Update(this);
    }

    private void Publish()
    {
        var value = selectedBlocks.Count != 0;
        if (updateDepth != 0 || canCopy == value) return;
        canCopy = value;
        foreach (var renderer in renderers) renderer.SetCanCopy(value);
    }

    internal readonly struct Update(MarkdownSelectionScope scope) : IDisposable
    {
        public void Dispose()
        {
            scope.updateDepth--;
            scope.Publish();
        }
    }
}
