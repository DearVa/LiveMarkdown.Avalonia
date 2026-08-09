using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders a Markdown paragraph and its inline content.
/// </summary>
public sealed class ParagraphBlockNode : InlineCollectionNode<ParagraphBlock>
{
    /// <summary>
    /// Initializes a new paragraph block node.
    /// </summary>
    public ParagraphBlockNode()
    {
        Control.Classes.Add("ParagraphBlock");
    }
}