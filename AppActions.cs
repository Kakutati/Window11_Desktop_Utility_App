namespace RingLauncher;

/// <summary>
/// 링 항목이 앱 레벨 동작(설정 열기, 작업 표시줄 토글)을 호출하기 위한 훅.
/// Program/AppHost가 실제 동작을 채운다. 항목이 UI/호스트에 직접 의존하지 않게 하는 최소 연결.
/// </summary>
public static class AppActions
{
    public static Action? OpenSettings;
    public static Action? ToggleTaskbar;
}
