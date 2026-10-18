import type { PrintCodepage } from '@shared/types';
import { encodeText } from './encoder';

const ESC = 0x1b;
const GS = 0x1d;
const LF = 0x0a;

export type Align = 'left' | 'center' | 'right';

/** Minimal ESC/POS byte builder (chainable). No hard dependency on any printer library. */
export class EscPosBuilder {
  private bytes: number[] = [];

  constructor(private readonly codepage: PrintCodepage) {}

  private raw(...b: number[]): this {
    this.bytes.push(...b);
    return this;
  }

  /** ESC @ - reset printer state. */
  init(): this {
    return this.raw(ESC, 0x40);
  }

  /** ESC t n - select the printer's character table (skipped for UTF-8 printers). */
  selectCodepage(id: number): this {
    return this.codepage === 'utf8' ? this : this.raw(ESC, 0x74, id & 0xff);
  }

  align(a: Align): this {
    return this.raw(ESC, 0x61, a === 'left' ? 0 : a === 'center' ? 1 : 2);
  }

  bold(on: boolean): this {
    return this.raw(ESC, 0x45, on ? 1 : 0);
  }

  /** GS ! n - character size multiplier 1..8 for width and height. */
  size(width = 1, height = 1): this {
    const w = Math.min(Math.max(width, 1), 8) - 1;
    const h = Math.min(Math.max(height, 1), 8) - 1;
    return this.raw(GS, 0x21, (w << 4) | h);
  }

  text(s: string): this {
    this.bytes.push(...encodeText(s, this.codepage));
    return this;
  }

  line(s = ''): this {
    return this.text(s).raw(LF);
  }

  /** ESC d n - print and feed n lines. */
  feed(n = 1): this {
    return this.raw(ESC, 0x64, Math.min(Math.max(n, 0), 255));
  }

  /** GS V B 0 - feed to the cutting position and partial-cut. */
  cut(): this {
    return this.raw(GS, 0x56, 0x42, 0x00);
  }

  /** ESC p m t1 t2 - cash drawer kick pulse on pin 2 (m=0) / pin 5 (m=1). */
  drawerKick(pin: 0 | 1 = 0): this {
    return this.raw(ESC, 0x70, pin, 0x19, 0xfa);
  }

  /** CODE128 barcode with human-readable text below. */
  barcode128(data: string, height = 64): this {
    const body = [0x7b, 0x42, ...Array.from(data, (c) => c.charCodeAt(0) & 0x7f)]; // {B = code set B
    return this.raw(GS, 0x68, height, GS, 0x77, 2, GS, 0x48, 2, GS, 0x6b, 73, body.length, ...body);
  }

  /** QR code (model 2). */
  qr(data: string, moduleSize = 5): this {
    const payload = Array.from(new TextEncoder().encode(data));
    const len = payload.length + 3;
    return this.raw(
      GS, 0x28, 0x6b, 4, 0, 0x31, 0x41, 0x32, 0, // model 2
      GS, 0x28, 0x6b, 3, 0, 0x31, 0x43, moduleSize, // module size
      GS, 0x28, 0x6b, 3, 0, 0x31, 0x45, 0x31, // error correction M
      GS, 0x28, 0x6b, len & 0xff, len >> 8, 0x31, 0x50, 0x30, ...payload, // store
      GS, 0x28, 0x6b, 3, 0, 0x31, 0x51, 0x30, // print
    );
  }

  build(): Uint8Array {
    return Uint8Array.from(this.bytes);
  }
}
