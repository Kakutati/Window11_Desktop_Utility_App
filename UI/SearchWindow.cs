using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RingLauncher.Items;

namespace RingLauncher.UI;

/// <summary>
/// 설치된 프로그램 검색 런처. 링과 달리 포커스를 받아야 하므로 일반 활성 창.
/// 타이핑 → 필터, ↑/↓ 이동, Enter/더블클릭 실행, ESC/포커스 잃음 → 닫힘.
/// </summary>
public sealed class SearchWindow : Window
{
    static SearchWindow? _open; // 중복 방지

    readonly TextBox _query = new()
    {
        FontSize = 18, Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(0),
        Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White,
    };
    readonly ListBox _list = new()
    {
        MaxHeight = 360, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        Foreground = Brushes.White, FontSize = 14,
    };

    SearchWindow()
    {
        Title = "RingLauncher 검색"; // 타이틀바는 없지만 접근성/식별용
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        System.Windows.Automation.AutomationProperties.SetAutomationId(_query, "ringSearchBox");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_list, "ringSearchList");
        _query.TextChanged += (_, _) => Refresh();
        _query.PreviewKeyDown += OnKey;
        _list.MouseDoubleClick += (_, _) => Launch();
        _list.PreviewKeyDown += OnKey;

        var panel = new StackPanel();
        panel.Children.Add(_query);
        panel.Children.Add(new Separator { Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) });
        panel.Children.Add(_list);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Child = panel,
        };

        Deactivated += (_, _) => { if (!_closing) Close(); };
        Closing += (_, _) => _closing = true;
        Closed += (_, _) => { if (_open == this) _open = null; };
        Loaded += (_, _) => { Refresh(); _query.Focus(); };
    }

    bool _closing;

    public static void Open()
    {
        if (_open is not null) { _open.Activate(); _open._query.Focus(); return; }
        _open = new SearchWindow();
        ((Window)_open).Show();
        _open.Activate();
    }

    void Refresh()
    {
        _list.ItemsSource = InstalledApps.Search(_query.Text);
        _list.DisplayMemberPath = nameof(InstalledApp.Name);
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Enter: Launch(); e.Handled = true; break;
            case Key.Down: Move(+1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
        }
    }

    void Move(int delta)
    {
        if (_list.Items.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, _list.Items.Count - 1);
        _list.ScrollIntoView(_list.SelectedItem);
    }

    void Launch()
    {
        if (_list.SelectedItem is not InstalledApp app) return;
        try { app.Launch(); }
        catch (Exception ex) { Log.Write($"검색 실행 실패({app.Name}): {ex.Message}"); }
        Close();
    }
}

/// <summary>링 항목: 검색 창 열기. Execute는 UI 스레드에서 호출된다(링 닫힌 뒤).</summary>
public sealed class SearchItem(string label, ImageSource? icon) : IRingItem
{
    public string Label => label;
    public ImageSource? Icon => icon;
    public void Execute() => SearchWindow.Open();
}
