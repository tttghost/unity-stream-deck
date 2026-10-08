# Release 절차

이 저장소는 하나의 태그로 Unity 패키지와 Stream Deck 플러그인의 버전을 맞춥니다. 태그 `vX.Y.Z`가 가리키는 `project/unity-package/`가 Unity 설치 원본이고, GitHub Release에 첨부되는 `.streamDeckPlugin`이 Stream Deck 설치 파일입니다.

## 첫 공개 전

- GitHub에 `tttghost/unity-stream-deck` 공개 저장소를 만들고 README의 설치 URL이 실제 저장소를 가리키는지 확인합니다.
- `project/streamdeck/com.tttghost.stream-deck-unity.sdPlugin/imgs/`의 이미지 사용·재배포 권한을 배포 전에 확인합니다.
- 루트, Unity 패키지와 Stream Deck 설치 파일에 MIT 라이선스 고지가 포함되는지 확인합니다. 필요한 제3자 고지도 함께 확인합니다.
- Windows와 macOS의 새 설치 환경에서 두 설치 경로와 연결을 확인합니다.

## 버전 배포

1. Unity `project/unity-package/package.json`과 Stream Deck `project/streamdeck/package.json`의 버전을 `X.Y.Z`로, 플러그인 `manifest.json`의 `Version`을 `X.Y.Z.0`으로 맞춥니다.
2. `node project/streamdeck/scripts/check-release-version.mjs vX.Y.Z`로 버전을 확인합니다.
3. `project/streamdeck/`에서 `npm ci`, `node --experimental-strip-types --test tests/build-button.test.ts`, `npm run dist`를 실행합니다.
4. 변경 사항을 커밋하고 `vX.Y.Z` 태그를 만든 뒤 커밋과 태그를 원격 저장소에 푸시합니다.
5. GitHub Actions의 **Release** 작업이 끝나면 같은 태그의 Release에 `com.tttghost.stream-deck-unity.streamDeckPlugin`이 첨부됐는지 확인합니다. 소스 ZIP과 tar.gz는 GitHub가 자동 제공합니다.
6. Release 페이지의 설치 파일과 태그 URL로 Unity와 Stream Deck을 새로 설치해 연결을 확인합니다.

태그가 푸시되기 전에는 Release가 생성되지 않습니다. CI가 실패하면 원인을 수정해 새 커밋과 새 태그로 다시 배포합니다. 이미 공개한 태그가 다른 코드를 가리키도록 이동하지 않습니다.
