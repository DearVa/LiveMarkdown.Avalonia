// @author https://github.com/DearVa
// @author https://github.com/AuroraZiling
// @author https://github.com/SlimeNull

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Logging;
using Avalonia.Threading;
using Markdig;
using TextMateSharp.Grammars;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders a Markdown document into an Avalonia visual tree and coordinates selection,
/// link interaction, text search, and asynchronous document updates.
/// </summary>
[PseudoClasses(":link-pending", ":selecting")]
public partial class MarkdownRenderer : Control
{
    /// <summary>
    /// Defines the attached SelectionScopeName property.
    /// </summary>
    [Obsolete("Use MarkdownTextBlock.IsSelectionScope on the shared visual root instead.")]
    public static readonly AttachedProperty<string?> SelectionScopeNameProperty =
        AvaloniaProperty.RegisterAttached<MarkdownRenderer, Visual, string?>("SelectionScopeName");

    /// <summary>
    /// Sets the SelectionScopeName attached property on the given Visual.
    /// Visuals with the same SelectionScopeName belong to the same selection scope.
    /// <see cref="MarkdownRenderer"/>s in the same selection scope can be selected across each other.
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="value"></param>
    [Obsolete("Use MarkdownTextBlock.IsSelectionScope on the shared visual root instead.")]
    public static void SetSelectionScopeName(Visual obj, string? value) => obj.SetValue(SelectionScopeNameProperty, value);

    /// <summary>
    /// Gets the SelectionScopeName attached property from the given Visual.
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    [Obsolete("Use MarkdownTextBlock.IsSelectionScope on the shared visual root instead.")]
    public static string? GetSelectionScopeName(Visual obj) => obj.GetValue(SelectionScopeNameProperty);

    /// <summary>
    /// Defines the <see cref="MarkdownBuilder"/> property.
    /// </summary>
    public static readonly DirectProperty<MarkdownRenderer, ObservableStringBuilder?> MarkdownBuilderProperty =
        AvaloniaProperty.RegisterDirect<MarkdownRenderer, ObservableStringBuilder?>(
            nameof(MarkdownBuilder),
            o => o.MarkdownBuilder,
            (o, v) => o.MarkdownBuilder = v);

    /// <summary>
    /// An <see cref="ObservableStringBuilder"/> containing the Markdown text to render.
    /// If set, the control will listen to changes in the builder and update the rendering accordingly.
    /// </summary>
    public ObservableStringBuilder? MarkdownBuilder
    {
        get;
        set
        {
            var oldValue = field;
            if (!SetAndRaise(MarkdownBuilderProperty, ref field, value)) return;

            if (oldValue is not null) oldValue.Changed -= CommitChange;

            // A builder replacement invalidates any parse started for the old builder.
            // Keep the new builder's changes independent from the old version sequence.
            var oldCancellation = currentCancellationTokenSource;
            currentCancellationTokenSource = new CancellationTokenSource();
            oldCancellation.Cancel();
            oldCancellation.Dispose();
            pendingChange = null;
            _pendingRenderedTextStateVersion = null;
            SetRenderedTextProjection(null);

            if (value is not null)
            {
                value.Changed += CommitChange;
                CommitChange(new ObservableStringBuilderChangedEventArgs(0, value.Length, value.Length, value.Version));
            }
        }
    }

    /// <summary>
    /// Defines the <see cref="RenderedTextProjection"/> property.
    /// </summary>
    public static readonly DirectProperty<MarkdownRenderer, MarkdownTextProjection?> RenderedTextProjectionProperty =
        AvaloniaProperty.RegisterDirect<MarkdownRenderer, MarkdownTextProjection?>(
            nameof(RenderedTextProjection),
            renderer => renderer.RenderedTextProjection);

    /// <summary>
    /// Gets the searchable text buffers produced by the most recently committed render.
    /// </summary>
    public MarkdownTextProjection? RenderedTextProjection => _renderedTextProjection;

    /// <summary>
    /// Gets the content version represented by the current visual tree, or <see langword="null"/>
    /// before the first render of the current builder completes.
    /// </summary>
    public long? RenderedVersion => RenderedTextProjection?.SourceVersion;

    /// <summary>
    /// Defines the <see cref="ImageBasePath"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> ImageBasePathProperty =
        AvaloniaProperty.Register<MarkdownRenderer, string?>(nameof(ImageBasePath));

    /// <summary>
    /// Base path for resolving relative image URLs.
    /// If not set, relative image URLs will not be resolved.
    /// Changing this property will not affect already rendered images.
    /// </summary>
    public string? ImageBasePath
    {
        get => GetValue(ImageBasePathProperty);
        set => SetValue(ImageBasePathProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CodeBlockColorTheme"/> property.
    /// </summary>
    public static readonly StyledProperty<ThemeName> CodeBlockColorThemeProperty =
        CodeBlock.ColorThemeProperty.AddOwner<MarkdownRenderer>();

    /// <summary>
    /// Gets or sets the color theme used for syntax highlighting in code blocks.
    /// The value is inherited by code blocks generated by this renderer.
    /// </summary>
    public ThemeName CodeBlockColorTheme
    {
        get => GetValue(CodeBlockColorThemeProperty);
        set => SetValue(CodeBlockColorThemeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CodeBlockCustomColorTheme"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> CodeBlockCustomColorThemeProperty =
        CodeBlock.CustomColorThemeProperty.AddOwner<MarkdownRenderer>();

    /// <summary>
    /// Gets or sets the registered custom theme name used for syntax highlighting in code blocks.
    /// When not set, <see cref="CodeBlockColorTheme"/> is used.
    /// The value is inherited by code blocks generated by this renderer.
    /// </summary>
    public string? CodeBlockCustomColorTheme
    {
        get => GetValue(CodeBlockCustomColorThemeProperty);
        set => SetValue(CodeBlockCustomColorThemeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LinkContextMenu"/> property.
    /// </summary>
    public static readonly StyledProperty<ContextMenu?> LinkContextMenuProperty =
        MarkdownTextBlock.LinkContextMenuProperty.AddOwner<MarkdownRenderer>();

    /// <summary>
    /// Context menu to show when right-clicking a link.
    /// The value is inherited by Markdown text blocks generated by this renderer.
    /// </summary>
    public ContextMenu? LinkContextMenu
    {
        get => GetValue(LinkContextMenuProperty);
        set => SetValue(LinkContextMenuProperty, value);
    }

    /// <summary>
    /// Raised when a Link is clicked.
    /// </summary>
    public event EventHandler<LinkClickedEventArgs>? LinkClick
    {
        add => AddHandler(MarkdownTextBlock.LinkClickEvent, value);
        remove => RemoveHandler(MarkdownTextBlock.LinkClickEvent, value);
    }

    /// <summary>
    /// Raised after the searchable text projection for the rendered visual tree changes.
    /// </summary>
    public event EventHandler? RenderedTextProjectionChanged;

    /// <summary>
    /// Defines the <see cref="LinkCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<MarkdownRenderer, ICommand?>(nameof(LinkCommand));

    /// <summary>
    /// Command that is executed when a Link is clicked. Command parameter is <see cref="LinkClickedEventArgs"/>.
    /// </summary>
    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    private ObservableStringBuilderChangedEventArgs? pendingChange;
    private Task? renderTask;
    private CancellationTokenSource currentCancellationTokenSource = new();
    private MarkdownTextProjection? _renderedTextProjection;

    private readonly DocumentNode documentNode;
    /// <summary>
    /// The parse pipeline, built once per process rather than once per renderer.
    /// </summary>
    /// <remarks>
    /// <para>Every renderer built an identical pipeline: <see cref="ConfigurePipeline"/> is documented as
    /// "set before any instances are created", so no two copies could ever differ. Each costs about 26 us
    /// to build and retains roughly 38 KB of parser tables for the renderer's lifetime — which, in a chat
    /// UI, is the lifetime of the message.</para>
    /// <para>Lazily initialised on purpose: <c>ConfigurePipeline += ...</c> is itself the first touch of
    /// this type, so a plain static initialiser would run <see cref="CreatePipeline"/> before the caller's
    /// handler finished registering and silently drop its extensions.</para>
    /// <para>Sharing is safe because concurrent parses share parser INSTANCES only, which Markdig
    /// supports. Extensions added through <see cref="ConfigurePipeline"/> must therefore hold no per-parse
    /// instance state.</para>
    /// </remarks>
    private static readonly Lazy<MarkdownPipeline> pipeline =
        new(CreatePipeline, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Optional callback to configure the Markdig pipeline before it is built.
    /// Set this before any MarkdownRenderer instances are created.
    /// </summary>
    public static event Action<MarkdownPipelineBuilder>? ConfigurePipeline;

    internal static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseCodeBlockSpanFixer();
        ConfigurePipeline?.Invoke(builder);
        return builder.Build();
    }

    internal static readonly ParametrizedLogger? VerboseLogger;

    static MarkdownRenderer()
    {
        VerboseLogger = Logger.TryGet(LogEventLevel.Verbose, nameof(MarkdownRenderer));

        MarkdownTextBlock.LinkClickEvent.AddClassHandler<MarkdownRenderer>(HandleLinkClick);
        RequestBringIntoViewEvent.AddClassHandler<MarkdownRenderer>(HandleRequestBringIntoView);
    }

    private static void HandleLinkClick(MarkdownRenderer sender, LinkClickedEventArgs args)
    {
        if (args.Handled || sender.LinkCommand is not { } linkCommand || !linkCommand.CanExecute(args)) return;
        linkCommand.Execute(args);
    }

    private static void HandleRequestBringIntoView(MarkdownRenderer sender, RequestBringIntoViewEventArgs args)
    {
        // ignore requests from children
        args.Handled = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MarkdownRenderer"/> class.
    /// </summary>
    public MarkdownRenderer()
    {
        documentNode = new DocumentNode(this);
        LogicalChildren.Add(documentNode.Control);
        VisualChildren.Add(documentNode.Control);

        AddHandler(KeyDownEvent, HandleKeyDown);
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureRenderLoopStarted();
        SchedulePendingRenderedTextStateRefresh();
    }

    /// <inheritdoc/>
    protected override void ArrangeCore(Rect finalRect)
    {
        // ArrangeCore must remain synchronous.  Avalonia marks an arrange as valid
        // around this call, so awaiting here would let a stale continuation update
        // the visual tree after a later arrange has already completed.
        EnsureRenderLoopStarted();
        base.ArrangeCore(finalRect);
        RefreshRenderedTextState();
    }

    private void EnsureRenderLoopStarted()
    {
        Dispatcher.UIThread.VerifyAccess();

        if (pendingChange is null) return;
        if (renderTask is { IsCompleted: false }) return;

        renderTask = null;
        if (currentCancellationTokenSource.IsCancellationRequested)
        {
            var oldCancellationTokenSource = currentCancellationTokenSource;
            currentCancellationTokenSource = new CancellationTokenSource();
            oldCancellationTokenSource.Dispose();
        }

        renderTask = RenderPendingChangesAsync(currentCancellationTokenSource.Token);
    }

    private async Task RenderPendingChangesAsync(CancellationToken cancellationToken)
    {
        ObservableStringBuilder? currentBuilder = null;
        ObservableStringBuilderSnapshot currentSnapshot = default;
        ObservableStringBuilderChangedEventArgs currentChange = default;

        try
        {
            while (pendingChange is { } change)
            {
                currentChange = change;
                currentBuilder = MarkdownBuilder;
                cancellationToken.ThrowIfCancellationRequested();

                currentSnapshot = currentBuilder?.CaptureSnapshot() ??
                    new ObservableStringBuilderSnapshot(string.Empty, currentChange.Version);
                if (currentSnapshot.Version != currentChange.Version)
                {
                    continue;
                }

                var time = DateTimeOffset.UtcNow;
                var document = await Task.Run(() => Markdown.Parse(currentSnapshot.Text, pipeline.Value), cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                Dispatcher.UIThread.VerifyAccess();

                // The source may have changed while parsing.  Leave the aggregate
                // pending change intact and parse the latest snapshot instead.
                if (!ReferenceEquals(currentBuilder, MarkdownBuilder) ||
                    pendingChange is not { } latestChange ||
                    latestChange.Version != currentChange.Version)
                {
                    continue;
                }

                if (VerboseLogger?.IsValid is true)
                {
                    VerboseLogger.Value.Log(this, "Parse markdown in {TotalMicroseconds} ms.", (DateTimeOffset.UtcNow - time).TotalMilliseconds);
                }

                time = DateTimeOffset.UtcNow;
                documentNode.Update(documentNode, document, currentChange, CancellationToken.None);
                // UpdateCore can invoke user code while controls are being
                // rebuilt.  Do not discard a change committed reentrantly.
                if (ReferenceEquals(currentBuilder, MarkdownBuilder) &&
                    pendingChange is { } appliedChange &&
                    appliedChange.Version == currentChange.Version)
                {
                    pendingChange = null;
                }

                InvalidateTextBlockCache();
                ScheduleRenderedTextStateRefresh(currentSnapshot.Version);
                InvalidateMeasure();

                if (VerboseLogger?.IsValid is true)
                {
                    VerboseLogger.Value.Log(this, "Render markdown in {TotalMicroseconds} ms.", (DateTimeOffset.UtcNow - time).TotalMilliseconds);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // A parse failure for the current snapshot should not cause an
            // invalidation/retry loop.  A newer change, if any, is retained.
            if (pendingChange is { } latestChange &&
                latestChange.Version == currentChange.Version &&
                ReferenceEquals(currentBuilder, MarkdownBuilder))
            {
                pendingChange = null;
            }

            if (VerboseLogger?.IsValid is true)
            {
                VerboseLogger.Value.Log(this, "Error rendering markdown: {Message}", ex.Message);
            }
        }
        finally
        {
            renderTask = null;
            if (VisualRoot is not null && pendingChange is not null)
            {
                EnsureRenderLoopStarted();
            }
        }
    }

    private void CommitChange(in ObservableStringBuilderChangedEventArgs e)
    {
        Dispatcher.UIThread.VerifyAccess();

        if (pendingChange is null) pendingChange = e;
        else
        {
            var startIndex = Math.Min(pendingChange.Value.StartIndex, e.StartIndex);
            var endIndex = Math.Max(pendingChange.Value.StartIndex + pendingChange.Value.Length, e.StartIndex + e.Length);
            pendingChange = new ObservableStringBuilderChangedEventArgs(
                startIndex,
                endIndex - startIndex,
                e.NewLength,
                e.Version);
        }

        InvalidateArrange();
    }

    private void SetRenderedTextProjection(MarkdownTextProjection? value)
    {
        if (!SetAndRaise(RenderedTextProjectionProperty, ref _renderedTextProjection, value)) return;
        RenderedTextProjectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
