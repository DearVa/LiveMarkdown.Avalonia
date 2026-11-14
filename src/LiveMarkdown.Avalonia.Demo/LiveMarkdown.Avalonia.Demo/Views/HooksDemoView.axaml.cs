using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using LiveMarkdown.Avalonia.Demo.Links;
using LiveMarkdown.Avalonia.Demo.Links.Preview;
using Markdig;

namespace LiveMarkdown.Avalonia.Demo.Views;

public partial class HooksDemoView : UserControl
{
    internal WebPreviewService? Previews { get; init; }

    private const string InitialMarkdown = """
        # A renderer you can customize

        This [link with **nested formatting**](https://docs.avaloniaui.net/) keeps its trailing arrow through streaming updates.

        ```csharp
        using var registration = renderer.NodeHooks.Register<LinkInlineNode>(
            initialize: Initialize,
            beforeUpdate: BeforeUpdate,
            afterUpdate: AfterUpdate,
            cleanup: Cleanup);
        ```

        Select this paragraph, including the link label. Copying omits the decorative icons.
        """;
    private const string ComparisonMarkdown = "# Another renderer\n\n[An ordinary link](https://github.com/DearVa/LiveMarkdown.Avalonia) stays untouched by local hooks.";

    private readonly ObservableStringBuilder _source = new();
    private readonly Dictionary<LinkInlineNode, InlineUIContainer> _icons = [];
    private readonly Queue<string> _log = new();
    private IDisposable? _linkHook;
    private IDisposable? _codeHook;
    private IDisposable? _globalHook;
    private DemoLinkInteraction? _primaryInteraction;
    private DemoLinkInteraction? _apiInteraction;
    private DemoLinkInteraction? _secondaryInteraction;
    private CancellationTokenSource? _streamCancellation;
    private bool _isAttached;
    private int _initializeCount;
    private int _beforeCount;
    private int _afterCount;
    private int _cleanupCount;

    public HooksDemoView()
    {
        InitializeComponent();
        AttachedToVisualTree += HandleAttached;
        DetachedFromVisualTree += HandleDetached;
        SizeChanged += HandleSizeChanged;
    }

    private void HandleAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        _primaryInteraction = new DemoLinkInteraction(this, PrimaryRenderer, ReportLink, Previews);
        _apiInteraction = new DemoLinkInteraction(this, ApiRenderer, ReportLink, Previews);
        _secondaryInteraction = new DemoLinkInteraction(this, SecondaryRenderer, ReportLink, Previews);
        PrimaryRenderer.MarkdownBuilder = _source;
        ConfigureHooks();
        ResetContent();
    }

    private void HandleDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _streamCancellation?.Cancel();
        _primaryInteraction?.Dispose();
        _apiInteraction?.Dispose();
        _secondaryInteraction?.Dispose();
        _primaryInteraction = null;
        _apiInteraction = null;
        _secondaryInteraction = null;
        _linkHook?.Dispose();
        _codeHook?.Dispose();
        _globalHook?.Dispose();
        _linkHook = null;
        _codeHook = null;
        _globalHook = null;
        PrimaryRenderer.MarkdownBuilder = null;
        PrimaryRenderer.DocumentUpdate = null;
        SecondaryRenderer.DocumentUpdate = null;
        ApiRenderer.DocumentUpdate = null;
    }

    private void HandleHookSettingsChanged(object? sender, RoutedEventArgs e)
    {
        if (!_isAttached)
        {
            return;
        }

        ConfigureHooks();
        // Registration itself does not render. Reapply source so the switches show the next-update effect.
        Render(PrimaryRenderer, _source.ToString());
        Render(SecondaryRenderer, ComparisonMarkdown);
    }

    private void ConfigureHooks()
    {
        if (LocalHooksToggle.IsChecked == true && _linkHook is null)
        {
            _linkHook = PrimaryRenderer.NodeHooks.Register<LinkInlineNode>(
                initialize: context =>
                {
                    _icons.Add(context.Node, new InlineUIContainer(new global::Avalonia.Controls.Shapes.Path
                    {
                        Width = 12,
                        Height = 12,
                        Margin = new Thickness(5, 0, 0, 0),
                        Fill = new SolidColorBrush(Color.Parse("#3B82F6")),
                        Stretch = Stretch.Uniform,
                        IsHitTestVisible = false,
                        Data = Geometry.Parse("M1 5H8V2L12 6L8 10V7H1Z")
                    }));
                    context.Node.Inline.Classes.Add("hook-link");
                    _initializeCount++;
                    Log("local: initialize link");
                },
                beforeUpdate: context =>
                {
                    context.Node.Inlines.Remove(_icons[context.Node]);
                    _beforeCount++;
                    Log("local: beforeUpdate");
                },
                afterUpdate: context =>
                {
                    if (context.Result == NodeUpdateResult.Applied &&
                        context.Update.MarkdownObject is Markdig.Syntax.Inlines.LinkInline { IsImage: false })
                    {
                        context.Node.Inlines.Add(_icons[context.Node]);
                    }

                    _afterCount++;
                    Log($"local: afterUpdate → {context.Result}");
                },
                cleanup: context =>
                {
                    context.Node.Inlines.Remove(_icons[context.Node]);
                    context.Node.Inline.Classes.Remove("hook-link");
                    _icons.Remove(context.Node);
                    _cleanupCount++;
                    Log("local: cleanup link");
                });
            _codeHook = PrimaryRenderer.NodeHooks.Register<CodeBlockNode>(
                initialize: context => context.Node.Control.Classes.Add("hook-code"),
                cleanup: context => context.Node.Control.Classes.Remove("hook-code"));
            Log("registered local link + code hooks");
        }
        else if (LocalHooksToggle.IsChecked != true && _linkHook is not null)
        {
            _linkHook.Dispose();
            _codeHook?.Dispose();
            _linkHook = null;
            _codeHook = null;
            Log("disposed local registrations");
        }

        if (GlobalHooksToggle.IsChecked == true && _globalHook is null)
        {
            _globalHook = MarkdownNode.GlobalHooks.Register<HeadingBlockNode>(
                initialize: context =>
                {
                    context.Node.Control.Classes.Add("hook-heading");
                    Log("global: initialize heading");
                },
                cleanup: context =>
                {
                    context.Node.Control.Classes.Remove("hook-heading");
                    Log("global: cleanup heading");
                });
            Log("registered global heading hook");
        }
        else if (GlobalHooksToggle.IsChecked != true && _globalHook is not null)
        {
            _globalHook.Dispose();
            _globalHook = null;
            Log("disposed global registration");
        }
    }

    private void ResetContent()
    {
        _streamCancellation?.Cancel();
        _source.Clear();
        _source.Append(InitialMarkdown);
        SourceText.Text = InitialMarkdown;
        Render(SecondaryRenderer, ComparisonMarkdown);
        Render(ApiRenderer, "Use `renderer.NodeHooks` for one renderer, or `MarkdownNode.GlobalHooks` for all renderers. Dispose the handle to run cleanup on live participants.");
    }

    private async void HandleStream(object? sender, RoutedEventArgs e)
    {
        _streamCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _streamCancellation = cancellation;
        try
        {
            _source.Clear();
            for (var index = 0; index < InitialMarkdown.Length; index += 9)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                _source.Append(InitialMarkdown.Substring(index, Math.Min(9, InitialMarkdown.Length - index)));
                SourceText.Text = _source.ToString();
                await Task.Delay(70, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Log("stream canceled");
        }
        finally
        {
            if (ReferenceEquals(_streamCancellation, cancellation))
            {
                _streamCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void HandleReset(object? sender, RoutedEventArgs e) => ResetContent();

    private void HandleClearLog(object? sender, RoutedEventArgs e)
    {
        _log.Clear();
        LifecycleLog.Text = string.Empty;
    }

    private void Log(string message)
    {
        _log.Enqueue(message);
        while (_log.Count > 18)
        {
            _log.Dequeue();
        }

        LifecycleLog.Text = string.Join(Environment.NewLine, _log);
        LifecycleCounts.Text = $"initialize {_initializeCount}    beforeUpdate {_beforeCount}    afterUpdate {_afterCount}    cleanup {_cleanupCount}";
    }

    private static void Render(MarkdownRenderer renderer, string markdown)
    {
        renderer.DocumentUpdate = new MarkdownDocumentUpdate.Full(Markdown.Parse(markdown, MarkdownUpdateProducer.DefaultPipeline));
    }

    private void ReportLink(DemoLinkTarget target, DemoOperationResult result) => LinkStatus.Text = result.Message;

    private void HandleSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var isNarrow = e.NewSize.Width < 800;
        PlaygroundGrid.ColumnDefinitions = new ColumnDefinitions(isNarrow ? "*" : "3*,24,2*");
        Grid.SetColumn(ComparisonPanel, isNarrow ? 0 : 2);
        Grid.SetRow(ComparisonPanel, isNarrow ? 1 : 0);
        ComparisonPanel.Margin = isNarrow ? new Thickness(0, 24, 0, 0) : default;
    }
}
