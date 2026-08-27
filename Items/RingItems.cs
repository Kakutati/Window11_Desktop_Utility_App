using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RingLauncher.Config;
using RingLauncher.Interop;
using RingLauncher.Triggers;

namespace RingLauncher.Items;

public interface IRingItem
{
    string Label { get; }
    ImageSource? Icon { get; }
    void Execute();
    /// <summary>하위 항목이 있으면 바깥 링으로 펼쳐진다. 펼칠 때마다 호출(창 목록 등 동적 자식 지원).</summary>
    IReadOnlyList<IRingItem>? GetChildren() => null;
}

public sealed class AppItem(string label, string path, string? args, ImageSource? icon) : IRingItem
{
    public string Label => label;
    public ImageSource? Icon => icon;
    public void Execute() => Process.Start(new ProcessStartInfo(path, args ?? "") { UseShellExecute = true });
}

public sealed class UriItem(string label, string uri, ImageSource? icon) : IRingItem
{
    public string Label => label;
    public ImageSource? Icon => icon;
    public void Execute() => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
}

public sealed class KeysItem(string label, KeyCombo combo, ImageSource? icon) : IRingItem
{
    public string Label => label;
    public ImageSource? Icon => icon;
    public void Execute() => Native.SendKeys(combo.ModifierVks, combo.Vk);
}

/// <summary>임의 동작을 실행하는 항목(시작 메뉴 열기 등, 내장 액션용).</summary>
public sealed class ActionItem(string label, ImageSource? icon, Action action) : IRingItem
{
    public string Label => label;
    public ImageSource? Icon => icon;
    public void Execute() => action();
}

public sealed class SubmenuItem(string label, ImageSource? icon, Func<List<IRingItem>> children) : IRingItem
{
    public string Label => label;
    public ImageSource? Icon => icon;
    public void Execute() { /* 부모 자체는 실행 없음 — 컨트롤러가 펼친다 */ }
    public IReadOnlyList<IRingItem>? GetChildren() => children();
}

public static class ItemFactory
{
    /// <summary>
    /// 링이 열릴 때마다 호출되는 항목 소스. 정적 항목은 한 번만 만들고, `windows`는 호출 시점에 창 목록으로 확장된다
    /// (windowListMax까지 인라인, 나머지는 "더 보기" 서브메뉴).
    /// </summary>
    public static Func<List<IRingItem>> CreateSource(IEnumerable<ItemConfig>? configs, PolicyConfig policy)
    {
        var parts = new List<Func<IEnumerable<IRingItem>>>();
        foreach (var c in configs ?? Enumerable.Empty<ItemConfig>())
        {
            if (c.Type.Equals("windows", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(() =>
                {
                    var all = WindowList.Enumerate();
                    if (all.Count <= policy.WindowListMax) return all;
                    var rest = all.Skip(policy.WindowListMax).ToList();
                    return all.Take(policy.WindowListMax)
                        .Append(new SubmenuItem("더 보기", IconLoader.Glyph("E712"), () => rest));
                });
                continue;
            }
            try
            {
                var item = Create(c, policy);
                if (item != null) parts.Add(() => new[] { item });
                else Log.Write($"미지원 항목: {c.Type} / {c.Action} ({c.Label})");
            }
            catch (Exception ex) { Log.Write($"항목 생성 실패 ({c.Label}): {ex.Message}"); }
        }
        return () => parts.SelectMany(p => p()).ToList();
    }

    static IRingItem? Create(ItemConfig c, PolicyConfig policy)
    {
        var path = c.Path is null ? null : Environment.ExpandEnvironmentVariables(c.Path);
        var icon = IconLoader.Load(c.Icon ?? path);
        switch (c.Type.ToLowerInvariant())
        {
            case "app": return new AppItem(c.Label ?? Path.GetFileNameWithoutExtension(path) ?? "?", path!, c.Args, icon);
            case "uri": return new UriItem(c.Label ?? c.Uri!, c.Uri!, icon);
            case "keys": return new KeysItem(c.Label ?? c.Sequence!, KeyCombo.Parse(c.Sequence!), icon);
            case "submenu": return new SubmenuItem(c.Label ?? "…", icon ?? IconLoader.Glyph("E712"), CreateSource(c.Items, policy));
            case "desktop":
                var next = !string.Equals(c.Direction, "prev", StringComparison.OrdinalIgnoreCase);
                return new KeysItem(c.Label ?? (next ? "다음 데스크톱" : "이전 데스크톱"),
                    KeyCombo.Parse(next ? "Win+Ctrl+Right" : "Win+Ctrl+Left"), icon ?? IconLoader.Glyph(next ? "E76C" : "E76B"));
            case "search": return new UI.SearchItem(c.Label ?? "검색", icon ?? IconLoader.Glyph("E721"));
            case "quick":
                return (c.Action ?? "").ToLowerInvariant() switch
                {
                    "volumeup" => new KeysItem(c.Label ?? "볼륨 +", KeyCombo.Parse("VolumeUp"), icon ?? IconLoader.Glyph("E767")),
                    "volumedown" => new KeysItem(c.Label ?? "볼륨 −", KeyCombo.Parse("VolumeDown"), icon ?? IconLoader.Glyph("E993")),
                    "volumemute" => new KeysItem(c.Label ?? "음소거", KeyCombo.Parse("VolumeMute"), icon ?? IconLoader.Glyph("E74F")),
                    "brightness" => new UriItem(c.Label ?? "밝기", "ms-settings:display", icon ?? IconLoader.Glyph("E706")),
                    "wifi" => new UriItem(c.Label ?? "Wi-Fi", "ms-availablenetworks:", icon ?? IconLoader.Glyph("E701")),
                    "bluetooth" => new UriItem(c.Label ?? "Bluetooth", "ms-settings:bluetooth", icon ?? IconLoader.Glyph("E702")),
                    // 단독 Win 키 → 시작 메뉴("윈도우 버튼"). RegisterHotKey로는 못 하지만 SendInput은 됨.
                    "start" => new ActionItem(c.Label ?? "시작", icon ?? IconLoader.Glyph("E71D"), () => Native.SendKeys(Array.Empty<int>(), Native.VK_LWIN)),
                    "search" => new UI.SearchItem(c.Label ?? "검색", icon ?? IconLoader.Glyph("E721")),
                    _ => null,
                };
            default: return null;
        }
    }
}

public static class IconLoader
{
    /// <summary>파일 경로 → 연결 아이콘. "glyph:E74F" → Segoe Fluent Icons 글리프.</summary>
    public static ImageSource? Load(string? file)
    {
        if (file is not null && file.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase)) return Glyph(file[6..]);
        var full = Resolve(file);
        if (full is null) return null;
        try
        {
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(full);
            return ico is null ? null : FromHIcon(ico.Handle);
        }
        catch { return null; }
    }

    /// <summary>HICON을 복사해 ImageSource로. 원본 핸들 소유권은 호출자에게 남는다.</summary>
    public static ImageSource? FromHIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    static readonly Dictionary<string, ImageSource> GlyphCache = new();

    /// <summary>Segoe Fluent Icons 코드포인트(16진) → 32×32 비트맵. ponytail: 흰색 고정, 테마 text 색은 설정 UI 단계에서 연결.</summary>
    public static ImageSource Glyph(string hex)
    {
        if (GlyphCache.TryGetValue(hex, out var cached)) return cached;
        var text = char.ConvertFromUtf32(Convert.ToInt32(hex, 16));
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe Fluent Icons"), 26, Brushes.White, 1.0);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawText(ft, new Point((32 - ft.Width) / 2, (32 - ft.Height) / 2));
        var bmp = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return GlyphCache[hex] = bmp;
    }

    static string? Resolve(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        if (File.Exists(file)) return Path.GetFullPath(file);
        if (Path.IsPathRooted(file)) return null;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = Path.Combine(dir, file);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
