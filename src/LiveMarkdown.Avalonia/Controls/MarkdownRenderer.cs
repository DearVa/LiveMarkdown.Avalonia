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
            if (value is not null)
            {
                value.Changed += CommitChange;
                CommitChange(new ObservableStringBuilderChangedEventArgs(0, value.Length, value.Length, value.Version));
            }
        }
    }

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
    /// </summary>
    public ThemeName CodeBlockColorTheme
    {
        get => GetValue(CodeBlockColorThemeProperty);
        set => SetValue(CodeBlockColorThemeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CodeBlockCustomThemeName"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> CodeBlockCustomThemeNameProperty =
        CodeBlock.CustomThemeNameProperty.AddOwner<MarkdownRenderer>();

    /// <summary>
    /// Gets or sets the registered custom theme name used for syntax highlighting in code blocks.
    /// When not set, <see cref="CodeBlockColorTheme"/> is used.
    /// </summary>
    public string? CodeBlockCustomThemeName
    {
        get => GetValue(CodeBlockCustomThemeNameProperty);
        set => SetValue(CodeBlockCustomThemeNameProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LinkContextMenu"/> property.
    /// </summary>
    public static readonly StyledProperty<ContextMenu?> LinkContextMenuProperty =
        MarkdownTextBlock.LinkContextMenuProperty.AddOwner<MarkdownRenderer>();

    /// <summary>
    /// Context menu to show when right-clicking a link.
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

    private readonly DocumentNode documentNode;
    private readonly MarkdownPipeline pipeline = CreatePipeline();

    /// <summary>
    /// Optional callback to configure the Markdig pipeline before it is built.
    /// Set this before any MarkdownRenderer instances are created.
    /// </summary>
    public static event Action<MarkdownPipelineBuilder>? ConfigurePipeline;

    private static MarkdownPipeline CreatePipeline()
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

    public MarkdownRenderer()
    {
        documentNode = new DocumentNode(this);
        LogicalChildren.Add(documentNode.Control);
        VisualChildren.Add(documentNode.Control);

        AddHandler(KeyDownEvent, HandleKeyDown);
    }

    protected override async void ArrangeCore(Rect finalRect)
    {
        if (pendingChange is { } e)
        {
            pendingChange = null;

            try
            {
                var markdown = MarkdownBuilder?.ToString() ?? string.Empty;
                var time = DateTimeOffset.UtcNow;
                var document = await Task.Run(() => Markdown.Parse(markdown, pipeline));

                if (VerboseLogger?.IsValid is true)
                {
                    VerboseLogger.Value.Log(this, "Parse markdown in {TotalMicroseconds} ms.", (DateTimeOffset.UtcNow - time).TotalMilliseconds);
                }

                time = DateTimeOffset.UtcNow;
                documentNode.Update(documentNode, document, e, CancellationToken.None);

                InvalidateTextBlockCache();
                ApplyTextSearchCore();

                if (VerboseLogger?.IsValid is true)
                {
                    VerboseLogger.Value.Log(this, "Render markdown in {TotalMicroseconds} ms.", (DateTimeOffset.UtcNow - time).TotalMilliseconds);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (VerboseLogger?.IsValid is true)
                {
                    VerboseLogger.Value.Log(this, "Error rendering markdown: {Message}", ex.Message);
                }
            }
        }

        base.ArrangeCore(finalRect);
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
}
