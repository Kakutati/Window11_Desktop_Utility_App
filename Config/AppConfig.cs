using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RingLauncher.Config;

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public TaskbarConfig Taskbar { get; set; } = new();
    public TriggerConfig Trigger { get; set; } = new();
    public RingConfig Ring { get; set; } = new();
    public PolicyConfig Policy { get; set; } = new();
    public List<ItemConfig> Items { get; set; } = new();
}

public sealed class TaskbarConfig
{
    public string Mode { get; set; } = "autohide"; // autohide | hideWindow(autohide + 창 숨김, 가장자리에서도 안 나옴) | none
}

public sealed class TriggerConfig
{
    public string Type { get; set; } = "hotkey"; // hotkey | ctrlDoubleTap | middleHold
    public string Hotkey { get; set; } = "Alt+OemTilde";
    public string Mode { get; set; } = "hold"; // hold: 누른 채 이동 → 떼면 실행 | toggle: 누르면 열림, 다시 누르거나 클릭하면 실행/이탈
    public int DoubleTapMs { get; set; } = 300;
    public int HoldMs { get; set; } = 200;
}

public sealed class RingConfig
{
    public double DeadZone { get; set; } = 28;
    public double InnerRadius { get; set; } = 44;
    public double OuterRadius { get; set; } = 140;
    public double SubRadius { get; set; } = 220;
    public double StartAngle { get; set; } = -90;
    public int AnimationMs { get; set; } = 120;
    public ThemeConfig Theme { get; set; } = new();

    /// <summary>창 한 변 (DIP). 바깥 링까지 담는다.</summary>
    [JsonIgnore] public double Diameter => SubRadius * 2 + 16;
}

public sealed class ThemeConfig
{
    public string Background { get; set; } = "#CC202020";
    public string Accent { get; set; } = "#FF0078D4";
    public string Text { get; set; } = "#FFFFFFFF";
    public string Font { get; set; } = "Segoe UI Variable";
}

public sealed class PolicyConfig
{
    public string Fullscreen { get; set; } = "suppress"; // suppress | allow
    public int WindowListMax { get; set; } = 6; // 초과분은 "더 보기" 서브메뉴
}

/// <summary>모든 항목 타입을 한 클래스로. 타입별로 쓰는 필드만 채운다.</summary>
public sealed class ItemConfig
{
    public string Type { get; set; } = "app"; // app | uri | keys | windows | submenu | quick | desktop
    public string? Label { get; set; }
    public string? Icon { get; set; }
    public string? Path { get; set; }
    public string? Args { get; set; }
    public string? Uri { get; set; }
    public string? Sequence { get; set; }
    public string? Action { get; set; }
    public string? Direction { get; set; }
    public List<ItemConfig>? Items { get; set; }
}

public static class ConfigStore
{
    public static readonly string Dir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RingLauncher");
    public static readonly string FilePath = System.IO.Path.Combine(Dir, "config.json");

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppConfig Load()
    {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(FilePath))
        {
            var def = Default();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(def, Json));
            return def;
        }
        try
        {
            var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Json) ?? Default();
            Validate(cfg);
            return cfg;
        }
        catch (Exception ex)
        {
            Log.Write($"config.json 로드 실패, 기본값 사용: {ex.Message}");
            return Default();
        }
    }

    static void Validate(AppConfig c)
    {
        var r = c.Ring;
        if (!(0 < r.DeadZone && r.DeadZone < r.InnerRadius && r.InnerRadius < r.OuterRadius && r.OuterRadius < r.SubRadius))
            throw new InvalidDataException("ring 반지름은 deadZone < innerRadius < outerRadius < subRadius 이어야 합니다");
        if (c.Items.Count is < 2 or > 12)
            throw new InvalidDataException("items는 2~12개여야 합니다");
    }

    public static AppConfig Default() => new()
    {
        Items =
        {
            new() { Type = "app", Label = "터미널", Path = "wt.exe" },
            new() { Type = "app", Label = "탐색기", Path = "explorer.exe" },
            new()
            {
                Type = "submenu", Label = "빠른 설정", Icon = "glyph:E713",
                Items = new()
                {
                    new() { Type = "quick", Action = "volumeMute" },
                    new() { Type = "quick", Action = "volumeUp" },
                    new() { Type = "quick", Action = "volumeDown" },
                    new() { Type = "quick", Action = "wifi" },
                    new() { Type = "quick", Action = "brightness" },
                    new() { Type = "uri", Label = "설정", Uri = "ms-settings:", Icon = "glyph:E713" },
                },
            },
            new() { Type = "keys", Label = "작업 보기", Sequence = "Win+Tab", Icon = "glyph:E7C4" },
            new() { Type = "desktop", Direction = "next" },
            new() { Type = "desktop", Direction = "prev" },
            new() { Type = "windows" },
        },
    };
}
