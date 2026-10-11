namespace ANX.Ui.ViewModels;

using System.Globalization;
using ANX.Control;
using ANX.Core;
using ANX.Core.Control;
using ANX.Ui.Services;
using Wpf.Ui;

/// <summary>一张实时指标卡（界面用，值随采样刷新）。</summary>
public partial class MetricCard(MetricDefinition definition) : ObservableObject
{
    public string DisplayName { get; } = definition.DisplayName;

    public string Unit { get; } = definition.Unit;

    public bool ShowPeak { get; } = definition.ShowPeak;

    [ObservableProperty]
    private string _value = "—";

    [ObservableProperty]
    private string _peak = "—";
}

/// <summary>
/// 试验执行页 ViewModel：设备 → 阶段机 → 判定 → 落库。
/// 它只认 <see cref="ITestExecution"/> 与 <see cref="ITestDeviceController"/>，
/// 不认识 T0715 的公式、表结构与报表。
/// </summary>
public partial class TestExecutionViewModel : ViewModelBase
{
    private readonly ITestExecution _execution;
    private readonly ITestDeviceController? _device;
    private readonly ICurrentSheetContext? _currentSheet;
    private readonly INavigationService? _navigation;
    private readonly IReportExportService? _exporter;
    private readonly List<CurvePoint> _curve = [];
    private readonly Dictionary<string, double> _peaks = new(StringComparer.Ordinal);
    private TestSequencer? _sequencer;
    private CancellationTokenSource? _cts;
    private string? _sheetId;
    private int _sampleCounter;

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
            Cards.Add(new MetricCard(definition));
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
        }
        catch (Exception ex)
        {
            ConnectionText = L("Exec.ConnectFailed");
            Append($"{L("Exec.ConnectFailed")}: {ex.Message}");
        }
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

        var sequencer = new TestSequencer(_execution.Profile);
        _sequencer = sequencer;
        sequencer.Start();

        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnSample(object? sender, TestSample sample)
        {
            if (!sequencer.OnSample(sample))
            {
                finished.TrySetResult(true);
                return;
            }

            UpdateLiveValues(sample);
        }

        _device!.SampleReceived += OnSample;
        IsRunning = true;
        _cts = new CancellationTokenSource();

        try
        {
            if (!_device.IsConnected)
            {
                await _device.ConnectAsync(_cts.Token);
                ConnectionText = L("Exec.Connected");
            }

            await _device.StartAsync(_cts.Token);

            var completed = await Task.WhenAny(
                finished.Task,
                Task.Delay(TimeSpan.FromMinutes(10), _cts.Token)
            );

            if (completed != finished.Task)
            {
                sequencer.Stop(TestStopReason.LoadingTimeout);
            }
        }
        finally
        {
            _device.SampleReceived -= OnSample;
            await _device.StopAsync(CancellationToken.None);
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }

        PhaseText = sequencer.StopReason switch
        {
            TestStopReason.Completed => L("Exec.PhaseCompleted"),
            TestStopReason.None => L("Exec.PhaseIdle"),
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
        SetCard("load", sample.Load, sample.Load);
        SetCard("displacement", sample.Displacement, sample.Displacement);
        SetCard("elapsed", sample.ElapsedSeconds, sample.ElapsedSeconds);

        _curve.Add(new CurvePoint(sample.Displacement, sample.Load));
        CurvePointAdded?.Invoke(this, new CurvePoint(sample.Displacement, sample.Load));
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
