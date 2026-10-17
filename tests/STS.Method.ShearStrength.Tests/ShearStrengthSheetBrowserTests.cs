namespace STS.Method.ShearStrength.Tests;

using STS.Core;
using STS.Storage;

/// <summary>
/// 历史数据页 / 试验单弹窗的数据来源（用户 2026-10-17 报的三个问题都指向它：历史页空、
/// 「上次试验项目」不可用、「已有试验单」列表为空）。这里钉住：浏览器要真的从库里查单、读明细、读曲线，
/// 且试件列给的是**资源键**（界面按当前语言取）、第一列是试件号（历史页拿它当列表标题）。
/// </summary>
public sealed class ShearStrengthSheetBrowserTests : IDisposable
{
    private readonly TempSheetDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task 查单应回本方法的试验单_新的在前_并支持关键字过滤()
    {
        var service = _database.CreateService();
        var execution = new ShearStrengthExecution(service);
        var browser = new ShearStrengthSheetBrowser(service);

        var older = await execution.CreateSheetAsync("项目甲", "WT");
        var newer = await execution.CreateSheetAsync("项目乙", "WT");
        await service.AppendSampleAsync(older, "S001", [], Completed(30, 130));

        var all = await browser.QueryAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal(newer, all[0].SheetId); // 创建时间倒序：最新的在最上面
        Assert.Equal(1, all.Single(sheet => sheet.SheetId == older).SampleCount);

        var filtered = Assert.Single(await browser.QueryAsync("乙"));
        Assert.Equal(newer, filtered.SheetId);

        Assert.Empty(await browser.QueryAsync("不存在的关键字"));
    }

    [Fact]
    public async Task 读试件应第一列是试件号_列名给资源键_并且能读回曲线()
    {
        var service = _database.CreateService();
        var execution = new ShearStrengthExecution(service);
        var browser = new ShearStrengthSheetBrowser(service);

        var sheetId = await execution.CreateSheetAsync("项目", "WT");
        var curve = new CurvePoint[] { new(0, 0), new(0.5, 1.25), new(1.0, 2.5) };
        await service.AppendSampleAsync(
            sheetId,
            "S001",
            curve,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["ConfiningPressure"] = 30,
                ["InitialHeight"] = 306,
                ["InitialDiameter"] = 100,
                ["MaxAxialLoad"] = 12345.6,
                ["ShearStrength"] = 130,
                ["TestStatus"] = 1,
            }
        );

        var row = Assert.Single(await browser.ReadSamplesAsync(sheetId));
        var first = row.First();

        // 第一列必须是试件号：历史页把第一列当列表标题、其余列做字段表
        Assert.Equal(ShearStrengthMetricKeys.SampleId, first.Key);
        Assert.Equal("S001", first.Value);

        // 列名是**资源键**（界面 L(key) 取当前语言），不是库里的列名
        Assert.Equal("12345.6", row[ShearStrengthMetricKeys.PeakLoad]);
        Assert.Equal(
            "130",
            row[ShearStrengthMetricKeys.ShearStrength]
        );
        Assert.Equal(
            "30",
            row[ShearStrengthSampleFields.NameKey(ShearStrengthSampleFields.ConfiningPressureKey)]
        );

        var stored = await browser.ReadCurveAsync(sheetId, "S001");
        Assert.Equal(3, stored.Count);
        Assert.Equal(1.0, stored[^1].X, 1e-9);
        Assert.Equal(2.5, stored[^1].Y, 1e-9);

        // 本方法不做原点修正拟合：历史回放没有标注可画
        Assert.Null(await browser.ReadAnnotationAsync(sheetId, "S001"));
    }

    private static Dictionary<string, double> Completed(double sigma3, double strength) =>
        new(StringComparer.Ordinal)
        {
            ["ConfiningPressure"] = sigma3,
            ["ShearStrength"] = strength,
            ["TestStatus"] = 1,
        };
}
