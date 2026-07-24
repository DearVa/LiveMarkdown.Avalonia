using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;

namespace LiveMarkdown.Avalonia;

public class BackslashMathExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<BackslashMathBlockParser>())
        {
            pipeline.BlockParsers.Insert(0, new BackslashMathBlockParser());
        }

        if (!pipeline.InlineParsers.Contains<BackslashMathInlineParser>())
        {
            pipeline.InlineParsers.Insert(0, new BackslashMathInlineParser());
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}

public static class MathExtensionBuilder
{
    public static MarkdownPipelineBuilder UseExtendedMathematics(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.UseMathematics();
        pipeline.Extensions.AddIfNotAlready<BackslashMathExtension>();
        return pipeline;
    }
}

public class BackslashMathInlineParser : InlineParser
{
    public BackslashMathInlineParser()
    {
        OpeningCharacters = ['\\'];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var openingDelimiter = slice.PeekChar(1);
        var closingDelimiter = openingDelimiter switch
        {
            '(' => ')',
            '[' => ']',
            _ => '\0'
        };

        if (closingDelimiter == '\0') return false;

        var openingStart = slice.Start;
        var contentStart = openingStart + 2;
        var closingStart = IndexOfClosingDelimiter(slice, contentStart, closingDelimiter);
        if (closingStart < 0) return false;

        var closingEnd = closingStart + 1;
        var mathInline = new MathInline
        {
            Delimiter = '\\',
            DelimiterCount = 2,
            Content = CreateSlice(slice.Text, contentStart, closingStart - 1),
            Span = new SourceSpan(openingStart, closingEnd)
        };

        processor.Inline = mathInline;
        slice.Start = closingEnd + 1;
        return true;
    }

    private static int IndexOfClosingDelimiter(StringSlice slice, int start, char closingDelimiter)
    {
        for (var i = start; i < slice.End; i++)
        {
            var c = slice.Text[i];
            if (c is '\r' or '\n') return -1;
            if (c == '\\' && slice.Text[i + 1] == closingDelimiter) return i;
        }

        return -1;
    }

    private static StringSlice CreateSlice(string text, int start, int end)
    {
        return start <= end ? new StringSlice(text, start, end) : StringSlice.Empty;
    }
}

public class BackslashMathBlockParser : BlockParser
{
    public BackslashMathBlockParser()
    {
        OpeningCharacters = ['\\'];
    }

    public override bool CanInterrupt(BlockProcessor processor, Block block)
    {
        return IsOpeningLine(processor);
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (!IsOpeningLine(processor)) return BlockState.None;

        var block = new MathBlock(this)
        {
            FencedChar = '\\',
            OpeningFencedCharCount = 2,
            Line = processor.LineIndex,
            Column = processor.Column,
            Span = new SourceSpan(processor.Line.Start, processor.Line.End)
        };

        processor.NewBlocks.Push(block);
        return AppendUntilClosingDelimiter(processor, block, processor.Start + 2);
    }

    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        return block is MathBlock mathBlock
            ? AppendUntilClosingDelimiter(processor, mathBlock, processor.Line.Start)
            : BlockState.None;
    }

    private static bool IsOpeningLine(BlockProcessor processor)
    {
        if (processor.IsCodeIndent) return false;

        var line = processor.Line;
        return line.Start + 1 <= line.End &&
               line.Text[line.Start] == '\\' &&
               line.Text[line.Start + 1] == '[';
    }

    private static BlockState AppendUntilClosingDelimiter(BlockProcessor processor, MathBlock block, int start)
    {
        var line = processor.Line;
        var closingStart = IndexOfClosingDelimiter(line, start);

        if (closingStart >= 0)
        {
            AppendLine(block, processor, start, closingStart - 1);
            block.ClosingFencedCharCount = 2;
            block.Span = new SourceSpan(block.Span.Start, closingStart + 1);
            return BlockState.BreakDiscard;
        }

        AppendLine(block, processor, start, line.End);
        block.UpdateSpanEnd(line.End);
        return BlockState.ContinueDiscard;
    }

    private static int IndexOfClosingDelimiter(StringSlice line, int start)
    {
        for (var i = start; i < line.End; i++)
        {
            if (line.Text[i] == '\\' && line.Text[i + 1] == ']') return i;
        }

        return -1;
    }

    private static void AppendLine(MathBlock block, BlockProcessor processor, int start, int end)
    {
        var line = processor.Line;
        if (start > end || IsWhitespace(line.Text, start, end)) return;

        var slice = new StringSlice(line.Text, start, end);
        block.AppendLine(
            ref slice,
            processor.Column,
            processor.LineIndex,
            processor.CurrentLineStartPosition + start - line.Start,
            processor.TrackTrivia);
    }

    private static bool IsWhitespace(string text, int start, int end)
    {
        for (var i = start; i <= end; i++)
        {
            if (!char.IsWhiteSpace(text[i])) return false;
        }

        return true;
    }
}
