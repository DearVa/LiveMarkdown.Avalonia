using Avalonia.Controls;
using Markdig;
using Markdig.Extensions.Mathematics;
using NUnit.Framework;
using RaTeX.Avalonia;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class MathRenderingTests
{
    [Test]
    public Task Update_MissingFallbackFont_ShowsSourceAndRecovers() =>
        HeadlessSession.Current.Dispatch(() =>
        {
            // The shared headless backend has no CJK fonts.
            var node = new MathBlockNode();
            var presenter = (Panel)node.Control;
            var formula = presenter.Children.OfType<FormulaControl>().Single();
            var text = presenter.Children.OfType<MarkdownTextBlock>().Single();
            var pipeline = new MarkdownPipelineBuilder().UseExtendedMathematics().Build();

            void Update(string source)
            {
                var document = Markdown.Parse("$$\n" + source + "\n$$", pipeline);
                node.Update(new DocumentNode(new MarkdownRenderer()), (MathBlock)document[0],
                    new ObservableStringBuilderChangedEventArgs(0, int.MaxValue, 0, 0), CancellationToken.None);
            }

            Update(@"\text{中文}");
            Assert.That(formula.DisplayList, Is.Null);
            Assert.That(text.IsVisible, Is.True);
            Assert.That(text.Classes.Contains("Error"), Is.True);
            Assert.That(ToolTip.GetTip(text)?.ToString(), Does.Contain("fallback font"));

            Update("x+y");
            Assert.That(formula.IsVisible, Is.True);
            Assert.That(text.Classes.Contains("Error"), Is.False);
            Assert.That(ToolTip.GetTip(text), Is.Null);
            formula.DisplayList = null;
        }, CancellationToken.None);
}