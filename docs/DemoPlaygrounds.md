# Link and Hooks playgrounds

Run `dotnet run --project src/LiveMarkdown.Avalonia.Demo/LiveMarkdown.Avalonia.Demo.Desktop` from the repository root,
then select **Link** or **Hooks** in the navigation bar. Both pages are created on demand and released when navigating
away. The link icons, actions, and hover behavior also apply to the rendered Markdown on the ordinary sample pages.

## Local links reveal first

The Link page creates a unique sample directory under the OS temporary directory:
`LiveMarkdown.Avalonia.Demo/link samples 中文/<guid>`. It contains a text file with Chinese characters and spaces,
an inert binary, an unknown extension, and a harmless script. There are also links to the directory, a missing file,
and the running executable. Samples remain in the temporary directory so Explorer/Finder does not lose a selected file
when the page is closed. They can be removed after testing.

- Left-click a local link to select it in its file manager; directories open directly.
- Right-click for default open, choose application, save a byte-for-byte copy, copy path/text, or reveal an existing parent.
- The file-reference button uses the same controller and action service as the Markdown links.
- Enter an absolute native path, `file:` URI, or HTTP(S) URL in the address field to test another target.
- Local hover shows a decoded path after 500 ms without filesystem access. File icons use Lucide and categorize common
  extensions without inspecting the files.

Relative paths have no implicit base. Foreign-platform paths, device paths, and unsupported URI schemes are rejected.
Network shares are recognized syntactically on Windows and accessed only after an explicit action. File URI encoding
preserves spaces, Unicode, `#`, and literal percent characters without treating a native filename as already URI-encoded.

`DemoLinkActions` owns parsing-policy consumption, action dispatch, execution confirmation, and result reporting.
Execution candidates include script/shortcut/installer extensions, Unix executable permissions, and common executable
headers even when the extension is unknown. The default-open and open-with paths use the same confirmation boundary.
The menu uses the extension as its initial Run/Open hint; the action performs the full check before launching anything.

Saving a copy uses Avalonia's storage picker, supports files only, and never recursively copies a directory.
Canceling a dialog or picker is a neutral result. Choosing the source as its own destination fails without truncating it.
Results distinguish an accepted OS request, cancellation, unavailable capability, and failure. An accepted request does
not establish that the destination application finished opening a file. Default-open failure never falls back to reveal
while reporting success.

## Native routes belong to the desktop host

`IDemoFileOperations` supplies only reveal and application selection. Other hosts leave `DemoPlatform.FileOperations`
unset and report local operations as unavailable; clipboard and HTTP(S) links remain independent.

| Desktop | Reveal a file | Choose application for one open |
| --- | --- | --- |
| Windows | `SHParseDisplayName` and `SHOpenFolderAndSelectItems` | `SHOpenWithDialog`, execute without association-registration flags |
| macOS | `/usr/bin/open -R` with separate arguments | `NSWorkspace` lists applications; a picker launches `/usr/bin/open -a` |
| Linux | `org.freedesktop.FileManager1.ShowItems`, then Portal `OpenDirectory`, then an explicitly reported parent-only fallback | Portal `OpenFile` with `ask=true`; an unavailable chooser is reported, not replaced with default open |

The desktop project explicitly references its existing transitive `Tmds.DBus.Protocol` 0.94.1 dependency. The shared
demo and library do not reference it. Portal requests subscribe before making the call, correlate responses with the
returned request path, and close outstanding requests on cancellation. Platform calls use native APIs, D-Bus, or a
fixed executable with an argument list rather than shell command interpolation.

Primary contracts: [Windows reveal](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems),
[Windows application chooser](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenwithdialog),
[NSWorkspace application discovery](https://developer.apple.com/documentation/appkit/nsworkspace/urlsforapplications(toopen:)-ualk),
[Portal OpenURI/OpenFile/OpenDirectory](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.OpenURI.html),
[Portal request lifetime](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Request.html).

## Hooks demonstrate ownership, not a tree-rewrite API

The primary renderer adds local link and code-block hooks to the shared Demo link behavior. Of the playground hooks,
only the optional global heading hook affects the comparison renderer. Switches explicitly reapply the source:
registering a hook alone does not trigger rendering.
The four delegates are registered atomically with the existing `initialize`, `beforeUpdate`, `afterUpdate`, and `cleanup`
parameters; disposing a registration cleans up its live participants.

The local link hook retains a trailing arrow per node, separate from the shared leading site or file icon. Before
an update it removes its arrow from the indexed inline collection; after an applied text-link update it restores the
same arrow. Cleanup removes the arrow and the host-added class. Only the icons are UI containers, so the label still
wraps and can be selected and copied as text.

Streaming uses the normal `ObservableStringBuilder` path. Navigating away cancels streaming, closes menus/tooltips,
disposes local/global registrations, and releases the rendered documents. The lifecycle counters track the local link
hook; the bounded log also includes global heading events.

`DemoLinkInteraction` retains one menu and one tooltip per renderer. Menu items are created when the renderer assigns
the requested Link as the menu's DataContext, immediately before opening. Avalonia's programmatic `ContextMenu.Open`
does not raise `Opening`. Closing clears the target and items; disposal restores the renderer's previous menu.
The tooltip has a logical owner for theme/template inheritance and uses the hovered wrapped fragment as its anchor.

## Web preview starts with the address

Hover a web link for about 150 ms in the desktop Demo. Moving within that link does not restart the delay.
The card shows the hostname and starts loading without a click. To open the page, click the original link.
Move off the link, press Escape, or scroll to dismiss the preview. Selection, context menus, and target changes also dismiss it.

The card adds plain-text title and description as soon as the HTML head has been parsed, without waiting for the page
body or images. It looks for [Open Graph](https://ogp.me/) metadata first, then Twitter card fields and ordinary
HTML title/description. Cover and favicon are optional.
If the declared favicon is missing or unusable, the client tries `/favicon.ico` on the final page's origin.
The favicon updates the card and the inline link as soon as it arrives, independently of the cover.
The card uses the same hostname throughout; metadata cannot change the original link's destination. A Demo-local adaptation
of Everywhere's `ContentSizeTransitionPanel` animates the measured viewport over 180 ms. There is no skeleton or
internal scrollbar. The inline icon starts as a Lucide globe; no requests are triggered by rendering the link itself.

Only the desktop Demo uses the dedicated preview client. It does not send browser cookies, credentials, or a referrer,
execute scripts, or load arbitrary HTML resources. Page, redirects, favicon, and cover all use the same policy:

- HTTP(S) only, standard ports and no user information. Direct connections resolve the host and connect only to checked
  public addresses, with normal TLS hostname validation.
- Requests use [.NET's system or environment proxy settings](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.defaultproxy).
  When routed through a proxy, the same URL checks still apply, and the configured proxy resolves the destination.
- At most three redirects per request, two simultaneous preview jobs, and a total budget of ten seconds. The
  declared favicon and cover each get two seconds; the root favicon gets four seconds to allow a fresh connection.
  The parser reads up to 512 KiB of decompressed HTML and stops when the head ends. A large page can still produce a preview.
  Cover responses are capped at 2 MiB and favicon responses at 128 KiB after decompression. Only bounded raster
  images are decoded and cached as small single-frame PNGs. Unavailable media does not remove text already fetched.
- The main view shares one in-memory cache of up to 12 results across the sample, Link, and Hooks pages. Successful
  results are fresh for ten minutes; failed results for thirty seconds. In-progress requests for the same URL are shared.
  The cache ignores fragments and preserves path and query case. Changing hover or navigating cancels the old card's
  wait; detaching the main view cancels any outstanding requests and clears the cache.

## Verification

```shell
dotnet build src/LiveMarkdown.Avalonia.Demo/LiveMarkdown.Avalonia.Demo.Desktop
dotnet test tests/LiveMarkdown.Avalonia.Tests
```

The library tests cover link hit testing, selection, and hook lifecycles. In the desktop Demo, hover the two GitHub
samples on Link: the address and text should appear before any slow media, and a favicon should update both the card
and inline link without waiting for the cover. Revisit LiveMarkdown Source on Hooks to check that it reuses the result.
Move off the link, scroll, or press Escape to dismiss the card; dragging over the label should still select text.

For native acceptance, use Link on each target desktop: reveal a text file, BIN, executable, unknown type, and directory;
cancel and complete application selection; save to a new file and cancel the picker; try a missing path. Check light/dark
themes, a narrow window, display scaling, wrapped links, selection drag, scrolling, and navigation while a tooltip/menu
is open. Native dialogs and file-manager selection require inspection on their respective OS.
