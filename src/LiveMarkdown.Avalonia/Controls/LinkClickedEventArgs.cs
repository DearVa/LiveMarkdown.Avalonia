using Avalonia.Interactivity;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Event arguments for the LinkClicked event.
/// </summary>
/// <param name="routedEvent"></param>
/// <param name="source">The text block raising the event, or a caller-supplied event source.</param>
/// <param name="href"></param>
public class LinkClickedEventArgs(RoutedEvent routedEvent, object source, Uri? href) : RoutedEventArgs(routedEvent, source)
{
    /// <summary>
    /// Gets the URI associated with the clicked link.
    /// </summary>
    public Uri? HRef => href;

    /// <summary>
    /// Gets the concrete link for renderer-generated clicks. Null for legacy event arguments
    /// constructed with only a URI. Use the source text block to query current inline geometry.
    /// </summary>
    public Link? Link { get; init; }
}