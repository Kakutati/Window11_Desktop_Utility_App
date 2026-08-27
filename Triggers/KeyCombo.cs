using System.Windows.Input;
using RingLauncher.Interop;

namespace RingLauncher.Triggers;

/// <summary>"Ctrl+Alt+Space" 같은 문자열 → RegisterHotKey 수정자 + VK.</summary>
public readonly record struct KeyCombo(uint Modifiers, int Vk, int[] ModifierVks)
{
    public static KeyCombo Parse(string text)
    {
        uint mods = 0; int vk = 0; var modVks = new List<int>();
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Native.MOD_CONTROL; modVks.Add(Native.VK_CONTROL); break;
                case "alt": mods |= Native.MOD_ALT; modVks.Add(Native.VK_MENU); break;
                case "shift": mods |= Native.MOD_SHIFT; modVks.Add(Native.VK_SHIFT); break;
                case "win": mods |= Native.MOD_WIN; modVks.Add(Native.VK_LWIN); break;
                default:
                    if (!Enum.TryParse<Key>(raw, true, out var key)) throw new FormatException($"알 수 없는 키: {raw}");
                    vk = KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        if (vk == 0) throw new FormatException($"수정자 외에 키가 하나 필요합니다: {text}");
        return new KeyCombo(mods, vk, modVks.ToArray());
    }
}
