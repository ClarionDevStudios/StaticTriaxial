namespace STS.Communication.Channels;

using STS.Core;

/// <summary>
/// 模拟用的**试件响应**（纯函数）：给一个跨中挠度，返回荷载。
///
/// 用户 2026-10-15："使用模拟器进行试验时，生成的数据能否更贴近实际？" —— 原先的模型是
/// "6 秒线性加载到峰值、再 2 秒线性回落 35%"（一条完美折线，末尾还会把荷载拉到负值）。
/// 现在按**真机试件**该有的样子来（骨架期沿用原小梁参数；静三轴模拟数据待按协议重做）：
///
/// | 特征 | 取值 | 依据 |
/// | --- | --- | --- |
/// | 破坏荷载 | 约 1~2kN（默认 1.5kN） | 弯拉强度 R = 3FL/(2bh²) = 122.5·F，8~15MPa ↔ 1.0~1.8kN |
/// | 破坏时跨中挠度 | 约 0.4~0.9mm（默认 0.7mm） | 弯拉应变 ε = 6hδ/L² ≈ 3700με，落在常见区间 |
/// | 接触后 | **一段"坐实段"**：挠度在走、荷载涨得慢（约峰挠度的前 30%） | 压头压上、试件与支座找平、支座微沉；真机曲线都有这一段（用户 2026-10-15：要从接触点到线性上升之间"适当延长"） |
/// | 上升段形状 | **大部分接近直线，只在接近峰值时圆下来** | 原真机 15℃ 小梁件的荷载–挠度特征（骨架期暂留）；也正好满足判定的"修正直线"拟合（10%~80% 区间要找到 R² ≥ 0.99 的直线段） |
/// | 过峰后 | **陡降**到峰值的百分之几，之后一条低位尾巴 | 破坏（开裂）后承载力骤失 |
/// | 接触前 | 荷载 ≈ 0 | 压头还没碰到试件；接触那一刻就是挠度零点 |
///
/// 特意独立成文件、不放进"仅 Debug"的范围：它**不依赖模拟器设备**，
/// 回放测试与阶段机测试也要用它（否则 Release 构建会因为引用不到模拟器而编译失败）。
/// </summary>
public static class SampleCurve
{
    /// <summary>接触前平台要空走的行程（mm）：真机上"压头还没碰到试件"那一段。</summary>
    public const double DefaultContactGapMM = 0.8;

    /// <summary>上升到峰挠度这个比例之前**基本是直线**（之后才开始圆）。</summary>
    private const double LinearPortionRatio = 0.75;

    /// <summary>圆膝处达到峰值的比例（与 <see cref="LinearPortionRatio"/> 一起定出上升段的斜率）。</summary>
    private const double KneeLoadRatio = 0.85;

    /// <summary>
    /// 接触后"坐实段"占峰挠度的比例。**这一段必须明显长于接触阈值带来的截断**：
    /// 上位机在荷载达到接触阈值（默认 0.05kN）那一刻把位移调零，坐实段的前半截因此不会进曲线 ——
    /// 若坐实段末端荷载只比阈值高一点点，录下来的坐实段就只剩一两个点，看不出东西。
    /// </summary>
    private const double SeatPortionRatio = 0.30;

    /// <summary>坐实段末端的荷载（占峰值）。默认 1.5kN 时是 0.12kN，比接触阈值 0.05kN 高出一倍多。</summary>
    private const double SeatLoadRatio = 0.08;

    /// <summary>坐实段的形状指数（&gt; 1：越压越实，略微上翘；末端斜率约为上升段的 1/4）。</summary>
    private const double SeatExponent = 1.5;

    /// <summary>
    /// 试件的**荷载–挠度响应**（纯函数）：挠度 ≤ 0 → 0；接触后先"坐实"、再直线上升、接近峰值圆下来；
    /// 过峰后陡降 + 低位尾巴。
    /// 全程不会出现负荷载（旧模型末尾会掉到 −26kN，那是假的）。
    /// </summary>
    /// <param name="deflectionMM">跨中挠度（接触零点起算，mm）。</param>
    /// <param name="peakLoadKN">破坏荷载（kN）。</param>
    /// <param name="deflectionAtPeakMM">破坏时跨中挠度（mm）。</param>
    public static double LoadAt(double deflectionMM, double peakLoadKN, double deflectionAtPeakMM)
    {
        if (deflectionMM <= 0 || deflectionAtPeakMM <= 0)
        {
            return 0;
        }

        var u = deflectionMM / deflectionAtPeakMM;
        if (u <= 1)
        {
            // ① 坐实段（前 30% 挠度）：挠度在走、荷载涨得慢。
            //    用户 2026-10-15："从起始点（模拟试件接触）到曲线呈线性增长之间适当延长" ——
            //    原先这一段是**零长**（接触后立刻直线上升），曲线看着又硬又不真。
            if (u <= SeatPortionRatio)
            {
                return peakLoadKN
                    * SeatLoadRatio
                    * Math.Pow(u / SeatPortionRatio, SeatExponent);
            }

            // ② 上升段：接近直线（判定的"修正直线"拟合就落在这段上 —— 10%~80% 峰值荷载
            //    整段在这个区间里，所以坐实段别再往右伸，否则拟合的 R² 会掉下来）
            if (u <= LinearPortionRatio)
            {
                var linear = (u - SeatPortionRatio) / (LinearPortionRatio - SeatPortionRatio);
                return peakLoadKN * (SeatLoadRatio + ((KneeLoadRatio - SeatLoadRatio) * linear));
            }

            // ③ 圆膝：剩下 15% 荷载用一个二次项圆下来，到峰挠度正好到峰值
            var knee = (1 - u) / (1 - LinearPortionRatio);
            return peakLoadKN * (1 - ((1 - KneeLoadRatio) * knee * knee));
        }

        // 过峰：陡降到峰值的百分之几，之后一条低位尾巴（衰减尺度取峰挠度的 9%）
        var beyond = deflectionMM - deflectionAtPeakMM;
        var drop = Math.Exp(-beyond / (0.09 * deflectionAtPeakMM));
        return peakLoadKN * (0.04 + (0.96 * drop));
    }

    /// <summary>
    /// 按"平台以 <paramref name="speedMillimetersPerMinute"/> **匀速**走 <paramref name="seconds"/> 秒"
    /// 合成一个采样（给测试与匀速场景用）。
    /// 返回的 <c>Displacement</c> 是**平台行程**（raw），接触调零后净值才等于挠度 ——
    /// 与真机控制器同口径（净值 = 原始 − 偏移，见 <c>SimulatedDeviceController</c>）。
    /// </summary>
    public static TestSample At(
        double seconds,
        double peakLoadKN,
        double deflectionAtPeakMM,
        double speedMillimetersPerMinute = 50.0,
        double contactGapMM = DefaultContactGapMM
    )
    {
        var travel = speedMillimetersPerMinute / 60.0 * seconds;
        var load = LoadAt(travel - contactGapMM, peakLoadKN, deflectionAtPeakMM);

        return new TestSample(load, travel, seconds);
    }
}
