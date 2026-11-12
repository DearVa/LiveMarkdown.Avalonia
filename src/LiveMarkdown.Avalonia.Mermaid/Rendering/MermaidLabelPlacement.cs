using Avalonia;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Places measured native labels near their anchors without covering previously reserved content.
/// </summary>
/// <remarks>
/// Mermaider also resolves label collisions in its SVG renderers, after graph layout. The native
/// path needs the same final placement step, using Avalonia font metrics instead of SVG estimates.
/// Candidate positions clear obstacle edges directly, avoiding oscillation between adjacent boxes.
/// If no candidate fits the reserved area, the clamped original anchor is retained as a best-effort
/// placement. Crowded diagrams can still require more space from the host.
/// </remarks>
internal static class MermaidLabelPlacement
{
    public static Rect Place(Size size, Point anchor, IReadOnlyList<Rect> obstacles, Rect? area = null)
    {
        var original = new Rect(anchor.X - size.Width / 2, anchor.Y - size.Height / 2, size.Width, size.Height);
        var best = Constrain(original, area);
        var bestDistance = double.PositiveInfinity;
        Consider(best);
        if (bestDistance == 0)
        {
            return best;
        }

        foreach (var obstacle in obstacles)
        {
            Consider(original.WithX(obstacle.Left - size.Width - 4));
            Consider(original.WithX(obstacle.Right + 4));
            Consider(original.WithY(obstacle.Top - size.Height - 4));
            Consider(original.WithY(obstacle.Bottom + 4));
        }
        return best;

        void Consider(Rect candidate)
        {
            candidate = Constrain(candidate, area);
            if (obstacles.Any(obstacle => candidate.Intersects(obstacle.Inflate(3))))
            {
                return;
            }
            var delta = candidate.Center - anchor;
            var distance = delta.X * delta.X + delta.Y * delta.Y;
            if (distance >= bestDistance)
            {
                return;
            }
            best = candidate;
            bestDistance = distance;
        }
    }

    private static Rect Constrain(Rect rect, Rect? area)
    {
        if (area is not { } bounds)
        {
            return rect;
        }
        return rect.WithX(Math.Clamp(rect.X, bounds.X, Math.Max(bounds.X, bounds.Right - rect.Width)))
            .WithY(Math.Clamp(rect.Y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - rect.Height)));
    }
}