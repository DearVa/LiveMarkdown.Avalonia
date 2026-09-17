using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;

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
    /// Defines the margin around this inline. Horizontal values reserve layout space when the
    /// containing text block can create its specialized shaped layout. Vertical values inset the
    /// painted background and border within the line box without changing the paragraph line height.
    /// </summary>
    public static readonly StyledProperty<Thickness> MarginProperty =
        AvaloniaProperty.Register<CodeInline, Thickness>(nameof(Margin));

    /// <summary>
    /// Defines the visual border brush used by the containing <see cref="MarkdownTextBlock"/>
    /// when painting this inline's background.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<CodeInline, IBrush?>(nameof(BorderBrush));

    /// <summary>
    /// Defines the visual border thickness used by the containing <see cref="MarkdownTextBlock"/>
    /// when painting this inline's background. The value is paint-only and does not affect text
    /// measurement or wrapping.
    /// </summary>
    public static readonly StyledProperty<double> BorderThicknessProperty =
        AvaloniaProperty.Register<CodeInline, double>(nameof(BorderThickness));

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
    /// Gets or sets the margin around the code inline. Horizontal values participate in the
    /// specialized text layout when available. Vertical values inset the painted background and
    /// border without changing the paragraph line height.
    /// </summary>
    public Thickness Margin
    {
        get => GetValue(MarginProperty);
        set => SetValue(MarginProperty, value);
    }

    /// <summary>
    /// Gets or sets the visual border brush used when painting the code background.
    /// </summary>
    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the visual border thickness used when painting the code background.
    /// </summary>
    public double BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    static CodeInline()
    {
        BackgroundProperty.Changed.AddClassHandler<CodeInline>(static (s, _) => s.InvalidateParentTextBlock(affectsLayout: false));
        CornerRadiusProperty.Changed.AddClassHandler<CodeInline>(static (s, _) => s.InvalidateParentTextBlock(affectsLayout: false));
        PaddingProperty.Changed.AddClassHandler<CodeInline>(static (s, _) => s.InvalidateParentTextBlock(affectsLayout: true));
        MarginProperty.Changed.AddClassHandler<CodeInline>(static (s, _) => s.InvalidateParentTextBlock(affectsLayout: true));
        BorderBrushProperty.Changed.AddClassHandler<CodeInline>(static (s, _) => s.InvalidateParentTextBlock(affectsLayout: false));
        BorderThicknessProperty.Changed.AddClassHandler<CodeInline>(static (s, _) => s.InvalidateParentTextBlock(affectsLayout: false));
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
