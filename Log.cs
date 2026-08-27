using System.IO;
namespace RingLauncher;

public static class Log
{
    static readonly string Path = System.IO.Path.Combine(Config.ConfigStore.Dir, "log.txt");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Config.ConfigStore.Dir);
            File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch { /* 로그 실패는 무시 */ }
    }

    public static void Write(Exception ex) => Write(ex.ToString());
}
