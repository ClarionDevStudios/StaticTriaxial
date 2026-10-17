namespace STS.Method.ShearStrength.Tests;

using STS.Core;

/// <summary>
/// 试验单编辑（报告 6.1~6.6 + 人员）：字段声明（16 行、含单号 / 试验日期两个只读展示行）、
/// 必填与日期校验、新建 / 读回 / 改单（含清空）、模板存读（只进模板表的 7 列）。
/// </summary>
public sealed class ShearStrengthSheetEditorTests : IDisposable
{
    private readonly TempSheetDatabase _database = new();

    public void Dispose() => _database.Dispose();

    /// <summary>按声明铺一份字段值（默认全空，按需覆盖）。</summary>
    private static Dictionary<string, string> Fields(
        params (string Key, string Value)[] overrides
    )
    {
        var fields = ShearStrengthSheetFields.Definitions.ToDictionary(
            definition => definition.Key,
            definition => definition.DefaultValue,
            StringComparer.Ordinal
        );

        foreach (var (key, value) in overrides)
        {
            fields[key] = value;
        }

        return fields;
    }

    [Fact]
    public void 声明应十六行_必填只认项目名称_含两个只读展示行()
    {
        var keys = ShearStrengthSheetFields.Definitions.Select(d => d.Key).ToArray();

        Assert.Equal(16, keys.Length);
        Assert.Contains(ShearStrengthSheetFields.SheetIdKey, keys);
        Assert.Contains(ShearStrengthSheetFields.CreatedOnKey, keys);

        var required = Assert.Single(ShearStrengthSheetFields.RequiredKeys);
        Assert.Equal(ShearStrengthSheetFields.ProjectNameKey, required);
    }

    [Fact]
    public async Task 新建应校验必填与日期格式_并可按单号读回()
    {
        var execution = _database.CreateExecution();

        // 必填：项目名称
        var missing = await Assert.ThrowsAsync<LocalizedException>(() =>
            execution.CreateSheetAsync(Fields())
        );
        Assert.Equal(ShearStrengthMessageKeys.FieldRequired, missing.Messages[0].ResourceKey);

        // 日期格式：接样日期不是 yyyy-MM-dd
        var badDate = await Assert.ThrowsAsync<LocalizedException>(() =>
            execution.CreateSheetAsync(
                Fields(
                    (ShearStrengthSheetFields.ProjectNameKey, "项目 A"),
                    (ShearStrengthSheetFields.SampleReceiptDateKey, "2026/13/40")
                )
            )
        );
        Assert.Equal(ShearStrengthMessageKeys.FieldFormat, badDate.Messages[0].ResourceKey);

        // 正常建单 + 读回
        var sheetId = await execution.CreateSheetAsync(
            Fields(
                (ShearStrengthSheetFields.ProjectNameKey, "G15 高速 K12+300"),
                (ShearStrengthSheetFields.SampleNumberKey, "JC-001"),
                (ShearStrengthSheetFields.SampleNameKey, "级配碎石"),
                (ShearStrengthSheetFields.SampleReceiptDateKey, "2026-10-15")
            )
        );

        Assert.StartsWith("WT", sheetId, StringComparison.Ordinal);

        var info = await execution.ReadSheetInfoAsync(sheetId);
        Assert.Equal("G15 高速 K12+300", info[ShearStrengthSheetFields.ProjectNameKey]);
        Assert.Equal("JC-001", info[ShearStrengthSheetFields.SampleNumberKey]);
        Assert.Equal("级配碎石", info[ShearStrengthSheetFields.SampleNameKey]);
        Assert.Equal("2026-10-15", info[ShearStrengthSheetFields.SampleReceiptDateKey]);

        // 试验日期只显示日期（库里是完整时间戳）：读回来应是 yyyy-MM-dd
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", info[ShearStrengthSheetFields.CreatedOnKey]);

        // 预览号是"下一个"（不消耗流水），且与刚建的单号不同
        Assert.NotEqual(sheetId, await execution.PeekNextSheetIdAsync());
    }

    [Fact]
    public async Task 改单应写回并支持清空()
    {
        var execution = _database.CreateExecution();
        var sheetId = await execution.CreateSheetAsync(
            Fields(
                (ShearStrengthSheetFields.ProjectNameKey, "项目 A"),
                (ShearStrengthSheetFields.SampleNameKey, "级配碎石")
            )
        );

        await execution.UpdateSheetAsync(
            sheetId,
            Fields(
                (ShearStrengthSheetFields.ProjectNameKey, "项目 A（改名）"),
                (ShearStrengthSheetFields.SampleOriginKey, "示例料场")
            )
        );

        var updated = await execution.ReadSheetInfoAsync(sheetId);
        Assert.Equal("项目 A（改名）", updated[ShearStrengthSheetFields.ProjectNameKey]);
        Assert.Equal("示例料场", updated[ShearStrengthSheetFields.SampleOriginKey]);
        Assert.Equal("", updated[ShearStrengthSheetFields.SampleNameKey]); // 表单里置空 → 库里清掉
    }

    [Fact]
    public async Task 模板应只存七项_读取时其余回落默认值()
    {
        var execution = _database.CreateExecution();

        Assert.False(await execution.HasTemplateAsync());

        await execution.SaveTemplateAsync(
            Fields(
                (ShearStrengthSheetFields.MachineNameAndIDKey, "土工三轴仪（GR-1）"),
                (ShearStrengthSheetFields.TestPersonKey, "张工"),
                (ShearStrengthSheetFields.ProjectNameKey, "不该进模板")
            )
        );

        Assert.True(await execution.HasTemplateAsync());

        var template = await execution.ReadTemplateAsync();
        Assert.Equal("土工三轴仪（GR-1）", template[ShearStrengthSheetFields.MachineNameAndIDKey]);
        Assert.Equal("张工", template[ShearStrengthSheetFields.TestPersonKey]);
        Assert.Equal("", template[ShearStrengthSheetFields.ProjectNameKey]); // 模板没这列 → 回落默认
    }
}
