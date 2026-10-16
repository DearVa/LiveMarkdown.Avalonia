using Avalonia.Controls;
using Markdig.Extensions.Mathematics;
using RaTeX;

namespace LiveMarkdown.Avalonia;

public class MathBlockNode : BlockNode<MathBlock>
{
    private readonly MathPresenter _presenter = new("MathBlock", RatexMode.Display);

    public override Control Control => _presenter;

    protected override bool UpdateCore(
        DocumentNode documentNode,
        MathBlock math,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        _presenter.Update(math.Lines.ToString(), math.IsOpen);
        return true;
    }
}