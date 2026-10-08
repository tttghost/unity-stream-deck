# Unity 에디터 아이콘 적용 지침

이 문서는 Stream Deck 플러그인에 Unity 에디터 아이콘을 추가하거나 교체할 때 반드시 지켜야 하는 기준이다.

## 기본 원칙

- 아이콘은 임의로 그리거나 기존 SVG를 비슷하게 수정하지 않는다.
- Unity 공식 Editor Icon Library 또는 대상 Unity 에디터가 실제 사용하는 내장 아이콘을 사용한다.
- 아이콘은 대상 Unity 버전을 명시한다. 현재 기준 버전은 Unity 6 (`6000.2.10f1`)이다.
- Unity 버전이 바뀌면 내장 아이콘 모양·이름·색상·크기가 바뀔 수 있으므로 다시 확인한다.
- 액션의 실행 로직과 아이콘 리소스를 섞지 않는다. 실행은 TypeScript/Unity 패키지가 담당하고, 아이콘은 Stream Deck 플러그인 폴더의 정적 리소스가 담당한다.

## Unity 내장 아이콘 추출 시 주의점

`EditorGUIUtility.IconContent("d_PlayButton")`로 이름을 확인할 수는 있지만, Unity를 `-batchmode -nographics`로 실행할 때 반환되는 텍스처가 실제 아이콘이 아닌 회색 플레이스홀더가 될 수 있다.

따라서 추출기는 다음 순서를 지킨다.

1. Unity Editor 내부 에셋 번들을 가져온다.
2. 아이콘 에셋의 실제 경로를 에셋 번들 목록에서 찾는다. 경로와 이름의 대소문자 및 `icons/processed` 하위 경로를 고려한다.
3. 텍스처를 `Graphics.CopyTexture`로 읽을 수 있는 `Texture2D`에 복사한다.
4. PNG로 저장한다.
5. 결과 PNG를 이미지 뷰어로 확인한다. 단색 회색 사각형이면 추출 실패로 처리한다.

`Graphics.Blit` 또는 배치 모드의 `IconContent` 결과를 그대로 저장하는 방식은 사용하지 않는다.

## 플러그인 리소스 규칙

추출된 아이콘은 다음 위치에 둔다.

```text
project/streamdeck/com.tttghost.stream-deck-unity.sdPlugin/imgs/unity-editor/
```

파일 규칙:

- 기본 이미지: `name.png`
- 고해상도 이미지: `name@2x.png`
- PNG는 투명 배경을 유지한다.
- `manifest.json`의 `Icon`과 `States[].Image`는 확장자를 생략한다. 예: `imgs/unity-editor/play`
- TypeScript의 `action.setImage()` 경로는 실제 파일명(`.png`)을 사용해도 되지만, manifest와 동일한 리소스를 가리켜야 한다.
- Stream Deck 검증에서 고해상도 경고가 발생하지 않도록 `@2x` 파일도 함께 제공한다.

## 현재 아이콘 매핑

| 액션 | Unity 아이콘 | 파일 |
|---|---|---|
| 플레이 | `d_PlayButton` | `play.png` |
| 일시정지 | `d_PauseButton` | `pause.png` |
| 재개 | `d_PlayButton` | `play.png` |
| 정지 | `d_StopButton` | `stop.png` |
| 게임 뷰 저장 | 사진 카메라 SVG | `imgs/ui/camera.svg` |
| 게임 뷰 복사 | 겹친 사진 SVG | `imgs/ui/clipboard.svg` |
| 콘솔 지우기 | `d_clear@2x` | `clear.png` |
| 빌드 | `d_BuildProfile Icon` | `build.png` |
| 콘솔 상태 | `d_UnityEditor.ConsoleWindow` | `console.png` |
| 에디터 창 열기 | `d_UIBuilder` | `window.png` |
| 씬 열기 | `d_SceneAsset Icon` | `scene.png` |
| 메뉴 실행 | `d__Menu` | `menu.png` |

Unity에 해당 기능의 정확한 단일 아이콘이 없는 경우에만 가장 가까운 공식 아이콘을 사용하고, 임의의 의미를 가진 아이콘을 새로 만들지 않는다. 구분선은 기능 아이콘이 아니므로 프로젝트 전용 `separator.svg`를 사용할 수 있다. 빌드 중·성공·실패처럼 상태를 명확히 전달해야 하는 이미지는 현재 상태 GIF를 유지할 수 있다.

## 변경 후 검증 절차

플러그인 폴더에서 다음 명령을 순서대로 실행한다.

```bash
npm run build
npm run validate
npx streamdeck restart com.tttghost.stream-deck-unity
```

검증 항목:

- 액션 목록에서 회색 사각형·빈 아이콘·깨진 이미지가 없는가?
- 플레이·일시정지·정지 토글의 상태 변경 아이콘이 정상인가?
- `manifest.json`의 모든 이미지 경로가 실제 파일과 일치하는가?
- `npm run validate`가 성공하는가?
- `@2x` 이미지가 존재하고 크기가 기본 이미지의 2배인가?
- Stream Deck 캐시 때문에 이전 아이콘이 보이면 앱을 완전히 재시작한 뒤 다시 확인했는가?

## 재발 방지 체크

- 회색 사각형이 보이면 먼저 Stream Deck 캐시가 아니라 PNG 픽셀 자체를 확인한다.
- PNG가 단색이면 아이콘 연결 작업을 중단하고 추출 방식을 점검한다.
- 새 아이콘을 추가할 때 `manifest.json`, 동적 `setImage()` 코드, 실제 PNG, `@2x` PNG를 한 번에 확인한다.
- Unity 버전이 다른 아이콘을 섞어 사용하지 않는다.
- 검증 통과만으로 이미지가 정상이라는 뜻은 아니므로 실제 Stream Deck 액션 목록에서 시각 확인까지 완료한다.

## 참고

- Unity Iconography: https://www.foundations.unity.com/fundamentals/iconography
- Unity Editor Icon Library: https://www.figma.com/@unity3d
- Stream Deck manifest 이미지 경로는 확장자를 생략하는 형식을 사용한다.

## 액션 목록 정리 (2026-09-09)

구분선은 플레이 모드, 캡처, 에디터 도구, 사용자 명령, 상태 표시의 다섯 그룹으로 유지합니다.
메서드 호출은 `d_cs Script Icon` (`method.png`)을 사용합니다. 터미널 명령 액션은 제거했습니다.
빌드와 창 아이콘은 각각 도구와 UI 창 모양을 사용합니다. 기존 InspectorWindow 리소스는 정보 기호여서 창 구별에 부적합했습니다.
교체 아이콘은 Unity 6000.2.10f1의 `unity editor resources`에서 UnityPy로 원본 Texture2D 픽셀을 직접 디코딩해 추출했습니다. GPU 렌더링을 거치지 않으며, PNG 픽셀과 실제 이미지 미리보기를 확인했습니다. 제공되는 가장 큰 원본(@2x 포함)을 선택해 기본 72px, 고해상도 144px로 저장합니다. 복사 아이콘은 16px Duplicate 대신 256px TextAsset 문서 아이콘을 사용합니다. 콘솔 지우기는 16px Trash 대신 32px clear 아이콘을 사용합니다.

## 키 제목과 아이콘 배치 (2026-09-10)

- 모든 States에 Title, ShowTitle, TitleAlignment, TitleColor, FontSize, FontStyle을 명시합니다. 일반 키는 하단·흰색·11·Bold, 두 줄 에디터 상태는 9·Bold입니다. 구분선은 빈 Title과 ShowTitle=false를 유지합니다.
- 목록 아이콘은 `imgs/unity-editor/`, 키 이미지는 `imgs/keys/`를 사용합니다. 72px 키에서 일반 아이콘은 28px 크기로 y=22…50에, 두 줄 상태 아이콘은 18px 크기로 y=27…45에 배치합니다. 투명 여백을 제거한 실제 도형을 기준으로 모두 (36, 36)에 중앙 정렬합니다. 하단은 네이티브 제목 영역입니다. 아이콘 원본의 가로세로 비율은 유지합니다.
- 키 이미지는 72px와 144px(@2x)를 제공합니다. 동적 setImage도 144px 키 전용 리소스를 사용합니다. 빌드 GIF 3종은 모든 프레임을 같은 중앙 영역에 배치하고 프레임 수·시간·반복 설정을 유지합니다.
- 플레이/정지 및 일시정지/재개는 Unity 상태로 표시를 결정하므로 DisableAutomaticStates=true입니다. 이미지 갱신 대상 state를 명시해 반대 상태의 이미지를 덮어쓰지 않습니다.
- 긴 씬 이름은 표시 폭에 맞게 말줄임하고 줄바꿈을 제거합니다. 콘솔 오류·경고 수는 999를 넘으면 999+로 표시합니다.
- `project/streamdeck/scripts/generate-key-images.py`로 재생성합니다. Python 환경에 UnityPy와 Pillow가 필요하며 일반 npm 빌드에는 필요하지 않습니다. 사용 원본과 크기는 `key-image-sources.json`에 기록됩니다. 작은 원본의 단순 확대를 고해상도 원본으로 간주하지 않습니다. Unity가 최대 32px만 제공하는 아이콘은 키의 실제 표시 크기를 32px 이하로 제한합니다.
- [키 배치 미리보기](KEY_APPEARANCE_PREVIEW.png)는 기본 제목과 동적 상태 예시를 합성한 검토용 이미지이며 실제 Stream Deck 화면 캡처가 아닙니다. 사용자가 기존 키에서 직접 지정한 제목·글꼴·이미지는 앱에 저장된 설정이 우선할 수 있습니다.

제목 필드 기준: https://docs.elgato.com/streamdeck/sdk/references/manifest/

## 저장·복사 아이콘 예외

사용자 요청에 따라 게임 뷰 저장·복사는 공식 Unity 비디오카메라·문서 아이콘 대신 프로젝트의 SVG 아이콘 체계를 사용합니다. 저장은 사진 카메라, 복사는 겹친 사진으로 구분합니다. `imgs/ui/` 원본에서 `imgs/keys/` 중앙 정렬 이미지를 재생성하며, 벡터이므로 별도 @2x 이미지가 필요하지 않습니다. 버튼 제목과 액션 목록 이름은 각각 게임 뷰 저장, 게임 뷰 복사로 통일합니다.
