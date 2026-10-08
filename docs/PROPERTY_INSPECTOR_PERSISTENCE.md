# Property Inspector 설정 저장 규약

이 프로젝트의 Property Inspector는 Stream Deck SDK의 액션 설정 저장 방식을 단일 기준으로 사용한다.

## 기본 원칙

1. 단순 입력은 공식 `sdpi-components`의 `setting="..."` 속성을 사용한다.
2. Property Inspector에서 `localStorage`, 플러그인 전역 설정, 별도 백업 맵을 만들지 않는다.
3. 플러그인은 키 입력 시 `ev.payload.settings`를 읽는다.
4. 플러그인이 설정을 변경해야 할 때만 `ev.action.setSettings()`를 호출한다.
5. Unity 전용 상태만 Unity `EditorPrefs`가 소유한다. 캡처 저장 경로는 액션 settings가 소유한다.
6. Stream Deck 액션 설정과 Unity 설정을 서로의 백업 저장소로 복제하지 않는다.
7. UI는 공식 컴포넌트를 우선 사용하고, 컴포넌트의 클릭 결과로 Unity 기능을 실행해야 할 때만 별도 WebSocket을 추가한다.

## 저장 책임

| 값 | 소유자 | 저장 방식 |
| --- | --- | --- |
| 씬 이름, 메뉴 경로, 해상도 | Stream Deck 액션 | `sdpi-components` → 액션 settings |
| 캡처 저장 폴더 | Stream Deck 액션 | `sdpi-textfield` + `sdpi-button` → `sendToPlugin` → `action.setSettings()` |
| 현재 Unity 연결·플레이 상태 | Unity 패키지/플러그인 | WebSocket 상태 이벤트 |

액션 settings가 유일한 원본이다. 버튼 제목(`States[].Title`)은 표시용 텍스트이며 입력값 저장 여부를 의미하지 않는다.

## 금지 패턴

```js
settings = parsed.payload.settings || {};
saveSettings();
```

Property Inspector가 재생성될 때 빈 payload로 기존 설정을 덮어쓸 수 있다. 공식 컴포넌트가 관리하는 설정 연결 함수를 덮어쓰지 않는다.

```js
window.connectElgatoStreamDeckSocket = function () { ... };
```

커스텀 동작이 필요하더라도 `sdpi-components`가 제공하는 연결 함수를 교체하지 말고, `streamDeckClient.send("sendToPlugin", ...)`로 플러그인에 요청한다.

## 추가 액션 점검표

- HTML 입력 요소에 `setting` 이름이 있는가?
- `manifest.json`의 Property Inspector 경로가 실제 파일과 일치하는가?
- 플러그인이 `ev.payload.settings`의 동일한 키를 읽는가?
- 초기화 시 빈 값으로 설정을 쓰지 않는가?
- 액션 실행 시 필요하지 않은 `setSettings()`를 호출하지 않는가?
- Unity 소유 상태와 Stream Deck 액션 settings를 혼합하지 않는가?

## 검증

```bash
cd project/streamdeck
npm run build
npm run validate
npx streamdeck restart com.tttghost.stream-deck-unity
```
