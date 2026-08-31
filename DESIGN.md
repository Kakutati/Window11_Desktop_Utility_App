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
| `ConfigStore` | `config.json` 로드/저장/검증 |
| `AppHost` | 재구성 런타임 소유. 저장·외부 편집을 `Reload()` 한 경로로. shell/hooks/RingWindow는 영구, 트리거/작업표시줄/컨트롤러만 교체 |
| `SettingsWindow` | 핫키 캡처+충돌 검사, 트리거/모드/작업표시줄/정책 콤보, 반지름 슬라이더, 항목 JSON+드래그앤드롭, 시작 프로그램 토글 |

---

## 2. Win32 인터롭 지점

| 지점 | API | 플래그/비고 |
|---|---|---|
| 작업 표시줄 자동 숨김 | `SHAppBarMessage(ABM_GETSTATE)` → 저장, `ABM_SETSTATE` | `lParam = ABS_AUTOHIDE \| ABS_ALWAYSONTOP`. 복구 시 저장값 재설정 |
| 작업 표시줄 창 숨김 | autohide 적용 후 `FindWindow("Shell_TrayWnd")` + `Shell_SecondaryTrayWnd` 전부 `ShowWindow(SW_HIDE)`, 복구 `SW_SHOWNA` | `SPI_SETWORKAREA`는 **사용 금지**: Explorer가 1초 내 되돌리고 `SPIF_SENDCHANGE` 브로드캐스트가 메인 스레드를 멈춤(실측). autohide가 작업 영역을 대신 반환 |
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
| 시작 메뉴(윈도우 버튼) | `SendInput(VK_LWIN down/up)` | RegisterHotKey로는 단독 수정자 불가 → SendInput로 전송 |
| 프로그램 검색 | `shell:AppsFolder` 열거(Shell.Application COM) → UWP/Store/Win32 포함, 실행 `explorer shell:AppsFolder\<AUMID·경로>`. 폴백: 시작 메뉴 `.lnk` 스캔(`IgnoreInaccessible`) | 검색 창은 포커스 필요 → NOACTIVATE 링과 별도 활성 창 |

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

열린 동안의 마우스 입력 (`GetAsyncKeyState(VK_LBUTTON/RBUTTON)` 전이 감지, hold/toggle 공통):
- 주 버튼 클릭 — 링 안(outerRadius 이내) 섹터면 실행, 그 외(dead zone·링 바깥)면 이탈
- 보조 버튼 클릭 — 이탈
- 트리거 릴리즈(hold) / 재탭(toggle) — 방향 제스처이므로 링 바깥 거리도 해당 방향 섹터로 인정, dead zone이면 이탈

입력 차단 정책: 링 창은 링 영역 크기만. 투명 픽셀은 클릭 통과(레이어드 창 기본). 링 밖 클릭은 이탈 처리와 동시에 아래 앱에도 전달됨(모달 아님). 키보드는 ESC만 가로챔.

---

## 4. 설정 JSON 스키마 (`%LOCALAPPDATA%\RingLauncher\config.json`)

```jsonc
{
  "version": 1,
  "taskbar": {
    "mode": "autohide"             // "autohide" | "hideWindow"(autohide + 창 숨김, 가장자리에서도 안 나옴) | "none"
  },
  "trigger": {
    "type": "hotkey",              // "hotkey" | "ctrlDoubleTap" | "middleHold"
    "hotkey": "Alt+OemTilde",      // Alt+` . 파서: 수정자+WPF Key 이름. Win+Space(IME 전환), Ctrl+Alt+Space(점유됨)는 기본 제외
    "mode": "hold",                // hold: 누른 채 이동 → 떼면 실행 | toggle: 누르면 열림, 다시 누르거나 클릭하면 실행/이탈
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

### 단계 0~1. 골격 + 링 MVP — 완료 (`3156e18`)
프로젝트/매니페스트/트레이/설정, `RegisterHotKey` 트리거(hold/toggle), NOACTIVATE 오버레이, 히트 테스트(`--selftest`), 클릭 처리(링 안 섹터 → 실행, 밖 → 이탈), app/uri/keys 항목.

### 단계 2. 작업 표시줄 제어 + 복구 — 완료
| # | 세부 항목 |
|---|---|
| 2-1 | `TaskbarController`: mode `autohide` / `hideWindow` / `none` |
| 2-2 | autohide: `ABM_GETSTATE` 저장 → `ABM_SETSTATE(ABS_AUTOHIDE\|ABS_ALWAYSONTOP)`, Restore 시 저장값 복원 |
| 2-3 | hideWindow: autohide + `Shell_TrayWnd`/`Shell_SecondaryTrayWnd` `SW_HIDE`, `SetWinEventHook(EVENT_OBJECT_SHOW)`로 되살아나면 재숨김. `SPI_SETWORKAREA` 미사용(실측: Explorer가 되돌림 + 브로드캐스트 행) |
| 2-4 | `taskbar-state.json`: Apply 직전 원래 상태 기록, Restore 후 삭제 |
| 2-5 | 시작 시 상태 파일 존재 → 이전 크래시로 판단, 먼저 복구 |
| 2-6 | 예외 핸들러 3종 + `SessionEnding`에서 Restore |
| 2-7 | `--restore-taskbar` CLI, 트레이 "작업 표시줄 복구" |
| 2-8 | `TaskbarCreated` 수신 → 0.5/2/5s 후 재적용 (NotifyIcon은 WinForms가 자체 재등록). 실측: Win11에서 Explorer 재시작 후 브로드캐스트까지 ~9초 걸리고, 그 뒤에도 Explorer가 트레이 창을 몇 번 더 보이게 함 → 훅이 즉시 재숨김 |
| 검증 | 강제 종료 → `--restore-taskbar` 또는 재실행 시 복구 / Explorer 재시작 → 재숨김 / 두 모드 모두 작업 영역 = 모니터 전체 |

### 단계 3. 실행 중 창 목록 — 완료 (단계 4 전까지는 안쪽 링에 인라인 확장, 초과분 "더 보기"는 단계 4)
| # | 세부 항목 |
|---|---|
| 3-1 | `WindowListProvider`: `EnumWindows` + 필터(보임, 제목 있음, 오너 없음 또는 APPWINDOW, TOOLWINDOW 아님, `DWMWA_CLOAKED` 아님) |
| 3-2 | 아이콘: `WM_GETICON`(SendMessageTimeout 200ms) → `GCLP_HICON` → exe 파일 아이콘 폴백 |
| 3-3 | `ForegroundHelper.Activate(hwnd)`: 최소화면 `SW_RESTORE` → `SetForegroundWindow` → 실패 시 Alt 탭 주입 후 재시도 → `SwitchToThisWindow` |
| 3-4 | `ItemFactory.CreateSource` → 링이 열릴 때마다 호출되는 항목 소스. `windows`는 호출 시점에 창 목록으로 확장(현재 포그라운드 창 제외, Z 순서) |
| 3-5 | `policy.windowListMax`까지만 표시. 초과분 "더 보기" 서브메뉴는 단계 4 |
| 검증 | 최소화 창 복원 ✅ / cloaked 창 제외 ✅ / 관리자 창 포커스 — UAC 없이 자동화 불가, 수동 확인 필요(실패 시 로그에 "창 전환 실패" 기록) |

### 단계 4. 서브메뉴(바깥 링) + 빠른 설정 + 가상 데스크톱 — 완료
| # | 세부 항목 |
|---|---|
| 4-1 | 바깥 링 렌더: `HitTester.Outer(parentCenter, count)` — 자식당 40°, 부모 중심각 기준 좌우 대칭(최대 360°), 반지름 outerRadius+6 ~ subRadius |
| 4-2 | 진입: `r > outerRadius×0.8` 또는 150ms 체류 → 펼침. 다른 inner 섹터/dead zone → 접힘. 부모에서 릴리즈/탭/클릭 → 펼친 채 유지(바깥 항목 클릭·ESC로 마무리) |
| 4-3 | `submenu` 항목(JSON 중첩 `items`) |
| 4-4 | `quick`: `volumeUp/Down/Mute`(미디어 키), `brightness`/`wifi`/`bluetooth`(`ms-settings:` / `ms-availablenetworks:`) |
| 4-5 | `desktop`: `next/prev` → `SendInput(Win+Ctrl+←/→)` |
| 4-6 | `icon: "glyph:E74F"` → Segoe Fluent Icons를 32px 비트맵으로 렌더. quick/desktop/submenu 기본 글리프 내장 |
| 검증 | 서브메뉴 진입-이탈 왕복 깜빡임 없음 / 볼륨·데스크톱 전환 / 시뮬레이션에 outer 시나리오 추가 |

### 단계 5. 훅 트리거 — 완료
| # | 세부 항목 |
|---|---|
| 5-1 | `LowLevelHookHost`: 전용 STA 스레드에 `WH_KEYBOARD_LL`/`WH_MOUSE_LL`. 삼킴 판정은 훅 스레드에서 동기(상수 시간), Pressed/Released는 UI 디스패처로 post. `dwExtraInfo == InjectMagic`인 재생 입력은 무시 |
| 5-2 | `CtrlDoubleTapTrigger`: down-up-down `doubleTapMs` 이내, 직전 Ctrl 누름 이후 다른 키 없음(Ctrl+C 뒤 Ctrl은 제외). 2번째 down/up은 삼킴. toggle이면 다음 더블탭이 Released |
| 5-3 | `MiddleHoldTrigger`: down 삼킴 → `holdMs` 내 up이면 UI 스레드에서 `SendInput` 재생, 넘기면 Pressed(누른 지점 기준). toggle이면 다음 누름이 Released |
| 5-4 | 훅 생존 감시 — 미구현(`ponytail:` 표시). 콜백이 상수 시간이라 타임아웃 제거 가능성 낮음. 실사용에서 끊김이 보고되면 30초 주기 재설치 추가 |
| 5-5 | 시작 로그에 UIPI 안내. 설정 UI(7단계)에서 트리거 선택 시 같은 문구 표시 |
| 검증 | 1분 빠른 타이핑/스크롤 중 유실 0 / 가운데 짧은 클릭이 브라우저 새 탭으로 정상 전달 |

### 단계 6. 엣지 케이스 — 완료(멀티 DPI는 수동 확인 대기)
| # | 세부 항목 |
|---|---|
| 6-1 | 멀티 DPI: 2단계 `SetWindowPos`(`ponytail:` 표시). 이 개발 PC는 단일 4K라 자동 검증 불가 — 혼합 배율 환경에서 수동 확인 필요. 튀면 모니터별 창 인스턴스 |
| 6-2 | `SHQueryUserNotificationState` → `policy.fullscreen` |
| 6-3 | `WM_DISPLAYCHANGE` 시 작업 표시줄 재적용(TaskbarController가 구독) |
| 6-4 | `WTSRegisterSessionNotification` → `WTS_SESSION_LOCK` 시 열린 링 강제 Close |
| 검증 | 전체화면(quns=2)에서 suppress → `Suppressed: fullscreen`, 해제(quns=5) 후 정상 Open ✅ / 멀티 DPI 물리 크기 일정 — 수동 확인 대기 |

### 단계 7. 설정 UI — 완료
| # | 세부 항목 |
|---|---|
| 7-1 | 트레이 "설정…"·더블클릭 → `SettingsWindow`, `--settings` 플래그로도 열림 |
| 7-2 | 핫키 입력 박스: 키 누르면 자동 기록, 즉시 시험 등록 → 충돌(1409) 표시 |
| 7-3 | 트리거 종류, hold/toggle, 작업 표시줄 모드, 전체화면 정책 → 라디오/콤보 |
| 7-4 | 바깥/서브 반지름 슬라이더(저장 시 적용). 색상·애니메이션은 JSON 편집. 실시간 미리보기는 추후(현재 저장 즉시 반영으로 대체) |
| 7-5 | 항목은 raw JSON 편집(모든 타입/중첩 서브메뉴 지원). GUI 목록 편집기는 추후 |
| 7-6 | exe/lnk 드래그앤드롭 → `app` 항목(lnk 대상 해석) |
| 7-7 | `AppHost.Reload` 단일 경로: 저장·외부 편집 모두 트리거 재등록 + 작업 표시줄 전략 교체 + 항목/링 재구성 |
| 7-8 | `FileSystemWatcher` 핫 리로드 |
| 7-9 | 시작 프로그램 등록 토글(`HKCU\...\Run`) |
| 검증 | UIAutomation으로 창 열기·mode 변경·저장 → config 반영 + 리로드 ✅ / 외부 편집 subRadius → 링 크기 즉시 변경 ✅ / 핫키 충돌 시험 등록 표시 ✅ |

### 단계 8. 배포 — 완료
| # | 세부 항목 |
|---|---|
| 8-1 | `dotnet publish -c Release -r win-x64` → 단일 exe 228KB(framework-dependent), 링 모양 app.ico 임베드 |
| 8-2 | framework-dependent라 런타임 미설치 시 OS가 안내 창 표시 |
| 8-3 | README.md: 설치·핫키·항목 타입·복구·빌드·제약 |
| 검증 | published exe --selftest 통과 ✅ / 실행·트레이·아이콘(32px) 확인 ✅ / 깨끗한 VM은 미검증 |

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
