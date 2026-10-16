namespace STS.Method.ShearStrength;

using System.Globalization;
using System.Text.Json;
using STS.Control;
using STS.Core;
using STS.Storage;

/// <summary>
/// 抗剪强度（静三轴法，T 1236—2026）的执行 / 试验单编辑实现。
///
/// 实装进度（2026-10-16）：①**试验单 / 试样信息真实读写**（建单、试件号、增删改查、缺尺寸硬校验、
/// 超窗提示）；②**判定 / 落库**（单件判定 → 分级均值 → 收尾拟包络线 c、φ、r → 完成时间；
/// 有效性 / 重算）；③**试验单编辑**（报告 6.1~6.6 + 人员的字段声明、必填 / 日期校验、模板存读）；
/// ④围压目标供执行页取（`IConfiningPressureSource`）。
/// </summary>
public sealed class ShearStrengthExecution(TestSheetService sheets)
    : ITestExecution,
        ITestSheetEditor,
        IConfiningPressureSource
{
    /// <summary>方法显示名（中文兜底；界面按 <c>MethodNameKey</c> 取当前语言）。</summary>
    public const string MethodDisplayName = "抗剪强度试验（静三轴法）";

    public string MethodCode => ShearStrengthPersistenceModule.MethodCodeValue;

    public string MethodName => MethodDisplayName;

    public string MethodNameKey => "Method.ShearStrength.Name";

    public string StandardReference => "JTG 3442—2026 / T 1236—2026";

    public string CurveXAxisKey => "Chart.Axis.Deformation";

    public string CurveYAxisKey => "Chart.Axis.Load";

    public TestRunProfile Profile { get; } = new();

    /// <summary>默认应变速率（%/min）：规范 4.5 的 1~2 取中值。</summary>
    public const double DefaultStrainRatePercentPerMinute = 1.5;

    /// <summary>接触应力（kPa）：规范 4.3 的 3~5kPa 取上限。</summary>
    public const double DefaultContactStressKPa = 5.0;

    public Task<TestRunProfile> LoadProfileAsync(
        TestStartContext? context = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(LoadProfile(context));

    /// <summary>
    /// 装配一次运行：T 1236 口径的默认值 + （有起点上下文时）按**逐件**试件尺寸
    /// 构造停止判据（15% 应变上限、校正面积应力都依赖 h0 / d0）与加载速率（应变速率 × h0）。
    /// **缺尺寸 → 明确报错**（设计文档待办①的闸口）：先在试样信息里填尺寸，再开始试验。
    /// </summary>
    private TestRunProfile LoadProfile(TestStartContext? context)
    {
        var nominalArea = ShearStrengthAnalyzer.InitialAreaMM2(ShearStrengthAnalyzer.NominalSpecimenDiameterMM);

        var profile = new TestRunProfile
        {
            // 规范 4.7 的卸压 / 拆室是人工环节：本方法不配"破坏后继续加载"，也不自动归位
            ContinueLoadingSeconds = 0,
            ResetSeconds = 0,
            // 点动速率取现役值（2400 × 0.001mm/min = 2.4mm/min）
            JogSpeedMillimetersPerMinute = 2.4,
            // 接触 = 3~5kPa（取上限）；按标准件 Φ100 试件面积折算成力（≈0.0393kN）
            ContactLoadKN = DefaultContactStressKPa * nominalArea / 1_000_000.0,
            // 规范 4.5：破坏控制在 5~10min 内完成 → 加载阶段兜底超时给足余量
            LoadingTimeoutSeconds = 30 * 60,
        };

        if (context is null)
        {
            return profile;
        }

        // 取**库里真实存的值**（不走字段默认值回落 —— 回落会让"没填尺寸"悄悄变成 306/100，
        // 闸口就形同虚设）
        var row = sheets.ReadDetailRow(
            context.SheetId,
            context.SampleId,
            [ShearStrengthSampleFields.InitialHeightKey, ShearStrengthSampleFields.InitialDiameterKey]
        );

        var stored = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in new[]
        {
            ShearStrengthSampleFields.InitialHeightKey,
            ShearStrengthSampleFields.InitialDiameterKey,
        })
        {
            if (row is not null && row.TryGetValue(key, out var value) && value is not null)
            {
                stored[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        if (!TryReadSpecimenSize(stored, out var heightMM, out var diameterMM))
        {
            throw LocalizedException.Single(
                ShearStrengthMessageKeys.MissingSpecimenSize,
                $"试件 {context.SampleId} 还没有试件尺寸（初始高度 / 初始直径），请先在试样信息里补全再开始试验",
                context.SampleId
            );
        }

        // 合格窗（规范 4.1.3：306±5mm）只警告不硬拦（用户 2026-10-16 定的 Q5 口径）：
        // 超窗是现场真实数据，记录真实值继续做，界面把提示写进操控信息。
        var notes = new List<LocalizedText>();

        if (
            Math.Abs(heightMM - ShearStrengthAnalyzer.NominalSpecimenHeightMM)
            > ShearStrengthAnalyzer.SpecimenHeightToleranceMM
        )
        {
            notes.Add(
                new LocalizedText(
                    ShearStrengthMessageKeys.HeightOutOfWindow,
                    heightMM.ToString("0.#", CultureInfo.InvariantCulture),
                    ShearStrengthAnalyzer.NominalSpecimenHeightMM.ToString("0.#", CultureInfo.InvariantCulture),
                    ShearStrengthAnalyzer.SpecimenHeightToleranceMM.ToString("0.#", CultureInfo.InvariantCulture)
                )
            );
        }

        return profile with
        {
            StopCriteriaFactory = () => new ShearStrengthStopCriteria(heightMM, diameterMM),
            // 加载速率 = 应变速率 × 初始高度（例：306mm × 1.5%/min ≈ 4.59mm/min）
            LoadRateMillimetersPerMinute = DefaultStrainRatePercentPerMinute * heightMM / 100.0,
            // 规范没有单独的"接近段速率"：与试验速率一致（也落在设备 0~5mm/min 窗内）
            ApproachSpeedMillimetersPerMinute = DefaultStrainRatePercentPerMinute * heightMM / 100.0,
            StartNotes = notes,
        };
    }

    /// <summary>
    /// 从试样信息字典里读试件尺寸（键 = 明细列名 `InitialHeight` / `InitialDiameter`，单位 mm）。
    /// 两个键都在、能解析且为正才算有效。
    /// </summary>
    public static bool TryReadSpecimenSize(
        IReadOnlyDictionary<string, string> sampleInfo,
        out double heightMM,
        out double diameterMM
    )
    {
        heightMM = 0;
        diameterMM = 0;

        return sampleInfo.TryGetValue("InitialHeight", out var heightText)
            && sampleInfo.TryGetValue("InitialDiameter", out var diameterText)
            && double.TryParse(heightText, NumberStyles.Float, CultureInfo.InvariantCulture, out heightMM)
            && double.TryParse(diameterText, NumberStyles.Float, CultureInfo.InvariantCulture, out diameterMM)
            && heightMM > 0
            && diameterMM > 0;
    }

    /// <summary>
    /// 实时指标卡：荷载 / 位移 / 剪切强度 σ1max / 破坏应变 ε1 / 试验持续时间。
    /// 键 `load` / `displacement` / `elapsed` 是执行页控制循环的实时刷新口；
    /// `shearStrength` / `axialStrain` 在单件判定落库时填。
    /// </summary>
    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        new("load", ShearStrengthMetricKeys.Load, "kN", ShowPeak: true, ValueMinChars: 8),
        new("displacement", ShearStrengthMetricKeys.Displacement, "mm", ShowPeak: true, ValueMinChars: 8),
        new("shearStrength", ShearStrengthMetricKeys.ShearStrength, "kPa"),
        new("axialStrain", ShearStrengthMetricKeys.AxialStrain, "%", ValueFormat: "0.##"),
        new("elapsed", ShearStrengthMetricKeys.Elapsed, "s"),
    ];

    /// <inheritdoc />
    public IReadOnlyList<ParameterDefinition> SampleFields => ShearStrengthSampleFields.Definitions;

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> LoadSampleInfoAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = ShearStrengthSampleFields.Definitions.Select(definition => definition.Key).ToArray();
        var row = sheets.ReadDetailRow(sheetId, sampleId, keys);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in ShearStrengthSampleFields.Definitions)
        {
            // 没有该件记录（还没填过）时回落到字段默认值
            var stored =
                row is not null && row.TryGetValue(definition.Key, out var value) && value is not null
                    ? Convert.ToString(value, CultureInfo.InvariantCulture)
                    : null;

            values[definition.Key] = string.IsNullOrWhiteSpace(stored)
                ? definition.DefaultValue
                : stored;
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(values);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SampleRowInfo>> ReadSampleRowsAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var columns = new[] { "SampleID", "SheetIndex", "TestStatus", "MaxAxialLoad", "ShearStrength" }
            .Concat(ShearStrengthSampleFields.Definitions.Select(definition => definition.Key))
            .ToArray();

        // 按序号排（SheetIndex 就是试件号里的序号）；老记录该列为空 → 从试件号里解析序号
        var rows = sheets
            .ReadDetailRows(sheetId, columns)
            .OrderBy(row =>
                TryReadIndex(row["SheetIndex"])
                ?? SampleNumbering.ParseIndex(
                    Convert.ToString(row["SampleID"], CultureInfo.InvariantCulture)
                )
                ?? int.MaxValue
            )
            .ThenBy(
                row => Convert.ToString(row["SampleID"], CultureInfo.InvariantCulture),
                StringComparer.Ordinal
            )
            .ToArray();

        var result = new List<SampleRowInfo>(rows.Length);

        foreach (var row in rows)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var definition in ShearStrengthSampleFields.Definitions)
            {
                fields[definition.Key] = FormatSampleField(row, definition);
            }

            result.Add(
                new SampleRowInfo(
                    Convert.ToString(row["SampleID"], CultureInfo.InvariantCulture) ?? string.Empty,
                    TryReadIndex(row["TestStatus"]) == TestStatusCompleted,
                    // 判定值齐 = 最大轴向荷载与剪切强度都读得出来
                    TryReadDouble(row, "MaxAxialLoad", out _)
                        && TryReadDouble(row, "ShearStrength", out _),
                    fields
                )
            );
        }

        return Task.FromResult<IReadOnlyList<SampleRowInfo>>(result);
    }

    /// <inheritdoc />
    public async Task SaveSampleInfoAsync(
        string sheetId,
        string sampleId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    )
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var definition in ShearStrengthSampleFields.Definitions)
        {
            if (!fields.TryGetValue(definition.Key, out var text))
            {
                continue;
            }

            text = text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                // 空值一律存 NULL（可空字段；"没有"和"填了 0"不是一回事）
                values[definition.Key] = null;
                continue;
            }

            if (definition.Kind != ParameterKind.Number)
            {
                values[definition.Key] = text;
                continue;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                throw LocalizedException.Single(
                    ShearStrengthMessageKeys.FieldFormat,
                    $"{definition.DisplayName}格式不对",
                    ShearStrengthMessageKeys.FieldName(definition.DisplayNameKey)
                );
            }

            if (
                (definition.Minimum is { } minimum && number < minimum)
                || (definition.Maximum is { } maximum && number > maximum)
            )
            {
                throw LocalizedException.Single(
                    ShearStrengthMessageKeys.FieldOutOfRange,
                    $"{definition.DisplayName}必须在 {definition.Minimum} ~ {definition.Maximum} 之间",
                    ShearStrengthMessageKeys.FieldName(definition.DisplayNameKey),
                    definition.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "−∞",
                    definition.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "+∞"
                );
            }

            values[definition.Key] = number;
        }

        await sheets
            .UpsertSampleInfoAsync(sheetId, sampleId, values, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<string> CreateSheetAsync(
        string projectName,
        string numberPrefix,
        CancellationToken cancellationToken = default
    ) => sheets.CreateAsync(MethodCode, projectName, numberPrefix, cancellationToken);

    /// <summary>
    /// 下一个试件号 = 现有明细里最大的序号 + 1（没有则 S001）。
    /// 现役同口径：`GetMaxSampleIndex`（MAX(SheetIndex)+1）+ `'S' + FormatFloat('000', …)`。
    /// </summary>
    public Task<string> NextSampleIdAsync(string sheetId, CancellationToken cancellationToken = default)
    {
        var rows = sheets.ReadDetailRows(sheetId, ["SampleID", "SheetIndex"]);

        var max = 0;
        foreach (var row in rows)
        {
            // 首选 SheetIndex；老记录这一列是空的，退回从试件号里解析序号
            var index =
                TryReadIndex(row["SheetIndex"])
                ?? SampleNumbering.ParseIndex(
                    Convert.ToString(row["SampleID"], CultureInfo.InvariantCulture)
                );

            if (index is { } value && value > max)
            {
                max = value;
            }
        }

        return Task.FromResult(SampleNumbering.Format(max + 1));
    }

    public Task<bool> DeleteSampleAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => sheets.DeleteSampleAsync(sheetId, sampleId, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// 执行页「施加围压」按这个值下发 C2（分段流程的阶段 B，用户 2026-10-16 定）。
    /// **只认实存数据**（不拿字段默认值兜）：没填过就回 <c>null</c>，界面提示"先去填试样信息"。
    /// </remarks>
    public Task<double?> ReadConfiningTargetKPaAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    )
    {
        var row = sheets.ReadDetailRow(sheetId, sampleId, [ShearStrengthSampleFields.ConfiningPressureKey]);

        if (
            row is not null
            && row.TryGetValue(ShearStrengthSampleFields.ConfiningPressureKey, out var value)
            && value is not null
            && double.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var target
            )
            && target > 0
        )
        {
            return Task.FromResult<double?>(target);
        }

        return Task.FromResult<double?>(null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, double>?> CompleteSampleAsync(
        string sheetId,
        string sampleId,
        IReadOnlyList<CurvePoint> curve,
        CancellationToken cancellationToken = default
    )
    {
        if (curve.Count < 3)
        {
            return null; // 曲线点太少：判不了，不落库（界面会提示未结算）
        }

        var (heightMM, diameterMM, confiningKPa) = ReadStoredConditions(sheetId, sampleId);
        var result = ShearStrengthAnalyzer.AnalyzeSample(curve, heightMM, diameterMM, confiningKPa);

        // 采集口径（用户 2026-10-16 定）：判定用**原始帧曲线**（峰值/15% 插值要精度），
        // **落库按 0.5mm 抽样**（规范 4.5 的记录间距）—— 曲线 JSON 存抽样后的记录曲线。
        var recordCurve = ShearStrengthAnalyzer.SampleAtInterval(curve);

        var columns = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["MaxAxialLoad"] = result.PeakLoadKN * 1000.0, // 明细列单位 N（规范 4.6 记 Pmax，N）
            ["PeakDeformation"] = result.PeakDeformationMM,
            ["AxialStrain"] = result.AxialStrainPercent,
            ["ShearStrength"] = result.ShearStrengthKPa,
            ["StoppedAt15PercentStrain"] = result.UsedStrain15Rule ? 1 : 0,
            ["TestStatus"] = TestStatusCompleted,
        };

        // 试样记录一般在"试样编辑"时已建好：有就更新（补曲线与结果），没有才追加
        if (sheets.ReadDetailRow(sheetId, sampleId, ["SampleID"]) is null)
        {
            await sheets
                .AppendSampleAsync(sheetId, sampleId, recordCurve, columns, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await sheets
                .UpdateDetailFieldsAsync(sheetId, sampleId, columns, recordCurve, cancellationToken)
                .ConfigureAwait(false);
        }

        // 分级均值（规范 5.4）随件刷新；收尾时（CompleteSheetAsync）再拟包络线
        await RefreshLevelResultsAsync(sheetId, cancellationToken).ConfigureAwait(false);

        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["load"] = result.PeakLoadKN,
            ["displacement"] = result.PeakDeformationMM,
            ["shearStrength"] = result.ShearStrengthKPa,
            ["axialStrain"] = result.AxialStrainPercent,
        };
    }

    /// <inheritdoc />
    public async Task CompleteSheetAsync(string sheetId, CancellationToken cancellationToken = default)
    {
        var levels = ReadCompletedLevels(sheetId);

        // 规范 4.8 / 5.4：不少于 3 级围压、每级 3 件 —— 收尾前先核，缺什么直接说清楚
        if (levels.Count < ShearStrengthAnalyzer.MinimumConfiningLevels)
        {
            throw LocalizedException.Single(
                ShearStrengthMessageKeys.NeedMoreLevels,
                $"完成试验单至少需要 {ShearStrengthAnalyzer.MinimumConfiningLevels} 级围压（当前 {levels.Count} 级）",
                levels.Count,
                ShearStrengthAnalyzer.MinimumConfiningLevels
            );
        }

        foreach (var level in levels.Where(level => level.Count < ShearStrengthAnalyzer.SpecimensPerLevel))
        {
            throw LocalizedException.Single(
                ShearStrengthMessageKeys.LevelIncomplete,
                $"围压 {level.ConfiningPressureKPa:0.###} kPa 只有 {level.Count} 件完成"
                    + $"（每级需要 {ShearStrengthAnalyzer.SpecimensPerLevel} 件）",
                level.ConfiningPressureKPa,
                level.Count,
                ShearStrengthAnalyzer.SpecimensPerLevel
            );
        }

        var fit = ShearStrengthAnalyzer.FitEnvelope([.. levels]);

        await sheets
            .UpdateMainFieldsAsync(
                sheetId,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    // |m| ≥ 1（数据异常）时 c、φ 无解：存 NULL，别把 NaN 写进库
                    ["CohesionKPa"] = double.IsNaN(fit.CohesionKPa) ? null : fit.CohesionKPa,
                    ["FrictionAngleDeg"] = double.IsNaN(fit.FrictionAngleDegrees)
                        ? null
                        : fit.FrictionAngleDegrees,
                    // 相关系数也算不出来时（例如各级"σ1max−σ3"完全相同 → 分母为 0）存 NULL，别把 NaN 写进库
                    ["EnvelopeCorrelation"] = double.IsNaN(fit.Correlation) ? null : fit.Correlation,
                    ["EnvelopeReliable"] = fit.IsReliable ? 1 : 0,
                    ["LevelResultsJson"] = EncodeLevelResults(levels),
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        await sheets.CompleteAsync(sheetId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ValidityReport> EvaluateValidityAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var rows = sheets.ReadDetailRows(
            sheetId,
            ["SampleID", "TestStatus", ShearStrengthSampleFields.ConfiningPressureKey]
        );

        var completed = rows
            .Where(row => TryReadIndex(row["TestStatus"]) == TestStatusCompleted)
            .Select(row => (
                SampleId: Convert.ToString(row["SampleID"], CultureInfo.InvariantCulture) ?? string.Empty,
                Sigma3: TryReadDouble(row, ShearStrengthSampleFields.ConfiningPressureKey, out var sigma)
                    ? sigma
                    : double.NaN
            ))
            .Where(pair => pair.SampleId.Length > 0 && !double.IsNaN(pair.Sigma3) && pair.Sigma3 > 0)
            .ToArray();

        // T 1236 没有剔除规则（规范未给；数据表设计文档第四节）——已完成的件全部计有效。
        // IsEffective 是预留列，现在就把它填起来（历史数据与云端各取所需）。
        foreach (var (sampleId, _) in completed)
        {
            await sheets
                .UpdateDetailFieldsAsync(
                    sheetId,
                    sampleId,
                    new Dictionary<string, double>(StringComparer.Ordinal) { ["IsEffective"] = 1 },
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }

        await RefreshLevelResultsAsync(sheetId, cancellationToken).ConfigureAwait(false);

        // 有哪个级不够 3 件 → 提示补充试验（规范 3.3.1 / 4.8 的件数口径）
        var needsSupplement = completed
            .GroupBy(pair => pair.Sigma3)
            .Any(group => group.Count() < ShearStrengthAnalyzer.SpecimensPerLevel);

        return new ValidityReport(
            completed.Length,
            completed.Length, // 无剔除规则：有效件 = 已完成件
            [],
            needsSupplement
        );
    }

    /// <inheritdoc />
    public async Task<RecomputeReport> RecomputeSheetAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var rows = sheets.ReadDetailRows(
            sheetId,
            [
                "SampleID",
                ShearStrengthSampleFields.InitialHeightKey,
                ShearStrengthSampleFields.InitialDiameterKey,
                ShearStrengthSampleFields.ConfiningPressureKey,
            ]
        );

        var recomputed = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            var sampleId = Convert.ToString(row["SampleID"], CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(sampleId))
            {
                continue;
            }

            var stored = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in new[]
            {
                ShearStrengthSampleFields.InitialHeightKey,
                ShearStrengthSampleFields.InitialDiameterKey,
                ShearStrengthSampleFields.ConfiningPressureKey,
            })
            {
                if (row.TryGetValue(key, out var value) && value is not null)
                {
                    stored[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                }
            }

            var curve = sheets.ReadCurve(sheetId, sampleId);

            if (
                curve.Count < 3
                || !TryReadSpecimenSize(stored, out var heightMM, out var diameterMM)
                || !stored.TryGetValue(ShearStrengthSampleFields.ConfiningPressureKey, out var sigmaText)
                || !double.TryParse(
                    sigmaText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var confiningKPa
                )
                || confiningKPa <= 0
            )
            {
                skipped++; // 曲线不足 / 条件不全：判不了，跳过（不连累整单）
                continue;
            }

            var result = ShearStrengthAnalyzer.AnalyzeSample(curve, heightMM, diameterMM, confiningKPa);

            await sheets
                .UpdateDetailFieldsAsync(
                    sheetId,
                    sampleId,
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["MaxAxialLoad"] = result.PeakLoadKN * 1000.0,
                        ["PeakDeformation"] = result.PeakDeformationMM,
                        ["AxialStrain"] = result.AxialStrainPercent,
                        ["ShearStrength"] = result.ShearStrengthKPa,
                        ["StoppedAt15PercentStrain"] = result.UsedStrain15Rule ? 1 : 0,
                    },
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            recomputed++;
        }

        if (recomputed > 0)
        {
            await RefreshLevelResultsAsync(sheetId, cancellationToken).ConfigureAwait(false);
        }

        return new RecomputeReport(recomputed, skipped);
    }

    // ── ITestSheetEditor（试验单编辑：报告 6.1~6.6 + 人员 + 模板）────────────

    /// <summary>单号前缀（沿用现役现场口径）。</summary>
    private const string NumberPrefix = "WT";

    /// <summary>
    /// 模板表里声明的列（与 `ShearStrengthPersistenceModule.TemplateTable` 一致）：
    /// 只存"下次新建时的缺省值"（仪器 + 人员 + 缩分 / 成型）；单号与试验日期自动生成、不进模板。
    /// </summary>
    private static readonly string[] TemplateColumns =
    [
        ShearStrengthSheetFields.MachineNameAndIDKey,
        ShearStrengthSheetFields.TestPersonKey,
        ShearStrengthSheetFields.CalculatePersonKey,
        ShearStrengthSheetFields.VerificationPersonKey,
        ShearStrengthSheetFields.SampleReductionMethodKey,
        ShearStrengthSheetFields.MoldingMethodKey,
        ShearStrengthSheetFields.MoldingParametersKey,
    ];

    /// <inheritdoc />
    public IReadOnlyList<ParameterDefinition> SheetInfoFields => ShearStrengthSheetFields.Definitions;

    /// <inheritdoc />
    public Task<string> PeekNextSheetIdAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(sheets.PeekNextSheetId(MethodCode, NumberPrefix));

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ReadSheetInfoAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = ShearStrengthSheetFields.Definitions.Select(definition => definition.Key).ToArray();
        return Task.FromResult(ToFieldTexts(sheets.ReadMainRow(sheetId, keys)));
    }

    /// <inheritdoc />
    public async Task<string> CreateSheetAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    )
    {
        ValidateSheetFields(fields);

        // 单号由流水生成器发（界面上显示的只是"预览号"）；试验日期也由存储层现取 —— 都从字段里剔掉
        var payload = fields
            .Where(pair =>
                !string.Equals(pair.Key, ShearStrengthSheetFields.SheetIdKey, StringComparison.Ordinal)
                && !string.Equals(pair.Key, ShearStrengthSheetFields.CreatedOnKey, StringComparison.Ordinal)
            )
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return await sheets
            .CreateAsync(MethodCode, NumberPrefix, BuildMainColumns(payload), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateSheetAsync(
        string sheetId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    )
    {
        ValidateSheetFields(fields);

        // includeBlank: true —— 改单时"清空某一项"要真的清掉（写 NULL），而不是跳过
        var columns = BuildMainColumns(fields, includeBlank: true);
        columns.Remove(ShearStrengthSheetFields.SheetIdKey);
        columns.Remove(ShearStrengthSheetFields.CreatedOnKey);

        await sheets.UpdateMainFieldsAsync(sheetId, columns, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// T 1236 单上没有"要写回参数表"的试验条件项（加载速率由方法按规范常量 / 逐件尺寸装配），
    /// 所以这里是空操作 —— 保留接口语义，将来若有单上条件要回写再补。
    /// </remarks>
    public Task PushTestConditionsAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> ResolveDependentDefaults(
        string changedKey,
        string value
    ) =>
        // T 1236 没有联动预填（现役那套"按试验目的带温度 / 速率"不适用）
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<bool> HasTemplateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(
            sheets.ReadTemplateRow(MethodCode, [ShearStrengthSheetFields.MachineNameAndIDKey])
                is not null
        );

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ReadTemplateAsync(
        CancellationToken cancellationToken = default
    )
    {
        var row = sheets.ReadTemplateRow(MethodCode, TemplateColumns);

        if (row is null)
        {
            return Task.FromResult(DefaultFieldTexts());
        }

        // 模板只覆盖它声明的 7 列，其余字段回落到声明默认值
        var values = ShearStrengthSheetFields.Definitions.ToDictionary(
            definition => definition.Key,
            definition =>
                row.TryGetValue(definition.Key, out var value) && value is not null
                    ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                    : definition.DefaultValue,
            StringComparer.Ordinal
        );

        return Task.FromResult<IReadOnlyDictionary<string, string>>(values);
    }

    /// <inheritdoc />
    public Task SaveTemplateAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    )
    {
        // 模板表只有 7 列：模板表没声明的字段直接丢掉（单号 / 试验日期本来也不进模板）
        var payload = fields
            .Where(pair => TemplateColumns.Contains(pair.Key, StringComparer.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return sheets.SaveTemplateAsync(
            MethodCode,
            BuildMainColumns(payload, includeBlank: true),
            cancellationToken
        );
    }

    /// <summary>
    /// 试验单的必填 / 格式校验（方法层的事 —— 界面不认识"哪个字段必须填"）。
    /// 必填 = 项目名称；日期项要求 `yyyy-MM-dd` 能解析；其余文本自由。
    /// </summary>
    private static void ValidateSheetFields(IReadOnlyDictionary<string, string> fields)
    {
        foreach (var definition in ShearStrengthSheetFields.Definitions)
        {
            var text = fields.TryGetValue(definition.Key, out var value)
                ? value?.Trim() ?? string.Empty
                : string.Empty;

            if (
                text.Length == 0
                && ShearStrengthSheetFields.RequiredKeys.Contains(definition.Key, StringComparer.Ordinal)
            )
            {
                throw LocalizedException.Single(
                    ShearStrengthMessageKeys.FieldRequired,
                    $"{definition.DisplayName.TrimEnd(' ', '*')}不能为空",
                    ShearStrengthMessageKeys.FieldName(definition.DisplayNameKey)
                );
            }

            if (
                text.Length > 0
                && definition.Kind == ParameterKind.Date
                && !DateOnly.TryParse(text, CultureInfo.InvariantCulture, out _)
            )
            {
                throw LocalizedException.Single(
                    ShearStrengthMessageKeys.FieldFormat,
                    $"{definition.DisplayName}格式不对（应为 yyyy-MM-dd）",
                    ShearStrengthMessageKeys.FieldName(definition.DisplayNameKey)
                );
            }
        }
    }

    /// <summary>
    /// 界面字段 → 主表列：文本列存字符串；空值默认跳过（留给数据库 NULL），
    /// <paramref name="includeBlank"/> = true 时把空值也写进列（值 = null），用于"改单时把原来填过的值清掉"。
    /// </summary>
    private static Dictionary<string, object?> BuildMainColumns(
        IReadOnlyDictionary<string, string> fields,
        bool includeBlank = false
    )
    {
        var columns = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var definition in ShearStrengthSheetFields.Definitions)
        {
            if (!fields.TryGetValue(definition.Key, out var text))
            {
                continue;
            }

            text = text?.Trim() ?? string.Empty;

            if (text.Length == 0)
            {
                if (includeBlank)
                {
                    columns[definition.Key] = null;
                }

                continue;
            }

            columns[definition.Key] = text;
        }

        return columns;
    }

    /// <summary>读回的主表行 → 界面字段文本（空值给空串，由界面决定显示）。</summary>
    private static IReadOnlyDictionary<string, string> ToFieldTexts(
        IReadOnlyDictionary<string, object?> row
    ) =>
        ShearStrengthSheetFields.Definitions.ToDictionary(
            definition => definition.Key,
            definition =>
                row.TryGetValue(definition.Key, out var value) && value is not null
                    ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty,
            StringComparer.Ordinal
        );

    /// <summary>各字段的默认值（没有模板时用）。</summary>
    private static IReadOnlyDictionary<string, string> DefaultFieldTexts() =>
        ShearStrengthSheetFields.Definitions.ToDictionary(
            definition => definition.Key,
            definition => definition.DefaultValue,
            StringComparer.Ordinal
        );

    /// <summary>
    /// 读该件**真实存下来**的试验条件（围压 σ3 / h0 / d0）——判定与加载速率都要它们；
    /// 缺了就是缺了，**不拿字段默认值兜**（兜了会算出一条"看起来正常"的错结果）。
    /// </summary>
    private (double HeightMM, double DiameterMM, double ConfiningKPa) ReadStoredConditions(
        string sheetId,
        string sampleId
    )
    {
        var keys = new[]
        {
            ShearStrengthSampleFields.InitialHeightKey,
            ShearStrengthSampleFields.InitialDiameterKey,
            ShearStrengthSampleFields.ConfiningPressureKey,
        };

        var row = sheets.ReadDetailRow(sheetId, sampleId, keys);
        var stored = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            if (row is not null && row.TryGetValue(key, out var value) && value is not null)
            {
                stored[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        if (!TryReadSpecimenSize(stored, out var heightMM, out var diameterMM))
        {
            throw LocalizedException.Single(
                ShearStrengthMessageKeys.MissingSpecimenSize,
                $"试件 {sampleId} 还没有试件尺寸（初始高度 / 初始直径），请先在试样信息里补全",
                sampleId
            );
        }

        if (
            !stored.TryGetValue(ShearStrengthSampleFields.ConfiningPressureKey, out var sigmaText)
            || !double.TryParse(
                sigmaText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var confiningKPa
            )
            || confiningKPa <= 0
        )
        {
            throw LocalizedException.Single(
                ShearStrengthMessageKeys.FieldRequired,
                "试验围压 σ3 不能为空",
                ShearStrengthMessageKeys.FieldName(
                    ShearStrengthSampleFields.NameKey(ShearStrengthSampleFields.ConfiningPressureKey)
                )
            );
        }

        return (heightMM, diameterMM, confiningKPa);
    }

    /// <summary>
    /// 按围压级归组"**已完成且判出了剪切强度**"的试件（规范 5.4 的算术平均口径）。
    /// 只含完成的件、只按实存数据，不做任何兜底。
    /// </summary>
    private IReadOnlyList<ShearStrengthLevelResult> ReadCompletedLevels(string sheetId)
    {
        var rows = sheets.ReadDetailRows(
            sheetId,
            ["ConfiningPressure", "ShearStrength", "TestStatus"]
        );

        return
        [
            .. rows
                .Where(row => TryReadIndex(row["TestStatus"]) == TestStatusCompleted)
                .Select(row => (
                    Sigma3: TryReadDouble(row, ShearStrengthSampleFields.ConfiningPressureKey, out var sigma)
                        ? sigma
                        : double.NaN,
                    Strength: TryReadDouble(row, "ShearStrength", out var strength) ? strength : double.NaN
                ))
                .Where(pair =>
                    !double.IsNaN(pair.Sigma3) && pair.Sigma3 > 0 && !double.IsNaN(pair.Strength)
                )
                .GroupBy(pair => pair.Sigma3)
                .OrderBy(group => group.Key)
                .Select(group =>
                    ShearStrengthAnalyzer.MeanLevel(group.Key, [.. group.Select(pair => pair.Strength)])
                ),
        ];
    }

    /// <summary>把当前分级均值写回主表（`LevelResultsJson`）——单件落库后随时刷新。</summary>
    private Task RefreshLevelResultsAsync(string sheetId, CancellationToken cancellationToken) =>
        sheets.UpdateMainFieldsAsync(
            sheetId,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["LevelResultsJson"] = EncodeLevelResults(ReadCompletedLevels(sheetId)),
            },
            cancellationToken
        );

    /// <summary>分级结果 JSON：`[{sigma3, meanShearStrength, count}, …]`（数据表设计文档的口径）。</summary>
    private static string EncodeLevelResults(IReadOnlyList<ShearStrengthLevelResult> levels) =>
        JsonSerializer.Serialize(
            levels.Select(level => new
            {
                sigma3 = level.ConfiningPressureKPa,
                meanShearStrength = level.MeanShearStrengthKPa,
                count = level.Count,
            })
        );

    /// <summary>完成状态下写进明细 `TestStatus` 的值（数据表 v1 口径：0 未做 / 1 完成）。</summary>
    private const int TestStatusCompleted = 1;

    /// <summary>读整数列（SQLite 会按存储类别回 int / long / 文本，都要认）。</summary>
    private static int? TryReadIndex(object? raw) =>
        raw switch
        {
            null or DBNull => null,
            int value => value,
            long value => (int)value,
            _ => int.TryParse(
                Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed
            )
                ? parsed
                : null,
        };

    /// <summary>读数值列（不存在 / 空 / 解析不了都回 false）。</summary>
    private static bool TryReadDouble(
        IReadOnlyDictionary<string, object?> row,
        string key,
        out double value
    )
    {
        value = 0;

        if (!row.TryGetValue(key, out var raw) || raw is null || raw is DBNull)
        {
            return false;
        }

        if (raw is double number)
        {
            value = number;
            return true;
        }

        return double.TryParse(
            Convert.ToString(raw, CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value
        );
    }

    /// <summary>明细行上的一个试样信息字段取成显示文本（数值按 0.###；空值回落字段默认值）。</summary>
    private static string FormatSampleField(
        IReadOnlyDictionary<string, object?> row,
        ParameterDefinition definition
    )
    {
        if (!row.TryGetValue(definition.Key, out var raw) || raw is null || raw is DBNull)
        {
            return definition.DefaultValue;
        }

        return raw is double number
            ? number.ToString("0.###", CultureInfo.InvariantCulture)
            : Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
