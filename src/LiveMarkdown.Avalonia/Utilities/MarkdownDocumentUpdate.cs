using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Represents one parsed Markdown state together with the source change that produced it.
/// </summary>
public abstract class MarkdownDocumentUpdate
{
    private readonly object identity = new();

    /// <summary>
    /// Gets the parsed Markdown document.
    /// </summary>
    public MarkdownDocument Document { get; }

    /// <summary>
    /// Gets the source change represented by this update.
    /// </summary>
    public ObservableStringBuilderChangedEventArgs Change { get; }

    /// <summary>
    /// Gets the source version represented by this update.
    /// </summary>
    /// <remarks>
    /// Versions are ordered only within updates produced from the same source. Different
    /// producers may use the same numeric versions without sharing incremental state.
    /// </remarks>
    public long Version => Change.Version;

    private MarkdownDocumentUpdate(MarkdownDocument document, in ObservableStringBuilderChangedEventArgs change)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Change = change;
    }

    /// <summary>
    /// Represents a complete parsed state that does not depend on an earlier update.
    /// </summary>
    public sealed class Full : MarkdownDocumentUpdate
    {
        /// <summary>
        /// Initializes a complete parsed state.
        /// </summary>
        /// <param name="document">The parsed Markdown document.</param>
        /// <param name="version">The source version represented by the document.</param>
        public Full(MarkdownDocument document, long version = 0)
            : base(
                document,
                new ObservableStringBuilderChangedEventArgs(
                    0,
                    document.GetLength(),
                    document.GetLength(),
                    version))
        {
        }
    }

    /// <summary>
    /// Represents a parsed state whose change information is relative to an earlier update.
    /// </summary>
    public sealed class Incremental : MarkdownDocumentUpdate
    {
        /// <summary>
        /// Gets the version of the previous update to which
        /// <see cref="MarkdownDocumentUpdate.Change"/> applies.
        /// </summary>
        public long BaseVersion { get; }

        /// <summary>
        /// Initializes an incremental parsed state.
        /// </summary>
        /// <param name="previous">The update on which this update is based.</param>
        /// <param name="document">The newly parsed Markdown document.</param>
        /// <param name="change">The aggregate source change since <paramref name="previous"/>.</param>
        public Incremental(
            MarkdownDocumentUpdate previous,
            MarkdownDocument document,
            in ObservableStringBuilderChangedEventArgs change) : base(document, change)
        {
            ArgumentNullException.ThrowIfNull(previous);
            BaseVersion = previous.Version;
            baseIdentity = previous.identity;
        }

        private readonly object baseIdentity;

        internal bool Follows(MarkdownDocumentUpdate previous)
        {
            return ReferenceEquals(baseIdentity, previous.identity);
        }
    }
}