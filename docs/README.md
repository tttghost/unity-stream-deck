# UnityStreamDeck

Unity Editor의 활성 탭을 감지하고, 이후 Elgato Stream Deck 플러그인과 양방향으로 연결하기 위한 프로젝트입니다.

## 프로젝트 구조

```text
unity-stream-deck/
├── project/
│   ├── streamdeck/      # Stream Deck 플러그인과 TypeScript 소스
│   └── unity-package/   # Unity Editor C# 패키지
├── command/   # 설치·패키징 명령
├── dist/      # 배포용 .streamDeckPlugin
└── docs/      # 사용법과 개발 안내
```

Stream Deck SDK와 현재 프로젝트의 연결 구조를 쉽게 설명한 [HTML 가이드](STREAM_DECK_SDK_GUIDE.html)도 함께 제공합니다.

## 현재 MVP

`Editor/EditorWindowFocusTracker.cs`가 다음 EditorWindow의 포커스 변경을 감지합니다.

- Scene View
- Game View
- Inspector
- Console
- Project
- Hierarchy

감지 시 Unity Console에 다음 형식으로 출력합니다.

```text
[UnityStreamDeck] Focus Changed: SceneView
[UnityStreamDeck] Focus Changed: Inspector
```

코드는 `Editor` 전용 어셈블리에 있으므로 플레이어 빌드와 런타임 어셈블리에 포함되지 않습니다.

## 테스트

설정 저장 문제와 Property Inspector 입력값 휘발 문제의 원인·재현 절차·재발 방지 규칙은 [TROUBLESHOOTING.md](TROUBLESHOOTING.md)와 [PROPERTY_INSPECTOR_PERSISTENCE.md](PROPERTY_INSPECTOR_PERSISTENCE.md)에 기록되어 있습니다.

1. 대상 Unity 프로젝트에서 **Window > Package Manager**를 엽니다.
2. 좌측 상단 **+** 메뉴에서 **Install package from disk...**를 선택합니다.
3. `project/unity-package/package.json`을 선택합니다.
4. 스크립트 컴파일이 끝나면 **Window > General > Console**을 엽니다.
5. Scene, Game, Inspector, Console, Project, Hierarchy 탭을 차례로 클릭합니다.
6. Console에서 `[UnityStreamDeck] Focus Changed: ...` 로그를 확인합니다.

설치 후 대상 프로젝트의 `Packages/manifest.json`에는 다음과 같은 로컬 의존성이 기록됩니다.

```json
"com.tttghost.unity-stream-deck": "file:../../unity-stream-deck/project/unity-package"
```

상대 경로는 대상 Unity 프로젝트와 이 저장소의 실제 위치에 따라 달라집니다. 패키지는 Unity 2022.3 이상을 대상으로 하며, 모든 C# 코드는 Editor 전용 어셈블리에만 포함됩니다.

## Stream Deck에서 Unity 제어

Unity 패키지는 로드될 때 다음 loopback WebSocket 서버를 자동으로 시작합니다.

```text
ws://127.0.0.1:18765/unitystreamdeck/
```

Unity 메뉴의 `Window > General > Unity Stream Deck`에서 연결 상태와 접속한 클라이언트 수를 확인할 수 있습니다. 기본값은 에디터 시작 시 자동 연결이며, 창에서 연결 해제와 다시 연결을 직접 실행할 수 있습니다.

현재 지원 명령은 다음과 같습니다. 수신한 명령은 Unity 메인 스레드에서 실행됩니다.

- `play`, `pause`, `stop`, `play.toggle`
- `console.clear`
- `build`: 활성 Build Target, 활성화된 Scene, Unity에 저장된 마지막 빌드 경로 사용
- `menu.execute`: 전체 Unity 메뉴 경로 실행
- `window.open`: 전체 CLR 타입 이름으로 `EditorWindow` 열기
- `method.invoke`: 정적이며 매개변수가 없는 Editor 메서드 실행
- `screenshot`: Unity Game View의 Target Resolution으로 PNG 캡처
- `screenshot.copy`: Game View 이미지를 시스템 클립보드에 복사 (macOS/Windows)

Stream Deck 플러그인과 Unity 패키지는 `project/` 아래에서 역할별로 분리 관리합니다.

### 플러그인 빌드 및 설치

플러그인 빌드에는 Node.js 20 이상과 Stream Deck 7.1 이상이 필요합니다. 매니페스트는 현재 설치된 Stream Deck 내장 런타임과 호환되도록 Node.js 20을 사용합니다.

```bash
cd project/streamdeck
npm install
npm run install:plugin
npm run refresh:app
```

macOS에서는 `command/management/install.command`를 더블클릭하면 npm 의존성 설치, 플러그인 빌드, 검증, 기존 개발 링크 제거, 새 경로 링크, 플러그인 재시작, Stream Deck 앱 재실행을 한 번에 실행할 수 있습니다. 플러그인 UUID와 폴더명은 `command/config/plugin.env`에서 관리합니다. Unity 패키지는 `project/unity-package/package.json`을 Unity Package Manager에서 별도로 설치해야 합니다.

배포 파일은 `command/management/dist.command`를 실행하거나 `project/streamdeck/`에서 `npm install` 후 `npm run dist`로 생성합니다. 빌드 후 패키징 과정에서 검증하고, `dist/com.tttghost.stream-deck-unity.streamDeckPlugin`을 같은 이름으로 덮어씁니다. 배포 명령은 앱 연결이나 재시작을 수행하지 않습니다.

모든 플러그인 npm 명령은 `scripts/with-plugin-env.sh`를 통해 공통 설정을 읽습니다. 로더가 `PROJECT_DIR`을 `project/streamdeck/`의 절대 경로로 설정한 뒤 `command/config/plugin.env`를 읽고, manifest의 UUID 일치 여부를 확인합니다. `PLUGIN_DIR`은 원본 리소스를 포함한 `.sdPlugin/` 실행 폴더이며, `DIST_DIR`은 배포 파일 출력 폴더입니다. 실행 폴더 전체를 삭제하지 마세요.

앱 갱신만 필요하면 `npm run refresh:app`을 실행합니다(macOS 전용). 앱 재실행 중에는 다른 플러그인도 잠시 중단됩니다. Unity 패키지는 배포 파일에 포함되지 않으므로 별도로 설치해야 합니다.

링크 후 Stream Deck 앱에서 **Unity** 카테고리의 다음 액션을 키에 배치합니다.

- 플레이 / 정지 토글: Unity의 실제 상태를 기준으로 플레이 또는 정지
- 일시정지: Unity 일시정지/재개 상태를 토글하며 아이콘도 자동 변경
- 콘솔 지우기
- 빌드: 실행과 진행·완료·실패 표시 통합
- 게임 뷰 저장
- 게임 뷰 복사
- 메뉴 실행
- 에디터 창 열기
- 에디터 메서드 호출
- 콘솔 상태: 오류와 경고 개수 표시
- 현재 에디터 상태: 활성 창과 씬 표시

매개변수가 필요한 액션은 Stream Deck 앱에서 버튼을 선택한 뒤 아래 설정을 입력합니다.

Play / Stop Toggle은 Unity가 보내는 `unity.state` 메시지를 받아 정지 상태에서는 Play 아이콘,
실행 상태에서는 Stop 아이콘을 표시합니다. 두 아이콘은 Stream Deck의 두 상태 토글로 정의되며,
Unity 툴바에서 직접 상태를 바꿔도 동기화됩니다.

`unity.state`는 `isPlaying`과 `isPaused`를 독립적으로 전달합니다. Play / Stop Toggle 아이콘은
`isPlaying`만 사용하고 Unity Pause 아이콘은 `isPaused`만 사용하므로 서로 영향을 주지 않습니다.

아이콘을 추가하거나 교체할 때는 [Unity 에디터 아이콘 적용 지침](UNITY_EDITOR_ICON_GUIDELINES.md)을 따릅니다.

Stream Deck SDK·SDPI·액션 설정 저장 규칙은 [Stream Deck 플러그인 개발 지침](STREAM_DECK_PLUGIN_GUIDELINES.md)을 따릅니다.

- Execute MenuItem: `Window/General/Console` 같은 전체 메뉴 경로
- Open EditorWindow: `UnityEditor.SceneView` 같은 전체 타입 이름
- Invoke Editor Method: 전체 타입 이름과 정적 메서드 이름
- Capture Game View: PNG 경로(비워두면 첫 실행 시 Unity 네이티브 저장 창으로 선택하며 경로를 기억)
- Choose Screenshot Path: Unity 네이티브 저장 창을 즉시 열어 캡처 경로를 변경

Unity Editor가 실행되고 이 로컬 패키지가 로드된 상태에서 버튼을 누르면 명령이 실행됩니다. 연결되지 않은 상태에서는 키에 경고 표시가 나타나며 플러그인이 2초 간격으로 재연결합니다.

### 메시지 형식

```json
{ "version": 1, "type": "unity.command", "command": "play" }
{ "version": 1, "type": "unity.command", "command": "menu.execute", "argument": "Window/General/Console" }
{ "version": 1, "type": "unity.command", "command": "window.open", "argument": "UnityEditor.SceneView" }
{ "version": 1, "type": "unity.command", "command": "method.invoke", "argument": "MyCompany.EditorCommands", "argument2": "Run" }
```

서버는 `127.0.0.1`에만 바인딩하며 최대 메시지 크기를 64 KiB로 제한합니다. 외부 네트워크에서는 접근할 수 없습니다.

## 전체 아키텍처

```text
Unity Editor-only package
  Focus detector / Command dispatcher
            ↕ localhost WebSocket (JSON)
Stream Deck plugin (@elgato/streamdeck, Node.js/TypeScript)
  Profile-page router / Key actions
             ↕ Stream Deck SDK WebSocket
Stream Deck application and device
```

Editor 코드에서는 WebSocket 전송/수명주기와 명령 이름의 Unity API 매핑을 각각 `UnityStreamDeckCommandServer.cs`와 `UnityStreamDeckCommandDispatcher.cs`로 분리해 관리합니다.

Unity와 플러그인 사이는 loopback 전용 WebSocket을 사용합니다. 연결 상태와 메시지 경계를 직접 정의해야 하는 순수 TCP보다 사용하기 쉽고, macOS/Windows에서 동일하게 동작하며, Node.js 기반 공식 Stream Deck SDK와 자연스럽게 연결됩니다. Named Pipe는 OS별 구현 차이 때문에 제외했습니다.

Stream Deck 플러그인은 사용자 정의 프로필에 접근하거나 전환할 수 없습니다. 자동 전환 대상 프로필은 플러그인에 `.streamDeckProfile`로 번들하고 `manifest.json`의 `Profiles`에 선언해야 합니다. 지원되는 Stream Deck 버전에서는 `switchToProfile` 호출에 페이지 번호를 지정하는 방식도 사용할 수 있습니다.

향후 메시지는 버전이 있는 작은 JSON envelope로 제한합니다.

```json
{ "version": 1, "type": "unity.focusChanged", "context": "SceneView" }
```

반대 방향 명령은 `unity.command` 메시지로 보내며 현재 Play/Pause/Stop을 지원합니다. 이후 Console Clear, Build, `EditorApplication.ExecuteMenuItem`, 창 열기, 등록된 사용자 명령으로 확장할 수 있습니다.

### 빌드 버튼

별도 빌드 상태 액션은 제거하고 기존 빌드 버튼(UUID 유지)에 통합했습니다.

| 현재 상태 | 버튼 클릭 |
|---|---|
| 빌드 (준비) | 3초 확인 대기 시작 |
| 빌드 시작? 3 → 2 → 1 | 3초 안에 다시 누르면 빌드 시작. 입력 없이 만료되면 준비로 복귀 |
| 빌드 중 | 중복 요청 방지 |
| 빌드 완료 / 빌드 실패 | 결과 확인 후 준비로 복귀. 빌드는 시작하지 않음 |

카운트다운은 확인 가능한 남은 시간입니다. 3초 안에 두 번째 클릭을 해야만 Unity에 빌드를 요청하며, 시간이 끝나면 아무 동작 없이 준비로 돌아갑니다. 준비로 돌아온 뒤 한 번 더 누르면 새 카운트다운이 시작됩니다. 카운트다운 중 연결 끊김·컴파일·에셋 갱신이 발생하거나 모든 빌드 키가 화면에서 사라지면 취소합니다. 여러 키 중 어느 빌드 키를 눌러도 같은 확인 대기를 승인합니다. 완료·실패 표시는 클릭 전까지 유지하며, 일반 컴파일 중에는 임시로 컴파일 중을 표시합니다. 같은 액션을 여러 키에 배치하면 상태와 결과 확인이 함께 반영됩니다. 플러그인을 재시작하면 Unity의 마지막 보고 상태로 동기화됩니다.

Unity에 연결되지 않으면 경고 표시만 하고 빌드를 시작하지 않습니다. 요청 후 15초 동안 시작 상태를 받지 못하거나 진행 중 연결이 끊기면 실패로 표시합니다. 이 경우 실제 Unity 빌드가 중단됐다는 뜻은 아니며, 재연결 시 Unity 결과로 갱신됩니다. 이미 시작이 확인된 빌드에는 시간 제한을 적용하지 않습니다.

이전에 배치한 별도 빌드 상태 키는 삭제하고 통합 빌드 키를 사용합니다. 상태 전이 테스트는 Node.js 22.6 이상에서 `node --experimental-strip-types --test tests/build-button.test.ts`로 실행합니다(`project/streamdeck/` 기준).
