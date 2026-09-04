using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RingLauncher.Items;

namespace RingLauncher.UI;

/// <summary>
/// 시간 + 달력 패널. 작업 표시줄을 숨기면 사라지는 Windows 시계/달력을 대체.
/// 링과 달리 포커스를 받는 활성 창(SearchWindow와 같은 패턴): ESC/포커스 이탈 시 닫힘.
/// </summary>
public sealed class ClockCalendarWindow : Window
{
    static ClockCalendarWindow? _open;
    static readonly CultureInfo Ci = CultureInfo.CurrentCulture;

    static readonly Brush Fg = Freeze(Brushes.White);
    static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromRgb(0x88, 0x8C, 0x92)));
    static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)));
    static readonly Brush Sun = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x6A, 0x6A)));
    static readonly Brush Sat = Freeze(new SolidColorBrush(Color.FromRgb(0x6A, 0x9E, 0xE8)));

    readonly TextBlock _time = new() { FontSize = 42, FontWeight = FontWeights.Light, Foreground = Fg, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _date = new() { FontSize = 14, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
    readonly TextBlock _month = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, MinWidth = 130 };
    readonly Grid _grid = new();
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    bool _closing;
    int _year, _mon; // 표시 중인 달

    ClockCalendarWindow()
    {
        Title = "RingLauncher 시계";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        for (int c = 0; c < 7; c++) _grid.ColumnDefinitions.Add(new ColumnDefinition());

        var prev = NavButton("", () => Shift(-1)); // ChevronLeft
        var next = NavButton("", () => Shift(+1)); // ChevronRight
        var today = new Button { Content = "오늘", Foreground = Dim, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FontSize = 12, Padding = new Thickness(6, 0, 6, 0) };
        today.Click += (_, _) => Show(DateTime.Today.Year, DateTime.Today.Month);
        var header = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
        DockPanel.SetDock(prev, Dock.Left);
        DockPanel.SetDock(next, Dock.Right);
        DockPanel.SetDock(today, Dock.Right);
        header.Children.Add(prev);
        header.Children.Add(next);
        header.Children.Add(today);
        header.Children.Add(_month);

        var panel = new StackPanel();
        panel.Children.Add(_time);
        panel.Children.Add(_date);
        panel.Children.Add(new Separator { Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) });
        panel.Children.Add(header);
        panel.Children.Add(_grid);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Child = panel,
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Left) { Shift(-1); e.Handled = true; }
            else if (e.Key == Key.Right) { Shift(+1); e.Handled = true; }
        };
        Deactivated += (_, _) => { if (!_closing) Close(); };
        Closing += (_, _) => { _closing = true; _tick.Stop(); };
        Closed += (_, _) => { if (_open == this) _open = null; };

        _tick.Tick += (_, _) => UpdateClock();
        UpdateClock();
        Show(DateTime.Today.Year, DateTime.Today.Month);
        _tick.Start();
    }

    public static void Open()
    {
        if (_open is not null) { _open.Activate(); return; }
        _open = new ClockCalendarWindow();
        ((Window)_open).Show();
        _open.Activate();
    }

    void UpdateClock()
    {
        var now = DateTime.Now;
        _time.Text = now.ToString("H:mm:ss", Ci);
        _date.Text = now.ToString("yyyy년 M월 d일 dddd", Ci);
    }

    void Shift(int months)
    {
        var d = new DateTime(_year, _mon, 1).AddMonths(months);
        Show(d.Year, d.Month);
    }

    void Show(int year, int mon)
    {
        _year = year; _mon = mon;
        _month.Text = new DateTime(year, mon, 1).ToString("yyyy년 M월", Ci);
        BuildGrid();
    }

    void BuildGrid()
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();

        var first = new DateTime(_year, _mon, 1);
        int firstDow = (int)Ci.DateTimeFormat.FirstDayOfWeek;
        int lead = ((int)first.DayOfWeek - firstDow + 7) % 7; // 첫 주 앞 빈칸 수
        int days = DateTime.DaysInMonth(_year, _mon);
        int rows = (int)Math.Ceiling((lead + days) / 7.0);

        _grid.RowDefinitions.Add(new RowDefinition()); // 요일 헤더
        for (int r = 0; r < rows; r++) _grid.RowDefinitions.Add(new RowDefinition());

        // 요일 헤더
        var names = Ci.DateTimeFormat.ShortestDayNames; // 일요일=0
        for (int c = 0; c < 7; c++)
        {
            int dow = (firstDow + c) % 7;
            var tb = new TextBlock { Text = names[dow], FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 4), Foreground = dow == 0 ? Sun : dow == 6 ? Sat : Dim };
            Grid.SetRow(tb, 0); Grid.SetColumn(tb, c);
            _grid.Children.Add(tb);
        }

        // 날짜 셀
        for (int day = 1; day <= days; day++)
        {
            int idx = lead + day - 1;
            int r = idx / 7 + 1, c = idx % 7;
            int dow = (firstDow + c) % 7;
            bool today = _year == DateTime.Today.Year && _mon == DateTime.Today.Month && day == DateTime.Today.Day;

            var cell = new Border
            {
                Width = 34, Height = 30, CornerRadius = new CornerRadius(6),
                Background = today ? Accent : Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = day.ToString(), FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = today ? Fg : dow == 0 ? Sun : dow == 6 ? Sat : Fg,
                },
            };
            Grid.SetRow(cell, r); Grid.SetColumn(cell, c);
            _grid.Children.Add(cell);
        }
    }

    static Button NavButton(string glyph, Action onClick)
    {
        var b = new Button
        {
            Content = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12, Foreground = Brushes.White, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Padding = new Thickness(8, 2, 8, 2),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    static Brush Freeze(Brush b) { b.Freeze(); return b; }
}

/// <summary>링 항목: 시계/달력 창 열기. Execute는 UI 스레드(링 닫힌 뒤).</summary>
public sealed class ClockItem(string label, ImageSource? icon) : IRingItem
{
    public string Label => label;
    public ImageSource? Icon => icon;
    public void Execute() => ClockCalendarWindow.Open();
}
