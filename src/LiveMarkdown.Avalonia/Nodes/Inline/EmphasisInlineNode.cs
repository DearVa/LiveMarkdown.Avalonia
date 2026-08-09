using Avalonia.Controls.Documents;
using Markdig.Syntax.Inlines;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A node that represents an emphasis inline (bold, italic, etc.).
/// </summary>
public class EmphasisInlineNode() : ContainerInlineNode<EmphasisInline>("Emphasis")
{
#pragma warning disable CS8620 // see https://github.com/dotnet/roslyn/issues/80024
    /// <inheritdoc/>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        EmphasisInline emphasisInline,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        var span = (Span)Inline;
        switch (emphasisInline.DelimiterChar)
        {
            case '*' when emphasisInline.DelimiterCount == 2: // bold
                span.Classes.Reset("Emphasis", "Bold", "Star");
                break;
            case '_' when emphasisInline.DelimiterCount == 2: // bold
                span.Classes.Reset("Emphasis", "Bold", "Underscore");
                break;
            case '*' when emphasisInline.DelimiterCount == 1: // italic
                span.Classes.Reset("Emphasis", "Italic", "Star");
                break;
            case '_' when emphasisInline.DelimiterCount == 1: // italic
                span.Classes.Reset("Emphasis", "Italic", "Underscore");
                break;
            case '~' when emphasisInline.DelimiterCount == 2:
                span.Classes.Reset("Emphasis", "Strikethrough", "Tilde");
                break;
            case '~' when emphasisInline.DelimiterCount == 1:
                span.Classes.Reset("Emphasis", "Subscript", "Tilde");
                break;
            case '^' when emphasisInline.DelimiterCount == 1: // 1x superscript
                span.Classes.Reset("Emphasis", "Superscript", "Caret");
                break;
            case '+' when emphasisInline.DelimiterCount == 2: // 2x underline
                span.Classes.Reset("Emphasis", "Underline", "Plus");
                break;
            case '=' when emphasisInline.DelimiterCount == 2:
                span.Classes.Reset("Emphasis", "Highlight", "Equals");
                break;
        }

        return base.UpdateCore(documentNode, emphasisInline, in change, cancellationToken);
    }
#pragma warning restore CS8620
}