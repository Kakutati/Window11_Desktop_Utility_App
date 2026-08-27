using RingLauncher.Core;
using RingLauncher.Interop;
using RingLauncher.Triggers;

namespace RingLauncher;

/// <summary>`RingLauncher.exe --selftest` — 히트 테스트/키 파서 검증. 실패 시 exit 1.</summary>
static class SelfTest
{
    public static int Run()
    {
        Native.AttachConsole(-1);
        var fails = new List<string>();
        void Check(string name, bool ok) { if (!ok) fails.Add(name); }
        static Hit T(double dx, double dy, OuterRing? o = null) => HitTester.Test(dx, dy, 28, 140, -90, 8, o);

        Check("위→0", T(0, -100) == new Hit(HitZone.Inner, 0));
        Check("오른쪽→2", T(100, 0).Index == 2);
        Check("아래→4", T(0, 100).Index == 4);
        Check("왼쪽→6", T(-100, 0).Index == 6);
        Check("대각→1", T(70, -70).Index == 1);
        Check("dead zone", T(10, -10).Zone == HitZone.Dead);
        Check("링 바깥도 inner", T(0, -300).Zone == HitZone.Inner);

        var outer = new OuterRing(-90, 90, 3);
        Check("outer 첫 섹터", T(0, -200, outer) == new Hit(HitZone.Outer, 0));
        Check("outer span 밖→None", T(200, 0, outer).Zone == HitZone.None);
        Check("outer 펼침 중 안쪽→inner", T(0, -100, outer).Zone == HitZone.Inner);

        var o3 = HitTester.Outer(-90, 3);
        Check("Outer(3) 기하", Math.Abs(o3.StartAngle - (-130)) < 1e-9 && Math.Abs(o3.SpanAngle - 120) < 1e-9 && o3.Count == 3);
        var o12 = HitTester.Outer(0, 12);
        Check("Outer(12) 360° 상한", Math.Abs(o12.SpanAngle - 360) < 1e-9 && Math.Abs(o12.StartAngle - (-165)) < 1e-9);
        Check("outer 끝 섹터", T(-153, -128, o3).Index == 0 && T(-128, -153, o3) == new Hit(HitZone.Outer, 0));

        var kc = KeyCombo.Parse("Ctrl+Alt+Space");
        Check("KeyCombo", kc.Modifiers == (Native.MOD_CONTROL | Native.MOD_ALT) && kc.Vk == 0x20 && kc.ModifierVks.Length == 2);

        Console.WriteLine(fails.Count == 0 ? "selftest OK" : "selftest FAIL: " + string.Join(", ", fails));
        return fails.Count == 0 ? 0 : 1;
    }
}
