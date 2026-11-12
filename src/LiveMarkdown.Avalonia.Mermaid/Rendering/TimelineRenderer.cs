using Avalonia;
using Avalonia.Media;
using Mermaider.Models;
using Point = Avalonia.Point;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Native Avalonia renderer for Mermaid timeline diagrams.
/// </summary>
/// <remarks>
/// Mermaid timeline models are not pre-positioned by Mermaider, so this renderer owns the compact
/// period-grid layout used by Mermaider's SVG renderer. Section and period metrics are styleable to
/// keep diagram-specific spacing out of <see cref="MermaidPresenter"/>.
/// </remarks>
public class TimelineRenderer : MermaidRenderer
{
    private static readonly IReadOnlyList<Color> DefaultSectionPalette =
    [
        Color.Parse("#4e79a7"), Color.Parse("#f28e2b"), Color.Parse("#e15759"), Color.Parse("#76b7b2"),
        Color.Parse("#59a14f"), Color.Parse("#edc948"), Color.Parse("#b07aa1"), Color.Parse("#ff9da7")
    ];

    // Mermaider's SVG renderer returns a small empty canvas when a timeline has no periods.
    private const double EmptyFallbackWidth = 200;
    private const double EmptyFallbackHeight = 100;

    // Visual compatibility constants from Mermaider's SVG renderer; these do not affect layout.
    private const byte SectionFillAlpha = 0x14;
    private const byte EventFillAlpha = 0x26;
    private const double SectionCornerRadius = 6;
    private const double EventBoxCornerRadius = 6;
    private const double MarkerStrokeThickness = 2;

    /// <summary>
    /// Defines the <see cref="PeriodWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double> PeriodWidthProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(PeriodWidth), 160);

    /// <summary>
    /// Width allocated to each period column.
    /// </summary>
    public double PeriodWidth
    {
        get => GetValue(PeriodWidthProperty);
        set => SetValue(PeriodWidthProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="PeriodGap"/> property.
    /// </summary>
    public static readonly StyledProperty<double> PeriodGapProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(PeriodGap), 20);

    /// <summary>
    /// Horizontal gap between period columns.
    /// </summary>
    public double PeriodGap
    {
        get => GetValue(PeriodGapProperty);
        set => SetValue(PeriodGapProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EventBoxHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EventBoxHeightProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(EventBoxHeight), 28);

    /// <summary>
    /// Height of each event box.
    /// </summary>
    public double EventBoxHeight
    {
        get => GetValue(EventBoxHeightProperty);
        set => SetValue(EventBoxHeightProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EventGap"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EventGapProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(EventGap), 6);

    /// <summary>
    /// Vertical gap between event boxes in a period.
    /// </summary>
    public double EventGap
    {
        get => GetValue(EventGapProperty);
        set => SetValue(EventGapProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TimelineY"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TimelineYProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(TimelineY), 80);

    /// <summary>
    /// Y coordinate of the timeline axis after the optional title offset.
    /// </summary>
    public double TimelineY
    {
        get => GetValue(TimelineYProperty);
        set => SetValue(TimelineYProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TitleOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TitleOffsetProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(TitleOffset), 40);

    /// <summary>
    /// Vertical offset added to the timeline axis when a title is present.
    /// </summary>
    public double TitleOffset
    {
        get => GetValue(TitleOffsetProperty);
        set => SetValue(TitleOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TitleY"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TitleYProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(TitleY), 28);

    /// <summary>
    /// Y coordinate of the title anchor inside the title offset area.
    /// </summary>
    public double TitleY
    {
        get => GetValue(TitleYProperty);
        set => SetValue(TitleYProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LeftPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LeftPaddingProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(LeftPadding), 40);

    /// <summary>
    /// Left padding before the first period column.
    /// </summary>
    public double LeftPadding
    {
        get => GetValue(LeftPaddingProperty);
        set => SetValue(LeftPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="RightPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> RightPaddingProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(RightPadding), 20);

    /// <summary>
    /// Right padding after the final period column and trailing gap.
    /// </summary>
    public double RightPadding
    {
        get => GetValue(RightPaddingProperty);
        set => SetValue(RightPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AxisExtension"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AxisExtensionProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(AxisExtension), 10);

    /// <summary>
    /// Horizontal extension of the timeline axis before the first and after the last marker.
    /// </summary>
    public double AxisExtension
    {
        get => GetValue(AxisExtensionProperty);
        set => SetValue(AxisExtensionProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="MarkerRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MarkerRadiusProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(MarkerRadius), 8);

    /// <summary>
    /// Radius of period markers on the timeline axis.
    /// </summary>
    public double MarkerRadius
    {
        get => GetValue(MarkerRadiusProperty);
        set => SetValue(MarkerRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="SectionPaddingX"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SectionPaddingXProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(SectionPaddingX), 10);

    /// <summary>
    /// Horizontal padding added around section background bands.
    /// </summary>
    public double SectionPaddingX
    {
        get => GetValue(SectionPaddingXProperty);
        set => SetValue(SectionPaddingXProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="SectionPaddingY"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SectionPaddingYProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(SectionPaddingY), 10);

    /// <summary>
    /// Extra vertical padding included at the bottom of the timeline.
    /// </summary>
    public double SectionPaddingY
    {
        get => GetValue(SectionPaddingYProperty);
        set => SetValue(SectionPaddingYProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AxisBottomPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AxisBottomPaddingProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(AxisBottomPadding), 50);

    /// <summary>
    /// Vertical space between the axis row and the measured event area.
    /// </summary>
    public double AxisBottomPadding
    {
        get => GetValue(AxisBottomPaddingProperty);
        set => SetValue(AxisBottomPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EventAreaBottomPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EventAreaBottomPaddingProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(EventAreaBottomPadding), 20);

    /// <summary>
    /// Extra vertical padding included below the tallest period's event stack.
    /// </summary>
    public double EventAreaBottomPadding
    {
        get => GetValue(EventAreaBottomPaddingProperty);
        set => SetValue(EventAreaBottomPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="SectionTopOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SectionTopOffsetProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(SectionTopOffset), 30);

    /// <summary>
    /// Distance from the axis to the top of a section background band.
    /// </summary>
    public double SectionTopOffset
    {
        get => GetValue(SectionTopOffsetProperty);
        set => SetValue(SectionTopOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="SectionBottomExtension"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SectionBottomExtensionProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(SectionBottomExtension), 20);

    /// <summary>
    /// Distance a section background band extends below the measured diagram height anchor.
    /// </summary>
    public double SectionBottomExtension
    {
        get => GetValue(SectionBottomExtensionProperty);
        set => SetValue(SectionBottomExtensionProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="SectionLabelOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SectionLabelOffsetProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(SectionLabelOffset), 16);

    /// <summary>
    /// Distance from the axis to the section label anchor.
    /// </summary>
    public double SectionLabelOffset
    {
        get => GetValue(SectionLabelOffsetProperty);
        set => SetValue(SectionLabelOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EventTopOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EventTopOffsetProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(EventTopOffset), 30);

    /// <summary>
    /// Distance from the axis to the first event box.
    /// </summary>
    public double EventTopOffset
    {
        get => GetValue(EventTopOffsetProperty);
        set => SetValue(EventTopOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EventHorizontalInset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EventHorizontalInsetProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(EventHorizontalInset), 10);

    /// <summary>
    /// Horizontal inset applied to event boxes inside each period column.
    /// </summary>
    public double EventHorizontalInset
    {
        get => GetValue(EventHorizontalInsetProperty);
        set => SetValue(EventHorizontalInsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="PeriodLabelOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> PeriodLabelOffsetProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(PeriodLabelOffset), 14);

    /// <summary>
    /// Distance from the axis to the period label anchor above each marker.
    /// </summary>
    public double PeriodLabelOffset
    {
        get => GetValue(PeriodLabelOffsetProperty);
        set => SetValue(PeriodLabelOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TitleFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TitleFontSizeProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(TitleFontSize), 18);

    /// <summary>
    /// Font size used for the optional timeline title.
    /// </summary>
    public double TitleFontSize
    {
        get => GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="PeriodFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> PeriodFontSizeProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(PeriodFontSize), 14);

    /// <summary>
    /// Font size used for period labels above markers.
    /// </summary>
    public double PeriodFontSize
    {
        get => GetValue(PeriodFontSizeProperty);
        set => SetValue(PeriodFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EventFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EventFontSizeProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(EventFontSize), 14);

    /// <summary>
    /// Font size used inside event boxes.
    /// </summary>
    public double EventFontSize
    {
        get => GetValue(EventFontSizeProperty);
        set => SetValue(EventFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="SectionFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SectionFontSizeProperty =
        AvaloniaProperty.Register<TimelineRenderer, double>(nameof(SectionFontSize), 14);

    /// <summary>
    /// Font size used for section labels.
    /// </summary>
    public double SectionFontSize
    {
        get => GetValue(SectionFontSizeProperty);
        set => SetValue(SectionFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="SectionPalette"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<Color>> SectionPaletteProperty =
        AvaloniaProperty.Register<TimelineRenderer, IReadOnlyList<Color>>(nameof(SectionPalette), DefaultSectionPalette);

    /// <summary>
    /// Repeating color palette used for section labels, markers, and event fills.
    /// </summary>
    public IReadOnlyList<Color> SectionPalette
    {
        get => GetValue(SectionPaletteProperty);
        set => SetValue(SectionPaletteProperty, value);
    }

    private readonly record struct Style(
        double PeriodWidth,
        double PeriodGap,
        double EventBoxHeight,
        double EventGap,
        double TimelineY,
        double TitleOffset,
        double TitleY,
        double LeftPadding,
        double RightPadding,
        double AxisExtension,
        double MarkerRadius,
        double SectionPaddingX,
        double SectionPaddingY,
        double AxisBottomPadding,
        double EventAreaBottomPadding,
        double SectionTopOffset,
        double SectionBottomExtension,
        double SectionLabelOffset,
        double EventTopOffset,
        double EventHorizontalInset,
        double PeriodLabelOffset,
        double TitleFontSize,
        double PeriodFontSize,
        double EventFontSize,
        double SectionFontSize,
        IReadOnlyList<Color> SectionPalette
    );

    /// <summary>
    /// Calculates the desired size for the diagram using this renderer part's current styled values.
    /// </summary>
    public Size MeasureDiagram(TimelineDiagram diagram) => Measure(diagram, CreateStyleSnapshot());

    /// <summary>
    /// Draws a timeline diagram using this renderer part's current styled values.
    /// </summary>
    public void RenderDiagram(DrawingContext dc, MermaidPresenter presenter, TimelineDiagram diagram)
    {
        var style = CreateStyleSnapshot();
        var metrics = CreateLayoutMetrics(diagram, style);
        var totalPeriods = diagram.Sections.Sum(static section => section.Periods.Count);

        if (totalPeriods == 0)
        {
            return;
        }

        var lineStartX = style.LeftPadding + style.PeriodWidth / 2 - style.AxisExtension;
        var lineEndX = style.LeftPadding + (totalPeriods - 1) * (style.PeriodWidth + style.PeriodGap) + style.PeriodWidth / 2 + style.AxisExtension;
        if (presenter.LinePen is { } linePen)
        {
            dc.DrawLine(linePen, new Point(lineStartX, metrics.AxisTop), new Point(lineEndX, metrics.AxisTop));
        }

        if (metrics.HasTitle)
        {
            MermaidTextRenderer.DrawText(
                dc,
                presenter,
                diagram.Title!,
                metrics.Width / 2,
                style.TitleY,
                style.TitleFontSize,
                presenter.Foreground,
                TextAlignment.Center,
                fontWeight: FontWeight.Bold);
        }

        var periodIndex = 0;
        var sectionColorIndex = 0;

        foreach (var section in diagram.Sections)
        {
            var sectionStartX = style.LeftPadding + periodIndex * (style.PeriodWidth + style.PeriodGap);
            var sectionWidth = section.Periods.Count * (style.PeriodWidth + style.PeriodGap) - style.PeriodGap;
            var color = GetPaletteColor(style.SectionPalette, sectionColorIndex);

            if (section is { Name.Length: > 0, Periods.Count: > 0 })
            {
                DrawSection(dc, presenter, style, section, color, sectionStartX, sectionWidth, metrics.AxisTop, metrics.Height);
            }

            foreach (var period in section.Periods)
            {
                var cx = style.LeftPadding + periodIndex * (style.PeriodWidth + style.PeriodGap) + style.PeriodWidth / 2;
                DrawPeriod(dc, presenter, style, period, color, cx, metrics.AxisTop);
                periodIndex++;
            }

            sectionColorIndex++;
        }
    }

    /// <inheritdoc/>
    protected override bool ShouldInvalidatePresenterMeasure(AvaloniaProperty property)
    {
        return property.Name is nameof(PeriodWidth) or nameof(PeriodGap) or nameof(EventBoxHeight) or nameof(EventGap) or nameof(TimelineY) or
            nameof(TitleOffset) or nameof(LeftPadding) or nameof(RightPadding) or nameof(SectionPaddingY) or nameof(AxisBottomPadding) or
            nameof(EventAreaBottomPadding) or nameof(SectionBottomExtension) or nameof(EventFontSize) or nameof(PeriodFontSize) or
            nameof(SectionFontSize) or nameof(SectionLabelOffset) or nameof(PeriodLabelOffset);
    }

    private static Size Measure(TimelineDiagram diagram, Style style)
    {
        var metrics = CreateLayoutMetrics(diagram, style);
        return new Size(metrics.Width, metrics.Height);
    }

    private static TimelineLayoutMetrics CreateLayoutMetrics(TimelineDiagram diagram, Style style)
    {
        var totalPeriods = 0;
        var maxEvents = 0;
        foreach (var section in diagram.Sections)
        {
            totalPeriods += section.Periods.Count;
            foreach (var period in section.Periods)
            {
                if (period.Events.Count > maxEvents)
                {
                    maxEvents = period.Events.Count;
                }
            }
        }

        if (totalPeriods == 0)
        {
            return new TimelineLayoutMetrics(false, 0, EmptyFallbackWidth, EmptyFallbackHeight);
        }

        var hasTitle = diagram.Title is { Length: > 0 };
        var titleOffset = hasTitle ? style.TitleOffset : 0;
        var axisTop = titleOffset + style.TimelineY;
        var width = style.LeftPadding + totalPeriods * (style.PeriodWidth + style.PeriodGap) + style.RightPadding;
        var eventAreaHeight = maxEvents * (style.EventBoxHeight + style.EventGap) + style.EventAreaBottomPadding;
        var height = axisTop + style.AxisBottomPadding + eventAreaHeight + style.SectionPaddingY + Math.Max(0, style.SectionBottomExtension);
        return new TimelineLayoutMetrics(hasTitle, axisTop, width, height);
    }

    private Style CreateStyleSnapshot() =>
        new(
            PeriodWidth,
            PeriodGap,
            EventBoxHeight,
            EventGap,
            TimelineY,
            TitleOffset,
            TitleY,
            LeftPadding,
            RightPadding,
            AxisExtension,
            MarkerRadius,
            SectionPaddingX,
            SectionPaddingY,
            AxisBottomPadding,
            EventAreaBottomPadding,
            SectionTopOffset,
            SectionBottomExtension,
            SectionLabelOffset,
            EventTopOffset,
            EventHorizontalInset,
            PeriodLabelOffset,
            TitleFontSize,
            PeriodFontSize,
            EventFontSize,
            SectionFontSize,
            SectionPalette);

    private static void DrawSection(
        DrawingContext dc,
        MermaidPresenter presenter,
        Style style,
        TimelineSection section,
        Color color,
        double sectionStartX,
        double sectionWidth,
        double axisTop,
        double totalHeight)
    {
        var labelY = axisTop - style.PeriodLabelOffset - style.PeriodFontSize * 1.5 - style.SectionLabelOffset;
        var top = Math.Min(axisTop - style.SectionTopOffset, labelY - style.SectionFontSize);
        dc.DrawRectangle(
            new SolidColorBrush(WithAlpha(color, SectionFillAlpha)),
            null,
            new Rect(
                sectionStartX - style.SectionPaddingX,
                top,
                sectionWidth + style.SectionPaddingX * 2,
                Math.Max(0, totalHeight - style.SectionPaddingY - top)),
            SectionCornerRadius,
            SectionCornerRadius);

        MermaidTextRenderer.DrawText(
            dc,
            presenter,
            section.Name!,
            sectionStartX + sectionWidth / 2,
            labelY,
            style.SectionFontSize,
            new SolidColorBrush(color),
            TextAlignment.Center,
            centerVertically: true,
            FontWeight.SemiBold);
    }

    private static void DrawPeriod(
        DrawingContext dc,
        MermaidPresenter presenter,
        Style style,
        TimelinePeriod period,
        Color color,
        double cx,
        double axisTop)
    {
        dc.DrawEllipse(
            new SolidColorBrush(color),
            new Pen(presenter.BackgroundBrush ?? Brushes.Transparent, MarkerStrokeThickness),
            new Point(cx, axisTop),
            style.MarkerRadius,
            style.MarkerRadius);

        MermaidTextRenderer.DrawTextInRect(
            dc,
            presenter,
            period.Label,
            new Rect(
                cx - style.PeriodWidth / 2,
                axisTop - style.PeriodLabelOffset - style.PeriodFontSize * 1.5,
                style.PeriodWidth,
                style.PeriodFontSize * 1.5),
            style.PeriodFontSize,
            presenter.Foreground,
            FontWeight.SemiBold);

        var eventY = axisTop + style.EventTopOffset;
        foreach (var evt in period.Events)
        {
            var boxX = cx - style.PeriodWidth / 2 + style.EventHorizontalInset;
            var boxWidth = style.PeriodWidth - style.EventHorizontalInset * 2;
            dc.DrawRectangle(
                new SolidColorBrush(WithAlpha(color, EventFillAlpha)),
                null,
                new Rect(boxX, eventY, boxWidth, style.EventBoxHeight),
                EventBoxCornerRadius,
                EventBoxCornerRadius);

            MermaidTextRenderer.DrawTextInRect(
                dc,
                presenter,
                evt,
                new Rect(boxX + 4, eventY + 2, Math.Max(0, boxWidth - 8), Math.Max(0, style.EventBoxHeight - 4)),
                style.EventFontSize,
                presenter.Foreground);

            eventY += style.EventBoxHeight + style.EventGap;
        }
    }

    private static Color GetPaletteColor(IReadOnlyList<Color> palette, int index) =>
        palette.Count > 0 ? palette[index % palette.Count] : DefaultSectionPalette[index % DefaultSectionPalette.Count];

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private readonly record struct TimelineLayoutMetrics(bool HasTitle, double AxisTop, double Width, double Height);
}