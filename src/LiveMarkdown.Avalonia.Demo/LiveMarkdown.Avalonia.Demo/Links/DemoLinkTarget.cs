namespace LiveMarkdown.Avalonia.Demo.Links;

/// <summary>
/// Classifies link text without touching the filesystem or resolving relative paths.
/// </summary>
public sealed record DemoLinkTarget(DemoLinkKind Kind, string Address, string? Reason = null)
{
    public bool IsNetworkPath => Kind == DemoLinkKind.Local &&
        (Address.StartsWith(@"\\", StringComparison.Ordinal) || Address.StartsWith("//", StringComparison.Ordinal));

    /// <summary>
    /// Encodes a native path, including literal percent characters, as a file URI for Markdown or D-Bus.
    /// </summary>
    public static string ToFileUri(string absolutePath)
    {
        var path = OperatingSystem.IsWindows() ? absolutePath.Replace('\\', '/') : absolutePath;
        var escaped = Uri.EscapeDataString(path).Replace("%2F", "/", StringComparison.Ordinal);
        if (OperatingSystem.IsWindows())
        {
            if (path.StartsWith("//", StringComparison.Ordinal))
            {
                return "file:" + escaped;
            }

            // Only the drive separator is URI syntax. Percent signs in native filenames are data.
            return "file:///" + path[..2] + escaped[4..];
        }

        return "file://" + escaped;
    }

    public static DemoLinkTarget Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return new DemoLinkTarget(DemoLinkKind.Unsupported, value, "The address is empty or contains control characters.");
        }

        // Check native paths before URI parsing: a literal '%' in a filename is not URI encoding.
        var isWindowsPath = value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/';
        var isUncPath = value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal);
        if (isWindowsPath || isUncPath)
        {
            return OperatingSystem.IsWindows()
                ? Local(value)
                : new DemoLinkTarget(DemoLinkKind.Unsupported, value, "This Windows path does not belong to the current platform.");
        }

        if (value.StartsWith('/'))
        {
            return OperatingSystem.IsWindows()
                ? new DemoLinkTarget(DemoLinkKind.Unsupported, value, "A Unix path cannot be resolved as a local Windows path.")
                : Local(value);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return new DemoLinkTarget(DemoLinkKind.Unsupported, value, "An absolute address is required. Relative paths have no base here.");
        }

        if (uri.Scheme is "http" or "https")
        {
            return new DemoLinkTarget(DemoLinkKind.Web, uri.AbsoluteUri);
        }

        if (uri.IsFile)
        {
            if (!OperatingSystem.IsWindows() && !string.IsNullOrEmpty(uri.Host) && uri.Host != "localhost")
            {
                return new DemoLinkTarget(DemoLinkKind.Unsupported, value, "This file URI names another host, not a mounted local path.");
            }

            var path = uri.LocalPath;
            if (!Path.IsPathFullyQualified(path))
            {
                return new DemoLinkTarget(DemoLinkKind.Unsupported, value, "The file URI does not contain an absolute local path.");
            }

            // Uri may map file:///C:/... to /C:/... on Unix. Do not mistake it for a native path.
            if (!OperatingSystem.IsWindows() && path.Length >= 4 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':')
            {
                return new DemoLinkTarget(DemoLinkKind.Unsupported, value, "This file URI contains a Windows drive path.");
            }

            return Local(path);
        }

        return new DemoLinkTarget(DemoLinkKind.Unsupported, value, $"The '{uri.Scheme}' scheme is not enabled in this demo.");
    }

    private static DemoLinkTarget Local(string path)
    {
        try
        {
            if (path.Any(char.IsControl))
            {
                return new DemoLinkTarget(DemoLinkKind.Unsupported, path, "The decoded path contains control characters.");
            }

            var absolutePath = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows() &&
                (absolutePath.StartsWith(@"\\?\", StringComparison.Ordinal) || absolutePath.StartsWith(@"\\.\", StringComparison.Ordinal)))
            {
                return new DemoLinkTarget(DemoLinkKind.Unsupported, path, "Device paths are not supported.");
            }

            return new DemoLinkTarget(DemoLinkKind.Local, absolutePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new DemoLinkTarget(DemoLinkKind.Unsupported, path, exception.Message);
        }
    }
}

public enum DemoLinkKind
{
    Web,
    Local,
    Unsupported
}

public enum DemoLinkAction
{
    Reveal,
    Open,
    OpenWith,
    SaveAs,
    CopyAddress,
    CopyText,
    RevealParent
}

public enum DemoOperationStatus
{
    Accepted,
    Canceled,
    Unavailable,
    Failed
}

/// <summary>
/// Accepted means that the platform accepted the request, not that another application finished opening it.
/// </summary>
public sealed record DemoOperationResult(DemoOperationStatus Status, string Message);
