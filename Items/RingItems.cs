using System.IO;
using System.Diagnostics;
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

public static class ItemFactory
{
    public static List<IRingItem> Create(IEnumerable<ItemConfig> configs)
    {
        var list = new List<IRingItem>();
        foreach (var c in configs)
        {
            try
            {
                var item = Create(c);
                if (item != null) list.Add(item);
                else Log.Write($"미지원 항목 타입 무시: {c.Type} ({c.Label})");
            }
            catch (Exception ex) { Log.Write($"항목 생성 실패 ({c.Label}): {ex.Message}"); }
        }
        return list;
    }

    static IRingItem? Create(ItemConfig c)
    {
        var path = c.Path is null ? null : Environment.ExpandEnvironmentVariables(c.Path);
        var icon = IconLoader.Load(c.Icon ?? path);
        return c.Type.ToLowerInvariant() switch
        {
            "app" => new AppItem(c.Label ?? Path.GetFileNameWithoutExtension(path) ?? "?", path!, c.Args, icon),
            "uri" => new UriItem(c.Label ?? c.Uri!, c.Uri!, icon),
            "keys" => new KeysItem(c.Label ?? c.Sequence!, KeyCombo.Parse(c.Sequence!), icon),
            _ => null, // windows / submenu / quick / desktop: 이후 단계
        };
    }
}

public static class IconLoader
{
    public static ImageSource? Load(string? file)
    {
        var full = Resolve(file);
        if (full is null) return null;
        try
        {
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(full);
            if (ico is null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
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
