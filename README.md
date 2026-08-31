# RingLauncher

Windows 11 작업 표시줄을 숨기고, 마우스 커서 주변에 뜨는 **방사형(ring) 런처**로 대체하는 데스크톱 유틸리티.
C# / .NET 8 / WPF / Win32 P/Invoke. 단일 실행 파일, 관리자 권한 불필요.

설계 문서는 [DESIGN.md](DESIGN.md) 참고.

## 요구 사항

- Windows 10/11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — 미설치 시 첫 실행에서 안내 창이 뜹니다.

## 실행

```
RingLauncher.exe
```

트레이 아이콘이 생기고, 기본 핫키 **Alt+`** 를 누른 채 마우스를 움직여 항목을 고른 뒤 떼면 실행됩니다.

- **hold 모드**(기본): 핫키를 누른 채 방향 이동 → 떼면 실행. dead zone(중앙)/ESC/우클릭 → 취소.
- **toggle 모드**: 한 번 누르면 열리고, 다시 누르거나 항목 클릭 → 실행/취소.
- 서브메뉴 항목은 바깥으로 밀거나 잠깐 머무르면 바깥 링으로 펼쳐집니다.

기본 항목에 **검색**(설치된 프로그램을 타이핑으로 찾아 실행)과 **시작**(Windows 시작 메뉴 열기)이 포함됩니다. 검색 창은 ↑/↓ 이동, Enter 실행, ESC/포커스 이탈 시 닫힘.

검색 대상은 시작 메뉴의 **모든 앱 목록(shell:AppsFolder)** — 일반 데스크톱 앱뿐 아니라 **UWP/Microsoft Store 앱**과 Windows **설정** 등도 포함합니다(실행은 `shell:AppsFolder`, COM 열거 실패 시 `.lnk` 스캔으로 폴백).

## 설정

트레이 아이콘 **더블클릭** 또는 우클릭 → **설정…** (또는 `RingLauncher.exe --settings`).

- 핫키 캡처(충돌 즉시 표시), 트리거 종류/동작, 작업 표시줄 모드, 전체화면 정책, 링 반지름
- 항목은 JSON으로 편집 — exe/바로가기를 창에 끌어다 놓으면 자동 추가
- Windows 시작 시 자동 실행 토글
- 저장하면 재시작 없이 즉시 적용. 설정 파일(`%LOCALAPPDATA%\RingLauncher\config.json`)을 직접 편집해도 자동 반영됩니다.

### 항목 타입

| type | 설명 | 주요 필드 |
|---|---|---|
| `app` | 앱/파일 실행 | `path`, `args` |
| `uri` | URI 실행 (`ms-settings:` 등) | `uri` |
| `keys` | 키 조합 전송 | `sequence` (예: `"Win+Tab"`) |
| `windows` | 실행 중인 창 목록(동적) | — |
| `submenu` | 바깥 링으로 펼쳐지는 하위 메뉴 | `items` |
| `search` | 설치된 프로그램 검색 런처 창 열기 | — |
| `settings` | 런처 설정 창 열기 | — |
| `taskbar` | 작업 표시줄 숨김/표시 토글 | — |
| `quick` | 빠른 설정 | `action`: `volumeUp`/`volumeDown`/`volumeMute`/`wifi`/`brightness`/`bluetooth`/`start`(시작 메뉴)/`search` |
| `desktop` | 가상 데스크톱 전환 | `direction`: `next`/`prev` |

아이콘은 `"icon": "glyph:E74F"` (Segoe Fluent Icons 코드포인트) 또는 파일 경로로 지정.

## 작업 표시줄 복구 / 재표시

작업 표시줄을 숨긴 상태에서 다시 보이게 하거나 설정에 접근하는 방법:

- **링에서**: 기본 링에 **설정** 항목과, 빠른 설정 안에 **작업 표시줄**(숨김/표시 토글) 항목이 있습니다. 트레이가 안 보여도 링만으로 접근 가능.
- **마우스**: 화면 맨 아래 끝으로 커서를 가져가면 autohide 작업 표시줄이 잠깐 나타납니다.
- **트레이**: 아이콘 우클릭 → 작업 표시줄 복구 / 설정….

앱이 비정상 종료해도 다음 실행 시 자동 복구됩니다. 수동 복구:

```
RingLauncher.exe --restore-taskbar
```

트레이 메뉴의 **작업 표시줄 복구** 로도 가능합니다.

작업 표시줄 모드:
- `autohide`(기본): 자동 숨김. Explorer가 되살리지 않아 안정적.
- `hideWindow`: 자동 숨김 + 트레이 창 완전 숨김(가장자리에서도 안 나옴). 실험적.
- `none`: 작업 표시줄 건드리지 않음(링 런처만 사용).

## 빌드 / 배포

```
dotnet build -c Debug
dotnet publish -c Release -r win-x64
```

publish 결과물은 `bin/Release/net8.0-windows/win-x64/publish/RingLauncher.exe` (단일 파일, framework-dependent).

자체 검사:

```
RingLauncher.exe --selftest
```

## 알려진 제약

- **관리자 권한 창**이 활성일 때는 UIPI 때문에 훅 트리거(Ctrl 더블탭·가운데 버튼 홀드)와 창 포커스가 제한됩니다. 이 경우 핫키 트리거(RegisterHotKey)를 쓰세요 — 영향받지 않습니다.
- **Exclusive fullscreen 게임** 위에는 오버레이가 뜨지 않아 기본적으로 억제(`policy.fullscreen: suppress`)됩니다.
- 멀티 모니터 혼합 DPI는 단일 모니터 개발 환경이라 자동 검증되지 않았습니다.
