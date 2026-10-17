-- STS 建表 SQL：由 STS.Tools.SchemaDump 从表定义生成，请勿手改
-- 架构版本：5（库版本对不上时程序报错并要求备份后重建）
-- 生成时间：2026-10-16 17:02:59

-- ==================== 框架表 ====================
-- 表 DbVersion：架构版本（本项目不做迁移，版本对不上直接报错提示备份后重建）
CREATE TABLE IF NOT EXISTS DbVersion (
    Version INTEGER NOT NULL,  -- 架构版本号；当前程序要求 5
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
    MethodCode TEXT NOT NULL,  -- 方法代号，如 ShearStrength
    ParamKey   TEXT NOT NULL,  -- 参数键，如 LoadRate
    ParamValue TEXT,           -- 参数值（一律按文本存，读时按声明类型解析）
    PRIMARY KEY (MethodCode, ParamKey)
);

-- ==================== 方法 ShearStrength ====================

-- 表 ShearStrengthTestMain：抗剪强度（静三轴）试验单主表
CREATE TABLE IF NOT EXISTS ShearStrengthTestMain (
    SheetID TEXT NOT NULL,       -- 试验单号（主键，自动生成）
    CreatedOn TEXT,              -- 试验日期（建单时间，报告 6.4；yyyy-MM-dd HH:mm:ss）
    CompletedOn TEXT,            -- 整单收尾时间（全部试件完成后由「抗剪强度参数」写入；不代表结论定稿）
    ProjectName TEXT,            -- 试验项目名称（报告 6.1；历史列表与查询用）
    UUID TEXT,                   -- 云端去重用 UUID（明细沿用同一枚）
    SampleNumber TEXT,           -- 样品编号（报告 6.2）
    SampleName TEXT,             -- 样品名称（报告 6.2；如「级配碎石」）
    SampleOrigin TEXT,           -- 产地（报告 6.2）
    SampleSpecification TEXT,    -- 规格（报告 6.2；如「0~31.5mm」）
    SampleReceiptDate TEXT,      -- 接样日期（报告 6.3；yyyy-MM-dd）
    SampleDescription TEXT,      -- 样品描述（报告 6.3，自由文本）
    SampleReductionMethod TEXT,  -- 缩分方法（报告 6.4；模板可复用）
    MachineNameAndID TEXT,       -- 主要仪器设备名称、型号及编号（报告 6.5；模板可复用）
    MoldingMethod TEXT,          -- 成型方法（报告 6.6；按本规程 T 1227，模板可复用）
    MoldingParameters TEXT,      -- 成型主要参数（报告 6.6；如分层数 / 击实功 / 目标含水率，模板可复用）
    TestPerson TEXT,             -- 试验员（模板可复用）
    CalculatePerson TEXT,        -- 计算人（模板可复用）
    VerificationPerson TEXT,     -- 校核人（模板可复用）
    CohesionKPa REAL,            -- 黏聚力 c（kPa；式 T 1236-5，收尾计算写入）
    FrictionAngleDeg REAL,       -- 内摩擦角 φ（°；式 T 1236-4，收尾计算写入）
    EnvelopeCorrelation REAL,    -- 相关系数 r（式 T 1236-8；规范要求 ≥0.99）
    EnvelopeReliable INTEGER,    -- 包络线可信标志（0/1：r ≥ 0.99 且斜率可解；供界面提示）
    LevelResultsJson TEXT,       -- 分级结果 JSON：[{sigma3, meanShearStrength, count}, …]（围压级数可扩展，收尾写入）
    PavementSigma1KPa REAL,      -- 「必要时」SSR 输入：路面应力条件 σ1（kPa）
    PavementSigma3KPa REAL,      -- 「必要时」SSR 输入：路面应力条件 σ3（kPa）
    Ssr REAL,                    -- 抗剪强度比 SSR（式 T 1236-11；未计算留空）
    PRIMARY KEY (SheetID)
);

-- 表 ShearStrengthTestDetail：抗剪强度（静三轴）试件明细表
CREATE TABLE IF NOT EXISTS ShearStrengthTestDetail (
    SheetID TEXT NOT NULL,             -- 所属试验单号（复合主键 1/2）
    SampleID TEXT NOT NULL,            -- 试件号（复合主键 2/2，形如 S001）
    SheetIndex INTEGER,                -- 试件序号（试件号里的序号，现役口径，写入时自动回填）
    TestStatus INTEGER,                -- 试验状态（0 未做 / 1 完成）
    UUID TEXT,                         -- 云端去重用 UUID（与本单主表一致）
    CreateDate TEXT,                   -- 明细创建时间（框架写入，yyyy-MM-dd HH:mm:ss）
    ChartData TEXT,                    -- 曲线 JSON（X=轴向变形 mm，Y=轴向荷载 kN）
    ConfiningPressure REAL,            -- 试验围压 σ3（kPa；本方法 30 / 60 / 100，每级 3 件；单件只做一级）
    InitialHeight REAL,                -- 试件初始高度 h0（mm；3 次 120° 测量均值，合格窗 306±5）
    InitialDiameter REAL,              -- 试件初始直径 d0（mm；上 / 中 / 下各测 3 次取均值）
    Mass REAL,                         -- 试件总质量（g；试验后称量，规范 4.7）
    WaterContent REAL,                 -- 含水率 w（%；按本规程 T 1206 测定，精确至 0.1%）
    DryDensity REAL,                   -- 干密度（g/cm³；报告 6.7）
    WaterContentLoss REAL,             -- 含水率损失（%；报告 6.7）
    MaxAxialLoad REAL,                 -- 最大轴向荷载 Pmax（N；规范 4.6 取值口径，式 T 1236-3）
    PeakDeformation REAL,              -- 取 Pmax 时的轴向变形 Δh（mm）
    AxialStrain REAL,                  -- 破坏时轴向应变 ε1（%；式 T 1236-1）
    ShearStrength REAL,                -- 试件剪切强度 σ1max（kPa；式 T 1236-3）
    StoppedAt15PercentStrain INTEGER,  -- 是否因轴向变形超过 15% 而取 15% 处荷载（0/1；规范 4.6）
    IsEffective INTEGER,               -- 是否有效（0/1；现役 Effective 语义；本规范未给剔除规则，先预留）
    SampleDesc TEXT,                   -- 试样描述（文本，允许空）
    InvalidReason TEXT,                -- 无效原因（文本，允许空）
    PRIMARY KEY (SheetID, SampleID)
);

-- 表 ShearStrengthTestTemplate：抗剪强度（静三轴）试验单模板表
CREATE TABLE IF NOT EXISTS ShearStrengthTestTemplate (
    TemplateID TEXT NOT NULL,    -- 模板主键（「<方法代号>-TEMPLATE」）
    MachineNameAndID TEXT,       -- 主要仪器设备名称、型号及编号（模板默认值）
    TestPerson TEXT,             -- 试验员（模板默认值）
    CalculatePerson TEXT,        -- 计算人（模板默认值）
    VerificationPerson TEXT,     -- 校核人（模板默认值）
    SampleReductionMethod TEXT,  -- 缩分方法（模板默认值）
    MoldingMethod TEXT,          -- 成型方法（模板默认值）
    MoldingParameters TEXT,      -- 成型主要参数（模板默认值）
    PRIMARY KEY (TemplateID)
);

