using Avalonia;
using Avalonia.Controls.Documents;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A text run whose visual background is painted by the containing <see cref="MarkdownTextBlock"/>.
/// </summary>
public sealed class CodeInline : Run
{
    /// <summary>
    /// Defines the corner radius used by the containing <see cref="MarkdownTextBlock"/> when
    /// painting this inline's background.
    /// </summary>
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<CodeInline, CornerRadius>(nameof(CornerRadius), new CornerRadius(4));

    /// <summary>
    /// Defines the visual padding used by the containing <see cref="MarkdownTextBlock"/> when
    /// painting this inline's background.
    /// </summary>
    public static readonly StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<CodeInline, Thickness>(nameof(Padding), new Thickness(2, 0));

    /// <summary>
    /// Defines the horizontal layout margin reserved around this inline. Vertical values are
    /// retained for API symmetry but currently do not affect layout or painting.
    /// </summary>
    public static readonly StyledProperty<Thickness> MarginProperty =
        AvaloniaProperty.Register<CodeInline, Thickness>(nameof(Margin));

    /// <summary>
    /// Gets or sets the corner radius used when painting the code background.
    /// </summary>
    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the visual padding used when painting the code background.
    /// </summary>
    public Thickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the margin reserved around the code inline. Horizontal values participate in
    /// text layout; vertical values are currently ignored so a single inline cannot unexpectedly
    /// alter the paragraph line height.
    /// </summary>
    public Thickness Margin
    {
        get => GetValue(MarginProperty);
        set => SetValue(MarginProperty, value);
    }

    static CodeInline()
    {
        BackgroundProperty.Changed.AddClassHandler<CodeInline>(static (s, _) => s.InvalidateParentTextBlock(affectsLayout: false));
        CornerRadiusProperty.Changed.AddClassHandler<CodeInline>(static (s, _) => s.InvalidateParentTextBlock(affectsLayout: false));
        PaddingProperty.Changed.AddClassHandler<CodeInline>(static (s, _) => s.InvalidateParentTextBlock(affectsLayout: true));
        MarginProperty.Changed.AddClassHandler<CodeInline>(static (s, _) => s.InvalidateParentTextBlock(affectsLayout: true));
    }

    private void InvalidateParentTextBlock(bool affectsLayout)
    {
        for (var parent = Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is MarkdownTextBlock textBlock)
            {
                textBlock.InvalidateInlineDecorations(affectsLayout);
                break;
            }
        }
    }
}