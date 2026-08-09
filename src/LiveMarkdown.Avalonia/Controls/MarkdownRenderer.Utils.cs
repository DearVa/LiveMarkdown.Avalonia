// @author https://github.com/DearVa

using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Media.TextFormatting.Unicode;
using Markdig.Syntax;
using LineBreak = Avalonia.Controls.Documents.LineBreak;

namespace LiveMarkdown.Avalonia;

public partial class MarkdownRenderer
{
    /// <summary>
    /// Utility class that keeps inline nodes synchronized with an <see cref="InlineCollection"/>.
    /// </summary>
    /// <param name="target"></param>
    public class InlinesProxy(InlineCollection target)
    {
        /// <summary>
        /// Gets the number of inline nodes in the proxy.
        /// </summary>
        public int Count => children.Count;

        /// <summary>
        /// Gets or replaces an inline node at the specified index.
        /// </summary>
        /// <param name="index">The zero-based inline index.</param>
        public InlineNode this[int index]
        {
            get => children[index];
            set
            {
                target[index] = value.Inline;
                children[index] = value;
            }
        }

        private readonly List<InlineNode> children = [];

        /// <summary>
        /// Adds an inline node and its Avalonia inline to the target collection.
        /// </summary>
        /// <param name="node">The node to add.</param>
        public void Add(InlineNode node)
        {
            children.Add(node);
            target.Add(node.Inline);
            Debug.Assert(children.Count == target.Count, "Children count mismatch");
        }

        /// <summary>
        /// Removes the inline node at the specified index.
        /// </summary>
        /// <param name="index">The zero-based inline index.</param>
        public void RemoveAt(int index)
        {
            var node = children[index];
            target.Remove(node.Inline);
            children.RemoveAt(index);
            Debug.Assert(children.Count == target.Count, "Children count mismatch");
        }
    }

    /// <summary>
    /// Utility class that keeps block nodes synchronized with an Avalonia control collection.
    /// </summary>
    /// <param name="target"></param>
    public class BlocksProxy(Controls target)
    {
        /// <summary>
        /// A lightweight block node used when a control has no corresponding Markdown node.
        /// </summary>
        /// <param name="control">The control represented by the mock node.</param>
        public class MockBlockNode(Control control) : BlockNode
        {
            private const string NotSupportedMessage = "This node is a mock and does not support this operation.";

            /// <summary>
            /// Gets the represented control.
            /// </summary>
            public override Control Control => control;

            private Control control = control;

            /// <summary>
            /// Replaces the represented control.
            /// </summary>
            /// <param name="control">The new control.</param>
            public void SetControl(Control control) => this.control = control;

            /// <summary>
            /// Mock nodes cannot update Markdown content.
            /// </summary>
            /// <exception cref="NotSupportedException">Always thrown.</exception>
            protected override bool UpdateCore(
                DocumentNode documentNode,
                MarkdownObject markdownObject,
                in ObservableStringBuilderChangedEventArgs change,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException(NotSupportedMessage);
            }
        }

        /// <summary>
        /// Gets the number of block nodes in the proxy.
        /// </summary>
        public int Count => children.Count;

        /// <summary>
        /// Gets or replaces a block node at the specified index.
        /// </summary>
        /// <param name="index">The zero-based block index.</param>
        public BlockNode this[int index]
        {
            get => children[index];
            set
            {
                target[index] = value.Control;
                children[index] = value;
            }
        }

        private readonly List<BlockNode> children = [];

        /// <summary>
        /// Adds a block node and its control to the target collection.
        /// </summary>
        /// <param name="node">The node to add.</param>
        public void Add(BlockNode node)
        {
            children.Add(node);
            target.Add(node.Control);
            Debug.Assert(children.Count == target.Count, "Children count mismatch");
        }

        /// <summary>
        /// Adds a control to the target collection for special cases where a mock node is needed.
        /// This should be treated carefully as it adds a mock node.
        /// </summary>
        /// <param name="control"></param>
        public void Add(Control control)
        {
            children.Add(new MockBlockNode(control));
            target.Add(control);
        }

        /// <summary>
        /// Replaces the control at an index, preserving a mock node when possible.
        /// </summary>
        /// <param name="index">The zero-based block index.</param>
        /// <param name="control">The replacement control.</param>
        public void SetControlAt(int index, Control control)
        {
            target[index] = control;
            if (children[index] is MockBlockNode mockBlockNode) mockBlockNode.SetControl(control);
            else children[index] = new MockBlockNode(control);
        }

        /// <summary>
        /// Removes the block node and control at the specified index.
        /// </summary>
        /// <param name="index">The zero-based block index.</param>
        public void RemoveAt(int index)
        {
            var node = children[index];
            target.Remove(node.Control);
            children.RemoveAt(index);
            Debug.Assert(children.Count == target.Count, "Children count mismatch");
        }

        /// <summary>
        /// Removes all block nodes and controls from the proxy and target collection.
        /// </summary>
        public void Clear()
        {
            target.Clear();
            children.Clear();
        }
    }

    private static class StringUtils
    {
        private enum CharClass
        {
            CharClassUnknown,
            CharClassWhitespace,
            CharClassAlphaNumeric,
        }

        public static bool IsEol(char c)
        {
            return c is '\r' or '\n';
        }

        public static bool IsStartOfWord(string text, int index)
        {
            if (index >= text.Length)
            {
                return false;
            }

            var codepoint = new Codepoint(text[index]);

            // A 'word' starts with an AlphaNumeric or some punctuation symbols immediately
            // preceeded by lwsp.
            if (index > 0)
            {
                var previousCodepoint = new Codepoint(text[index - 1]);

                if (!previousCodepoint.IsWhiteSpace)
                {
                    return false;
                }

                if (previousCodepoint.IsBreakChar)
                {
                    return true;
                }
            }

            switch (codepoint.GeneralCategory)
            {
                case GeneralCategory.LowercaseLetter:
                case GeneralCategory.TitlecaseLetter:
                case GeneralCategory.UppercaseLetter:
                case GeneralCategory.DecimalNumber:
                case GeneralCategory.LetterNumber:
                case GeneralCategory.OtherNumber:
                case GeneralCategory.DashPunctuation:
                case GeneralCategory.InitialPunctuation:
                case GeneralCategory.OpenPunctuation:
                case GeneralCategory.CurrencySymbol:
                case GeneralCategory.MathSymbol:
                    return true;

                // TODO: How do you do this in .NET?
                // case UnicodeCategory.OtherPunctuation:
                //    // words cannot start with '.', but they can start with '&' or '*' (for example)
                //    return g_unichar_break_type(buffer->text[index]) == G_UNICODE_BREAK_ALPHABETIC;
                default:
                    return false;
            }
        }

        public static bool IsEndOfWord(string text, int index)
        {
            if (index >= text.Length)
            {
                return true;
            }

            var codepoint = new Codepoint(text[index]);

            if (!codepoint.IsWhiteSpace)
            {
                return false;
            }
            // A 'word' starts with an AlphaNumeric or some punctuation symbols immediately
            // preceeded by lwsp.
            if (index > 0)
            {
                if (index + 1 < text.Length)
                {
                    var nextCodePoint = new Codepoint(text[index + 1]);

                    if (nextCodePoint.IsBreakChar)
                    {
                        return true;
                    }
                }
                else
                {
                    return true;
                }

            }

            switch (codepoint.GeneralCategory)
            {
                case GeneralCategory.LowercaseLetter:
                case GeneralCategory.TitlecaseLetter:
                case GeneralCategory.UppercaseLetter:
                case GeneralCategory.DecimalNumber:
                case GeneralCategory.LetterNumber:
                case GeneralCategory.OtherNumber:
                case GeneralCategory.DashPunctuation:
                case GeneralCategory.InitialPunctuation:
                case GeneralCategory.OpenPunctuation:
                case GeneralCategory.CurrencySymbol:
                case GeneralCategory.MathSymbol:
                    return false;

                // TODO: How do you do this in .NET?
                // case UnicodeCategory.OtherPunctuation:
                //    // words cannot start with '.', but they can start with '&' or '*' (for example)
                //    return g_unichar_break_type(buffer->text[index]) == G_UNICODE_BREAK_ALPHABETIC;
                default:
                    return true;
            }
        }

        public static int PreviousWord(string text, int cursor)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            cursor = Math.Min(cursor, text.Length);

            int cr;
            var lf = LineBegin(text, cursor) - 1;
            if (lf > 0 && text[lf] == '\n' && text[lf - 1] == '\r')
            {
                cr = lf - 1;
            }
            else
            {
                cr = lf;
            }

            // if the cursor is at the beginning of the line, return the end of the prev line
            if (cursor - 1 == lf)
            {
                return (cr > 0) ? cr : 0;
            }

            var cc = GetCharClass(text[cursor - 1]);
            var begin = lf + 1;
            var i = cursor;

            // skip over the word, punctuation, or run of whitespace
            while (i > begin && GetCharClass(text[i - 1]) == cc)
            {
                i--;
            }

            // if the cursor was at whitespace, skip back a word too
            if (cc == CharClass.CharClassWhitespace && i > begin)
            {
                cc = GetCharClass(text[i - 1]);
                while (i > begin && GetCharClass(text[i - 1]) == cc)
                {
                    i--;
                }
            }

            return i;
        }

        public static int NextWord(string text, int cursor)
        {
            if (cursor >= text.Length)
            {
                return cursor;
            }

            int lf;
            var cr = LineEnd(text, cursor);
            if (cr < text.Length && text[cr] == '\r' && cr + 1 < text.Length && text[cr + 1] == '\n')
            {
                lf = cr + 1;
            }
            else
            {
                lf = cr;
            }

            // if the cursor is at the end of the line, return the starting offset of the next line
            if (cursor == cr || cursor == lf)
            {
                if (lf < text.Length)
                {
                    return lf + 1;
                }

                return cursor;
            }

            // skip any whitespace after the word/punct
            var i = cursor;
            while (i < cr && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i >= cr)
            {
                return i;
            }

            var cc = GetCharClass(text[i]);

            // skip over the word, punctuation, or run of whitespace
            while (i < cr && GetCharClass(text[i]) == cc)
            {
                i++;
            }

            return i;
        }

        private static CharClass GetCharClass(char c)
        {
            if (char.IsWhiteSpace(c))
            {
                return CharClass.CharClassWhitespace;
            }

            if (char.IsLetterOrDigit(c))
            {
                return CharClass.CharClassAlphaNumeric;
            }

            return CharClass.CharClassUnknown;
        }

        private static int LineBegin(string text, int pos)
        {
            while (pos > 0 && !IsEol(text[pos - 1]))
            {
                pos--;
            }

            return pos;
        }

        private static int LineEnd(string text, int cursor, bool include = false)
        {
            while (cursor < text.Length && !IsEol(text[cursor]))
            {
                cursor++;
            }

            if (include && cursor < text.Length)
            {
                if (text[cursor] == '\r' && text[cursor + 1] == '\n')
                {
                    cursor += 2;
                }
                else
                {
                    cursor++;
                }
            }

            return cursor;
        }
    }
}

internal static class ClassesExtension
{
    /// <param name="classes"></param>
    extension(Classes classes)
    {
        /// <summary>
        /// Ensures that the specified class name is present in the given <see cref="Classes"/> collection.
        /// </summary>
        /// <param name="name"></param>
        public void EnsureClassName(string name)
        {
            if (!classes.Contains(name))
            {
                classes.Add(name);
            }
        }

        /// <summary>
        /// Ensures that the specified class name with prefix and suffix is present in the given <see cref="Classes"/> collection.
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="suffix"></param>
        public void EnsureClassName<TSuffix>(string prefix, TSuffix suffix)
        {
            var index = -1;
            for (var i = 0; i < classes.Count; i++)
            {
                var cls = classes[i];
                if (cls.StartsWith(prefix, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            var name = prefix + suffix;
            if (index == -1)
            {
                classes.Add(name);
            }
            else if (classes[index] != name)
            {
                // classes[index] = name; // This may not work in Avalonia
                classes.RemoveAt(index);
                classes.Add(name);
            }
        }
    }
}

internal static class InlineExtension
{
    extension(IEnumerable<Inline> inlines)
    {
        /// <summary>
        /// Gets the actual text content of the inlines, concatenating text from Runs and handling LineBreaks.
        /// </summary>
        public string ActualText
        {
            get
            {
                var stringBuilder = new StringBuilder();
                foreach (var inline in inlines) AppendInline(inline);
                return stringBuilder.ToString();

                void AppendInline(Inline inline)
                {
                    switch (inline)
                    {
                        case Run run:
                        {
                            stringBuilder.Append(run.Text);
                            break;
                        }
                        case Span span:
                        {
                            foreach (var childInline in span.Inlines) AppendInline(childInline);
                            break;
                        }
                        case LineBreak:
                        {
                            stringBuilder.Append(Environment.NewLine);
                            break;
                        }
                        case InlineUIContainer { Child: { } logicalChild }:
                        {
                            AppendLogicalText(logicalChild);
                            break;
                        }
                    }
                }

                void AppendLogicalText(ILogical logical)
                {
                    if (logical is MarkdownTextBlock markdownTextBlock)
                    {
                        stringBuilder.Append(markdownTextBlock.ActualText);
                        return; // markdownTextBlock.ActualText will handle its own inlines
                    }

                    foreach (var child in logical.LogicalChildren) AppendLogicalText(child);
                }
            }
        }
    }
}
