using Markdig.Extensions.Mathematics;
using RaTeX;
using AvaloniaDocs = Avalonia.Controls.Documents;

namespace LiveMarkdown.Avalonia;

public class MathInlineNode : InlineNode<MathInline>
{
    private readonly MathPresenter _presenter = new("Math", RatexMode.Inline);
    private readonly AvaloniaDocs.InlineUIContainer _inlineUIContainer;

    public override AvaloniaDocs.Inline Inline => _inlineUIContainer;

    public MathInlineNode()
    {
        _inlineUIContainer = new AvaloniaDocs.InlineUIContainer
        {
            Classes = { "Math" },
            Child = _presenter
        };
    }

    protected override bool UpdateCore(
        DocumentNode documentNode,
        MathInline math,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        _presenter.Update(math.Content.ToString());
        return true;
    }
}