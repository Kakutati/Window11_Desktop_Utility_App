# RingLauncher — 설계 문서

Windows 11 작업 표시줄을 숨기고, 커서 주변 방사형 런처로 대체하는 데스크톱 유틸리티.
스택: C# / .NET 8 / WPF / Win32 P/Invoke. 단일 실행 파일, 비관리자 권한 우선.

---

## 0. 설계 원칙 (결정 사항)

| 결정 | 이유 |
|---|---|
| 외부 NuGet 0개 (MVP) | 트레이 아이콘은 in-box `System.Windows.Forms.NotifyIcon`, JSON은 `System.Text.Json`, 나머지는 P/Invoke |
| 기본 트리거 = `RegisterHotKey` | 관리자 창이 포그라운드여도 동작(UIPI 무관). 훅은 확장으로만 |
| 링 창은 링 크기만큼만 (전체 화면 오버레이 아님) | 다른 앱 입력을 차단하지 않음. 커서 추적은 `GetCursorPos` 폴링 |
| 기본 작업 표시줄 모드 = `ABM_SETSTATE` 자동 숨김 | Explorer가 되살리지 않음, 복구가 단순(상태 1개) |
| 가상 데스크톱 전환 = `SendInput(Win+Ctrl+←/→)` | 비공개 COM(IVirtualDesktopManagerInternal)은 빌드마다 GUID가 바뀜 |
| 빠른 설정 MVP = 미디어 키 + `ms-settings:` URI | 밝기 DDC/CI, Wi-Fi 토글은 관리자/드라이버 의존. 슬라이더는 확장 |
| 배포 = framework-dependent single-file | WPF는 trimming/AOT 불가. self-contained는 150MB+ |

---

## 1. 아키텍처 개요

```
┌──────────────────────────────────────────────────────────────┐
│ App (진입점, 예외 핸들러, 트레이, 설정 로드)                    │
├──────────────────────────────────────────────────────────────┤
│ Core                                                          │
│   RingController  ── 상태 머신: Idle → Open → Executing        │
│   HitTester       ── (dx,dy) → (ring, sector) 순수 함수         │
│   RingModel       ── 현재 표시 중인 항목 트리                    │
├──────────────────────────────────────────────────────────────┤
│ Triggers (ITrigger)          │ Items (IRingItem / IItemProvider)│
│   HotkeyTrigger              │   StaticAppItem                  │
│   CtrlDoubleTapTrigger (훅)  │   WindowListProvider             │
│   MiddleHoldTrigger (훅)     │   QuickSettingsProvider          │
│   LowLevelHookHost (공유)    │   VirtualDesktopProvider          │
│                              │   SubmenuItem, UriItem, KeyItem   │
├──────────────────────────────────────────────────────────────┤
│ Shell                                                         │
│   TaskbarController (ITaskbarStrategy: AppBarAutoHide / HideWindow)│
│   TaskbarStateStore  ── 크래시 복구용 상태 파일                   │
│   ShellEventWindow   ── TaskbarCreated / WM_HOTKEY 수신 창       │
├──────────────────────────────────────────────────────────────┤
│ UI (WPF)                                                      │
│   RingWindow  ── 레이어드/NOACTIVATE 오버레이, 섹터 렌더, 애니메이션│
│   SettingsWindow ── 설정 편집, 드래그앤드롭 항목 추가              │
├──────────────────────────────────────────────────────────────┤
│ Interop                                                       │
│   NativeMethods (P/Invoke), MonitorInfo, DpiHelper, ForegroundHelper│
└──────────────────────────────────────────────────────────────┘
```

### 주요 클래스와 책임

| 클래스 | 책임 |
|---|---|
| `App` | `DispatcherUnhandledException`/`AppDomain.UnhandledException`/`UnobservedTaskException` → 작업 표시줄 복구 후 종료. 시작 시 이전 크래시 잔여 상태 복구. 단일 인스턴스(Mutex) |
| `ShellEventWindow` | **top-level** 숨김 창 (HWND_MESSAGE 아님 — 브로드캐스트 수신 필요). `WM_HOTKEY`, `TaskbarCreated`, `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE` 처리 |
| `ITrigger` | `Start()/Stop()`, `event Pressed(POINT cursor)`, `event Released()`, `event Cancelled()` |
| `HotkeyTrigger` | `RegisterHotKey` → Pressed. 열린 동안 8ms 타이머로 `GetAsyncKeyState` 폴링해 릴리즈 감지 |
| `LowLevelHookHost` | 훅 1개씩(WH_KEYBOARD_LL/WH_MOUSE_LL)을 전용 STA 스레드에 설치. 콜백은 큐에 넣고 즉시 반환(타임아웃 방지). 훅 트리거들이 구독 |
| `RingController` | 트리거 이벤트 → 정책 검사 → 모델 구성 → 창 표시 → 프레임마다 히트 테스트 → 릴리즈 시 실행/취소 |
| `HitTester` | 입력: 중심 기준 벡터(DIP), 섹터 수, 반지름들. 출력: `None / Dead / Inner(i) / Outer(i)` |
| `RingWindow` | 창 스타일 확장, 물리 픽셀 `SetWindowPos`, `WM_MOUSEACTIVATE→MA_NOACTIVATE`, 섹터 지오메트리, 120ms 페이드/스케일 |
| `ITaskbarStrategy` | `Apply()`, `Restore()`, `Reapply()`(Explorer 재시작 후) |
| `TaskbarStateStore` | `%LOCALAPPDATA%\RingLauncher\taskbar-state.json` — Apply 직전 원래 상태 기록, Restore 후 삭제 |
| `WindowListProvider` | `EnumWindows` + 필터 → `WindowItem(hwnd)`; 실행 = `ForegroundHelper.Activate(hwnd)` |
| `ConfigStore` | `config.json` 로드/저장/검증, `FileSystemWatcher`로 핫 리로드 |

---

## 2. Win32 인터롭 지점

| 지점 | API | 플래그/비고 |
|---|---|---|
| 작업 표시줄 자동 숨김 | `SHAppBarMessage(ABM_GETSTATE)` → 저장, `ABM_SETSTATE` | `lParam = ABS_AUTOHIDE \| ABS_ALWAYSONTOP`. 복구 시 저장값 재설정 |
| 작업 표시줄 창 숨김 | `FindWindow("Shell_TrayWnd")`, `EnumWindows`로 `Shell_SecondaryTrayWnd` 전부, `ShowWindow(SW_HIDE/SW_SHOW)` | 숨겨도 작업 영역은 안 바뀜 → `SystemParametersInfo(SPI_SETWORKAREA, ..., SPIF_SENDCHANGE)` 모니터별. 복구 시 원래 rcWork 재설정 |
| Explorer가 되살리는 것 감지 | `SetWinEventHook(EVENT_OBJECT_SHOW, WINEVENT_OUTOFCONTEXT)` on Shell_TrayWnd | HideWindow 모드 전용. Win 키·알림 등에 Explorer가 SW_SHOW 하면 재숨김 |
| Explorer 재시작 | `RegisterWindowMessage("TaskbarCreated")` | top-level 창에만 브로드캐스트. 수신 시 `Reapply()` + NotifyIcon 재등록 |
| 전역 핫키 | `RegisterHotKey(hwnd, id, MOD_*\|MOD_NOREPEAT, vk)` / `UnregisterHotKey` | 링 열린 동안만 `VK_ESCAPE` 단독 핫키 추가 등록 → ESC 취소를 훅 없이 처리 |
| 키/버튼 릴리즈 감지 | `GetAsyncKeyState(vk)` | 8ms `DispatcherTimer` |
| LL 훅 | `SetWindowsHookEx(WH_KEYBOARD_LL / WH_MOUSE_LL, proc, hMod, 0)` | 콜백 300ms 이내 반환 필수(초과 누적 시 조용히 해제). 주입 이벤트 구분: `LLKHF_INJECTED`, `dwExtraInfo` 매직값 |
| 가운데 버튼 홀드 재생 | `SendInput(MOUSEEVENTF_MIDDLEDOWN/UP, dwExtraInfo=MAGIC)` | 홀드 판정 전 삼킨 다운을 짧은 클릭이었으면 재생 |
| 커서 위치 | `GetCursorPos` | 물리 픽셀 |
| 모니터/DPI | `MonitorFromPoint(MONITOR_DEFAULTTONEAREST)`, `GetMonitorInfo`(rcMonitor), `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` | 클램핑 및 DIP↔물리 변환 |
| 오버레이 창 스타일 | `GetWindowLongPtr/SetWindowLongPtr(GWL_EXSTYLE)` | `WS_EX_LAYERED`(WPF가 설정) `\| WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE \| WS_EX_TOPMOST` |
| 창 위치 | `SetWindowPos(HWND_TOPMOST, x, y, cx, cy, SWP_NOACTIVATE \| SWP_SHOWWINDOW)` | 물리 픽셀 직접 지정(WPF Left/Top 우회) |
| 활성화 방지 | `WM_MOUSEACTIVATE` → `MA_NOACTIVATE` | `HwndSource.AddHook` |
| 창 목록 | `EnumWindows`, `IsWindowVisible`, `GetWindow(GW_OWNER)`, `GetWindowLongPtr(GWL_EXSTYLE)`, `GetWindowTextW`, `DwmGetWindowAttribute(DWMWA_CLOAKED)` | 필터: 보임 && 제목≠"" && (오너 없음 \|\| APPWINDOW) && !TOOLWINDOW && !cloaked(다른 가상 데스크톱/UWP 유령 제외) |
| 창 아이콘 | `SendMessageTimeout(WM_GETICON, ICON_BIG)`, `GetClassLongPtr(GCLP_HICON)`, 실패 시 `SHGetFileInfo(exe, SHGFI_ICON)` | 상승 창엔 SendMessage 차단 → 타임아웃 200ms + 파일 아이콘 폴백 |
| 프로세스 경로 | `GetWindowThreadProcessId`, `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`, `QueryFullProcessImageName` | LIMITED는 상승 프로세스에도 허용 |
| 창 포커스 | `ShowWindow(SW_RESTORE)`(최소화 시), `SetForegroundWindow` | 실패 시 `SendInput(VK_MENU 탭)` 후 재시도. `AttachThreadInput`은 상승 창에 실패하므로 미사용 |
| 전체화면 판정 | `SHQueryUserNotificationState` | `QUNS_RUNNING_D3D_FULL_SCREEN`, `QUNS_BUSY`, `QUNS_PRESENTATION_MODE` → 정책 적용 |
| 볼륨/미디어 | `SendInput(VK_VOLUME_UP/DOWN/MUTE)` | 확장: `IAudioEndpointVolume` COM |
| 가상 데스크톱 | `SendInput(Win+Ctrl+Left/Right)` | 확장: `IVirtualDesktopManager`(공식, 창의 데스크톱 판별만) |
| 트레이 | `System.Windows.Forms.NotifyIcon` (`UseWindowsForms=true`) | in-box. TaskbarCreated 후 `Visible=false→true` |
| 앱 실행 | `Process.Start(UseShellExecute=true)` / `ShellExecuteEx` | URI(`ms-settings:`)도 동일 |

매니페스트: `<dpiAwareness>PerMonitorV2</dpiAwareness>`, `requestedExecutionLevel level="asInvoker"`.

---

## 3. 데이터 흐름: 트리거 → 렌더 → 히트 테스트 → 실행

```
[Trigger.Pressed(cursor)]
   │
   ▼
RingController.Open()
   ├─ 정책: SHQueryUserNotificationState → 전체화면이면 suppress/allow (설정)
   ├─ 이미 Open이면 무시(자동 반복)
   ├─ RingModel = providers.SelectMany(GetItems)   ← WindowList는 이 시점에 EnumWindows (동기, <10ms)
   ├─ hMon = MonitorFromPoint(cursor); dpi = GetDpiForMonitor
   ├─ sizePx = (subRadius*2 + margin) * dpi/96
   ├─ centerPx = clamp(cursor, rcMonitor 안쪽 sizePx/2)      ← 가장자리 클램핑
   ├─ RegisterHotKey(ESC)
   └─ RingWindow.ShowAt(centerPx, sizePx, model)
        ├─ SetWindowPos(TOPMOST, NOACTIVATE)
        ├─ 섹터 지오메트리 생성 (N개, 시작각 -90°)
        └─ Storyboard: Opacity 0→1, Scale .85→1, 120ms EaseOut
   │
   ▼  (CompositionTarget.Rendering, 프레임마다)
   ├─ GetCursorPos → v = (cursor - centerPx) / (dpi/96)     ← DIP 벡터
   ├─ hit = HitTester.Hit(v, N, deadZone, innerR, outerR, subR)
   │     r = |v|; θ = atan2(v.y, v.x) → 0..360, (θ - startAngle + 360/N/2) mod 360
   │     sector = floor(θ' / (360/N))
   │     r < deadZone → Dead ; r < outerR → Inner(sector) ; else Outer(subSector) (확장 중일 때만)
   ├─ hit != prevHit → 하이라이트 갱신
   └─ Inner(i)가 submenu이고 r > outerR*0.8 → 바깥 링 확장(children M개, 부모 섹터 중심각 기준 ±범위)
   │
   ▼
[Trigger.Released]  ── 또는 ── [ESC 핫키] / [Dead 에서 릴리즈] → Cancel
   ├─ selected = 현재 hit 항목 (submenu 자체면 Cancel)
   ├─ RingWindow.Hide(); UnregisterHotKey(ESC); 상태=Idle
   └─ Dispatcher.BeginInvoke(selected.Execute)   ← 창 숨긴 뒤 실행해야 SetForegroundWindow 성공
```

입력 차단 정책: 링 창은 링 영역 크기만. 투명 픽셀은 클릭 통과(레이어드 창 기본). 마우스 클릭은 링 조작에 불필요(호버 선택)하므로 다른 앱 입력은 차단하지 않음. 키보드는 ESC만 가로챔.

---

## 4. 설정 JSON 스키마 (`%LOCALAPPDATA%\RingLauncher\config.json`)

```jsonc
{
  "version": 1,
  "taskbar": {
    "mode": "autohide",            // "autohide" | "hideWindow" | "none"
    "reclaimWorkArea": true        // hideWindow 모드에서 SPI_SETWORKAREA 적용 여부
  },
  "trigger": {
    "type": "hotkey",              // "hotkey" | "ctrlDoubleTap" | "middleHold"
    "hotkey": "Ctrl+Alt+Space",    // 파서: 수정자+ / VK 이름. Win+Space는 IME 전환이라 기본 제외
    "doubleTapMs": 300,
    "holdMs": 200
  },
  "ring": {
    "deadZone": 28,                // DIP
    "innerRadius": 44,
    "outerRadius": 140,
    "subRadius": 220,
    "startAngle": -90,             // 첫 섹터 중심각 (위쪽)
    "animationMs": 120,
    "theme": { "background": "#CC202020", "accent": "#FF0078D4", "text": "#FFFFFFFF", "font": "Segoe UI Variable" }
  },
  "policy": {
    "fullscreen": "suppress",      // "suppress" | "allow"
    "windowListMax": 8             // 창 목록 섹터 상한, 초과 시 submenu로
  },
  "items": [
    { "type": "app",     "label": "Terminal", "path": "wt.exe", "args": "", "icon": null },
    { "type": "windows", "label": "창" },                       // 실행 중 창 목록 (동적)
    { "type": "submenu", "label": "빠른 설정", "items": [
        { "type": "quick", "action": "volumeMute" },            // volumeUp/Down/Mute, brightnessSettings, wifiFlyout
        { "type": "uri",   "label": "Wi-Fi", "uri": "ms-availablenetworks:" },
        { "type": "uri",   "label": "밝기",  "uri": "ms-settings:display" }
    ]},
    { "type": "desktop", "direction": "next" },                 // "next" | "prev"
    { "type": "keys",    "label": "작업 보기", "sequence": "Win+Tab" }
  ]
}
```

검증 규칙: `items.length` 2..12 (초과 시 로드 거부 + 기본값), 반지름 단조 증가, 알 수 없는 `type`은 무시하고 경고. 드래그앤드롭은 SettingsWindow에 파일 드롭 → `{ "type":"app", "path":... }` 추가 후 저장 → 핫 리로드.

---

## 5. 구현 순서와 검증

| 단계 | 내용 | 검증 |
|---|---|---|
| **0. 골격** | 프로젝트, 매니페스트(PMv2, asInvoker), 트레이 아이콘, 단일 인스턴스, 전역 예외 핸들러, config 로드 | 트레이 표시/종료. 잘못된 JSON → 기본값 + 경고 |
| **1. 링 MVP** | RingWindow(스타일 확장, NOACTIVATE), HotkeyTrigger + 릴리즈 폴링, 고정 앱 항목, HitTester, ESC 핫키, 애니메이션 | 메모장에 타이핑 중 핫키 → 링 뜨고 `GetForegroundWindow` 불변, 타이핑 계속됨. 화면 모서리에서 잘리지 않음. HitTester 단위 검사: 8섹터 경계각/deadZone/바깥 반지름 각 1케이스 |
| **2. 작업 표시줄** | 두 전략, TaskbarStateStore, TaskbarCreated 재적용, WinEventHook 재숨김 | 정상 종료 → 복구. 작업 관리자 강제 종료 → 재실행 시 복구. `taskkill /f /im explorer.exe; start explorer` → 1초 내 재숨김 + 트레이 복귀. hideWindow 모드: 최대화 창이 하단까지 차지 |
| **3. 창 목록** | WindowListProvider, 아이콘, ForegroundHelper | 8개 이상 창 → submenu. 최소화 창 복원. 관리자 메모장 포커스 성공 여부 기록(실패 시 Alt 탭 폴백 동작) |
| **4. 서브메뉴/빠른 설정/데스크톱** | 바깥 링 확장, quick/uri/desktop/keys 항목 | 서브메뉴 진입-이탈 왕복 시 깜빡임 없음. 볼륨 키·데스크톱 전환 동작 |
| **5. 훅 트리거** | LowLevelHookHost, CtrlDoubleTap, MiddleHold(삼킴+재생) | 1분간 빠른 타이핑/스크롤 중 입력 유실 0. 관리자 창 포커스 상태에서 훅 트리거가 안 되는 것을 확인하고 UI에 안내 |
| **6. 엣지 케이스** | 멀티 모니터 DPI, 전체화면 정책, DISPLAYCHANGE | 100%/150% 혼합 모니터에서 링 물리 크기 일정, 경계 넘어갈 때 크기 튐 없음. exclusive 전체화면 게임에서 suppress; borderless에서 표시 |
| **7. 설정 UI** | SettingsWindow, 드래그앤드롭, 핫 리로드 | exe 드롭 → 링에 즉시 반영 |
| **8. 배포** | `PublishSingleFile`, framework-dependent | 깨끗한 VM(.NET 8 Desktop Runtime만)에서 실행 |

---

## 6. 기술적 리스크와 대안

| 리스크 | 영향 | 대안 |
|---|---|---|
| **Win11 작업 표시줄이 XAML 호스팅** — `Shell_TrayWnd` 숨겨도 Win 키/알림/토스트에 Explorer가 되살림 | hideWindow 모드 불안정 | 기본값을 autohide 모드로. hideWindow는 WinEventHook 재숨김 + "실험적" 표기 |
| `ABM_SETSTATE` 자동 숨김이 레지스트리(`StuckRects3`)에 남음 → 크래시 후 다음 실행 전까지 자동 숨김 유지 | 사용자 혼란 | 상태 파일 기반 시작 시 복구 + `--restore-taskbar` CLI 플래그 + 트레이 "복구" 메뉴. 최후 수단: Explorer 재시작 안내 |
| **UIPI**: 관리자 창이 포그라운드일 때 LL 훅이 입력을 못 봄, `SendMessage`/`AttachThreadInput` 차단 | 훅 트리거 불가, 아이콘 조회 실패 | 기본 트리거를 RegisterHotKey로(영향 없음). 아이콘은 파일 아이콘 폴백. 근본 해결은 `uiAccess=true`(서명+Program Files 설치 필요) — 요구사항과 충돌하므로 미채택 |
| `SetForegroundWindow` 포그라운드 잠금 규칙 | 창 목록 클릭이 깜빡임만 | 링 숨긴 뒤 실행 + Alt 탭 주입 폴백. 그래도 실패하면 `SwitchToThisWindow` |
| Exclusive 전체화면 게임 위에 TOPMOST 창이 안 보임 | 링 표시 불가 | `SHQueryUserNotificationState`로 suppress. 대부분 게임은 borderless라 실제 영향 제한적 |
| LL 훅 콜백 타임아웃 누적 시 조용히 해제 | 훅 트리거 갑자기 멈춤 | 콜백은 큐잉만. 30초마다 훅 생존 확인(자체 테스트 키 주입은 안 함 — 훅 핸들 유효성 + 최근 이벤트 시각) 후 재설치 |
| `RegisterHotKey`로 수정자 단독/Win 단독 조합 불가, 충돌(이미 등록된 키) | 트리거 설정 제한 | 등록 실패 시 트레이 풍선 + 설정 창 안내. 수정자 단독은 훅 트리거로 |
| WPF `AllowsTransparency` 창은 `UpdateLayeredWindow` 경로 → GPU 가속 제한 | 프레임 드랍 | 링 창 400~500px로 제한, 지오메트리 `Freeze()`, 하이라이트만 갱신. 부족하면 `DwmExtendFrameIntoClientArea` + 투명 배경 방식으로 교체 |
| 첫 표시 지연(창 생성/JIT) | 150ms 초과 | 시작 시 창 미리 생성 후 `Visibility.Hidden`; `Show()`가 아닌 `SetWindowPos(SWP_SHOWWINDOW)`; ReadyToRun 컴파일 |
| 모니터 경계에서 `WM_DPICHANGED`로 크기 튐 | 1프레임 깜빡임 | 클램핑으로 항상 단일 모니터 안에 위치. 그래도 튀면 모니터별 RingWindow 인스턴스(확장) |
| 가상 데스크톱 COM 비공개 인터페이스 | 빌드 업데이트로 파손 | SendInput 단축키로 대체(채택). 특정 데스크톱 직접 이동은 미지원 |
| Explorer 재시작 시 `TaskbarCreated`가 메시지 전용 창엔 안 옴 | 재적용 실패 | ShellEventWindow를 0×0 top-level 툴윈도우로 |
| `SPI_SETWORKAREA` 복구 누락 시 최대화 창 영역 어긋남 | 크래시 후 레이아웃 이상 | 상태 파일에 원래 rcWork 저장. Explorer 재시작이 항상 원복하므로 안내 |
| 훅 트리거 릴리즈 중 Win/Ctrl 단독 릴리즈가 시작 메뉴/IME를 건드림 | 부작용 | 트리거 활성 시 릴리즈 이벤트 삼킴(훅 반환 1) — 훅 트리거 한정 |

---

## 부록: 상태 머신

```
Idle ──Pressed──▶ Opening(정책/모델/위치) ──▶ Open(프레임 히트테스트)
                                              │ Released@Item ──▶ Executing ──▶ Idle
                                              │ Released@Dead / ESC / Cancelled ──▶ Idle
                                              │ Pressed(반복) ──▶ 무시
Any ──TaskbarCreated──▶ Taskbar.Reapply (링 상태와 독립)
Any ──UnhandledException──▶ Taskbar.Restore → 종료
```
