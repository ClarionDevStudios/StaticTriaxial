namespace ANX.Ui.ViewModels;

using System.Globalization;
using System.Windows.Media;
using ANX.Control;
using ANX.Core;
using ANX.Core.Control;
using ANX.Ui.Services;
using Wpf.Ui;

/// <summary>
/// 一张实时指标卡（界面用，值随采样刷新）。
/// 显示名与"峰值"文案都按当前语言解析 —— 方法给的是资源键（IAP 同款做法）。
/// </summary>
public partial class MetricCard : ObservableObject
{
    private readonly MetricDefinition _definition;
    private readonly ILocalizationService _localization;

    public MetricCard(MetricDefinition definition, ILocalizationService localization)
    {
        _definition = definition;
        _localization = localization;

        // 换语言后卡片文字要跟着变（数字不动）
        localization.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(PeakText));
        };
    }

    /// <summary>显示名（按当前语言取资源键）。</summary>
    public string DisplayName => _localization.GetString(_definition.DisplayNameKey);

    /// <summary>
    /// 数值区最少宽度（像素）：按卡片数值那套等宽字体量出 <see cref="MetricDefinition.ValueMinChars"/> 个数字的宽。
    /// 0 表示不预留（交给内容决定）。
    /// </summary>
    public double ValueMinWidth =>
        _definition.ValueMinChars > 0 ? _definition.ValueMinChars * DigitWidth : 0;

    /// <summary>单位（不译：IAP 的口径是单位为不译项）。</summary>
    public string Unit => _definition.Unit;

    /// <summary>数值字体（MonoTextStyle / 22px）一个数字的宽，只量一次。</summary>
    private static readonly double DigitWidth = MeasureDigitWidth();

    private static double MeasureDigitWidth()
    {
        var typeface = new Typeface(
            new FontFamily("Consolas"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal
        );

        var text = new FormattedText(
            "0",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            22,
            System.Windows.Media.Brushes.Black,
            1.0
        );

        return text.WidthIncludingTrailingWhitespace;
    }

    public bool ShowPeak => _definition.ShowPeak;

    /// <summary>峰值整句（"峰值 12.3"），格式也归语言资源。</summary>
    public string PeakText => string.Format(_localization.GetString("Exec.PeakFormat"), Peak);

    [ObservableProperty]
    private string _value = "—";

    [ObservableProperty]
    private string _peak = "—";

    partial void OnPeakChanged(string value) => OnPropertyChanged(nameof(PeakText));
}

/// <summary>
/// 试验执行页 ViewModel：设备 → 阶段机 → 判定 → 落库。
/// 它只认 <see cref="ITestExecution"/> 与 <see cref="ITestDeviceController"/>，
/// 不认识 T0715 的公式、表结构与报表。
/// </summary>
public partial class TestExecutionViewModel : ViewModelBase
{
    /// <summary>控制周期：与现役线程一致（20ms / 50Hz）。</summary>
    private static readonly TimeSpan ControlPeriod = TimeSpan.FromMilliseconds(20);

    /// <summary>空闲时数值卡的最低刷新间隔（设备约 190Hz，没必要逐帧刷）。</summary>
    private static readonly TimeSpan LiveReadoutInterval = TimeSpan.FromMilliseconds(50);

    private readonly ITestExecution _execution;
    private readonly ITestDeviceController? _device;
    private readonly ICurrentSheetContext? _currentSheet;
    private readonly INavigationService? _navigation;
    private readonly IReportExportService? _exporter;
    private readonly List<CurvePoint> _curve = [];
    private readonly Dictionary<string, double> _peaks = new(StringComparer.Ordinal);
    private TestSequencer? _sequencer;
    private readonly MotorStallGuard _stallGuard = new();

    /// <summary>
    /// 数据流断了就收尾：下位机 60 秒收不到下行帧会回收会话（真机实测），
    /// 断线时软件不能挂着"运行中"空等。20ms 一拍 → 100 拍 ≈ 2 秒。
    /// </summary>
    private readonly CommunicationLostGuard _communicationGuard = new(maxConsecutiveMisses: 100);

    private int _receivedSampleCount;
    private int _lastSeenSampleCount;
    private CancellationTokenSource? _cts;
    private string? _sheetId;
    private int _sampleCounter;
    private TestSample? _latestSample;
    private DateTime _lastLiveReadout = DateTime.MinValue;

    public TestExecutionViewModel(
        ILocalizationService localization,
        ITestExecution execution,
        ITestDeviceController? device = null,
        ICurrentSheetContext? currentSheet = null,
        INavigationService? navigation = null,
        IReportExportService? exporter = null
    )
        : base(localization)
    {
        _execution = execution;
        _device = device;
        _currentSheet = currentSheet;
        _navigation = navigation;
        _exporter = exporter;

        foreach (var definition in execution.Metrics)
        {
            Cards.Add(new MetricCard(definition, localization));
        }

        ConnectionText = device is null ? L("Exec.NoDevice") : L("Exec.NotConnected");
        PhaseText = L("Exec.PhaseIdle");
    }

    /// <summary>方法名（标题栏用）。</summary>
    public string HeaderText => $"{_execution.MethodName}（{_execution.MethodCode}）";

    /// <summary>规范依据。</summary>
    public string StandardText => _execution.StandardReference;

    public ObservableCollection<MetricCard> Cards { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    private string _connectionText = string.Empty;

    [ObservableProperty]
    private string _phaseText = string.Empty;

    [ObservableProperty]
    private string _sheetText = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    /// <summary>能不能点动工作台：已联机，且没有在跑试验（免得手动点动和加载抢电机）。</summary>
    [ObservableProperty]
    private bool _canJog;

    /// <summary>曲线新增了一个点（页面据此重绘）。</summary>
    public event EventHandler<CurvePoint>? CurvePointAdded;

    /// <summary>曲线被清空（新一轮试验）。</summary>
    public event EventHandler? CurveCleared;

    /// <summary>曲线横轴标题（按当前语言；语言切换后由页面重新取一次）。</summary>
    public string CurveXAxisTitle => L(_execution.CurveXAxisKey);

    /// <summary>曲线纵轴标题（按当前语言）。</summary>
    public string CurveYAxisTitle => L(_execution.CurveYAxisKey);

    /// <summary>联机。</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            await _device.ConnectAsync();
            ConnectionText = L("Exec.Connected");
            Append($"{L("Exec.Connected")} · {_device.DeviceName}");
            UpdateCanJog();
        }
        catch (Exception ex)
        {
            ConnectionText = L("Exec.ConnectFailed");
            Append($"{L("Exec.ConnectFailed")}: {ex.Message}");
        }
    }

    /// <summary>
    /// 按住"工作台上升"：界面在鼠标按下时调用，松开时调 <see cref="EndJogAsync"/>。
    /// 走设备契约的 <see cref="ITestDeviceController.JogAsync"/> —— 界面不认识 GR-3 的 C4 方向字。
    /// </summary>
    public async Task BeginJogAsync(JogDirection direction)
    {
        if (_device is null || !_device.IsConnected)
        {
            Append(L("Exec.JogNeedConnect"));
            return;
        }

        try
        {
            await _device.JogAsync(direction);
            Append(L(direction == JogDirection.Up ? "Exec.JogUp" : "Exec.JogDown"));
        }
        catch (Exception ex)
        {
            Append($"{L("Exec.JogFailed")}: {ex.Message}");
        }
    }

    /// <summary>松开工作台按钮（或鼠标移出按钮）：停止点动。</summary>
    public async Task EndJogAsync()
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            await _device.StopAsync();
            Append(L("Exec.JogStopped"));
        }
        catch (Exception ex)
        {
            Append($"{L("Exec.JogFailed")}: {ex.Message}");
        }
    }

    /// <summary>点动按钮的可用性：联机中、且没在跑试验。</summary>
    partial void OnIsRunningChanged(bool value) => UpdateCanJog();

    private void UpdateCanJog() => CanJog = !IsRunning && _device?.IsConnected == true;

    /// <summary>
    /// 进入执行页就开始把设备的实时采样刷到数值卡上 —— 不跑试验时也能随时看到当前荷载 / 位移
    /// （操作者要"看着数值接近试件"）。离开页面退订；曲线只在跑试验时画，所以这里只刷卡片。
    /// </summary>
    public void BeginLiveReadout()
    {
        if (_device is null)
        {
            return;
        }

        // 先减后加：重复进入页面不会把同一处理器挂两遍
        _device.SampleReceived -= OnLiveSample;
        _device.SampleReceived += OnLiveSample;
    }

    /// <summary>离开执行页：停止实时刷新。</summary>
    public void EndLiveReadout()
    {
        if (_device is not null)
        {
            _device.SampleReceived -= OnLiveSample;
        }
    }

    /// <summary>
    /// 空闲时的实时采样：只刷新数值卡，不画曲线 —— 曲线归试验那一轮。
    /// 设备约 190Hz，界面按 20Hz 刷就够了。
    /// </summary>
    private void OnLiveSample(object? sender, TestSample sample)
    {
        var now = DateTime.UtcNow;
        if (now - _lastLiveReadout < LiveReadoutInterval)
        {
            return;
        }

        _lastLiveReadout = now;
        UpdateCards(sample);
    }

    /// <summary>开始 / 停止一个试件。</summary>
    [RelayCommand]
    private async Task StartOrStopAsync()
    {
        if (IsRunning)
        {
            _cts?.Cancel();
            Append(L("Exec.Stopping"));
            return;
        }

        if (_device is null)
        {
            Append(L("Exec.NoDevice"));
            return;
        }

        try
        {
            await RunOneSampleAsync();
        }
        catch (Exception ex)
        {
            Append($"{L("Exec.Failed")}: {ex.Message}");
        }
    }

    /// <summary>完成一组：算组统计并标记试验单完成。</summary>
    [RelayCommand]
    private async Task CompleteSheetAsync()
    {
        if (_sheetId is null)
        {
            return;
        }

        await _execution.CompleteSheetAsync(_sheetId);
        Append($"{L("Exec.SheetCompleted")} {_sheetId}");
    }

    /// <summary>清空信息。</summary>
    [RelayCommand]
    private void ClearLog() => Log.Clear();

    /// <summary>去试验单页（新建 / 选择已有）。</summary>
    [RelayCommand]
    private void OpenSheetSelection() =>
        _navigation?.Navigate(typeof(Views.Pages.SheetSelectionPage));

    /// <summary>去参数配置页。</summary>
    [RelayCommand]
    private void OpenMachineSettings() =>
        _navigation?.Navigate(typeof(Views.Pages.MachineSettingsPage));

    /// <summary>去试样编辑页。</summary>
    [RelayCommand]
    private void OpenSampleEditor() =>
        _navigation?.Navigate(typeof(Views.Pages.SampleEditorPage));

    /// <summary>去历史数据页（报表导出的入口在那里）。</summary>
    [RelayCommand]
    private void OpenHistory() => _navigation?.Navigate(typeof(Views.Pages.HistoryPage));

    /// <summary>打印当前试验单的报表 —— 与历史页导出**同一套实现**，只是目标不同（当前 vs 选中）。</summary>
    [RelayCommand]
    private async Task PrintReportAsync()
    {
        if (_sheetId is null || _exporter is null)
        {
            Append(L("Exec.NoSheet"));
            return;
        }

        try
        {
            var path = await _exporter.ExportAsync(_sheetId, "zh-CN");
            Append($"{L("History.Exported")} {path}");
        }
        catch (Exception ex)
        {
            Append($"{L("History.ExportFailed")}: {ex.Message}");
        }
    }

    private async Task RunOneSampleAsync()
    {
        // 试验单页选过单就用它；否则自己建一张
        if (_currentSheet?.SheetId is { Length: > 0 } selected
            && !string.Equals(selected, _sheetId, StringComparison.Ordinal))
        {
            _sheetId = selected;
            Append($"{L("Exec.SheetSelected")} {selected}");
        }

        _sheetId ??= await _execution.CreateSheetAsync(
            projectName: _currentSheet?.ProjectName ?? L("Exec.DefaultProject"),
            numberPrefix: "WT"
        );
        SheetText = _sheetId;

        // 试件号由方法侧按库里已有明细算，避免覆盖同号试件
        var sampleId = await _execution.NextSampleIdAsync(_sheetId);
        _sampleCounter++;

        ResetLiveValues();
        _curve.Clear();
        CurveCleared?.Invoke(this, EventArgs.Empty);

        // 运行参数从**参数表**来（接触阈值 / 破坏判据 / 确认帧数 / 加载速率），不再用写死的默认值
        var requested = await _execution.LoadProfileAsync();

        // 闸门都按**设备量程**算：位移闸门落在量程内（顶过会压坏传感器），荷载闸门 = 量程 × 系数
        //（现场位移量程 25.000mm、压力量程 100.000kN；两者此前都是写死的错值）
        var profile = requested
            .ClampDisplacementToDeviceRange(_device?.DisplacementRangeMillimeters)
            .ClampLoadToDeviceRange(_device?.LoadRangeKN);

        if (profile.MaxDisplacementMM < requested.MaxDisplacementMM)
        {
            Append(string.Format(CultureInfo.CurrentCulture, L("Exec.DisplacementClamped"), profile.MaxDisplacementMM));
        }

        if (Math.Abs(profile.MaxLoadKN - requested.MaxLoadKN) > 0.0001)
        {
            Append(string.Format(CultureInfo.CurrentCulture, L("Exec.LoadClamped"), profile.MaxLoadKN));
        }

        var sequencer = new TestSequencer(profile);
        _sequencer = sequencer;
        _stallGuard.Reset();
        _communicationGuard.Reset();
        _lastSeenSampleCount = _receivedSampleCount;
        sequencer.Start();

        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 设备帧只落到"最新一帧"；判定与界面刷新由控制循环按 20ms 节拍推进
        void OnSample(object? sender, TestSample sample)
        {
            _latestSample = sample;
            _receivedSampleCount++;
        }

        var device = _device!;
        device.SampleReceived += OnSample;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var controlLoop = new FixedStepLoop(ControlPeriod);
        _latestSample = null;

        try
        {
            if (!device.IsConnected)
            {
                await device.ConnectAsync(_cts.Token);
                ConnectionText = L("Exec.Connected");
            }

            device.ElapsedSeconds = 0;
            controlLoop.Start();
            var controlToken = _cts.Token;

            /**
             * 控制循环：与现役线程同构 —— 固定 20ms 节拍，每拍拿最新一帧、
             * 用循环的时间推进 ElapsedSeconds（A2 帧不带时间戳）、喂阶段机、刷界面。
             * 采样率再高（真机约 190Hz）界面也只按 50Hz 刷，等于顺带做了节流。
             */
            var loopTask = Task.Run(async () =>
            {
                try
                {
                    while (!controlToken.IsCancellationRequested)
                    {
                        _ = await controlLoop.WaitForNextTickAsync(controlToken).ConfigureAwait(false);

                        var sampleArrived = _receivedSampleCount != _lastSeenSampleCount;
                        _lastSeenSampleCount = _receivedSampleCount;

                        // 数据流断了（下位机静默超时 / 拔网线）：软件必须收尾，不能挂在"运行中"空等
                        if (_communicationGuard.Register(sequencer.Phase, sampleArrived))
                        {
                            sequencer.Stop(TestStopReason.CommunicationLost);
                            _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                                () => Append(L("Exec.CommunicationLost"))
                            );
                            finished.TrySetResult(true);
                            return;
                        }

                        // 命令在加载而机器不转（手动停机 / 撞限位 / 伺服保护）：立刻收尾
                        if (
                            _stallGuard.Register(
                                sequencer.Phase,
                                device.IsMotorRunning,
                                ControlPeriod.TotalSeconds
                            )
                        )
                        {
                            sequencer.Stop(TestStopReason.DeviceError);
                            _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                                () => Append(L("Exec.MotorStalled"))
                            );
                            finished.TrySetResult(true);
                            return;
                        }

                        if (_latestSample is not { } latest)
                        {
                            continue; // 联机阶段下位机本就不回送数据
                        }

                        device.ElapsedSeconds += ControlPeriod.TotalSeconds;
                        var sample = latest with { ElapsedSeconds = device.ElapsedSeconds };

                        var phaseBefore = sequencer.Phase;

                        if (!sequencer.OnSample(sample))
                        {
                            finished.TrySetResult(true);
                            return;
                        }

                        if (
                            phaseBefore == TestPhase.ContactSearch
                            && sequencer.Phase == TestPhase.Loading
                        )
                        {
                            // 接触成立：**上位机软件调零**（存偏移量，不动设备零点）并切到试验速率
                            // —— 与现役线程的"4.4 传感器调零 + 提速"一致（ZeroAxialDisplacement + SetVelocityCmd）
                            await device.ZeroDisplacementAsync(controlToken).ConfigureAwait(false);
                            await device
                                .SetSpeedAsync(profile.LoadRateMillimetersPerMinute, controlToken)
                                .ConfigureAwait(false);

                            _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                                () => Append(L("Exec.ContactDetected"))
                            );
                        }

                        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                            () => UpdateLiveValues(sample)
                        );
                    }
                }
                catch (OperationCanceledException)
                {
                    // 停止 / 取消：正常结束
                }
            });

            // 空走阶段用接近速率（比试验速率慢），接触后再提速
            await device.SetSpeedAsync(profile.ApproachSpeedMillimetersPerMinute, _cts.Token);
            await device.StartAsync(_cts.Token);

            var completed = await Task.WhenAny(
                finished.Task,
                Task.Delay(TimeSpan.FromMinutes(10), _cts.Token)
            );

            if (completed != finished.Task)
            {
                sequencer.Stop(TestStopReason.LoadingTimeout);
            }

            await _cts.CancelAsync();
            await loopTask;
        }
        finally
        {
            device.SampleReceived -= OnSample;
            await device.StopAsync(CancellationToken.None);
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }

        PhaseText = sequencer.StopReason switch
        {
            TestStopReason.Completed => L("Exec.PhaseCompleted"),
            TestStopReason.None => L("Exec.PhaseIdle"),
            TestStopReason.LoadRangeExceeded or TestStopReason.DisplacementRangeExceeded =>
                $"{L("Exec.PhaseRangeStopped")}（{sequencer.StopReason}）",
            _ => $"{L("Exec.PhaseAborted")}（{sequencer.StopReason}）",
        };

        if (!sequencer.CanSettle)
        {
            Append($"{L("Exec.NotSettleable")}（{sequencer.StopReason}）");
            return;
        }

        var values = await _execution.CompleteSampleAsync(_sheetId, sampleId, sequencer.Curve);
        if (values is not null)
        {
            foreach (var (key, value) in values)
            {
                var card = FindCard(key);
                if (card is not null)
                {
                    card.Value = Format(value);
                }
            }
        }

        Append($"{L("Exec.SampleDone")} #{sampleId} · {sequencer.Curve.Count} {L("Exec.Points")}");
    }

    private void UpdateLiveValues(TestSample sample)
    {
        UpdateCards(sample);

        _curve.Add(new CurvePoint(sample.Displacement, sample.Load));
        CurvePointAdded?.Invoke(this, new CurvePoint(sample.Displacement, sample.Load));
    }

    /// <summary>刷新数值卡（荷载 / 位移 / 时间）；点动与跑试验共用。</summary>
    private void UpdateCards(TestSample sample)
    {
        SetCard("load", sample.Load, sample.Load);
        SetCard("displacement", sample.Displacement, sample.Displacement);
        SetCard("elapsed", sample.ElapsedSeconds, sample.ElapsedSeconds);
    }

    private void SetCard(string key, double value, double peakCandidate)
    {
        var card = FindCard(key);
        if (card is null)
        {
            return;
        }

        card.Value = Format(value);

        if (!card.ShowPeak)
        {
            return;
        }

        _peaks.TryGetValue(key, out var peak);
        if (Math.Abs(peakCandidate) > Math.Abs(peak))
        {
            peak = peakCandidate;
            _peaks[key] = peak;
        }

        card.Peak = Format(peak);
    }

    private void ResetLiveValues()
    {
        _peaks.Clear();
        foreach (var card in Cards)
        {
            card.Value = "—";
            card.Peak = "—";
        }
    }

    private MetricCard? FindCard(string key)
    {
        var index = _execution.Metrics
            .Select((m, i) => (m, i))
            .FirstOrDefault(pair => string.Equals(pair.m.Key, key, StringComparison.Ordinal))
            .i;

        return index >= 0 && index < Cards.Count ? Cards[index] : null;
    }

    private void Append(string message) =>
        Log.Insert(
            0,
            $"{DateTime.Now:HH:mm:ss}  {message}"
        );

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
