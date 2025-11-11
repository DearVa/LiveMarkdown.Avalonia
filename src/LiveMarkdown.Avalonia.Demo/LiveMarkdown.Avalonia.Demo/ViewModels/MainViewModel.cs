using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Reflection;

namespace LiveMarkdown.Avalonia.Demo.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public ObservableStringBuilder MarkdownBuilder { get; } = new();

    public ObservableCollection<string> MarkdownList { get; } = [];

    [ObservableProperty]
    public partial string? RawMarkdownText { get; private set; }

    [ObservableProperty]
    public partial double RenderSpeed { get; set; } = 30d;

    public string? SelectedMarkdown
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;

            _ = RenderMarkdownAsync(value);
        }
    }

    private CancellationTokenSource? cancellationTokenSource;

    public MainViewModel()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".md"));

        foreach (var resource in resourceNames)
        {
            var fileName = Path.GetFileNameWithoutExtension(resource);
            MarkdownList.Add(fileName);
        }
    }

    [RelayCommand]
    private async Task OpenUriAsync(InlineHyperlinkClickedEventArgs args)
    {
        if (args.HRef is { IsAbsoluteUri: true, Scheme: "http" or "https" } url)
        {
            await LaunchUriAsync(url);
        }
    }

    [RelayCommand]
    public void ClearMarkdown()
    {
        RawMarkdownText = string.Empty;
        MarkdownBuilder.Clear();
    }

    private async Task RenderMarkdownAsync(string? markdownFileName)
    {
        try
        {
            if (cancellationTokenSource is not null)
                await cancellationTokenSource.CancelAsync();

            if (string.IsNullOrWhiteSpace(markdownFileName))
                return;

            ClearMarkdown();

            cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($"{markdownFileName}.md", StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
            {
                await Console.Error.WriteLineAsync($"Resource not found: {markdownFileName}.md");
                return;
            }

            string markdownText;
            await using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream ?? throw new InvalidOperationException("Failed to load resource stream.")))
            {
                markdownText = await reader.ReadToEndAsync(cancellationToken);
            }

            var cursor = 0;
            while (!cancellationToken.IsCancellationRequested && cursor < markdownText.Length)
            {
                var speed = (int)RenderSpeed;
                var newText = markdownText.Substring(cursor, Math.Min(speed, markdownText.Length - cursor));
                cursor += speed;

                RawMarkdownText += newText;
                MarkdownBuilder.Append(newText);

                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error rendering markdown: {ex.Message}");
        }
    }

    private async static Task LaunchUriAsync(Uri uri)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            var launcher = TopLevel.GetTopLevel(window)?.Launcher;
            if (launcher is not null)
            {
                await launcher.LaunchUriAsync(uri);
            }
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime { MainView: { } mainView })
        {
            var launcher = TopLevel.GetTopLevel(mainView)?.Launcher;
            if (launcher is not null)
            {
                await launcher.LaunchUriAsync(uri);
            }
        }
    }
}