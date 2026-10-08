/** Keep live titles within the reserved key label area. */
export function compactKeyTitle(value: string): string {
  const characters = Array.from(value.replace(/\s+/gu, " ").trim());
  const width = (character: string) => /[^\x00-\x7F]/u.test(character) ? 2 : 1;
  if (characters.reduce((total, character) => total + width(character), 0) <= 10) {
    return characters.join("");
  }
  let result = "";
  let used = 0;
  for (const character of characters) {
    if (used + width(character) > 8) break;
    result += character;
    used += width(character);
  }
  return result.trimEnd() + "…";
}

export function compactCount(value: number): string {
  return value > 999 ? "999+" : String(value);
}
