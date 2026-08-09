using Avalonia.Controls.Documents;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A node that represents an inline that is not yet implemented.
/// </summary>
/// <param name="markdownType">The Markdig type represented by this fallback node.</param>
public class NotImplementedInlineNode(Type markdownType) : InlineNode
{
    /// <summary>
    /// Gets the fallback Avalonia inline.
    /// </summary>
    public override Inline Inline { get; } = new Run
    {
        Classes = { "NotImplementedInline" }
    };

    /// <summary>
    /// Reports whether the fallback node still represents the supplied Markdown type.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="markdownObject">The Markdown object being updated.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> when the object has the expected type.</returns>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        MarkdownObject markdownObject,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        return documentNode.GetType() == markdownType;
    }
}