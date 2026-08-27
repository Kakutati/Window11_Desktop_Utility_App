using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RingLauncher.Config;
using RingLauncher.Interop;
using RingLauncher.Triggers;

namespace RingLauncher.UI;

/// <summary>
/// 설정 편집 창. 저장 = 검증 → config.json 저장 → AppHost.Reload(즉시 적용).
/// 항목은 raw JSON 편집(가장 유연, 드래그앤드롭으로 app 항목 자동 추가). 핫키는 캡처 + 충돌 즉시 검사.
/// </summary>
public sealed class SettingsWindow : Window
{
    readonly Func<AppConfig, string?> _save;

    readonly TextBox _hotkey = new() { IsReadOnly = true, Width = 200 };
    readonly TextBlock _hotkeyStatus = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox _triggerType = Combo("hotkey", "ctrlDoubleTap", "middleHold");
    readonly ComboBox _mode = Combo("hold", "toggle");
    readonly ComboBox _taskbar = Combo("autohide", "hideWindow", "none");
    readonly ComboBox _fullscreen = Combo("suppress", "allow");
    readonly CheckBox _startup = new() { Content = "Windows 시작 시 자동 실행", Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBox _items = new()
    {
        AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), FontSize = 12,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 220, AllowDrop = true,
        TextWrapping = TextWrapping.NoWrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    readonly Slider _outer = SliderFor(80, 260);
    readonly Slider _sub = SliderFor(140, 360);

    AppConfig _cfg;

    public SettingsWindow(AppConfig cfg, Func<AppConfig, string?> save)
    {
        _cfg = Clone(cfg);
        _save = save;

        Title = "RingLauncher 설정";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanMinimize;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Section("트리거"));
        _hotkey.PreviewKeyDown += CaptureHotkey;
        root.Children.Add(Row("핫키", Wrap(_hotkey, _hotkeyStatus)));
        root.Children.Add(Row("종류", _triggerType));
        root.Children.Add(Row("동작", _mode));

        root.Children.Add(Section("작업 표시줄 / 정책"));
        root.Children.Add(Row("작업 표시줄", _taskbar));
        root.Children.Add(Row("전체화면", _fullscreen));

        root.Children.Add(Section("링 크기"));
        root.Children.Add(Row("바깥 반지름", _outer));
        root.Children.Add(Row("서브 반지름", _sub));

        root.Children.Add(Section("항목 (JSON — exe/바로가기를 끌어다 놓으면 추가)"));
        _items.PreviewDragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        _items.Drop += OnDropFiles;
        root.Children.Add(_items);
        root.Children.Add(_error);

        root.Children.Add(_startup);

        var apply = new Button { Content = "저장 후 적용", Width = 110, Height = 30, IsDefault = true };
        apply.Click += OnSave;
        var cancel = new Button { Content = "닫기", Width = 80, Height = 30, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        cancel.Click += (_, _) => Close();
        var folder = new Button { Content = "설정 폴더", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        folder.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", ConfigStore.Dir) { UseShellExecute = true });
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0),
            Children = { folder, apply, cancel },
        });

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        LoadFrom(_cfg);
    }

    void LoadFrom(AppConfig c)
    {
        _hotkey.Text = c.Trigger.Hotkey;
        Select(_triggerType, c.Trigger.Type);
        Select(_mode, c.Trigger.Mode);
        Select(_taskbar, c.Taskbar.Mode);
        Select(_fullscreen, c.Policy.Fullscreen);
        _outer.Value = c.Ring.OuterRadius;
        _sub.Value = c.Ring.SubRadius;
        _items.Text = JsonSerializer.Serialize(c.Items, new JsonSerializerOptions
        {
            WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        _startup.IsChecked = StartupRegistry.IsEnabled();
        ValidateHotkey();
    }

    void CaptureHotkey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

        var mods = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods.Add("Win");
        if (mods.Count == 0) { _hotkeyStatus.Text = "수정자(Ctrl/Alt/…)를 함께 누르세요"; _hotkeyStatus.Foreground = Brushes.OrangeRed; return; }
        mods.Add(key.ToString());
        _hotkey.Text = string.Join("+", mods);
        ValidateHotkey();
    }

    /// <summary>실제로 잠깐 등록해 보고 충돌 여부를 즉시 표시.</summary>
    void ValidateHotkey()
    {
        try
        {
            var combo = KeyCombo.Parse(_hotkey.Text);
            bool ok = Native.RegisterHotKey(IntPtr.Zero, 0x4321, combo.Modifiers, (uint)combo.Vk);
            if (ok) Native.UnregisterHotKey(IntPtr.Zero, 0x4321);
            _hotkeyStatus.Text = ok ? "사용 가능" : "이미 다른 앱이 사용 중";
            _hotkeyStatus.Foreground = ok ? Brushes.SeaGreen : Brushes.OrangeRed;
        }
        catch (Exception ex) { _hotkeyStatus.Text = ex.Message; _hotkeyStatus.Foreground = Brushes.OrangeRed; }
    }

    void OnDropFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        List<ItemConfig> list;
        try { list = JsonSerializer.Deserialize<List<ItemConfig>>(_items.Text, JsonOpts) ?? new(); }
        catch { list = new(); }
        foreach (var f in files)
        {
            var target = f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(f) ?? f : f;
            list.Add(new ItemConfig { Type = "app", Label = Path.GetFileNameWithoutExtension(f), Path = target });
        }
        _items.Text = JsonSerializer.Serialize(list, JsonIndented);
        e.Handled = true;
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        _error.Text = "";
        try
        {
            _cfg.Trigger.Hotkey = _hotkey.Text;
            _cfg.Trigger.Type = (string)((ComboBoxItem)_triggerType.SelectedItem).Content;
            _cfg.Trigger.Mode = (string)((ComboBoxItem)_mode.SelectedItem).Content;
            _cfg.Taskbar.Mode = (string)((ComboBoxItem)_taskbar.SelectedItem).Content;
            _cfg.Policy.Fullscreen = (string)((ComboBoxItem)_fullscreen.SelectedItem).Content;
            _cfg.Ring.OuterRadius = Math.Round(_outer.Value);
            _cfg.Ring.SubRadius = Math.Round(_sub.Value);
            _cfg.Items = JsonSerializer.Deserialize<List<ItemConfig>>(_items.Text, JsonOpts) ?? new();
        }
        catch (Exception ex) { _error.Text = "항목 JSON 오류: " + ex.Message; return; }

        StartupRegistry.Set(_startup.IsChecked == true);
        var err = _save(_cfg);
        if (err is not null) { _error.Text = err; return; }
        _cfg = Clone(_cfg);
        _error.Foreground = Brushes.SeaGreen;
        _error.Text = "저장됨 · 적용 완료";
    }

    // ---- 헬퍼 ----
    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static readonly JsonSerializerOptions JsonIndented = new(JsonOpts) { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static AppConfig Clone(AppConfig c) => JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(c))!;

    static ComboBox Combo(params string[] values)
    {
        var cb = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var v in values) cb.Items.Add(new ComboBoxItem { Content = v });
        cb.SelectedIndex = 0;
        return cb;
    }
    static void Select(ComboBox cb, string value)
    {
        foreach (ComboBoxItem it in cb.Items) if ((string)it.Content == value) { cb.SelectedItem = it; return; }
        cb.SelectedIndex = 0;
    }
    static Slider SliderFor(double min, double max) => new() { Minimum = min, Maximum = max, Width = 200, HorizontalAlignment = HorizontalAlignment.Left, TickFrequency = 4, IsSnapToTickEnabled = true };
    static TextBlock Section(string t) => new() { Text = t, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 6) };
    static StackPanel Wrap(params UIElement[] kids) { var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var k in kids) p.Children.Add(k); return p; }
    static Grid Row(string label, UIElement value)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 1);
        g.Children.Add(lb);
        g.Children.Add(value);
        return g;
    }

    static string? ShortcutTarget(string lnk)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t is null) return null;
            dynamic shell = Activator.CreateInstance(t)!;
            var sc = shell.CreateShortcut(lnk);
            string target = sc.TargetPath;
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch { return null; }
    }
}

/// <summary>시작 프로그램 등록 (HKCU\...\Run, 관리자 권한 불필요).</summary>
public static class StartupRegistry
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "RingLauncher";

    public static bool IsEnabled()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(Name) is not null;
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
            if (enabled) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue(Name, false);
        }
        catch (Exception ex) { Log.Write($"시작 프로그램 등록 실패: {ex.Message}"); }
    }
}
