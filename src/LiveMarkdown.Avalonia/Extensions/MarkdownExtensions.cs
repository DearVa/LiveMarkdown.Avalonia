using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Extension methods for integrating LiveMarkdown behavior with Markdig pipelines and source spans.
/// </summary>
public static class MarkdownExtensions
{
    /// <summary>
    /// Registers the code-block span fixer extension on a Markdig pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline to configure.</param>
    /// <returns>The same pipeline instance for fluent configuration.</returns>
    public static MarkdownPipelineBuilder UseCodeBlockSpanFixer(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.ReplaceOrAdd<CodeBlockSpanFixerExtension>(new CodeBlockSpanFixerExtension());
        return pipeline;
    }

    /// <summary>
    /// Converts a Markdig source span, whose end index is inclusive, to a .NET range.
    /// </summary>
    /// <param name="span">The source span to convert.</param>
    /// <returns>A range with an exclusive end index.</returns>
    public static Range ToRange(this in SourceSpan span)
    {
        return new Range(span.Start, span.End + 1);
    }

    /// <summary>
    /// Markdown extension that fixes the spans of code blocks.
    /// </summary>
    private class CodeBlockSpanFixerExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            var index = pipeline.BlockParsers.FindIndex(x => x is FencedCodeBlockParser);
            if (index == -1) pipeline.BlockParsers.Add(new CodeBlockSpanFixerParser());
            else pipeline.BlockParsers[index] = new CodeBlockSpanFixerParser();
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
        }
    }

    /// <summary>
    /// A parser that fixes the spans of code blocks.
    /// </summary>
    private class CodeBlockSpanFixerParser : FencedCodeBlockParser
    {
        public override BlockState TryContinue(BlockProcessor processor, Block block)
        {
            var state = base.TryContinue(processor, block);
            var currentBlock = block;
            while (currentBlock is not null)
            {
                FixSpan(ref currentBlock.Span, processor);
                currentBlock = currentBlock.Parent;
            }
            return state;
        }

        private static void FixSpan(ref SourceSpan span, BlockProcessor processor)
        {
            span = new SourceSpan(
                Math.Min(span.Start, processor.Line.Start),
                Math.Max(span.End, processor.Line.End));
        }
    }

    internal static int GetLength(this MarkdownDocument? document)
    {
        return document is null || document.Span.IsEmpty ? 0 : document.Span.End + 1;
    }
}