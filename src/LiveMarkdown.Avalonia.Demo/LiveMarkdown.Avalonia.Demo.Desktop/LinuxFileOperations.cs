using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LiveMarkdown.Avalonia.Demo.Links;
using Tmds.DBus.Protocol;

namespace LiveMarkdown.Avalonia.Demo.Desktop;

internal static class LinuxFileOperations
{
    private const string Portal = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";

    public static async Task<DemoOperationResult> RevealAsync(string path, TopLevel owner, CancellationToken cancellationToken)
    {
        using var connection = await ConnectAsync(cancellationToken);
        try
        {
            await connection.CallMethodAsync(CreateShowItemsMessage(connection, path)).WaitAsync(cancellationToken);
            return new DemoOperationResult(DemoOperationStatus.Accepted, "File selection request accepted by the file manager.");
        }
        catch (DBusErrorReplyException exception) when (IsUnavailable(exception))
        {
            var result = await OpenPortalAsync(connection, path, shouldChooseApplication: false, cancellationToken);
            if (result.Status != DemoOperationStatus.Unavailable)
            {
                return result;
            }

            var parent = Path.GetDirectoryName(path);
            var hasAccepted = parent is not null && await owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(parent));
            return new DemoOperationResult(
                hasAccepted ? DemoOperationStatus.Accepted : DemoOperationStatus.Unavailable,
                hasAccepted ? "Opened the parent directory; this desktop cannot select the file." : "File location is unavailable on this desktop.");
        }
    }

    public static async Task<DemoOperationResult> OpenWithAsync(string path, CancellationToken cancellationToken)
    {
        using var connection = await ConnectAsync(cancellationToken);
        return await OpenPortalAsync(connection, path, shouldChooseApplication: true, cancellationToken);
    }

    private static async Task<DBusConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var connection = new DBusConnection(DBusAddress.Session ?? throw new InvalidOperationException("No desktop session bus is available."));
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static async Task<DemoOperationResult> OpenPortalAsync(
        DBusConnection connection,
        string path,
        bool shouldChooseApplication,
        CancellationToken cancellationToken)
    {
        // Subscribe before the call. A response may precede its reply; buffer by returned request path,
        // including older portals that do not follow the modern handle_token path convention.
        var responses = Channel.CreateUnbounded<(string Path, uint Code)>();
        using var subscription = await connection.AddMatchAsync(
            new MatchRule
            {
                Type = MessageType.Signal,
                Sender = Portal,
                Destination = connection.UniqueName,
                Interface = "org.freedesktop.portal.Request",
                Member = "Response",
                PathNamespace = "/org/freedesktop/portal/desktop/request"
            },
            static (message, _) => (message.PathAsString ?? string.Empty, message.GetBodyReader().ReadUInt32()),
            notification =>
            {
                if (notification.HasValue)
                {
                    responses.Writer.TryWrite(notification.Value);
                }
                else if (notification.IsCompletion)
                {
                    responses.Writer.TryComplete(notification.Exception);
                }
            });
        var token = "livemarkdown_" + Guid.NewGuid().ToString("N");
        var sender = connection.UniqueName?.TrimStart(':').Replace('.', '_');
        var request = $"/org/freedesktop/portal/desktop/request/{sender}/{token}";
        try
        {
            var version = await connection.CallMethodAsync(
                CreateVersionMessage(connection),
                static (message, _) => message.GetBodyReader().ReadVariantValue().GetUInt32()).WaitAsync(cancellationToken);
            if (version < 3)
            {
                return new DemoOperationResult(
                    DemoOperationStatus.Unavailable,
                    "This desktop portal does not support application selection or file location.");
            }

            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            request = await connection.CallMethodAsync(
                CreatePortalMessage(connection, file, shouldChooseApplication, token),
                static (message, _) => message.GetBodyReader().ReadObjectPathAsString()).WaitAsync(cancellationToken);

            while (true)
            {
                var response = await responses.Reader.ReadAsync(cancellationToken);
                if (response.Path != request)
                {
                    continue;
                }

                return response.Code switch
                {
                    0 => new DemoOperationResult(DemoOperationStatus.Accepted, "The desktop portal accepted the request."),
                    1 => new DemoOperationResult(DemoOperationStatus.Canceled, "Application selection canceled."),
                    _ => new DemoOperationResult(DemoOperationStatus.Failed, "The desktop portal could not complete the request.")
                };
            }
        }
        catch (DBusErrorReplyException exception) when (IsUnavailable(exception))
        {
            return new DemoOperationResult(DemoOperationStatus.Unavailable, "The required desktop portal capability is unavailable.");
        }
        catch (OperationCanceledException)
        {
            try
            {
                // Closing the request must outlive the cancellation that caused it.
                await connection.CallMethodAsync(CreateCloseMessage(connection, request)).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
            }
            catch (Exception exception)
            {
                await Console.Error.WriteLineAsync($"Could not close portal request: {exception.Message}", CancellationToken.None);
            }

            throw;
        }
    }

    private static bool IsUnavailable(DBusErrorReplyException exception)
    {
        return exception.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner" or
            "org.freedesktop.DBus.Error.UnknownInterface" or "org.freedesktop.DBus.Error.UnknownMethod" or
            "org.freedesktop.portal.Error.NotFound" or "org.freedesktop.portal.Error.NotSupported";
    }

    private static MessageBuffer CreateShowItemsMessage(DBusConnection connection, string path)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: "org.freedesktop.FileManager1",
            path: "/org/freedesktop/FileManager1",
            @interface: "org.freedesktop.FileManager1",
            member: "ShowItems",
            signature: "ass");
        writer.WriteArray(new[] { DemoLinkTarget.ToFileUri(path) });
        writer.WriteString(string.Empty);
        return writer.CreateMessage();
    }

    private static MessageBuffer CreatePortalMessage(
        DBusConnection connection,
        Microsoft.Win32.SafeHandles.SafeFileHandle file,
        bool shouldChooseApplication,
        string token)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: Portal,
            path: PortalPath,
            @interface: "org.freedesktop.portal.OpenURI",
            member: shouldChooseApplication ? "OpenFile" : "OpenDirectory",
            signature: "sha{sv}");
        writer.WriteString(string.Empty);
        writer.WriteHandle(file);
        var options = new Dictionary<string, VariantValue>
        {
            ["handle_token"] = token
        };
        if (shouldChooseApplication)
        {
            options["ask"] = true;
        }

        writer.WriteDictionary(options);
        return writer.CreateMessage();
    }

    private static MessageBuffer CreateVersionMessage(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: Portal,
            path: PortalPath,
            @interface: "org.freedesktop.DBus.Properties",
            member: "Get",
            signature: "ss");
        writer.WriteString("org.freedesktop.portal.OpenURI");
        writer.WriteString("version");
        return writer.CreateMessage();
    }

    private static MessageBuffer CreateCloseMessage(DBusConnection connection, string path)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Portal, path: path, @interface: "org.freedesktop.portal.Request", member: "Close");
        return writer.CreateMessage();
    }
}