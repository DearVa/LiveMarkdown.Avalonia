using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media.Imaging;
using Lucide.Avalonia;

namespace LiveMarkdown.Avalonia.Demo.Links.Preview;

/// <summary>
/// Adds only decorative inlines. Icon size is fixed so a downloaded favicon does not reflow the link label.
/// </summary>
internal sealed class LinkPreviewIcons : IDisposable
{
    private readonly Dictionary<Link, Decoration> _decorations = [];
    private readonly IDisposable _registration;

    public LinkPreviewIcons(MarkdownRenderer renderer)
    {
        _registration = renderer.NodeHooks.Register<LinkInlineNode>(
            initialize: context =>
            {
                if (context.Node.Inline is Link link)
                {
                    _decorations.Add(link, new Decoration());
                }
            },
            beforeUpdate: context =>
            {
                if (context.Node.Inline is Link link && _decorations.TryGetValue(link, out var decoration))
                {
                    link.Inlines.Remove(decoration.Inline);
                }
            },
            afterUpdate: context =>
            {
                if (context.Node.Inline is not Link link || !_decorations.TryGetValue(link, out var decoration) ||
                    link.Image is not null || link.HRef is not { } uri)
                {
                    return;
                }

                // Restore the stable decoration after indexed child reconciliation, even on a retained node.
                var target = DemoLinkTarget.Parse(uri.OriginalString);
                if (decoration.Target != target)
                {
                    decoration.Reset(target);
                }

                if (target.Kind != DemoLinkKind.Unsupported)
                {
                    link.Inlines.Insert(0, decoration.Inline);
                }
            },
            cleanup: context =>
            {
                if (context.Node.Inline is Link link && _decorations.Remove(link, out var decoration))
                {
                    link.Inlines.Remove(decoration.Inline);
                    decoration.Dispose();
                }
            });
    }

    public void SetFavicon(DemoLinkTarget target, byte[] bytes)
    {
        foreach (var decoration in _decorations.Values)
        {
            if (decoration.Target == target)
            {
                decoration.SetFavicon(bytes);
            }
        }
    }

    public void Dispose() => _registration.Dispose();

    private static LucideIconKind FileKind(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".txt" or ".md" or ".log" or ".pdf" => LucideIconKind.FileText,
            ".cmd" or ".ps1" or ".sh" => LucideIconKind.FileCode,
            ".png" or ".jpg" or ".webp" or ".gif" => LucideIconKind.FileImage,
            _ => LucideIconKind.File,
        };
    }

    private sealed class Decoration : IDisposable
    {
        public InlineUIContainer Inline { get; }
        public DemoLinkTarget? Target { get; private set; }

        private readonly Panel _holder = new() { Width = 14, Height = 14, Margin = new Thickness(0, 0, 4, 0), IsHitTestVisible = false };
        private Bitmap? _bitmap;
        private byte[]? _iconBytes;

        public Decoration()
        {
            Inline = new InlineUIContainer(_holder);
        }

        public void Reset(DemoLinkTarget target)
        {
            Dispose();
            Target = target;
            _holder.Children.Add(new LucideIcon
            {
                Kind = target.Kind == DemoLinkKind.Web ? LucideIconKind.Globe : FileKind(target.Address),
                Size = 13,
                Opacity = 0.75,
            });
        }

        public void SetFavicon(byte[] bytes)
        {
            if (ReferenceEquals(_iconBytes, bytes))
            {
                return;
            }

            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new Bitmap(stream);
            Dispose();
            _bitmap = bitmap;
            _iconBytes = bytes;
            _holder.Children.Add(new Image { Source = bitmap });
        }

        public void Dispose()
        {
            _holder.Children.Clear();
            _bitmap?.Dispose();
            _bitmap = null;
            _iconBytes = null;
        }
    }
}
