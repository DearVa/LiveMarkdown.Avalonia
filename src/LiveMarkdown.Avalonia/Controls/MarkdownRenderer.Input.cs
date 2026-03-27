using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace LiveMarkdown.Avalonia;

public partial class MarkdownRenderer
{
    /// <summary>
    /// Defines the <see cref="CanCopy"/> property.
    /// </summary>
    public static readonly DirectProperty<MarkdownRenderer, bool> CanCopyProperty =
        AvaloniaProperty.RegisterDirect<MarkdownRenderer, bool>(
            nameof(CanCopy),
            o => o.CanCopy);

    /// <summary>
    /// Defines the <see cref="CopyGesture"/> property.
    /// </summary>
    public static readonly StyledProperty<KeyGesture> CopyGestureProperty =
        AvaloniaProperty.Register<MarkdownRenderer, KeyGesture>(
            nameof(CopyGesture),
            new KeyGesture(Key.C, KeyModifiers.Control));

    /// <summary>
    /// Gets whether there is any text selected that can be copied.
    /// </summary>
    public bool CanCopy
    {
        get;
        private set => SetAndRaise(CanCopyProperty, ref field, value);
    }

    /// <summary>
    /// Gets or sets the keyboard gesture for the Copy command.
    /// </summary>
    public KeyGesture CopyGesture
    {
        get => GetValue(CopyGestureProperty);
        set => SetValue(CopyGestureProperty, value);
    }

    public string SelectedText
    {
        get
        {
            // Get all selected blocks in the current scope.
            // We fetch this dynamically to ensure consistency across Renderers.
            var scopeName = GetSelectionScopeName(this);
            var allBlocks = GetAllSelectableBlocksInScope(this, scopeName);

            var sb = new StringBuilder();
            var isFirst = true;

            foreach (var block in allBlocks.Where(b => !IsNestedBlock(b)))
            {
                var text = block.ActualSelectedText;
                if (string.IsNullOrEmpty(text)) continue;

                if (!isFirst) sb.AppendLine();
                sb.Append(text);
                isFirst = false;
            }

            return sb.ToString();
        }
    }
    
    /// <summary>
    /// All renderers that attached to the visual tree.
    /// </summary>
    private static readonly HashSet<MarkdownRenderer> AllRenderer = [];

    // Anchor point where the drag started (Block + Offset)
    private (MarkdownTextBlock Block, int Offset)? selectionAnchor;

    // Cache of all blocks in the current scope to avoid frequent VisualTree traversal during move.
    // Built on PointerPressed, cleared on PointerReleased.
    private List<MarkdownTextBlock>? activeScopeBlocks;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        AllRenderer.Add(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        AllRenderer.Remove(this);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.Handled)
        {
            base.OnPointerPressed(e);
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            base.OnPointerPressed(e);
            return;
        }

        // 1. Initialize Scope Context
        var scopeName = GetSelectionScopeName(this);
        activeScopeBlocks = GetAllSelectableBlocksInScope(this, scopeName).ToList();

        // 2. Find the "Root" MarkdownTextBlock that was hit.
        // If a nested block is hit, ResolveRootBlock helps us decide if we should start from the inner or outer.
        // However, for precise selection, we usually want the specific leaf block initially,
        // and then handle hierarchy during the selection update.
        // But here, we just find the visual hit.
        var targetBlock = FindNearestBlockInList(activeScopeBlocks, this, point.Position);
        if (targetBlock == null) // Truly nothing to select
        {
            base.OnPointerPressed(e);
            return;
        }

        e.Handled = true;
        Focus();

        // 3. Clear old selection
        ClearSelection(activeScopeBlocks);
        UpdateCanCopy();

        // 4. Calculate Anchor
        var position = GetCaretPosition(targetBlock, e);
        selectionAnchor = (targetBlock, position);

        // 5. Handle Click Selection (Single/Double/Triple)
        HandleClickSelection(targetBlock, position, e.ClickCount);

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (selectionAnchor == null || activeScopeBlocks == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            base.OnPointerMoved(e);
            return;
        }

        // 1. Find the target block under the cursor
        var currentPoint = e.GetPosition(this);
        // Use FindNearestBlock to ensure we track selection even if mouse leaves the exact bounds
        var targetBlock = FindNearestBlockInList(activeScopeBlocks, this, currentPoint);

        if (targetBlock == null)
        {
            base.OnPointerMoved(e);
            return;
        }

        // 2. Calculate Focus position
        var pointInBlock = this.TranslatePoint(currentPoint, targetBlock) ?? new Point();
        var focusOffset = targetBlock.TextLayout.HitTestPoint(pointInBlock).TextPosition;

        // 3. Update Selection Range
        UpdateSelectionRange(selectionAnchor.Value, (targetBlock, focusOffset));

        e.Handled = true;

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (selectionAnchor == null || e.InitialPressMouseButton != MouseButton.Left)
        {
            e.Source = this;
            base.OnPointerReleased(e);
            return;
        }

        selectionAnchor = null;
        activeScopeBlocks = null; // Release cache
        e.Handled = true;

        base.OnPointerReleased(e);
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        var keymap = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        if (keymap == null) return;

        if (Match(keymap.Copy))
        {
            Copy();
            e.Handled = true;
        }
        else if (Match(keymap.SelectAll))
        {
            e.Handled = true;
        }

        bool Match(List<KeyGesture> gestures) => gestures.Any(g => g.Matches(e));
    }

    public async void Copy()
    {
        if (TopLevel.GetTopLevel(this) is not { Clipboard: { } clipboard }) return;

        var text = SelectedText;
        if (string.IsNullOrEmpty(text)) return;

        await clipboard.SetTextAsync(text);
    }

    // -----------------------------------------------------------------------
    // Core Logic: Selection Range Calculation
    // -----------------------------------------------------------------------

    private void UpdateSelectionRange((MarkdownTextBlock Block, int Offset) anchor, (MarkdownTextBlock Block, int Offset) focus)
    {
        if (activeScopeBlocks == null) return;

        var (startNode, endNode) = OrderPositions(anchor, focus);

        foreach (var block in activeScopeBlocks)
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

        UpdateCanCopy();
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
        if (ComparePositions(block, 0, startNode.Block, startNode.Offset) >= 0) return 0;
        return block.EscapedTextLength;
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
            if (endNode.Offset > parentIndex) return block.EscapedTextLength;

            // If end is before the placeholder, this block is fully after end.
            return 0;
        }

        // Unrelated
        var textLength = block.EscapedTextLength;
        if (ComparePositions(block, textLength, endNode.Block, endNode.Offset) <= 0) return textLength;
        return 0;
    }

    private ((MarkdownTextBlock Block, int Offset) Start, (MarkdownTextBlock Block, int Offset) End) OrderPositions(
        (MarkdownTextBlock Block, int Offset) a,
        (MarkdownTextBlock Block, int Offset) b)
    {
        return ComparePositions(a.Block, a.Offset, b.Block, b.Offset) <= 0 ? (a, b) : (b, a);
    }

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
        var idxA = activeScopeBlocks!.IndexOf(blockA);
        var idxB = activeScopeBlocks!.IndexOf(blockB);
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

    /// <summary>
    /// Finds the nearest MarkdownTextBlock in the provided list to the given point.
    /// </summary>
    private static MarkdownTextBlock? FindNearestBlockInList(List<MarkdownTextBlock> blocks, Visual relativeTo, Point point)
    {
        if (blocks.Count == 0) return null;

        MarkdownTextBlock? bestBlock = null;
        var minDistance = double.MaxValue;

        // reverse order so that inner blocks are prioritized
        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            var block = blocks[i];

            var topLeft = block.TranslatePoint(new Point(0, 0), relativeTo) ?? new Point();
            var bottomRight = block.TranslatePoint(new Point(block.Bounds.Width, block.Bounds.Height), relativeTo) ?? new Point();
            var rect = new Rect(topLeft, bottomRight);

            var distance = GetDistanceToRect(point, rect);

            if (distance < minDistance)
            {
                minDistance = distance;
                bestBlock = block;
            }

            if (distance < 0.1) return block;
        }

        return bestBlock;
    }

    private static double GetDistanceToRect(Point p, Rect r)
    {
        var dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right);
        var dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static IEnumerable<MarkdownTextBlock> GetAllSelectableBlocksInScope(MarkdownRenderer current, string? scopeName)
    {
        // 1. Find all renderers in the same scope
        var renderers = AllRenderer
            .Where(r => GetSelectionScopeName(r) == scopeName)
            .ToList();

        // 2. If no renderers found, use the current one
        if (renderers.Count == 0) renderers.Add(current);

        // 3. Collect all MarkdownTextBlocks from the renderers
        // The order of blocks is important, so we traverse the visual tree in DFS order,
        // which ensures we get the correct hierarchy and selection context.
        foreach (var block in renderers.SelectMany(r => r.GetVisualDescendants().OfType<MarkdownTextBlock>()))
        {
            // We want ALL blocks, including nested ones, because we handle them in the logic
            yield return block;
        }
    }

    private static bool IsNestedBlock(MarkdownTextBlock child) => child.FindAncestorOfType<MarkdownTextBlock>() is not null;

    private static int GetCaretPosition(MarkdownTextBlock block, PointerEventArgs e)
    {
        var point = e.GetPosition(block);

        // Clamp point to block bounds to avoid HitTestPoint failures
        var x = Math.Clamp(point.X, 0, Math.Max(0, block.Bounds.Width));
        var y = Math.Clamp(point.Y, 0, Math.Max(0, block.Bounds.Height));

        return block.TextLayout.HitTestPoint(new Point(x, y)).TextPosition;
    }

    private void HandleClickSelection(MarkdownTextBlock block, int position, int clickCount)
    {
        var text = block.ActualText;
        var start = position;
        var end = position;

        switch (clickCount % 6)
        {
            // select word
            case 2:
            {
                if (!StringUtils.IsStartOfWord(text, position)) start = StringUtils.PreviousWord(text, position);
                if (!StringUtils.IsEndOfWord(text, position)) end = StringUtils.NextWord(text, position);
                break;
            }
            // select section
            case 3:
            {
                SelectAll(block.GetSelfAndVisualDescendants().OfType<MarkdownTextBlock>());
                UpdateCanCopy();
                return;
            }
            // select all
            case 4 when activeScopeBlocks is not null:
            {
                SelectAll(activeScopeBlocks);
                UpdateCanCopy();
                return;
            }
            // clear selection
            case 5 when activeScopeBlocks is not null:
            {
                ClearSelection(activeScopeBlocks);
                UpdateCanCopy();
                return;
            }
        }

        block.SetCurrentValue(SelectableTextBlock.SelectionStartProperty, start);
        block.SetCurrentValue(SelectableTextBlock.SelectionEndProperty, end);

        if (clickCount > 1)
        {
            selectionAnchor = (block, start);
        }

        UpdateCanCopy();
    }

    /// <summary>
    /// Selects all text in the current selection scope.
    /// </summary>
    public void SelectAll()
    {
        SelectAll(GetAllSelectableBlocksInScope(this, GetSelectionScopeName(this)));
        UpdateCanCopy();
    }

    /// <summary>
    /// Clears selection in the current selection scope.
    /// </summary>
    public void ClearSelection()
    {
        ClearSelection(GetAllSelectableBlocksInScope(this, GetSelectionScopeName(this)));
        UpdateCanCopy();
    }

    private static void SelectAll(IEnumerable<MarkdownTextBlock> blocks)
    {
        foreach (var block in blocks) block.SelectAll();
    }

    private static void ClearSelection(IEnumerable<MarkdownTextBlock> blocks)
    {
        foreach (var block in blocks) block.ClearSelection();
    }

    private void UpdateCanCopy()
    {
        CanCopy = !string.IsNullOrEmpty(SelectedText);
    }
}