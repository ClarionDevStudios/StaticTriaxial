namespace ANX.Control;

using ANX.Core;

/// <summary>
/// 试验阶段机。**由采样驱动**、不持有定时器与线程：喂它 TestSample，它推进阶段。
/// 这样阶段流转、保护与曲线抽点都能用单元测试与回放验证，不必依赖真机或真实时间。
///
/// 阶段：Idle → Connecting → ContactSearch →（接触）Loading →（荷载下降）Breaking →（连续确认）Resetting →（到时）Completed。
/// 保护：荷载超量程 / 位移超正行程 / 接触超时 / 加载超时 → Aborted。
/// </summary>
public sealed class TestSequencer(TestRunProfile? profile = null)
{
    private readonly TestRunProfile _profile = profile ?? new TestRunProfile();
    private readonly List<CurvePoint> _curve = [];
    private int _breakConfirmCount;
    private double _resetElapsedSeconds;
    private double? _lastSampleSeconds;
    private TestSample _peak;

    /// <summary>当前阶段。</summary>
    public TestPhase Phase { get; private set; } = TestPhase.Idle;

    /// <summary>停止原因；未结束时为 TestStopReason.None。</summary>
    public TestStopReason StopReason { get; private set; } = TestStopReason.None;

    /// <summary>已记录的曲线（接触判定成立后才开始记录，与现役一致）。</summary>
    public IReadOnlyList<CurvePoint> Curve => _curve;

    /// <summary>峰值采样。</summary>
    public TestSample Peak => _peak;

    /// <summary>运行参数。</summary>
    public TestRunProfile Profile => _profile;

    /// <summary>
    /// 是否可以结算。与现役线程一致：**量程停机也算**（已捕获破坏点的按峰值结算；
    /// 未捕获的按"无明显峰值"结束，由判定器取曲线最大荷载结算），只要曲线非空即可。
    /// 超时与人工停止不算。
    /// </summary>
    public bool CanSettle
    {
        get
        {
            var settleable =
                StopReason == TestStopReason.Completed
                || StopReason == TestStopReason.LoadRangeExceeded
                || StopReason == TestStopReason.DisplacementRangeExceeded;

            return settleable && _curve.Count > 0;
        }
    }

    /// <summary>是否已结束（完成或中止）。</summary>
    public bool IsFinished => StopReason != TestStopReason.None;

    /// <summary>开始试验（进入联机阶段；该阶段下位机本就不回送数据，不按通讯中断判）。</summary>
    public void Start()
    {
        Phase = TestPhase.Connecting;
        StopReason = TestStopReason.None;
        _curve.Clear();
        _breakConfirmCount = 0;
        _resetElapsedSeconds = 0;
        _lastSampleSeconds = null;
        _peak = default;
    }

    /// <summary>人工停止。</summary>
    public void Stop(TestStopReason reason = TestStopReason.ManualStop)
    {
        if (IsFinished)
        {
            return;
        }

        StopReason = reason;
        Phase = TestPhase.Aborted;
    }

    /// <summary>喂一帧采样推进阶段机；返回 false 表示已结束。</summary>
    public bool OnSample(TestSample sample)
    {
        if (IsFinished)
        {
            return false;
        }

        if (Phase == TestPhase.Idle)
        {
            throw new InvalidOperationException("试验尚未开始（先调用 Start）");
        }

        // 保护一：荷载 / 位移正向超限（用原始值判定，与现役一致）
        if (sample.Load > _profile.MaxLoadKN)
        {
            return Abort(TestStopReason.LoadRangeExceeded);
        }

        if (sample.Displacement > _profile.MaxDisplacementMM)
        {
            return Abort(TestStopReason.DisplacementRangeExceeded);
        }

        switch (Phase)
        {
            case TestPhase.Connecting:
                Phase = TestPhase.ContactSearch;
                break;

            case TestPhase.ContactSearch:
                if (sample.ElapsedSeconds > _profile.ContactTimeoutSeconds)
                {
                    return Abort(TestStopReason.ContactTimeout);
                }

                if (Math.Abs(sample.Load) >= _profile.ContactLoadKN)
                {
                    Phase = TestPhase.Loading;
                    _peak = sample;
                    _curve.Add(new CurvePoint(sample.Displacement, sample.Load));
                }

                break;

            case TestPhase.Loading:
                if (sample.ElapsedSeconds > _profile.LoadingTimeoutSeconds)
                {
                    return Abort(TestStopReason.LoadingTimeout);
                }

                _curve.Add(new CurvePoint(sample.Displacement, sample.Load));

                if (sample.Load > _peak.Load)
                {
                    _peak = sample;
                    _breakConfirmCount = 0;
                }
                else if (_peak.Load - sample.Load >= _profile.BreakDropKN)
                {
                    Phase = TestPhase.Breaking;
                    _breakConfirmCount = 1;
                }

                break;

            case TestPhase.Breaking:
                _curve.Add(new CurvePoint(sample.Displacement, sample.Load));

                if (sample.Load > _peak.Load)
                {
                    // 又创新高 → 视为未破坏，回加载阶段
                    _peak = sample;
                    _breakConfirmCount = 0;
                    Phase = TestPhase.Loading;
                    break;
                }

                _breakConfirmCount++;
                if (_breakConfirmCount >= _profile.BreakConfirmSamples)
                {
                    Phase = TestPhase.Resetting;
                    _resetElapsedSeconds = 0;
                }

                break;

            case TestPhase.Resetting:
                if (_lastSampleSeconds is { } previous)
                {
                    _resetElapsedSeconds += Math.Max(0, sample.ElapsedSeconds - previous);
                }

                if (_resetElapsedSeconds >= _profile.ResetSeconds)
                {
                    StopReason = TestStopReason.Completed;
                    Phase = TestPhase.Completed;
                    return false;
                }

                break;

            default:
                return false;
        }

        _lastSampleSeconds = sample.ElapsedSeconds;
        return true;
    }

    private bool Abort(TestStopReason reason)
    {
        StopReason = reason;
        Phase = TestPhase.Aborted;
        return false;
    }
}
