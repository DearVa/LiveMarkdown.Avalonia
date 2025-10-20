using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using LiveMarkdown.Avalonia.Demo;
using System.Runtime.Versioning;
using System.Threading.Tasks;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
        .With(new FontManagerOptions
        {
            FontFallbacks = new[]
        {
            new FontFallback
            {
                FontFamily = new FontFamily("avares://LiveMarkdown.Avalonia.Demo/Assets/Fonts/NotoSansSC-Regular.ttf#Noto Sans SC")
            }
         }
        });
}