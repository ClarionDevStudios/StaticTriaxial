namespace ANX.Ui.Services;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/// <summary>
/// 把"整张报表位图"按系统打印机的可打印区**宽度铺满、纵向切成多页**打出去（用户 2026-10-15：
/// 走 WPF 打印，不依赖装没装 Excel）。
///
/// 为什么要自己切页：WPF 的 <c>PrintVisual</c> 一次只出一页，而我们手上是一整张长图
/// （18 件的报表渲出来 1500×5300 像素），交给它会被压成一页、小到看不清。
/// </summary>
internal static class ReportPrinter
{
    /// <summary>弹系统打印对话框并打印；返回 <c>false</c> 表示用户取消。</summary>
    public static bool Print(BitmapSource image, string documentName)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        dialog.PrintDocument(
            new ImagePaginator(image, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight),
            documentName
        );

        return true;
    }

    /// <summary>按"宽度铺满可打印区、纵向切页"排版的 <see cref="DocumentPaginator"/>。</summary>
    private sealed class ImagePaginator : DocumentPaginator
    {
        private readonly BitmapSource _image;
        private readonly Size _pageSize;
        private readonly double _scale;
        private readonly int _rowsPerPage;

        public ImagePaginator(BitmapSource image, double pageWidth, double pageHeight)
        {
            _image = image;
            _pageSize = new Size(pageWidth, pageHeight);
            _scale = pageWidth / image.PixelWidth;
            _rowsPerPage = Math.Max(1, (int)Math.Floor(pageHeight / _scale));
        }

        public override bool IsPageCountValid => true;

        public override int PageCount => (int)Math.Ceiling(_image.PixelHeight / (double)_rowsPerPage);

        public override Size PageSize
        {
            get => _pageSize;
            set => throw new NotSupportedException("页面尺寸由打印机的可打印区决定");
        }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            var top = pageNumber * _rowsPerPage;
            var height = Math.Min(_rowsPerPage, _image.PixelHeight - top);
            if (height <= 0)
            {
                return DocumentPage.Missing;
            }

            var slice = new CroppedBitmap(_image, new Int32Rect(0, top, _image.PixelWidth, height));

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(slice, new Rect(0, 0, _image.PixelWidth * _scale, height * _scale));
            }

            return new DocumentPage(visual);
        }
    }
}
