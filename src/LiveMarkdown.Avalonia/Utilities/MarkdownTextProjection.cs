using System.Text;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdigCodeInline = Markdig.Syntax.Inlines.CodeInline;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Represents one searchable text block produced from a Markdown document.
/// </summary>
/// <param name="SourceSpan">The Markdown source span represented by this buffer.</param>
/// <param name="Text">The exact local layout text searched as one block.</param>
public readonly record struct MarkdownTextBuffer(SourceSpan SourceSpan, StringSlice Text)
{
    /// <inheritdoc />
    public bool Equals(MarkdownTextBuffer other) =>
        SourceSpan.Equals(other.SourceSpan) && Text.AsSpan().SequenceEqual(other.Text.AsSpan());

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        SourceSpan,
        string.GetHashCode(Text.AsSpan(), StringComparison.Ordinal));
}

/// <summary>
/// Represents the searchable text projection of one Markdown source version.
/// </summary>
public sealed class MarkdownTextProjection
{
    /// <summary>
    /// Represents one embedded object in layout-coordinate text.
    /// </summary>
    public const char ObjectReplacementCharacter = '\uFFFC';

    /// <summary>
    /// Gets the source content version.
    /// </summary>
    public long SourceVersion { get; }

    /// <summary>
    /// Gets searchable buffers in visual document order.
    /// </summary>
    public IReadOnlyList<MarkdownTextBuffer> Buffers { get; }

    internal MarkdownTextProjection(long sourceVersion, IReadOnlyList<MarkdownTextBuffer> buffers)
    {
        SourceVersion = sourceVersion;
        Buffers = buffers;
    }
}

/// <summary>
/// Creates searchable text projections without constructing an Avalonia visual tree.
/// </summary>
public class MarkdownTextProjector
{
    /// <summary>
    /// Represents the replacement text for an inline that renders an embedded object.
    /// </summary>
    protected static readonly StringSlice ObjectReplacementText = new(MarkdownTextProjection.ObjectReplacementCharacter.ToString());

    /// <summary>
    /// Parses and projects a committed Markdown snapshot synchronously.
    /// </summary>
    /// <param name="snapshot">The source text and matching version.</param>
    /// <param name="cancellationToken">A token that cancels before parsing or during projection.</param>
    /// <returns>The searchable text buffers for the supplied version.</returns>
    public MarkdownTextProjection Project(ObservableStringBuilderSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = Markdown.Parse(snapshot.Text, MarkdownUpdateProducer.DefaultPipeline);
        var buffers = new List<MarkdownTextBuffer>();
        AppendBlocks(document, buffers, cancellationToken);
        return new MarkdownTextProjection(snapshot.Version, buffers);
    }

    /// <summary>
    /// Appends the searchable projection of every direct child block.
    /// </summary>
    /// <param name="container">The block container to project.</param>
    /// <param name="buffers">The destination buffers.</param>
    /// <param name="cancellationToken">A token that cancels projection.</param>
    protected virtual void AppendBlocks(ContainerBlock container, List<MarkdownTextBuffer> buffers, CancellationToken cancellationToken)
    {
        foreach (var block in container)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendBlock(block, buffers, cancellationToken);
        }
    }

    /// <summary>
    /// Appends the searchable projection of one block.
    /// </summary>
    /// <param name="block">The block to project.</param>
    /// <param name="buffers">The destination buffers.</param>
    /// <param name="cancellationToken">A token that cancels projection.</param>
    protected virtual void AppendBlock(Block block, List<MarkdownTextBuffer> buffers, CancellationToken cancellationToken)
    {
        switch (block)
        {
            case ParagraphBlock paragraph when
                !BlockNode.HasMoreSpecificBlockNodeFactory(paragraph.GetType(), typeof(ParagraphBlock)):
                AppendLeafBlock(paragraph, buffers, cancellationToken);
                break;
            case HeadingBlock heading when
                !BlockNode.HasMoreSpecificBlockNodeFactory(heading.GetType(), typeof(HeadingBlock)):
                AppendLeafBlock(heading, buffers, cancellationToken);
                break;
            case Markdig.Syntax.CodeBlock codeBlock when
                !BlockNode.HasMoreSpecificBlockNodeFactory(codeBlock.GetType(), typeof(Markdig.Syntax.CodeBlock)):
                AppendCodeBlock(codeBlock, buffers, cancellationToken);
                break;
            case ContainerBlock childContainer:
                AppendBlocks(childContainer, buffers, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Appends the searchable projection of one inline-bearing leaf block.
    /// </summary>
    /// <param name="block">The leaf block to project.</param>
    /// <param name="buffers">The destination buffers.</param>
    /// <param name="cancellationToken">A token that cancels projection.</param>
    protected virtual void AppendLeafBlock(LeafBlock block, List<MarkdownTextBuffer> buffers, CancellationToken cancellationToken)
    {
        if (block.Inline is not { } inline) return;

        cancellationToken.ThrowIfCancellationRequested();
        if (inline.FirstChild is { NextSibling: null } child &&
            TryGetDirectInlineText(child, cancellationToken, out var text))
        {
            buffers.Add(new MarkdownTextBuffer(block.Span, text));
            return;
        }

        var builder = new StringBuilder();
        AppendInlines(inline, builder, cancellationToken);
        buffers.Add(new MarkdownTextBuffer(block.Span, new StringSlice(builder.ToString())));
    }

    /// <summary>
    /// Appends the searchable projection of one code block.
    /// </summary>
    /// <param name="block">The code block to project.</param>
    /// <param name="buffers">The destination buffers.</param>
    /// <param name="cancellationToken">A token that cancels projection.</param>
    protected virtual void AppendCodeBlock(Markdig.Syntax.CodeBlock block, List<MarkdownTextBuffer> buffers, CancellationToken cancellationToken)
    {
        if (block.Lines.Lines is null) return;

        cancellationToken.ThrowIfCancellationRequested();
        if (block.Lines.Count == 1)
        {
            buffers.Add(new MarkdownTextBuffer(block.Span, block.Lines.Lines[0].Slice));
            return;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < block.Lines.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i > 0) builder.Append(Environment.NewLine);
            builder.Append(block.Lines.Lines[i].Slice.AsSpan());
        }
        buffers.Add(new MarkdownTextBuffer(block.Span, new StringSlice(builder.ToString())));
    }

    /// <summary>
    /// Appends the searchable projection of every direct child inline.
    /// </summary>
    /// <param name="container">The inline container to project.</param>
    /// <param name="builder">The destination text builder.</param>
    /// <param name="cancellationToken">A token that cancels projection.</param>
    protected virtual void AppendInlines(ContainerInline container, StringBuilder builder, CancellationToken cancellationToken)
    {
        foreach (var inline in container)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendInline(inline, builder, cancellationToken);
        }
    }

    /// <summary>
    /// Appends the searchable projection of one inline.
    /// </summary>
    /// <param name="inline">The inline to project.</param>
    /// <param name="builder">The destination text builder.</param>
    /// <param name="cancellationToken">A token that cancels projection.</param>
    protected virtual void AppendInline(Inline inline, StringBuilder builder, CancellationToken cancellationToken)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.AsSpan());
                break;
            case MarkdigCodeInline code:
                builder.Append(code.Content);
                break;
            case LineBreakInline:
                builder.Append(Environment.NewLine);
                break;
            case HtmlEntityInline entity:
                builder.Append(entity.Transcoded.AsSpan());
                break;
            case AutolinkInline autolink:
                builder.Append(autolink.Url);
                break;
            case DelimiterInline delimiter:
                builder.Append(delimiter.ToLiteral());
                break;
            case TaskList:
            case LinkInline { IsImage: true }:
                builder.Append(MarkdownTextProjection.ObjectReplacementCharacter);
                break;
            case ContainerInline childContainer:
                AppendInlines(childContainer, builder, cancellationToken);
                break;
            default:
                if (InlineNode.HasRegisteredInlineNodeFactory(inline.GetType()))
                {
                    // Registered custom inline nodes commonly render an InlineUIContainer.
                    // The object-replacement character preserves the layout coordinate occupied
                    // by that control without exposing its source markup as searchable text.
                    builder.Append(MarkdownTextProjection.ObjectReplacementCharacter);
                }
                break;
        }
    }

    /// <summary>
    /// Tries to project a single inline directly from existing storage without building a new string.
    /// </summary>
    /// <param name="inline">The only inline in a leaf block.</param>
    /// <param name="cancellationToken">A token that cancels projection.</param>
    /// <param name="text">The direct searchable text when the inline supports it.</param>
    /// <returns><see langword="true"/> when the inline can be represented directly.</returns>
    protected virtual bool TryGetDirectInlineText(Inline inline, CancellationToken cancellationToken, out StringSlice text)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (inline)
        {
            case LiteralInline literal:
                text = literal.Content;
                return true;
            case MarkdigCodeInline code:
                text = new StringSlice(code.Content);
                return true;
            case LineBreakInline:
                text = new StringSlice(Environment.NewLine);
                return true;
            case HtmlEntityInline entity:
                text = entity.Transcoded;
                return true;
            case AutolinkInline autolink:
                text = new StringSlice(autolink.Url);
                return true;
            case DelimiterInline delimiter:
                text = new StringSlice(delimiter.ToLiteral());
                return true;
            case TaskList:
            case LinkInline { IsImage: true }:
                text = ObjectReplacementText;
                return true;
            default:
                text = default;
                return false;
        }
    }
}