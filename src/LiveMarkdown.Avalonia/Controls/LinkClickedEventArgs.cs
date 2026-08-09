using Avalonia.Interactivity;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Event arguments for the LinkClicked event.
/// </summary>
/// <param name="routedEvent"></param>
/// <param name="source">Must be <see cref="Link"/></param>
/// <param name="href"></param>
public class LinkClickedEventArgs(RoutedEvent routedEvent, object source, Uri? href) : RoutedEventArgs(routedEvent, source)
{
    /// <summary>
    /// Gets the URI associated with the clicked link.
    /// </summary>
    public Uri? HRef => href;
}