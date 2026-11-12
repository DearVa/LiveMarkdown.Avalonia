using Avalonia;
using Avalonia.Media;
using Mermaider.Models;
using Point = Avalonia.Point;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Native Avalonia renderer for Mermaid radar charts.
/// </summary>
/// <remarks>
/// Radar charts are laid out directly by Mermaider's SVG renderer, so this renderer owns the polar
/// chart metrics and exposes them as renderer-local style tokens.
/// </remarks>
public sealed class RadarRenderer : MermaidRenderer
{
    private static readonly IReadOnlyList<Color> DefaultCurvePalette =
    [
        Color.Parse("#4e79a7"), Color.Parse("#f28e2b"), Color.Parse("#e15759"), Color.Parse("#76b7b2"),
        Color.Parse("#59a14f"), Color.Parse("#edc948"), Color.Parse("#b07aa1"), Color.Parse("#ff9da7")
    ];

    // Mermaider's SVG renderer returns a small empty canvas when no axes are defined.
    private const double EmptyFallbackWidth = 200;
    private const double EmptyFallbackHeight = 100;

    // Stable SVG-compatible visual constants. These tune drawing weight, not layout.
    private const byte GridAlpha = 0x80;
    private const byte CurveFillAlpha = 0x40;
    private const double GridStrokeThickness = 0.5;
    private const double CurveStrokeThickness = 2;
    private const double CurvePointRadius = 3;
    private const double LegendSwatchCornerRadius = 2;

    /// <summary>
    /// Defines the <see cref="Radius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(Radius), 160);

    /// <summary>
    /// Radius of the radar plotting area.
    /// </summary>
    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CenterX"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CenterXProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(CenterX), 220);

    /// <summary>
    /// X coordinate of the radar center.
    /// </summary>
    public double CenterX
    {
        get => GetValue(CenterXProperty);
        set => SetValue(CenterXProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LabelPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LabelPaddingProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(LabelPadding), 20);

    /// <summary>
    /// Distance from the outer radius to axis labels.
    /// </summary>
    public double LabelPadding
    {
        get => GetValue(LabelPaddingProperty);
        set => SetValue(LabelPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TitleHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TitleHeightProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(TitleHeight), 36);

    /// <summary>
    /// Vertical space reserved before the chart when a title is present.
    /// </summary>
    public double TitleHeight
    {
        get => GetValue(TitleHeightProperty);
        set => SetValue(TitleHeightProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TitleY"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TitleYProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(TitleY), 28);

    /// <summary>
    /// Y coordinate of the title anchor inside the title area.
    /// </summary>
    public double TitleY
    {
        get => GetValue(TitleYProperty);
        set => SetValue(TitleYProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="ChartTopPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ChartTopPaddingProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(ChartTopPadding), 20);

    /// <summary>
    /// Top padding between the optional title area and the radar center minus radius.
    /// </summary>
    public double ChartTopPadding
    {
        get => GetValue(ChartTopPaddingProperty);
        set => SetValue(ChartTopPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="RightPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> RightPaddingProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(RightPadding), 60);

    /// <summary>
    /// Extra horizontal padding after axis labels before the optional legend.
    /// </summary>
    public double RightPadding
    {
        get => GetValue(RightPaddingProperty);
        set => SetValue(RightPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="BottomPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> BottomPaddingProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(BottomPadding), 30);

    /// <summary>
    /// Extra vertical padding below the radar axis labels.
    /// </summary>
    public double BottomPadding
    {
        get => GetValue(BottomPaddingProperty);
        set => SetValue(BottomPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LegendWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LegendWidthProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(LegendWidth), 160);

    /// <summary>
    /// Width reserved for the legend when it is visible.
    /// </summary>
    public double LegendWidth
    {
        get => GetValue(LegendWidthProperty);
        set => SetValue(LegendWidthProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LegendTopOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LegendTopOffsetProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(LegendTopOffset), 30);

    /// <summary>
    /// Top offset for the first legend row after the optional title area.
    /// </summary>
    public double LegendTopOffset
    {
        get => GetValue(LegendTopOffsetProperty);
        set => SetValue(LegendTopOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LegendSwatchSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LegendSwatchSizeProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(LegendSwatchSize), 12);

    /// <summary>
    /// Size of legend color swatches.
    /// </summary>
    public double LegendSwatchSize
    {
        get => GetValue(LegendSwatchSizeProperty);
        set => SetValue(LegendSwatchSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LegendRowHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LegendRowHeightProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(LegendRowHeight), 20);

    /// <summary>
    /// Vertical distance between legend rows.
    /// </summary>
    public double LegendRowHeight
    {
        get => GetValue(LegendRowHeightProperty);
        set => SetValue(LegendRowHeightProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LegendTextOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LegendTextOffsetProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(LegendTextOffset), 18);

    /// <summary>
    /// Horizontal offset from a legend swatch to its text.
    /// </summary>
    public double LegendTextOffset
    {
        get => GetValue(LegendTextOffsetProperty);
        set => SetValue(LegendTextOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TitleFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TitleFontSizeProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(TitleFontSize), 18);

    /// <summary>
    /// Font size used for the optional chart title.
    /// </summary>
    public double TitleFontSize
    {
        get => GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AxisLabelFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AxisLabelFontSizeProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(AxisLabelFontSize), 14);

    /// <summary>
    /// Font size used for axis labels.
    /// </summary>
    public double AxisLabelFontSize
    {
        get => GetValue(AxisLabelFontSizeProperty);
        set => SetValue(AxisLabelFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LegendFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LegendFontSizeProperty =
        AvaloniaProperty.Register<RadarRenderer, double>(nameof(LegendFontSize), 14);

    /// <summary>
    /// Font size used for legend labels.
    /// </summary>
    public double LegendFontSize
    {
        get => GetValue(LegendFontSizeProperty);
        set => SetValue(LegendFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CurvePalette"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<Color>> CurvePaletteProperty =
        AvaloniaProperty.Register<RadarRenderer, IReadOnlyList<Color>>(nameof(CurvePalette), DefaultCurvePalette);

    /// <summary>
    /// Repeating palette used for radar curves and legend swatches.
    /// </summary>
    public IReadOnlyList<Color> CurvePalette
    {
        get => GetValue(CurvePaletteProperty);
        set => SetValue(CurvePaletteProperty, value);
    }

    private readonly record struct Style(
        double Radius,
        double CenterX,
        double LabelPadding,
        double TitleHeight,
        double TitleY,
        double ChartTopPadding,
        double RightPadding,
        double BottomPadding,
        double LegendWidth,
        double LegendTopOffset,
        double LegendSwatchSize,
        double LegendRowHeight,
        double LegendTextOffset,
        double TitleFontSize,
        double AxisLabelFontSize,
        double LegendFontSize,
        IReadOnlyList<Color> CurvePalette
    );

    /// <summary>
    /// Calculates the desired size for the chart using this renderer part's current styled values.
    /// </summary>
    public Size MeasureDiagram(RadarChart chart) => Measure(chart, CreateStyleSnapshot());

    /// <summary>
    /// Draws a radar chart using this renderer part's current styled values.
    /// </summary>
    public void RenderDiagram(DrawingContext dc, MermaidPresenter presenter, RadarChart chart)
    {
        var style = CreateStyleSnapshot();
        if (chart.Axes.Count == 0)
        {
            return;
        }

        var metrics = CreateLayoutMetrics(chart, style);
        if (metrics.HasTitle)
        {
            MermaidTextRenderer.DrawText(
                dc,
                presenter,
                chart.Title!,
                style.CenterX,
                style.TitleY,
                style.TitleFontSize,
                presenter.Foreground,
                TextAlignment.Center,
                centerVertically: true,
                FontWeight.Bold);
        }

        DrawGraticule(dc, presenter, style, chart, metrics.CenterY);
        DrawAxisLines(dc, presenter, style, chart, metrics.CenterY);

        for (var i = 0; i < chart.Curves.Count; i++)
        {
            DrawCurve(dc, style, chart, chart.Curves[i], metrics.CenterY, GetPaletteColor(style.CurvePalette, i));
        }

        if (chart is { ShowLegend: true, Curves.Count: > 0 })
        {
            DrawLegend(dc, presenter, style, chart, metrics.LegendTop);
        }
    }

    /// <inheritdoc/>
    protected override bool ShouldInvalidatePresenterMeasure(AvaloniaProperty property)
    {
        return property.Name is nameof(Radius) or nameof(CenterX) or nameof(LabelPadding) or nameof(TitleHeight) or
            nameof(ChartTopPadding) or nameof(RightPadding) or nameof(BottomPadding) or nameof(LegendWidth) or
            nameof(LegendTopOffset) or nameof(LegendRowHeight) or nameof(TitleY) or nameof(TitleFontSize) or nameof(AxisLabelFontSize);
    }

    private static Size Measure(RadarChart chart, Style style)
    {
        if (chart.Axes.Count == 0)
        {
            return new Size(EmptyFallbackWidth, EmptyFallbackHeight);
        }

        var metrics = CreateLayoutMetrics(chart, style);
        return new Size(metrics.Width, metrics.Height);
    }

    private static RadarLayoutMetrics CreateLayoutMetrics(RadarChart chart, Style style)
    {
        var hasTitle = chart.Title is { Length: > 0 };
        // Axis captions extend above the circle. Reserve that band below the title.
        var titleBottom = style.TitleY + style.TitleFontSize / 2;
        var titleOffset = hasTitle ? Math.Max(style.TitleHeight, titleBottom + style.AxisLabelFontSize + 12) : 0;
        var centerY = titleOffset + style.ChartTopPadding + style.Radius;
        var legendWidth = chart is { ShowLegend: true, Curves.Count: > 0 } ? style.LegendWidth : 0;
        var width = style.CenterX + style.Radius + style.LabelPadding + style.RightPadding + legendWidth;
        var height = centerY + style.Radius + style.LabelPadding + style.BottomPadding;
        var legendTop = titleOffset + style.LegendTopOffset;
        return new RadarLayoutMetrics(hasTitle, centerY, legendTop, width, height);
    }

    private Style CreateStyleSnapshot() =>
        new(
            Radius,
            CenterX,
            LabelPadding,
            TitleHeight,
            TitleY,
            ChartTopPadding,
            RightPadding,
            BottomPadding,
            LegendWidth,
            LegendTopOffset,
            LegendSwatchSize,
            LegendRowHeight,
            LegendTextOffset,
            TitleFontSize,
            AxisLabelFontSize,
            LegendFontSize,
            CurvePalette);

    private static void DrawGraticule(DrawingContext dc, MermaidPresenter presenter, Style style, RadarChart chart, double centerY)
    {
        var ticks = Math.Max(1, chart.Ticks);
        var pen = CreateGridPen(presenter);
        for (var tick = 1; tick <= ticks; tick++)
        {
            var radius = style.Radius * tick / ticks;
            if (chart.Graticule == RadarGraticule.Circle)
            {
                dc.DrawEllipse(null, pen, new Point(style.CenterX, centerY), radius, radius);
            }
            else
            {
                MermaidDrawingHelpers.DrawPolygon(dc, null, pen, CreateRadarPoints(style, chart.Axes.Count, centerY, radius));
            }
        }
    }

    private static void DrawAxisLines(DrawingContext dc, MermaidPresenter presenter, Style style, RadarChart chart, double centerY)
    {
        var pen = CreateGridPen(presenter);
        var axisCount = chart.Axes.Count;
        for (var i = 0; i < axisCount; i++)
        {
            var angle = AngleFor(i, axisCount);
            var tip = new Point(style.CenterX + style.Radius * Math.Cos(angle), centerY + style.Radius * Math.Sin(angle));
            dc.DrawLine(pen, new Point(style.CenterX, centerY), tip);

            var labelRadius = style.Radius + style.LabelPadding;
            var lx = style.CenterX + labelRadius * Math.Cos(angle);
            var ly = centerY + labelRadius * Math.Sin(angle);
            var alignment = Math.Abs(Math.Cos(angle)) < 0.1 ? TextAlignment.Center
                : Math.Cos(angle) > 0 ? TextAlignment.Left : TextAlignment.Right;

            MermaidTextRenderer.DrawText(
                dc,
                presenter,
                chart.Axes[i].Label,
                lx,
                ly,
                style.AxisLabelFontSize,
                presenter.SecondaryForeground,
                alignment,
                centerVertically: true);
        }
    }

    private static void DrawCurve(DrawingContext dc, Style style, RadarChart chart, RadarCurve curve, double centerY, Color color)
    {
        var range = chart.Max - chart.Min;
        if (range <= 0 || chart.Axes.Count == 0)
        {
            return;
        }

        var points = new Point[chart.Axes.Count];
        for (var i = 0; i < points.Length; i++)
        {
            var value = i < curve.Values.Count ? curve.Values[i] : 0;
            var normalized = Math.Clamp((value - chart.Min) / range, 0, 1);
            var radius = style.Radius * normalized;
            var angle = AngleFor(i, points.Length);
            points[i] = new Point(style.CenterX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle));
        }

        MermaidDrawingHelpers.DrawPolygon(
            dc,
            new SolidColorBrush(WithAlpha(color, CurveFillAlpha)),
            new Pen(new SolidColorBrush(color), CurveStrokeThickness),
            points);

        var fill = new SolidColorBrush(color);
        foreach (var point in points)
        {
            dc.DrawEllipse(fill, null, point, CurvePointRadius, CurvePointRadius);
        }
    }

    private static void DrawLegend(DrawingContext dc, MermaidPresenter presenter, Style style, RadarChart chart, double legendTop)
    {
        var legendX = style.CenterX + style.Radius + style.LabelPadding + 50;
        for (var i = 0; i < chart.Curves.Count; i++)
        {
            var curve = chart.Curves[i];
            var color = GetPaletteColor(style.CurvePalette, i);
            var y = legendTop + i * style.LegendRowHeight;

            dc.DrawRectangle(
                new SolidColorBrush(color),
                null,
                new Rect(legendX, y, style.LegendSwatchSize, style.LegendSwatchSize),
                LegendSwatchCornerRadius,
                LegendSwatchCornerRadius);

            MermaidTextRenderer.DrawText(
                dc,
                presenter,
                curve.Label,
                legendX + style.LegendSwatchSize + style.LegendTextOffset,
                y + style.LegendSwatchSize / 2,
                style.LegendFontSize,
                presenter.Foreground,
                TextAlignment.Left,
                centerVertically: true);
        }
    }

    private static Point[] CreateRadarPoints(Style style, int count, double centerY, double radius)
    {
        var points = new Point[count];
        for (var i = 0; i < count; i++)
        {
            var angle = AngleFor(i, count);
            points[i] = new Point(style.CenterX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle));
        }

        return points;
    }

    private static Pen CreateGridPen(MermaidPresenter presenter) =>
        new(new SolidColorBrush(WithAlpha(GetBrushColor(presenter.LineStroke, Colors.Gray), GridAlpha)), GridStrokeThickness);

    private static Color GetPaletteColor(IReadOnlyList<Color> palette, int index) =>
        palette.Count > 0 ? palette[index % palette.Count] : DefaultCurvePalette[index % DefaultCurvePalette.Count];

    private static Color GetBrushColor(IBrush? brush, Color fallback) =>
        brush is ISolidColorBrush solid ? solid.Color : fallback;

    private static Color WithAlpha(Color color, byte alpha) => new(alpha, color.R, color.G, color.B);

    private static double AngleFor(int index, int count) => (2 * Math.PI * index / count) - (Math.PI / 2);

    private readonly record struct RadarLayoutMetrics(bool HasTitle, double CenterY, double LegendTop, double Width, double Height);
}