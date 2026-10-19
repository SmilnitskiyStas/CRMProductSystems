import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { PNG } from 'pngjs';
import bwipjs from 'bwip-js/node';
import { describe, expect, it } from 'vitest';
import { gs1CheckDigitValid, isAcceptableCode, ScanGate } from '@shared/cameraScan';
import { decodeFrame, FrameDecoder } from './barcodeDecoder';
import { CameraController, type MediaDevicesLike, type StreamLike } from './cameraController';

describe('check digits', () => {
  it('validates EAN-13, EAN-8 and UPC-A', () => {
    expect(gs1CheckDigitValid('4823117506458')).toBe(true);
    expect(gs1CheckDigitValid('4823117506459')).toBe(false);
    expect(gs1CheckDigitValid('96385074')).toBe(true);
    expect(gs1CheckDigitValid('96385075')).toBe(false);
    expect(gs1CheckDigitValid('036000291452')).toBe(true);
    expect(gs1CheckDigitValid('12345')).toBe(false);
  });

  it('accepts internal Code128 codes but rejects numeric codes of EAN-like length with a bad digit', () => {
    expect(isAcceptableCode('4823117506458')).toBe(true);
    expect(isAcceptableCode('4823117506450')).toBe(false);
    expect(isAcceptableCode('123456789')).toBe(false); // 9 digits: not a valid retail length
    expect(isAcceptableCode('SKU-0042')).toBe(true);
    expect(isAcceptableCode('ab')).toBe(false);
  });
});

describe('ScanGate', () => {
  const code = '4823117506458';
  it('needs 2 consecutive identical reads', () => {
    const g = new ScanGate();
    expect(g.feed(code, 0)).toBeNull();
    expect(g.feed(code, 100)).toBe(code);
  });
  it('a different read in between resets the confirmation; stale reads do not count', () => {
    const g = new ScanGate();
    g.feed(code, 0);
    expect(g.feed('4820046962881', 100)).toBeNull();
    expect(g.feed(code, 200)).toBeNull();
    expect(g.feed(code, 5000)).toBeNull(); // too long since the previous read: counted as a fresh first read
    expect(g.feed(code, 5100)).toBe(code);
  });
  it('ignores the same code for 1.5 s, sliding while it stays in view, then accepts it again', () => {
    const g = new ScanGate();
    g.feed(code, 0);
    expect(g.feed(code, 100)).toBe(code);
    let t = 100;
    for (let i = 0; i < 30; i++) expect(g.feed(code, (t += 100))).toBeNull(); // 3 s of continuous reads: no re-add
    t += 1600; // product removed for longer than the cooldown
    expect(g.feed(code, t)).toBeNull();
    expect(g.feed(code, t + 100)).toBe(code);
  });
  it('another product is accepted right away after the first', () => {
    const g = new ScanGate();
    g.feed(code, 0);
    g.feed(code, 100);
    expect(g.feed('4820046962881', 200)).toBeNull();
    expect(g.feed('4820046962881', 300)).toBe('4820046962881');
  });
  it('drops codes failing the check digit', () => {
    const g = new ScanGate();
    expect(g.feed('4823117506450', 0)).toBeNull();
    expect(g.feed('4823117506450', 100)).toBeNull();
  });
});

async function barcodePixels(bcid: string, text: string, scale = 3) {
  const png = await bwipjs.toBuffer({
    bcid,
    text,
    scale,
    height: 14,
    includetext: false,
    paddingwidth: 12,
    paddingheight: 8,
    backgroundcolor: 'ffffff',
  });
  const img = PNG.sync.read(png);
  return { data: new Uint8ClampedArray(img.data), w: img.width, h: img.height };
}

describe('decoder (same code path as the live camera loop, fed with generated PNG pixels)', () => {
  it.each([
    ['ean13', '482311750645'],
    ['ean8', '9638507'],
  ])('decodes %s', async (bcid, payload) => {
    const { data, w, h } = await barcodePixels(bcid, payload);
    const text = decodeFrame(data, w, h);
    expect(text).not.toBeNull();
    expect(text!.startsWith(payload)).toBe(true);
    expect(isAcceptableCode(text!)).toBe(true);
  });

  it('decodes code128 through FrameDecoder', async () => {
    const { data, w, h } = await barcodePixels('code128', 'SKU-0042');
    expect(new FrameDecoder().decode(data, w, h)).toBe('SKU-0042');
  });

  it('returns null for a blank frame', () => {
    const blank = new Uint8ClampedArray(200 * 100 * 4).fill(255);
    expect(new FrameDecoder().decode(blank, 200, 100)).toBeNull();
  });
});

describe('CameraController (fake media stream)', () => {
  const fakeStream = () => {
    const stopped: boolean[] = [];
    const stream: StreamLike = {
      getTracks: () => [{ stop: () => void stopped.push(true) }, { stop: () => void stopped.push(true) }],
    };
    return { stream, stopped };
  };
  const md = (impl: Partial<MediaDevicesLike>): MediaDevicesLike => ({
    getUserMedia: async () => fakeStream().stream,
    enumerateDevices: async () => [],
    ...impl,
  });
  const err = (name: string) => Object.assign(new Error(name), { name });

  it('starts, lists cameras and stops every track', async () => {
    const { stream, stopped } = fakeStream();
    const c = new CameraController(
      md({
        getUserMedia: async () => stream,
        enumerateDevices: async () => [
          { kind: 'videoinput', deviceId: 'a', label: 'Integrated' },
          { kind: 'audioinput', deviceId: 'm', label: 'Mic' },
          { kind: 'videoinput', deviceId: 'b', label: '' },
        ],
      }),
    );
    const r = await c.start();
    expect(r.status).toBe('scanning');
    expect(r.devices).toEqual([
      { deviceId: 'a', label: 'Integrated' },
      { deviceId: 'b', label: 'Камера 2' },
    ]);
    c.stop();
    expect(stopped).toHaveLength(2);
    expect(c.active).toBe(false);
    c.stop(); // idempotent
    expect(stopped).toHaveLength(2);
  });

  it('maps permission and device errors to readable states', async () => {
    const denied = await new CameraController(md({ getUserMedia: () => Promise.reject(err('NotAllowedError')) })).start();
    expect(denied.status).toBe('denied');
    expect(denied.message).toContain('Конфіденційність');
    expect((await new CameraController(md({ getUserMedia: () => Promise.reject(err('NotFoundError')) })).start()).status).toBe('nocamera');
    const busy = await new CameraController(md({ getUserMedia: () => Promise.reject(err('NotReadableError')) })).start();
    expect(busy.status).toBe('error');
    expect(busy.message).toContain('інша програма');
  });

  it('falls back to the default camera when the remembered one is gone, and stops tracks if enumeration fails', async () => {
    const calls: unknown[] = [];
    const c = new CameraController(
      md({
        getUserMedia: async (cons) => {
          calls.push(cons.video);
          if (calls.length === 1) throw err('OverconstrainedError');
          return fakeStream().stream;
        },
      }),
    );
    expect((await c.start('gone')).status).toBe('scanning');
    expect(calls).toHaveLength(2);

    const { stream, stopped } = fakeStream();
    const c2 = new CameraController(md({ getUserMedia: async () => stream, enumerateDevices: () => Promise.reject(err('X')) }));
    expect((await c2.start()).status).toBe('error');
    expect(stopped).toHaveLength(2);
  });
});

describe('demo barcode images (dev-assets/barcodes)', () => {
  const dir = join(process.cwd(), 'dev-assets', 'barcodes');
  const files = existsSync(dir) ? readdirSync(dir).filter((f) => f.endsWith('.png')) : [];
  it('every PNG decodes to the code in its filename and passes the check digit', () => {
    expect(files.length).toBeGreaterThanOrEqual(4);
    for (const f of files) {
      const img = PNG.sync.read(readFileSync(join(dir, f)));
      const text = new FrameDecoder().decode(new Uint8ClampedArray(img.data), img.width, img.height);
      expect(text, f).toBe(f.split('__')[1].replace('.png', ''));
      expect(isAcceptableCode(text!), f).toBe(true);
    }
  });
});
