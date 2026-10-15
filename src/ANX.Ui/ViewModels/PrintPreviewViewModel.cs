namespace ANX.Ui.ViewModels;

using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ANX.Control;
using ANX.Core.Reporting;
using ANX.Ui.Services;

/// <summary>
/// **打印预览**窗体（用户 2026-10-15）：打开时**按当前界面语言**生成报表文件（同名静默覆盖），
/// 再用 FlexCel 自带渲染把整张表渲成图显示；上方三个按钮：打印报表 / 定位文件 / 导出PDF（暂置灰）。
///
/// 与执行页那个「打印报表」按钮的分工：那个只生成文件、不预览；这里把"生成 → 看 → 打印"串成一条。
/// 历史页原来的"导出报表（中文）/（英文）"两个按钮合并成了这一个入口。
/// </summary>
public sealed partial class PrintPreviewViewModel : ViewModelBase, IModalDialogViewModel
{
    private readonly IReportExportService _exporter;
    private readonly IReportPreviewRenderer _renderer;
    private string _sheetId = string.Empty;

    public PrintPreviewViewModel(
        ILocalizationService localization,
        IReportExportService exporter,
        IReportPreviewRenderer renderer
    )
        : base(localization)
    {
        _exporter = exporter;
        _renderer = renderer;

        Title = L("Preview.Title");
        Description = L("Preview.Description");
        ExportPdfHint = L("Preview.ExportPdfPending");
    }

    public string Title { get; }

    public string Description { get; }

    /// <summary>「导出PDF」置灰时挂在按钮上的说明（用户 2026-10-15：先置灰、暂不实现）。</summary>
    public string ExportPdfHint { get; }

    /// <summary>整张表渲出来的图；没生成好之前是 null（界面那里就是空的）。</summary>
    [ObservableProperty]
    private ImageSource? _preview;

    /// <summary>这次生成的报表文件路径（「定位文件」用它）。</summary>
    [ObservableProperty]
    private string _reportPath = string.Empty;

    /// <inheritdoc />
    public event EventHandler<bool>? CloseRequested;

    /// <summary>打开前定死"预览哪张试验单"（历史页知道用户选了哪一行）。</summary>
    public void PrepareFor(string sheetId) => _sheetId = sheetId;

    public override void OnOpened() => _ = BuildAsync();

    /// <summary>关窗（页面宿主转发给弹窗）。</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, false);

    /// <summary>
    /// 生成报表 + 渲染预览。报表**在窗体打开时**生成、**同名静默覆盖**（用户 2026-10-15），
    /// 语种跟**当前界面语言**走 —— 所以历史页不再给中/英两个按钮。
    /// </summary>
    private async Task BuildAsync()
    {
        if (string.IsNullOrWhiteSpace(_sheetId))
        {
            SetStatus(L("Preview.NoSheet"), isError: true);
            return;
        }

        ReportPath = string.Empty;
        Preview = null;
        SetStatus(L("Preview.Rendering"));

        try
        {
            var path = await _exporter.ExportAsync(_sheetId, Localization.CurrentCulture);
            ReportPath = path;

            // 渲一张 1500×5000 量级的位图是重活，扔线程池跑；**不能在这里 ConfigureAwait(false)** ——
            // 回来要写绑定属性，必须在 UI 线程上（2026-10-14 踩过：断言后集合直接打挂界面）
            var png = await Task.Run(() => _renderer.RenderPng(path));
            Preview = ToImageSource(png);

            SetStatus(string.Format(CultureInfo.CurrentCulture, L("Preview.Created"), path));
        }
        catch (Exception ex)
        {
            SetStatus($"{L("Preview.Failed")}: {ex.Message}", isError: true);
        }
    }

    /// <summary>打印：把预览图按宽度铺满可打印区、纵向切页交给打印队列（见 <see cref="ReportPrinter"/>）。</summary>
    [RelayCommand]
    private void Print()
    {
        if (Preview is not BitmapSource bitmap)
        {
            SetStatus(L("Preview.NothingToPrint"), isError: true);
            return;
        }

        var documentName = string.IsNullOrWhiteSpace(_sheetId) ? "ANX" : _sheetId;

        try
        {
            if (ReportPrinter.Print(bitmap, documentName))
            {
                SetStatus(string.Format(CultureInfo.CurrentCulture, L("Preview.Printed"), documentName));
            }
        }
        catch (Exception ex)
        {
            SetStatus($"{L("Preview.PrintFailed")}: {ex.Message}", isError: true);
        }
    }

    /// <summary>在资源管理器里选中刚生成的那份报表。</summary>
    [RelayCommand]
    private void Locate()
    {
        if (string.IsNullOrWhiteSpace(ReportPath) || !File.Exists(ReportPath))
        {
            SetStatus(L("Preview.NothingToLocate"), isError: true);
            return;
        }

        try
        {
            _ = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{ReportPath}\"")
                {
                    UseShellExecute = true,
                }
            );
        }
        catch (Exception ex)
        {
            SetStatus($"{L("Preview.LocateFailed")}: {ex.Message}", isError: true);
        }
    }

    /// <summary>PNG 字节 → 冻结过的位图（冻结后才能跨线程/长期持有）。</summary>
    private static ImageSource ToImageSource(byte[] png)
    {
        using var stream = new MemoryStream(png);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // 立刻读进内存，别留着流
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        return image;
    }
}
