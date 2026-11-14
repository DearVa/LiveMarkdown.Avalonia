using System.Runtime.InteropServices;
using Avalonia.Controls;
using LiveMarkdown.Avalonia.Demo.Links;
using ShadUI;

namespace LiveMarkdown.Avalonia.Demo.Desktop;

internal static partial class MacFileOperations
{
    public static async Task<DemoOperationResult> OpenWithAsync(string path, TopLevel owner, CancellationToken cancellationToken)
    {
        var applications = GetApplications(path);
        if (applications.Count == 0)
        {
            return new DemoOperationResult(DemoOperationStatus.Unavailable, "macOS did not report an application for this file type.");
        }

        var picker = new ListBox { MinWidth = 320, MaxHeight = 320, SelectedIndex = 0 };
        foreach (var application in applications)
        {
            picker.Items.Add(Path.GetFileNameWithoutExtension(application));
        }

        var choice = await DialogManager
            .CreateDialog(picker, "Open once with…", preferredTopLevel: owner)
            .WithPrimaryButton("Open")
            .WithCancelButton("Cancel")
            .ShowAsync(cancellationToken);
        if (choice == DialogResult.HostNotFound)
        {
            return new DemoOperationResult(DemoOperationStatus.Unavailable, "An application selection dialog is unavailable. Nothing was opened.");
        }

        if (choice != DialogResult.Primary || picker.SelectedIndex < 0)
        {
            return new DemoOperationResult(DemoOperationStatus.Canceled, "Application selection canceled.");
        }

        return await DesktopFileOperations.RunOpenAsync(["-a", applications[picker.SelectedIndex], "--", path], cancellationToken);
    }

    private static List<string> GetApplications(string path)
    {
        var pool = objc_autoreleasePoolPush();
        var nativePath = CFStringCreateWithCharacters(nint.Zero, path, path.Length);
        try
        {
            var url = Send(SendClass("NSURL"), Selector("fileURLWithPath:"), nativePath);
            var workspace = Send(SendClass("NSWorkspace"), Selector("sharedWorkspace"));
            var urls = Send(workspace, Selector("URLsForApplicationsToOpenURL:"), url);
            var count = (int)Send(urls, Selector("count"));
            var applications = new List<string>(count);
            for (var index = 0; index < count; index++)
            {
                var application = Send(urls, Selector("objectAtIndex:"), index);
                var applicationPath = Send(application, Selector("path"));
                var utf8 = Send(applicationPath, Selector("UTF8String"));
                if (Marshal.PtrToStringUTF8(utf8) is { } value)
                {
                    applications.Add(value);
                }
            }

            return applications;
        }
        finally
        {
            CFRelease(nativePath);
            objc_autoreleasePoolPop(pool);
        }
    }

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static partial nint SendClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static partial nint Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint instance, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint instance, nint selector, nint argument);

    [LibraryImport("/usr/lib/libobjc.A.dylib")]
    private static partial nint objc_autoreleasePoolPush();

    [LibraryImport("/usr/lib/libobjc.A.dylib")]
    private static partial void objc_autoreleasePoolPop(nint pool);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CFStringCreateWithCharacters(nint allocator, string characters, nint length);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRelease(nint value);
}
