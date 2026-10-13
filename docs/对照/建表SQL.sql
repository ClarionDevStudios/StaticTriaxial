-- ANX 建表 SQL：由 ANX.Tools.SchemaDump 从表定义生成，请勿手改
-- 架构版本：3（库版本对不上时程序报错并要求备份后重建）
-- 生成时间：2026-10-13 15:34:26

-- ==================== 框架表 ====================
-- 表 DbVersion：架构版本（本项目不做迁移，版本对不上直接报错提示备份后重建）
CREATE TABLE IF NOT EXISTS DbVersion (
    Version INTEGER NOT NULL,  -- 架构版本号；当前程序要求 3
    PRIMARY KEY (Version)
);

-- 表 SheetSequence：单号流水（前缀 + 日期 → 当天已用到的流水号）
CREATE TABLE IF NOT EXISTS SheetSequence (
    Prefix       TEXT    NOT NULL,  -- 单号前缀，如 WT
    SheetDate    TEXT    NOT NULL,  -- 单号日期（yyyyMMdd）
    LastSequence INTEGER NOT NULL,  -- 该前缀该日期已用到的最末流水号（3 位，从 001 起）
    PRIMARY KEY (Prefix, SheetDate)
);

-- 表 TestParameterConfig：方法参数集中配置（键值对，不给每个参数建强类型列）
CREATE TABLE IF NOT EXISTS TestParameterConfig (
    MethodCode TEXT NOT NULL,  -- 方法代号，如 T0715
    ParamKey   TEXT NOT NULL,  -- 参数键，如 LoadRate
    ParamValue TEXT,           -- 参数值（一律按文本存，读时按声明类型解析）
    PRIMARY KEY (MethodCode, ParamKey)
);

-- ==================== 方法 T0715 ====================

-- 表 BendingTestMain：T 0715 试验单主表 —— 一试单一行的头信息（工程信息 / 试验条件 / 统计口径）+ 组统计结果
CREATE TABLE IF NOT EXISTS BendingTestMain (
    SheetID TEXT NOT NULL,             -- 试验单号（主键，形如 WT20261011001）
    UUID TEXT,                         -- 记录唯一标识（GUID）；现役 UUID
    SheetIndex INTEGER,                -- 试验单排序索引；现役 SheetIndex
    ProjectName TEXT,                  -- 工程（项目）名称
    ClientUnits TEXT,                  -- 委托单位
    SupervisionUnits TEXT,             -- 监理单位
    TestSpecificationsId INTEGER,      -- 试验依据（执行标准）代号；现役 TestSpecificationsID
    TestSpecificationsDesc TEXT,       -- 试验依据描述，如 JTG 3410-2025 T 0715-2025
    AcceptanceDate TEXT,               -- 接样日期（yyyy-MM-dd）
    TestObjectiveId INTEGER,           -- 试验目的代号：0=常温弯拉性能（15℃），1=低温弯拉性能（−10℃）
    TestObjective TEXT,                -- 试验目的描述（常温弯拉性能 / 低温弯拉性能）
    TestTemperature REAL,              -- 试验温度（℃）；现役用文本 TestTemperatureDesc，这里用真实类型
    LoadRate REAL,                     -- 加载速率（mm/min），规范 50±5
    SpanLength REAL,                   -- 支点间距（跨径）L（mm），规范 200±0.5；现役放在明细表
    MixtureType TEXT,                  -- 沥青混合料类型；现役 AsphaltMixtureType
    SampleSizeId INTEGER,              -- 试件尺寸代号（按公称最大粒径选取）；现役 SampleSizeID
    SampleSizeDesc TEXT,               -- 试件尺寸描述，如 250×30×35（长×宽×高，mm）
    FormingMethodId INTEGER,           -- 成型方法代号；现役 FormingMethodID
    FormingMethod TEXT,                -- 成型方法描述（轮碾法 / 静压法 等）
    SampleDensity REAL,                -- 试件密度（g/cm³）
    VoidRatio REAL,                    -- 空隙率 VV（%）
    AsphaltUsage REAL,                 -- 沥青用量（%）
    ContentByVolume REAL,              -- 沥青体积百分率（%）
    MineralAggregateVoidRatio REAL,    -- 矿料间隙率 VMA（%）
    AsphaltSaturation REAL,            -- 沥青饱和度 VFA（%）
    StdDevMethodId INTEGER,            -- 标准差计算方法代号；现役 StdDevMethodID
    StdDevMethodDesc TEXT,             -- 标准差计算方法描述（样本 / 总体）
    OutlierDetectionMethodId INTEGER,  -- 超差剔除依据代号：0=最大弯拉应变（规范默认），1=弯拉强度
    OutlierDetectionMethodDesc TEXT,   -- 超差剔除依据描述
    AllowedError REAL,                 -- 最大弯拉应变允许误差（με），规范默认 400；现役存文本 '400'
    FlexuralStrengthAvg REAL,          -- 弯拉强度组平均值 R_B（MPa，精确至 0.01）
    FlexuralStrengthStdDev REAL,       -- 弯拉强度组标准差（MPa）；现役 FlexuralStrength_Std_Dev
    FlexuralStrengthCvPercent REAL,    -- 弯拉强度变异系数 Cv（%）；现役 FlexuralStrength_Cv_Percent
    MaxBendingStrainAvg REAL,          -- 最大弯拉应变组平均值 ε_B（με，精确至 1）
    MaxBendingStrainStdDev REAL,       -- 最大弯拉应变组标准差（με）；现役 MaxBendingTensileStrain_Std_Dev
    MaxBendingStrainCvPercent REAL,    -- 最大弯拉应变变异系数 Cv（%）；现役 MaxBendingTensileStrain_Cv_Percent
    Operator TEXT,                     -- 试验员；现役 Tester
    CreatedOn TEXT,                    -- 创建时间（yyyy-MM-dd HH:mm:ss）；现役 CreateDate
    CompletedOn TEXT,                  -- 完成时间（yyyy-MM-dd HH:mm:ss）
    EffectiveCount INTEGER,            -- 有效试件数（剔除后计入组统计的件数）
    ExcludedCount INTEGER,             -- 被剔除的试件数（规范 5.6 超差剔除）
    NeedsSupplement INTEGER,           -- 是否需补做：1=剔除后不足 3 个需补做，0=不需要（规范 3.4）
    PRIMARY KEY (SheetID)
);

-- 表 BendingTestDetail：T 0715 试件明细表 —— 一试件一行（尺寸 / 单件判定值 / 曲线序列化 / 有效性）
CREATE TABLE IF NOT EXISTS BendingTestDetail (
    SheetID TEXT NOT NULL,         -- 所属试验单号；与 SampleID 一起构成明细主键
    SampleID TEXT NOT NULL,        -- 试件编号，形如 S001（现役同格式，单内顺序号）；与 SheetID 一起构成明细主键
    UUID TEXT,                     -- 记录唯一标识（GUID）；现役 UUID
    SampleDesc TEXT,               -- 试件描述；现役 SampleDesc
    SheetIndex INTEGER,            -- 试件排序索引；现役 SheetIndex
    SampleLength REAL,             -- 试件长度（mm），规范 250
    SampleWidth REAL,              -- 试件宽度 b（mm），规范 30/40/50（按公称最大粒径）
    SampleHeight REAL,             -- 试件高度 h（mm），规范 35/40/50（按公称最大粒径）
    SpanLength REAL,               -- 支点间距（跨径）L（mm），规范 200；现役明细列
    StrainRate REAL,               -- 加载速率（mm/min），默认 50；现役 StrainRate
    TestTemperature REAL,          -- 该试件的试验温度（℃），一般与主表一致；现役 TestTemperatureDesc
    PeakLoadValue REAL,            -- 破坏荷载 P_B（kN）；现役 LoadPeakValue
    PeakDeflection REAL,           -- 跨中挠度峰值 d（mm）；现役 DeformationPeakValue
    OriginCorrection REAL,         -- 原点修正量 O′（mm）；现役 OPrime
    CorrectedDeflection REAL,      -- 修正后跨中挠度（mm）；现役 DeformationCorrected
    TangentK REAL,                 -- 修正直线斜率（荷载-挠度曲线直线段）
    TangentB REAL,                 -- 修正直线截距
    FlexuralStrength REAL,         -- 弯拉强度 R_B = 3·L·P_B / (2·b·h²)（MPa，精确至 0.01）
    MaxBendingTensileStrain REAL,  -- 最大弯拉应变 ε_B = 6·h·d / L²（με，精确至 0.1）
    IsEffective INTEGER,           -- 是否有效：1=计入组统计，0=被剔除；现役 Effective
    InvalidReason TEXT,            -- 无效原因（超出允许误差 / 试验异常 等）
    EndPointIndex INTEGER,         -- 曲线有效段终点在采样序列中的索引；现役 EndPointIndex
    TestStatus INTEGER,            -- 试件试验状态：0=未做（其余取值待确认）；现役 TestStatus
    ChartData TEXT,                -- 荷载-跨中挠度曲线序列化（紧凑 JSON，保留 3 位小数）
    CreateDate TEXT,               -- 创建时间（yyyy-MM-dd HH:mm:ss）
    Remark TEXT,                   -- 备注
    PRIMARY KEY (SheetID, SampleID)
);

-- 表 BendingTestMainTemplate：T 0715 参数模板表 —— 主表去掉组统计列与运行结果列，用作参数预设、继承上次试验信息
CREATE TABLE IF NOT EXISTS BendingTestMainTemplate (
    TemplateID TEXT NOT NULL,          -- 模板标识（主键）；现役沿用一个 SheetID 作主键
    UUID TEXT,                         -- 记录唯一标识（GUID）；现役 UUID
    SheetIndex INTEGER,                -- 排序索引；现役 SheetIndex
    ProjectName TEXT,                  -- 工程（项目）名称
    ClientUnits TEXT,                  -- 委托单位
    SupervisionUnits TEXT,             -- 监理单位
    TestSpecificationsId INTEGER,      -- 试验依据（执行标准）代号
    TestSpecificationsDesc TEXT,       -- 试验依据描述
    AcceptanceDate TEXT,               -- 接样日期（yyyy-MM-dd）
    TestObjectiveId INTEGER,           -- 试验目的代号：0=常温，1=低温
    TestObjective TEXT,                -- 试验目的描述
    TestTemperature REAL,              -- 试验温度（℃）
    LoadRate REAL,                     -- 加载速率（mm/min）
    SpanLength REAL,                   -- 支点间距（跨径）L（mm）
    MixtureType TEXT,                  -- 沥青混合料类型
    SampleSizeId INTEGER,              -- 试件尺寸代号
    SampleSizeDesc TEXT,               -- 试件尺寸描述，如 250×30×35
    FormingMethodId INTEGER,           -- 成型方法代号
    FormingMethod TEXT,                -- 成型方法描述
    SampleDensity REAL,                -- 试件密度（g/cm³）
    VoidRatio REAL,                    -- 空隙率 VV（%）
    AsphaltUsage REAL,                 -- 沥青用量（%）
    ContentByVolume REAL,              -- 沥青体积百分率（%）
    MineralAggregateVoidRatio REAL,    -- 矿料间隙率 VMA（%）
    AsphaltSaturation REAL,            -- 沥青饱和度 VFA（%）
    StdDevMethodId INTEGER,            -- 标准差计算方法代号
    StdDevMethodDesc TEXT,             -- 标准差计算方法描述
    OutlierDetectionMethodId INTEGER,  -- 超差剔除依据代号：0=最大弯拉应变，1=弯拉强度
    OutlierDetectionMethodDesc TEXT,   -- 超差剔除依据描述
    AllowedError REAL,                 -- 最大弯拉应变允许误差（με），默认 400
    Operator TEXT,                     -- 试验员；现役 Tester
    CreateDate TEXT,                   -- 创建时间（yyyy-MM-dd HH:mm:ss）
    PRIMARY KEY (TemplateID)
);

