using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;

namespace LiveMarkdown.Avalonia.Demo.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private CancellationTokenSource? cancellationTokenSource;
        public ObservableStringBuilder MarkdownBuilder { get; } = new();
        [ObservableProperty]
        private ObservableCollection<string> _markdownList = new ObservableCollection<string>();

        [ObservableProperty]
        private string? _rawMarkdownTextBlock;
        [ObservableProperty]
        private double _renderSpeedSlider = 30;

        private string? _selectedMarkdown;
        public string? SelectedMarkdown
        {
            get => _selectedMarkdown;
            set
            {
                if (!Equals(_selectedMarkdown, value))
                {
                    _selectedMarkdown = value;
                    _ = RenderMarkdownAsync(value);
                    OnPropertyChanged(nameof(SelectedMarkdown));
                }
            }
        }
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
        public void ClearMarkdown()
        {
            RawMarkdownTextBlock = string.Empty;
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
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                using (var reader = new StreamReader(stream!))
                {
                    markdownText = await reader.ReadToEndAsync(cancellationToken);
                }

                var cursor = 0;
                while (!cancellationToken.IsCancellationRequested && cursor < markdownText.Length)
                {
                    var speed = (int)RenderSpeedSlider;
                    var newText = markdownText.Substring(cursor, Math.Min(speed, markdownText.Length - cursor));
                    cursor += speed;

                    RawMarkdownTextBlock += newText;
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
        private async Task LaunchUriAsync(Uri uri)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
            {
                var launcher = TopLevel.GetTopLevel(window)?.Launcher;
                if (launcher is { })
                {
                    await launcher.LaunchUriAsync(uri);
                }
            }

            if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime { MainView: { } mainView })
            {
                var launcher = TopLevel.GetTopLevel(mainView)?.Launcher;
                if (launcher is { })
                {
                    await launcher.LaunchUriAsync(uri);
                }
            }
        }
    }
}
