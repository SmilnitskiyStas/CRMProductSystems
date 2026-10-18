import type { PrintCodepage } from '@shared/types';

/**
 * Text -> printer bytes for the supported codepages. Characters a codepage cannot represent are
 * transliterated to the closest glyph (never dropped silently into garbage): e.g. plain CP866 has
 * no і/ґ, so they print as i/г. Use CP1125 or CP1251 for full Ukrainian.
 */

/** Default ESC t n page numbers (Epson-compatible); printers differ, so the value is configurable. */
export const DEFAULT_CODEPAGE_ID: Record<PrintCodepage, number> = {
  cp866: 17,
  cp1125: 17, // many Ukrainian-market printers expose CP1125 in the CP866 slot
  cp1251: 46,
  utf8: 255,
};

// Ukrainian/Russian capital letters А..Я in Unicode order (U+0410..U+042F)
const BASIC_UPPER = 0x0410;

function cp866Byte(ch: number): number | undefined {
  if (ch >= 0x0410 && ch <= 0x043f) return 0x80 + (ch - 0x0410); // А-п
  if (ch >= 0x0440 && ch <= 0x044f) return 0xe0 + (ch - 0x0440); // р-я
  switch (ch) {
    case 0x0401: return 0xf0; // Ё
    case 0x0451: return 0xf1; // ё
    case 0x0404: return 0xf2; // Є
    case 0x0454: return 0xf3; // є
    case 0x0407: return 0xf4; // Ї
    case 0x0457: return 0xf5; // ї
  }
  return undefined;
}

function cp1125Byte(ch: number): number | undefined {
  switch (ch) {
    case 0x0490: return 0xf2; // Ґ
    case 0x0491: return 0xf3; // ґ
    case 0x0404: return 0xf4; // Є
    case 0x0454: return 0xf5; // є
    case 0x0406: return 0xf6; // І
    case 0x0456: return 0xf7; // і
    case 0x0407: return 0xf8; // Ї
    case 0x0457: return 0xf9; // ї
  }
  if (ch >= 0x0410 && ch <= 0x043f) return 0x80 + (ch - 0x0410);
  if (ch >= 0x0440 && ch <= 0x044f) return 0xe0 + (ch - 0x0440);
  if (ch === 0x0401) return 0xf0;
  if (ch === 0x0451) return 0xf1;
  return undefined;
}

function cp1251Byte(ch: number): number | undefined {
  if (ch >= 0x0410 && ch <= 0x044f) return 0xc0 + (ch - BASIC_UPPER);
  switch (ch) {
    case 0x0401: return 0xa8; // Ё
    case 0x0451: return 0xb8; // ё
    case 0x0404: return 0xaa; // Є
    case 0x0454: return 0xba; // є
    case 0x0406: return 0xb2; // І
    case 0x0456: return 0xb3; // і
    case 0x0407: return 0xaf; // Ї
    case 0x0457: return 0xbf; // ї
    case 0x0490: return 0xa5; // Ґ
    case 0x0491: return 0xb4; // ґ
    case 0x2116: return 0xb9; // №
  }
  return undefined;
}

/** Fallbacks for glyphs missing in a codepage / non-ASCII punctuation. */
const TRANSLIT: Record<number, string> = {
  0x0490: 'Г', 0x0491: 'г', 0x0406: 'I', 0x0456: 'i', 0x2116: 'N', 0x20b4: 'грн', 0x2019: "'", 0x02bc: "'",
  0x2018: "'", 0x201c: '"', 0x201d: '"', 0x00ab: '"', 0x00bb: '"', 0x2013: '-', 0x2014: '-', 0x00d7: 'x',
  0x2026: '...', 0x00a0: ' ', 0x2212: '-',
};

function singleByte(ch: number, cp: Exclude<PrintCodepage, 'utf8'>): number | undefined {
  if (ch < 0x80) return ch;
  return (cp === 'cp866' ? cp866Byte(ch) : cp === 'cp1125' ? cp1125Byte(ch) : cp1251Byte(ch));
}

export function encodeText(text: string, codepage: PrintCodepage): Uint8Array {
  if (codepage === 'utf8') return new TextEncoder().encode(text);
  const out: number[] = [];
  const push = (s: string): void => {
    for (const c of s) {
      const code = c.codePointAt(0)!;
      const b = singleByte(code, codepage as Exclude<PrintCodepage, 'utf8'>);
      if (b !== undefined) out.push(b);
      else if (TRANSLIT[code] !== undefined) {
        // transliteration may itself contain non-ASCII (e.g. 'грн'); encode it once more without recursion into TRANSLIT
        for (const t of TRANSLIT[code]) {
          const tb = singleByte(t.codePointAt(0)!, codepage as Exclude<PrintCodepage, 'utf8'>);
          out.push(tb ?? 0x3f);
        }
      } else out.push(0x3f); // '?'
    }
  };
  push(text);
  return Uint8Array.from(out);
}
