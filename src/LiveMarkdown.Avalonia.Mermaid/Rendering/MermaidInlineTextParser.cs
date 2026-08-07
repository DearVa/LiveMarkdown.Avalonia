using System.Net;
using System.Buffers;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdigCodeInline = Markdig.Syntax.Inlines.CodeInline;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Converts Mermaid label inline markup into the compact text layout consumed by native renderers.
/// </summary>
/// <remarks>
/// Mermaid labels arrive through two different paths: raw Markdown labels such as
/// <c>`**bold**`</c>, and Mermaider-normalized labels that can contain a tiny HTML-like subset such
/// as <c>&lt;b&gt;bold&lt;/b&gt;</c>. This parser keeps those paths separate so escaped text like
/// <c>&amp;lt;b&amp;gt;</c> stays text instead of being promoted into markup.
/// </remarks>
internal static class MermaidInlineTextParser
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// Parses a Mermaid label using Markdown when the upstream node explicitly marks it as Markdown,
    /// otherwise falling back to the Mermaider HTML-like subset only when supported tags are present.
    /// </summary>
    public static MermaidTextLayout Parse(string? text, bool isMarkdown)
    {
        if (string.IsNullOrEmpty(text))
        {
            return MermaidTextLayout.Plain(text);
        }

        if (isMarkdown)
        {
            return ParseMarkdown(text);
        }

        return ContainsMermaiderHtmlLikeFormatting(text) ? ParseMermaiderHtmlLike(text) : MermaidTextLayout.Plain(text);
    }

    /// <summary>
    /// Parses simple inline Markdown into plain text plus style ranges.
    /// </summary>
    /// <remarks>
    /// Block structure is intentionally flattened to line breaks because diagram labels are drawn as
    /// one <c>FormattedText</c> instance, not through Markdig's block renderer.
    /// Links keep only their visible text; URLs are not meaningful inside the diagram canvas yet.
    /// </remarks>
    public static MermaidTextLayout ParseMarkdown(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return MermaidTextLayout.Plain(markdown);
        }

        var builder = new Builder(stackalloc char[256], stackalloc MermaidTextSpan[8]);
        var document = Markdown.Parse(markdown, MarkdownPipeline);
        var firstBlock = true;
        try
        {
            foreach (var block in document)
            {
                if (!firstBlock)
                {
                    builder.Append("\n", MermaidTextStyle.None);
                }

                firstBlock = false;

                if (block is LeafBlock { Inline: { } inline })
                {
                    AppendInlineChildren(ref builder, inline, MermaidTextStyle.None);
                }
            }

            return builder.ToLayout();
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>
    /// Parses the narrow HTML-like subset currently produced by Mermaider label normalization.
    /// </summary>
    /// <remarks>
    /// Supported tags are <c>b</c>/<c>strong</c>, <c>i</c>/<c>em</c>, <c>u</c>,
    /// <c>s</c>/<c>del</c>, and <c>br</c>. Unknown tags, tags with attributes, and malformed tags
    /// are left as ordinary text. Entities are decoded only while appending text segments, so
    /// <c>&amp;lt;b&amp;gt;</c> renders as <c>&lt;b&gt;</c> instead of becoming a bold tag.
    /// </remarks>
    public static MermaidTextLayout ParseMermaiderHtmlLike(string? htmlLike)
    {
        if (string.IsNullOrEmpty(htmlLike))
        {
            return MermaidTextLayout.Plain(htmlLike);
        }

        var builder = new Builder(stackalloc char[256], stackalloc MermaidTextSpan[8]);
        var styleStack = new StyleStack(stackalloc MermaidTextStyle[8]);

        var i = 0;
        var textStart = 0;
        try
        {
            while (i < htmlLike.Length)
            {
                if (htmlLike[i] != '<' ||
                    !TryReadTag(htmlLike.AsSpan(i), out var tag, out var consumed, out var isClosing, out var isSelfClosing))
                {
                    i++;
                    continue;
                }

                AppendDecodedText(ref builder, htmlLike.AsSpan(textStart, i - textStart), styleStack.Current);

                if (tag == MermaidHtmlTag.Br)
                {
                    builder.Append("\n", styleStack.Current);
                }
                else
                {
                    var style = ToStyle(tag);
                    if (isClosing)
                    {
                        styleStack.RemoveLast(style);
                    }
                    else
                    {
                        styleStack.Push(style);
                        if (isSelfClosing)
                        {
                            styleStack.RemoveLast(style);
                        }
                    }
                }

                i += consumed;
                textStart = i;
            }

            AppendDecodedText(ref builder, htmlLike.AsSpan(textStart), styleStack.Current);
            return builder.ToLayout();
        }
        finally
        {
            builder.Dispose();
            styleStack.Dispose();
        }
    }

    /// <summary>
    /// Checks whether a string contains one of the supported Mermaider HTML-like formatting tags.
    /// </summary>
    /// <remarks>
    /// This is a cheap gate used by non-Markdown labels so ordinary text avoids the parser path.
    /// </remarks>
    internal static bool ContainsMermaiderHtmlLikeFormatting(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<' &&
                TryReadTag(text.AsSpan(i), out _, out _, out _, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendInlineChildren(ref Builder builder, ContainerInline container, MermaidTextStyle style)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            AppendInline(ref builder, inline, style);
        }
    }

    private static void AppendInline(ref Builder builder, Inline inline, MermaidTextStyle style)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.ToString(), style);
                break;
            case MarkdigCodeInline code:
                builder.Append(code.Content, style | MermaidTextStyle.Code);
                break;
            case LineBreakInline:
                builder.Append("\n", style);
                break;
            case HtmlEntityInline entity:
                builder.Append(entity.Transcoded.ToString(), style);
                break;
            case EmphasisInline emphasis:
                AppendInlineChildren(ref builder, emphasis, style | GetEmphasisStyle(emphasis));
                break;
            case LinkInline link:
                AppendInlineChildren(ref builder, link, style);
                break;
            case ContainerInline container:
                AppendInlineChildren(ref builder, container, style);
                break;
        }
    }

    private static MermaidTextStyle GetEmphasisStyle(EmphasisInline emphasis)
    {
        return emphasis.DelimiterChar switch
        {
            '*' or '_' when emphasis.DelimiterCount >= 2 => MermaidTextStyle.Bold,
            '*' or '_' => MermaidTextStyle.Italic,
            '~' when emphasis.DelimiterCount >= 2 => MermaidTextStyle.Strikethrough,
            '+' when emphasis.DelimiterCount >= 2 => MermaidTextStyle.Underline,
            _ => MermaidTextStyle.None
        };
    }

    private static void AppendDecodedText(ref Builder builder, ReadOnlySpan<char> text, MermaidTextStyle style)
    {
        if (text.IsEmpty)
        {
            return;
        }

        if (!text.Contains('&'))
        {
            builder.Append(text, style);
            return;
        }

        builder.Append(WebUtility.HtmlDecode(text.ToString()), style);
    }

    private static MermaidTextStyle ToStyle(MermaidHtmlTag tag) =>
        tag switch
        {
            MermaidHtmlTag.Bold => MermaidTextStyle.Bold,
            MermaidHtmlTag.Italic => MermaidTextStyle.Italic,
            MermaidHtmlTag.Underline => MermaidTextStyle.Underline,
            MermaidHtmlTag.Strikethrough => MermaidTextStyle.Strikethrough,
            _ => MermaidTextStyle.None
        };

    private static bool TryReadTag(
        ReadOnlySpan<char> input,
        out MermaidHtmlTag tag,
        out int consumed,
        out bool isClosing,
        out bool isSelfClosing)
    {
        tag = MermaidHtmlTag.None;
        consumed = 0;
        isClosing = false;
        isSelfClosing = false;

        if (input.Length < 3 || input[0] != '<')
        {
            return false;
        }

        var end = input.IndexOf('>');
        if (end < 0)
        {
            return false;
        }

        var content = input[1..end].Trim();
        if (content.IsEmpty)
        {
            return false;
        }

        if (content[0] == '/')
        {
            isClosing = true;
            content = content[1..].TrimStart();
        }

        if (content.EndsWith("/".AsSpan(), StringComparison.Ordinal))
        {
            isSelfClosing = true;
            content = content[..^1].TrimEnd();
        }

        if (content.IsEmpty || content.IndexOfAny([' ', '\t', '\r', '\n']) >= 0)
        {
            return false;
        }

        tag = ReadTagName(content);
        if (tag == MermaidHtmlTag.None)
        {
            return false;
        }

        consumed = end + 1;
        return true;
    }

    private static MermaidHtmlTag ReadTagName(ReadOnlySpan<char> tag) =>
        tag switch
        {
            _ when tag.Equals("b".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("strong".AsSpan(), StringComparison.OrdinalIgnoreCase) => MermaidHtmlTag.Bold,
            _ when tag.Equals("i".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("em".AsSpan(), StringComparison.OrdinalIgnoreCase) => MermaidHtmlTag.Italic,
            _ when tag.Equals("u".AsSpan(), StringComparison.OrdinalIgnoreCase) => MermaidHtmlTag.Underline,
            _ when tag.Equals("s".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                tag.Equals("del".AsSpan(), StringComparison.OrdinalIgnoreCase) => MermaidHtmlTag.Strikethrough,
            _ when tag.Equals("br".AsSpan(), StringComparison.OrdinalIgnoreCase) => MermaidHtmlTag.Br,
            _ => MermaidHtmlTag.None
        };

    /// <summary>
    /// Whitelist of HTML-like tags that Mermaider can emit for inline label formatting.
    /// </summary>
    private enum MermaidHtmlTag
    {
        None,
        Bold,
        Italic,
        Underline,
        Strikethrough,
        Br
    }

    /// <summary>
    /// Appends plain text and merges adjacent source constructs by recording style ranges over the
    /// normalized output buffer.
    /// </summary>
    private ref struct Builder(Span<char> initialTextBuffer, Span<MermaidTextSpan> initialSpanBuffer)
    {
        private Span<char> _text = initialTextBuffer;
        private char[]? _rentedText;
        private int _textLength;
        private Span<MermaidTextSpan> _spans = initialSpanBuffer;
        private MermaidTextSpan[]? _rentedSpans;
        private int _spanCount;

        public void Append(string? text, MermaidTextStyle style)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Append(text.AsSpan(), style);
        }

        public void Append(ReadOnlySpan<char> text, MermaidTextStyle style)
        {
            if (text.IsEmpty)
            {
                return;
            }

            var start = _textLength;
            EnsureTextCapacity(_textLength + text.Length);
            text.CopyTo(_text[_textLength..]);
            _textLength += text.Length;

            if (style != MermaidTextStyle.None)
            {
                AddSpan(new MermaidTextSpan(start, text.Length, style));
            }
        }

        public MermaidTextLayout ToLayout()
        {
            var text = _text[.._textLength].ToString();
            var spans = _spanCount == 0 ? [] : _spans[.._spanCount].ToArray();
            return new MermaidTextLayout(text, spans);
        }

        public void Dispose()
        {
            if (_rentedText is not null)
            {
                ArrayPool<char>.Shared.Return(_rentedText);
                _rentedText = null;
            }

            if (_rentedSpans is not null)
            {
                ArrayPool<MermaidTextSpan>.Shared.Return(_rentedSpans, clearArray: true);
                _rentedSpans = null;
            }
        }

        private void AddSpan(MermaidTextSpan span)
        {
            EnsureSpanCapacity(_spanCount + 1);
            _spans[_spanCount++] = span;
        }

        private void EnsureTextCapacity(int requiredLength)
        {
            if (requiredLength <= _text.Length)
            {
                return;
            }

            var newArray = ArrayPool<char>.Shared.Rent(Math.Max(requiredLength, _text.Length * 2));
            _text[.._textLength].CopyTo(newArray);

            if (_rentedText is not null)
            {
                ArrayPool<char>.Shared.Return(_rentedText);
            }

            _rentedText = newArray;
            _text = newArray;
        }

        private void EnsureSpanCapacity(int requiredLength)
        {
            if (requiredLength <= _spans.Length)
            {
                return;
            }

            var newArray = ArrayPool<MermaidTextSpan>.Shared.Rent(Math.Max(requiredLength, _spans.Length * 2));
            _spans[.._spanCount].CopyTo(newArray);

            if (_rentedSpans is not null)
            {
                ArrayPool<MermaidTextSpan>.Shared.Return(_rentedSpans, clearArray: true);
            }

            _rentedSpans = newArray;
            _spans = newArray;
        }
    }

    private ref struct StyleStack(Span<MermaidTextStyle> initialBuffer)
    {
        private Span<MermaidTextStyle> _items = initialBuffer;
        private MermaidTextStyle[]? _rentedItems;
        private int _count;

        public MermaidTextStyle Current
        {
            get
            {
                var style = MermaidTextStyle.None;
                for (var i = 0; i < _count; i++)
                {
                    style |= _items[i];
                }

                return style;
            }
        }

        public void Push(MermaidTextStyle style)
        {
            EnsureCapacity(_count + 1);
            _items[_count++] = style;
        }

        public void RemoveLast(MermaidTextStyle style)
        {
            for (var i = _count - 1; i >= 0; i--)
            {
                if (_items[i] != style)
                {
                    continue;
                }

                if (i < _count - 1)
                {
                    _items[(i + 1).._count].CopyTo(_items[i..]);
                }

                _count--;
                return;
            }
        }

        public void Dispose()
        {
            if (_rentedItems is not null)
            {
                ArrayPool<MermaidTextStyle>.Shared.Return(_rentedItems, clearArray: true);
                _rentedItems = null;
            }
        }

        private void EnsureCapacity(int requiredLength)
        {
            if (requiredLength <= _items.Length)
            {
                return;
            }

            var newArray = ArrayPool<MermaidTextStyle>.Shared.Rent(Math.Max(requiredLength, _items.Length * 2));
            _items[.._count].CopyTo(newArray);

            if (_rentedItems is not null)
            {
                ArrayPool<MermaidTextStyle>.Shared.Return(_rentedItems, clearArray: true);
            }

            _rentedItems = newArray;
            _items = newArray;
        }
    }
}