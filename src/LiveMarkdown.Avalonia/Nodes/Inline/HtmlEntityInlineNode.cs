using Avalonia.Controls.Documents;
using Markdig.Syntax.Inlines;
using Inline = Avalonia.Controls.Documents.Inline;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders an HTML character entity as its decoded text.
/// </summary>
public class HtmlEntityInlineNode : InlineNode<HtmlEntityInline>
{
    /// <summary>
    /// Gets the run that displays the decoded entity.
    /// </summary>
    public override Inline Inline { get; }

    private readonly Run run;

    /// <summary>
    /// Initializes a new HTML entity inline node.
    /// </summary>
    public HtmlEntityInlineNode()
    {
        Inline = run = new Run
        {
            Classes = { "HtmlEntity" }
        };
    }

    /// <inheritdoc/>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        HtmlEntityInline htmlEntity,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        run.Text = htmlEntity.Transcoded.ToString();
        return true;
    }
}