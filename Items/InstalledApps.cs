using System.IO;

namespace RingLauncher.Items;

public readonly record struct InstalledApp(string Name, string LnkPath);

/// <summary>시작 메뉴(.lnk) 스캔으로 설치된 프로그램 목록. 검색 창의 데이터 소스.</summary>
public static class InstalledApps
{
    static List<InstalledApp>? _cache;

    static readonly string[] Roots =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
    };

    /// <summary>한 번 스캔해 캐시. 이름 오름차순, 이름 기준 중복 제거.</summary>
    public static IReadOnlyList<InstalledApp> All()
    {
        if (_cache is not null) return _cache;
        var map = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots.Distinct())
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            try
            {
                // IgnoreInaccessible: '프로그램'(로컬라이즈된 Programs 정션) 등 접근 불가 폴더에서 전체가 죽지 않게
                var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
                foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", opts))
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    // 언인스톨/도움말류 소음 제외
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    map.TryAdd(name, new InstalledApp(name, lnk));
                }
            }
            catch (Exception ex) { Log.Write($"시작 메뉴 스캔 실패({root}): {ex.Message}"); }
        }
        _cache = map.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return _cache;
    }

    /// <summary>부분 문자열 매칭(공백 분리 AND). 이름 시작 일치를 앞으로.</summary>
    public static List<InstalledApp> Search(string query, int max = 30)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return All().Take(max).ToList();
        return All()
            .Where(a => terms.All(t => a.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(a => a.Name.StartsWith(terms[0], StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(max)
            .ToList();
    }
}
