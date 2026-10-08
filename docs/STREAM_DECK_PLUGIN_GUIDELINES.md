# Stream Deck 플러그인 개발 지침

이 문서는 특정 외부 애플리케이션이나 엔진에 종속되지 않는 Stream Deck 플러그인 개발 규칙이다. Stream Deck SDK, manifest, 액션, Property Inspector(SDPI)의 경계를 지키고 설정값 휘발·UI 재생성·통신 혼선을 예방하는 것을 목표로 한다.

## 1. 구조와 책임

```text
Stream Deck 앱
  ├─ Property Inspector(SDPI): 설정 UI
  └─ Plugin application layer: 액션 실행·상태·영속 설정
          └─ 외부 앱 브리지(필요한 경우에만)
```

- `manifest.json`은 액션의 공개 정보와 파일 경로를 정의한다.
- Plugin application layer는 `@elgato/streamdeck`를 사용해 액션 이벤트, 설정, 상태, 이미지, 알림을 처리한다.
- SDPI는 HTML UI이며, 액션 설정을 표시·수정한다.
- 외부 앱 통신은 Plugin application layer에 둔다. SDPI가 외부 앱에 직접 연결하지 않는다.
- Stream Deck와 SDPI 사이의 WebSocket은 SDK가 관리한다. 플러그인이 이 연결을 직접 대체하거나 별도 포트를 열지 않는다.
- 외부 앱 브리지용 WebSocket/HTTP가 필요하더라도 Stream Deck SDK 통신과 별개의 계층으로 분리한다.

## 2. manifest 규칙

- 플러그인 UUID와 액션 UUID는 reverse-DNS 형식의 소문자·숫자·하이픈·마침표만 사용한다.
- 모든 액션 UUID는 플러그인 UUID를 접두사로 사용하고 서로 중복되지 않아야 한다.
- `@action({ UUID })`와 `manifest.json`의 액션 UUID는 반드시 일치시킨다.
- `CodePath`와 `PropertyInspectorPath`는 실제 확장자를 포함한다.
- `Icon`, `States[].Image` 같은 이미지 경로는 확장자를 생략한다.
- Property Inspector 파일은 플러그인 루트 기준 상대 경로이며 선행 `/`를 사용하지 않는다.
- 액션을 추가·삭제·이름 변경할 때 manifest, TypeScript 등록, Property Inspector 경로를 함께 점검한다.
- 파일 경로의 대소문자는 실제 파일명과 일치시킨다. macOS에서 우연히 통과한 경로가 다른 환경에서 실패할 수 있다.

## 3. 액션 구현 규칙

- 일반 액션은 `SingletonAction`과 `@action`으로 구현한다.
- `onKeyDown`에는 짧은 명령 전달과 실패 표시만 두고, 긴 작업은 별도 함수나 서비스로 분리한다.
- 액션 인스턴스마다 달라지는 값은 전역 변수나 모듈 전역 Map에 저장하지 않는다.
- `onWillAppear`에서 현재 상태와 아이콘을 초기화하고, `onWillDisappear`에서 해당 액션의 listener·Map 항목을 정리한다.
- 상태를 표시하는 액션은 연결 상태, 현재 상태, 오류 상태를 구분한다. 연결이 끊겼는데 정상 상태처럼 표시하지 않는다.
- `setImage`, `setTitle`, `setState` 호출은 필요한 값이 바뀔 때만 수행해 깜빡임을 줄인다.
- 액션의 명령 문자열은 한 곳에서 타입 또는 상수로 관리하며 manifest 이름과 실행 명령을 혼용하지 않는다.
- 사용자에게 보여주는 이름·Tooltip·알림은 명확한 자연어로 작성한다.

## 4. 설정값 영속성 규칙

액션 설정의 유일한 원본은 Stream Deck의 action settings다.

```text
SDPI 입력
  → streamDeckClient.setSettings(...) 또는 setting 속성
  → Stream Deck 저장
  → Plugin의 onDidReceiveSettings / ev.payload.settings
```

- 입력값을 `localStorage`, 전역 변수, HTML 변수, 외부 앱 설정 파일에 별도로 저장하지 않는다.
- 버튼을 눌렀을 때만 저장되는 설정과 입력 즉시 저장되는 설정을 의도적으로 구분한다.
- 액션 실행 시 현재 이벤트의 `ev.payload.settings`를 사용한다. 오래된 전역 복사본을 우선하지 않는다.
- Plugin에서 설정을 보정할 때는 기존 값을 먼저 읽고 필요한 필드만 병합한다. 빈 객체로 전체 설정을 덮어쓰지 않는다.
- 설정 키 이름은 SDPI의 `setting="..."`, TypeScript Settings 타입, 명령 변환 코드에서 동일하게 유지한다.
- 다른 액션의 설정을 읽거나 전역 설정으로 대체하지 않는다. 액션 설정은 액션 인스턴스별 값이다.
- Property Inspector가 다시 열리거나 페이지를 이동해도 값이 남는지 확인한다.
- 숫자·불리언·문자열 타입을 임의로 바꾸지 않는다. 입력값 검증과 기본값은 Plugin에서 최종적으로 다시 확인한다.

## 5. SDPI 작성 규칙

- 반복되는 입력 UI는 `sdpi-item`, `sdpi-textfield`, `sdpi-textarea`, `sdpi-select`, `sdpi-checkbox`, `sdpi-button` 등 sdpi-components를 우선 사용한다.
- 일반 설정 입력은 `setting` 속성으로 action settings에 직접 연결한다.
- 명령 실행 버튼은 입력 필드와 분리한다. 한 컴포넌트의 값 영역과 버튼 영역에 서로 다른 동작을 겹쳐 놓지 않는다.
- 폴더·파일 선택처럼 기본 SDPI 컴포넌트만으로 처리할 수 없는 동작은 버튼 클릭 후 `sendToPlugin`으로 알린다.
- `sendToPlugin` payload에는 이벤트 이름과 필요한 최소 데이터만 넣는다. 설정값 자체는 `setSettings`가 저장하도록 한다.
- SDPI에서 `@elgato/streamdeck`를 import하지 않는다. 브라우저에서는 `SDPIComponents.streamDeckClient`를 사용한다.
- 외부 앱용 WebSocket을 SDPI에서 직접 열지 않는다. 필요한 응답은 Plugin이 받아 `sendToPropertyInspector`로 전달한다.
- 원격 CDN은 빠른 개발용으로만 사용한다. 배포 시 `sdpi-components.js`를 플러그인에 포함하고 로컬 경로를 사용한다.
- HTML에 캐시 버스터를 붙이기 전에 실제 저장 로직과 lifecycle 문제를 먼저 확인한다. 캐시 버스터는 영속성 해결책이 아니다.

## 6. SDK 이벤트와 통신

- Plugin은 시작 시 액션을 등록한 뒤 `streamDeck.connect()`를 호출한다.
- SDK 이벤트와 외부 브리지 이벤트를 같은 이름 체계로 사용하지 않는다. 예를 들어 SDK의 `sendToPlugin`과 외부 명령 `capture`는 별개의 이벤트다.
- 연결·재연결·종료 시 listener를 중복 등록하지 않는다.
- 응답 메시지는 타입, 요청 식별자, 성공 여부, 오류 정보를 일관된 형식으로 유지한다.
- 연결 실패는 조용히 무시하지 말고 로그와 사용자 피드백으로 구분한다.
- 이벤트 수신 순서를 가정하지 않는다. 액션이 사라진 뒤 도착한 비동기 응답은 무시할 수 있어야 한다.
- 비동기 작업 완료 후에는 해당 액션이 아직 표시 중인지 확인하고 UI를 갱신한다.

## 7. 개발·검증 절차

변경 후에는 Plugin 디렉터리에서 실행한다.

```bash
npm run build
npm run validate
npx streamdeck restart <plugin-uuid>
```

확인 순서:

1. TypeScript 빌드가 성공하는가?
2. manifest 검증이 성공하는가?
3. 액션 목록에서 이름·아이콘·Property Inspector가 정상인가?
4. 액션을 추가하고 설정값을 입력한 뒤 다른 액션·페이지로 이동해도 유지되는가?
5. Stream Deck 앱을 재시작해도 설정값이 유지되는가?
6. 액션을 실제로 눌렀을 때 현재 설정값으로 실행되는가?
7. 연결 끊김·재연결·액션 삭제 후 늦게 도착한 응답에서 오류가 없는가?

## 8. 금지 패턴

- SDPI 입력값을 `localStorage`에만 저장
- Plugin 전역 변수에 액션별 설정을 저장
- Property Inspector가 외부 애플리케이션에 직접 WebSocket 연결
- SDK가 관리하는 Stream Deck WebSocket을 직접 재구현
- `onWillAppear`마다 listener를 등록하고 `onWillDisappear`에서 제거하지 않음
- 설정을 갱신할 때 기존 설정을 보존하지 않고 `{}` 또는 일부 필드만 전체 객체로 저장
- manifest 경로에만 존재하는 파일을 가정
- `npm run validate` 성공만으로 UI가 정상이라고 판단
- 설정 휘발 문제를 HTML 새로고침이나 캐시 버스터로 해결하려고 시도

## 9. 변경 체크리스트

- [ ] manifest 액션 UUID와 `@action` UUID가 일치한다.
- [ ] Property Inspector의 `setting` 키와 Settings 타입이 일치한다.
- [ ] 설정은 Stream Deck action settings에 저장된다.
- [ ] 전역 변수·localStorage에 액션 설정을 중복 저장하지 않는다.
- [ ] SDPI의 명령 버튼은 `sendToPlugin`만 사용한다.
- [ ] 외부 앱 통신은 Plugin 계층에만 있다.
- [ ] lifecycle listener가 중복 등록되지 않는다.
- [ ] `npm run build`와 `npm run validate`가 성공한다.
- [ ] 액션 추가·페이지 이동·앱 재시작 후 설정값을 직접 확인했다.

## 공식 문서

- Getting Started: https://docs.elgato.com/streamdeck/sdk/introduction/getting-started/
- Property Inspectors: https://docs.elgato.com/streamdeck/sdk/guides/ui/
- Settings: https://docs.elgato.com/streamdeck/sdk/guides/settings/
- Manifest reference: https://docs.elgato.com/streamdeck/sdk/references/manifest/
- Property Inspector WebSocket reference: https://docs.elgato.com/streamdeck/sdk/references/websocket/ui/
