using RingLauncher.Interop;

namespace RingLauncher.Triggers;

/// <summary>링을 여는 입력 소스. Pressed → (이동) → Released 순서. 모든 이벤트는 UI 스레드.</summary>
public interface ITrigger : IDisposable
{
    event Action<POINT>? Pressed;
    event Action? Released;

    /// <summary>Released 이후에도 물리적으로 눌려 있을 수 있는 키들. 실행 전 모두 떼어질 때까지 기다린다.</summary>
    int[] LingeringKeys { get; }

    void Start();
    /// <summary>컨트롤러가 트리거 외 경로(클릭/ESC)로 닫았을 때 호출. 다음 Pressed를 받을 수 있는 상태로.</summary>
    void Reset();
    void Stop();
}
