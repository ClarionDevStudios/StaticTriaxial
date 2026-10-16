namespace STS.Core.Control;

/// <summary>
/// 点动方向（手动移动工作台）。
/// 界面按"**按住运行、松开停止**"使用：按下时调 <c>JogAsync</c>，松开时调 <c>StopAsync</c>。
/// </summary>
public enum JogDirection
{
    /// <summary>上升（正向）。</summary>
    Up = 0,

    /// <summary>下降（反向）。</summary>
    Down = 1,
}
