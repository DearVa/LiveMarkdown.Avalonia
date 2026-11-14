using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LiveMarkdown.Avalonia.Demo.Links;

namespace LiveMarkdown.Avalonia.Demo.Desktop;

/// <summary>
/// Routes explicit demo file actions to the current desktop's shell or portal.
/// </summary>
public sealed class DesktopFileOperations : IDemoFileOperations
{
    /// <inheritdoc />
    public async Task<DemoOperationResult> RevealAsync(string path, TopLevel owner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(path))
        {
            var hasAccepted = await owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
            return new DemoOperationResult(
                hasAccepted ? DemoOperationStatus.Accepted : DemoOperationStatus.Failed,
                hasAccepted ? "Directory open request accepted." : "The file manager did not accept the directory.");
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsFileOperations.Reveal(path);
        }

        if (OperatingSystem.IsMacOS())
        {
            return await RunOpenAsync(["-R", "--", path], cancellationToken);
        }

        return await LinuxFileOperations.RevealAsync(path, owner, cancellationToken);
    }

    /// <inheritdoc />
    public Task<DemoOperationResult> OpenWithAsync(string path, TopLevel owner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            return Task.FromResult(WindowsFileOperations.OpenWith(path, owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero));
        }

        return OperatingSystem.IsMacOS() ?
            MacFileOperations.OpenWithAsync(path, owner, cancellationToken) :
            LinuxFileOperations.OpenWithAsync(path, cancellationToken);
    }

    internal static async Task<DemoOperationResult> RunOpenAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("/usr/bin/open")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the system open service.");
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 ?
            new DemoOperationResult(DemoOperationStatus.Accepted, "The system accepted the request.") :
            new DemoOperationResult(DemoOperationStatus.Failed, string.IsNullOrWhiteSpace(error) ? "The system rejected the request." : error.Trim());
    }
}