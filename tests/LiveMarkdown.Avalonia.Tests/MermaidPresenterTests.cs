using Avalonia;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Markdig;
using Mermaider;
using Mermaider.Layout;
using Mermaider.Models;
using NUnit.Framework;
using MermaidRenderOptions = Mermaider.Models.RenderOptions;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
public class MermaidPresenterTests
{
    [Test]
    public void MermaidBlockParser_AcceptsMermaidAsFirstInfoToken()
    {
        var document = Markdown.Parse(
            """
            ```mermaid interactive
            graph TD
                A --> B
            ```
            """,
            new MarkdownPipelineBuilder().UseMermaid().Build());

        Assert.That(document[0], Is.TypeOf<MermaidCodeBlock>());
    }

    [Test]
    public void MermaidBlockParser_RejectsInfoTokenThatOnlyEndsWithMermaid()
    {
        var document = Markdown.Parse(
            """
            ```notmermaid
            graph TD
                A --> B
            ```
            """,
            new MarkdownPipelineBuilder().UseMermaid().Build());

        Assert.That(document[0], Is.Not.TypeOf<MermaidCodeBlock>());
    }

    [Test]
    public void MermaidBlockParser_AcceptsOpenMermaidFence()
    {
        var document = Markdown.Parse(
            """
            ```mermaid
            graph TD
                A --> B
            """,
            new MarkdownPipelineBuilder().UseMermaid().Build());

        Assert.That(document[0], Is.TypeOf<MermaidCodeBlock>());
        Assert.That(((MermaidCodeBlock)document[0]).IsOpen, Is.True);
    }

    [Test]
    public void MermaidInputPreprocessor_RemovesInitAndAccessibilityLines()
    {
        var input = MermaidInputPreprocessor.Process(
            """
            ---
            title: Frontmatter Title
            ---
            %%{init: {"theme": "github-dark", "title": "Init Title"}}%%
            graph TD
            accTitle: Accessible title
            accDescr: Accessible description
                A --> B
            """);

        Assert.That(input.Metadata.Title, Is.EqualTo("Init Title"));
        Assert.That(input.Metadata.Theme, Is.EqualTo("github-dark"));
        Assert.That(input.Accessibility.Title, Is.EqualTo("Accessible title"));
        Assert.That(input.Accessibility.Description, Is.EqualTo("Accessible description"));
        Assert.That(input.Lines, Does.Not.Contain("accTitle: Accessible title"));
        Assert.That(input.Lines, Does.Not.Contain("accDescr: Accessible description"));
        Assert.That(input.Lines[0], Is.EqualTo("graph TD"));
    }

    [Test]
    public void Measure_ClearsPreviousDiagramForEmptyText()
    {
        var presenter = new MermaidPresenter
        {
            Text = """
                   graph TD
                       A --> B
                   """
        };

        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(presenter.DesiredSize.Width, Is.GreaterThan(0));
        Assert.That(presenter.DesiredSize.Height, Is.GreaterThan(0));

        presenter.Text = string.Empty;
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.That(presenter.DesiredSize, Is.EqualTo(default(Size)));
    }

    [Test]
    public void Measure_InvalidDiagramDoesNotThrow()
    {
        var presenter = new MermaidPresenter
        {
            Text = """
                   graph TD
                       A -->
                   """
        };

        Assert.DoesNotThrow(() => presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)));
    }

    [Test]
    public void MermaidStyleValue_NormalizesCssLikeColorAndLengthValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MermaidStyleValue.NormalizeColor("#123;"), Is.EqualTo("#112233"));
            Assert.That(MermaidStyleValue.NormalizeColor("#123"), Is.EqualTo("#112233"));
            Assert.That(MermaidStyleValue.NormalizeColor("#112233;"), Is.EqualTo("#112233"));
            Assert.That(MermaidStyleValue.NormalizeColor(" none ; "), Is.EqualTo("none"));
            Assert.That(MermaidStyleValue.NormalizeColor("#abcd"), Is.EqualTo("#ddaabbcc"));
            Assert.That(MermaidStyleValue.NormalizeLength("2px;"), Is.EqualTo("2"));
            Assert.That(MermaidStyleValue.NormalizeLength("1.5;"), Is.EqualTo("1.5"));
        });
    }

    [Test]
    public void GetCachedBrush_UsesNormalizedMermaidStyleColor()
    {
        var presenter = new MermaidPresenter();

        var brush = presenter.GetCachedBrush("#123;", Brushes.White);

        Assert.That(brush, Is.TypeOf<SolidColorBrush>());
        Assert.That(((SolidColorBrush)brush!).Color, Is.EqualTo(Color.Parse("#112233")));
        Assert.That(presenter.GetCachedBrush("definitely-not-a-color;", Brushes.White), Is.SameAs(Brushes.White));
    }

    [Test]
    public void GetCachedPen_UsesNormalizedStrokeWidth()
    {
        var presenter = new MermaidPresenter();
        var fallback = new Pen(Brushes.White, 1);

        var pen = presenter.GetCachedPen("#123;", "2px;", fallback);

        Assert.That(pen, Is.Not.Null);
        Assert.That(pen!.Thickness, Is.EqualTo(2));
        Assert.That(((SolidColorBrush)pen.Brush!).Color, Is.EqualTo(Color.Parse("#112233")));
    }

    [Test]
    public void MermaidPresenter_AttachesOnlyActiveRendererPartAsLogicalChild()
    {
        var presenter = new MermaidPresenter();

        Assert.That(GetActiveRenderers(presenter), Is.Empty);

        presenter.Text = """
                         flowchart LR
                             A --> B
                         """;
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var flowchartRenderers = GetActiveRenderers(presenter);
        Assert.That(flowchartRenderers, Has.Length.EqualTo(1));
        Assert.That(flowchartRenderers[0], Is.TypeOf<DefaultRenderer>());

        presenter.Text = """
                         classDiagram
                             A <|-- B
        """;
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var classRenderers = GetActiveRenderers(presenter);
        Assert.That(classRenderers, Has.Length.EqualTo(1));
        Assert.That(classRenderers[0], Is.TypeOf<ClassRenderer>());
        Assert.That(classRenderers[0], Is.Not.SameAs(flowchartRenderers[0]));

        presenter.Text = """
                         pie
                             "A" : 1
                         """;
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(GetActiveRenderers(presenter).Single(), Is.TypeOf<PieRenderer>());

        presenter.Text = """
                         quadrantChart
                             A: [0.5, 0.5]
                         """;
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(GetActiveRenderers(presenter).Single(), Is.TypeOf<QuadrantRenderer>());

        presenter.Text = """
                         timeline
                             2024 : Built
                         """;
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(GetActiveRenderers(presenter).Single(), Is.TypeOf<TimelineRenderer>());

        presenter.Text = """
                         gitGraph
                             commit id: "init"
                         """;
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(GetActiveRenderers(presenter).Single(), Is.TypeOf<GitGraphRenderer>());

        presenter.Text = """
                         radar-beta
                             axis a["A"], b["B"]
                             curve current["Current"]{1, 2}
                         """;
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(GetActiveRenderers(presenter).Single(), Is.TypeOf<RadarRenderer>());

        presenter.Text = """
                         treemap-beta
                           "Root"
                             "A": 1
                         """;
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(GetActiveRenderers(presenter).Single(), Is.TypeOf<TreemapRenderer>());

        presenter.Text = """
                         venn-beta
                             set A["A"]: 1
                             set B["B"]: 1
                             union A, B["Both"]
                         """;
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(GetActiveRenderers(presenter).Single(), Is.TypeOf<VennRenderer>());

        presenter.Text = string.Empty;
        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.That(GetActiveRenderers(presenter), Is.Empty);
    }

    [Test]
    public void RenderOptions_ChangingFlowchartSpacingInvalidatesLayout()
    {
        var presenter = new MermaidPresenter
        {
            Text = """
                   flowchart LR
                       A --> B
                   """,
            RenderOptions = new MermaidRenderOptions
            {
                Padding = 16,
                NodeSpacing = 16,
                LayerSpacing = 16
            }
        };

        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var compactSize = presenter.DesiredSize;

        presenter.RenderOptions = new MermaidRenderOptions
        {
            Padding = 120,
            NodeSpacing = 96,
            LayerSpacing = 96
        };

        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.That(presenter.DesiredSize.Width, Is.GreaterThan(compactSize.Width));
        Assert.That(presenter.DesiredSize.Height, Is.GreaterThan(compactSize.Height));
    }

    [Test]
    public void RenderOptions_LayoutProviderIsUsedForFlowchartClassAndEr()
    {
        var fallback = Mermaider.MermaidRenderer.LayoutProvider;

        var flowchartProvider = new CountingLayoutProvider(fallback);
        AssertMermaidMeasures(
            """
            flowchart LR
                A --> B
            """,
            new MermaidRenderOptions { LayoutProvider = flowchartProvider });

        var classProvider = new CountingLayoutProvider(fallback);
        AssertMermaidMeasures(
            """
            classDiagram
                A <|-- B
            """,
            new MermaidRenderOptions { LayoutProvider = classProvider });

        var erProvider = new CountingLayoutProvider(fallback);
        AssertMermaidMeasures(
            """
            erDiagram
                CUSTOMER ||--o{ ORDER : places
            """,
            new MermaidRenderOptions { LayoutProvider = erProvider });

        Assert.Multiple(() =>
        {
            Assert.That(flowchartProvider.FlowchartCalls, Is.EqualTo(1));
            Assert.That(classProvider.ClassCalls, Is.EqualTo(1));
            Assert.That(erProvider.ErCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void RenderOptions_StrictModeRejectsClassDefBeforeLayout()
    {
        var provider = new CountingLayoutProvider(Mermaider.MermaidRenderer.LayoutProvider);
        var presenter = new MermaidPresenter
        {
            Text = """
                   flowchart LR
                       A:::accent --> B
                       classDef accent fill:#e8f3ff,stroke:#2f80ed,color:#123;
                   """,
            RenderOptions = new MermaidRenderOptions
            {
                LayoutProvider = provider,
                Strict = new StrictStylingOptions { Mode = StrictStylingMode.Block }
            }
        };

        Assert.DoesNotThrow(() => presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)));
        Assert.That(provider.FlowchartCalls, Is.EqualTo(0));
        Assert.That(presenter.DesiredSize.Width, Is.GreaterThan(0));
    }

    [Test]
    public void DefaultRenderer_RoundedEdgesOptionControlsDefaultEdgeRadius()
    {
        var renderer = new DefaultRenderer();

        Assert.That(renderer.GetEffectiveEdgeCornerRadius(new MermaidRenderOptions { RoundedEdges = false }), Is.EqualTo(0));

        renderer.EdgeCornerRadius = 9;

        Assert.That(renderer.GetEffectiveEdgeCornerRadius(new MermaidRenderOptions { RoundedEdges = false }), Is.EqualTo(9));
    }

    [Test]
    public void MermaidInlineTextParser_MarkdownParsesInlineStyles()
    {
        var layout = MermaidInlineTextParser.ParseMarkdown("***both*** `code`\nnext");

        Assert.That(layout.Text, Is.EqualTo("both code\nnext"));
        Assert.That(layout.Spans, Has.One.Matches<MermaidTextSpan>(
            span => span.Start == 0 &&
                    span.Length == 4 &&
                    span.Style.HasFlag(MermaidTextStyle.Bold) &&
                    span.Style.HasFlag(MermaidTextStyle.Italic)));
        Assert.That(layout.Spans, Has.One.Matches<MermaidTextSpan>(
            span => span.Start == 5 &&
                    span.Length == 4 &&
                    span.Style.HasFlag(MermaidTextStyle.Code)));
    }

    [Test]
    public void MermaidInlineTextParser_HtmlLikeParsesSupportedTags()
    {
        var layout = MermaidInlineTextParser.ParseMermaiderHtmlLike(
            "<strong>bold</strong> <em>it</em> <u>u</u> <del>s</del><br>next");

        Assert.That(layout.Text, Is.EqualTo("bold it u s\nnext"));
        Assert.That(layout.Spans, Has.One.Matches<MermaidTextSpan>(
            span => span.Start == 0 && span.Length == 4 && span.Style == MermaidTextStyle.Bold));
        Assert.That(layout.Spans, Has.One.Matches<MermaidTextSpan>(
            span => span.Start == 5 && span.Length == 2 && span.Style == MermaidTextStyle.Italic));
        Assert.That(layout.Spans, Has.One.Matches<MermaidTextSpan>(
            span => span.Start == 8 && span.Length == 1 && span.Style == MermaidTextStyle.Underline));
        Assert.That(layout.Spans, Has.One.Matches<MermaidTextSpan>(
            span => span.Start == 10 && span.Length == 1 && span.Style == MermaidTextStyle.Strikethrough));
    }

    [Test]
    public void MermaidInlineTextParser_HtmlLikePreservesUnknownTags()
    {
        var layout = MermaidInlineTextParser.ParseMermaiderHtmlLike("<mark>x</mark>");

        Assert.That(layout.Text, Is.EqualTo("<mark>x</mark>"));
        Assert.That(layout.Spans, Is.Empty);
    }

    [Test]
    public void MermaidInlineTextParser_HtmlLikeDecodesEntitiesWithoutPromotingEscapedTags()
    {
        var layout = MermaidInlineTextParser.ParseMermaiderHtmlLike("&amp; &lt;b&gt; <b>x &amp; y</b>");

        Assert.That(layout.Text, Is.EqualTo("& <b> x & y"));
        Assert.That(layout.Spans, Has.One.Matches<MermaidTextSpan>(
            span => span.Start == 6 && span.Length == 5 && span.Style == MermaidTextStyle.Bold));
    }

    [Test]
    public void Measure_FlowchartMarkdownLabelDoesNotThrow()
    {
        var presenter = new MermaidPresenter
        {
            Text = """
                   graph TD
                       A["`**Bold** *Italic*`"] --> B
                   """
        };

        Assert.DoesNotThrow(() => presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)));
        Assert.That(presenter.DesiredSize.Width, Is.GreaterThan(0));
        Assert.That(presenter.DesiredSize.Height, Is.GreaterThan(0));
    }

    [Test]
    public void Measure_FlowchartHtmlLikeLabelDoesNotThrow()
    {
        var presenter = new MermaidPresenter
        {
            Text = """
                   graph TD
                       A["**Bold** *Italic*"] --> B
                   """
        };

        Assert.DoesNotThrow(() => presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)));
        Assert.That(presenter.DesiredSize.Width, Is.GreaterThan(0));
        Assert.That(presenter.DesiredSize.Height, Is.GreaterThan(0));
    }

    [Test]
    public void PreparedPositionedGraph_FlowchartCachesParsedLabels()
    {
        var positioned = Mermaider.MermaidRenderer.LayoutProvider.LayoutFlowchart(Mermaider.MermaidRenderer.Parse(
            """
            flowchart LR
                A["`**Bold** *Italic*`"] -->|yes| B
            """));

        var prepared = PreparedPositionedGraph.Prepare(positioned);

        Assert.That(prepared.Edges, Has.One.Matches<PreparedPositionedEdge>(
            edge => edge is not null && edge.LabelLayout is { Text: "yes" }));
        Assert.That(prepared.Nodes, Has.One.Matches<PreparedPositionedNode>(
            node => node is not null && node.Node.Id == "A" &&
                    node.LabelLayout.Text == "Bold Italic" &&
                    node.LabelLayout.Spans.Count >= 2));
    }

    [Test]
    public void PreparedPositionedGraph_FlowchartCachesEdgeGeometryWhenPlatformIsAvailable()
    {
        var positioned = Mermaider.MermaidRenderer.LayoutProvider.LayoutFlowchart(Mermaider.MermaidRenderer.Parse(
            """
            flowchart LR
                A --> B
            """));

        var prepared = PreparedPositionedGraph.Prepare(positioned);
        var edge = prepared.Edges.Single(edge => edge.Edge.Points.Count >= 2);

        var first = GetRoundedPathOrIgnore(edge);
        var second = GetRoundedPathOrIgnore(edge);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void PreparedPositionedGraph_EdgeGeometryCacheIsKeyedByRadius()
    {
        var positioned = Mermaider.MermaidRenderer.LayoutProvider.LayoutFlowchart(Mermaider.MermaidRenderer.Parse(
            """
            flowchart LR
                A --> B
            """));

        var prepared = PreparedPositionedGraph.Prepare(positioned);
        var edge = prepared.Edges.Single(edge => edge.Edge.Points.Count >= 2);

        var first = GetRoundedPathOrIgnore(edge, 6);
        var second = GetRoundedPathOrIgnore(edge, 6);
        var third = GetRoundedPathOrIgnore(edge, 12);

        Assert.That(second, Is.SameAs(first));
        Assert.That(third, Is.Not.SameAs(first));
    }

    [Test]
    public void PreparedPositionedGraph_StateCachesEdgeGeometryWhenPlatformIsAvailable()
    {
        var positioned = Mermaider.MermaidRenderer.LayoutProvider.LayoutFlowchart(Mermaider.MermaidRenderer.Parse(
            """
            stateDiagram-v2
                [*] --> Idle
                Idle --> [*]
            """));

        var prepared = PreparedPositionedGraph.Prepare(positioned);

        foreach (var edge in prepared.Edges.Where(edge => edge.Edge.Points.Count >= 2))
        {
            Assert.That(GetRoundedPathOrIgnore(edge), Is.Not.Null);
        }
    }

    [Test]
    public void Measure_SequenceDiagramSmokeTestDoesNotThrow()
    {
        AssertMermaidMeasures(
            """
            sequenceDiagram
                autonumber
                actor User
                participant API
                participant DB
                User->>API: Request
                API-->>User: Accepted
                API->>API: Validate
                loop Retry
                    API->>DB: Write
                end
                alt Success
                    DB-->>API: OK
                else Failure
                    DB-->>API: Error
                end
                Note over API,DB: smoke test note
                destroy DB
                API-xDB: Close
            """);
    }

    [Test]
    public void Measure_ClassDiagramSmokeTestDoesNotThrow()
    {
        AssertMermaidMeasures(
            """
            classDiagram
                class Animal {
                    <<abstract>>
                    +string Name
                    +Speak() void
                }
                class Dog {
                    +Run() void
                }
                class IRepository~T~ {
                    <<interface>>
                    +Save(T item) void
                }
                Animal <|-- Dog
                Dog *-- "1" Collar : owns
                IRepository <|.. Dog : persists
                note for Dog "Loyal companion"
            """);
    }

    [Test]
    public void Measure_ErDiagramSmokeTestDoesNotThrow()
    {
        AssertMermaidMeasures(
            """
            erDiagram
                CUSTOMER ||--o{ ORDER : places
                ORDER ||--|{ ORDER_ITEM : contains
                PRODUCT ||--o{ ORDER_ITEM : appears_in
                CUSTOMER {
                    string id PK
                    string email UK "contact email"
                }
                ORDER {
                    string id PK
                    date createdAt
                }
                ORDER_ITEM {
                    string id PK
                    int quantity
                }
                PRODUCT {
                    string id PK
                    string name
                }
            """);
    }

    [Test]
    public void Measure_PieChartSmokeTestDoesNotThrow()
    {
        AssertMermaidMeasures(
            """
            pie showData
                title Renderer Work Split
                "Flowchart and State" : 35
                "Shared helpers" : 20
                "Sequence/Class/ER" : 25
                "Other charts" : 20
            """);
    }

    [Test]
    public void Measure_QuadrantChartSmokeTestDoesNotThrow()
    {
        AssertMermaidMeasures(
            """
            quadrantChart
                title Renderer Priorities
                x-axis Low complexity --> High complexity
                y-axis Low visual risk --> High visual risk
                quadrant-1 Polish later
                quadrant-2 Design carefully
                quadrant-3 Quick wins
                quadrant-4 Implement first
                Flowchart: [0.25, 0.35]
                Pie: [0.30, 0.25]
            """);
    }

    [Test]
    public void Measure_TimelineDiagramSmokeTestDoesNotThrow()
    {
        AssertMermaidMeasures(
            """
            timeline
                title Native Mermaid Renderer Roadmap
                section Foundation
                Step 1 : Preprocessing : Presenter state
                Step 2 : Styled font sizes : Shared text helpers
                section Renderers
                Step 3 : Sequence : Class : ER
                Step 4 : Pie : Quadrant : Timeline
            """);
    }

    [Test]
    public void Measure_GitGraphSmokeTestDoesNotThrow()
    {
        AssertMermaidMeasures(
            """
            gitGraph
                commit id: "init"
                branch native-renderer order: 2
                checkout native-renderer
                commit id: "flowchart"
                commit id: "text" tag: "helpers"
                checkout main
                commit id: "docs"
                merge native-renderer id: "merge-native" tag: "demo"
            """);
    }

    [Test]
    public void Measure_RadarChartSmokeTestDoesNotThrow()
    {
        AssertMermaidMeasures(
            """
            radar-beta
                title Renderer Coverage
                axis flow["Flowchart"], state["State"], seq["Sequence"], cls["Class"], er["ER"], charts["Charts"]
                min 0
                max 100
                graticule polygon
                curve current["Current"]{90, 80, 15, 10, 10, 5}
                curve target["Target"]{100, 100, 90, 90, 85, 80}
            """);
    }

    [Test]
    public void Measure_TreemapDiagramSmokeTestDoesNotThrow()
    {
        AssertMermaidMeasures(
            """
            treemap-beta
              "Mermaid Native Renderer"
                "Foundation"
                  "Preprocessor": 20
                  "Presenter state": 20
                "Diagram Renderers"
                  "Sequence": 15
                  "Class": 12
                  "ER": 10
            """);
    }

    [Test]
    public void Measure_VennDiagramSmokeTestDoesNotThrow()
    {
        AssertMermaidMeasures(
            """
            venn-beta
                set Markdig["Markdown"]: 100
                set Mermaider["Mermaid model"]: 100
                set Avalonia["DrawingContext"]: 100
                union Markdig, Mermaider["Preprocessed labels"]
                union Mermaider, Avalonia["Native layout"]
            """);
    }

    [Test]
    public void Measure_EmptyPieStillUsesStableCanvas()
    {
        AssertMermaidMeasures(
            """
            pie showData
                title Empty Pie
                "Zero" : 0
            """);
    }

    [Test]
    public void Measure_EmptyTimelineUsesSvgFallbackSize()
    {
        var presenter = new MermaidPresenter
        {
            Text = """
                   timeline
                       title Empty Timeline
                   """
        };

        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.That(presenter.DesiredSize, Is.EqualTo(new Size(200, 100)));
    }

    [Test]
    public void Measure_EmptyGitGraphUsesSvgFallbackSize()
    {
        var presenter = new MermaidPresenter
        {
            Text = """
                   gitGraph
                   """
        };

        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.That(presenter.DesiredSize, Is.EqualTo(new Size(200, 100)));
    }

    [Test]
    public void Measure_EmptyRadarUsesSvgFallbackSize()
    {
        var presenter = new MermaidPresenter
        {
            Text = """
                   radar-beta
                   """
        };

        presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.That(presenter.DesiredSize, Is.EqualTo(new Size(200, 100)));
    }

    [Test]
    public void Measure_QuadrantWithoutPointsUsesStableAxisLabelLayout()
    {
        AssertMermaidMeasures(
            """
            quadrantChart
                title No Points
                x-axis Low --> High
                y-axis Quiet --> Loud
                quadrant-1 Top Right
                quadrant-2 Top Left
                quadrant-3 Bottom Left
                quadrant-4 Bottom Right
            """);
    }

    [Test]
    public void ChartRendererLayoutTokensAffectDesiredSize()
    {
        var pie = new PieChart { Title = "Pie", ShowData = false, Slices = [new PieSlice("A", 1)] };
        var quadrant = new QuadrantChart
        {
            XAxisLeft = "Low",
            XAxisRight = "High",
            Points = [new QuadrantPoint("A", 0.5, 0.5)]
        };
        var timeline = new TimelineDiagram { Sections = [new TimelineSection(null, [new TimelinePeriod("2024", ["Built"])])] };
        var gitGraph = new GitGraph { Actions = [new GitCommitAction { Id = "a" }, new GitCommitAction { Id = "b" }] };
        var radar = new RadarChart
        {
            Axes = [new RadarAxis("a", "A"), new RadarAxis("b", "B")],
            Curves = [new RadarCurve("current", "Current", [1, 2])],
            Max = 2
        };
        var treemap = new TreemapDiagram
        {
            Roots =
            [
                new TreemapNode
                {
                    Label = "Root",
                    Children = [new TreemapNode { Label = "A", Value = 1, Children = [] }]
                }
            ]
        };
        var venn = new VennDiagram
        {
            Sets = [new VennSet("A", "A"), new VennSet("B", "B")],
            Unions = [new VennUnion(["A", "B"], "Both")]
        };
        var pieRenderer = new PieRenderer();
        var quadrantRenderer = new QuadrantRenderer();
        var timelineRenderer = new TimelineRenderer();
        var gitGraphRenderer = new GitGraphRenderer();
        var radarRenderer = new RadarRenderer();
        var treemapRenderer = new TreemapRenderer();
        var vennRenderer = new VennRenderer();

        var pieDefault = pieRenderer.MeasureDiagram(pie);
        var quadrantDefault = quadrantRenderer.MeasureDiagram(quadrant);
        var timelineDefault = timelineRenderer.MeasureDiagram(timeline);
        var gitGraphDefault = gitGraphRenderer.MeasureDiagram(gitGraph);
        var radarDefault = radarRenderer.MeasureDiagram(radar);
        var treemapDefault = treemapRenderer.MeasureDiagram(treemap);
        var vennDefault = vennRenderer.MeasureDiagram(venn);

        pieRenderer.PieTopPadding += 40;
        quadrantRenderer.XAxisLabelReserve += 40;
        timelineRenderer.LeftPadding += 40;
        gitGraphRenderer.CommitSpacing += 40;
        radarRenderer.Radius += 40;
        treemapRenderer.ChartWidth += 40;
        vennRenderer.CenterX += 40;

        Assert.Multiple(() =>
        {
            Assert.That(pieRenderer.MeasureDiagram(pie).Height, Is.GreaterThan(pieDefault.Height));
            Assert.That(quadrantRenderer.MeasureDiagram(quadrant).Height, Is.GreaterThan(quadrantDefault.Height));
            Assert.That(timelineRenderer.MeasureDiagram(timeline).Width, Is.GreaterThan(timelineDefault.Width));
            Assert.That(gitGraphRenderer.MeasureDiagram(gitGraph).Width, Is.GreaterThan(gitGraphDefault.Width));
            Assert.That(radarRenderer.MeasureDiagram(radar).Width, Is.GreaterThan(radarDefault.Width));
            Assert.That(treemapRenderer.MeasureDiagram(treemap).Width, Is.GreaterThan(treemapDefault.Width));
            Assert.That(vennRenderer.MeasureDiagram(venn).Width, Is.GreaterThan(vennDefault.Width));
        });
    }

    private static void AssertMermaidMeasures(string text, MermaidRenderOptions? options = null)
    {
        var presenter = new MermaidPresenter { Text = text, RenderOptions = options };
        Assert.DoesNotThrow(() => presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)));
        Assert.That(presenter.DesiredSize.Width, Is.GreaterThan(0));
        Assert.That(presenter.DesiredSize.Height, Is.GreaterThan(0));
    }

    private static MermaidRenderer[] GetActiveRenderers(MermaidPresenter presenter) =>
        presenter.GetLogicalChildren().OfType<MermaidRenderer>().ToArray();

    private static StreamGeometry? GetRoundedPathOrIgnore(PreparedPositionedEdge edge)
    {
        return GetRoundedPathOrIgnore(edge, null);
    }

    private static StreamGeometry? GetRoundedPathOrIgnore(PreparedPositionedEdge edge, double? radius)
    {
        try
        {
            return radius is { } r ? edge.GetRoundedPath(r) : edge.RoundedPath;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("IPlatformRenderInterface", StringComparison.Ordinal))
        {
            Assert.Ignore("StreamGeometry creation requires an Avalonia platform render interface.");
            return null;
        }
    }

    private sealed class CountingLayoutProvider(IGraphLayoutProvider fallback) : IGraphLayoutProvider
    {
        public int FlowchartCalls { get; private set; }

        public int ClassCalls { get; private set; }

        public int ErCalls { get; private set; }

        public PositionedGraph LayoutFlowchart(MermaidGraph graph, MermaidRenderOptions? options = null, StrictStylingOptions? strict = null)
        {
            FlowchartCalls++;
            return fallback.LayoutFlowchart(graph, options, strict);
        }

        public PositionedClassDiagram LayoutClass(ClassDiagram diagram)
        {
            ClassCalls++;
            return fallback.LayoutClass(diagram);
        }

        public PositionedErDiagram LayoutEr(ErDiagram diagram)
        {
            ErCalls++;
            return fallback.LayoutEr(diagram);
        }
    }
}
