namespace STS.Storage.Tests;

using STS.Core;
using STS.Method.ShearStrength;
using Microsoft.Data.Sqlite;

/// <summary>试验单完整生命周期：建单 → 加试件（含曲线）→ 写组统计 → 完成 → 查回。</summary>
public sealed class TestSheetServiceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"sts-sheet-{Guid.NewGuid():N}.db"
    );

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task 完整生命周期应能建单加试件写组统计并查回曲线()
    {
        var service = CreateService();

        var sheetId = await service.CreateAsync("ShearStrength", "G15 高速 K12+300", "WT");
        Assert.StartsWith("WT", sheetId, StringComparison.Ordinal);

        var curve = Enumerable
            .Range(0, 20)
            .Select(i => new CurvePoint(i * 0.1, i * 0.5))
            .ToArray();

        await service.AppendSampleAsync(sheetId, "1", curve, Sample(strength: 73.47));
        await service.AppendSampleAsync(sheetId, "2", curve, Sample(strength: 74.10));

        await service.UpdateMainFieldsAsync(
            sheetId,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["CohesionKPa"] = 11.55,
                ["FrictionAngleDeg"] = 30.0,
                ["EnvelopeCorrelation"] = 0.995,
            }
        );
        await service.CompleteAsync(sheetId);

        var sheets = await service.QueryAsync("ShearStrength");
        Assert.Single(sheets);
        Assert.Equal(sheetId, sheets[0].SheetId);
        Assert.Equal("G15 高速 K12+300", sheets[0].ProjectName);
        Assert.Equal(2, sheets[0].SampleCount);

        // 曲线原样回放
        var restored = service.ReadCurve(sheetId, "1");
        Assert.Equal(curve.Length, restored.Count);
        Assert.Equal(curve[5], restored[5]);
    }

    [Fact]
    public async Task 写未声明的列应报错而不是静默忽略()
    {
        var service = CreateService();
        var sheetId = await service.CreateAsync("ShearStrength", "项目", "WT");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AppendSampleAsync(
                sheetId,
                "1",
                [],
                new Dictionary<string, double>(StringComparer.Ordinal) { ["FlexualStrength"] = 1 }
            )
        );

        Assert.Contains("FlexualStrength", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 曲线JSON应能往返且足够紧凑()
    {
        var curve = Enumerable
            .Range(0, 30000)
            .Select(i => new CurvePoint(i * 0.02, Math.Sin(i * 0.01) * 5))
            .ToArray();

        var json = CurveJson.Encode(curve);

        Assert.StartsWith("[[0,0],[", json, StringComparison.Ordinal);
        var restored = CurveJson.Decode(json);
        Assert.Equal(curve.Length, restored.Count);

        // 序列化按 3 位小数（位移 0.001mm），因此是近似无损：往返误差不超过半个最小刻度
        Assert.Equal(curve[1234].X, restored[1234].X, 5e-4);
        Assert.Equal(curve[1234].Y, restored[1234].Y, 5e-4);
        Assert.True(json.Length < 700_000, $"3 万点应保持紧凑，实际 {json.Length} 字符");

        Assert.Empty(CurveJson.Decode(null));
        Assert.Empty(CurveJson.Decode("[]"));
    }

    /// <summary>
    /// 明细第一次落地的另一条路径（只填试样信息、还没跑试验）也要带 UUID ——
    /// 否则"填了尺寸还没做试验"的试件上传云端时没有归属标识。
    /// </summary>
    [Fact]
    public async Task 只填试样信息时明细也要带所属单的UUID()
    {
        var service = CreateService();
        var sheetId = await service.CreateAsync("ShearStrength", "项目", "WT");

        await service.UpsertSampleInfoAsync(
            sheetId,
            "1",
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["InitialDiameter"] = 150,
                ["InitialHeight"] = 306,
            }
        );

        var mainUuid = Convert.ToString(service.ReadMainRow(sheetId, ["UUID"])["UUID"]);
        var sample = Convert.ToString(service.ReadDetailRow(sheetId, "1", ["UUID"])!["UUID"]);

        Assert.False(string.IsNullOrWhiteSpace(mainUuid));
        Assert.Equal(mainUuid, sample);
    }

    /// <summary>
    /// 试样信息**可以带文本字段**（用户 2026-10-16：扩框架支持文本列，现役的"试样描述"要用）——
    /// 数值照样进数值列、文本进文本列、空串存 NULL；读回来原样。
    /// </summary>
    [Fact]
    public async Task 试样信息支持文本字段()
    {
        var service = CreateService();
        var sheetId = await service.CreateAsync("ShearStrength", "项目", "WT");

        await service.UpsertSampleInfoAsync(
            sheetId,
            "S001",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["SampleDesc"] = "第三批 1 号件（边角有磕碰）",
                ["InitialDiameter"] = 150.0,
                ["InitialHeight"] = 305.5,
                ["InvalidReason"] = null, // 空值 = 清掉该列（NULL）
            }
        );

        var row = service.ReadDetailRow(
            sheetId,
            "S001",
            ["SampleDesc", "InitialDiameter", "InitialHeight", "InvalidReason"]
        )!;

        Assert.Equal("第三批 1 号件（边角有磕碰）", Convert.ToString(row["SampleDesc"]));
        Assert.Equal(150.0, Convert.ToDouble(row["InitialDiameter"]), 6);
        Assert.Equal(305.5, Convert.ToDouble(row["InitialHeight"]), 6);
        Assert.True(row["InvalidReason"] is null or DBNull);

        // 更新路径同样支持（再存一次，文本换掉、数值换掉）
        await service.UpsertSampleInfoAsync(
            sheetId,
            "S001",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["SampleDesc"] = "第三批 1 号件（复测）",
                ["InitialDiameter"] = 150.5,
            }
        );

        var updated = service.ReadDetailRow(sheetId, "S001", ["SampleDesc", "InitialDiameter"])!;
        Assert.Equal("第三批 1 号件（复测）", Convert.ToString(updated["SampleDesc"]));
        Assert.Equal(150.5, Convert.ToDouble(updated["InitialDiameter"]), 6);
    }

    /// <summary>
    /// 明细按 `(SheetID, SampleID)` 定位（用户 2026-10-13：删掉自造的 SampleKey、与现役一致）——
    /// 两张单里的同号试件必须互不干扰；试件号里的序号要自动回填到 SheetIndex。
    /// </summary>
    [Fact]
    public async Task 明细按单号加试件号定位_不同单里的同号试件互不干扰()
    {
        var service = CreateService();
        var sheetA = await service.CreateAsync("ShearStrength", "单A", "WT");
        var sheetB = await service.CreateAsync("ShearStrength", "单B", "WT");

        var curveA = new CurvePoint[] { new(0, 0), new(1, 5), new(2, 3) };
        var curveB = new CurvePoint[] { new(0, 0), new(1, 8), new(2, 6), new(3, 2) };

        await service.AppendSampleAsync(sheetA, "S001", curveA, Sample(strength: 70));
        await service.AppendSampleAsync(sheetB, "S001", curveB, Sample(strength: 80));

        var rowA = service.ReadDetailRow(sheetA, "S001", ["ShearStrength", "SheetIndex"])!;
        var rowB = service.ReadDetailRow(sheetB, "S001", ["ShearStrength", "SheetIndex"])!;

        Assert.Equal(70d, Convert.ToDouble(rowA["ShearStrength"]), 3);
        Assert.Equal(80d, Convert.ToDouble(rowB["ShearStrength"]), 3);

        // 试件号里的序号 = SheetIndex（现役同口径），写入时自动回填
        Assert.Equal(1, Convert.ToInt32(rowA["SheetIndex"]));
        Assert.Equal(1, Convert.ToInt32(rowB["SheetIndex"]));

        // 曲线也按两个条件取，各取各的
        Assert.Equal(curveA.Length, service.ReadCurve(sheetA, "S001").Count);
        Assert.Equal(curveB.Length, service.ReadCurve(sheetB, "S001").Count);
        Assert.Equal(curveB[3], service.ReadCurve(sheetB, "S001")[3]);

        // 不存在的试件号回 null / 空曲线（不是"取到别的单的那一件"）
        Assert.Null(service.ReadDetailRow(sheetA, "S999", ["SampleID"]));
        Assert.Empty(service.ReadCurve(sheetA, "S999"));
    }

    /// <summary>
    /// 删除一件试样（用户 2026-10-13）：只删那一行，同单别的件不动；删掉的号可以再用。
    /// </summary>
    [Fact]
    public async Task 删除一件试样只删那一行()
    {
        var service = CreateService();
        var sheetId = await service.CreateAsync("ShearStrength", "删除验证", "WT");
        var other = await service.CreateAsync("ShearStrength", "别的不动", "WT");

        var curve = new CurvePoint[] { new(0, 0), new(1, 5), new(2, 3) };
        await service.AppendSampleAsync(sheetId, "S001", curve, Sample(strength: 70));
        await service.AppendSampleAsync(sheetId, "S002", curve, Sample(strength: 71));
        await service.AppendSampleAsync(other, "S001", curve, Sample(strength: 72));

        Assert.True(await service.DeleteSampleAsync(sheetId, "S002"));

        // 只有 S002 没了：同单的 S001 与另一张单的同号件都在
        Assert.Null(service.ReadDetailRow(sheetId, "S002", ["SampleID"]));
        Assert.NotNull(service.ReadDetailRow(sheetId, "S001", ["SampleID"]));
        Assert.NotNull(service.ReadDetailRow(other, "S001", ["SampleID"]));

        // 曲线也随行删掉了
        Assert.Empty(service.ReadCurve(sheetId, "S002"));

        // 删不存在的件回 false（界面据此提示"没找到"）
        Assert.False(await service.DeleteSampleAsync(sheetId, "S999"));
    }

    private static Dictionary<string, double> Sample(double strength) =>
        new(StringComparer.Ordinal)
        {
            ["ConfiningPressure"] = 30,
            ["InitialHeight"] = 306,
            ["InitialDiameter"] = 150,
            ["MaxAxialLoad"] = 9600,
            ["PeakDeformation"] = 30.6,
            ["AxialStrain"] = 10,
            ["ShearStrength"] = strength,
            ["IsEffective"] = 1,
        };

    /// <summary>
    /// UUID 口径（用户 2026-10-13）：建单时主表生成 UUID，**明细沿用同一张单的 UUID**
    /// —— 现役同口径（云端按 UUID 去重防同号单重复）。
    /// </summary>
    [Fact]
    public async Task 试验单与它的试样共用同一个UUID()
    {
        var service = CreateService();
        var sheetId = await service.CreateAsync("ShearStrength", "项目", "WT");

        var curve = new CurvePoint[] { new(0, 0), new(1, 5), new(2, 3) };
        await service.AppendSampleAsync(sheetId, "1", curve, Sample(strength: 73.47));
        await service.AppendSampleAsync(sheetId, "2", curve, Sample(strength: 74.10));

        var mainUuid = Convert.ToString(service.ReadMainRow(sheetId, ["UUID"])["UUID"]);

        Assert.False(string.IsNullOrWhiteSpace(mainUuid));
        Assert.True(Guid.TryParse(mainUuid, out _), $"应是标准 GUID 文本，实际 '{mainUuid}'");
        Assert.Equal(36, mainUuid!.Length);                       // 带连字符、无花括号（现役 GenerateCleanGUID）
        Assert.Equal(mainUuid, mainUuid.ToUpperInvariant());      // 大写

        var sample1 = Convert.ToString(service.ReadDetailRow(sheetId, "1", ["UUID"])!["UUID"]);
        var sample2 = Convert.ToString(service.ReadDetailRow(sheetId, "2", ["UUID"])!["UUID"]);

        Assert.Equal(mainUuid, sample1);
        Assert.Equal(mainUuid, sample2);
    }

    /// <summary>
    /// 老库（本功能之前建的）主表 UUID 是空的：追加试样时要**自愈** —— 补一个写回主表，
    /// 明细拿到的就是它，不会出现"主表空、明细空"这种云端没法去重的数据。
    /// </summary>
    [Fact]
    public async Task 老记录没有UUID时追加试样会补上并写回主表()
    {
        var service = CreateService();
        var sheetId = await service.CreateAsync("ShearStrength", "项目", "WT");

        // 模拟老库：把主表的 UUID 抹掉
        using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ShearStrengthTestMain SET UUID = NULL WHERE SheetID = $key;";
            command.Parameters.AddWithValue("$key", sheetId);
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var curve = new CurvePoint[] { new(0, 0), new(1, 5) };
        await service.AppendSampleAsync(sheetId, "1", curve, Sample(strength: 73.47));

        var healed = Convert.ToString(service.ReadMainRow(sheetId, ["UUID"])["UUID"]);
        var sample = Convert.ToString(service.ReadDetailRow(sheetId, "1", ["UUID"])!["UUID"]);

        Assert.False(string.IsNullOrWhiteSpace(healed));
        Assert.True(Guid.TryParse(healed, out _));
        Assert.Equal(healed, sample);
    }

    private TestSheetService CreateService()
    {
        var module = new ShearStrengthPersistenceModule();
        var initializer = new DatabaseInitializer(new StorageOptions(_databasePath), [module]);
        initializer.Initialize();

        return new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]);
    }
}
