using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders a Markdig code block through the <see cref="CodeBlock"/> control.
/// </summary>
public class CodeBlockNode : BlockNode<Markdig.Syntax.CodeBlock>
{
    /// <summary>
    /// Gets the code block control.
    /// </summary>
    public override Control Control { get; }

    private readonly CodeBlock _codeBlock;

    /// <summary>
    /// Initializes a code block node and its control.
    /// </summary>
    public CodeBlockNode()
    {
        Control = _codeBlock = new CodeBlock
        {
            Classes = { "CodeBlock" }
        };
        _codeBlock.ApplyTemplate(); // Ensure the template is applied to initialize the CodeTextBlock
    }

    /// <inheritdoc/>
    protected override bool MatchesBlock(Markdig.Syntax.CodeBlock block)
    {
        var blockType = block.GetType();
        if (blockType == typeof(Markdig.Syntax.CodeBlock) || blockType == typeof(FencedCodeBlock))
        {
            return true;
        }

        return !HasMoreSpecificBlockNodeFactory(blockType, typeof(Markdig.Syntax.CodeBlock));
    }

    /// <summary>
    /// Updates code lines, language metadata, and syntax highlighting.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="codeBlock">The Markdig code block.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> when the code block remains valid.</returns>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        Markdig.Syntax.CodeBlock codeBlock,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (codeBlock.Lines.Lines is null) return false;

        _codeBlock.SourceSpan = codeBlock.Span;

        var inlines = _codeBlock.Inlines;
        foreach (var (slice, lineIndex) in codeBlock.Lines.Lines.Take(codeBlock.Lines.Count).Select((l, i) => (l.Slice, i)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inlineIndex = lineIndex * 2;

            // Skip if the slice is completely outside the change range
            if (inlines.Count > inlineIndex &&
                (slice.End < change.StartIndex || change.StartIndex + change.Length <= slice.Start)) continue;

            if (inlines.Count <= inlineIndex)
            {
                if (inlines.Count % 2 == 1)
                {
                    // we need to add a LineBreak before the new Run
                    inlines.Add(new LineBreak());
                }

                inlines.Add(new Run(slice.ToString()));
            }
            else if (inlines[inlineIndex] is Run run)
            {
                // Update existing run
                run.Text = slice.ToString();
                run.Classes.Remove(SyntaxHighlighting.FormattedClassName);
            }
            else
            {
                // Replace it with a new run if it's not a Run
                inlines[inlineIndex] = new Run(slice.ToString());
            }

            if (lineIndex < codeBlock.Lines.Count - 1)
            {
                // Add a line break after each line except the last one
                if (inlines.Count <= inlineIndex + 1)
                {
                    inlines.Add(new LineBreak());
                }
                else if (inlines[inlineIndex + 1] is not LineBreak)
                {
                    // Replace it with a LineBreak if it's not a LineBreak
                    inlines[inlineIndex + 1] = new LineBreak();
                }
            }
        }

        while (inlines.Count > codeBlock.Lines.Count * 2 - 1)
        {
            // Remove excess inlines
            inlines.RemoveAt(inlines.Count - 1);
        }

        // Highlighting only works for closed FencedCodeBlock with Info
        if (codeBlock is not FencedCodeBlock fencedCodeBlock) return true;

        _codeBlock.Language = fencedCodeBlock.Info?.Trim();
        _codeBlock.HighlightSyntax();

        return true;
    }
}