using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using TextMateSharp.Grammars;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Represents a code block in a Markdown document.
/// This control is used to display code snippets with optional syntax highlighting.
/// </summary>
[TemplatePart(CodeTextBlockName, typeof(MarkdownTextBlock), IsRequired = true)]
[TemplatePart(ScrollViewerName, typeof(ScrollViewer), IsRequired = false)]
[TemplatePart(LanguageTextBlockName, typeof(TextBlock), IsRequired = false)]
[TemplatePart(ToggleTextWrapButtonName, typeof(ToggleButton), IsRequired = false)]
[TemplatePart(CopyButtonName, typeof(Button), IsRequired = false)]
public class CodeBlock : TemplatedControl
{
    private const string ScrollViewerName = "PART_ScrollViewer";
    private const string CodeTextBlockName = "PART_CodeTextBlock";
    private const string LanguageTextBlockName = "PART_LanguageTextBlock";
    private const string ToggleTextWrapButtonName = "PART_ToggleTextWrapButton";
    private const string CopyButtonName = "PART_CopyButton";

    /// <summary>
    /// Defines the <see cref="AutoSyntaxHighlight"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> AutoSyntaxHighlightProperty =
        AvaloniaProperty.Register<CodeBlock, bool>(nameof(AutoSyntaxHighlight), true);

    /// <summary>
    /// Gets or sets a value indicating whether to automatically apply syntax highlighting when the code or language changes.
    /// </summary>
    public bool AutoSyntaxHighlight
    {
        get => GetValue(AutoSyntaxHighlightProperty);
        set => SetValue(AutoSyntaxHighlightProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="Language"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> LanguageProperty =
        AvaloniaProperty.Register<CodeBlock, string?>(nameof(Language));

    /// <summary>
    /// Gets or sets the programming language of the code block.
    /// </summary>
    public string? Language
    {
        get => GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="ColorTheme"/> property.
    /// </summary>
    public static readonly StyledProperty<ThemeName> ColorThemeProperty =
        AvaloniaProperty.Register<CodeBlock, ThemeName>(nameof(ColorTheme), ThemeName.DarkPlus);

    /// <summary>
    /// Gets or sets the theme used for syntax highlighting.
    /// </summary>
    public ThemeName ColorTheme
    {
        get => GetValue(ColorThemeProperty);
        set => SetValue(ColorThemeProperty, value);
    }

    /// <summary>
    /// Backing field for the <see cref="Code"/> property.
    /// </summary>
    public static readonly DirectProperty<CodeBlock, string?> CodeProperty = AvaloniaProperty.RegisterDirect<CodeBlock, string?>(
        nameof(Code),
        o => o.Code,
        (o, v) => o.Code = v);

    /// <summary>
    /// Defines the <see cref="IsCodeWrapped"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsCodeWrappedProperty =
        AvaloniaProperty.Register<CodeBlock, bool>(nameof(IsCodeWrapped), true);

    /// <summary>
    /// Gets or sets a value indicating whether the code content is wrapped.
    /// </summary>
    public bool IsCodeWrapped
    {
        get => GetValue(IsCodeWrappedProperty);
        set => SetValue(IsCodeWrappedProperty, value);
    }

    public static readonly DirectProperty<CodeBlock, TextWrapping> TextWrappingProperty =
        AvaloniaProperty.RegisterDirect<CodeBlock, TextWrapping>(
            nameof(TextWrapping),
            o => o.TextWrapping);

    public TextWrapping TextWrapping =>
        IsCodeWrapped ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public static readonly DirectProperty<CodeBlock, ScrollBarVisibility> HorizontalScrollBarVisibilityProperty =
        AvaloniaProperty.RegisterDirect<CodeBlock, ScrollBarVisibility>(
        nameof(HorizontalScrollBarVisibility),
        o => o.HorizontalScrollBarVisibility);

    public ScrollBarVisibility HorizontalScrollBarVisibility =>
        IsCodeWrapped ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

    /// <summary>
    /// An alternative property of <see cref="Inlines"/> to set the code content and apply syntax highlighting automatically.
    /// </summary>
    public string? Code
    {
        get => Inlines.Text;
        set
        {
            Inlines.Clear();
            if (value is null) return;

            // pause applying syntax highlighting while adding lines
            isApplyingSyntaxHighlighting = true;
            var lines = value.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            for (var i = 0; i < lines.Length; i++)
            {
                Inlines.Add(lines[i]);
                if (i < lines.Length - 1) Inlines.Add(new LineBreak());
            }
            isApplyingSyntaxHighlighting = false;

            if (AutoSyntaxHighlight) HighlightSyntax();
        }
    }

    public InlineCollection Inlines { get; } = new();

    /// <summary>
    /// Defines the <see cref="CopyingToClipboard"/> event.
    /// Handle this event to perform custom actions when the code block is copying to clipboard and prevent the default behavior by setting <see cref="RoutedEventArgs.Handled"/> to true.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> CopyingToClipboardEvent =
        RoutedEvent.Register<TextBox, RoutedEventArgs>(nameof(CopyingToClipboard), RoutingStrategies.Bubble);

    /// <summary>
    /// Raised when the code block is copying to clipboard.
    /// </summary>
    public event EventHandler<RoutedEventArgs>? CopyingToClipboard
    {
        add => AddHandler(CopyingToClipboardEvent, value);
        remove => RemoveHandler(CopyingToClipboardEvent, value);
    }

    internal MarkdownTextBlock? CodeTextBlock { get; private set; }

    private ScrollViewer? _scrollViewer;
    private IDisposable? _copyButtonClickedSubscription;
    private bool isApplyingSyntaxHighlighting; // prevent re-entrancy

    public CodeBlock()
    {
        Inlines.CollectionChanged += HandleInlinesChanged;
    }

    private void HandleInlinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HighlightSyntax();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_copyButtonClickedSubscription is not null)
        {
            _copyButtonClickedSubscription.Dispose();
            _copyButtonClickedSubscription = null;
        }

        CodeTextBlock = e.NameScope.Find<MarkdownTextBlock>(CodeTextBlockName);
        if (CodeTextBlock is null)
        {
            throw new InvalidOperationException($"{CodeTextBlockName} is not found in the template.");
        }

        CodeTextBlock.Inlines = Inlines;

        _scrollViewer = e.NameScope.Find<ScrollViewer>(ScrollViewerName);

        if (e.NameScope.Find<Button>(CopyButtonName) is { } copyButton)
        {
            _copyButtonClickedSubscription = copyButton.AddDisposableHandler(
                Button.ClickEvent,
                HandleCopyButtonClick,
                RoutingStrategies.Bubble,
                true);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LanguageProperty)
        {
            if (AutoSyntaxHighlight) HighlightSyntax();
        }
        else if (change.Property == ColorThemeProperty)
        {
            if (AutoSyntaxHighlight) Code = Code; // re-apply syntax highlighting
        }
        else if (change.Property == AutoSyntaxHighlightProperty)
        {
            if (change.NewValue is true)
            {
                Inlines.CollectionChanged += HandleInlinesChanged;
            }
            else
            {
                Inlines.CollectionChanged -= HandleInlinesChanged;
            }
        }
        else if (change.Property == IsCodeWrappedProperty)
        {
            var isCodeWrapped = change.NewValue is true;

            var (textWrappingOldValue, textWrappingNewValue) = isCodeWrapped ?
                (TextWrapping.NoWrap, TextWrapping.Wrap) :
                (TextWrapping.Wrap, TextWrapping.NoWrap);
            RaisePropertyChanged(TextWrappingProperty, textWrappingOldValue, textWrappingNewValue);

            var (scrollBarVisibilityOldValue, scrollBarVisibilityNewValue) = isCodeWrapped ?
                (ScrollBarVisibility.Auto, ScrollBarVisibility.Disabled) :
                (ScrollBarVisibility.Disabled, ScrollBarVisibility.Auto);
            RaisePropertyChanged(HorizontalScrollBarVisibilityProperty, scrollBarVisibilityOldValue, scrollBarVisibilityNewValue);

            if (_scrollViewer is not null && CodeTextBlock is not null)
            {
                CodeTextBlock.Width = _scrollViewer.Viewport.Width;
                CodeTextBlock.UpdateLayout(); // fix bug that the text block does not resize correctly
                CodeTextBlock.Width = double.NaN;
            }
        }
    }

    /// <summary>
    /// Applies syntax highlighting to the code block based on the specified language.
    /// </summary>
    public void HighlightSyntax()
    {
        if (isApplyingSyntaxHighlighting) return;
        if (string.IsNullOrWhiteSpace(Language) || Inlines.Count == 0) return;

        isApplyingSyntaxHighlighting = true;
        try
        {
            SyntaxHighlighting.Create(Language!.ToLower()).FormatInlines(Inlines, ColorTheme);
        }
        finally
        {
            isApplyingSyntaxHighlighting = false;
        }
    }

    private void HandleCopyButtonClick(object? sender, RoutedEventArgs e)
    {
        var copyEventArgs = new RoutedEventArgs(CopyingToClipboardEvent);
        RaiseEvent(copyEventArgs);
        if (copyEventArgs.Handled) return;

        var text = Inlines.Text;
        if (string.IsNullOrEmpty(text)) return;

        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
    }
}