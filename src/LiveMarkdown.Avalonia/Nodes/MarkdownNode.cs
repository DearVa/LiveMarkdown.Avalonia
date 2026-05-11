using System.Collections.Immutable;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

// ReSharper disable InconsistentNaming

namespace LiveMarkdown.Avalonia;

public abstract class MarkdownNode
{
    protected static ImmutableHashSet<IMarkdownNodeFactory> NodeFactories { get; private set; } = ImmutableHashSet.Create<IMarkdownNodeFactory>(
        new MarkdownNodeFactory<AutolinkInlineNode>(),
        new MarkdownNodeFactory<CodeInlineNode>(),
        new MarkdownNodeFactory<ContainerInlineNode<ContainerInline>>(),
        new MarkdownNodeFactory<DelimiterInlineNode>(),
        new MarkdownNodeFactory<EmphasisInlineNode>(),
        new MarkdownNodeFactory<HtmlEntityInlineNode>(),
        new MarkdownNodeFactory<LineBreakInlineNode>(),
        new MarkdownNodeFactory<LinkInlineNode>(),
        new MarkdownNodeFactory<LiteralInlineNode>(),
        new MarkdownNodeFactory<TaskListNode>(),
        new MarkdownNodeFactory<AlertBlockNode>(),
        new MarkdownNodeFactory<CodeBlockNode>(),
        new MarkdownNodeFactory<HeadingBlockNode>(),
        new MarkdownNodeFactory<ListBlockNode>(),
        new MarkdownNodeFactory<ListItemBlockNode>(),
        new MarkdownNodeFactory<ParagraphBlockNode>(),
        new MarkdownNodeFactory<QuoteBlockNode>(),
        new MarkdownNodeFactory<TableCellNode>(),
        new MarkdownNodeFactory<TableNode>(),
        new MarkdownNodeFactory<ThematicBreakBlockNode>()
    );

    /// <summary>
    /// Register a single node factory to the pipeline. This allows for custom node factories to be added to the pipeline, enabling support for custom Markdown syntax or behavior.
    /// </summary>
    /// <remarks>
    /// If you want to register multiple node factories, consider using the <see cref="Edit"/> method to configure the pipeline in a single call, which can be more efficient and easier to read.
    /// </remarks>
    /// <typeparam name="TNode"></typeparam>
    public static void Register<TNode>() where TNode : MarkdownNode, new()
    {
        Edit(builder => builder.Register<TNode>());
    }

    /// <summary>
    /// Edits the pipeline of node factories using the provided builder function.
    /// </summary>
    /// <example>
    /// <code>
    /// MarkdownNode.Edit(builder =&gt;
    ///     builder.Register&lt;CustomNode&gt;().Unregister&lt;HeadingBlockNode&gt;());
    /// </code>
    /// </example>
    /// <param name="builder">The function to configure the pipeline.</param>
    public static void Edit(Func<MarkdownNodePipelineBuilder, MarkdownNodePipelineBuilder> builder)
    {
        NodeFactories = [..builder(new MarkdownNodePipelineBuilder([..NodeFactories])).RegisteredNodeFactories];
    }

    /// <summary>
    /// records the source span of the block in the Markdown document.
    /// </summary>
    private SourceSpan span;

    private bool IsDirty(MarkdownObject markdownObject, in ObservableStringBuilderChangedEventArgs change)
    {
        return !span.Equals(markdownObject.Span) || span.End >= change.StartIndex && change.StartIndex + change.Length > span.Start;
    }

    public bool? Update(
        DocumentNode documentNode,
        MarkdownObject markdownObject,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        if (!IsDirty(markdownObject, change))
        {
            // No need to update, the change does not affect this node
            return null;
        }

        var result = UpdateCore(documentNode, markdownObject, change, cancellationToken);
        span = markdownObject.Span;

        MarkdownRenderer.VerboseLogger?.Log(
            this,
            "Updated {NodeName} {Result} {Span}",
            markdownObject.GetType().Name,
            result,
            markdownObject.Span);
        return result;
    }

    /// <summary>
    /// Updates the block with the given inlines and change information.
    /// </summary>
    /// <param name="documentNode"></param>
    /// <param name="markdownObject"></param>
    /// <param name="change"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>true if the block was updated successfully, false if it needs to be removed</returns>
    protected abstract bool UpdateCore(
        DocumentNode documentNode,
        MarkdownObject markdownObject,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken);
}