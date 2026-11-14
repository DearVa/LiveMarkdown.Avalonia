using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveMarkdown.Avalonia.Demo.Links;
using LiveMarkdown.Avalonia.Demo.Links.Preview;
using Markdig;

namespace LiveMarkdown.Avalonia.Demo.Views;

public partial class LinkDemoView : UserControl
{
    internal WebPreviewService? Previews { get; init; }

    private DemoLinkInteraction? _interaction;
    private string? _referencePath;

    public LinkDemoView()
    {
        InitializeComponent();
        AttachedToVisualTree += HandleAttached;
        DetachedFromVisualTree += HandleDetached;
        SizeChanged += HandleSizeChanged;
        FileReference.ContextRequested += HandleReferenceContextRequested;
    }

    private void HandleAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _interaction = new DemoLinkInteraction(this, LinksRenderer, Report, Previews);
        PlatformNote.Text = DemoPlatform.FileOperations is null
            ? "Local path actions are unavailable in this host. Use the desktop demo to try them."
            : "Opening executable files, scripts and shortcuts requires confirmation. Canceling a picker is not an error.";

        string markdown;
        if (DemoPlatform.FileOperations is not null)
        {
            // These are deliberately inert fixtures. The real demo executable is linked separately.
            var folder = Path.Combine(Path.GetTempPath(), "LiveMarkdown.Avalonia.Demo", "link samples 中文", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            _referencePath = Path.Combine(folder, "中文 notes with spaces.txt");
            File.WriteAllText(_referencePath, "LiveMarkdown link playground.\nChinese paths and spaces are intentional.\n");
            var binary = Path.Combine(folder, "sample.bin");
            File.WriteAllBytes(binary, [0x00, 0x01, 0x02, 0xff]);
            var unknown = Path.Combine(folder, "sample.unknown-extension");
            File.WriteAllText(unknown, "A file without a registered application.\n");
            var script = Path.Combine(folder, OperatingSystem.IsWindows() ? "sample.cmd" : "sample.sh");
            File.WriteAllText(script, OperatingSystem.IsWindows() ? "@echo This is a harmless demo script.\r\n" : "#!/bin/sh\necho 'This is a harmless demo script.'\n");
            markdown = $"""
                ### Local targets

                [Text file — Chinese name and spaces](<{DemoLinkTarget.ToFileUri(_referencePath)}>)

                [Binary file (.bin)](<{DemoLinkTarget.ToFileUri(binary)}>) · [Unknown file type](<{DemoLinkTarget.ToFileUri(unknown)}>)

                [Script — confirmation required](<{DemoLinkTarget.ToFileUri(script)}>)

                [Directory](<{DemoLinkTarget.ToFileUri(folder)}>) · [Missing file](<{DemoLinkTarget.ToFileUri(Path.Combine(folder, "missing", "not here.txt"))}>)
                """;
            if (Environment.ProcessPath is { } executable)
            {
                markdown += $"\n\n[This demo executable](<{DemoLinkTarget.ToFileUri(executable)}>)";
            }

            AddressInput.Text = _referencePath;
        }
        else
        {
            markdown = "### Local targets\n\nOpen the desktop demo for real filesystem samples. No files are created by this host.";
        }

        markdown += """


            ### Web and edge cases

            [Avalonia documentation](https://docs.avaloniaui.net/) · [LiveMarkdown source](https://github.com/DearVa/LiveMarkdown.Avalonia)

            [W3C: hover or focus](https://www.w3.org/WAI/WCAG22/Understanding/content-on-hover-or-focus.html) · [GitHub: OpenTelemetry .NET](https://github.com/open-telemetry/opentelemetry-dotnet)

            [A deliberately long **formatted link that wraps across multiple lines while remaining selectable**, 中文与 spaces included](https://example.com/a/long/path?keep=QueryCase)

            [Relative path — no base](relative/file.txt) · [Unsupported scheme](custom-demo:example)
            """;
        FileReference.IsEnabled = _referencePath is not null;
        ToolTip.SetTip(FileReference, _referencePath);
        SourceText.Text = markdown;
        LinksRenderer.DocumentUpdate = new MarkdownDocumentUpdate.Full(Markdown.Parse(markdown, MarkdownUpdateProducer.DefaultPipeline));
    }

    private void HandleDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _interaction?.Dispose();
        _interaction = null;
        LinksRenderer.DocumentUpdate = null;
    }

    private void Report(DemoLinkTarget target, DemoOperationResult result)
    {
        OperationStatus.Text = result.Message;
        OperationPath.Text = target.Address;
        AddressInput.Text = target.Address;
    }

    private async void HandleTryAddress(object? sender, RoutedEventArgs e)
    {
        if (_interaction is not { } interaction)
        {
            return;
        }

        var target = DemoLinkTarget.Parse(AddressInput.Text ?? string.Empty);
        await interaction.ExecuteAsync(target, target.Kind == DemoLinkKind.Local ? DemoLinkAction.Reveal : DemoLinkAction.Open);
    }

    private async void HandleCopyAddress(object? sender, RoutedEventArgs e)
    {
        if (_interaction is { } interaction)
        {
            await interaction.ExecuteAsync(DemoLinkTarget.Parse(AddressInput.Text ?? string.Empty), DemoLinkAction.CopyAddress);
        }
    }

    private async void HandleRevealParent(object? sender, RoutedEventArgs e)
    {
        if (_interaction is { } interaction)
        {
            await interaction.ExecuteAsync(DemoLinkTarget.Parse(AddressInput.Text ?? string.Empty), DemoLinkAction.RevealParent);
        }
    }

    private async void HandleFileReference(object? sender, RoutedEventArgs e)
    {
        if (_interaction is { } interaction && _referencePath is not null)
        {
            await interaction.ExecuteAsync(DemoLinkTarget.Parse(_referencePath), DemoLinkAction.Reveal);
        }
    }

    private void HandleReferenceContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_interaction is { } interaction && _referencePath is not null)
        {
            interaction.OpenContextMenu(FileReference, DemoLinkTarget.Parse(_referencePath), Path.GetFileName(_referencePath));
            e.Handled = true;
        }
    }

    private void HandleSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var isNarrow = e.NewSize.Width < 800;
        ExamplesGrid.ColumnDefinitions = new ColumnDefinitions(isNarrow ? "*" : "3*,24,2*");
        Grid.SetColumn(ReferencePanel, isNarrow ? 0 : 2);
        Grid.SetRow(ReferencePanel, isNarrow ? 1 : 0);
        ReferencePanel.Margin = isNarrow ? new Thickness(0, 24, 0, 0) : default;
    }
}
