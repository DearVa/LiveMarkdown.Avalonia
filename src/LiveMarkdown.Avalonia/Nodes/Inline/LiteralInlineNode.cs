using Avalonia.Controls.Documents;
using Markdig.Syntax.Inlines;
using Inline = Avalonia.Controls.Documents.Inline;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders a Markdig literal inline as an Avalonia text run.
/// </summary>
public class LiteralInlineNode : InlineNode<LiteralInline>
{
    /// <summary>
    /// Gets the Avalonia run represented by this node.
    /// </summary>
    public override Inline Inline { get; }

    private readonly Run run;

    /// <summary>
    /// Initializes a new literal inline node.
    /// </summary>
    public LiteralInlineNode()
    {
        Inline = run = new Run
        {
            Classes = { "Literal" }
        };
    }

    /// <summary>
    /// Updates the text run with the literal content.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="literal">The Markdig literal inline.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> when the literal was updated.</returns>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        LiteralInline literal,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        run.Text = literal.Content.ToString();
        return true;
    }
}