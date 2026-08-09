using Inline = Avalonia.Controls.Documents.Inline;
using MarkdigCodeInline = Markdig.Syntax.Inlines.CodeInline;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A node that represents a code inline.
/// </summary>
public class CodeInlineNode : InlineNode<MarkdigCodeInline>
{
    /// <summary>
    /// Gets the code inline rendered by this node.
    /// </summary>
    public override Inline Inline => codeInline;

    private readonly CodeInline codeInline = new()
    {
        Classes = { "Code" }
    };

    /// <inheritdoc/>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        MarkdigCodeInline code,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        codeInline.Text = code.Content;
        return true;
    }
}