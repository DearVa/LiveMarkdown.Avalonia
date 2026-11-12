using System.Reflection;
using Avalonia;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Mermaider.Models;
using NUnit.Framework;
using Point = Avalonia.Point;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class MermaidRenderingLayoutTests
{
    [Test]
    public Task StateDiagram_WithReciprocalTransitions_PreservesUpstreamRoutes()
    {
        return HeadlessSession.Current.Dispatch(
            () =>
            {
                // The demo's crowded retry route also occurs in Mermaider's SVG. Routing is owned
                // upstream; the native renderer must not silently substitute its own graph algorithm.
                const string source = """
                                      stateDiagram-v2
                                          [*] --> Idle
                                          Idle --> Loading : request
                                          Loading --> Ready : success
                                          Loading --> Error : failure
                                          Error --> Loading : retry
                                          Ready --> [*]
                                      """;
                var upstream = Mermaider.MermaidRenderer.LayoutProvider.LayoutFlowchart(Mermaider.MermaidRenderer.Parse(source));
                var presenter = new MermaidPresenter { Text = source };
                presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var native = GetDiagram<PreparedPositionedGraph>(presenter);
                Assert.That(native.Edges.Select(edge => edge.Edge.Points), Is.EqualTo(upstream.Edges.Select(edge => edge.Points)));
            },
            CancellationToken.None);
    }

    [Test]
    public void LabelPlacement_WithNearbyDemoQuadrantPoints_SeparatesLabelsAndMarkers()
    {
        // These are the Sequence, Class and ER points from the existing demo, on its 400px plot.
        var anchors = new[] { new Point(220, 100), new Point(260, 120), new Point(240, 140) };
        var occupied = anchors.Select(point => new Rect(point.X - 6, point.Y - 6, 12, 12)).ToList();
        foreach (var anchor in anchors)
        {
            var label = MermaidLabelPlacement.Place(new Size(64, 18), new Point(anchor.X, anchor.Y + 27), occupied, new Rect(0, 0, 400, 400));
            Assert.That(occupied.Any(rect => rect.Intersects(label)), Is.False);
            occupied.Add(label);
        }
    }

    [Test]
    public Task ErDiagram_WithConvergingRelationships_KeepsLabelsClearOfEntities()
    {
        return HeadlessSession.Current.Dispatch(
            () =>
            {
                // The demo converges ORDER and PRODUCT onto ORDER_ITEM. Native labels must use
                // their measured bounds rather than reverting to an arbitrary route midpoint.
                var presenter = new MermaidPresenter
                {
                    Text = """
                           erDiagram
                               CUSTOMER ||--o{ ORDER : places
                               ORDER ||--|{ ORDER_ITEM : contains
                               PRODUCT ||--o{ ORDER_ITEM : appears_in
                           """
                };
                presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var diagram = GetDiagram<PositionedErDiagram>(presenter);
                var padding = presenter.GetLogicalChildren().OfType<ErRenderer>().Single().EdgeLabelPadding;
                var occupied = diagram.Entities.Select(entity => new Rect(entity.X, entity.Y, entity.Width, entity.Height)).ToList();
                foreach (var relationship in diagram.Relationships)
                {
                    Assert.That(relationship.LabelPosition, Is.Not.Null);
                    var position = relationship.LabelPosition.GetValueOrDefault();
                    var text = MermaidTextRenderer.CreateFormattedText(
                        presenter,
                        relationship.Label,
                        presenter.EdgeLabelFontSize,
                        Brushes.Black,
                        TextAlignment.Center);
                    var label = new Rect(
                        position.X - (text.Width + padding * 2) / 2,
                        position.Y - (text.Height + padding) / 2,
                        text.Width + padding * 2,
                        text.Height + padding);
                    Assert.That(occupied.Any(rect => rect.Intersects(label)), Is.False);
                    occupied.Add(label);
                }
            },
            CancellationToken.None);
    }

    private static T GetDiagram<T>(MermaidPresenter presenter) where T : class
    {
        var state = typeof(MermaidPresenter).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(presenter);
        if (state is not MermaidDiagramState { Error: null, Diagram: T diagram })
        {
            throw new InvalidOperationException($"Expected a successful {typeof(T).Name} diagram, got {state}.");
        }
        return diagram;
    }
}