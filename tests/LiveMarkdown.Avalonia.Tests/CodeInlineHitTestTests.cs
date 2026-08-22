using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

/// <summary>
/// <c>CodeInlineAt</c> / <c>CodeInlineRects</c> — the fork's answer to "a chip is a Run now, so how does a host
/// make it clickable?". Both are pure functions of the block's text layout, so they are exercised against a
/// laid-out block rather than through synthesized input.
/// </summary>
[TestFixture]
[NonParallelizable]
public class CodeInlineHitTestTests
{
    private HeadlessUnitTestSession session = null!;

    [OneTimeSetUp]
    public void StartSession() => session = HeadlessSession.Current;

    [OneTimeTearDown]
    public void StopSession()
    {
        // Shared for the whole assembly — see HeadlessSession. Deliberately not disposed.
    }

    /// <summary>A laid-out block: "before " + `chip` + " after".</summary>
    private static (MarkdownTextBlock Block, CodeInline Chip, Window Window) Build(
        string before = "before ", string chipText = "chip", string after = " after", double width = 600)
    {
        var chip = new CodeInline { Text = chipText, Background = Brushes.Gainsboro };
        var block = new MarkdownTextBlock { FontSize = 16, FontFamily = FontFamily.Default };
        block.Inlines!.Add(new Run(before));
        block.Inlines!.Add(chip);
        block.Inlines!.Add(new Run(after));

        var window = new Window { Width = width, Height = 300, Content = block };
        window.Show();   // headless Show() runs the initial layout pass, which is what builds TextLayout
        return (block, chip, window);
    }

    /// <summary>The centre of the chip's own painted rect — the point a user would actually be over.</summary>
    private static Point CentreOf(MarkdownTextBlock block, CodeInline chip)
    {
        var rects = block.GetCodeInlineRects(chip);
        Assert.That(rects, Is.Not.Empty, "the chip must have been laid out for this test to mean anything");
        return rects[0].Center;
    }

    [Test]
    public void A_Point_On_The_Chip_Finds_It() => session.Dispatch(() =>
    {
        var (block, chip, _) = Build();

        Assert.That(block.GetCodeInlineAt(CentreOf(block, chip)), Is.SameAs(chip));
    }, CancellationToken.None).GetAwaiter().GetResult();

    [Test]
    public void A_Point_On_The_PROSE_Finds_Nothing() => session.Dispatch(() =>
    {
        var (block, chip, _) = Build();
        var chipRect = block.GetCodeInlineRects(chip)[0];

        // Just left of the chip is the leading "before " run.
        var inProse = new Point(Math.Max(1, chipRect.X - 6), chipRect.Center.Y);

        Assert.That(block.GetCodeInlineAt(inProse), Is.Null,
            "prose beside a chip must not report the chip, or the whole line becomes clickable");
    }, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// The reason <c>CodeInlineAt</c> insists the hit is INSIDE the text: Avalonia snaps a point past the end of
    /// a line back onto the last character, so a line ENDING in a chip would otherwise claim the entire empty
    /// remainder of that line.
    /// </summary>
    [Test]
    public void Empty_Space_Past_A_Trailing_Chip_Finds_Nothing() => session.Dispatch(() =>
    {
        var (block, chip, _) = Build(before: "x ", chipText: "tail", after: "");
        var chipRect = block.GetCodeInlineRects(chip)[0];

        var pastTheEnd = new Point(chipRect.Right + 200, chipRect.Center.Y);

        Assert.That(block.GetCodeInlineAt(pastTheEnd), Is.Null);
    }, CancellationToken.None).GetAwaiter().GetResult();

    [Test]
    public void A_Point_Below_The_Text_Finds_Nothing() => session.Dispatch(() =>
    {
        var (block, chip, _) = Build();

        Assert.That(block.GetCodeInlineAt(new Point(CentreOf(block, chip).X, 250)), Is.Null);
    }, CancellationToken.None).GetAwaiter().GetResult();

    [Test]
    public void The_Right_Chip_Is_Found_When_There_Are_Several() => session.Dispatch(() =>
    {
        var first = new CodeInline { Text = "alpha" };
        var second = new CodeInline { Text = "omega" };
        var block = new MarkdownTextBlock { FontSize = 16, FontFamily = FontFamily.Default };
        block.Inlines!.Add(new Run("a "));
        block.Inlines!.Add(first);
        block.Inlines!.Add(new Run(" b "));
        block.Inlines!.Add(second);
        var window = new Window { Width = 600, Height = 300, Content = block };
        window.Show();   // headless Show() runs the initial layout pass, which is what builds TextLayout

        Assert.That(block.GetCodeInlineAt(block.GetCodeInlineRects(first)[0].Center), Is.SameAs(first));
        Assert.That(block.GetCodeInlineAt(block.GetCodeInlineRects(second)[0].Center), Is.SameAs(second));
    }, CancellationToken.None).GetAwaiter().GetResult();

    [Test]
    public void Rects_Follow_The_Chip_Along_The_Line() => session.Dispatch(() =>
    {
        var (shortBlock, shortChip, _) = Build(before: "a ");
        var (longBlock, longChip, _) = Build(before: "a much much longer preamble than that one ");

        var near = shortBlock.GetCodeInlineRects(shortChip)[0];
        var far = longBlock.GetCodeInlineRects(longChip)[0];

        Assert.That(far.X, Is.GreaterThan(near.X),
            "the rect must track where the chip actually sits — a caller positions an affordance off it");
        Assert.That(near.Width, Is.GreaterThan(0));
        Assert.That(near.Height, Is.GreaterThan(0));
    }, CancellationToken.None).GetAwaiter().GetResult();

    [Test]
    public void A_Chip_From_Another_Block_Has_No_Rects() => session.Dispatch(() =>
    {
        var (block, _, _) = Build();
        var stranger = new CodeInline { Text = "elsewhere" };

        Assert.That(block.GetCodeInlineRects(stranger), Is.Empty);
    }, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>A chip that wraps reports one rect per line, so a caller can attach to the last fragment rather
    /// than to a union that would span the gutter between lines.</summary>
    [Test]
    public void A_Wrapped_Chip_Reports_A_Rect_Per_Line() => session.Dispatch(() =>
    {
        var (block, chip, _) = Build(
            before: "prefix ",
            chipText: "a-very-long-chip-that-has-to-wrap-because-the-block-is-narrow",
            after: " suffix",
            width: 120);

        var rects = block.GetCodeInlineRects(chip);

        Assert.That(rects, Is.Not.Empty);
        if (rects.Count > 1)
        {
            Assert.That(rects[^1].Y, Is.GreaterThan(rects[0].Y), "later fragments sit on later lines");
        }
    }, CancellationToken.None).GetAwaiter().GetResult();
}
