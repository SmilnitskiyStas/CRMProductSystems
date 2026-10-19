import {
  BarcodeFormat,
  BinaryBitmap,
  DecodeHintType,
  HybridBinarizer,
  MultiFormatReader,
  RGBLuminanceSource,
} from '@zxing/library';

/**
 * JS barcode decoder (Chromium in Electron has no BarcodeDetector). Restricted to retail formats
 * for speed and fewer false positives. Pure over pixel data, so the same code path is used by the
 * live camera loop and by the PNG-based unit tests.
 */
const FORMATS = [BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.CODE_128];

function makeReader(tryHarder: boolean): MultiFormatReader {
  const reader = new MultiFormatReader();
  const hints = new Map<DecodeHintType, unknown>();
  hints.set(DecodeHintType.POSSIBLE_FORMATS, FORMATS);
  if (tryHarder) hints.set(DecodeHintType.TRY_HARDER, true);
  reader.setHints(hints);
  return reader;
}

const fast = makeReader(false);
const hard = makeReader(true);

/** RGBA -> 8-bit luminance (BT.601 integer approximation). */
export function toLuminance(rgba: Uint8ClampedArray | Uint8Array, width: number, height: number): Uint8ClampedArray {
  const out = new Uint8ClampedArray(width * height);
  for (let i = 0, p = 0; i < out.length; i++, p += 4) {
    out[i] = (rgba[p] * 77 + rgba[p + 1] * 150 + rgba[p + 2] * 29) >> 8;
  }
  return out;
}

/** Decodes one frame; returns the text or null when no barcode was found. */
export function decodeFrame(
  rgba: Uint8ClampedArray | Uint8Array,
  width: number,
  height: number,
  opts: { tryHarder?: boolean } = {},
): string | null {
  const source = new RGBLuminanceSource(toLuminance(rgba, width, height), width, height);
  const bitmap = new BinaryBitmap(new HybridBinarizer(source));
  try {
    return (opts.tryHarder ? hard : fast).decode(bitmap).getText();
  } catch {
    return null; // NotFound / Checksum / Format: nothing readable in this frame
  }
}

/**
 * Decode strategy for a live frame: the fast pass every time, the slower TRY_HARDER pass only on
 * every `hardEvery`-th miss so the loop stays at ~8-10 fps.
 */
export class FrameDecoder {
  private misses = 0;
  constructor(private readonly hardEvery = 3) {}

  decode(rgba: Uint8ClampedArray | Uint8Array, width: number, height: number): string | null {
    const quick = decodeFrame(rgba, width, height);
    if (quick) {
      this.misses = 0;
      return quick;
    }
    this.misses++;
    if (this.misses % this.hardEvery === 0) return decodeFrame(rgba, width, height, { tryHarder: true });
    return null;
  }
}
