using System.Runtime.InteropServices;
using LiveMarkdown.Avalonia.Demo.Links;

namespace LiveMarkdown.Avalonia.Demo.Desktop;

internal static partial class WindowsFileOperations
{
    public static DemoOperationResult Reveal(string path)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, nint.Zero, out var item, 0, out _));
        try
        {
            // With zero children, the PIDL identifies the single item to select in its parent folder.
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, nint.Zero, 0));
            return new DemoOperationResult(DemoOperationStatus.Accepted, "File selection request accepted by Explorer.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(item);
        }
    }

    public static DemoOperationResult OpenWith(string path, nint owner)
    {
        // OAIF_EXEC is required. No association-registration flags are enabled.
        var info = new OpenAsInfo { File = path, Flags = 0x00000004 | 0x00000020 };
        var result = SHOpenWithDialog(owner, ref info);
        if (result is unchecked((int)0x800704c7) or unchecked((int)0x80004004))
        {
            return new DemoOperationResult(DemoOperationStatus.Canceled, "Application selection canceled.");
        }

        Marshal.ThrowExceptionForHR(result);
        return new DemoOperationResult(DemoOperationStatus.Accepted, "Open-with request accepted. The default association is unchanged.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string File;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Class;
        public uint Flags;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, nint binding, out nint item, uint attributes, out uint resultAttributes);

    [LibraryImport("shell32.dll")]
    private static partial int SHOpenFolderAndSelectItems(nint folder, uint count, nint children, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(nint owner, ref OpenAsInfo info);
}