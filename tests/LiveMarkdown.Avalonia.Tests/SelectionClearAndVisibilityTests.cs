using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

/// <summary>
/// Two selection contracts a reader notices when they break: a left press drops the highlight even when the
/// thing pressed handles the event, and select-all never reaches text that is hidden.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SelectionClearAndVisibilityTests
{
    private HeadlessUnitTestSession session = null!;

    [OneTimeSetUp]
    public void StartSession() => session = HeadlessSession.Current;

    private static (Window Window, MarkdownRenderer Renderer) Build(string markdown)
    {
        var renderer = new MarkdownRenderer { MarkdownBuilder = new ObservableStringBuilder(markdown) };
        var window = new Window { Width = 600, Height = 400, Content = renderer };
        window.Show();
        // The renderer's producer parses off the UI thread and publishes back onto it, so a layout pass
        // alone renders nothing — the dispatcher has to run for the document to arrive.
        for (var i = 0; i < 60; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            if (renderer.GetVisualDescendants().OfType<MarkdownTextBlock>().Any()) break;
            Thread.Sleep(5);
        }
        window.UpdateLayout();
        return (window, renderer);
    }

    private static PointerPressedEventArgs LeftPress(Control source, Visual root) =>
        new(source, new Pointer(0, PointerType.Mouse, true), root, default, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None)
        {
            RoutedEvent = InputElement.PointerPressedEvent,
            Source = source,
        };

    /// <summary>A press that something else HANDLES — a button, a chevron, an embedded control — still has to
    /// drop the selection, or the highlight sits there after the reader has clearly moved on.</summary>
    [Test]
    public void A_Left_Press_Marked_Handled_Still_Clears_The_Selection() => session.Dispatch(() =>
    {
        var (window, renderer) = Build("Some selectable prose to highlight.");
        renderer.SelectAll();
        Assert.That(renderer.SelectedText, Is.Not.Empty, "the fixture needs a selection to clear");

        // Stand in for a button, chevron or embedded node that swallows the press: a real control from the
        // rendered tree as the source, already marked handled before the renderer sees it.
        var swallow = renderer.GetVisualDescendants().OfType<MarkdownTextBlock>().First();
        var args = LeftPress(swallow, renderer);
        args.Handled = true;
        renderer.RaiseEvent(args);

        Assert.That(renderer.SelectedText, Is.Empty,
            "a handled press is still a press — the tunnel handler must see it");
        window.Close();
    }, CancellationToken.None).GetAwaiter().GetResult();

    [Test]
    public void A_Right_Press_Leaves_The_Selection_Alone() => session.Dispatch(() =>
    {
        var (window, renderer) = Build("Some selectable prose to highlight.");
        renderer.SelectAll();
        var before = renderer.SelectedText;

        var target = (Control)renderer;
        renderer.RaiseEvent(new PointerPressedEventArgs(
            target, new Pointer(0, PointerType.Mouse, true), target, default, 0,
            new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed),
            KeyModifiers.None)
        {
            RoutedEvent = InputElement.PointerPressedEvent,
            Source = target,
        });

        Assert.That(renderer.SelectedText, Is.EqualTo(before),
            "right-click opens a context menu for the selection — dropping it first is useless");
        window.Close();
    }, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Hidden text must not reach the clipboard. Visibility is evaluated at selection time, not
    /// cached with the document, because collapsing a section changes it without changing the document.</summary>
    [Test]
    public void Select_All_Skips_Text_That_Is_Not_Visible() => session.Dispatch(() =>
    {
        var (window, renderer) = Build("Visible paragraph.\n\nSecond paragraph.");
        renderer.SelectAll();
        var whenAllVisible = renderer.SelectedText ?? "";
        Assert.That(whenAllVisible, Does.Contain("Second"), "the fixture needs both paragraphs selectable");

        // Collapse the branch containing the second block, the way a fold or an alternate view does.
        var blocks = renderer.GetVisualDescendants().OfType<MarkdownTextBlock>().ToList();
        Assert.That(blocks.Count, Is.GreaterThan(1), "the fixture needs more than one block");
        blocks[^1].IsVisible = false;
        window.UpdateLayout();

        renderer.SelectAll();

        Assert.That(renderer.SelectedText ?? "", Does.Not.Contain("Second"),
            "select-all must not pick up text the reader cannot see");
        window.Close();
    }, CancellationToken.None).GetAwaiter().GetResult();
}
