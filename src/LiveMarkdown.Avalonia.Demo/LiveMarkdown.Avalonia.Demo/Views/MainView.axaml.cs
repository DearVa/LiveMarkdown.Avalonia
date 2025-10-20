using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveMarkdown.Avalonia.Demo.ViewModels;

namespace LiveMarkdown.Avalonia.Demo.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();

            _ = new AutoScrollHelper(RawMarkdownTextBlockScrollViewer);
            _ = new AutoScrollHelper(MarkdownRendererScrollViewer);
        }


        private void HandleClearButtonClick(object? sender, RoutedEventArgs e) => (this.DataContext as MainViewModel)?.ClearMarkdown();
    }

    public class AutoScrollHelper
    {
        private bool isAtEnd = true;

        public AutoScrollHelper(ScrollViewer scrollViewer)
        {
            scrollViewer.PropertyChanged += OnScrollViewerPropertyChanged;
        }

        private void OnScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer) return;

            if (e.Property != ScrollViewer.OffsetProperty &&
                e.Property != ScrollViewer.ViewportProperty &&
                e.Property != ScrollViewer.ExtentProperty) return;

            if (e.Property == ScrollViewer.OffsetProperty)
            {
                isAtEnd = ((Vector)e.NewValue!).Y >= scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
            }

            if (isAtEnd)
            {
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, double.PositiveInfinity);
            }
        }
    }
}