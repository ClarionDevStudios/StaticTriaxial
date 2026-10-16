namespace STS.Method.ShearStrength.Tests;

using STS.Control;
using STS.Core;

/// <summary>
/// 试验单 / 试样信息实装（2026-10-16 第一批）：建单、试件号递增、试样信息存取与默认值回落、
/// 列表行口径、缺尺寸硬校验闸口、格式校验带键报错。临时 SQLite 库驱动，全程确定性。
/// </summary>
public sealed class ShearStrengthSampleInfoTests : IDisposable
{
    private readonly TempSheetDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task 建单与试件号应按现役口径走()
    {
        var execution = _database.CreateExecution();

        var sheetId = await execution.CreateSheetAsync("G15 高速 K12+300", "WT");

        Assert.StartsWith("WT", sheetId, StringComparison.Ordinal);
        Assert.Equal("S001", await execution.NextSampleIdAsync(sheetId));

        await execution.SaveSampleInfoAsync(sheetId, "S001", Size("306", "100"));
        Assert.Equal("S002", await execution.NextSampleIdAsync(sheetId));

        Assert.True(await execution.DeleteSampleAsync(sheetId, "S001"));
        Assert.False(await execution.DeleteSampleAsync(sheetId, "S001")); // 第二次删不到
        Assert.Equal("S001", await execution.NextSampleIdAsync(sheetId));
    }

    [Fact]
    public async Task 试样信息应能存取_留空与未填回落默认值()
    {
        var execution = _database.CreateExecution();
        var sheetId = await execution.CreateSheetAsync("项目", "WT");

        var fields = Size("305.5", "100.2");
        fields[ShearStrengthSampleFields.ConfiningPressureKey] = "60";
        fields[ShearStrengthSampleFields.SampleDescKey] = "级配碎石/第二组";
        fields[ShearStrengthSampleFields.MassKey] = ""; // 试验后才有值：留空

        await execution.SaveSampleInfoAsync(sheetId, "S001", fields);

        var info = await execution.LoadSampleInfoAsync(sheetId, "S001");

        Assert.Equal("60", info[ShearStrengthSampleFields.ConfiningPressureKey]);
        Assert.Equal("305.5", info[ShearStrengthSampleFields.InitialHeightKey]);
        Assert.Equal("100.2", info[ShearStrengthSampleFields.InitialDiameterKey]);
        Assert.Equal("级配碎石/第二组", info[ShearStrengthSampleFields.SampleDescKey]);
        Assert.Equal("", info[ShearStrengthSampleFields.MassKey]); // 留空保持空
        Assert.Equal("", info[ShearStrengthSampleFields.WaterContentKey]); // 从未填 → 默认空
    }

    [Fact]
    public async Task 列表行应带完成状态与判定值口径()
    {
        var execution = _database.CreateExecution();
        var sheetId = await execution.CreateSheetAsync("项目", "WT");
        await execution.SaveSampleInfoAsync(sheetId, "S001", Size("306", "100"));

        var rows = await execution.ReadSampleRowsAsync(sheetId);

        var row = Assert.Single(rows);
        Assert.Equal("S001", row.SampleId);
        Assert.False(row.IsCompleted); // 还没跑试验
        Assert.False(row.HasResult); // 判定值还没有
        Assert.Equal("306", row.Fields[ShearStrengthSampleFields.InitialHeightKey]);
        Assert.Equal("100", row.Fields[ShearStrengthSampleFields.InitialDiameterKey]);
    }

    [Fact]
    public async Task 缺尺寸时开始试验应带键报错_有尺寸后按件装配判据与速率()
    {
        var execution = _database.CreateExecution();
        var sheetId = await execution.CreateSheetAsync("项目", "WT");

        // 尺寸还没填：硬校验报错（界面按资源键出提示；设计文档待办①的闸口）
        var missing = await Assert.ThrowsAsync<LocalizedException>(() =>
            execution.LoadProfileAsync(new TestStartContext(sheetId, "S001"))
        );
        Assert.Equal(ShearStrengthMessageKeys.MissingSpecimenSize, missing.Messages[0].ResourceKey);

        await execution.SaveSampleInfoAsync(sheetId, "S001", Size("306", "100"));

        var profile = await execution.LoadProfileAsync(new TestStartContext(sheetId, "S001"));

        Assert.NotNull(profile.StopCriteriaFactory); // 判据按逐件尺寸构造
        Assert.Equal(4.59, profile.LoadRateMillimetersPerMinute, 1e-9); // 1.5%/min × 306mm
        Assert.Equal(4.59, profile.ApproachSpeedMillimetersPerMinute, 1e-9);
        Assert.Empty(profile.StartNotes); // 306 在合格窗内：无提示

        // 超窗（312 > 306±5）→ 不拦，但带一条"按真实数据继续"的提示（Q5 口径）
        await execution.SaveSampleInfoAsync(sheetId, "S002", Size("312", "100"));
        var outOfWindow = await execution.LoadProfileAsync(new TestStartContext(sheetId, "S002"));
        var note = Assert.Single(outOfWindow.StartNotes);
        Assert.Equal(ShearStrengthMessageKeys.HeightOutOfWindow, note.ResourceKey);
    }

    [Fact]
    public async Task 试样信息格式不对应带键报错()
    {
        var execution = _database.CreateExecution();
        var sheetId = await execution.CreateSheetAsync("项目", "WT");

        var exception = await Assert.ThrowsAsync<LocalizedException>(() =>
            execution.SaveSampleInfoAsync(sheetId, "S001", Size("abc", "100"))
        );

        Assert.Equal(ShearStrengthMessageKeys.FieldFormat, exception.Messages[0].ResourceKey);
    }

    private static Dictionary<string, string> Size(string height, string diameter) =>
        new(StringComparer.Ordinal)
        {
            [ShearStrengthSampleFields.InitialHeightKey] = height,
            [ShearStrengthSampleFields.InitialDiameterKey] = diameter,
        };
}
