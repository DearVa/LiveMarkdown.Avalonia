using Avalonia.Controls.Documents;
using Markdig.Syntax.Inlines;
using Inline = Avalonia.Controls.Documents.Inline;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders a Markdig line-break inline as an Avalonia line break.
/// </summary>
public class LineBreakInlineNode : InlineNode<LineBreakInline>
{
    /// <summary>
    /// Gets the Avalonia line break represented by this node.
    /// </summary>
    public override Inline Inline { get; } = new LineBreak
    {
        Classes = { "LineBreak" }
    };

    /// <summary>
    /// Keeps the line-break node valid; line breaks have no additional content to update.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="lineBreak">The Markdig line-break inline.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> because the line break remains valid.</returns>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        LineBreakInline lineBreak,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        return true;
    }
}