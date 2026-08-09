using Avalonia.Controls.Documents;
using Markdig.Syntax.Inlines;
using Inline = Avalonia.Controls.Documents.Inline;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders delimiter text that remains visible in a Markdown inline.
/// </summary>
public class DelimiterInlineNode : InlineNode<DelimiterInline>
{
    /// <summary>
    /// Gets the run that displays the delimiter.
    /// </summary>
    public override Inline Inline { get; }

    private readonly Run run;

    /// <summary>
    /// Initializes a new delimiter inline node.
    /// </summary>
    public DelimiterInlineNode()
    {
        Inline = run = new Run
        {
            Classes = { "Delimiter" }
        };
    }

    /// <inheritdoc/>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        DelimiterInline delimiter,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        run.Text = delimiter.ToLiteral();
        return true;
    }
}