using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Markdig;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class MarkdownSelectionScopeTests
{
    private HeadlessUnitTestSession Session => HeadlessSession.Current;

    [Test]
    public async Task AttachingRenderer_JoinsExistingSelection()
    {
        var result = await Session.Dispatch(() =>
        {
            var first = Renderer("first");
            var second = Renderer("second");
            var root = Scope(first);
            var window = new Window { Content = root };
            try
            {
                window.Show();
                first.SelectAll();
                root.Children.Add(second);
                var joined = second.CanCopy;
                second.ClearSelection();
                return (joined, first.CanCopy, second.CanCopy);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        Assert.That(result, Is.EqualTo((true, false, false)));
    }

    [Test]
    public async Task RemovingSelectedRenderer_UnregistersItsBlocks()
    {
        var result = await Session.Dispatch(() =>
        {
            var first = Renderer("first");
            var second = Renderer("second");
            var root = Scope(first, second);
            var window = new Window { Content = root };
            try
            {
                window.Show();
                var block = Block(first);
                block.SelectAll();
                var before = (first.CanCopy, second.CanCopy);
                root.Children.Remove(first);
                return (before, first.CanCopy, second.CanCopy, Released: block.SelectionScope is null);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        Assert.That(result, Is.EqualTo(((true, true), false, false, true)));
    }

    [Test]
    public async Task ReparentingSelectedRenderer_TransfersStateBetweenScopes()
    {
        var result = await Session.Dispatch(() =>
        {
            var moving = Renderer("moving");
            var oldPeer = Renderer("old");
            var newPeer = Renderer("new");
            var oldRoot = Scope(moving, oldPeer);
            var newRoot = Scope(newPeer);
            var window = new Window { Content = new StackPanel { Children = { oldRoot, newRoot } } };
            try
            {
                window.Show();
                Block(moving).SelectAll();
                oldRoot.Children.Remove(moving);
                newRoot.Children.Add(moving);
                return (oldPeer.CanCopy, newPeer.CanCopy, moving.CanCopy, Text: newPeer.SelectedText);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        Assert.That(result, Is.EqualTo((false, true, true, "moving")));
    }

    [Test]
    public async Task TogglingOuterScope_MergesAndSplitsAtOutermostMarker()
    {
        var result = await Session.Dispatch(() =>
        {
            var first = Renderer("first");
            var second = Renderer("second");
            var inner = Scope(first);
            var outer = new StackPanel { Children = { inner, second } };
            var window = new Window { Content = outer };
            try
            {
                window.Show();
                first.SelectAll();
                var separate = (first.CanCopy, second.CanCopy);
                MarkdownTextBlock.SetIsSelectionScope(outer, true);
                var merged = (first.CanCopy, second.CanCopy);
                MarkdownTextBlock.SetIsSelectionScope(outer, false);
                return (separate, merged, split: (first.CanCopy, second.CanCopy));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        Assert.That(result, Is.EqualTo(((true, false), (true, true), (true, false))));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HidingSelectedBranch_UpdatesSharedCanCopy(bool hideAncestor)
    {
        var result = await Session.Dispatch(() =>
        {
            var first = Renderer("first");
            var second = Renderer("second");
            var branch = new Border { Child = first };
            var root = Scope(branch, second);
            var window = new Window { Content = root };
            try
            {
                window.Show();
                Block(first).SelectAll();
                var hidden = hideAncestor ? (Control)branch : Block(first);
                hidden.IsVisible = false;
                var collapsed = (first.CanCopy, second.CanCopy, second.SelectedText);
                hidden.IsVisible = true;
                return (collapsed, restored: (first.CanCopy, second.CanCopy, second.SelectedText));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        Assert.That(result, Is.EqualTo(((false, false, ""), (true, true, "first"))));
    }

    [Test]
    public async Task ClearingDocument_RemovesOldSelection()
    {
        var result = await Session.Dispatch(() =>
        {
            var first = Renderer("first");
            var second = Renderer("second");
            var window = new Window { Content = Scope(first, second) };
            try
            {
                window.Show();
                var oldBlock = Block(first);
                oldBlock.SelectAll();
                first.DocumentUpdate = null;
                return (first.CanCopy, second.CanCopy, second.SelectedText, Released: oldBlock.SelectionScope is null);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        Assert.That(result, Is.EqualTo((false, false, "", true)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RemovingOrHidingBlockDuringDrag_ReleasesSnapshotAndAnchor(bool hide)
    {
        var result = await Session.Dispatch(() =>
        {
            var first = Renderer("first paragraph");
            var second = Renderer("second paragraph");
            var root = Scope(first, second);
            var window = new Window { Width = 600, Height = 400, Content = root };
            try
            {
                window.Show();
                var block = Block(first);
                var start = block.TranslatePoint(new Point(1, block.Bounds.Height / 2), window)!.Value;
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(start + new Vector(50, 0), RawInputModifiers.LeftMouseButton);
                var scope = MarkdownSelectionScope.Get(root);
                var active = scope.ActiveBlocks is not null && scope.Anchor is not null;
                if (hide) second.IsVisible = false;
                else root.Children.Remove(second);
                var released = scope.ActiveBlocks is null && scope.Anchor is null;
                window.MouseUp(start, MouseButton.Left);
                return (active, released, first.IsPointerSelectionDragging);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        Assert.That(result, Is.EqualTo((true, true, false)));
    }

    [Test]
    public async Task RemovingDetachedBranch_ReleasesScopeReferencesWithoutDetachEvents()
    {
        var result = await Session.Dispatch(() =>
        {
            var first = Renderer("first");
            var second = Renderer("second");
            var branch = new Border { Child = first };
            var root = Scope(branch, second);
            first.SelectAll();
            var block = Block(first);
            root.Children.Remove(branch);
            return (first.CanCopy, second.CanCopy, Released: block.SelectionScope is null, second.SelectedText);
        }, CancellationToken.None);
        Assert.That(result, Is.EqualTo((false, true, true, "second")));
    }

    [Test]
    public async Task DetachedCommands_KeepVisualOrderAndScopeSemantics()
    {
        var result = await Session.Dispatch(() =>
        {
            var first = Renderer("first");
            var second = Renderer("second");
            var root = Scope(first, second);
            first.SelectAll();
            var initial = (first.SelectedText, second.CanCopy);
            root.Children.Move(1, 0);
            var reordered = first.SelectedText;
            second.ClearSelection();
            return (initial, reordered, first.CanCopy, second.CanCopy);
        }, CancellationToken.None);
        Assert.That(result, Is.EqualTo((("first" + Environment.NewLine + "second", true),
            "second" + Environment.NewLine + "first", false, false)));
    }

    [Test]
    public async Task CanCopy_NotifiesOnlyOnSharedBooleanTransitions()
    {
        var result = await Session.Dispatch(() =>
        {
            var renderer = Renderer("first\n\nsecond");
            var window = new Window { Content = renderer };
            var notifications = 0;
            renderer.PropertyChanged += (_, e) =>
            {
                if (e.Property == MarkdownRenderer.CanCopyProperty) notifications++;
            };
            try
            {
                window.Show();
                renderer.SelectAll();
                renderer.SelectAll();
                renderer.ClearSelection();
                return notifications;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        Assert.That(result, Is.EqualTo(2));
    }

    private static MarkdownRenderer Renderer(string markdown) => new() { DocumentUpdate = Document(markdown) };
    private static MarkdownDocumentUpdate Document(string markdown) =>
        new MarkdownDocumentUpdate.Full(Markdown.Parse(markdown, MarkdownUpdateProducer.DefaultPipeline));
    private static MarkdownTextBlock Block(MarkdownRenderer renderer) =>
        renderer.GetVisualDescendants().OfType<MarkdownTextBlock>().Single();
    private static StackPanel Scope(params Control[] children)
    {
        var root = new StackPanel();
        foreach (var child in children) root.Children.Add(child);
        MarkdownTextBlock.SetIsSelectionScope(root, true);
        return root;
    }
}
