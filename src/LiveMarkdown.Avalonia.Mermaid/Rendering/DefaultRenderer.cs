using Avalonia;
using Avalonia.Media;
using Mermaider.Models;
using AvaloniaPoint = Avalonia.Point;
using MermaidRenderOptions = Mermaider.Models.RenderOptions;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Native Avalonia renderer for Mermaider positioned flowchart and state graph models.
/// </summary>
/// <remarks>
/// The renderer follows Mermaider's SVG renderer ordering: group bodies, edges, group headers,
/// edge labels, nodes, then notes. That order keeps labels and nodes above connectors while allowing
/// groups to act as a visual backdrop.
/// </remarks>
public class DefaultRenderer : MermaidRenderer
{
    /// <summary>
    /// Defines the <see cref="ArrowSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ArrowSizeProperty =
        AvaloniaProperty.Register<DefaultRenderer, double>(nameof(ArrowSize), 6);

    /// <summary>
    /// Size of flowchart and state diagram arrow heads, measured in diagram pixels.
    /// </summary>
    public double ArrowSize
    {
        get => GetValue(ArrowSizeProperty);
        set => SetValue(ArrowSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EdgeCornerRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EdgeCornerRadiusProperty =
        AvaloniaProperty.Register<DefaultRenderer, double>(nameof(EdgeCornerRadius), 6);

    /// <summary>
    /// Radius used when rounding orthogonal edge corners.
    /// </summary>
    public double EdgeCornerRadius
    {
        get => GetValue(EdgeCornerRadiusProperty);
        set => SetValue(EdgeCornerRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="RectangleCornerRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> RectangleCornerRadiusProperty =
        AvaloniaProperty.Register<DefaultRenderer, double>(nameof(RectangleCornerRadius), 6);

    /// <summary>
    /// Corner radius for rectangular node shapes.
    /// </summary>
    public double RectangleCornerRadius
    {
        get => GetValue(RectangleCornerRadiusProperty);
        set => SetValue(RectangleCornerRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="RoundedCornerRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> RoundedCornerRadiusProperty =
        AvaloniaProperty.Register<DefaultRenderer, double>(nameof(RoundedCornerRadius), 10);

    /// <summary>
    /// Corner radius for Mermaid rounded node shapes.
    /// </summary>
    public double RoundedCornerRadius
    {
        get => GetValue(RoundedCornerRadiusProperty);
        set => SetValue(RoundedCornerRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="GroupCornerRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> GroupCornerRadiusProperty =
        AvaloniaProperty.Register<DefaultRenderer, double>(nameof(GroupCornerRadius), 8);

    /// <summary>
    /// Corner radius for subgraph and state group containers.
    /// </summary>
    public double GroupCornerRadius
    {
        get => GetValue(GroupCornerRadiusProperty);
        set => SetValue(GroupCornerRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EdgeLabelPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EdgeLabelPaddingProperty =
        AvaloniaProperty.Register<DefaultRenderer, double>(nameof(EdgeLabelPadding), 8);

    /// <summary>
    /// Padding around edge label text when a background label box is drawn.
    /// </summary>
    public double EdgeLabelPadding
    {
        get => GetValue(EdgeLabelPaddingProperty);
        set => SetValue(EdgeLabelPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EdgeLabelCornerRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EdgeLabelCornerRadiusProperty =
        AvaloniaProperty.Register<DefaultRenderer, double>(nameof(EdgeLabelCornerRadius), 10);

    /// <summary>
    /// Corner radius for edge label background boxes.
    /// </summary>
    public double EdgeLabelCornerRadius
    {
        get => GetValue(EdgeLabelCornerRadiusProperty);
        set => SetValue(EdgeLabelCornerRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="SubroutineInset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SubroutineInsetProperty =
        AvaloniaProperty.Register<DefaultRenderer, double>(nameof(SubroutineInset), 8);

    /// <summary>
    /// Horizontal inset for the two inner vertical lines in subroutine nodes.
    /// </summary>
    public double SubroutineInset
    {
        get => GetValue(SubroutineInsetProperty);
        set => SetValue(SubroutineInsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CylinderCapRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CylinderCapRadiusProperty =
        AvaloniaProperty.Register<DefaultRenderer, double>(nameof(CylinderCapRadius), 7);

    /// <summary>
    /// Vertical radius of the ellipse caps used by cylinder/database nodes.
    /// </summary>
    public double CylinderCapRadius
    {
        get => GetValue(CylinderCapRadiusProperty);
        set => SetValue(CylinderCapRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AsymmetricIndent"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AsymmetricIndentProperty =
        AvaloniaProperty.Register<DefaultRenderer, double>(nameof(AsymmetricIndent), 12);

    /// <summary>
    /// Left-side indent for asymmetric flowchart nodes.
    /// </summary>
    public double AsymmetricIndent
    {
        get => GetValue(AsymmetricIndentProperty);
        set => SetValue(AsymmetricIndentProperty, value);
    }

    private readonly record struct Style(
        double ArrowSize,
        double EdgeCornerRadius,
        double RectangleCornerRadius,
        double RoundedCornerRadius,
        double GroupCornerRadius,
        double EdgeLabelPadding,
        double EdgeLabelCornerRadius,
        double SubroutineInset,
        double CylinderCapRadius,
        double AsymmetricIndent
    );

    /// <summary>
    /// Draws a positioned graph using this renderer part's current styled values.
    /// </summary>
    /// <param name="dc"></param>
    /// <param name="presenter"></param>
    /// <param name="positionedGraph"></param>
    /// <param name="options"></param>
    public void RenderGraph(DrawingContext dc, MermaidPresenter presenter, PositionedGraph positionedGraph, MermaidRenderOptions? options = null)
    {
        RenderGraph(dc, presenter, PreparedPositionedGraph.Prepare(positionedGraph), options);
    }

    /// <summary>
    /// Draws a prepared positioned graph using this renderer part's current styled values.
    /// </summary>
    internal void RenderGraph(DrawingContext dc, MermaidPresenter presenter, PreparedPositionedGraph graph, MermaidRenderOptions? options = null)
    {
        var style = CreateStyleSnapshot(options);
        using var offset = dc.PushTransform(Matrix.CreateTranslation(-graph.MinX, 0));
        foreach (var group in graph.Groups)
            DrawGroupBody(dc, presenter, style, group);

        foreach (var edge in graph.Edges)
        {
            if (edge.Edge.Style != EdgeStyle.Invisible)
                DrawEdge(dc, presenter, style, edge);
        }

        foreach (var group in graph.Groups)
            DrawGroupHeader(dc, presenter, style, group);

        foreach (var edge in graph.Edges)
        {
            if (edge.Edge.Style != EdgeStyle.Invisible && edge.Edge.Label is not null)
                DrawEdgeLabel(dc, presenter, style, edge);
        }

        foreach (var node in graph.Nodes)
            DrawNode(dc, presenter, style, node);

        foreach (var note in graph.Notes)
            DrawNote(dc, presenter, note);
    }

    public double GetEffectiveEdgeCornerRadius(MermaidRenderOptions? options)
    {
        if (options?.RoundedEdges == false && !IsSet(EdgeCornerRadiusProperty))
            return 0;

        return EdgeCornerRadius;
    }

    private Style CreateStyleSnapshot(MermaidRenderOptions? options) =>
        new(
            ArrowSize,
            GetEffectiveEdgeCornerRadius(options),
            RectangleCornerRadius,
            RoundedCornerRadius,
            GroupCornerRadius,
            EdgeLabelPadding,
            EdgeLabelCornerRadius,
            SubroutineInset,
            CylinderCapRadius,
            AsymmetricIndent);

    // ========================================================================
    // Group rendering
    // ========================================================================

    private static void DrawGroupBody(DrawingContext dc, MermaidPresenter presenter, Style style, PreparedPositionedGroup preparedGroup)
    {
        var group = preparedGroup.Group;
        var rect = new Rect(group.X, group.Y, group.Width, group.Height);
        dc.DrawRectangle(presenter.GroupFill, presenter.GroupPen, rect, style.GroupCornerRadius, style.GroupCornerRadius);

        foreach (var child in preparedGroup.Children)
            DrawGroupBody(dc, presenter, style, child);
    }

    private static void DrawGroupHeader(DrawingContext dc, MermaidPresenter presenter, Style style, PreparedPositionedGroup preparedGroup)
    {
        var group = preparedGroup.Group;
        var headerHeight = presenter.GroupHeaderFontSize + 16;
        var rect = new Rect(group.X, group.Y, group.Width, headerHeight);

        dc.DrawRectangle(presenter.GroupHeaderFill, presenter.GroupPen, rect, style.GroupCornerRadius, style.GroupCornerRadius);

        MermaidTextRenderer.DrawInlineText(
            dc,
            presenter,
            preparedGroup.LabelLayout,
            group.X + 12,
            group.Y + headerHeight / 2.0,
            presenter.GroupHeaderFontSize,
            presenter.SecondaryForeground,
            TextAlignment.Left,
            centerVertically: true);

        foreach (var child in preparedGroup.Children)
            DrawGroupHeader(dc, presenter, style, child);
    }

    // ========================================================================
    // Edge rendering
    // ========================================================================

    private static void DrawEdge(DrawingContext dc, MermaidPresenter presenter, Style style, PreparedPositionedEdge preparedEdge)
    {
        var edge = preparedEdge.Edge;
        if (edge.Points.Count < 2) return;

        var pen = edge.Style switch
        {
            EdgeStyle.Thick => presenter.ThickLinePen,
            EdgeStyle.Dotted => presenter.DottedLinePen,
            _ => presenter.LinePen
        };

        if (preparedEdge.GetRoundedPath(style.EdgeCornerRadius) is { } roundedPath)
        {
            dc.DrawGeometry(null, pen, roundedPath);
        }

        if (edge.HasArrowEnd)
            MermaidDrawingHelpers.DrawArrowHead(dc, presenter.ArrowFill, edge.Points[^2], edge.Points[^1], false, style.ArrowSize);

        if (edge.HasArrowStart)
            MermaidDrawingHelpers.DrawArrowHead(dc, presenter.ArrowFill, edge.Points[1], edge.Points[0], true, style.ArrowSize);
    }

    private static void DrawEdgeLabel(DrawingContext dc, MermaidPresenter presenter, Style style, PreparedPositionedEdge preparedEdge)
    {
        if (preparedEdge.LabelLayout is null)
            return;

        var mid = preparedEdge.LabelPosition;
        MermaidTextRenderer.DrawTextWithBackground(
            dc,
            presenter,
            preparedEdge.LabelLayout,
            mid.X,
            mid.Y,
            presenter.EdgeLabelFontSize,
            presenter.SecondaryForeground,
            presenter.EdgeLabelBackground,
            presenter.EdgeLabelPen,
            padding: style.EdgeLabelPadding,
            radius: style.EdgeLabelCornerRadius);
    }

    // ========================================================================
    // Node rendering
    // ========================================================================

    private static void DrawNode(DrawingContext dc, MermaidPresenter presenter, Style style, PreparedPositionedNode preparedNode)
    {
        var node = preparedNode.Node;
        var (x, y, w, h) = (node.X, node.Y, node.Width, node.Height);

        var styleFill = node.InlineStyle?.GetValueOrDefault("fill");
        var styleStroke = node.InlineStyle?.GetValueOrDefault("stroke");
        var styleStrokeWidth = node.InlineStyle?.GetValueOrDefault("stroke-width");

        var fill = presenter.GetCachedBrush(styleFill, presenter.NodeFill);
        var stroke = presenter.GetCachedPen(styleStroke, styleStrokeWidth, presenter.NodePen);
        if (fill is null && stroke is null) return;

        switch (node.Shape)
        {
            case NodeShape.Rectangle:
                dc.DrawRectangle(fill, stroke, new Rect(x, y, w, h), style.RectangleCornerRadius, style.RectangleCornerRadius);
                break;
            case NodeShape.Rounded:
                dc.DrawRectangle(fill, stroke, new Rect(x, y, w, h), style.RoundedCornerRadius, style.RoundedCornerRadius);
                break;
            case NodeShape.Stadium:
                dc.DrawRectangle(fill, stroke, new Rect(x, y, w, h), h / 2, h / 2);
                break;
            case NodeShape.Diamond:
                MermaidDrawingHelpers.DrawPolygon(
                    dc,
                    fill,
                    stroke,
                    new AvaloniaPoint(x + w / 2, y),
                    new AvaloniaPoint(x + w, y + h / 2),
                    new AvaloniaPoint(x + w / 2, y + h),
                    new AvaloniaPoint(x, y + h / 2));
                break;
            case NodeShape.Circle:
                dc.DrawEllipse(fill, stroke, new AvaloniaPoint(x + w / 2, y + h / 2), Math.Min(w, h) / 2, Math.Min(w, h) / 2);
                break;
            case NodeShape.DoubleCircle:
                var cx = x + w / 2;
                var cy = y + h / 2;
                var outerR = Math.Min(w, h) / 2;
                dc.DrawEllipse(fill, stroke, new AvaloniaPoint(cx, cy), outerR, outerR);
                dc.DrawEllipse(fill, stroke, new AvaloniaPoint(cx, cy), outerR - 5, outerR - 5);
                break;
            case NodeShape.Subroutine when stroke is not null:
                dc.DrawRectangle(fill, stroke, new Rect(x, y, w, h), style.RectangleCornerRadius, style.RectangleCornerRadius);
                dc.DrawLine(stroke, new AvaloniaPoint(x + style.SubroutineInset, y), new AvaloniaPoint(x + style.SubroutineInset, y + h));
                dc.DrawLine(stroke, new AvaloniaPoint(x + w - style.SubroutineInset, y), new AvaloniaPoint(x + w - style.SubroutineInset, y + h));
                break;
            case NodeShape.Hexagon:
                var hexInset = h / 4;
                MermaidDrawingHelpers.DrawPolygon(
                    dc,
                    fill,
                    stroke,
                    new AvaloniaPoint(x + hexInset, y),
                    new AvaloniaPoint(x + w - hexInset, y),
                    new AvaloniaPoint(x + w, y + h / 2),
                    new AvaloniaPoint(x + w - hexInset, y + h),
                    new AvaloniaPoint(x + hexInset, y + h),
                    new AvaloniaPoint(x, y + h / 2));
                break;
            case NodeShape.Cylinder:
                var ry = style.CylinderCapRadius;
                var bodyTop = y + ry;
                var bodyH = h - 2 * ry;
                dc.DrawRectangle(fill, null, new Rect(x, bodyTop, w, bodyH));
                if (stroke is not null) dc.DrawLine(stroke, new AvaloniaPoint(x, bodyTop), new AvaloniaPoint(x, bodyTop + bodyH));
                if (stroke is not null) dc.DrawLine(stroke, new AvaloniaPoint(x + w, bodyTop), new AvaloniaPoint(x + w, bodyTop + bodyH));
                dc.DrawEllipse(fill, stroke, new AvaloniaPoint(x + w / 2, y + h - ry), w / 2, ry); // Bottom
                dc.DrawEllipse(fill, stroke, new AvaloniaPoint(x + w / 2, bodyTop), w / 2, ry); // Top
                break;
            case NodeShape.Asymmetric:
                MermaidDrawingHelpers.DrawPolygon(
                    dc,
                    fill,
                    stroke,
                    new AvaloniaPoint(x + style.AsymmetricIndent, y),
                    new AvaloniaPoint(x + w, y),
                    new AvaloniaPoint(x + w, y + h),
                    new AvaloniaPoint(x + style.AsymmetricIndent, y + h),
                    new AvaloniaPoint(x, y + h / 2));
                break;
            case NodeShape.Trapezoid:
                var trapInset = w * 0.15;
                MermaidDrawingHelpers.DrawPolygon(
                    dc,
                    fill,
                    stroke,
                    new AvaloniaPoint(x + trapInset, y),
                    new AvaloniaPoint(x + w - trapInset, y),
                    new AvaloniaPoint(x + w, y + h),
                    new AvaloniaPoint(x, y + h));
                break;
            case NodeShape.TrapezoidAlt:
                var altInset = w * 0.15;
                MermaidDrawingHelpers.DrawPolygon(
                    dc,
                    fill,
                    stroke,
                    new AvaloniaPoint(x, y),
                    new AvaloniaPoint(x + w, y),
                    new AvaloniaPoint(x + w - altInset, y + h),
                    new AvaloniaPoint(x + altInset, y + h));
                break;
            case NodeShape.Parallelogram:
            case NodeShape.ParallelogramAlt:
                var lean = w * 0.15;
                var isRight = node.Shape == NodeShape.Parallelogram;
                MermaidDrawingHelpers.DrawPolygon(
                    dc,
                    fill,
                    stroke,
                    new AvaloniaPoint(x + (isRight ? lean : 0), y),
                    new AvaloniaPoint(x + w - (isRight ? 0 : lean), y),
                    new AvaloniaPoint(x + w - (isRight ? lean : 0), y + h),
                    new AvaloniaPoint(x + (isRight ? 0 : lean), y + h));
                break;
            case NodeShape.StateStart:
                dc.DrawEllipse(
                    presenter.Foreground,
                    null,
                    new AvaloniaPoint(x + w / 2, y + h / 2),
                    (Math.Min(w, h) / 2) - 2,
                    (Math.Min(w, h) / 2) - 2);
                break;
            case NodeShape.StateEnd:
                var seCx = x + w / 2;
                var seCy = y + h / 2;
                var seOuter = (Math.Min(w, h) / 2) - 2;
                dc.DrawEllipse(
                    null,
                    presenter.StateEndPen,
                    new AvaloniaPoint(seCx, seCy),
                    seOuter,
                    seOuter);
                dc.DrawEllipse(presenter.Foreground, null, new AvaloniaPoint(seCx, seCy), seOuter - 4, seOuter - 4);
                break;
            case NodeShape.ForkJoin:
                dc.DrawRectangle(presenter.Foreground, null, new Rect(x, y, w, h), 2, 2);
                break;
        }

        DrawNodeLabel(dc, presenter, preparedNode);
    }

    private static void DrawNote(DrawingContext dc, MermaidPresenter presenter, PreparedPositionedNote preparedNote)
    {
        var note = preparedNote.Note;
        if (note.LineFrom is { } from && note.LineTo is { } to && presenter.DottedLinePen is { } pen)
        {
            dc.DrawLine(pen, new AvaloniaPoint(from.X, from.Y), new AvaloniaPoint(to.X, to.Y));
        }
        var rect = new Rect(note.X, note.Y, note.Width, note.Height);
        dc.DrawRectangle(presenter.AccentFill, presenter.AccentPen, rect, 6, 6);

        MermaidTextRenderer.DrawInlineText(
            dc,
            presenter,
            preparedNote.TextLayout,
            note.X + note.Width / 2,
            note.Y + note.Height / 2,
            presenter.EdgeLabelFontSize,
            presenter.AccentForeground,
            TextAlignment.Center,
            true);
    }

    // ========================================================================
    // Text / Markdown rendering
    // ========================================================================

    private static void DrawNodeLabel(DrawingContext dc, MermaidPresenter presenter, PreparedPositionedNode preparedNode)
    {
        var node = preparedNode.Node;
        if (node.Shape is NodeShape.StateStart or NodeShape.StateEnd or NodeShape.ForkJoin && string.IsNullOrEmpty(node.Label))
            return;

        var cx = node.X + (node.Width / 2);
        var cy = node.Y + (node.Height / 2);

        var colorStr = node.InlineStyle?.GetValueOrDefault("color");
        var textBrush = presenter.GetCachedBrush(colorStr, presenter.Foreground);

        MermaidTextRenderer.DrawInlineText(
            dc,
            presenter,
            preparedNode.LabelLayout,
            cx,
            cy,
            presenter.NodeLabelFontSize,
            textBrush,
            TextAlignment.Center,
            centerVertically: true);
    }
}