using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;
using MermaidRenderOptions = Mermaider.Models.RenderOptions;
using Point = Avalonia.Point;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Avalonia control that parses Mermaid source and renders supported diagrams using native drawing APIs.
/// </summary>
/// <remarks>
/// The presenter owns the styled properties that determine visual appearance. Diagram-specific
/// renderers should read colors, pens, and font sizes from this control instead of hard-coding
/// renderer-local constants.
/// </remarks>
public class MermaidPresenter : Control
{
    /// <summary>
    /// Defines the <see cref="Text"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MermaidPresenter, string?>(nameof(Text));

    /// <summary>
    /// Mermaid diagram definition text.
    /// </summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="RenderOptions"/> property.
    /// </summary>
    public static readonly StyledProperty<MermaidRenderOptions?> RenderOptionsProperty =
        AvaloniaProperty.Register<MermaidPresenter, MermaidRenderOptions?>(nameof(RenderOptions));

    /// <summary>
    /// Mermaider options used by the native parse, layout, and strict-validation pipeline.
    /// </summary>
    /// <remarks>
    /// These options are forwarded to Mermaider where the native path delegates work to Mermaider,
    /// such as graph layout spacing, custom layout providers, strict mode, and rounded-edge routing.
    /// Visual choices such as colors and fonts are intentionally controlled by Avalonia styles instead.
    /// </remarks>
    public MermaidRenderOptions? RenderOptions
    {
        get => GetValue(RenderOptionsProperty);
        set => SetValue(RenderOptionsProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="FontFamily"/> property.
    /// </summary>
    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextBlock.FontFamilyProperty.AddOwner<MermaidPresenter>();

    /// <summary>
    /// Font family for diagram text. Default is system default font.
    /// </summary>
    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="FontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<MermaidPresenter>();

    /// <summary>
    /// Font size for diagram text. Default is 12.0.
    /// </summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="NodeLabelFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> NodeLabelFontSizeProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(NodeLabelFontSize), 16);

    /// <summary>
    /// Font size used for primary node labels.
    /// </summary>
    public double NodeLabelFontSize
    {
        get => GetValue(NodeLabelFontSizeProperty);
        set => SetValue(NodeLabelFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EdgeLabelFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EdgeLabelFontSizeProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(EdgeLabelFontSize), 14);

    /// <summary>
    /// Font size used for labels attached to edges and connectors.
    /// </summary>
    public double EdgeLabelFontSize
    {
        get => GetValue(EdgeLabelFontSizeProperty);
        set => SetValue(EdgeLabelFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="GroupHeaderFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> GroupHeaderFontSizeProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(GroupHeaderFontSize), 14);

    /// <summary>
    /// Font size used for group or subgraph header labels.
    /// </summary>
    public double GroupHeaderFontSize
    {
        get => GetValue(GroupHeaderFontSizeProperty);
        set => SetValue(GroupHeaderFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="MemberFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MemberFontSizeProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(MemberFontSize), 14);

    /// <summary>
    /// Font size used for member-like rows, such as future class diagram fields and methods.
    /// </summary>
    public double MemberFontSize
    {
        get => GetValue(MemberFontSizeProperty);
        set => SetValue(MemberFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AnnotationFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AnnotationFontSizeProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(AnnotationFontSize), 12);

    /// <summary>
    /// Font size used for secondary annotations, notes, and low-emphasis diagram text.
    /// </summary>
    public double AnnotationFontSize
    {
        get => GetValue(AnnotationFontSizeProperty);
        set => SetValue(AnnotationFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="KeyBadgeFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> KeyBadgeFontSizeProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(KeyBadgeFontSize), 12);

    /// <summary>
    /// Font size used for compact badges or key labels in chart-like diagrams.
    /// </summary>
    public double KeyBadgeFontSize
    {
        get => GetValue(KeyBadgeFontSizeProperty);
        set => SetValue(KeyBadgeFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="BackgroundBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(BackgroundBrush), Brushes.Transparent);

    /// <summary>
    /// Background brush for the entire diagram. Default is Transparent.
    /// </summary>
    public IBrush? BackgroundBrush
    {
        get => GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="GroupFill"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> GroupFillProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(GroupFill), new SolidColorBrush(new Color(0xFF, 0xF2, 0xF2, 0xF2)));

    /// <summary>
    /// Fill brush for groups/clusters. Default is a light gray color (#FFF2F2F2).
    /// </summary>
    public IBrush? GroupFill
    {
        get => GetValue(GroupFillProperty);
        set => SetValue(GroupFillProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="GroupHeaderFill"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> GroupHeaderFillProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(GroupHeaderFill), new SolidColorBrush(new Color(0xFF, 0xE2, 0xE2, 0xE2)));

    /// <summary>
    /// Fill brush for group headers. Default is a slightly darker gray color (#FFE2E2E2).
    /// </summary>
    public IBrush? GroupHeaderFill
    {
        get => GetValue(GroupHeaderFillProperty);
        set => SetValue(GroupHeaderFillProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="NodeFill"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> NodeFillProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(NodeFill), new SolidColorBrush(new Color(0xFF, 0xEC, 0xEC, 0xFF)));

    /// <summary>
    /// Fill brush for nodes. Default is a light blue color (#FFECECFF).
    /// </summary>
    public IBrush? NodeFill
    {
        get => GetValue(NodeFillProperty);
        set => SetValue(NodeFillProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AccentFill"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> AccentFillProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(AccentFill), new SolidColorBrush(new Color(0xFF, 0xFF, 0xF5, 0xAD)));

    /// <summary>
    /// Fill brush for accents (e.g. highlighted nodes). Default is a light orange color (#FFFFF5AD).
    /// </summary>
    public IBrush? AccentFill
    {
        get => GetValue(AccentFillProperty);
        set => SetValue(AccentFillProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="Foreground"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<MermaidPresenter>();

    /// <summary>
    /// Foreground brush for diagram text. Default is system default foreground color (usually black).
    /// </summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="SecondaryForeground"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> SecondaryForegroundProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(SecondaryForeground), new SolidColorBrush(new Color(0xFF, 0x33, 0x33, 0x33)));

    /// <summary>
    /// Foreground brush for secondary text (e.g. edge labels). Default is a dark gray color (#FF333333).
    /// </summary>
    public IBrush? SecondaryForeground
    {
        get => GetValue(SecondaryForegroundProperty);
        set => SetValue(SecondaryForegroundProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AccentForeground"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> AccentForegroundProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(AccentForeground), Brushes.Black);

    /// <summary>
    /// Foreground brush for accents (e.g. highlighted nodes). Default is black.
    /// </summary>
    public IBrush? AccentForeground
    {
        get => GetValue(AccentForegroundProperty);
        set => SetValue(AccentForegroundProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="ArrowFill"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ArrowFillProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(ArrowFill), Brushes.White);

    /// <summary>
    /// Fill brush for arrows. Default is white.
    /// </summary>
    public IBrush? ArrowFill
    {
        get => GetValue(ArrowFillProperty);
        set => SetValue(ArrowFillProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EdgeLabelBackground"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> EdgeLabelBackgroundProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(EdgeLabelBackground), Brushes.White);

    /// <summary>
    /// Background brush for edge labels. Default is white.
    /// </summary>
    public IBrush? EdgeLabelBackground
    {
        get => GetValue(EdgeLabelBackgroundProperty);
        set => SetValue(EdgeLabelBackgroundProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="GroupStroke"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> GroupStrokeProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(GroupStroke), new SolidColorBrush(new Color(0xFF, 0xCC, 0xCC, 0xCC)));

    /// <summary>
    /// Stroke brush for groups/clusters. Default is a medium gray color (#FFCCCCCC).
    /// </summary>
    public IBrush? GroupStroke
    {
        get => GetValue(GroupStrokeProperty);
        set => SetValue(GroupStrokeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="GroupStrokeThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<double> GroupStrokeThicknessProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(GroupStrokeThickness), 2);

    /// <summary>
    /// Stroke thickness for groups/clusters. Default is 2.
    /// </summary>
    public double GroupStrokeThickness
    {
        get => GetValue(GroupStrokeThicknessProperty);
        set => SetValue(GroupStrokeThicknessProperty, value);
    }

    /// <summary>
    /// Cached GroupPen based on the current GroupStroke and GroupStrokeThickness properties. This is used to optimize pen creation during rendering.
    /// </summary>
    internal IPen? GroupPen => GetCachedPen(ref _groupPen, GroupStroke, GroupStrokeThickness);

    /// <summary>
    /// Defines the <see cref="NodeStroke"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> NodeStrokeProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(NodeStroke), new SolidColorBrush(new Color(0xFF, 0x93, 0x70, 0xDB)));

    /// <summary>
    /// Stroke brush for nodes. Default is a purple color (#FF9370DB).
    /// </summary>
    public IBrush? NodeStroke
    {
        get => GetValue(NodeStrokeProperty);
        set => SetValue(NodeStrokeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="NodeStrokeThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<double> NodeStrokeThicknessProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(NodeStrokeThickness), 1);

    /// <summary>
    /// Stroke thickness for nodes. Default is 1.
    /// </summary>
    public double NodeStrokeThickness
    {
        get => GetValue(NodeStrokeThicknessProperty);
        set => SetValue(NodeStrokeThicknessProperty, value);
    }

    /// <summary>
    /// Cached NodePen based on the current NodeStroke and NodeStrokeThickness properties. This is used to optimize pen creation during rendering.
    /// </summary>
    internal IPen? NodePen => GetCachedPen(ref _nodePen, NodeStroke, NodeStrokeThickness);

    /// <summary>
    /// Defines the <see cref="AccentStroke"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> AccentStrokeProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(AccentStroke), new SolidColorBrush(new Color(0xFF, 0xCC, 0xCC, 0x00)));

    /// <summary>
    /// Stroke brush for accents (e.g. highlighted nodes). Default is a transparent yellow color (#FFCCCC00).
    /// </summary>
    public IBrush? AccentStroke
    {
        get => GetValue(AccentStrokeProperty);
        set => SetValue(AccentStrokeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="AccentStrokeThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AccentStrokeThicknessProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(AccentStrokeThickness), 1);

    /// <summary>
    /// Stroke thickness for accents (e.g. highlighted nodes). Default is 1.
    /// </summary>
    public double AccentStrokeThickness
    {
        get => GetValue(AccentStrokeThicknessProperty);
        set => SetValue(AccentStrokeThicknessProperty, value);
    }

    /// <summary>
    /// Cached AccentPen based on the current AccentStroke and AccentStrokeThickness properties. This is used to optimize pen creation during rendering.
    /// </summary>
    internal IPen? AccentPen => GetCachedPen(ref _accentPen, AccentStroke, AccentStrokeThickness);

    /// <summary>
    /// Defines the <see cref="LineStroke"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> LineStrokeProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(LineStroke), Brushes.White);

    /// <summary>
    /// Stroke brush for lines (e.g. edges). Default is white.
    /// </summary>
    public IBrush? LineStroke
    {
        get => GetValue(LineStrokeProperty);
        set => SetValue(LineStrokeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LineStrokeThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LineStrokeThicknessProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(LineStrokeThickness), 1.5);

    /// <summary>
    /// Stroke thickness for lines (e.g. edges). Default is 1.5.
    /// </summary>
    public double LineStrokeThickness
    {
        get => GetValue(LineStrokeThicknessProperty);
        set => SetValue(LineStrokeThicknessProperty, value);
    }

    /// <summary>
    /// Cached LinePen based on the current LineStroke and LineStrokeThickness properties. This is used to optimize pen creation during rendering.
    /// </summary>
    internal IPen? LinePen => GetCachedPen(ref _linePen, LineStroke, LineStrokeThickness);

    /// <summary>
    /// Defines the <see cref="ThickLineStroke"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ThickLineStrokeProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(ThickLineStroke), Brushes.White);

    /// <summary>
    /// Stroke brush for thick lines (e.g. bold edges). Default is white.
    /// </summary>
    public IBrush? ThickLineStroke
    {
        get => GetValue(ThickLineStrokeProperty);
        set => SetValue(ThickLineStrokeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="ThickLineStrokeThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ThickLineStrokeThicknessProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(ThickLineStrokeThickness), 2.5);

    /// <summary>
    /// Stroke thickness for thick lines (e.g. bold edges). Default is 2.5.
    /// </summary>
    public double ThickLineStrokeThickness
    {
        get => GetValue(ThickLineStrokeThicknessProperty);
        set => SetValue(ThickLineStrokeThicknessProperty, value);
    }

    /// <summary>
    /// Cached ThickLinePen based on the current ThickLineStroke and ThickLineStrokeThickness properties. This is used to optimize pen creation during rendering.
    /// </summary>
    internal IPen? ThickLinePen => GetCachedPen(ref _thickLinePen, ThickLineStroke, ThickLineStrokeThickness);

    /// <summary>
    /// Defines the <see cref="DottedLineStroke"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> DottedLineStrokeProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(DottedLineStroke), Brushes.White);

    /// <summary>
    /// Stroke brush for dotted lines (e.g. dotted edges). Default is white.
    /// </summary>
    public IBrush? DottedLineStroke
    {
        get => GetValue(DottedLineStrokeProperty);
        set => SetValue(DottedLineStrokeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="DottedLineStrokeThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<double> DottedLineStrokeThicknessProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(DottedLineStrokeThickness), 1.5);

    /// <summary>
    /// Stroke thickness for dotted lines (e.g. dotted edges). Default is 1.5.
    /// </summary>
    public double DottedLineStrokeThickness
    {
        get => GetValue(DottedLineStrokeThicknessProperty);
        set => SetValue(DottedLineStrokeThicknessProperty, value);
    }

    /// <summary>
    /// Cached DottedLinePen based on the current DottedLineStroke and DottedLineStrokeThickness properties. This is used to optimize pen creation during rendering.
    /// </summary>
    internal IPen? DottedLinePen => GetCachedPen(ref _dottedLinePen, DottedLineStroke, DottedLineStrokeThickness, DottedDashStyle);

    /// <summary>
    /// Defines the <see cref="EdgeLabelStroke"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> EdgeLabelStrokeProperty =
        AvaloniaProperty.Register<MermaidPresenter, IBrush?>(nameof(EdgeLabelStroke), new SolidColorBrush(new Color(0xFF, 0xDD, 0xDD, 0xDD)));

    /// <summary>
    /// Stroke brush for edge label borders. Default is a light gray color (#FFDDDDDD).
    /// </summary>
    public IBrush? EdgeLabelStroke
    {
        get => GetValue(EdgeLabelStrokeProperty);
        set => SetValue(EdgeLabelStrokeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="EdgeLabelStrokeThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<double> EdgeLabelStrokeThicknessProperty =
        AvaloniaProperty.Register<MermaidPresenter, double>(nameof(EdgeLabelStrokeThickness), 1);

    /// <summary>
    /// Stroke thickness for edge label borders. Default is 1.
    /// </summary>
    public double EdgeLabelStrokeThickness
    {
        get => GetValue(EdgeLabelStrokeThicknessProperty);
        set => SetValue(EdgeLabelStrokeThicknessProperty, value);
    }

    /// <summary>
    /// Cached EdgeLabelPen based on the current EdgeLabelStroke and EdgeLabelStrokeThickness properties. This is used to optimize pen creation during rendering.
    /// </summary>
    internal IPen? EdgeLabelPen => GetCachedPen(ref _edgeLabelPen, EdgeLabelStroke, EdgeLabelStrokeThickness);

    internal IPen? StateEndPen => GetCachedPen(ref _stateEndPen, Foreground, Math.Max(NodeStrokeThickness * 2, 1));

    private readonly Dictionary<string, IBrush> _brushCache = new();
    private readonly Dictionary<string, IPen> _penCache = new();
    private static readonly DashStyle DottedDashStyle = new([4, 4], 0);

    private IPen? _groupPen;
    private IPen? _nodePen;
    private IPen? _accentPen;
    private IPen? _linePen;
    private IPen? _thickLinePen;
    private IPen? _dottedLinePen;
    private IPen? _edgeLabelPen;
    private IPen? _stateEndPen;

    private MermaidRenderer? _activeRenderer;
    private MermaidDiagramState _state = MermaidDiagramState.Empty;

    static MermaidPresenter()
    {
        AffectsMeasure<MermaidPresenter>(
            TextProperty,
            RenderOptionsProperty,
            FontFamilyProperty,
            FontSizeProperty,
            NodeLabelFontSizeProperty,
            EdgeLabelFontSizeProperty,
            GroupHeaderFontSizeProperty,
            MemberFontSizeProperty,
            AnnotationFontSizeProperty,
            KeyBadgeFontSizeProperty);

        AffectsRender<MermaidPresenter>(
            BackgroundBrushProperty,
            GroupFillProperty,
            GroupHeaderFillProperty,
            NodeFillProperty,
            AccentFillProperty,
            ForegroundProperty,
            SecondaryForegroundProperty,
            AccentForegroundProperty,
            ArrowFillProperty,
            EdgeLabelBackgroundProperty,
            GroupStrokeProperty,
            GroupStrokeThicknessProperty,
            NodeStrokeProperty,
            NodeStrokeThicknessProperty,
            AccentStrokeProperty,
            AccentStrokeThicknessProperty,
            LineStrokeProperty,
            LineStrokeThicknessProperty,
            ThickLineStrokeProperty,
            ThickLineStrokeThicknessProperty,
            DottedLineStrokeProperty,
            DottedLineStrokeThicknessProperty,
            EdgeLabelStrokeProperty,
            EdgeLabelStrokeThicknessProperty,
            FontFamilyProperty,
            FontSizeProperty,
            NodeLabelFontSizeProperty,
            EdgeLabelFontSizeProperty,
            GroupHeaderFontSizeProperty,
            MemberFontSizeProperty,
            AnnotationFontSizeProperty,
            KeyBadgeFontSizeProperty);
    }

    /// <summary>
    /// Returns a cached brush for a Mermaider style color, or a presenter-provided fallback.
    /// </summary>
    /// <remarks>
    /// This method is primarily for renderer code that needs to honor per-node or per-edge style
    /// overrides from the parsed model. Invalid colors and Mermaid's <c>none</c> value intentionally
    /// fall back to the current Avalonia style instead of throwing during rendering.
    /// </remarks>
    public IBrush? GetCachedBrush(string? colorHex, IBrush? fallback)
    {
        var normalized = MermaidStyleValue.NormalizeColor(colorHex);
        if (normalized is null || normalized.Equals("none", StringComparison.OrdinalIgnoreCase)) return fallback;
        if (_brushCache.TryGetValue(normalized, out var brush)) return brush;

        try
        {
            var parsed = new SolidColorBrush(Color.Parse(normalized));
            _brushCache[normalized] = parsed;
            return parsed;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Returns a cached pen for Mermaider style stroke values, preserving fallback dash and cap settings.
    /// </summary>
    /// <remarks>
    /// Width strings are parsed using invariant culture because Mermaid style data is culture-neutral.
    /// If either color or width is unusable, the fallback pen is returned so malformed diagram style
    /// snippets do not break the entire diagram.
    /// </remarks>
    public IPen? GetCachedPen(string? colorHex, string? widthString, IPen? fallback)
    {
        var normalizedColor = MermaidStyleValue.NormalizeColor(colorHex);
        var normalizedWidth = MermaidStyleValue.NormalizeLength(widthString);
        if (normalizedColor is null && normalizedWidth is null) return fallback;
        var key = $"{normalizedColor}_{normalizedWidth}";
        if (_penCache.TryGetValue(key, out var existingPen)) return existingPen;

        var brush = GetCachedBrush(normalizedColor, fallback?.Brush);
        var width = fallback?.Thickness ?? 0d;
        if (double.TryParse(normalizedWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) width = w;
        if (brush is null || width <= 0) return fallback;

        var pen = new Pen(
            brush,
            width,
            fallback?.DashStyle,
            fallback?.LineCap ?? PenLineCap.Flat,
            fallback?.LineJoin ?? PenLineJoin.Miter,
            fallback?.MiterLimit ?? 10);
        _penCache[key] = pen;
        return pen;
    }

    private static IPen? GetCachedPen(ref IPen? cachedPen, IBrush? brush, double thickness, IDashStyle? dashStyle = null)
    {
        if (cachedPen is not null) return cachedPen;
        if (brush is null || thickness <= 0) return null;
        cachedPen = new Pen(brush, thickness, dashStyle);
        return cachedPen;
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        switch (change.Property.Name)
        {
            case nameof(Text):
            case nameof(RenderOptions):
                _state = MermaidDiagramState.Empty;
                break;
            case nameof(Foreground):
                _stateEndPen = null;
                _penCache.Clear();
                break;
            case nameof(GroupStroke):
            case nameof(GroupStrokeThickness):
                _groupPen = null;
                _penCache.Clear();
                break;
            case nameof(NodeStroke):
            case nameof(NodeStrokeThickness):
                _nodePen = null;
                _stateEndPen = null;
                _penCache.Clear();
                break;
            case nameof(AccentStroke):
            case nameof(AccentStrokeThickness):
                _accentPen = null;
                _penCache.Clear();
                break;
            case nameof(LineStroke):
            case nameof(LineStrokeThickness):
                _linePen = null;
                _penCache.Clear();
                break;
            case nameof(ThickLineStroke):
            case nameof(ThickLineStrokeThickness):
                _thickLinePen = null;
                _penCache.Clear();
                break;
            case nameof(DottedLineStroke):
            case nameof(DottedLineStrokeThickness):
                _dottedLinePen = null;
                _penCache.Clear();
                break;
            case nameof(EdgeLabelStroke):
            case nameof(EdgeLabelStrokeThickness):
                _edgeLabelPen = null;
                _penCache.Clear();
                break;
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        _state = BuildState(Text);
        return _state.DesiredSize;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext dc)
    {
        var state = _state;
        if (state == MermaidDiagramState.Empty && Text is { Length: > 0 })
        {
            state = _state = BuildState(Text);
        }

        DrawBackground(dc);

        if (state.Error is { } error)
        {
            DrawError(dc, error);
            return;
        }

        switch (state.Diagram)
        {
            case PositionedSequenceDiagram positionedSequenceDiagram:
            {
                UseRendererPart<SequenceRenderer>().RenderDiagram(dc, this, positionedSequenceDiagram);
                break;
            }
            case PositionedClassDiagram positionedClassDiagram:
            {
                UseRendererPart<ClassRenderer>().RenderDiagram(dc, this, positionedClassDiagram);
                break;
            }
            case PositionedErDiagram positionedErDiagram:
            {
                UseRendererPart<ErRenderer>().RenderDiagram(dc, this, positionedErDiagram);
                break;
            }
            case PieChart pieChart:
            {
                UseRendererPart<PieRenderer>().RenderDiagram(dc, this, pieChart);
                break;
            }
            case QuadrantChart quadrantChart:
            {
                UseRendererPart<QuadrantRenderer>().RenderDiagram(dc, this, quadrantChart);
                break;
            }
            case TimelineDiagram timelineDiagram:
            {
                UseRendererPart<TimelineRenderer>().RenderDiagram(dc, this, timelineDiagram);
                break;
            }
            case GitGraph gitGraph:
            {
                UseRendererPart<GitGraphRenderer>().RenderDiagram(dc, this, gitGraph);
                break;
            }
            case RadarChart radarChart:
            {
                UseRendererPart<RadarRenderer>().RenderDiagram(dc, this, radarChart);
                break;
            }
            case TreemapDiagram treemapDiagram:
            {
                UseRendererPart<TreemapRenderer>().RenderDiagram(dc, this, treemapDiagram);
                break;
            }
            case VennDiagram vennDiagram:
            {
                UseRendererPart<VennRenderer>().RenderDiagram(dc, this, vennDiagram);
                break;
            }
            case PreparedPositionedGraph preparedGraph:
            {
                UseRendererPart<DefaultRenderer>().RenderGraph(dc, this, preparedGraph, RenderOptions);
                break;
            }
            case PositionedGraph positionedGraph:
            {
                UseRendererPart<DefaultRenderer>().RenderGraph(dc, this, PreparedPositionedGraph.Prepare(positionedGraph), RenderOptions);
                break;
            }
        }
    }

    private TRenderer CreateRendererPart<TRenderer>() where TRenderer : MermaidRenderer, new()
    {
        var renderer = new TRenderer();
        renderer.AttachOwner(this);
        return renderer;
    }

    private TRenderer UseRendererPart<TRenderer>() where TRenderer : MermaidRenderer, new()
    {
        if (_activeRenderer is TRenderer activeRenderer)
        {
            return activeRenderer;
        }

        ClearActiveRendererPart();
        var renderer = CreateRendererPart<TRenderer>();
        _activeRenderer = renderer;
        LogicalChildren.Add(renderer);
        return renderer;
    }

    private void ClearActiveRendererPart()
    {
        if (_activeRenderer is null)
        {
            return;
        }

        LogicalChildren.Remove(_activeRenderer);
        _activeRenderer = null;
    }

    private void UseRendererPart(DiagramType diagramType)
    {
        switch (diagramType)
        {
            case DiagramType.Flowchart:
            case DiagramType.State:
                UseRendererPart<DefaultRenderer>();
                break;
            case DiagramType.Sequence:
                UseRendererPart<SequenceRenderer>();
                break;
            case DiagramType.Class:
                UseRendererPart<ClassRenderer>();
                break;
            case DiagramType.Er:
                UseRendererPart<ErRenderer>();
                break;
            case DiagramType.Pie:
                UseRendererPart<PieRenderer>();
                break;
            case DiagramType.Quadrant:
                UseRendererPart<QuadrantRenderer>();
                break;
            case DiagramType.Timeline:
                UseRendererPart<TimelineRenderer>();
                break;
            case DiagramType.GitGraph:
                UseRendererPart<GitGraphRenderer>();
                break;
            case DiagramType.Radar:
                UseRendererPart<RadarRenderer>();
                break;
            case DiagramType.Treemap:
                UseRendererPart<TreemapRenderer>();
                break;
            case DiagramType.Venn:
                UseRendererPart<VennRenderer>();
                break;
            default:
                ClearActiveRendererPart();
                break;
        }
    }

    private MermaidDiagramState BuildState(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ClearActiveRendererPart();
            return MermaidDiagramState.Empty;
        }

        MermaidInput? input = null;
        try
        {
            input = MermaidInputPreprocessor.Process(text);
            if (input.Lines.Length == 0)
            {
                ClearActiveRendererPart();
                return MermaidDiagramState.Empty;
            }

            Size size;
            var options = RenderOptions;
            if (options?.Strict is { } strict)
            {
                var violations = new List<StrictStylingViolation>();
                StrictStylingValidator.Validate(input.Lines, strict, violations);
                if (violations.Count > 0)
                {
                    strict.OnStripped?.Invoke(violations);
                }
            }

            var diagramType = DiagramDetector.Detect(input.CleanedText.AsSpan());
            object diagram = diagramType switch
            {
                DiagramType.Sequence => LayoutSequence(input, out size),
                DiagramType.Class => LayoutClass(input, options, out size),
                DiagramType.Er => LayoutEr(input, options, out size),
                DiagramType.Pie => LayoutPie(input, UseRendererPart<PieRenderer>(), out size),
                DiagramType.Quadrant => LayoutQuadrant(input, UseRendererPart<QuadrantRenderer>(), out size),
                DiagramType.Timeline => LayoutTimeline(input, UseRendererPart<TimelineRenderer>(), out size),
                DiagramType.GitGraph => LayoutGitGraph(input, UseRendererPart<GitGraphRenderer>(), out size),
                DiagramType.Radar => LayoutRadar(input, UseRendererPart<RadarRenderer>(), out size),
                DiagramType.Treemap => LayoutTreemap(input, UseRendererPart<TreemapRenderer>(), out size),
                DiagramType.Venn => LayoutVenn(input, UseRendererPart<VennRenderer>(), out size),
                DiagramType.Flowchart => LayoutFlowchart(input, options, out size),
                DiagramType.State => LayoutState(input, options, out size),
                _ => throw new NotSupportedException($"Diagram type '{diagramType}' is not supported by the native Mermaid presenter yet.")
            };

            UseRendererPart(diagramType);
            return MermaidDiagramState.Success(diagramType, diagram, size, input);
        }
        catch (Exception ex)
        {
            ClearActiveRendererPart();
            return MermaidDiagramState.Failed(ex, MeasureError(ex), input);
        }
    }

    private static PieChart LayoutPie(MermaidInput input, PieRenderer renderer, out Size size)
    {
        var diagram = PieParser.Parse(input.Lines);
        size = renderer.MeasureDiagram(diagram);
        return diagram;
    }

    private static QuadrantChart LayoutQuadrant(MermaidInput input, QuadrantRenderer renderer, out Size size)
    {
        var diagram = QuadrantParser.Parse(input.Lines);
        size = renderer.MeasureDiagram(diagram);
        return diagram;
    }

    private static TimelineDiagram LayoutTimeline(MermaidInput input, TimelineRenderer renderer, out Size size)
    {
        var diagram = TimelineParser.Parse(input.Lines);
        size = renderer.MeasureDiagram(diagram);
        return diagram;
    }

    private static GitGraph LayoutGitGraph(MermaidInput input, GitGraphRenderer renderer, out Size size)
    {
        var diagram = GitGraphParser.Parse(input.Lines);
        size = renderer.MeasureDiagram(diagram);
        return diagram;
    }

    private static RadarChart LayoutRadar(MermaidInput input, RadarRenderer renderer, out Size size)
    {
        var diagram = RadarParser.Parse(input.Lines);
        size = renderer.MeasureDiagram(diagram);
        return diagram;
    }

    private static TreemapDiagram LayoutTreemap(MermaidInput input, TreemapRenderer renderer, out Size size)
    {
        var diagram = TreemapParser.Parse(input.PreserveIndentLines);
        size = renderer.MeasureDiagram(diagram);
        return diagram;
    }

    private static VennDiagram LayoutVenn(MermaidInput input, VennRenderer renderer, out Size size)
    {
        var diagram = VennParser.Parse(input.Lines);
        size = renderer.MeasureDiagram(diagram);
        return diagram;
    }

    private static PositionedSequenceDiagram LayoutSequence(MermaidInput input, out Size size)
    {
        var diagram = SequenceLayout.Layout(SequenceParser.Parse(input.Lines));
        size = new Size(diagram.Width, diagram.Height);
        return diagram;
    }

    private static PositionedClassDiagram LayoutClass(MermaidInput input, MermaidRenderOptions? options, out Size size)
    {
        var provider = options?.LayoutProvider ?? Mermaider.MermaidRenderer.LayoutProvider;
        var diagram = provider.LayoutClass(ClassParser.Parse(input.Lines));
        size = new Size(diagram.Width, diagram.Height);
        return diagram;
    }

    private PositionedErDiagram LayoutEr(MermaidInput input, MermaidRenderOptions? options, out Size size)
    {
        var provider = options?.LayoutProvider ?? Mermaider.MermaidRenderer.LayoutProvider;
        var diagram = UseRendererPart<ErRenderer>().PrepareDiagram(this, provider.LayoutEr(ErParser.Parse(input.Lines)));
        size = new Size(diagram.Width, diagram.Height);
        return diagram;
    }

    private static PreparedPositionedGraph LayoutFlowchart(MermaidInput input, MermaidRenderOptions? options, out Size size)
    {
        var provider = options?.LayoutProvider ?? Mermaider.MermaidRenderer.LayoutProvider;
        var diagram = provider.LayoutFlowchart(FlowchartParser.Parse(input.Lines), options, options?.Strict);
        size = new Size(diagram.Width - diagram.MinX, diagram.Height);
        return PreparedPositionedGraph.Prepare(diagram);
    }

    private static PreparedPositionedGraph LayoutState(MermaidInput input, MermaidRenderOptions? options, out Size size)
    {
        var provider = options?.LayoutProvider ?? Mermaider.MermaidRenderer.LayoutProvider;
        var diagram = provider.LayoutFlowchart(StateParser.Parse(input.Lines), options, options?.Strict);
        size = new Size(diagram.Width - diagram.MinX, diagram.Height);
        return PreparedPositionedGraph.Prepare(diagram);
    }

    private void DrawBackground(DrawingContext dc)
    {
        if (BackgroundBrush is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, Bounds.Width, Bounds.Height));
    }

    private void DrawError(DrawingContext dc, Exception error)
    {
        var text = CreateErrorText(error, Bounds.Width > 16 ? Bounds.Width - 16 : double.PositiveInfinity);
        dc.DrawText(text, new Point(8, 8));
    }

    private Size MeasureError(Exception error)
    {
        var message = $"Failed to render Mermaid diagram: {error.Message}";
        var width = Math.Clamp(message.Length * Math.Max(EdgeLabelFontSize * 0.55, 6) + 16, 240, 520);
        var lines = Math.Max(1, Math.Ceiling((message.Length * Math.Max(EdgeLabelFontSize * 0.55, 6)) / Math.Max(width - 16, 1)));
        var height = (lines * Math.Max(EdgeLabelFontSize * 1.35, 16)) + 16;
        return new Size(width, height);
    }

    private FormattedText CreateErrorText(Exception error, double maxTextWidth)
    {
        return new FormattedText(
            $"Failed to render Mermaid diagram: {error.Message}",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily),
            EdgeLabelFontSize,
            SecondaryForeground ?? Foreground)
        {
            MaxTextWidth = maxTextWidth,
            TextAlignment = TextAlignment.Left
        };
    }
}