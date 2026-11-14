using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class NodeHookTests
{
    [Test]
    public Task Register_BaseAndDerivedTypes_EnterGlobalFirstAndUnwindLocalFirst() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var document = new DocumentNode(renderer);
        var node = new LinkInlineNode();
        var calls = new List<string>();
        using var global = MarkdownNode.GlobalHooks.Register<InlinesNode<Markdig.Syntax.Inlines.LinkInline>>(
            initialize: _ => calls.Add("global attached"), beforeUpdate: _ => calls.Add("global before"),
            afterUpdate: _ => calls.Add("global after"), cleanup: _ => calls.Add("global detached"));
        using var local = renderer.NodeHooks.Register<LinkInlineNode>(
            initialize: context =>
            {
                Assert.That(context.Node, Is.SameAs(node));
                Assert.That(context.Renderer, Is.SameAs(renderer));
                Assert.That(context.Element, Is.SameAs(node.Inline));
                calls.Add("local attached");
            }, beforeUpdate: _ => calls.Add("local before"),
            afterUpdate: _ => calls.Add("local after"), cleanup: _ => calls.Add("local detached"));
        UpdateLink(node, document, "[one](https://example.com)");
        node.Release();
        node.Release();
        Assert.That(calls, Is.EqualTo(new[] { "global attached", "local attached", "global before", "local before",
            "local after", "global after", "local detached", "global detached" }));
    });

    [Test]
    public Task InlineIcon_StreamingAndUnregister_PreserveTextIdentityAndWrapping() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var icons = new Dictionary<LinkInlineNode, InlineUIContainer>();
        var detachedCount = 0;
        var registration = renderer.NodeHooks.Register<LinkInlineNode>(
            initialize: context => icons.Add(context.Node, new InlineUIContainer(new Border
            {
                Width = 16, Height = 16, Background = Brushes.SkyBlue, IsHitTestVisible = false
            })),
            beforeUpdate: context => context.Node.Inlines.Remove(icons[context.Node]),
            afterUpdate: context =>
            {
                if (context.Result == NodeUpdateResult.Applied) context.Node.Inlines.Insert(0, icons[context.Node]);
            },
            cleanup: context =>
            {
                context.Node.Inlines.Remove(icons[context.Node]);
                icons.Remove(context.Node);
                detachedCount++;
            });
        Render(renderer, "Read [a **bold** link](https://example.com) after.");
        var node = icons.Keys.Single();
        var icon = icons[node];
        var link = (Link)node.Inline;
        var window = new Window { Width = 180, Height = 240, Content = renderer };
        window.Show();
        try
        {
            Render(renderer, "Read [a **longer bold** link with more words](https://example.org) after.");
            window.UpdateLayout();
            Assert.That(icons.Keys.Single(), Is.SameAs(node));
            Assert.That(node.Inlines[0], Is.SameAs(icon));
            Assert.That(node.Inlines.OfType<InlineUIContainer>().Count(), Is.EqualTo(1));
            Assert.That(link.HRef?.Host, Is.EqualTo("example.org"));
            var block = renderer.GetLogicalDescendants().OfType<MarkdownTextBlock>().Single();
            Assert.That(block.ActualText, Is.EqualTo("Read a longer bold link with more words after."));
            block.SelectAll();
            Assert.That(block.ActualSelectedText, Is.EqualTo(block.ActualText));
            window.UpdateLayout();
            var geometry = block.GetInlineGeometry(link) ?? throw new AssertionException("Expected link geometry.");
            Assert.That(geometry.Rects.Count, Is.GreaterThan(1));
            Assert.That(geometry.Length, Is.EqualTo("a longer bold link with more words".Length + 1));
            Assert.That(block.GetLinkAt(geometry.Rects[0].Center), Is.SameAs(link));
            var match = renderer.ApplyTextSearch("longer").Single();
            Assert.That(match.Range.Start, Is.EqualTo(block.LayoutText.IndexOf("longer", StringComparison.Ordinal)));
            registration.Dispose();
            registration.Dispose();
            Assert.That(detachedCount, Is.EqualTo(1));
            Assert.That(icons, Is.Empty);
            Assert.That(node.Inlines.Contains(icon), Is.False);
            Render(renderer, "Read [plain updated link](https://example.com) after.");
            Assert.That(block.ActualText, Is.EqualTo("Read plain updated link after."));
        }
        finally
        {
            registration.Dispose();
            window.Close();
        }
    });

    [Test]
    public Task RegistrationChangedDuringPass_UsesOneSnapshotForTheWholeDocument() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var calls = new List<string>();
        IDisposable? replacement = null;
        var original = renderer.NodeHooks.Register<LinkInlineNode>(
            initialize: _ => calls.Add("old attached"), afterUpdate: _ => calls.Add("old after"),
            cleanup: _ => calls.Add("old detached"));
        using var mutator = renderer.NodeHooks.Register<DocumentNode>(beforeUpdate: _ =>
        {
            original.Dispose();
            replacement ??= renderer.NodeHooks.Register<LinkInlineNode>(afterUpdate: _ => calls.Add("new after"));
        });
        try
        {
            Render(renderer, "[first](https://example.com) and [second](https://example.org)");
            Assert.That(calls, Is.EqualTo(new[] { "old attached", "old after", "old attached", "old after", "old detached", "old detached" }));
            calls.Clear();
            Render(renderer, "[changed first](https://example.com) and [changed second](https://example.org)");
            Assert.That(calls, Is.EqualTo(new[] { "new after", "new after" }));
        }
        finally { replacement?.Dispose(); }
    });

    [Test]
    public Task LocalScopeCreatedDuringGlobalHook_DoesNotJoinTheCurrentPass() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var count = 0;
        IDisposable? local = null;
        using var global = MarkdownNode.GlobalHooks.Register<DocumentNode>(beforeUpdate: _ =>
            local ??= renderer.NodeHooks.Register<LinkInlineNode>(afterUpdate: _ => count++));
        try
        {
            Render(renderer, "[first](https://example.com)");
            Assert.That(count, Is.Zero);
            Render(renderer, "[second](https://example.com)");
            Assert.That(count, Is.EqualTo(1));
        }
        finally { local?.Dispose(); }
    });

    [Test]
    public Task DuplicateDelegates_DisposeOneHandle_LeavesTheOtherRegistered() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var count = 0;
        var callback = (NodeAfterUpdateContext<DocumentNode> _) => { count++; };
        using var first = renderer.NodeHooks.Register(afterUpdate: callback);
        using var second = renderer.NodeHooks.Register(afterUpdate: callback);
        Render(renderer, "one");
        first.Dispose();
        Render(renderer, "two");
        Assert.That(count, Is.EqualTo(3));
    });

    [Test]
    public Task GlobalHookRegisteredLater_StillUnwindsAfterLocalHooks() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var document = new DocumentNode(renderer);
        var node = new LinkInlineNode();
        var calls = new List<string>();
        using var local = renderer.NodeHooks.Register<LinkInlineNode>(cleanup: _ => calls.Add("local"));
        UpdateLink(node, document, "[one](https://example.com)");
        using var global = MarkdownNode.GlobalHooks.Register<LinkInlineNode>(cleanup: _ => calls.Add("global"));
        UpdateLink(node, document, "[two](https://example.com)");
        node.Release();
        Assert.That(calls, Is.EqualTo(new[] { "local", "global" }));
    });

    [Test]
    public Task SameRendererDocumentChangedFromHook_RejectsReentrancyAndRemainsUsable() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var hook = renderer.NodeHooks.Register<DocumentNode>(beforeUpdate: _ => Render(renderer, "nested"));
        try
        {
            Assert.Throws<InvalidOperationException>(() => Render(renderer, "outer"));
        }
        finally { hook.Dispose(); }
        Render(renderer, "recovered");
        Assert.That(renderer.GetLogicalDescendants().OfType<MarkdownTextBlock>().Single().ActualText, Is.EqualTo("recovered"));
    });

    [Test]
    public Task UnchangedNode_SkipsHooksUntilAffected_AndAttachesOnlyOnce() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var document = new DocumentNode(renderer);
        var node = new LinkInlineNode();
        var attachedCount = 0;
        var updateCount = 0;
        using var hook = renderer.NodeHooks.Register<LinkInlineNode>(initialize: _ => attachedCount++, beforeUpdate: _ => updateCount++);
        UpdateLink(node, document, "[one](https://example.com)");
        var markdown = Markdown.Parse("[one](https://example.com)");
        var link = ((ParagraphBlock)markdown[0]).Inline?.FirstChild ?? throw new AssertionException("Expected link.");
        Assert.That(node.Update(document, link, new ObservableStringBuilderChangedEventArgs(100, 2, 102, 2), CancellationToken.None), Is.Null);
        UpdateLink(node, document, "[two](https://example.com)");
        Assert.That(attachedCount, Is.EqualTo(1));
        Assert.That(updateCount, Is.EqualTo(2));
        node.Release();
    });

    [TestCase("# [heading](https://example.com)\n\n> [quote](https://example.com)")]
    [TestCase("- [one](https://example.com)\n- [two](https://example.com)")]
    [TestCase("| Title |\n| --- |\n| [cell](https://example.com) |")]
    public Task Clear_CompositeOwners_ReleaseEveryAttachedNodeAndCanRenderAgain(string markdown) => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var active = new HashSet<MarkdownNode>();
        using var hook = renderer.NodeHooks.Register<MarkdownNode>(
            initialize: context => Assert.That(active.Add(context.Node), Is.True),
            cleanup: context => Assert.That(active.Remove(context.Node), Is.True));
        Render(renderer, markdown);
        Assert.That(active.OfType<LinkInlineNode>(), Is.Not.Empty);
        renderer.DocumentUpdate = null;
        Assert.That(active, Is.Empty);
        Render(renderer, markdown);
        renderer.DocumentUpdate = null;
        Assert.That(active, Is.Empty);
    });

    [Test]
    public Task ReplacementAndEmptyDocument_ReleaseObsoleteSubtreesAfterCompletion() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var active = new HashSet<MarkdownNode>();
        var calls = new List<string>();
        using var hook = renderer.NodeHooks.Register<MarkdownNode>(
            initialize: context => active.Add(context.Node),
            afterUpdate: context =>
            {
                if (context.Node is DocumentNode && context.Result == NodeUpdateResult.Rejected) calls.Add("completed");
            },
            cleanup: context =>
            {
                active.Remove(context.Node);
                if (context.Node is DocumentNode) calls.Add("detached");
            });
        Render(renderer, "[link](https://example.com)");
        var oldLink = active.OfType<LinkInlineNode>().Single();
        Render(renderer, "# heading");
        Assert.That(active.Contains(oldLink), Is.False);
        Render(renderer, "");
        Assert.That(active, Is.Empty);
        Assert.That(calls, Is.EqualTo(new[] { "completed", "detached" }));
    });

    [Test]
    public Task VisualDetachAndReattach_DoNotEndNodeLifetime() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var detachedCount = 0;
        using var hook = renderer.NodeHooks.Register<LinkInlineNode>(cleanup: _ => detachedCount++);
        Render(renderer, "[link](https://example.com)");
        var window = new Window { Width = 300, Height = 200, Content = renderer };
        window.Show();
        try
        {
            window.Content = null;
            window.Content = renderer;
            Assert.That(detachedCount, Is.Zero);
            renderer.DocumentUpdate = null;
            Assert.That(detachedCount, Is.EqualTo(1));
        }
        finally { window.Close(); }
    });

    [Test]
    public Task UpdatingThrows_UnwindsOnlyEnteredPairsAndPreservesCleanupErrors() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var document = new DocumentNode(renderer);
        var node = new LinkInlineNode();
        var calls = new List<string>();
        using var first = renderer.NodeHooks.Register<LinkInlineNode>(
            beforeUpdate: _ => calls.Add("first before"), afterUpdate: context =>
            {
                calls.Add("first after");
                Assert.That(context.Result, Is.EqualTo(NodeUpdateResult.Failed));
                Assert.That(context.Exception?.Message, Is.EqualTo("update"));
                throw new InvalidOperationException("completion");
            });
        IDisposable? throwing = null;
        throwing = renderer.NodeHooks.Register<LinkInlineNode>(beforeUpdate: _ =>
        {
            calls.Add("throwing before");
            throwing?.Dispose();
            throw new InvalidOperationException("update");
        }, afterUpdate: _ => calls.Add("throwing after"), cleanup: _ => throw new InvalidOperationException("detach"));
        using var last = renderer.NodeHooks.Register<LinkInlineNode>(
            beforeUpdate: _ => calls.Add("last before"), afterUpdate: _ => calls.Add("last after"));
        try
        {
            var exception = Assert.Throws<AggregateException>(() => UpdateLink(node, document, "[one](https://example.com)"));
            Assert.That(exception?.Flatten().InnerExceptions.Select(error => error.Message), Is.EquivalentTo(new[] { "update", "completion", "detach" }));
            Assert.That(calls, Is.EqualTo(new[] { "first before", "throwing before", "throwing after", "first after" }));
        }
        finally { throwing.Dispose(); node.Release(); }
    });

    [Test]
    public Task AttachedThrows_NewNodeBeforeInsertion_IsReleased() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var active = new HashSet<LinkInlineNode>();
        using var hook = renderer.NodeHooks.Register<LinkInlineNode>(initialize: context =>
        {
            active.Add(context.Node);
            throw new InvalidOperationException("attach");
        }, cleanup: context => active.Remove(context.Node));
        Assert.Throws<InvalidOperationException>(() => Render(renderer, "[one](https://example.com)"));
        Assert.That(active, Is.Empty);
        renderer.DocumentUpdate = null;
    });

    [Test]
    public Task GlobalDispose_ReleasesParticipantsAcrossRenderersImmediately() => Run(() =>
    {
        var first = new MarkdownRenderer();
        var second = new MarkdownRenderer();
        var active = new HashSet<LinkInlineNode>();
        using var global = MarkdownNode.GlobalHooks.Register<LinkInlineNode>(
            initialize: context => active.Add(context.Node), cleanup: context => active.Remove(context.Node));
        Render(first, "[first](https://example.com)");
        Render(second, "[second](https://example.com)");
        Assert.That(active.Count, Is.EqualTo(2));
        global.Dispose();
        Assert.That(active, Is.Empty);
    });

    [Test]
    public Task CanceledUpdate_ReportsCancellationAndStillCompletes() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var node = new LinkInlineNode();
        var document = new DocumentNode(renderer);
        NodeUpdateResult? outcome = null;
        using var hook = renderer.NodeHooks.Register<LinkInlineNode>(afterUpdate: context => outcome = context.Result);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => UpdateLink(node, document, "[one](https://example.com)", cancellation.Token));
        Assert.That(outcome, Is.EqualTo(NodeUpdateResult.Canceled));
        node.Release();
    });

    [Test]
    public Task Clear_WhenOneDetachedCallbackThrows_StillReleasesSiblingsAndRemovesControls() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var active = new HashSet<LinkInlineNode>();
        using var hook = renderer.NodeHooks.Register<LinkInlineNode>(initialize: context => active.Add(context.Node), cleanup: context =>
        {
            active.Remove(context.Node);
            throw new InvalidOperationException("detach");
        });
        Render(renderer, "[one](https://example.com)\n\n[two](https://example.com)");
        Assert.Throws<AggregateException>(() => renderer.DocumentUpdate = null);
        Assert.That(active, Is.Empty);
        Assert.That(renderer.GetLogicalDescendants().OfType<MarkdownTextBlock>(), Is.Empty);
    });

    [TestCase(true)]
    [TestCase(false)]
    public Task BeforeUpdateThrows_InterleavedObserversAndPairs_UnwindInReverseOrder(bool shouldFailGlobal) => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var document = new DocumentNode(renderer);
        var node = new LinkInlineNode();
        var calls = new List<string>();
        using var globalObserver = MarkdownNode.GlobalHooks.Register<LinkInlineNode>(
            afterUpdate: _ => calls.Add("global observer"));
        using var globalPair = MarkdownNode.GlobalHooks.Register<LinkInlineNode>(
            beforeUpdate: _ =>
            {
                calls.Add("global before");
                if (shouldFailGlobal)
                {
                    throw new InvalidOperationException("global failure");
                }
            },
            afterUpdate: _ => calls.Add("global after"));
        using var firstLocalObserver = renderer.NodeHooks.Register<LinkInlineNode>(
            afterUpdate: _ => calls.Add("first local observer"));
        using var localPair = renderer.NodeHooks.Register<LinkInlineNode>(
            beforeUpdate: _ =>
            {
                calls.Add("local before");
                throw new InvalidOperationException("local failure");
            },
            afterUpdate: _ => calls.Add("local after"));
        using var initializationOnly = renderer.NodeHooks.Register<LinkInlineNode>(initialize: _ => { });
        using var laterLocalPair = renderer.NodeHooks.Register<LinkInlineNode>(
            beforeUpdate: _ => calls.Add("later local before"),
            afterUpdate: _ => calls.Add("later local after"));
        using var lastLocalObserver = renderer.NodeHooks.Register<LinkInlineNode>(
            afterUpdate: context =>
            {
                Assert.That(context.Result, Is.EqualTo(NodeUpdateResult.Failed));
                calls.Add("last local observer");
            });

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => UpdateLink(node, document, "[one](https://example.com)"));
            Assert.That(exception?.Message, Is.EqualTo(shouldFailGlobal ? "global failure" : "local failure"));

            var expected = new List<string> { "global before" };
            if (!shouldFailGlobal)
            {
                expected.Add("local before");
            }

            expected.Add("last local observer");
            if (!shouldFailGlobal)
            {
                expected.Add("local after");
            }

            expected.Add("first local observer");
            expected.Add("global after");
            expected.Add("global observer");
            Assert.That(calls, Is.EqualTo(expected));
        }
        finally
        {
            node.Release();
        }
    });

    [Test]
    public Task StandaloneDocumentUpdate_UnregisteringFromChild_DefersCleanupUntilAllSiblingsComplete() => Run(() =>
    {
        var renderer = new MarkdownRenderer();
        var document = new DocumentNode(renderer);
        var calls = new List<string>();
        IDisposable? registration = null;
        registration = renderer.NodeHooks.Register<LinkInlineNode>(
            afterUpdate: _ =>
            {
                calls.Add("updated");
                registration?.Dispose();
            },
            cleanup: _ => calls.Add("cleanup"));
        var markdown = "[one](https://example.com) and [two](https://example.org)";

        try
        {
            document.Update(
                document,
                Markdown.Parse(markdown),
                new ObservableStringBuilderChangedEventArgs(0, markdown.Length, markdown.Length, 1),
                CancellationToken.None);

            Assert.That(calls, Is.EqualTo(new[] { "updated", "updated", "cleanup", "cleanup" }));
        }
        finally
        {
            registration.Dispose();
            document.Clear();
        }
    });

    private static void Render(MarkdownRenderer renderer, string markdown)
    {
        renderer.DocumentUpdate = new MarkdownDocumentUpdate.Full(Markdown.Parse(markdown, MarkdownUpdateProducer.DefaultPipeline));
    }

    private static void UpdateLink(LinkInlineNode node, DocumentNode document, string markdown, CancellationToken token = default)
    {
        var parsed = Markdown.Parse(markdown);
        var link = ((ParagraphBlock)parsed[0]).Inline?.FirstChild ?? throw new AssertionException("Expected link.");
        node.Update(document, link, new ObservableStringBuilderChangedEventArgs(0, markdown.Length, markdown.Length, 1), token);
    }

    private static Task Run(Action action) => HeadlessSession.Current.Dispatch(action, CancellationToken.None);
}
