using Avalonia.Controls;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders a Markdown block quote.
/// </summary>
public sealed class QuoteBlockNode : ContainerBlockNode<QuoteBlock>
{
    /// <summary>
    /// Gets the bordered control that displays the quote.
    /// </summary>
    public override Control Control { get; }

    /// <summary>
    /// Initializes a new quote block node.
    /// </summary>
    public QuoteBlockNode()
    {
        Control = new Border
        {
            Classes = { "QuoteBlock" },
            Child = container
        };
    }
}