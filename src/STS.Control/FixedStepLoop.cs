namespace STS.Control;

using System.Diagnostics;

/// <summary>
/// 定点周期循环：按固定周期唤醒控制逻辑，用 Stopwatch 补偿处理耗时造成的漂移。
/// **落后过多时重置基准、不补跑**（补跑会把一堆积压周期瞬间灌进控制循环，
/// 对试验机是危险的：宁可丢几个采样点，也不要一个瞬间发出几十条电机指令）。
/// </summary>
public sealed class FixedStepLoop(TimeSpan period, TimeSpan? resetThreshold = null)
{
    private readonly TimeSpan _period = period;
    private readonly TimeSpan _resetThreshold =
        resetThreshold ?? TimeSpan.FromTicks(period.Ticks * 5);
    private readonly Stopwatch _watch = new();
    private long _targetTick;

    /// <summary>周期。</summary>
    public TimeSpan Period => _period;

    /// <summary>已产生的周期数（基准重置后重新计数）。</summary>
    public long TickCount { get; private set; }

    /// <summary>上一拍的实际滞后（负值表示提前唤醒）。</summary>
    public TimeSpan LastLag { get; private set; }

    /// <summary>累计因落后过多而重置基准的次数。</summary>
    public int ResetCount { get; private set; }

    /// <summary>开始计时并清零计数。</summary>
    public void Start()
    {
        _watch.Restart();
        _targetTick = 0;
        TickCount = 0;
        ResetCount = 0;
        LastLag = TimeSpan.Zero;
    }

    /// <summary>停下计时。</summary>
    public void Stop() => _watch.Stop();

    /// <summary>
    /// 等到下一拍。返回本拍**因落后而被跳过的周期数**（0 表示准点，没有跳过）。
    /// </summary>
    public async Task<int> WaitForNextTickAsync(CancellationToken cancellationToken = default)
    {
        _targetTick++;
        var target = TimeSpan.FromTicks(_period.Ticks * _targetTick);
        var lag = _watch.Elapsed - target;

        if (lag > _resetThreshold)
        {
            // 落后太多：重置基准，把这一拍之后的时间轴重新对准现在
            var missed = (int)(lag.Ticks / _period.Ticks);
            _watch.Restart();
            _targetTick = 0;
            TickCount = 0;
            ResetCount++;
            LastLag = lag;
            return missed;
        }

        if (lag < TimeSpan.Zero)
        {
            await Task.Delay(-lag, cancellationToken).ConfigureAwait(false);
        }

        TickCount++;
        LastLag = lag;
        return 0;
    }
}
