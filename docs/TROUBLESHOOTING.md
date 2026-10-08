# Unity Stream Deck 트러블슈팅 기록

## Property Inspector 입력값이 사라지는 문제

### 확정된 원인

Stream Deck은 버튼 제목과 액션 settings를 별도로 저장한다. 제목에 값이 표시되어도 Property Inspector 입력값이 저장됐다는 뜻은 아니다.

이 프로젝트의 이전 구현은 수동 WebSocket, `localStorage`, 플러그인 전역 백업, 초기화 시 복원 요청을 동시에 사용했다. Property Inspector가 다시 만들어질 때 빈 응답이 기존 값을 덮어쓸 수 있었고, 액션 컨텍스트별 저장 대상이 섞일 여지도 있었다.

### 현재 해결 원칙

- 단순 입력은 공식 `sdpi-components`가 Stream Deck 액션 settings를 저장한다.
- 플러그인은 `ev.payload.settings`에서 값을 읽는다.
- 액션 실행 시 불필요한 `setSettings()`를 다시 호출하지 않는다.
- 캡처 경로는 `sdpi-textfield`와 Stream Deck 액션 settings에 표시·저장한다. Unity는 명령으로 전달받은 경로를 사용한다.
- `sdpi-components`가 제공하는 `connectElgatoStreamDeckSocket`을 커스텀 스크립트가 덮어쓰지 않는다.
- 버튼 모양은 `sdpi-button`을 사용하고, 버튼의 Unity 실행 동작만 별도 JavaScript로 연결한다.
- 네이티브 폴더 선택은 `sdpi-button` → `sendToPlugin` → Unity WebSocket 순서로 처리한다.

세부 규약은 [`PROPERTY_INSPECTOR_PERSISTENCE.md`](PROPERTY_INSPECTOR_PERSISTENCE.md)를 따른다.

### 재현·검증 절차

1. 씬 열기, 메뉴 실행, 게임 뷰 복사 중 하나에 값을 입력한다.
2. 다른 액션이나 페이지로 이동했다가 돌아온다.
3. 플러그인을 재시작한다.
4. Stream Deck 앱을 재시작한다.
5. 모든 단계에서 같은 액션의 입력값이 유지되는지 확인한다.

### 검증 명령

```bash
cd project/streamdeck
npm run build
npm run validate
npx streamdeck restart com.tttghost.stream-deck-unity
```

새 입력값 문제가 생기면 먼저 해당 HTML에 `sdpi-textfield setting="..."`이 있는지, 플러그인이 같은 settings 키를 읽는지, 커스텀 스크립트가 공식 연결 함수를 덮어쓰지 않는지 확인한다.


## 2026-09-18: 연결됐지만 Unity 버튼이 반응하지 않음

- 원인: `[InitializeOnLoad]`가 Asset Import Worker에서도 실행되어 보조 프로세스가 18765 포트를 선점했습니다. 실제 편집기는 `Address already in use`로 서버 시작에 실패했고, 플러그인은 보조 프로세스에 연결되어 상태·명령 처리가 멈췄습니다.
- 수정: 서버와 포커스 추적기는 `AssetDatabase.IsAssetImportWorkerProcess()` 및 `Application.isBatchMode`를 확인해 보조·배치 프로세스에서 초기화하지 않습니다. 서버 시작 함수에도 같은 방어를 적용했습니다.
- 서버 바인딩 성공 후에만 listener 필드를 설정합니다. 실패한 소켓은 정리하며 연결 요청이 유지되는 동안 2초마다 재시도합니다. 수동 연결 해제 시에는 재시도하지 않습니다.
- 검증: Unity 6000.2.10f1의 실제 프로젝트 컴파일 응답 파일로 C# 컴파일 성공. 서버 PID가 임포트 워커 82482에서 메인 편집기 76646으로 바뀌고 Stream Deck 플러그인에 Unity 상태 수신이 복구됐습니다.
- 진단: `lsof -nP -iTCP:18765`로 LISTEN 프로세스를 확인하고 `ps -p <PID> -o command=`에서 `-name AssetImportWorker`인지 확인합니다. 수정 적용 전부터 실행 중인 워커는 Unity의 스크립트 리로드가 완료되어야 기존 포트를 놓습니다.
- API 참고: https://docs.unity3d.com/6000.2/Documentation/ScriptReference/AssetDatabase.IsAssetImportWorkerProcess.html
