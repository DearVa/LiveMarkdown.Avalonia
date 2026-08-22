namespace LiveMarkdown.Avalonia;

/// <summary>
/// Produces parsed Markdown updates for a renderer.
/// </summary>
/// <remarks>
/// Implementations are multicast observables. When a valid update is already available,
/// <see cref="IObservable{T}.Subscribe"/> must synchronously replay it to the new subscriber.
/// All members, subscriptions, and observer notifications are used on the Avalonia UI thread.
/// </remarks>
public interface IMarkdownUpdateProducer : IObservable<MarkdownDocumentUpdate>
{
    /// <summary>
    /// Gets or sets the primary streaming Markdown source.
    /// </summary>
    ObservableStringBuilder? MarkdownBuilder { get; set; }
}