using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Mermaider.Models;
using Point = Avalonia.Point;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Native Avalonia renderer for Mermaid git graph diagrams.
/// </summary>
/// <remarks>
/// Mermaider currently simulates git graph lanes inside its SVG renderer rather than exposing a
/// positioned model. This renderer owns the same lightweight simulation and exposes only chart-local
/// spacing and palette tokens as styleable properties.
/// </remarks>
public sealed class GitGraphRenderer : MermaidRenderer
{
    private static readonly IReadOnlyList<Color> DefaultBranchPalette =
    [
        Color.Parse("#4e79a7"), Color.Parse("#f28e2b"), Color.Parse("#e15759"), Color.Parse("#76b7b2"),
        Color.Parse("#59a14f"), Color.Parse("#edc948"), Color.Parse("#b07aa1"), Color.Parse("#ff9da7")
    ];

    // Mermaider's SVG renderer returns a small empty canvas when there are no commits.
    private const double EmptyFallbackWidth = 200;
    private const double EmptyFallbackHeight = 100;

    // Visual compatibility constants from Mermaider's SVG renderer; these do not drive layout.
    private const double CommitStrokeThickness = 2;
    private const double HighlightCornerRadius = 3;
    private const double BranchPillHorizontalPadding = 6;
    private const double BranchPillVerticalPadding = 4;
    private const double TagPillHorizontalPadding = 5;
    private const double TagPillHeight = 16;
    private const double TagPillCornerRadius = 8;
    private const byte SubtleFillAlpha = 0x33;

    /// <summary>
    /// Defines the <see cref="CommitSpacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CommitSpacingProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(CommitSpacing), 60);

    /// <summary>
    /// Minimum horizontal distance between commits; wider labels increase the spacing.
    /// </summary>
    public double CommitSpacing
    {
        get => GetValue(CommitSpacingProperty);
        set => SetValue(CommitSpacingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LaneSpacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LaneSpacingProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(LaneSpacing), 40);

    /// <summary>
    /// Minimum vertical distance between branch lanes; text and tags reserve additional space.
    /// </summary>
    public double LaneSpacing
    {
        get => GetValue(LaneSpacingProperty);
        set => SetValue(LaneSpacingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CommitRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CommitRadiusProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(CommitRadius), 8);

    /// <summary>
    /// Radius of normal commit markers.
    /// </summary>
    public double CommitRadius
    {
        get => GetValue(CommitRadiusProperty);
        set => SetValue(CommitRadiusProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LeftPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LeftPaddingProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(LeftPadding), 100);

    /// <summary>
    /// Minimum left padding before the first commit column; branch labels can widen the gutter.
    /// </summary>
    public double LeftPadding
    {
        get => GetValue(LeftPaddingProperty);
        set => SetValue(LeftPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TopPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TopPaddingProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(TopPadding), 40);

    /// <summary>
    /// Top padding before the first branch lane.
    /// </summary>
    public double TopPadding
    {
        get => GetValue(TopPaddingProperty);
        set => SetValue(TopPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TrailingPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TrailingPaddingProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(TrailingPadding), 60);

    /// <summary>
    /// Extra horizontal space after the final simulated commit.
    /// </summary>
    public double TrailingPadding
    {
        get => GetValue(TrailingPaddingProperty);
        set => SetValue(TrailingPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="BottomPadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> BottomPaddingProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(BottomPadding), 60);

    /// <summary>
    /// Extra vertical space after the final branch lane.
    /// </summary>
    public double BottomPadding
    {
        get => GetValue(BottomPaddingProperty);
        set => SetValue(BottomPaddingProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="CommitLabelOffsetY"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CommitLabelOffsetYProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(CommitLabelOffsetY), 22);

    /// <summary>
    /// Vertical offset from a commit marker center to its label.
    /// </summary>
    public double CommitLabelOffsetY
    {
        get => GetValue(CommitLabelOffsetYProperty);
        set => SetValue(CommitLabelOffsetYProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TagOffsetY"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TagOffsetYProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(TagOffsetY), -16);

    /// <summary>
    /// Vertical offset from a commit marker center to its tag pill.
    /// </summary>
    public double TagOffsetY
    {
        get => GetValue(TagOffsetYProperty);
        set => SetValue(TagOffsetYProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LinkStrokeThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LinkStrokeThicknessProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(LinkStrokeThickness), 3);

    /// <summary>
    /// Stroke thickness used for commit links.
    /// </summary>
    public double LinkStrokeThickness
    {
        get => GetValue(LinkStrokeThicknessProperty);
        set => SetValue(LinkStrokeThicknessProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="LabelFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LabelFontSizeProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(LabelFontSize), 14);

    /// <summary>
    /// Font size used for commit labels.
    /// </summary>
    public double LabelFontSize
    {
        get => GetValue(LabelFontSizeProperty);
        set => SetValue(LabelFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="TagFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> TagFontSizeProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(TagFontSize), 14);

    /// <summary>
    /// Font size used inside commit tag pills.
    /// </summary>
    public double TagFontSize
    {
        get => GetValue(TagFontSizeProperty);
        set => SetValue(TagFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="BranchFontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> BranchFontSizeProperty =
        AvaloniaProperty.Register<GitGraphRenderer, double>(nameof(BranchFontSize), 14);

    /// <summary>
    /// Font size used for branch lane labels.
    /// </summary>
    public double BranchFontSize
    {
        get => GetValue(BranchFontSizeProperty);
        set => SetValue(BranchFontSizeProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="BranchPalette"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<Color>> BranchPaletteProperty =
        AvaloniaProperty.Register<GitGraphRenderer, IReadOnlyList<Color>>(nameof(BranchPalette), DefaultBranchPalette);

    /// <summary>
    /// Repeating palette used for branch lanes, commit markers, and links.
    /// </summary>
    public IReadOnlyList<Color> BranchPalette
    {
        get => GetValue(BranchPaletteProperty);
        set => SetValue(BranchPaletteProperty, value);
    }

    private readonly record struct Style(
        double CommitSpacing,
        double LaneSpacing,
        double CommitRadius,
        double LeftPadding,
        double TopPadding,
        double TrailingPadding,
        double BottomPadding,
        double CommitLabelOffsetY,
        double TagOffsetY,
        double LinkStrokeThickness,
        double LabelFontSize,
        double TagFontSize,
        double BranchFontSize,
        IReadOnlyList<Color> BranchPalette
    );

    /// <summary>
    /// Calculates the desired size for the graph using this renderer part's current styled values.
    /// </summary>
    public Size MeasureDiagram(GitGraph graph)
    {
        var style = CreateStyleSnapshot();
        var simulation = Simulate(graph, style);
        return Measure(simulation, FitLabelSpacing(Owner, simulation, style));
    }

    /// <summary>
    /// Draws a git graph using this renderer part's current styled values.
    /// </summary>
    public void RenderDiagram(DrawingContext dc, MermaidPresenter presenter, GitGraph graph)
    {
        var style = CreateStyleSnapshot();
        var simulation = Simulate(graph, style);
        style = FitLabelSpacing(presenter, simulation, style);
        if (simulation.Commits.Count == 0)
        {
            return;
        }

        foreach (var branch in simulation.Branches)
        {
            DrawBranchLabel(dc, presenter, style, branch);
        }

        foreach (var link in simulation.Links)
        {
            DrawLink(dc, style, link, simulation);
        }

        foreach (var commit in simulation.Commits)
        {
            DrawCommit(dc, presenter, style, commit);
        }
    }

    /// <inheritdoc/>
    protected override bool ShouldInvalidatePresenterMeasure(AvaloniaProperty property)
    {
        return property.Name is nameof(CommitSpacing) or nameof(LaneSpacing) or nameof(CommitRadius) or
            nameof(LeftPadding) or nameof(TopPadding) or nameof(TrailingPadding) or nameof(BottomPadding) or
            nameof(CommitLabelOffsetY) or nameof(TagOffsetY) or nameof(LinkStrokeThickness) or
            nameof(LabelFontSize) or nameof(TagFontSize) or nameof(BranchFontSize);
    }

    // Commit spacing and branch-label gutters are minimums, not fixed text allocations.
    // Use the same measured dimensions in MeasureDiagram and RenderDiagram.
    private static Style FitLabelSpacing(MermaidPresenter? presenter, SimulationResult simulation, Style style)
    {
        if (presenter is null || simulation.Commits.Count == 0)
        {
            return style;
        }
        var commitWidth = 0.0;
        var branchWidth = 0.0;
        var labelHeight = 0.0;
        foreach (var commit in simulation.Commits)
        {
            var label = MermaidTextRenderer.CreateFormattedText(
                presenter,
                commit.Label ?? string.Empty,
                style.LabelFontSize,
                presenter.Foreground,
                TextAlignment.Center);
            var tag = MermaidTextRenderer.CreateFormattedText(
                presenter,
                commit.Tag ?? string.Empty,
                style.TagFontSize,
                presenter.Foreground,
                TextAlignment.Center,
                FontWeight.Medium);
            commitWidth = Math.Max(commitWidth, Math.Max(label.Width, tag.Width + TagPillHorizontalPadding * 2));
            labelHeight = Math.Max(labelHeight, label.Height);
        }
        foreach (var branch in simulation.Branches)
        {
            var text = MermaidTextRenderer.CreateFormattedText(
                presenter,
                branch.Name,
                style.BranchFontSize,
                presenter.Foreground,
                TextAlignment.Left,
                FontWeight.SemiBold);
            branchWidth = Math.Max(branchWidth, text.Width + BranchPillHorizontalPadding * 2);
        }
        return style with
        {
            CommitSpacing = Math.Max(style.CommitSpacing, commitWidth + 20),
            LeftPadding = Math.Max(style.LeftPadding, branchWidth + commitWidth / 2 + 20),
            LaneSpacing = Math.Max(style.LaneSpacing, style.CommitLabelOffsetY - style.TagOffsetY + labelHeight + 12)
        };
    }

    private static Size Measure(SimulationResult simulation, Style style)
    {
        if (simulation.Commits.Count == 0)
        {
            return new Size(EmptyFallbackWidth, EmptyFallbackHeight);
        }

        var maxLane = 0;
        foreach (var commit in simulation.Commits)
        {
            maxLane = Math.Max(maxLane, commit.Lane);
        }

        return new Size(
            style.LeftPadding + simulation.Commits.Count * style.CommitSpacing + style.TrailingPadding,
            style.TopPadding + (maxLane + 1) * style.LaneSpacing + style.BottomPadding);
    }

    private Style CreateStyleSnapshot() =>
        new(
            CommitSpacing,
            LaneSpacing,
            CommitRadius,
            LeftPadding,
            TopPadding,
            TrailingPadding,
            BottomPadding,
            CommitLabelOffsetY,
            TagOffsetY,
            LinkStrokeThickness,
            LabelFontSize,
            TagFontSize,
            BranchFontSize,
            BranchPalette);

    private static void DrawBranchLabel(DrawingContext dc, MermaidPresenter presenter, Style style, BranchInfo branch)
    {
        var y = style.TopPadding + branch.Lane * style.LaneSpacing;
        var brush = new SolidColorBrush(branch.Color);
        var text = MermaidTextRenderer.CreateFormattedText(
            presenter,
            branch.Name,
            style.BranchFontSize,
            brush,
            TextAlignment.Left,
            FontWeight.SemiBold);
        var pillHeight = text.Height + BranchPillVerticalPadding * 2;
        var pillWidth = text.Width + BranchPillHorizontalPadding * 2;

        dc.DrawRectangle(
            new SolidColorBrush(WithAlpha(branch.Color, SubtleFillAlpha)),
            null,
            new Rect(6, y - pillHeight / 2, pillWidth, pillHeight),
            pillHeight / 2,
            pillHeight / 2);
        dc.DrawText(text, new Point(6 + BranchPillHorizontalPadding, y - text.Height / 2));
    }

    private static void DrawLink(DrawingContext dc, Style style, CommitLink link, SimulationResult simulation)
    {
        var from = simulation.Commits[link.FromIndex];
        var to = simulation.Commits[link.ToIndex];
        var x1 = style.LeftPadding + from.Position * style.CommitSpacing;
        var y1 = style.TopPadding + from.Lane * style.LaneSpacing;
        var x2 = style.LeftPadding + to.Position * style.CommitSpacing;
        var y2 = style.TopPadding + to.Lane * style.LaneSpacing;
        var pen = new Pen(new SolidColorBrush(link.Color), style.LinkStrokeThickness);

        if (Math.Abs(y1 - y2) < 0.01)
        {
            dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var midX = (x1 + x2) / 2;
            ctx.BeginFigure(new Point(x1, y1), isFilled: false);
            ctx.CubicBezierTo(new Point(midX, y1), new Point(midX, y2), new Point(x2, y2));
        }

        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawCommit(DrawingContext dc, MermaidPresenter presenter, Style style, CommitInfo commit)
    {
        var cx = style.LeftPadding + commit.Position * style.CommitSpacing;
        var cy = style.TopPadding + commit.Lane * style.LaneSpacing;
        var brush = new SolidColorBrush(commit.Color);
        var bgPen = new Pen(presenter.BackgroundBrush ?? Brushes.Transparent, CommitStrokeThickness);

        switch (commit.Type)
        {
            case GitCommitType.Highlight:
                dc.DrawRectangle(
                    brush,
                    bgPen,
                    new Rect(cx - style.CommitRadius, cy - style.CommitRadius, style.CommitRadius * 2, style.CommitRadius * 2),
                    HighlightCornerRadius,
                    HighlightCornerRadius);
                break;
            case GitCommitType.Reverse:
                dc.DrawEllipse(
                    presenter.BackgroundBrush ?? Brushes.Transparent,
                    new Pen(brush, CommitStrokeThickness),
                    new Point(cx, cy),
                    style.CommitRadius,
                    style.CommitRadius);
                dc.DrawLine(new Pen(brush, CommitStrokeThickness), new Point(cx - 5, cy - 5), new Point(cx + 5, cy + 5));
                dc.DrawLine(new Pen(brush, CommitStrokeThickness), new Point(cx + 5, cy - 5), new Point(cx - 5, cy + 5));
                break;
            default:
                var radius = commit.IsMerge ? style.CommitRadius + 2 : style.CommitRadius;
                dc.DrawEllipse(brush, bgPen, new Point(cx, cy), radius, radius);
                if (commit.IsMerge)
                {
                    dc.DrawEllipse(brush, null, new Point(cx, cy), Math.Max(0, style.CommitRadius - 2), Math.Max(0, style.CommitRadius - 2));
                }

                break;
        }

        if (commit.Label is { Length: > 0 })
        {
            MermaidTextRenderer.DrawText(
                dc,
                presenter,
                commit.Label,
                cx,
                cy + style.CommitLabelOffsetY,
                style.LabelFontSize,
                presenter.SecondaryForeground,
                TextAlignment.Center,
                centerVertically: true);
        }

        if (commit.Tag is { Length: > 0 })
        {
            DrawTag(dc, presenter, style, commit, cx, cy);
        }
    }

    private static void DrawTag(DrawingContext dc, MermaidPresenter presenter, Style style, CommitInfo commit, double cx, double cy)
    {
        var brush = new SolidColorBrush(commit.Color);
        var text = MermaidTextRenderer.CreateFormattedText(
            presenter,
            commit.Tag!,
            style.TagFontSize,
            brush,
            TextAlignment.Center,
            FontWeight.Medium);
        var tagWidth = text.Width + TagPillHorizontalPadding * 2;
        var tagCenterY = cy + style.TagOffsetY;

        dc.DrawRectangle(
            new SolidColorBrush(WithAlpha(commit.Color, SubtleFillAlpha)),
            null,
            new Rect(cx - tagWidth / 2, tagCenterY - TagPillHeight / 2, tagWidth, TagPillHeight),
            TagPillCornerRadius,
            TagPillCornerRadius);
        dc.DrawText(text, new Point(cx - text.Width / 2, tagCenterY - text.Height / 2));
    }

    private static SimulationResult Simulate(GitGraph graph, Style style)
    {
        var commits = new List<CommitInfo>();
        var links = new List<CommitLink>();
        var branches = new Dictionary<string, int>();
        var branchColors = new Dictionary<string, Color>();
        var branchHeads = new Dictionary<string, int>();
        var branchList = new List<BranchInfo>();
        var nextLane = 0;
        var position = 0;
        var currentBranch = "main";
        var mainColor = GetPaletteColor(style.BranchPalette, 0);

        branches["main"] = nextLane++;
        branchColors["main"] = mainColor;
        branchList.Add(new BranchInfo("main", 0, mainColor));

        var commitCounter = 0;

        foreach (var action in graph.Actions)
        {
            switch (action)
            {
                case GitBranchAction branch:
                    if (!branches.ContainsKey(branch.Name))
                    {
                        var lane = nextLane++;
                        var color = GetPaletteColor(style.BranchPalette, lane);
                        branches[branch.Name] = lane;
                        branchColors[branch.Name] = color;
                        branchList.Add(new BranchInfo(branch.Name, lane, color));
                        if (branchHeads.TryGetValue(currentBranch, out var forkIndex))
                        {
                            branchHeads[branch.Name] = forkIndex;
                        }
                    }

                    currentBranch = branch.Name;
                    break;

                case GitCheckoutAction checkout:
                    currentBranch = checkout.Name;
                    break;

                case GitCommitAction commit:
                {
                    var lane = branches.GetValueOrDefault(currentBranch, 0);
                    var color = branchColors.GetValueOrDefault(currentBranch, mainColor);
                    var label = commit.Id ?? commitCounter.ToString(CultureInfo.InvariantCulture);
                    commitCounter++;

                    var index = commits.Count;
                    commits.Add(new CommitInfo(position, lane, color, label, commit.Tag, commit.Type, false));
                    if (branchHeads.TryGetValue(currentBranch, out var previousIndex))
                    {
                        links.Add(new CommitLink(previousIndex, index, color));
                    }

                    branchHeads[currentBranch] = index;
                    position++;
                    break;
                }

                case GitMergeAction merge:
                {
                    var lane = branches.GetValueOrDefault(currentBranch, 0);
                    var color = branchColors.GetValueOrDefault(currentBranch, mainColor);
                    commitCounter++;

                    var index = commits.Count;
                    commits.Add(new CommitInfo(position, lane, color, merge.Id, merge.Tag, merge.Type, true));
                    if (branchHeads.TryGetValue(currentBranch, out var previousIndex))
                    {
                        links.Add(new CommitLink(previousIndex, index, color));
                    }

                    if (branchHeads.TryGetValue(merge.Name, out var mergeFromIndex))
                    {
                        links.Add(new CommitLink(mergeFromIndex, index, branchColors.GetValueOrDefault(merge.Name, color)));
                    }

                    branchHeads[currentBranch] = index;
                    position++;
                    break;
                }

                case GitCherryPickAction cherryPick:
                {
                    var lane = branches.GetValueOrDefault(currentBranch, 0);
                    var color = branchColors.GetValueOrDefault(currentBranch, mainColor);
                    commitCounter++;

                    var index = commits.Count;
                    commits.Add(new CommitInfo(position, lane, color, cherryPick.Id, null, GitCommitType.Normal, false));
                    if (branchHeads.TryGetValue(currentBranch, out var previousIndex))
                    {
                        links.Add(new CommitLink(previousIndex, index, color));
                    }

                    branchHeads[currentBranch] = index;
                    position++;
                    break;
                }
            }
        }

        return new SimulationResult(commits, links, branchList);
    }

    private static Color GetPaletteColor(IReadOnlyList<Color> palette, int index) =>
        palette.Count > 0 ? palette[index % palette.Count] : DefaultBranchPalette[index % DefaultBranchPalette.Count];

    private static Color WithAlpha(Color color, byte alpha) => new(alpha, color.R, color.G, color.B);

    private sealed record CommitInfo(int Position, int Lane, Color Color, string? Label, string? Tag, GitCommitType Type, bool IsMerge);

    private sealed record CommitLink(int FromIndex, int ToIndex, Color Color);

    private sealed record BranchInfo(string Name, int Lane, Color Color);

    private sealed record SimulationResult(IReadOnlyList<CommitInfo> Commits, IReadOnlyList<CommitLink> Links, IReadOnlyList<BranchInfo> Branches);
}