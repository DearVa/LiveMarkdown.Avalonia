using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Svg;
using Avalonia.Threading;
using Svg.Model;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Decoder for parsing SVG streams into SvgImage objects.
/// </summary>
public class SvgImageDecoder : IImageDecoder
{
    /// <summary>
    /// A shared instance of the SvgImageDecoder for reuse across the application.
    /// </summary>
    public static SvgImageDecoder Shared { get; } = new();

    private SvgImageDecoder() { }

    /// <inheritdoc />
    public async Task<IImage?> TryDecodeAsync(Image target, Stream stream, Uri uri, CancellationToken cancellationToken)
    {
        // Fast-fail check: If it's pure binary (contains null bytes in the header), it's likely a Bitmap, skip SVG parsing.
        if (!await IsLikelySvgAsync(uri, stream, cancellationToken))
        {
            return null;
        }

        try
        {
            stream.Position = 0;
            var parameters = new SvgParameters
            {
                Css = await Dispatcher.UIThread.InvokeAsync(() => target.GetValue(SvgImageExtension.CssProperty))
            };

            cancellationToken.ThrowIfCancellationRequested();
            var svgSource = SvgSource.Load(stream, parameters);

            cancellationToken.ThrowIfCancellationRequested();
            return await Dispatcher.UIThread.InvokeAsync(() => new SvgImage { Source = svgSource });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Parsing failed (e.g., malformed SVG), let the next decoder try
        }

        return null;
    }

    private static async ValueTask<bool> IsLikelySvgAsync(Uri uri, Stream stream, CancellationToken cancellationToken)
    {
        if (uri.ToString().EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return true;

        var buffer = new byte[32];
#if NETSTANDARD2_0
        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
#else
        var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
#endif

        if (bytesRead == 0) return false;

        // Check for null bytes which strongly indicate non-text binary data (like PNG/JPG)
        return !buffer.AsSpan(0, bytesRead).Contains((byte)0);
    }
}