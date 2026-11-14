using Avalonia.Input.Platform;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ShadUI;

namespace LiveMarkdown.Avalonia.Demo.Links;

/// <summary>
/// The single policy boundary for opening links from Markdown, menus, and file-reference buttons.
/// </summary>
public sealed class DemoLinkActions(Control owner)
{
    private static readonly HashSet<string> ExecutionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta",
        ".scr", ".cpl", ".msi", ".msp", ".reg", ".lnk", ".url", ".desktop", ".app", ".command",
        ".sh", ".bash", ".zsh", ".fish", ".py", ".pyw", ".pl", ".rb", ".jar", ".scpt"
    };

    public static bool HasExecutionExtension(string path) => ExecutionExtensions.Contains(Path.GetExtension(path.TrimEnd(' ', '.')));

    public async Task<DemoOperationResult> ExecuteAsync(
        DemoLinkTarget target,
        DemoLinkAction action,
        string label,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var topLevel = TopLevel.GetTopLevel(owner);
            if (topLevel is null)
            {
                return new DemoOperationResult(DemoOperationStatus.Unavailable, "The view is not attached to a window.");
            }

            if (action is DemoLinkAction.CopyAddress or DemoLinkAction.CopyText)
            {
                if (topLevel.Clipboard is not { } clipboard)
                {
                    return new DemoOperationResult(DemoOperationStatus.Unavailable, "Clipboard access is unavailable.");
                }

                await clipboard.SetTextAsync(action == DemoLinkAction.CopyText ? label : target.Address);
                return new DemoOperationResult(DemoOperationStatus.Accepted, "Copied to the clipboard.");
            }

            if (target.Kind == DemoLinkKind.Unsupported)
            {
                return new DemoOperationResult(DemoOperationStatus.Unavailable, target.Reason ?? "Unsupported address.");
            }

            if (target.Kind == DemoLinkKind.Web)
            {
                return action == DemoLinkAction.Open
                    ? Accepted(await topLevel.Launcher.LaunchUriAsync(new Uri(target.Address)), "Browser open request accepted.")
                    : new DemoOperationResult(DemoOperationStatus.Unavailable, "This action is only available for local files.");
            }

            if (DemoPlatform.FileOperations is not { } fileOperations)
            {
                return new DemoOperationResult(DemoOperationStatus.Unavailable, "Local path operations require the desktop demo.");
            }

            if (action == DemoLinkAction.RevealParent)
            {
                var parent = await Task.Run(() => FindExistingParent(target.Address), cancellationToken);
                return parent is null
                    ? new DemoOperationResult(DemoOperationStatus.Failed, "No accessible parent directory was found.")
                    : await fileOperations.RevealAsync(parent, topLevel, cancellationToken);
            }

            // Filesystem access occurs only after an explicit action, never during hover or address parsing.
            var attributes = await Task.Run(() => File.GetAttributes(target.Address), cancellationToken);
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            if (action == DemoLinkAction.Reveal)
            {
                return await fileOperations.RevealAsync(target.Address, topLevel, cancellationToken);
            }

            if (action == DemoLinkAction.SaveAs)
            {
                return isDirectory
                    ? new DemoOperationResult(DemoOperationStatus.Unavailable, "Save a copy supports files, not recursive directory copies.")
                    : await SaveAsAsync(topLevel, target.Address, cancellationToken);
            }

            if (action == DemoLinkAction.OpenWith && isDirectory)
            {
                return new DemoOperationResult(DemoOperationStatus.Unavailable, "Choose an application is only available for files.");
            }

            var canExecute = await Task.Run(() => IsExecutionCandidate(target.Address, isDirectory), cancellationToken);
            if (canExecute)
            {
                var choice = await DialogManager.CreateDialog(
                        new TextBlock { Text = $"Only continue if you trust this file and its source.\n\n{target.Address}", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                        action == DemoLinkAction.OpenWith ? "Choose an application for this executable target?" : "Run this file?",
                        preferredTopLevel: topLevel)
                    .WithPrimaryButton(action == DemoLinkAction.OpenWith ? "Choose application…" : "Run", buttonStyle: ButtonStyle.Destructive)
                    .WithCancelButton("Cancel")
                    .ShowAsync(cancellationToken);
                if (choice == DialogResult.HostNotFound)
                {
                    return new DemoOperationResult(DemoOperationStatus.Unavailable, "A confirmation dialog is unavailable. Nothing was opened.");
                }

                if (choice != DialogResult.Primary)
                {
                    return new DemoOperationResult(DemoOperationStatus.Canceled, "Canceled.");
                }
            }

            if (action == DemoLinkAction.OpenWith)
            {
                return await fileOperations.OpenWithAsync(target.Address, topLevel, cancellationToken);
            }

            var hasAccepted = isDirectory
                ? await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(target.Address))
                : await topLevel.Launcher.LaunchFileInfoAsync(new FileInfo(target.Address));
            return Accepted(hasAccepted, canExecute ? "Run request accepted." : "Open request accepted.");
        }
        catch (OperationCanceledException)
        {
            return new DemoOperationResult(DemoOperationStatus.Canceled, "Canceled.");
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new DemoOperationResult(DemoOperationStatus.Failed, "This target no longer exists. Copy its address or reveal an existing parent directory.");
        }
        catch (Exception exception)
        {
            return new DemoOperationResult(DemoOperationStatus.Failed, exception.Message);
        }
    }

    private static DemoOperationResult Accepted(bool hasAccepted, string message)
    {
        return new DemoOperationResult(
            hasAccepted ? DemoOperationStatus.Accepted : DemoOperationStatus.Failed,
            hasAccepted ? message : "The system did not accept the request. No associated application may be available.");
    }

    private static string? FindExistingParent(string path)
    {
        var parent = Path.GetDirectoryName(path);
        while (parent is not null)
        {
            if (Directory.Exists(parent))
            {
                return parent;
            }

            parent = Path.GetDirectoryName(parent);
        }

        return null;
    }

    private static bool IsExecutionCandidate(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return OperatingSystem.IsMacOS() && Path.GetExtension(path).Equals(".app", StringComparison.OrdinalIgnoreCase);
        }

        if (HasExecutionExtension(path))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) &
            (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
        {
            return true;
        }

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        var count = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return count >= 2 && (header[..2].SequenceEqual("MZ"u8) || header[..2].SequenceEqual("#!"u8)) ||
            count == 4 && (header.SequenceEqual<byte>([0x7f, 0x45, 0x4c, 0x46]) ||
                header.SequenceEqual<byte>([0xcf, 0xfa, 0xed, 0xfe]) || header.SequenceEqual<byte>([0xfe, 0xed, 0xfa, 0xcf]) ||
                header.SequenceEqual<byte>([0xca, 0xfe, 0xba, 0xbe]));
    }

    private static async Task<DemoOperationResult> SaveAsAsync(TopLevel topLevel, string source, CancellationToken cancellationToken)
    {
        if (!topLevel.StorageProvider.CanSave)
        {
            return new DemoOperationResult(DemoOperationStatus.Unavailable, "Save file dialogs are unavailable on this host.");
        }

        using var destination = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save a copy",
            SuggestedFileName = Path.GetFileName(source),
            ShowOverwritePrompt = true
        });
        if (destination is null)
        {
            return new DemoOperationResult(DemoOperationStatus.Canceled, "Save canceled.");
        }

        if (destination.TryGetLocalPath() is { } localPath)
        {
            await Task.Run(() => File.Copy(source, localPath, overwrite: true), cancellationToken);
        }
        else
        {
            await using var input = File.OpenRead(source);
            await using var output = await destination.OpenWriteAsync();
            await input.CopyToAsync(output, cancellationToken);
            if (output.CanSeek)
            {
                output.SetLength(output.Position);
            }
        }

        return new DemoOperationResult(DemoOperationStatus.Accepted, "Saved a byte-for-byte copy. The original file is unchanged.");
    }
}
