namespace LiveMarkdown.Avalonia;

/// <summary>Controls how newly streamed Markdown text appears.</summary>
public enum MarkdownStreamingRevealMode
{
    /// <summary>Draw text immediately, without animation.</summary>
    None = 0,

    /// <summary>Fade in each newly appearing layout line.</summary>
    Line = 1,

    /// <summary>Fade in each newly streamed text chunk while newly appearing lines rise gently.</summary>
    Character = 2,
}
