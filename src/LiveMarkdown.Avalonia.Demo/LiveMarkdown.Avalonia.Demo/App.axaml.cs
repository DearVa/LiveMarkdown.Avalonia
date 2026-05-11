using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Markup.Xaml;
using LiveMarkdown.Avalonia.Demo.Views;

namespace LiveMarkdown.Avalonia.Demo;

public partial class App : Application, ILogSink
{
    public override void Initialize()
    {
        Logger.Sink = this;

        MarkdownRenderer.ConfigurePipeline += x => x.UseMermaid();
        MarkdownNode.Edit(builder => builder
            .Register<MathInlineNode>()
            .Register<MathBlockNode>()
            .Register<MermaidBlockNode>()
        );

        AsyncImageLoader.DefaultDecoders =
        [
            SvgImageDecoder.Shared,
            DefaultBitmapDecoder.Shared
        ];

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow();
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                singleViewPlatform.MainView = new MainView();
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

    public bool IsEnabled(LogEventLevel level, string area) => area == nameof(MarkdownRenderer);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        Console.WriteLine($"[{source}] {messageTemplate}");
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        var index = 0;
        var formattedMessage = LogTemplateRegex().Replace(
            messageTemplate,
            match => propertyValues.Length > index ? propertyValues[index++]?.ToString() ?? string.Empty : match.Value);
        Log(level, area, source, formattedMessage);
    }

    [GeneratedRegex(@"\{[^\}]+\}")]
    private static partial Regex LogTemplateRegex();
}