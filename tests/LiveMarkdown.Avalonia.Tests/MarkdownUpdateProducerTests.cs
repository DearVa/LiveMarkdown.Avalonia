using Avalonia.Headless;
using Markdig;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class MarkdownUpdateProducerTests
{
    private HeadlessUnitTestSession session = null!;

    [OneTimeSetUp]
    public void StartSession()
    {
        session = HeadlessSession.Current;
    }

    [Test]
    public async Task Producer_PublishesFullThenIncrementalAndReplaysLatestUpdate()
    {
        var fullUpdate = new TaskCompletionSource<MarkdownDocumentUpdate>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var incrementalUpdate = new TaskCompletionSource<MarkdownDocumentUpdate>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var state = await session.Dispatch(
            () =>
            {
                var source = new ObservableStringBuilder("initial");
                var producer = new MarkdownUpdateProducer { MarkdownBuilder = source };
                var subscription = producer.Subscribe(
                    new CallbackObserver(update =>
                    {
                        if (update is MarkdownDocumentUpdate.Full)
                        {
                            fullUpdate.TrySetResult(update);
                        }
                        else
                        {
                            incrementalUpdate.TrySetResult(update);
                        }
                    }));
                return (source, producer, subscription);
            },
            CancellationToken.None);

        var first = await WaitForUpdateAsync(fullUpdate.Task);
        await session.Dispatch(() => state.source.Append(" content"), CancellationToken.None);
        var second = await WaitForUpdateAsync(incrementalUpdate.Task);
        var replayed = await session.Dispatch(
            () =>
            {
                MarkdownDocumentUpdate? result = null;
                using var subscription = state.producer.Subscribe(new CallbackObserver(update => result = update));
                return result;
            },
            CancellationToken.None);
        await session.Dispatch(state.subscription.Dispose, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.TypeOf<MarkdownDocumentUpdate.Full>());
            Assert.That(second, Is.TypeOf<MarkdownDocumentUpdate.Incremental>());
            Assert.That(second.Version, Is.EqualTo(1));
            Assert.That(replayed, Is.SameAs(second));
        });
    }

    [Test]
    public async Task MarkdownBuilder_Changed_PublishesFullUpdate()
    {
        var firstUpdate = new TaskCompletionSource<MarkdownDocumentUpdate>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondUpdate = new TaskCompletionSource<MarkdownDocumentUpdate>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var updateCount = 0;
        var state = await session.Dispatch(
            () =>
            {
                var producer = new MarkdownUpdateProducer
                {
                    MarkdownBuilder = new ObservableStringBuilder("first"),
                };
                var subscription = producer.Subscribe(
                    new CallbackObserver(update =>
                    {
                        if (++updateCount == 1)
                        {
                            firstUpdate.TrySetResult(update);
                        }
                        else
                        {
                            secondUpdate.TrySetResult(update);
                        }
                    }));
                return (producer, subscription);
            },
            CancellationToken.None);

        await WaitForUpdateAsync(firstUpdate.Task);
        await session.Dispatch(
            () => state.producer.MarkdownBuilder = new ObservableStringBuilder("replacement"),
            CancellationToken.None);
        var update = await WaitForUpdateAsync(secondUpdate.Task);
        await session.Dispatch(state.subscription.Dispose, CancellationToken.None);

        Assert.That(update, Is.TypeOf<MarkdownDocumentUpdate.Full>());
    }

    [Test]
    public void IncrementalUpdate_FollowsOnlyItsExactPreviousState()
    {
        var firstDocument = Markdown.Parse("first", MarkdownUpdateProducer.DefaultPipeline);
        var otherDocument = Markdown.Parse("other", MarkdownUpdateProducer.DefaultPipeline);
        var nextDocument = Markdown.Parse("first next", MarkdownUpdateProducer.DefaultPipeline);
        var first = new MarkdownDocumentUpdate.Full(firstDocument, 0);
        var other = new MarkdownDocumentUpdate.Full(otherDocument, 0);
        var incremental = new MarkdownDocumentUpdate.Incremental(
            first,
            nextDocument,
            new ObservableStringBuilderChangedEventArgs(5, 5, 10, 1));
        var later = new MarkdownDocumentUpdate.Incremental(
            incremental,
            Markdown.Parse("first next later", MarkdownUpdateProducer.DefaultPipeline),
            new ObservableStringBuilderChangedEventArgs(10, 6, 16, 2));

        Assert.Multiple(() =>
        {
            Assert.That(incremental.Follows(first), Is.True);
            Assert.That(incremental.Follows(other), Is.False);
            Assert.That(later.Follows(first), Is.False);
            Assert.That(later.Follows(incremental), Is.True);
        });
    }

    private async Task<MarkdownDocumentUpdate> WaitForUpdateAsync(Task<MarkdownDocumentUpdate> updateTask)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!updateTask.IsCompleted)
        {
            await Task.Delay(10, cancellation.Token);
            await session.Dispatch(static () => { }, cancellation.Token);
        }

        return await updateTask;
    }

    private sealed class CallbackObserver(Action<MarkdownDocumentUpdate> onNext) : IObserver<MarkdownDocumentUpdate>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            Assert.Fail($"The producer unexpectedly terminated: {error}");
        }

        public void OnNext(MarkdownDocumentUpdate value)
        {
            onNext(value);
        }
    }
}
