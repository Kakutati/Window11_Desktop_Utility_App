using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using RingLauncher.Config;
using RingLauncher.Core;
using RingLauncher.Interop;
using RingLauncher.Items;
using Path = System.Windows.Shapes.Path;

namespace RingLauncher.UI;

/// <summary>투명 레이어드 오버레이. 포커스를 절대 가져가지 않는다. 위치/크기는 물리 픽셀로 직접 지정.</summary>
public sealed class RingWindow : Window
{
    const double Gap = 1.5; // 섹터 사이 간격(도)

    readonly RingConfig _cfg;
    readonly Grid _root = new();
    readonly Canvas _canvas = new();
    readonly ScaleTransform _scale = new(1, 1);
    readonly Brush _bg, _accent, _text;
    readonly List<Shape> _inner = new(), _outer = new();
    readonly List<UIElement> _outerVisuals = new();
    readonly IntPtr _hwnd;
    int _innerHi = -1, _outerHi = -1;

    public RingWindow(RingConfig cfg)
    {
        _cfg = cfg;
        _bg = MakeBrush(cfg.Theme.Background);
        _accent = MakeBrush(cfg.Theme.Accent);
        _text = MakeBrush(cfg.Theme.Text);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = Height = cfg.Diameter;
        FontFamily = new FontFamily(cfg.Theme.Font);

        _canvas.Width = _canvas.Height = cfg.Diameter;
        _root.Opacity = 0;
        _root.RenderTransformOrigin = new Point(0.5, 0.5);
        _root.RenderTransform = _scale;
        _root.Children.Add(_canvas);
        Content = _root;

        _hwnd = new WindowInteropHelper(this).EnsureHandle(); // 미리 생성해 첫 표시 지연 제거
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        ApplyExStyle();
    }

    void ApplyExStyle() => Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
        handled = true;
        return new IntPtr(Native.MA_NOACTIVATE);
    }

    static Brush MakeBrush(string color)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        b.Freeze();
        return b;
    }

    /// <summary>안쪽 링 구성. 바깥 링은 비운다.</summary>
    public void SetItems(IReadOnlyList<IRingItem> items)
    {
        _canvas.Children.Clear();
        _inner.Clear();
        _outer.Clear();
        _outerVisuals.Clear();
        _innerHi = _outerHi = -1;

        var dead = new Ellipse { Width = _cfg.DeadZone * 2, Height = _cfg.DeadZone * 2, Fill = _bg, Opacity = 0.5, IsHitTestVisible = false };
        Canvas.SetLeft(dead, _cfg.Diameter / 2 - _cfg.DeadZone);
        Canvas.SetTop(dead, _cfg.Diameter / 2 - _cfg.DeadZone);
        _canvas.Children.Add(dead);

        double w = 360.0 / items.Count;
        for (int i = 0; i < items.Count; i++)
            AddSector(_inner, items[i], HitTester.SectorCenter(_cfg.StartAngle, 360, items.Count, i), w / 2, _cfg.InnerRadius, _cfg.OuterRadius, null);
    }

    /// <summary>바깥 링(서브메뉴) 표시. 기존 바깥 링은 교체.</summary>
    public void ShowOuter(IReadOnlyList<IRingItem> items, OuterRing ring)
    {
        HideOuter();
        double w = ring.SpanAngle / ring.Count;
        for (int i = 0; i < items.Count; i++)
            AddSector(_outer, items[i], ring.StartAngle + w * i, w / 2, _cfg.OuterRadius + 6, _cfg.SubRadius, _outerVisuals);
    }

    public void HideOuter()
    {
        foreach (var v in _outerVisuals) _canvas.Children.Remove(v);
        _outerVisuals.Clear();
        _outer.Clear();
        _outerHi = -1;
    }

    void AddSector(List<Shape> sectors, IRingItem item, double mid, double halfWidth, double ri, double ro, List<UIElement>? track)
    {
        double c = _cfg.Diameter / 2;
        var sector = new Path { Data = Annulus(c, ri, ro, mid - halfWidth + Gap, mid + halfWidth - Gap), Fill = _bg };
        _canvas.Children.Add(sector);
        sectors.Add(sector);
        track?.Add(sector);

        var rad = mid * Math.PI / 180;
        var rm = (ri + ro) / 2;
        var panel = new StackPanel { Width = 88, IsHitTestVisible = false };
        if (item.Icon is { } icon)
            panel.Children.Add(new Image { Source = icon, Width = 32, Height = 32, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(new TextBlock
        {
            Text = item.Label, Foreground = _text, FontSize = 12,
            TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Canvas.SetLeft(panel, c + Math.Cos(rad) * rm - 44);
        Canvas.SetTop(panel, c + Math.Sin(rad) * rm - (item.Icon is null ? 8 : 26));
        _canvas.Children.Add(panel);
        track?.Add(panel);
    }

    static Geometry Annulus(double c, double ri, double ro, double a0, double a1)
    {
        var g = new StreamGeometry();
        var large = a1 - a0 > 180;
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(Polar(c, ri, a0), true, true);
            ctx.LineTo(Polar(c, ro, a0), true, false);
            ctx.ArcTo(Polar(c, ro, a1), new Size(ro, ro), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(Polar(c, ri, a1), true, false);
            ctx.ArcTo(Polar(c, ri, a0), new Size(ri, ri), 0, large, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    static Point Polar(double c, double r, double deg)
    {
        var t = deg * Math.PI / 180;
        return new Point(c + r * Math.Cos(t), c + r * Math.Sin(t));
    }

    /// <param name="inner">안쪽 하이라이트(펼쳐진 부모 포함), 없으면 -1</param>
    /// <param name="outer">바깥 하이라이트, 없으면 -1</param>
    public void Highlight(int inner, int outer)
    {
        Swap(_inner, ref _innerHi, inner);
        Swap(_outer, ref _outerHi, outer);
    }

    void Swap(List<Shape> sectors, ref int current, int next)
    {
        if (next == current) return;
        if (current >= 0 && current < sectors.Count) sectors[current].Fill = _bg;
        if (next >= 0 && next < sectors.Count) sectors[next].Fill = _accent;
        current = next;
    }

    /// <param name="centerPx">링 중심 (물리 픽셀)</param>
    public void ShowAt(POINT centerPx, uint dpi)
    {
        int size = (int)Math.Round(_cfg.Diameter * dpi / 96.0);
        int x = centerPx.X - size / 2, y = centerPx.Y - size / 2;

        _root.BeginAnimation(OpacityProperty, null);
        _root.Opacity = 0;
        if (!IsVisible) Show(); // ShowActivated=false → SW_SHOWNOACTIVATE
        ApplyExStyle();

        // ponytail: 2단계 SetWindowPos. 1단계 이동으로 WPF가 대상 모니터의 WM_DPICHANGED를 먼저 처리하게 하고,
        // 2단계에서 정확한 물리 크기를 확정한다. 멀티 DPI 검증(6단계)에서 튐이 보이면 모니터별 창 인스턴스로 전환.
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, size, size, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        var dur = TimeSpan.FromMilliseconds(_cfg.AnimationMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, 1, dur) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1, dur) { EasingFunction = ease });
    }

    public void HideRing()
    {
        Highlight(-1, -1);
        Hide();
    }
}
