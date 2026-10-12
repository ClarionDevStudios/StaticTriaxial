namespace ANX.Ui.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

/// <summary>
/// 把页面高度**限制在内容宿主给的可见区**里（扣掉页面自身外边距），让页内 Grid 的 `*` 行拿到真实高度。
///
/// **为什么需要**：页面外面还有一层 WPF-UI 的滚动容器（`DynamicScrollViewer`），它按**无限高度**量内容，
/// 于是页内 `*` 行退化成"按内容取高"——列表条目一多，列表就把整页撑高，滚动落到**外层宿主**上
/// （实测：执行页操控信息空列表 739、追加 300 条后 12380；试验单页的"已有试验单"列表同理）。
/// 给页面加上限之后，`*` 行拿到真实可见高度，列表在页内自己滚动（用户 2026-10-12 要求：
/// **只滚动列表内容，不要滚动页面**）。
///
/// 取外层滚动容器的**视口**高度而不是窗口高度：宿主已经把标题栏、导航头扣掉了，正是需要的那块。
/// （纯 XAML 的 `RelativeSource AncestorType=Window` 在这个宿主持下取不到窗口，绑定会静默失效。）
///
/// **用法**：页面构造函数末尾 `PageViewportFitter.Attach(this);`
/// </summary>
public static class PageViewportFitter
{
    public static void Attach(FrameworkElement page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var state = new FitterState(page);

        page.Loaded += (_, _) => state.BindHost();
        page.Unloaded += (_, _) => state.UnbindHost();
        page.SizeChanged += (_, _) => state.Apply();
    }

    private sealed class FitterState(FrameworkElement page)
    {
        private ScrollViewer? _host;
        private double _alignedTo = -1;

        /// <summary>
        /// 盯住**可见区**的尺寸变化。不能盯页面的直接父元素（`ContentPresenter`）：
        /// 它的高度跟着内容走，会形成环路；外层 ScrollViewer 的视口才是被约束住的真值。
        /// </summary>
        public void BindHost()
        {
            var host = FindHost(page);
            if (host is null || ReferenceEquals(host, _host))
            {
                Apply(); // 没有滚动宿主（例如设计器里）就退回窗口内容区
                return;
            }

            UnbindHost();
            _host = host;
            _host.SizeChanged += OnHostSizeChanged;
            _host.LayoutUpdated += OnHostLayoutUpdated;
            Apply();
        }

        public void UnbindHost()
        {
            if (_host is null)
            {
                return;
            }

            _host.SizeChanged -= OnHostSizeChanged;
            _host.LayoutUpdated -= OnHostLayoutUpdated;
            _host = null;
        }

        private void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => Apply();

        /// <summary>可见区每次布局完都对一次高；尺寸没变就跳过（否则每个 LayoutUpdated 都要重设一遍）。</summary>
        private void OnHostLayoutUpdated(object? sender, EventArgs e)
        {
            var viewport = Viewport();
            if (viewport > 0 && Math.Abs(viewport - _alignedTo) > 0.5)
            {
                Apply();
            }
        }

        /// <summary>延后到布局之后设值，避免在布局过程中改尺寸。</summary>
        public void Apply() =>
            _ = page.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyCore));

        private void ApplyCore()
        {
            var viewport = Viewport();
            if (viewport <= 0)
            {
                return;
            }

            _alignedTo = viewport;
            var margin = page.Margin;
            page.MaxHeight = Math.Max(0, viewport - margin.Top - margin.Bottom);
        }

        /// <summary>
        /// 可见区高度：优先取外层 ScrollViewer 的视口高度（被 NavigationView 约束、不随内容变）；
        /// 万一没有滚动容器，就退回窗口内容区。
        /// </summary>
        private double Viewport()
        {
            if ((_host ?? FindHost(page)) is { ViewportHeight: > 0 } scroll)
            {
                return scroll.ViewportHeight;
            }

            return Window.GetWindow(page)?.Content is FrameworkElement content
                ? content.ActualHeight
                : 0;
        }

        private static ScrollViewer? FindHost(DependencyObject page)
        {
            for (
                var node = VisualTreeHelper.GetParent(page);
                node is not null;
                node = VisualTreeHelper.GetParent(node)
            )
            {
                if (node is ScrollViewer scroll)
                {
                    return scroll;
                }
            }

            return null;
        }
    }
}
