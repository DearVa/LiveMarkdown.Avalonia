using Markdig.Extensions.Alerts;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders a Markdig alert block and its child blocks.
/// </summary>
public sealed class AlertBlockNode : ContainerBlockNode<AlertBlock>
{
    /// <summary>
    /// Initializes an alert block node.
    /// </summary>
    public AlertBlockNode()
    {
        container.Classes.Add("AlertBlock");
    }
}