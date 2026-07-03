// Shared slug generation with Ukrainian → Latin transliteration.
// Company name stays as typed (Cyrillic OK) — only the slug is transliterated.

const UA_TO_LAT: Record<string, string> = {
  а: "a", б: "b", в: "v", г: "h", ґ: "g", д: "d", е: "e", є: "ye",
  ж: "zh", з: "z", и: "y", і: "i", ї: "yi", й: "i", к: "k", л: "l",
  м: "m", н: "n", о: "o", п: "p", р: "r", с: "s", т: "t", у: "u",
  ф: "f", х: "kh", ц: "ts", ч: "ch", ш: "sh", щ: "shch", ь: "",
  ю: "yu", я: "ya",
  // ru extras occasionally typed by users
  ы: "y", э: "e", ё: "e", ъ: "",
};

export function slugify(name: string): string {
  return name
    .toLowerCase()
    .trim()
    .split("")
    .map((ch) => UA_TO_LAT[ch] ?? ch)
    .join("")
    .replace(/[\s_]+/g, "-")
    .replace(/[^a-z0-9-]/g, "")
    .replace(/-+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 32);
}
