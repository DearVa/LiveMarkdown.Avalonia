using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Markdig;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class MarkdownRendererDocumentTests
{
    private HeadlessUnitTestSession session = null!;

    [OneTimeSetUp]
    public void StartSession()
    {
        session = HeadlessSession.Current;
    }

    [Test]
    public async Task MarkdownBuilder_UsesSameLazyDefaultProducerWhenSourceChanges()
    {
        await session.Dispatch(
            () =>
            {
                var firstSource = new ObservableStringBuilder("first");
                var secondSource = new ObservableStringBuilder("second");
                var renderer = new MarkdownRenderer { MarkdownBuilder = firstSource };
                var producer = renderer.UpdateProducer;

                renderer.MarkdownBuilder = secondSource;

                Assert.Multiple(() =>
                {
                    Assert.That(renderer.UpdateProducer, Is.SameAs(producer));
                    Assert.That(renderer.MarkdownBuilder, Is.SameAs(secondSource));
                    Assert.That(producer.MarkdownBuilder, Is.SameAs(secondSource));
                });

                renderer.MarkdownBuilder = null;
            },
            CancellationToken.None);
    }

    [Test]
    public async Task DocumentUpdate_SetBeforeAttach_IsAvailableDuringFirstMeasure()
    {
        var firstMeasureText = await session.Dispatch(
            () =>
            {
                var renderer = new FirstMeasureMarkdownRenderer
                {
                    DocumentUpdate = new MarkdownDocumentUpdate.Full(
                        Markdown.Parse("# Heading\n\nBody", MarkdownUpdateProducer.DefaultPipeline)),
                };
                var window = new Window
                {
                    Width = 400,
                    Height = 200,
                    Content = renderer,
                };

                try
                {
                    Assert.That(GetRenderedText(renderer), Is.EqualTo(new[] { "Heading", "Body" }));
                    window.Show();
                    return renderer.FirstMeasureText;
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(firstMeasureText, Is.EqualTo(new[] { "Heading", "Body" }));
    }

    [Test]
    public async Task DocumentUpdate_SetWhileAttached_UpdatesVisualTreeSynchronously()
    {
        var result = await session.Dispatch(
            () =>
            {
                var renderer = new MarkdownRenderer
                {
                    DocumentUpdate = new MarkdownDocumentUpdate.Full(
                        Markdown.Parse("old", MarkdownUpdateProducer.DefaultPipeline)),
                };
                var window = new Window
                {
                    Width = 400,
                    Height = 200,
                    Content = renderer,
                };

                try
                {
                    window.Show();
                    renderer.DocumentUpdate = new MarkdownDocumentUpdate.Full(
                        Markdown.Parse("new content", MarkdownUpdateProducer.DefaultPipeline));
                    return (
                        RenderedText: GetRenderedText(renderer),
                        renderer.RenderedTextProjection);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.RenderedText, Is.EqualTo(new[] { "new content" }));
            Assert.That(result.RenderedTextProjection, Is.Null);
        });
    }

    [Test]
    public async Task DocumentUpdate_ReplacedWhileDetached_UpdatesExistingVisualTreeSynchronously()
    {
        var result = await session.Dispatch(
            () =>
            {
                var renderer = new MarkdownRenderer
                {
                    DocumentUpdate = new MarkdownDocumentUpdate.Full(
                        Markdown.Parse("old content", MarkdownUpdateProducer.DefaultPipeline)),
                };
                var originalBlock = renderer.GetVisualDescendants().OfType<MarkdownTextBlock>().Single();

                renderer.DocumentUpdate = new MarkdownDocumentUpdate.Full(
                    Markdown.Parse("new content", MarkdownUpdateProducer.DefaultPipeline));

                var updatedBlock = renderer.GetVisualDescendants().OfType<MarkdownTextBlock>().Single();
                return (originalBlock, updatedBlock, updatedBlock.ActualText);
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.updatedBlock, Is.SameAs(result.originalBlock));
            Assert.That(result.ActualText, Is.EqualTo("new content"));
        });
    }

    [Test]
    public async Task DocumentUpdate_SetBeforeAttach_AppliesInheritedInlineStylesBeforeLayout()
    {
        var inlineStyle = await session.Dispatch(
            () =>
            {
                var renderer = new MarkdownRenderer
                {
                    DocumentUpdate = new MarkdownDocumentUpdate.Full(
                        Markdown.Parse("`code`", MarkdownUpdateProducer.DefaultPipeline)),
                };
                var style = new Style(static selector => selector.OfType<CodeInline>());
                style.Setters.Add(new Setter(CodeInline.BackgroundProperty, Brushes.Orange));
                style.Setters.Add(new Setter(CodeInline.MarginProperty, new Thickness(7, 0)));
                renderer.Styles.Add(style);
                var window = new Window
                {
                    Width = 400,
                    Height = 200,
                    Content = renderer,
                };

                try
                {
                    window.Show();
                    var code = renderer.GetVisualDescendants()
                        .OfType<MarkdownTextBlock>()
                        .Single()
                        .Inlines!
                        .OfType<CodeInline>()
                        .Single();
                    return (code.Background, code.Margin, code.CornerRadius, code.Padding);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(inlineStyle.Background, Is.SameAs(Brushes.Orange));
            Assert.That(inlineStyle.Margin, Is.EqualTo(new Thickness(7, 0)));
            Assert.That(inlineStyle.CornerRadius, Is.EqualTo(new CornerRadius(4)));
            Assert.That(inlineStyle.Padding, Is.EqualTo(new Thickness(2, 0)));
        });
    }

    [Test]
    public async Task DocumentUpdate_SetBeforeAttach_PublishesTextProjectionAfterFirstLayout()
    {
        var projectionText = await session.Dispatch(
            () =>
            {
                var renderer = new MarkdownRenderer
                {
                    DocumentUpdate = new MarkdownDocumentUpdate.Full(
                        Markdown.Parse("Heading\n\nBody", MarkdownUpdateProducer.DefaultPipeline)),
                };
                var window = new Window
                {
                    Width = 400,
                    Height = 200,
                    Content = renderer,
                };

                try
                {
                    window.Show();
                    return renderer.RenderedTextProjection?.Buffers
                        .Select(static buffer => buffer.Text.ToString())
                        .ToArray();
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(projectionText, Is.EqualTo(new[] { "Heading", "Body" }));
    }

    [Test]
    public async Task DocumentUpdate_SetToNull_ClearsVisualTreeSynchronously()
    {
        var renderedText = await session.Dispatch(
            () =>
            {
                var renderer = new MarkdownRenderer
                {
                    DocumentUpdate = new MarkdownDocumentUpdate.Full(
                        Markdown.Parse("content", MarkdownUpdateProducer.DefaultPipeline)),
                };
                var window = new Window
                {
                    Width = 400,
                    Height = 200,
                    Content = renderer,
                };

                try
                {
                    window.Show();
                    renderer.DocumentUpdate = null;
                    return GetRenderedText(renderer);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.That(renderedText, Is.Empty);
    }

    [Test]
    public async Task IncrementalDocumentUpdate_AppliedWithoutItsBase_FallsBackToFullUpdate()
    {
        var renderedText = await session.Dispatch(
            () =>
            {
                var previous = new MarkdownDocumentUpdate.Full(
                    Markdown.Parse("old", MarkdownUpdateProducer.DefaultPipeline),
                    0);
                var update = new MarkdownDocumentUpdate.Incremental(
                    previous,
                    Markdown.Parse("complete content", MarkdownUpdateProducer.DefaultPipeline),
                    new ObservableStringBuilderChangedEventArgs(3, 13, 16, 1));
                var renderer = new MarkdownRenderer { DocumentUpdate = update };

                return GetRenderedText(renderer);
            },
            CancellationToken.None);

        Assert.That(renderedText, Is.EqualTo(new[] { "complete content" }));
    }

    [Test]
    public async Task MarkdownBuilder_SetWhileDetached_ProducesCompleteVisualTree()
    {
        var documentReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = await session.Dispatch(
            () =>
            {
                var result = new MarkdownRenderer();
                result.PropertyChanged += (_, e) =>
                {
                    if (e.Property == MarkdownRenderer.DocumentUpdateProperty)
                    {
                        documentReady.TrySetResult();
                    }
                };
                result.MarkdownBuilder = new ObservableStringBuilder("detached content");
                return result;
            },
            CancellationToken.None);

        await WaitForDocumentAsync(documentReady.Task);
        var result = await session.Dispatch(
            () =>
            {
                var renderedText = GetRenderedText(renderer);
                renderer.MarkdownBuilder = null;
                return (renderer.DocumentUpdate, renderedText);
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.DocumentUpdate, Is.TypeOf<MarkdownDocumentUpdate.Full>());
            Assert.That(result.renderedText, Is.EqualTo(new[] { "detached content" }));
        });
    }

    [Test]
    public async Task UpdateProducerPipeline_ChangedForMarkdownBuilder_StartsNewFullUpdateSequence()
    {
        var firstDocument = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = await session.Dispatch(
            () =>
            {
                var result = new MarkdownRenderer();
                result.PropertyChanged += (_, e) =>
                {
                    if (e.Property == MarkdownRenderer.DocumentUpdateProperty)
                    {
                        firstDocument.TrySetResult();
                    }
                };
                result.MarkdownBuilder = new ObservableStringBuilder("content");
                return result;
            },
            CancellationToken.None);

        await WaitForDocumentAsync(firstDocument.Task);

        var secondDocument = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = new MarkdownPipelineBuilder().Build();
        await session.Dispatch(
            () =>
            {
                renderer.PropertyChanged += (_, e) =>
                {
                    if (e.Property == MarkdownRenderer.DocumentUpdateProperty)
                    {
                        secondDocument.TrySetResult();
                    }
                };
                ((MarkdownUpdateProducer)renderer.UpdateProducer).Pipeline = pipeline;
            },
            CancellationToken.None);

        await WaitForDocumentAsync(secondDocument.Task);
        var result = await session.Dispatch(
            () =>
            {
                var update = renderer.DocumentUpdate;
                renderer.MarkdownBuilder = null;
                return (((MarkdownUpdateProducer)renderer.UpdateProducer).Pipeline, Update: update);
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Pipeline, Is.SameAs(pipeline));
            Assert.That(result.Update, Is.TypeOf<MarkdownDocumentUpdate.Full>());
            Assert.That(result.Update!.Version, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExternalUpdateProducer_CanBeAssignedToRenderer()
    {
        var documentReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = await session.Dispatch(
            () =>
            {
                var result = new MarkdownRenderer();
                result.PropertyChanged += (_, e) =>
                {
                    if (e.Property == MarkdownRenderer.DocumentUpdateProperty)
                    {
                        documentReady.TrySetResult();
                    }
                };

                var producer = new MarkdownUpdateProducer
                {
                    MarkdownBuilder = new ObservableStringBuilder("external content"),
                };
                result.UpdateProducer = producer;
                return result;
            },
            CancellationToken.None);

        await WaitForDocumentAsync(documentReady.Task);
        var renderedText = await session.Dispatch(
            () => GetRenderedText(renderer),
            CancellationToken.None);

        Assert.That(renderedText, Is.EqualTo(new[] { "external content" }));
    }

    [Test]
    public async Task MarkdownBuilder_ChangedWhileAttached_DoesNotInvalidateLayoutBeforeParseCompletes()
    {
        var firstDocument = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rendererState = await session.Dispatch(
            () =>
            {
                var builder = new ObservableStringBuilder("initial");
                var renderer = new MarkdownRenderer { MarkdownBuilder = builder };
                renderer.PropertyChanged += (_, e) =>
                {
                    if (e.Property == MarkdownRenderer.DocumentUpdateProperty)
                    {
                        firstDocument.TrySetResult();
                    }
                };

                var window = new Window
                {
                    Width = 400,
                    Height = 200,
                    Content = renderer,
                };
                window.Show();
                return (window, renderer, builder);
            },
            CancellationToken.None);

        try
        {
            await WaitForDocumentAsync(firstDocument.Task);

            var secondDocument = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var layoutRemainedValid = await session.Dispatch(
                () =>
                {
                    rendererState.renderer.PropertyChanged += HandleDocumentChanged;
                    rendererState.renderer.Measure(new Size(400, 200));
                    rendererState.renderer.Arrange(new Rect(0, 0, 400, 200));
                    Assert.That(rendererState.renderer.IsMeasureValid, Is.True);
                    Assert.That(rendererState.renderer.IsArrangeValid, Is.True);

                    rendererState.builder.Append(" content");
                    return (
                        rendererState.renderer.IsMeasureValid,
                        rendererState.renderer.IsArrangeValid);

                    void HandleDocumentChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
                    {
                        if (e.Property == MarkdownRenderer.DocumentUpdateProperty)
                        {
                            secondDocument.TrySetResult();
                        }
                    }
                },
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(layoutRemainedValid.IsMeasureValid, Is.True);
                Assert.That(layoutRemainedValid.IsArrangeValid, Is.True);
            });
            await WaitForDocumentAsync(secondDocument.Task);

            var renderedText = await session.Dispatch(
                () => GetRenderedText(rendererState.renderer),
                CancellationToken.None);
            Assert.That(renderedText, Is.EqualTo(new[] { "initial content" }));
        }
        finally
        {
            await session.Dispatch(rendererState.window.Close, CancellationToken.None);
        }
    }

    [Test]
    public async Task MarkdownBuilder_ParseStartedWhileAttached_CompletesAfterDetach()
    {
        var firstDocument = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rendererState = await session.Dispatch(
            () =>
            {
                var builder = new ObservableStringBuilder("initial");
                var renderer = new MarkdownRenderer { MarkdownBuilder = builder };
                renderer.PropertyChanged += (_, e) =>
                {
                    if (e.Property == MarkdownRenderer.DocumentUpdateProperty)
                    {
                        firstDocument.TrySetResult();
                    }
                };

                var window = new Window
                {
                    Width = 400,
                    Height = 200,
                    Content = renderer,
                };
                window.Show();
                return (window, renderer, builder);
            },
            CancellationToken.None);

        try
        {
            await WaitForDocumentAsync(firstDocument.Task);

            var secondDocument = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await session.Dispatch(
                () =>
                {
                    rendererState.renderer.PropertyChanged += (_, e) =>
                    {
                        if (e.Property == MarkdownRenderer.DocumentUpdateProperty)
                        {
                            secondDocument.TrySetResult();
                        }
                    };
                    rendererState.builder.Append(" content");
                    rendererState.window.Content = null;
                    rendererState.window.Close();
                },
                CancellationToken.None);

            await WaitForDocumentAsync(secondDocument.Task);
            var result = await session.Dispatch(
                () => (
                    TopLevel: TopLevel.GetTopLevel(rendererState.renderer),
                    RenderedText: GetRenderedText(rendererState.renderer)),
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.TopLevel, Is.Null);
                Assert.That(result.RenderedText, Is.EqualTo(new[] { "initial content" }));
            });
        }
        finally
        {
            await session.Dispatch(rendererState.window.Close, CancellationToken.None);
        }
    }

    private async Task WaitForDocumentAsync(Task documentTask)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!documentTask.IsCompleted)
        {
            await Task.Delay(10, cancellation.Token);
            await session.Dispatch(static () => { }, cancellation.Token);
        }

        await documentTask;
    }

    private static string[] GetRenderedText(MarkdownRenderer renderer) =>
    [
        .. renderer.GetVisualDescendants()
            .OfType<MarkdownTextBlock>()
            .Select(block => block.ActualText),
    ];

    private sealed class FirstMeasureMarkdownRenderer : MarkdownRenderer
    {
        public string[]? FirstMeasureText { get; private set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            FirstMeasureText ??= GetRenderedText(this);
            return base.MeasureOverride(availableSize);
        }
    }
}
