using Avalonia.Controls.Documents;
using Avalonia.Media;
using NUnit.Framework;
using TextMateSharp.Themes;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
public class SyntaxHighlightingTests
{
    [Test]
    public void ColorTheme_RemainsTheFallbackWhenCustomThemeNameIsNotSet()
    {
        var customTheme = new TestRawTheme("#123456");
        const string themeName = "LiveMarkdown.Tests.Fallback";

        try
        {
            SyntaxHighlighting.RegisterCustomTheme(themeName, customTheme);

            var codeBlock = CreateCodeBlock(null);

            Assert.That(GetForeground(codeBlock, "public"), Is.Not.EqualTo(Color.Parse("#123456")));
        }
        finally
        {
            SyntaxHighlighting.UnregisterCustomTheme(themeName);
        }
    }

    [Test]
    public void CustomThemeName_UsesRegisteredThemeAndRehighlightsWhenChanged()
    {
        const string firstThemeName = "LiveMarkdown.Tests.First";
        const string secondThemeName = "LiveMarkdown.Tests.Second";

        try
        {
            SyntaxHighlighting.RegisterCustomTheme(firstThemeName, new TestRawTheme("#123456"));
            SyntaxHighlighting.RegisterCustomTheme(secondThemeName, new TestRawTheme("#654321"));

            var codeBlock = CreateCodeBlock(firstThemeName);
            Assert.That(GetForeground(codeBlock, "public"), Is.EqualTo(Color.Parse("#123456")));

            codeBlock.CustomColorTheme = secondThemeName;

            Assert.That(GetForeground(codeBlock, "public"), Is.EqualTo(Color.Parse("#654321")));
        }
        finally
        {
            SyntaxHighlighting.UnregisterCustomTheme(firstThemeName);
            SyntaxHighlighting.UnregisterCustomTheme(secondThemeName);
        }
    }

    [Test]
    public void MissingCustomThemeName_FallsBackToColorTheme()
    {
        var customTheme = new TestRawTheme("#123456");
        const string themeName = "LiveMarkdown.Tests.Missing";

        try
        {
            SyntaxHighlighting.RegisterCustomTheme(themeName, customTheme);

            var codeBlock = CreateCodeBlock("LiveMarkdown.Tests.NotRegistered");

            Assert.That(GetForeground(codeBlock, "public"), Is.Not.EqualTo(Color.Parse("#123456")));
        }
        finally
        {
            SyntaxHighlighting.UnregisterCustomTheme(themeName);
        }
    }

    [Test]
    public void RegisteringTheSameNameReplacesTheCachedThemeAndParsesOnce()
    {
        const string themeName = "LiveMarkdown.Tests.Replaced";
        var firstTheme = new TestRawTheme("#123456");
        var secondTheme = new TestRawTheme("#654321");

        try
        {
            SyntaxHighlighting.RegisterCustomTheme(themeName, firstTheme);

            var firstBlock = CreateCodeBlock(themeName);
            var secondBlock = CreateCodeBlock(themeName);

            Assert.That(GetForeground(firstBlock, "public"), Is.EqualTo(Color.Parse("#123456")));
            Assert.That(GetForeground(secondBlock, "public"), Is.EqualTo(Color.Parse("#123456")));
            Assert.That(firstTheme.TokenColorsReadCount, Is.EqualTo(1));

            SyntaxHighlighting.RegisterCustomTheme(themeName, secondTheme);
            var replacedBlock = CreateCodeBlock(themeName);

            Assert.That(GetForeground(replacedBlock, "public"), Is.EqualTo(Color.Parse("#654321")));
            Assert.That(secondTheme.TokenColorsReadCount, Is.EqualTo(1));
        }
        finally
        {
            SyntaxHighlighting.UnregisterCustomTheme(themeName);
        }
    }

    private static CodeBlock CreateCodeBlock(string? customThemeName)
    {
        var codeBlock = new CodeBlock
        {
            Language = "csharp",
            CustomColorTheme = customThemeName,
            Code = "public class Demo {}"
        };

        return codeBlock;
    }

    private static Color? GetForeground(CodeBlock codeBlock, string text)
    {
        foreach (var run in EnumerateRuns(codeBlock.Inlines))
        {
            if (run.Text != text) continue;
            return (run.Foreground as ISolidColorBrush)?.Color;
        }

        Assert.Fail($"The highlighted code did not contain a run with text '{text}'.");
        return null;
    }

    private static IEnumerable<Run> EnumerateRuns(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                yield return run;
            }
            else if (inline is Span span)
            {
                foreach (var nestedRun in EnumerateRuns(span.Inlines))
                    yield return nestedRun;
            }
        }
    }

    private sealed class TestRawTheme : IRawTheme
    {
        private readonly string _foreground;

        public TestRawTheme(string foreground)
        {
            _foreground = foreground;
        }

        public int TokenColorsReadCount { get; private set; }

        public string GetName() => "LiveMarkdown test theme";

        public string GetInclude() => string.Empty;

        public ICollection<IRawThemeSetting> GetSettings() => [];

        public ICollection<IRawThemeSetting> GetTokenColors()
        {
            TokenColorsReadCount++;
            return [new TestRawThemeSetting(_foreground)];
        }

        public ICollection<KeyValuePair<string, object>> GetGuiColors() => [];
    }

    private sealed class TestRawThemeSetting : IRawThemeSetting
    {
        private readonly string _foreground;

        public TestRawThemeSetting(string foreground)
        {
            _foreground = foreground;
        }

        public string GetName() => "keyword";

        public object GetScope() => "storage.modifier.public.cs";

        public IThemeSetting GetSetting() => new TestThemeSetting(_foreground);
    }

    private sealed class TestThemeSetting : IThemeSetting
    {
        private readonly string _foreground;

        public TestThemeSetting(string foreground)
        {
            _foreground = foreground;
        }

        public object GetFontStyle() => string.Empty;

        public string GetBackground() => string.Empty;

        public string GetForeground() => _foreground;
    }
}
