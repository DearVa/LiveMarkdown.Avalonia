using Avalonia.Controls;

namespace LiveMarkdown.Avalonia.Demo.Links;

/// <summary>
/// Desktop-only operations supplied by the host; the shared and browser projects contain no native interop.
/// </summary>
public interface IDemoFileOperations
{
    /// <summary>
    /// Selects a file in its parent directory, or enters a directory without opening its contents as a file.
    /// </summary>
    Task<DemoOperationResult> RevealAsync(string path, TopLevel owner, CancellationToken cancellationToken);

    /// <summary>
    /// Lets the user choose an application for this request without changing the default association.
    /// </summary>
    Task<DemoOperationResult> OpenWithAsync(string path, TopLevel owner, CancellationToken cancellationToken);
}

/// <summary>
/// Supplies desktop capabilities at startup. Other hosts leave these unavailable.
/// </summary>
public static class DemoPlatform
{
    /// <summary>
    /// Gets or sets the file operations installed by the demo host before creating views.
    /// </summary>
    public static IDemoFileOperations? FileOperations { get; set; }
}
