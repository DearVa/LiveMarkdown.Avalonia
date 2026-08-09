using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Markdig.Syntax.Inlines;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A node that represents a link inline.
/// </summary>
public class LinkInlineNode() : InlinesNode<LinkInline>(new Link())
{
    /// <summary>
    /// Updates the link target, image source, and child inline content.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="linkInline">The Markdig link inline.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> when the link remains valid.</returns>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        LinkInline linkInline,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        if (linkInline.Url == null) return false;

        var inlineHyperlink = Unsafe.As<Link>(Inline);
        Uri.TryCreate(linkInline.Url, UriKind.RelativeOrAbsolute, out var uri);

        if (linkInline.IsImage)
        {
            Image img;
            if (inlineHyperlink.Image is { } image)
            {
                img = image;
            }
            else
            {
                inlineHyperlink.Image = img = new Image
                {
                    Classes = { "Link" },
                };
            }

            if (uri is { IsAbsoluteUri: false })
            {
                if (documentNode.Owner.ImageBasePath is { } imageBasePath)
                {
                    // If the URL is a relative path, combine it with the base path
                    Uri.TryCreate(Path.GetFullPath(Path.Combine(imageBasePath, linkInline.Url)), UriKind.Absolute, out uri);
                }
                else
                {
                    // If no base path is set, set the URI to null, preventing unexpected behavior
                    uri = null;
                }
            }

            inlineHyperlink.HRef = uri;
            AsyncImageLoader.SetSource(img, uri?.ToString());
        }
        else
        {
            inlineHyperlink.HRef = uri;
            inlineHyperlink.Image = null;
            base.UpdateCore(documentNode, linkInline, change, cancellationToken);
        }

        return true;
    }
}