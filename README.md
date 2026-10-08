# Unity Stream Deck

Stream Deck 버튼으로 Unity Editor의 플레이 모드, 빌드, 캡처와 에디터 명령을 제어합니다. Unity 패키지와 Stream Deck 플러그인을 모두 설치해야 합니다. 두 프로그램은 이 컴퓨터의 `127.0.0.1:18765`에서 연결됩니다.

## 설치

Unity 2022.3 이상과 Stream Deck 7.1 이상이 필요합니다. Git URL로 Unity 패키지를 설치하려면 Git도 설치되어 있어야 합니다.

1. GitHub의 **Releases** 탭에서 사용할 버전(예: `v0.3.1`)을 선택합니다.
2. Unity에서 **Window > Package Manager > + > Install package from git URL**을 열고 다음 주소를 입력합니다. 설치하려는 Release의 버전과 URL 끝의 태그를 맞춥니다.

   ```text
   https://github.com/tttghost/unity-stream-deck.git?path=/project/unity-package#v0.3.1
   ```

3. 같은 Release의 `com.tttghost.stream-deck-unity.streamDeckPlugin`을 내려받아 더블클릭하고 Stream Deck에서 설치합니다.
4. Unity Editor를 연 상태에서 Stream Deck의 **스트림덱 유니티** 액션을 키에 배치합니다. Unity의 **Window > General > Unity Stream Deck**에서 연결 상태를 확인할 수 있습니다.

업데이트할 때는 새 버전 태그가 들어간 Git URL을 Unity Package Manager에 입력하고, 같은 버전 Release의 Stream Deck 설치 파일을 다시 설치합니다. GitHub의 소스 ZIP은 두 프로젝트의 원본 코드이며 Stream Deck 설치 파일은 Release의 별도 첨부 파일입니다.

## 개발 및 배포

| 위치 | 내용 |
| --- | --- |
| `project/unity-package/` | Unity Editor 전용 UPM 패키지 |
| `project/streamdeck/src/` | Stream Deck TypeScript 소스 |
| `project/streamdeck/com.tttghost.stream-deck-unity.sdPlugin/` | 플러그인 매니페스트, 이미지, 설정 화면 |
| `command/` | 로컬 설치·배포 명령 |
| `docs/` | 상세 사용법과 개발 문서 |

태그 `vX.Y.Z`를 푸시하면 GitHub Actions가 Unity `package.json`, Stream Deck `package.json`, 플러그인 `manifest.json`의 버전을 검사하고, 플러그인을 빌드·검증한 뒤 `.streamDeckPlugin`을 해당 GitHub Release에 첨부합니다. 배포 파일과 `node_modules`는 Git에 올리지 않습니다. 배포 절차는 [Release 안내](docs/RELEASING.md)를 참고하세요.

자세한 기능, 로컬 개발 설치법과 문제 해결은 [개발 문서](docs/README.md)를 참고하세요.

## 라이선스

이 프로젝트는 [MIT 라이선스](LICENSE)로 배포합니다. Stream Deck 설치 파일에 포함된 외부 라이브러리의 라이선스는 [제3자 고지](project/streamdeck/com.tttghost.stream-deck-unity.sdPlugin/THIRD_PARTY_NOTICES.md)에 정리했습니다.
