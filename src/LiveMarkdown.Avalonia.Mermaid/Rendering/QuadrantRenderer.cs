using Avalonia;
using Avalonia.Media;
using Mermaider.Models;
using Point = Avalonia.Point;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Native Avalonia renderer for Mermaid quadrant charts.
/// </summary>
/// <remarks>
/// Quadrant charts use a compact fixed plotting area like Mermaider's SVG renderer. Axes, quadrant
/// labels, and point labels are laid out by this renderer because Mermaider exposes only the parsed
/// chart model for this diagram type.
/// </remarks>
public class QuadrantRenderer : MermaidRenderer
{
    private static readonly IReadOnlyList<IBrush> DefaultQuadrantFills =
    [
        new SolidColorBrush(Color.FromArgb(0x1F, 0x4E, 0x79, 0xA7)),
        new SolidColorBrush(Color.FromArgb(0x14, 0x4E, 0x79, 0xA7)),
        new SolidColorBrush(Color.FromArgb(0x0A, 0x4E, 0x79, 0xA7)),
        new SolidColorBrush(Color.FromArgb(0x0F, 0x4E, 0x79, 0xA7))
    ];

    private static readonly IReadOnlyList<double> DefaultDividerDashPattern = [4, 3];

    // Visual compatibility constants from Mermaider's SVG renderer; these do not affect layout.
    private const double DividerStrokeThickness = 1;
    private const double PointStrokeThickness = 1.5;

    /// <summary>
    /// Defines the <see cref="ChartSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ChartSizeProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(ChartSize), 400);

    /// <summary>
    /// Width and height of the square plotting area.
    /// </summary>
    public double ChartSize
    {
        get => GetValue(ChartSizeProperty);
        set => SetValue(ChartSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="Padding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> PaddingProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(Padding), 60);

    /// <summary>
    /// Outer padding around the plotting area.
    /// </summary>
    public double Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TitleHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TitleHeightProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(TitleHeight), 32);

    /// <summary>
    /// Vertical space reserved above the chart when a title is present.
    /// </summary>
    public double TitleHeight
    {
        get => GetValue(TitleHeightProperty);
        set => SetValue(TitleHeightProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AxisLabelPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AxisLabelPaddingProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(AxisLabelPadding), 8);

    /// <summary>
    /// Gap between the plotting area and axis labels.
    /// </summary>
    public double AxisLabelPadding
    {
        get => GetValue(AxisLabelPaddingProperty);
        set => SetValue(AxisLabelPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="XAxisLabelReserve"/> property.
    /// </summary>
    public static readonly StyledProperty<double> XAxisLabelReserveProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(XAxisLabelReserve), 28);

    /// <summary>
    /// Vertical space reserved below the plotting area when x-axis labels are present.
    /// </summary>
    public double XAxisLabelReserve
    {
        get => GetValue(XAxisLabelReserveProperty);
        set => SetValue(XAxisLabelReserveProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="YAxisLabelReserve"/> property.
    /// </summary>
    public static readonly StyledProperty<double> YAxisLabelReserveProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(YAxisLabelReserve), 20);

    /// <summary>
    /// Horizontal space reserved to the left of the plotting area when y-axis labels are present.
    /// </summary>
    public double YAxisLabelReserve
    {
        get => GetValue(YAxisLabelReserveProperty);
        set => SetValue(YAxisLabelReserveProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AxisLabelBaselineOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AxisLabelBaselineOffsetProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(AxisLabelBaselineOffset), 16);

    /// <summary>
    /// Baseline offset used when positioning axis labels from the chart edge.
    /// </summary>
    public double AxisLabelBaselineOffset
    {
        get => GetValue(AxisLabelBaselineOffsetProperty);
        set => SetValue(AxisLabelBaselineOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="QuadrantLabelTopOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> QuadrantLabelTopOffsetProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(QuadrantLabelTopOffset), 20);

    /// <summary>
    /// Top offset used for quadrant labels when points are present.
    /// </summary>
    public double QuadrantLabelTopOffset
    {
        get => GetValue(QuadrantLabelTopOffsetProperty);
        set => SetValue(QuadrantLabelTopOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="PointLabelOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> PointLabelOffsetProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(PointLabelOffset), 12);

    /// <summary>
    /// Distance from a point marker edge to its label baseline.
    /// </summary>
    public double PointLabelOffset
    {
        get => GetValue(PointLabelOffsetProperty);
        set => SetValue(PointLabelOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="PointRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> PointRadiusProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(PointRadius), 6);

    /// <summary>
    /// Radius of plotted quadrant points.
    /// </summary>
    public double PointRadius
    {
        get => GetValue(PointRadiusProperty);
        set => SetValue(PointRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TitleFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TitleFontSizeProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(TitleFontSize), 18);

    /// <summary>
    /// Font size used for the optional title.
    /// </summary>
    public double TitleFontSize
    {
        get => GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="PointLabelFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> PointLabelFontSizeProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(PointLabelFontSize), 14);

    /// <summary>
    /// Font size used for point labels.
    /// </summary>
    public double PointLabelFontSize
    {
        get => GetValue(PointLabelFontSizeProperty);
        set => SetValue(PointLabelFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="QuadrantLabelFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> QuadrantLabelFontSizeProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(QuadrantLabelFontSize), 16);

    /// <summary>
    /// Font size used for labels inside each quadrant.
    /// </summary>
    public double QuadrantLabelFontSize
    {
        get => GetValue(QuadrantLabelFontSizeProperty);
        set => SetValue(QuadrantLabelFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AxisLabelFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AxisLabelFontSizeProperty =
        AvaloniaProperty.Register<QuadrantRenderer, double>(nameof(AxisLabelFontSize), 14);

    /// <summary>
    /// Font size used for axis labels.
    /// </summary>
    public double AxisLabelFontSize
    {
        get => GetValue(AxisLabelFontSizeProperty);
        set => SetValue(AxisLabelFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="QuadrantFills"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<IBrush>> QuadrantFillsProperty =
        AvaloniaProperty.Register<QuadrantRenderer, IReadOnlyList<IBrush>>(nameof(QuadrantFills), DefaultQuadrantFills);

    /// <summary>
    /// Fills used for quadrants in the order 1, 2, 3, 4.
    /// </summary>
    public IReadOnlyList<IBrush> QuadrantFills
    {
        get => GetValue(QuadrantFillsProperty);
        set => SetValue(QuadrantFillsProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="DividerDashPattern"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<double>> DividerDashPatternProperty =
        AvaloniaProperty.Register<QuadrantRenderer, IReadOnlyList<double>>(nameof(DividerDashPattern), DefaultDividerDashPattern);

    /// <summary>
    /// Dash pattern used for the vertical and horizontal quadrant dividers.
    /// </summary>
    public IReadOnlyList<double> DividerDashPattern
    {
        get => GetValue(DividerDashPatternProperty);
        set => SetValue(DividerDashPatternProperty, value);
    }

    private readonly record struct Style(
        double ChartSize,
        double Padding,
        double TitleHeight,
        double AxisLabelPadding,
        double XAxisLabelReserve,
        double YAxisLabelReserve,
        double AxisLabelBaselineOffset,
        double QuadrantLabelTopOffset,
        double PointLabelOffset,
        double PointRadius,
        double TitleFontSize,
        double PointLabelFontSize,
        double QuadrantLabelFontSize,
        double AxisLabelFontSize,
        IReadOnlyList<IBrush> QuadrantFills,
        IReadOnlyList<double> DividerDashPattern
    );

    /// <summary>
    /// Calculates the desired size for the chart using this renderer part's current styled values.
    /// </summary>
    public Size MeasureDiagram(QuadrantChart chart) => Measure(chart, CreateStyleSnapshot());

    /// <summary>
    /// Draws a quadrant chart using this renderer part's current styled values.
    /// </summary>
    public void RenderDiagram(DrawingContext dc, MermaidPresenter presenter, QuadrantChart chart)
    {
        var style = CreateStyleSnapshot();
        var hasTitle = chart.Title is { Length: > 0 };
        var hasPoints = chart.Points.Count > 0;
        var metrics = CreateLayoutMetrics(chart, style, hasTitle);
        var chartLeft = metrics.ChartLeft;
        var chartTop = metrics.ChartTop;
        var half = style.ChartSize / 2;

        if (hasTitle)
        {
            MermaidTextRenderer.DrawText(
                dc,
                presenter,
                chart.Title!,
                chartLeft + half,
                style.TitleHeight / 2,
                style.TitleFontSize,
                presenter.Foreground,
                TextAlignment.Center,
                centerVertically: true,
                fontWeight: FontWeight.Bold);
        }

        DrawQuadrant(dc, presenter, style, chartLeft, chartTop, half, half, GetFill(style, 1), chart.Quadrant2, hasPoints);
        DrawQuadrant(dc, presenter, style, chartLeft + half, chartTop, half, half, GetFill(style, 0), chart.Quadrant1, hasPoints);
        DrawQuadrant(dc, presenter, style, chartLeft, chartTop + half, half, half, GetFill(style, 2), chart.Quadrant3, hasPoints);
        DrawQuadrant(dc, presenter, style, chartLeft + half, chartTop + half, half, half, GetFill(style, 3), chart.Quadrant4, hasPoints);

        var borderPen = presenter.NodePen;
        dc.DrawRectangle(null, borderPen, new Rect(chartLeft, chartTop, style.ChartSize, style.ChartSize));

        var dividerPen = new Pen(presenter.NodeStroke, DividerStrokeThickness, new DashStyle(style.DividerDashPattern, 0));
        dc.DrawLine(dividerPen, new Point(chartLeft + half, chartTop), new Point(chartLeft + half, chartTop + style.ChartSize));
        dc.DrawLine(dividerPen, new Point(chartLeft, chartTop + half), new Point(chartLeft + style.ChartSize, chartTop + half));

        DrawAxisLabels(dc, presenter, style, chart, chartLeft, chartTop, hasPoints, metrics.AxisBottomPad);

        var occupied = chart.Points.Select(point => new Rect(
            chartLeft + point.X * style.ChartSize - style.PointRadius,
            chartTop + (1 - point.Y) * style.ChartSize - style.PointRadius,
            style.PointRadius * 2,
            style.PointRadius * 2)).ToList();
        var area = new Rect(chartLeft + 4, chartTop + 4, Math.Max(0, style.ChartSize - 8), Math.Max(0, style.ChartSize - 8));
        foreach (var point in chart.Points)
        {
            DrawPoint(dc, presenter, style, point, chartLeft, chartTop, occupied, area);
        }
    }

    /// <inheritdoc/>
    protected override bool ShouldInvalidatePresenterMeasure(AvaloniaProperty property) =>
        property.Name is nameof(ChartSize) or nameof(Padding) or nameof(TitleHeight) or nameof(XAxisLabelReserve) or nameof(YAxisLabelReserve);

    private static Size Measure(QuadrantChart chart, Style style)
    {
        var hasTitle = chart.Title is { Length: > 0 };
        var metrics = CreateLayoutMetrics(chart, style, hasTitle);
        return new Size(metrics.Width, metrics.Height);
    }

    private static QuadrantLayoutMetrics CreateLayoutMetrics(QuadrantChart chart, Style style, bool hasTitle)
    {
        var titleOffset = hasTitle ? style.TitleHeight : 0;
        var axisBottomPad = (chart.XAxisLeft ?? chart.XAxisRight) is not null ? style.XAxisLabelReserve : 0;
        var axisLeftPad = (chart.YAxisBottom ?? chart.YAxisTop) is not null ? style.YAxisLabelReserve : 0;
        var chartLeft = style.Padding + axisLeftPad;
        var chartTop = titleOffset + style.Padding;
        var width = style.Padding + axisLeftPad + style.ChartSize + style.Padding;
        var height = titleOffset + style.Padding + style.ChartSize + axisBottomPad + style.Padding;
        return new QuadrantLayoutMetrics(chartLeft, chartTop, axisBottomPad, width, height);
    }

    private Style CreateStyleSnapshot() =>
        new(
            ChartSize,
            Padding,
            TitleHeight,
            AxisLabelPadding,
            XAxisLabelReserve,
            YAxisLabelReserve,
            AxisLabelBaselineOffset,
            QuadrantLabelTopOffset,
            PointLabelOffset,
            PointRadius,
            TitleFontSize,
            PointLabelFontSize,
            QuadrantLabelFontSize,
            AxisLabelFontSize,
            QuadrantFills,
            DividerDashPattern);

    private static void DrawQuadrant(
        DrawingContext dc,
        MermaidPresenter presenter,
        Style style,
        double x,
        double y,
        double width,
        double height,
        IBrush fill,
        string? label,
        bool hasPoints)
    {
        dc.DrawRectangle(fill, null, new Rect(x, y, width, height));

        if (label is not { Length: > 0 })
        {
            return;
        }

        var textY = hasPoints ? y + style.QuadrantLabelTopOffset : y + height / 2;
        MermaidTextRenderer.DrawText(
            dc,
            presenter,
            label,
            x + width / 2,
            textY,
            style.QuadrantLabelFontSize,
            presenter.SecondaryForeground,
            TextAlignment.Center,
            centerVertically: true,
            FontWeight.SemiBold);
    }

    private static void DrawAxisLabels(
        DrawingContext dc,
        MermaidPresenter presenter,
        Style style,
        QuadrantChart chart,
        double chartLeft,
        double chartTop,
        bool hasPoints,
        double axisBottomPad)
    {
        var bottom = chartTop + style.ChartSize;
        var half = style.ChartSize / 2;

        if (hasPoints && axisBottomPad > 0)
        {
            DrawOptionalText(
                dc,
                presenter,
                chart.XAxisLeft,
                chartLeft,
                bottom + style.AxisLabelPadding + style.AxisLabelBaselineOffset,
                TextAlignment.Left,
                style);
            DrawOptionalText(
                dc,
                presenter,
                chart.XAxisRight,
                chartLeft + style.ChartSize,
                bottom + style.AxisLabelPadding + style.AxisLabelBaselineOffset,
                TextAlignment.Right,
                style);
        }
        else if (axisBottomPad > 0)
        {
            DrawOptionalText(
                dc,
                presenter,
                chart.XAxisLeft,
                chartLeft + half / 2,
                bottom + style.AxisLabelPadding + style.AxisLabelBaselineOffset,
                TextAlignment.Center,
                style);
            DrawOptionalText(
                dc,
                presenter,
                chart.XAxisRight,
                chartLeft + half + half / 2,
                bottom + style.AxisLabelPadding + style.AxisLabelBaselineOffset,
                TextAlignment.Center,
                style);
        }

        if (chart.YAxisBottom is { Length: > 0 })
        {
            var yPos = hasPoints ? bottom : chartTop + half + half / 2;
            DrawRotatedAxisText(dc, presenter, style, chart.YAxisBottom, chartLeft - style.AxisLabelPadding, yPos);
        }

        if (chart.YAxisTop is { Length: > 0 })
        {
            var yPos = hasPoints ? chartTop : chartTop + half / 2;
            DrawRotatedAxisText(dc, presenter, style, chart.YAxisTop, chartLeft - style.AxisLabelPadding, yPos);
        }
    }

    private static void DrawOptionalText(
        DrawingContext dc,
        MermaidPresenter presenter,
        string? text,
        double x,
        double y,
        TextAlignment alignment,
        Style style)
    {
        if (text is not { Length: > 0 })
        {
            return;
        }

        MermaidTextRenderer.DrawText(dc, presenter, text, x, y, style.AxisLabelFontSize, presenter.SecondaryForeground, alignment);
    }

    private static void DrawRotatedAxisText(DrawingContext dc, MermaidPresenter presenter, Style style, string text, double x, double y)
    {
        var matrix = Matrix.CreateTranslation(-x, -y) *
            Matrix.CreateRotation(-Math.PI / 2) *
            Matrix.CreateTranslation(x, y);
        using (dc.PushTransform(matrix))
        {
            MermaidTextRenderer.DrawText(dc, presenter, text, x, y, style.AxisLabelFontSize, presenter.SecondaryForeground, TextAlignment.Right);
        }
    }

    private static void DrawPoint(
        DrawingContext dc,
        MermaidPresenter presenter,
        Style style,
        QuadrantPoint point,
        double chartLeft,
        double chartTop,
        List<Rect> occupied,
        Rect area)
    {
        var x = chartLeft + point.X * style.ChartSize;
        var y = chartTop + (1 - point.Y) * style.ChartSize;

        dc.DrawEllipse(
            presenter.ArrowFill,
            new Pen(presenter.BackgroundBrush ?? Brushes.Transparent, PointStrokeThickness),
            new Point(x, y),
            style.PointRadius,
            style.PointRadius);
        var text = MermaidTextRenderer.CreateFormattedText(
            presenter,
            point.Label,
            style.PointLabelFontSize,
            presenter.Foreground,
            TextAlignment.Center);
        text.MaxTextWidth = Math.Max(1, Math.Min(text.Width, area.Width));
        var anchor = new Point(x, y + style.PointRadius + style.PointLabelOffset + text.Height / 2);
        var bounds = MermaidLabelPlacement.Place(new Size(text.MaxTextWidth, text.Height), anchor, occupied, area);
        occupied.Add(bounds);
        MermaidTextRenderer.DrawTextInRect(dc, presenter, point.Label, bounds, style.PointLabelFontSize, presenter.Foreground);
    }

    private static IBrush GetFill(Style style, int index) =>
        style.QuadrantFills.Count > 0 ?
            style.QuadrantFills[index % style.QuadrantFills.Count] :
            DefaultQuadrantFills[index % DefaultQuadrantFills.Count];

    private readonly record struct QuadrantLayoutMetrics(double ChartLeft, double ChartTop, double AxisBottomPad, double Width, double Height);
}