using System.Diagnostics;
using System.IO;

namespace RingLauncher.Items;

/// <summary>
/// 검색 대상 앱. ViaAppsFolder면 shell:AppsFolder로 실행(UWP/Store/Win32 공통), 아니면 .lnk 직접 실행(폴백).
/// </summary>
public readonly record struct InstalledApp(string Name, string Target, bool ViaAppsFolder)
{
    public void Launch()
    {
        if (ViaAppsFolder)
        {
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            psi.ArgumentList.Add("shell:AppsFolder\\" + Target); // AUMID/경로에 공백이 있어도 안전
            Process.Start(psi);
        }
        else
        {
            Process.Start(new ProcessStartInfo(Target) { UseShellExecute = true });
        }
    }
}

/// <summary>
/// 설치된 앱 목록. 기본은 shell:AppsFolder(시작 메뉴 '모든 앱'과 동일 — UWP/Store/Win32 포함).
/// COM 열거가 실패하면 시작 메뉴 .lnk 스캔으로 폴백.
/// </summary>
public static class InstalledApps
{
    static List<InstalledApp>? _cache;

    public static IReadOnlyList<InstalledApp> All()
    {
        if (_cache is not null) return _cache;
        var apps = FromAppsFolder();
        if (apps.Count == 0)
        {
            Log.Write("AppsFolder 열거 결과 없음 → .lnk 스캔으로 폴백");
            apps = FromStartMenuShortcuts();
        }
        _cache = apps
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Log.Write($"검색 대상 앱 {_cache.Count}개 로드");
        return _cache;
    }

    /// <summary>shell:AppsFolder 열거 (Shell.Application COM). 실패 시 빈 목록.</summary>
    static List<InstalledApp> FromAppsFolder()
    {
        var list = new List<InstalledApp>();
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t is null) return list;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            dynamic items = folder.Items();
            int n = items.Count;
            for (int i = 0; i < n; i++)
            {
                dynamic it = items.Item(i);
                string name = it.Name, path = it.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new InstalledApp(name, path, ViaAppsFolder: true));
            }
        }
        catch (Exception ex) { Log.Write($"AppsFolder 열거 실패: {ex.Message}"); }
        return list;
    }

    static readonly string[] Roots =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
    };

    /// <summary>폴백: 시작 메뉴 .lnk 스캔.</summary>
    static List<InstalledApp> FromStartMenuShortcuts()
    {
        var list = new List<InstalledApp>();
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        foreach (var root in Roots.Distinct())
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", opts))
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new InstalledApp(name, lnk, ViaAppsFolder: false));
                }
            }
            catch (Exception ex) { Log.Write($"시작 메뉴 스캔 실패({root}): {ex.Message}"); }
        }
        return list;
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
