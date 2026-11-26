using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class StreamingRevealBidiTests
{
    [Test]
    public Task AdjacentAppendsInOneHebrewRunOccupyDifferentVisualThirds() =>
        HeadlessSession.Current.Dispatch(() =>
        {
            var block = new MarkdownTextBlock
            {
                Text = new string('א', 12), FlowDirection = FlowDirection.RightToLeft,
                Foreground = Brushes.Black, TextAlignment = TextAlignment.Center
            };
            var window = new Window { Width = 320, Height = 120, Content = block };
            try
            {
                window.Show();
                var now = Stopwatch.GetTimestamp();
                using var snapshot = block.CreateRevealSnapshot(0, default,
                    [new(4, 4, now - Stopwatch.Frequency / 4), new(8, 4, now)],
                    MarkdownStreamingRevealMode.Character, TimeSpan.FromSeconds(1));
                var line = snapshot!.Lines.Single();
                Assert.That(line.Glyphs, Has.Length.EqualTo(1));
                Assert.That(line.Bands, Has.Length.EqualTo(2));

                // All twelve letters carry the same advance. In this RTL run the first four
                // (settled) letters are the right third; the most recent four are the left.
                var native = block.TextLayout.TextLines.Single();
                var third = native.Width / 3;
                var older = line.Bands.Single(b => b.Age > TimeSpan.Zero);
                var newer = line.Bands.Single(b => b.Age == TimeSpan.Zero);
                Assert.That(newer.Left, Is.LessThanOrEqualTo(native.Start));
                Assert.That(newer.Right, Is.EqualTo(native.Start + third).Within(0.001));
                Assert.That(older.Left, Is.EqualTo(native.Start + third).Within(0.001));
                Assert.That(older.Right, Is.EqualTo(native.Start + third * 2).Within(0.001));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
}
