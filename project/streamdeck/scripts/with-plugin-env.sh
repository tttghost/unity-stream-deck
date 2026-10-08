#!/bin/sh
set -eu

PROJECT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
export PROJECT_DIR
set -a
. "$PROJECT_DIR/../../command/config/plugin.env"
set +a

: "${PLUGIN_UUID:?PLUGIN_UUID is required}"
: "${PLUGIN_FOLDER:?PLUGIN_FOLDER is required}"
: "${PLUGIN_DIR:?PLUGIN_DIR is required}"
: "${DIST_DIR:?DIST_DIR is required}"
if [ ! -f "$PLUGIN_DIR/manifest.json" ]; then
  echo "Plugin manifest not found: $PLUGIN_DIR/manifest.json" >&2
  exit 1
fi

node --input-type=module -e '
import { readFileSync } from "node:fs";
import { join } from "node:path";
const manifest = JSON.parse(readFileSync(join(process.env.PLUGIN_DIR, "manifest.json"), "utf8"));
if (manifest.UUID !== process.env.PLUGIN_UUID) {
  throw new Error("manifest.json UUID must match plugin.env PLUGIN_UUID");
}
'

cd "$PROJECT_DIR"
exec "$@"
