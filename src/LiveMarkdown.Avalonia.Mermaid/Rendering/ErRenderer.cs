using Avalonia;
using Avalonia.Media;
using Mermaider.Models;
using Point = Avalonia.Point;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Native Avalonia renderer entry point for Mermaid entity-relationship diagrams.
/// </summary>
/// <remarks>
/// The parser and layout model come from Mermaider; this renderer is responsible only for translating
/// the positioned diagram into Avalonia drawing primitives.
/// </remarks>
public class ErRenderer : MermaidRenderer
{
    /// <summary>
    /// Defines the <see cref="RelationshipCornerRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> RelationshipCornerRadiusProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(RelationshipCornerRadius), 6);

    /// <summary>
    /// Radius used when rounding ER relationship connector corners.
    /// </summary>
    public double RelationshipCornerRadius
    {
        get => GetValue(RelationshipCornerRadiusProperty);
        set => SetValue(RelationshipCornerRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AttributePaddingX"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AttributePaddingXProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(AttributePaddingX), 8);

    /// <summary>
    /// Horizontal padding for attribute rows inside entity boxes.
    /// </summary>
    public double AttributePaddingX
    {
        get => GetValue(AttributePaddingXProperty);
        set => SetValue(AttributePaddingXProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="KeyBadgePaddingX"/> property.
    /// </summary>
    public static readonly StyledProperty<double> KeyBadgePaddingXProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(KeyBadgePaddingX), 8);

    /// <summary>
    /// Horizontal padding inside PK/FK/UK badges.
    /// </summary>
    public double KeyBadgePaddingX
    {
        get => GetValue(KeyBadgePaddingXProperty);
        set => SetValue(KeyBadgePaddingXProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="KeyBadgeHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double> KeyBadgeHeightProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(KeyBadgeHeight), 14);

    /// <summary>
    /// Height of compact PK/FK/UK badges.
    /// </summary>
    public double KeyBadgeHeight
    {
        get => GetValue(KeyBadgeHeightProperty);
        set => SetValue(KeyBadgeHeightProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AttributeFontFamily"/> property.
    /// </summary>
    public static readonly StyledProperty<FontFamily> AttributeFontFamilyProperty =
        AvaloniaProperty.Register<ErRenderer, FontFamily>(
            nameof(AttributeFontFamily),
            FontFamily.Parse("Cascadia Mono, Consolas, Menlo, monospace"));

    /// <summary>
    /// Font family used for ER attribute type and name columns.
    /// </summary>
    public FontFamily AttributeFontFamily
    {
        get => GetValue(AttributeFontFamilyProperty);
        set => SetValue(AttributeFontFamilyProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="BoxCornerRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> BoxCornerRadiusProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(BoxCornerRadius), 6);

    /// <summary>
    /// Corner radius for entity boxes and their clipped header area.
    /// </summary>
    public double BoxCornerRadius
    {
        get => GetValue(BoxCornerRadiusProperty);
        set => SetValue(BoxCornerRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CardinalityLineHalfWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CardinalityLineHalfWidthProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(CardinalityLineHalfWidth), 6);

    /// <summary>
    /// Half width of the perpendicular bars used by one-cardinality markers.
    /// </summary>
    public double CardinalityLineHalfWidth
    {
        get => GetValue(CardinalityLineHalfWidthProperty);
        set => SetValue(CardinalityLineHalfWidthProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CrowsFootFanWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CrowsFootFanWidthProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(CrowsFootFanWidth), 7);

    /// <summary>
    /// Fan width of the outer lines in many-cardinality crow's-foot markers.
    /// </summary>
    public double CrowsFootFanWidth
    {
        get => GetValue(CrowsFootFanWidthProperty);
        set => SetValue(CrowsFootFanWidthProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CardinalityTipOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CardinalityTipOffsetProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(CardinalityTipOffset), 4);

    /// <summary>
    /// Distance from relationship endpoint to the first cardinality marker point.
    /// </summary>
    public double CardinalityTipOffset
    {
        get => GetValue(CardinalityTipOffsetProperty);
        set => SetValue(CardinalityTipOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CardinalityBackOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CardinalityBackOffsetProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(CardinalityBackOffset), 16);

    /// <summary>
    /// Distance from relationship endpoint to the rear point of crow's-foot markers.
    /// </summary>
    public double CardinalityBackOffset
    {
        get => GetValue(CardinalityBackOffsetProperty);
        set => SetValue(CardinalityBackOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CardinalityCircleRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CardinalityCircleRadiusProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(CardinalityCircleRadius), 4);

    /// <summary>
    /// Radius of optional zero-cardinality circles.
    /// </summary>
    public double CardinalityCircleRadius
    {
        get => GetValue(CardinalityCircleRadiusProperty);
        set => SetValue(CardinalityCircleRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="ZeroManyCircleOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZeroManyCircleOffsetProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(ZeroManyCircleOffset), 20);

    /// <summary>
    /// Circle offset used for zero-or-many cardinality markers.
    /// </summary>
    public double ZeroManyCircleOffset
    {
        get => GetValue(ZeroManyCircleOffsetProperty);
        set => SetValue(ZeroManyCircleOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="ZeroOneCircleOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZeroOneCircleOffsetProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(ZeroOneCircleOffset), 12);

    /// <summary>
    /// Circle offset used for zero-or-one cardinality markers.
    /// </summary>
    public double ZeroOneCircleOffset
    {
        get => GetValue(ZeroOneCircleOffsetProperty);
        set => SetValue(ZeroOneCircleOffsetProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EdgeLabelPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EdgeLabelPaddingProperty =
        AvaloniaProperty.Register<ErRenderer, double>(nameof(EdgeLabelPadding), 8);

    /// <summary>
    /// Padding around ER relationship label backgrounds.
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
        AvaloniaProperty.Register<ErRenderer, double>(nameof(EdgeLabelCornerRadius), 10);

    /// <summary>
    /// Corner radius for ER relationship label backgrounds.
    /// </summary>
    public double EdgeLabelCornerRadius
    {
        get => GetValue(EdgeLabelCornerRadiusProperty);
        set => SetValue(EdgeLabelCornerRadiusProperty, value);
    }

    private readonly record struct Style(
        double RelationshipCornerRadius,
        double AttributePaddingX,
        double KeyBadgePaddingX,
        double KeyBadgeHeight,
        FontFamily AttributeFontFamily,
        double BoxCornerRadius,
        double CardinalityLineHalfWidth,
        double CrowsFootFanWidth,
        double CardinalityTipOffset,
        double CardinalityBackOffset,
        double CardinalityCircleRadius,
        double ZeroManyCircleOffset,
        double ZeroOneCircleOffset,
        double EdgeLabelPadding,
        double EdgeLabelCornerRadius
    );

    /// <summary>
    /// Resolves native label bounds after Mermaider routes the relationships.
    /// </summary>
    /// <remarks>
    /// Mermaider's SVG path has its own post-layout collision pass. Reserve actual Avalonia label
    /// rectangles here so drawing and the presenter's desired size use identical final positions.
    /// </remarks>
    internal PositionedErDiagram PrepareDiagram(MermaidPresenter presenter, PositionedErDiagram diagram)
    {
        var labels = new List<Rect>();
        var relationships = new List<PositionedErRelationship>(diagram.Relationships.Count);
        foreach (var relationship in diagram.Relationships)
        {
            if (relationship.Label.Length == 0 || relationship.Points.Count < 2)
            {
                relationships.Add(relationship);
                continue;
            }
            var text = MermaidTextRenderer.CreateFormattedText(
                presenter,
                relationship.Label,
                presenter.EdgeLabelFontSize,
                presenter.SecondaryForeground,
                TextAlignment.Center);
            var size = new Size(text.Width + EdgeLabelPadding * 2, text.Height + EdgeLabelPadding);
            var anchor = relationship.LabelPosition is { } position ? new Point(position.X, position.Y) : LongestSegmentMidpoint(relationship.Points);
            var obstacles = diagram.Entities
                .Where(entity => relationship.Entity1 != relationship.Entity2 || entity.Id != relationship.Entity1)
                .Select(entity => new Rect(entity.X, entity.Y, entity.Width, entity.Height))
                .Concat(labels).ToList();
            var bounds = MermaidLabelPlacement.Place(size, anchor, obstacles);
            labels.Add(bounds);
            relationships.Add(relationship with { LabelPosition = new Mermaider.Models.Point(bounds.Center.X, bounds.Center.Y) });
        }

        var offsetX = labels.Count == 0 ? 0 : Math.Max(0, 8 - labels.Min(label => label.Left));
        var offsetY = labels.Count == 0 ? 0 : Math.Max(0, 8 - labels.Min(label => label.Top));
        return diagram with
        {
            Width = Math.Max(diagram.Width, labels.Count == 0 ? 0 : labels.Max(label => label.Right) + 8) + offsetX,
            Height = Math.Max(diagram.Height, labels.Count == 0 ? 0 : labels.Max(label => label.Bottom) + 8) + offsetY,
            Entities = diagram.Entities.Select(entity => entity with { X = entity.X + offsetX, Y = entity.Y + offsetY }).ToList(),
            Relationships = relationships.Select(relationship => relationship with
            {
                Points = relationship.Points.Select(point => new Mermaider.Models.Point(point.X + offsetX, point.Y + offsetY)).ToList(),
                LabelPosition = relationship.LabelPosition is { } labelPosition ?
                    new Mermaider.Models.Point(labelPosition.X + offsetX, labelPosition.Y + offsetY) :
                    null
            }).ToList()
        };
    }

    private static Point LongestSegmentMidpoint(IReadOnlyList<Mermaider.Models.Point> points)
    {
        var result = new Point(points[0].X, points[0].Y);
        var longest = -1.0;
        for (var i = 1; i < points.Count; i++)
        {
            var dx = points[i].X - points[i - 1].X;
            var dy = points[i].Y - points[i - 1].Y;
            var length = dx * dx + dy * dy;
            if (length <= longest)
            {
                continue;
            }
            longest = length;
            result = new Point((points[i].X + points[i - 1].X) / 2, (points[i].Y + points[i - 1].Y) / 2);
        }
        return result;
    }

    /// <summary>
    /// Draws a positioned entity-relationship diagram using this renderer part's styled values.
    /// </summary>
    internal void RenderDiagram(DrawingContext dc, MermaidPresenter presenter, PositionedErDiagram diagram)
    {
        var style = CreateStyleSnapshot();
        foreach (var relationship in diagram.Relationships)
        {
            DrawRelationshipLine(dc, presenter, style, relationship);
        }

        foreach (var entity in diagram.Entities)
        {
            DrawEntityBox(dc, presenter, style, entity);
        }

        foreach (var relationship in diagram.Relationships)
        {
            DrawCardinality(dc, presenter, style, relationship);
        }

        foreach (var relationship in diagram.Relationships)
        {
            DrawRelationshipLabel(dc, presenter, style, relationship);
        }
    }

    private Style CreateStyleSnapshot() =>
        new(
            RelationshipCornerRadius,
            AttributePaddingX,
            KeyBadgePaddingX,
            KeyBadgeHeight,
            AttributeFontFamily,
            BoxCornerRadius,
            CardinalityLineHalfWidth,
            CrowsFootFanWidth,
            CardinalityTipOffset,
            CardinalityBackOffset,
            CardinalityCircleRadius,
            ZeroManyCircleOffset,
            ZeroOneCircleOffset,
            EdgeLabelPadding,
            EdgeLabelCornerRadius);

    /// <inheritdoc/>
    protected override bool ShouldInvalidatePresenterMeasure(AvaloniaProperty property) => property == EdgeLabelPaddingProperty;

    private static void DrawEntityBox(DrawingContext dc, MermaidPresenter presenter, Style style, PositionedErEntity entity)
    {
        var rect = new Rect(entity.X, entity.Y, entity.Width, entity.Height);
        dc.DrawRectangle(presenter.NodeFill, presenter.NodePen, rect, style.BoxCornerRadius, style.BoxCornerRadius);
        using (dc.PushClip(new RoundedRect(rect, style.BoxCornerRadius, style.BoxCornerRadius)))
        {
            var headerRect = new Rect(entity.X, entity.Y, entity.Width, entity.HeaderHeight);
            dc.DrawRectangle(presenter.GroupHeaderFill, presenter.NodePen, headerRect);
        }

        MermaidTextRenderer.DrawInlineText(
            dc,
            presenter,
            entity.Label,
            isMarkdown: false,
            entity.X + entity.Width / 2,
            entity.Y + entity.HeaderHeight / 2,
            presenter.NodeLabelFontSize,
            presenter.Foreground,
            TextAlignment.Center,
            centerVertically: true,
            FontWeight.Bold);

        var attrTop = entity.Y + entity.HeaderHeight;
        if (presenter.NodePen is { } pen)
        {
            dc.DrawLine(pen, new Point(entity.X, attrTop), new Point(entity.X + entity.Width, attrTop));
        }

        if (entity.Attributes.Count == 0)
        {
            DrawEmptyAttributesPlaceholder(dc, presenter, entity, attrTop);
            return;
        }

        for (var i = 0; i < entity.Attributes.Count; i++)
        {
            var rowY = attrTop + i * entity.RowHeight + entity.RowHeight / 2;
            DrawAttribute(dc, presenter, style, entity.Attributes[i], entity.X, rowY, entity.Width);
        }
    }

    private static void DrawEmptyAttributesPlaceholder(
        DrawingContext dc,
        MermaidPresenter presenter,
        PositionedErEntity entity,
        double attrTop)
    {
        var text = MermaidTextRenderer.CreateFormattedText(
            presenter,
            "(no attributes)",
            presenter.MemberFontSize,
            presenter.SecondaryForeground,
            TextAlignment.Center);
        text.SetFontStyle(FontStyle.Italic);
        MermaidTextRenderer.DrawFormattedText(
            dc,
            text,
            entity.X + entity.Width / 2,
            attrTop + entity.RowHeight / 2,
            TextAlignment.Center,
            centerVertically: true);
    }

    private static void DrawAttribute(
        DrawingContext dc,
        MermaidPresenter presenter,
        Style style,
        ErAttributeInfo attribute,
        double boxX,
        double y,
        double boxWidth)
    {
        var keyWidth = 0.0;
        if (attribute.Keys.Count > 0)
        {
            keyWidth = DrawKeyBadge(dc, presenter, style, attribute, boxX + 6, y);
        }

        var typeX = boxX + style.AttributePaddingX + (keyWidth > 0 ? keyWidth + 6 : 0);
        var typeText = MermaidTextRenderer.CreateFormattedText(
            presenter,
            attribute.Type,
            presenter.MemberFontSize,
            presenter.SecondaryForeground,
            TextAlignment.Left,
            FontWeight.Medium);
        typeText.SetFontFamily(style.AttributeFontFamily);
        MermaidTextRenderer.DrawFormattedText(dc, typeText, typeX, y, TextAlignment.Left, centerVertically: true);

        var nameText = MermaidTextRenderer.CreateFormattedText(
            presenter,
            attribute.Name,
            presenter.MemberFontSize,
            presenter.Foreground,
            TextAlignment.Right,
            FontWeight.Medium);
        nameText.SetFontFamily(style.AttributeFontFamily);
        MermaidTextRenderer.DrawFormattedText(
            dc,
            nameText,
            boxX + boxWidth - style.AttributePaddingX,
            y,
            TextAlignment.Right,
            centerVertically: true);
    }

    private static double DrawKeyBadge(
        DrawingContext dc,
        MermaidPresenter presenter,
        Style style,
        ErAttributeInfo attribute,
        double x,
        double centerY)
    {
        var keyText = string.Join(",", attribute.Keys);
        var text = MermaidTextRenderer.CreateFormattedText(
            presenter,
            keyText,
            presenter.KeyBadgeFontSize,
            presenter.SecondaryForeground,
            TextAlignment.Center,
            FontWeight.Bold);
        var width = text.Width + style.KeyBadgePaddingX;
        var rect = new Rect(x, centerY - style.KeyBadgeHeight / 2, width, style.KeyBadgeHeight);

        dc.DrawRectangle(presenter.AccentFill, null, rect, style.KeyBadgeHeight / 2, style.KeyBadgeHeight / 2);
        dc.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
        return width;
    }

    private static void DrawRelationshipLine(DrawingContext dc, MermaidPresenter presenter, Style style, PositionedErRelationship relationship)
    {
        if (relationship.Points.Count < 2)
        {
            return;
        }

        var pen = relationship.Identifying ? presenter.LinePen : presenter.DottedLinePen;
        MermaidDrawingHelpers.DrawRoundedPath(dc, relationship.Points, pen, style.RelationshipCornerRadius);
    }

    private static void DrawRelationshipLabel(DrawingContext dc, MermaidPresenter presenter, Style style, PositionedErRelationship relationship)
    {
        if (relationship.Label.Length == 0 || relationship.Points.Count < 2)
        {
            return;
        }

        var midpoint = relationship.LabelPosition ?? MermaidDrawingHelpers.Midpoint(relationship.Points);
        MermaidTextRenderer.DrawTextWithBackground(
            dc,
            presenter,
            relationship.Label,
            isMarkdown: false,
            midpoint.X,
            midpoint.Y,
            presenter.EdgeLabelFontSize,
            presenter.SecondaryForeground,
            presenter.EdgeLabelBackground,
            presenter.EdgeLabelPen,
            padding: style.EdgeLabelPadding,
            radius: style.EdgeLabelCornerRadius);
    }

    private static void DrawCardinality(DrawingContext dc, MermaidPresenter presenter, Style style, PositionedErRelationship relationship)
    {
        if (relationship.Points.Count < 2)
        {
            return;
        }

        DrawCrowsFoot(dc, presenter, style, relationship.Points[0], relationship.Points[1], relationship.Cardinality1);
        DrawCrowsFoot(dc, presenter, style, relationship.Points[^1], relationship.Points[^2], relationship.Cardinality2);
    }

    private static void DrawCrowsFoot(
        DrawingContext dc,
        MermaidPresenter presenter,
        Style style,
        Mermaider.Models.Point point,
        Mermaider.Models.Point toward,
        ErCardinality cardinality)
    {
        if (presenter.LinePen is not { } pen)
        {
            return;
        }

        var dx = point.X - toward.X;
        var dy = point.Y - toward.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= 0.001)
        {
            return;
        }

        var ux = dx / len;
        var uy = dy / len;
        var px = -uy;
        var py = ux;

        var tipX = point.X - ux * style.CardinalityTipOffset;
        var tipY = point.Y - uy * style.CardinalityTipOffset;
        var backX = point.X - ux * style.CardinalityBackOffset;
        var backY = point.Y - uy * style.CardinalityBackOffset;

        var hasOneLine = cardinality is ErCardinality.One or ErCardinality.ZeroOne;
        var hasCrowsFoot = cardinality is ErCardinality.Many or ErCardinality.ZeroMany;
        var hasCircle = cardinality is ErCardinality.ZeroOne or ErCardinality.ZeroMany;

        if (hasOneLine)
        {
            DrawCardinalityLine(dc, pen, tipX, tipY, px, py, style.CardinalityLineHalfWidth);
            DrawCardinalityLine(
                dc,
                pen,
                tipX - ux * style.CardinalityTipOffset,
                tipY - uy * style.CardinalityTipOffset,
                px,
                py,
                style.CardinalityLineHalfWidth);
        }

        if (hasCrowsFoot)
        {
            dc.DrawLine(pen, new Point(tipX + px * style.CrowsFootFanWidth, tipY + py * style.CrowsFootFanWidth), new Point(backX, backY));
            dc.DrawLine(pen, new Point(tipX, tipY), new Point(backX, backY));
            dc.DrawLine(pen, new Point(tipX - px * style.CrowsFootFanWidth, tipY - py * style.CrowsFootFanWidth), new Point(backX, backY));
        }

        if (hasCircle)
        {
            var circleOffset = hasCrowsFoot ? style.ZeroManyCircleOffset : style.ZeroOneCircleOffset;
            var circleCenter = new Point(point.X - ux * circleOffset, point.Y - uy * circleOffset);
            dc.DrawEllipse(
                presenter.BackgroundBrush ?? presenter.EdgeLabelBackground,
                pen,
                circleCenter,
                style.CardinalityCircleRadius,
                style.CardinalityCircleRadius);
        }
    }

    private static void DrawCardinalityLine(
        DrawingContext dc,
        IPen pen,
        double cx,
        double cy,
        double px,
        double py,
        double halfWidth)
    {
        dc.DrawLine(
            pen,
            new Point(cx + px * halfWidth, cy + py * halfWidth),
            new Point(cx - px * halfWidth, cy - py * halfWidth));
    }
}