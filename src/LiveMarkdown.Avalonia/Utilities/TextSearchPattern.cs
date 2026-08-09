using System.Globalization;
using Markdig.Helpers;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Represents an immutable literal text-search pattern shared by rendered and projected text.
/// </summary>
public sealed class TextSearchPattern
{
    /// <summary>
    /// Gets the literal query text.
    /// </summary>
    public string Query { get; }

    /// <summary>
    /// Gets the comparison and word-boundary options.
    /// </summary>
    public TextSearchOptions Options { get; }

    private readonly bool ignoreCase;
    private readonly bool wholeWord;
    private readonly bool containsObjectReplacementCharacter;
    private readonly bool startsWithWord;
    private readonly bool endsWithWord;

    /// <summary>
    /// Initializes a literal text-search pattern.
    /// </summary>
    /// <param name="query">The non-empty literal query.</param>
    /// <param name="options">Case and whole-word options.</param>
    public TextSearchPattern(string query, TextSearchOptions options = TextSearchOptions.None)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);

        Query = query;
        Options = options;
        ignoreCase = !options.HasFlag(TextSearchOptions.MatchCase);
        wholeWord = options.HasFlag(TextSearchOptions.WholeWord);
        containsObjectReplacementCharacter = query.Contains(MarkdownTextProjection.ObjectReplacementCharacter);
        startsWithWord = IsWordCharacter(query[0]);
        endsWithWord = IsWordCharacter(query[^1]);
    }

    /// <summary>
    /// Finds non-overlapping matches in UTF-16 coordinates.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <returns>Matches in ascending order.</returns>
    public IEnumerable<TextHighlightRange> FindRanges(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return FindRanges(new StringSlice(text));
    }

    /// <summary>
    /// Finds non-overlapping matches in a source slice using slice-local UTF-16 coordinates.
    /// </summary>
    /// <param name="text">The source slice to search.</param>
    /// <returns>Matches in ascending order relative to the start of the slice.</returns>
    public IEnumerable<TextHighlightRange> FindRanges(StringSlice text)
    {
        if (text.Text is null || text.IsEmpty || containsObjectReplacementCharacter) yield break;

        var searchStart = 0;
        while (searchStart <= text.Length - Query.Length)
        {
            var absoluteIndex = text.IndexOf(Query, searchStart, ignoreCase);
            if (absoluteIndex < 0) yield break;

            var index = absoluteIndex - text.Start;
            var end = index + Query.Length;
            if (!wholeWord || IsWholeWordMatch(text, absoluteIndex, absoluteIndex + Query.Length))
            {
                yield return new TextHighlightRange(index, Query.Length);
                searchStart = end;
                continue;
            }

            // A rejected whole-word candidate does not occupy a result range. Advance by one
            // UTF-16 position so an overlapping candidate can still be considered.
            searchStart = index + 1;
        }
    }

    private bool IsWholeWordMatch(StringSlice text, int start, int end) =>
        (!startsWithWord || start == text.Start || !IsWordCharacter(text.Text[start - 1])) &&
        (!endsWithWord || end > text.End || !IsWordCharacter(text.Text[end]));

    private static bool IsWordCharacter(char character)
    {
        return character == '_' || char.GetUnicodeCategory(character) is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark or
            UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.LetterNumber or
            UnicodeCategory.OtherNumber or
            UnicodeCategory.ConnectorPunctuation;
    }
}