#!/bin/zsh -l
set -eu

# Resolve from this file so double-clicking works from any current directory.
command_dir="${0:A:h}"
project_dir="$(cd -- "$command_dir/../../project/streamdeck" && pwd)"
export PATH="$PATH:/opt/homebrew/bin:/usr/local/bin"

cd "$project_dir"

if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
  print -u2 "Node.js 20 이상과 npm이 필요합니다. 설치 후 다시 실행해 주세요."
  exit 1
fi

node_major="$(node -p 'process.versions.node.split(".")[0]')"
if (( node_major < 20 )); then
  print -u2 "Node.js 20 이상이 필요합니다. 현재 버전: $(node --version)"
  exit 1
fi

npm install
npm run install:plugin
npm run refresh:app

print "Unity Stream Deck 플러그인 설치·링크·앱 재실행 완료"
