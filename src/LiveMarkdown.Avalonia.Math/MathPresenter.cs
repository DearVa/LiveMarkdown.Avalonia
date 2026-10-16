using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using RaTeX;
using RaTeX.Avalonia;

namespace LiveMarkdown.Avalonia;

internal sealed class MathPresenter : Panel
{
    private readonly MarkdownTextBlock _textBlock;
    private readonly FormulaControl _formula;
    private readonly RatexMode _mode;

    private string? _source;
    private bool _isOpen;
    private string? _parseError;

    public MathPresenter(string styleClass, RatexMode mode)
    {
        _mode = mode;
        Classes.Add(styleClass);
        _textBlock = new MarkdownTextBlock
        {
            Classes =
            {
                styleClass
            }
        };
        _formula = new FormulaControl
        {
            Classes =
            {
                styleClass
            },
            IsVisible = false,
            [!FormulaControl.FontSizeProperty] =
                mode == RatexMode.Inline ? this[!TextElement.FontSizeProperty] : _textBlock[!TextBlock.FontSizeProperty],
        };
        _formula.PropertyChanged += (_, change) =>
        {
            if (change.Property == FormulaControl.PreparationErrorProperty)
            {
                UpdateVisibility();
            }
            else if (change.Property == FormulaControl.BaselineProperty)
            {
                UpdateBaseline();
            }
        };
        Children.Add(_textBlock);
        Children.Add(_formula);
    }

    public void Update(string source, bool isOpen = false)
    {
        _source = source;
        _isOpen = isOpen;
        Parse();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextElement.ForegroundProperty && _source is not null)
        {
            Parse();
        }
    }

    private void Parse()
    {
        _parseError = null;
        DisplayList? displayList = null;
        if (!_isOpen)
        {
            var brush = GetValue(TextElement.ForegroundProperty) as ISolidColorBrush;
            var color = brush?.Color ?? Colors.Black;
            var options = new RatexOptions
            {
                Mode = _mode,
                DefaultColor = new RatexColor(
                    color.R / 255f,
                    color.G / 255f,
                    color.B / 255f,
                    (float)(color.A / 255d * (brush?.Opacity ?? 1)))
            };
            try
            {
                if (!Ratex.TryParseAndLayout(_source!, options, out displayList, out var error))
                {
                    _parseError = error.NativeMessage ?? "Unable to render formula.";
                }
            }
            catch (ArgumentException ex)
            {
                // Streaming input may temporarily contain an unpaired surrogate or NUL.
                _parseError = ex.Message;
            }
        }

        _formula.DisplayList = displayList;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        var error = _parseError ?? _formula.PreparationError?.Message;
        var rendered = !_isOpen && error is null && _formula.DisplayList is not null;
        _formula.IsVisible = rendered;
        _textBlock.IsVisible = !rendered;
        _textBlock.Classes.Set("Error", error is not null);
        _textBlock.Text = rendered ? null : _source;
        ToolTip.SetTip(_textBlock, error);
        UpdateBaseline();
    }

    private void UpdateBaseline()
    {
        if (_formula.IsVisible)
        {
            TextBlock.SetBaselineOffset(this, _formula.Baseline);
        }
        else
        {
            ClearValue(TextBlock.BaselineOffsetProperty);
        }
    }
}