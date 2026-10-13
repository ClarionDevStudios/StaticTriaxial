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

        if (device is null)
        {
            ConnectionText = L("Exec.NoDevice");
        }
        else
        {
            SetConnectionState(null); // 还没联机：中性色
        }

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

    /// <summary>连接状态文字的颜色：成功绿、失败红、未连接中性（用户 2026-10-12）。</summary>
    [ObservableProperty]
    private Brush? _connectionBrush;

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

    /// <summary>
    /// **试样完成后**给出曲线要标注的拟合信息（峰值点由页面自己从曲线取）。
    /// 用户 2026-10-13 定的口径：只在完成时标注，不放过程中；参数表关掉原点修正时 <c>TangentK</c> = 0
    /// （页面据此不画直线与交点）。
    /// </summary>
    public event EventHandler<CurveAnnotation>? CurveAnnotated;

    /// <summary>
    /// **是否已接触试样**。点"启动试验"时置 false —— **接触成立**才置 true（用户 2026-10-12）：
    /// 它同时管三件事：① 峰值只在接触后累计；② 「试验持续时间」只在接触后显示；
    /// ③ 点启动时峰值/计时清零。一轮结束（finally）置回 false：显示值保留，但不再被空闲实时读数改写。
    ///
    /// 注意：设备侧的内部计时（<c>device.ElapsedSeconds</c>）**一直在走**，接触搜索超时靠它。
    /// </summary>
    private bool _contactEstablished;

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
            SetConnectionState(true);
            Append($"{L("Exec.Connected")} · {_device.DeviceName}");
            UpdateCanJog();

            // 联机成功后立刻按参数表把采集口径推给设备：否则实时读数用的是适配器写死的默认通道，
            // 与参数表里选的"位移1+2通道平均值"不一致（用户 2026-10-12 真机发现）。
            await ApplyAcquisitionSettingsFromStoreAsync();
        }
        catch (Exception ex)
        {
            SetConnectionState(false);
            Append($"{L("Exec.ConnectFailed")}: {ex.Message}");
        }
    }

    /// <summary>
    /// 连接状态文字与颜色一起更新：<c>true</c> 已连接（绿）、<c>false</c> 联机失败（红）、
    /// <c>null</c> 未连接（中性）。颜色取工程主题里的语义色（WPF-UI 的 Success / Critical）。
    /// </summary>
    private void SetConnectionState(bool? connected)
    {
        ConnectionText = connected switch
        {
            true => L("Exec.Connected"),
            false => L("Exec.ConnectFailed"),
            _ => L("Exec.NotConnected"),
        };

        ConnectionBrush =
            System.Windows.Application.Current?.TryFindResource(
                connected switch
                {
                    true => "SystemFillColorSuccessBrush",
                    false => "SystemFillColorCriticalBrush",
                    _ => "TextFillColorSecondaryBrush",
                }
            ) as Brush;
    }

    /// <summary>
    /// 把参数表里的**采集口径**推给设备适配器：压力通道 / 位移通道（含双传感器两路平均）/ 平台升降速率。
    /// 调用点两处：**联机成功后**、**每次开始试验时**（另有参数配置页保存时的即时生效）。
    /// </summary>
    private void ApplyAcquisitionSettings(TestRunProfile profile)
    {
        if (_device is null)
        {
            return;
        }

        _device.PressChannelNumber = profile.PressChannelNumber;
        _device.DisplacementSource = profile.DisplacementSource;
        _device.JogSpeedMillimetersPerMinute = profile.JogSpeedMillimetersPerMinute;
    }

    /// <summary>从参数表取一次当前口径再推给设备（联机成功时用）。</summary>
    private async Task ApplyAcquisitionSettingsFromStoreAsync()
    {
        if (_device is null)
        {
            return;
        }

        ApplyAcquisitionSettings(await _execution.LoadProfileAsync());
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
            // 平台升降速率与试验速率分开（用户 2026-10-12）：按下按钮时按参数表的当前值走
            var profile = await _execution.LoadProfileAsync();
            _device.JogSpeedMillimetersPerMinute = profile.JogSpeedMillimetersPerMinute;

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
    partial void OnIsRunningChanged(bool value)
    {
        UpdateCanJog();
        OnPropertyChanged(nameof(StartStopText));
        OnPropertyChanged(nameof(StartStopSymbol));
    }

    /// <summary>启动/停止按钮的文字：待机"启动试验"，试验中"停止试验"（用户 2026-10-12）。</summary>
    public string StartStopText => L(IsRunning ? "Exec.StopTest" : "Exec.StartTest");

    /// <summary>启动/停止按钮的图标：待机播放、试验中停止。</summary>
    public Wpf.Ui.Controls.SymbolRegular StartStopSymbol =>
        IsRunning ? Wpf.Ui.Controls.SymbolRegular.Stop24 : Wpf.Ui.Controls.SymbolRegular.Play24;

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

        // 峰值只在试验过程中/结束后显示：刚进页面先置空（用户 2026-10-12）
        ClearPeaks();
    }

    /// <summary>把各卡片的峰值清成"—"（不动当前值）。</summary>
    private void ClearPeaks()
    {
        _peaks.Clear();
        foreach (var card in Cards.Where(card => card.ShowPeak))
        {
            card.Peak = "—";
        }
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
    private void StartOrStop()
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

        // 点"启动试验"**立刻**把峰值清零、清空上一轮曲线：峰值只反映本轮（用户 2026-10-12）。
        ResetLiveValues();
        _contactEstablished = false; // 接触成立后才开始累计峰值、才显示试验计时
        CurveCleared?.Invoke(this, EventArgs.Empty);

        // **不能 await 整轮**：命令若等到整轮结束才完成，执行期间按钮会被置灰，
        // 结果是"试验一开始就再也点不到停止"（用户 2026-10-12 真机踩到）。
        // 立刻返回 → 按钮保持可用 → 下一次点击才走到上面的"停止"分支。
        _ = RunOneSampleSafeAsync();
    }

    /// <summary>跑一轮并把异常报给界面（fire-and-forget：命令必须立刻返回，否则停止按钮被置灰）。</summary>
    private async Task RunOneSampleSafeAsync()
    {
        try
        {
            await RunOneSampleAsync();
        }
        catch (Exception ex)
        {
            Append($"{L("Exec.Failed")}: {ex.Message}");
        }
    }

    /// <summary>
    /// 有效性计算（现役 bending 窗体的「有效性计算」按钮）：按 T 0715-2025 5.6 做有效性判定
    /// （与平均值偏差超过允许误差者剔除）并写回主表/明细；**不**标记试验单完成。
    /// 与现役一致：试验进行中不允许计算。
    /// </summary>
    [RelayCommand]
    private async Task EvaluateValidityAsync()
    {
        if (IsRunning)
        {
            Append(L("Exec.ValidityBusy"));
            return;
        }

        if (_sheetId is null)
        {
            Append(L("Exec.ValidityNoSheet"));
            return;
        }

        try
        {
            var report = await _execution.EvaluateValidityAsync(_sheetId);

            if (report.Evaluated == 0)
            {
                Append(L("Exec.ValidityNoSamples"));
                return;
            }

            var excluded = report.ExcludedSampleIds.Count == 0
                ? L("Exec.ValidityNoExclusion")
                : string.Join(
                    "、",
                    report.ExcludedSampleIds.Select(id => $"{L("Exec.SampleLabel")}{id}")
                );

            Append(
                string.Format(
                    CultureInfo.CurrentCulture,
                    L("Exec.ValidityDone"),
                    report.Evaluated,
                    report.EffectiveCount,
                    excluded
                )
            );

            if (report.NeedsSupplement)
            {
                Append(L("Exec.NeedsSupplement"));
            }
        }
        catch (Exception ex)
        {
            Append($"{L("Exec.Failed")}: {ex.Message}");
        }
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
                SetConnectionState(true);
            }

            device.ElapsedSeconds = 0;
            controlLoop.Start();
            var controlToken = _cts.Token;

            // 通道设置（参数表）：运行前推给设备适配器 —— 轴向压力/位移取自哪个通道
            ApplyAcquisitionSettings(profile);

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
                        var recordedBefore = sequencer.Curve.Count;

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

                            // 试验计时**从接触成立起算**（现役线程也是在接触这一点 `StartTime := CurrentTime`）：
                            // 接触前的空走时间不计入"试验持续时间"（用户 2026-10-12）。
                            device.ElapsedSeconds = 0;

                            // 峰值与「试验持续时间」都从接触起算：接触前的空走读数不算峰值、也不计时
                            //（用户 2026-10-12）
                            _contactEstablished = true;

                            _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                                () => Append(L("Exec.ContactDetected"))
                            );
                        }

                        if (
                            phaseBefore != TestPhase.Resetting
                            && sequencer.Phase == TestPhase.Resetting
                        )
                        {
                            // 归位（现役 tpAutoReset）：破坏确认、继续加载走完之后，**先停再反转**把工作台送回起始位。
                            // 速率用试验速率（现役同）；走够「试验完成后归位时间」由阶段机收尾，finally 里 StopAsync 停机。
                            // 归位段的数据不记曲线（阶段机的 Resetting 分支不写曲线）。
                            await device
                                .ReturnToStartAsync(profile.LoadRateMillimetersPerMinute, controlToken)
                                .ConfigureAwait(false);

                            _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                                () => Append(L("Exec.Returning"))
                            );
                        }

                        // 界面曲线只画**阶段机真正记录的点**：接触成立才开始记、归位段不记 ——
                        // 与落库、判定用的是同一条曲线（用户 2026-10-12：接触试样前不要画曲线）。
                        CurvePoint[] newPoints = sequencer.Curve.Count > recordedBefore
                            ? [.. sequencer.Curve.Skip(recordedBefore)]
                            : [];

                        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                            () => UpdateLiveValues(sample, newPoints)
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
                // 走到这里说明不是阶段机自己结束的：**用户点了"停止"**（令牌已取消 → 人工停止）
                // 或 10 分钟兜底超时（令牌没动过）。用户 2026-10-12 反馈：手动停止曾被记成 LoadingTimeout。
                sequencer.Stop(
                    _cts.IsCancellationRequested
                        ? TestStopReason.ManualStop
                        : TestStopReason.LoadingTimeout
                );
            }

            await _cts.CancelAsync();
            await loopTask;
        }
        finally
        {
            device.SampleReceived -= OnSample;
            await device.StopAsync(CancellationToken.None);
            IsRunning = false;
            _contactEstablished = false;
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

            // 拟合信息（不是卡片，界面上按 key 找不到卡片，只是被上面那轮循环忽略）→ 单独挑出来发给页面
            CurveAnnotated?.Invoke(
                this,
                new CurveAnnotation(
                    values.GetValueOrDefault("tangentK"),
                    values.GetValueOrDefault("tangentB"),
                    values.GetValueOrDefault("originCorrection"),
                    values.GetValueOrDefault(
                        "originCorrectionStart",
                        CurveAnnotation.DefaultStartPercent
                    ),
                    values.GetValueOrDefault(
                        "originCorrectionEnd",
                        CurveAnnotation.DefaultEndPercent
                    )
                )
            );
        }

        Append($"{L("Exec.SampleDone")} #{sampleId} · {sequencer.Curve.Count} {L("Exec.Points")}");
    }

    private void UpdateLiveValues(TestSample sample, IReadOnlyList<CurvePoint> newCurvePoints)
    {
        UpdateCards(sample);

        foreach (var point in newCurvePoints)
        {
            CurvePointAdded?.Invoke(this, point);
        }
    }

    /// <summary>刷新数值卡（荷载 / 位移 / 时间）；点动与跑试验共用。</summary>
    private void UpdateCards(TestSample sample)
    {
        SetCard("load", sample.Load, sample.Load);
        SetCard("displacement", sample.Displacement, sample.Displacement);

        // 「试验持续时间」**接触成立后**才开始显示并计时：点启动后接触前保持 "—"（用户 2026-10-12）。
        // 设备侧的内部计时一直在走（接触搜索 300s 超时靠它），只是不往这张卡上写。
        if (_contactEstablished)
        {
            SetCard("elapsed", sample.ElapsedSeconds, sample.ElapsedSeconds);
        }
    }

    private void SetCard(string key, double value, double peakCandidate)
    {
        var card = FindCard(key);
        if (card is null)
        {
            return;
        }

        card.Value = Format(value);

        // 峰值只在**试验过程中**（且已接触试样）有意义：进页面、手动升降、接触前的空走都不该动它。
        // 试验结束后 _contactEstablished 转 false，峰值数值保留（继续显示最后一轮的值）但不再被改写。
        if (!card.ShowPeak || !_contactEstablished)
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
