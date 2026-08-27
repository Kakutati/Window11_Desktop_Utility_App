namespace RingLauncher.Core;

public enum HitZone { None, Dead, Inner, Outer }

public readonly record struct Hit(HitZone Zone, int Index)
{
    public static readonly Hit None = new(HitZone.None, -1);
    public static readonly Hit Dead = new(HitZone.Dead, -1);
}

/// <summary>바깥 링(서브메뉴)이 펼쳐진 상태. 부모 섹터 중심각 기준으로 span 안에 count개.</summary>
public readonly record struct OuterRing(double StartAngle, double SpanAngle, int Count);

public static class HitTester
{
    /// <param name="dx">중심 기준 커서 벡터 (DIP), 화면 좌표계(y 아래 방향)</param>
    public static Hit Test(double dx, double dy, double deadZone, double outerRadius, double startAngle,
        int innerCount, OuterRing? outer = null)
    {
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < deadZone) return Hit.Dead;
        if (innerCount <= 0) return Hit.None;

        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;

        if (outer is { Count: > 0 } o && r >= outerRadius)
        {
            var idx = SectorAt(angle, o.StartAngle, o.SpanAngle, o.Count);
            return idx < 0 ? Hit.None : new Hit(HitZone.Outer, idx);
        }
        return new Hit(HitZone.Inner, SectorAt(angle, startAngle, 360, innerCount));
    }

    /// <summary>첫 섹터의 중심이 start에 오도록 배치. span 밖이면 -1.</summary>
    public static int SectorAt(double angle, double start, double span, int count)
    {
        var w = span / count;
        var a = ((angle - start + w / 2) % 360 + 360) % 360;
        return a >= span ? -1 : (int)(a / w);
    }

    /// <summary>i번째 섹터의 중심각(도).</summary>
    public static double SectorCenter(double start, double span, int count, int i) => start + span / count * i;
}
