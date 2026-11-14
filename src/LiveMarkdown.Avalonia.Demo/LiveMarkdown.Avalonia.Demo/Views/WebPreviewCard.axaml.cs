using Avalonia.Controls;
using Avalonia.Media.Imaging;
using LiveMarkdown.Avalonia.Demo.Links.Preview;

namespace LiveMarkdown.Avalonia.Demo.Views;

internal partial class WebPreviewCard : UserControl, IDisposable
{
    private Bitmap? _cover;
    private Bitmap? _icon;
    private WebPreview? _shown;

    public WebPreviewCard()
    {
        InitializeComponent();
    }

    public void ShowAddress(Uri address) => SiteAddress.Text = address.IdnHost;

    public void Show(WebPreview preview)
    {
        SetText(PageTitle, preview.Title);
        SetText(PageDescription, preview.Description);
        SetText(StatusNote, preview.Note);
        DetailsText.IsVisible = PageTitle.IsVisible || PageDescription.IsVisible || StatusNote.IsVisible;
        if (!ReferenceEquals(_shown?.Cover, preview.Cover))
        {
            _cover = SetImage(CoverImage, _cover, preview.Cover);
        }
        if (!ReferenceEquals(_shown?.Icon, preview.Icon))
        {
            _icon = SetImage(SiteIcon, _icon, preview.Icon);
        }
        _shown = preview;
        DefaultIcon.IsVisible = _icon is null;
        Details.IsVisible = CoverImage.IsVisible || DetailsText.IsVisible;
    }

    public void Dispose()
    {
        _cover = SetImage(CoverImage, _cover, null);
        _icon = SetImage(SiteIcon, _icon, null);
        _shown = null;
    }

    private static void SetText(TextBlock text, string? value)
    {
        text.Text = value;
        text.IsVisible = value is not null;
    }

    private static Bitmap? SetImage(Image image, Bitmap? previous, byte[]? bytes)
    {
        image.Source = null;
        previous?.Dispose();
        Bitmap? bitmap = null;
        if (bytes is not null)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            bitmap = new Bitmap(stream);
        }

        image.Source = bitmap;
        image.IsVisible = bitmap is not null;
        return bitmap;
    }
}
