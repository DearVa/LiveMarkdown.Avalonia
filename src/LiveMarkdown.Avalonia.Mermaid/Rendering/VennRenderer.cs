using Avalonia;
using Avalonia.Media;
using Mermaider.Models;
using Point = Avalonia.Point;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Native Avalonia renderer for Mermaid Venn diagrams.
/// </summary>
/// <remarks>
/// Venn diagrams in Mermaider use a compact fixed circle layout, so this renderer owns the set
/// placement math and keeps radius, center, palette, and text sizes on the renderer part.
/// </remarks>
public sealed class VennRenderer : MermaidRenderer
{
    private static readonly IReadOnlyList<Color> DefaultSetPalette =
    [
        Color.Parse("#4e79a7"), Color.Parse("#f28e2b"), Color.Parse("#e15759"), Color.Parse("#76b7b2"),
        Color.Parse("#59a14f"), Color.Parse("#edc948"), Color.Parse("#b07aa1"), Color.Parse("#ff9da7")
    ];

    // SVG-compatible placement multipliers. They encode Mermaid's compact illustrative layout.
    private const double TwoSetOffsetRatio = 0.55;
    private const double MultiSetArrangeRadiusRatio = 0.6;

    // Stable visual constants from the SVG renderer.
    private const byte SetFillAlpha = 0x59;
    private const double SetStrokeThickness = 2;

    /// <summary>
    /// Defines the <see cref="BaseRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> BaseRadiusProperty =
        AvaloniaProperty.Register<VennRenderer, double>(nameof(BaseRadius), 120);

    /// <summary>
    /// Radius used for each Venn set circle.
    /// </summary>
    public double BaseRadius
    {
        get => GetValue(BaseRadiusProperty);
        set => SetValue(BaseRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CenterX"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CenterXProperty =
        AvaloniaProperty.Register<VennRenderer, double>(nameof(CenterX), 300);

    /// <summary>
    /// X coordinate of the diagram center; desired width is twice this value.
    /// </summary>
    public double CenterX
    {
        get => GetValue(CenterXProperty);
        set => SetValue(CenterXProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CenterY"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CenterYProperty =
        AvaloniaProperty.Register<VennRenderer, double>(nameof(CenterY), 200);

    /// <summary>
    /// Y coordinate of the diagram center; desired height is twice this value.
    /// </summary>
    public double CenterY
    {
        get => GetValue(CenterYProperty);
        set => SetValue(CenterYProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="SetLabelFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SetLabelFontSizeProperty =
        AvaloniaProperty.Register<VennRenderer, double>(nameof(SetLabelFontSize), 16);

    /// <summary>
    /// Font size used for individual set labels.
    /// </summary>
    public double SetLabelFontSize
    {
        get => GetValue(SetLabelFontSizeProperty);
        set => SetValue(SetLabelFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="UnionLabelFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> UnionLabelFontSizeProperty =
        AvaloniaProperty.Register<VennRenderer, double>(nameof(UnionLabelFontSize), 14);

    /// <summary>
    /// Font size used for intersection and union labels.
    /// </summary>
    public double UnionLabelFontSize
    {
        get => GetValue(UnionLabelFontSizeProperty);
        set => SetValue(UnionLabelFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="SetPalette"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<Color>> SetPaletteProperty =
        AvaloniaProperty.Register<VennRenderer, IReadOnlyList<Color>>(nameof(SetPalette), DefaultSetPalette);

    /// <summary>
    /// Repeating palette used for set circle fills and strokes.
    /// </summary>
    public IReadOnlyList<Color> SetPalette
    {
        get => GetValue(SetPaletteProperty);
        set => SetValue(SetPaletteProperty, value);
    }

    private readonly record struct Style(
        double BaseRadius,
        double CenterX,
        double CenterY,
        double SetLabelFontSize,
        double UnionLabelFontSize,
        IReadOnlyList<Color> SetPalette
    );

    /// <summary>
    /// Calculates the desired size for the diagram using this renderer part's current styled values.
    /// </summary>
    public Size MeasureDiagram(VennDiagram diagram)
    {
        var style = CreateStyleSnapshot();
        return new Size(style.CenterX * 2, style.CenterY * 2);
    }

    /// <summary>
    /// Draws a Venn diagram using this renderer part's current styled values.
    /// </summary>
    public void RenderDiagram(DrawingContext dc, MermaidPresenter presenter, VennDiagram diagram)
    {
        var style = CreateStyleSnapshot();
        if (diagram.Sets.Count == 0)
        {
            return;
        }

        var positions = ComputePositions(diagram.Sets.Count, style);
        var setPositions = new Dictionary<string, Point>();

        for (var i = 0; i < diagram.Sets.Count; i++)
        {
            var set = diagram.Sets[i];
            var position = positions[i];
            var color = GetPaletteColor(style.SetPalette, i);
            var stroke = new Pen(new SolidColorBrush(color), SetStrokeThickness);

            setPositions[set.Id] = position;
            dc.DrawEllipse(
                new SolidColorBrush(WithAlpha(color, SetFillAlpha)),
                stroke,
                position,
                style.BaseRadius,
                style.BaseRadius);
        }

        var centroid = new Point(positions.Average(point => point.X), positions.Average(point => point.Y));
        var occupied = new List<Rect>();
        for (var i = 0; i < diagram.Sets.Count; i++)
        {
            var anchor = PushOutward(positions[i], centroid, diagram.Sets.Count == 1 ? 0 : style.BaseRadius * 0.55);
            DrawLabel(diagram.Sets[i].Label, anchor, style.BaseRadius * 1.15, style.SetLabelFontSize, FontWeight.SemiBold);
        }
        foreach (var union in diagram.Unions)
        {
            if (union.Label is not { Length: > 0 } label)
            {
                continue;
            }
            var members = union.SetIds.Where(setPositions.ContainsKey).Select(id => setPositions[id]).ToList();
            if (members.Count == 0)
            {
                continue;
            }
            var anchor = new Point(members.Average(point => point.X), members.Average(point => point.Y));
            if (members.Count < diagram.Sets.Count)
            {
                anchor = PushOutward(anchor, centroid, style.BaseRadius * 0.2);
            }
            DrawLabel(label, anchor, style.BaseRadius * 0.9, style.UnionLabelFontSize, FontWeight.Medium);
        }

        void DrawLabel(string label, Point anchor, double width, double fontSize, FontWeight weight)
        {
            var text = MermaidTextRenderer.CreateFormattedText(presenter, label, fontSize, presenter.Foreground, TextAlignment.Center, weight);
            text.MaxTextWidth = Math.Max(1, width);
            text.MaxTextHeight = fontSize * 3;
            text.Trimming = TextTrimming.WordEllipsis;
            var bounds = MermaidLabelPlacement.Place(
                new Size(text.MaxTextWidth, text.Height),
                anchor,
                occupied,
                new Rect(4, 4, Math.Max(0, style.CenterX * 2 - 8), Math.Max(0, style.CenterY * 2 - 8)));
            occupied.Add(bounds);
            MermaidTextRenderer.DrawTextInRect(dc, presenter, label, bounds, fontSize, presenter.Foreground, weight);
        }
    }

    /// <inheritdoc/>
    protected override bool ShouldInvalidatePresenterMeasure(AvaloniaProperty property) =>
        property.Name is nameof(BaseRadius) or nameof(CenterX) or nameof(CenterY);

    private Style CreateStyleSnapshot() =>
        new(BaseRadius, CenterX, CenterY, SetLabelFontSize, UnionLabelFontSize, SetPalette);

    private static IReadOnlyList<Point> ComputePositions(int count, Style style)
    {
        var positions = new List<Point>(count);
        if (count == 1)
        {
            positions.Add(new Point(style.CenterX, style.CenterY));
        }
        else if (count == 2)
        {
            var offset = style.BaseRadius * TwoSetOffsetRatio;
            positions.Add(new Point(style.CenterX - offset, style.CenterY));
            positions.Add(new Point(style.CenterX + offset, style.CenterY));
        }
        else
        {
            var arrangeRadius = style.BaseRadius * MultiSetArrangeRadiusRatio;
            for (var i = 0; i < count; i++)
            {
                var angle = (2 * Math.PI * i / count) - (Math.PI / 2);
                positions.Add(
                    new Point(
                        style.CenterX + arrangeRadius * Math.Cos(angle),
                        style.CenterY + arrangeRadius * Math.Sin(angle)));
            }
        }

        return positions;
    }

    private static Point PushOutward(Point point, Point center, double distance)
    {
        var delta = point - center;
        var length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        return length < 0.01 ? point : point + delta * (distance / length);
    }

    private static Color GetPaletteColor(IReadOnlyList<Color> palette, int index) =>
        palette.Count > 0 ? palette[index % palette.Count] : DefaultSetPalette[index % DefaultSetPalette.Count];

    private static Color WithAlpha(Color color, byte alpha) => new(alpha, color.R, color.G, color.B);
}