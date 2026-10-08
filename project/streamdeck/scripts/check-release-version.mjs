import { readFileSync } from "node:fs";

const readJson = (relativePath) =>
  JSON.parse(readFileSync(new URL(relativePath, import.meta.url), "utf8"));

const tag = process.argv[2];
if (!/^v\d+\.\d+\.\d+$/.test(tag ?? "")) {
  throw new Error("Release tag must have the form vX.Y.Z.");
}

const expected = tag.slice(1);
const unity = readJson("../../unity-package/package.json");
const streamdeck = readJson("../package.json");
const manifest = readJson("../com.tttghost.stream-deck-unity.sdPlugin/manifest.json");

for (const [name, actual] of [
  ["Unity package", unity.version],
  ["Stream Deck package", streamdeck.version],
  ["Stream Deck manifest", manifest.Version],
]) {
  const wanted = name === "Stream Deck manifest" ? `${expected}.0` : expected;
  if (actual !== wanted) {
    throw new Error(`${name} version ${actual} does not match ${tag} (expected ${wanted}).`);
  }
}

process.stdout.write(`Release versions match ${tag}.\n`);
