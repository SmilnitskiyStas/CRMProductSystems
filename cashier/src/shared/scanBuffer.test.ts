import { describe, expect, it } from 'vitest';
import { ScanDetector } from './scanBuffer';

function type(d: ScanDetector, text: string, start: number, gap: number) {
  let t = start;
  let last = d.feed('Shift', t);
  for (const ch of text) {
    t += gap;
    last = d.feed(ch, t);
  }
  return { last, t };
}

describe('ScanDetector', () => {
  it('reports a fast burst ended by Enter as a scan', () => {
    const d = new ScanDetector();
    const { t } = type(d, '4820024700016', 1000, 8);
    expect(d.feed('Enter', t + 10)).toEqual({ type: 'scan', code: '4820024700016' });
  });

  it('ignores slow human typing', () => {
    const d = new ScanDetector();
    const { t } = type(d, '12345', 1000, 250);
    expect(d.feed('Enter', t + 100)).toEqual({ type: 'none' });
  });

  it('ignores short bursts', () => {
    const d = new ScanDetector();
    const { t } = type(d, '12', 1000, 5);
    expect(d.feed('Enter', t + 5)).toEqual({ type: 'none' });
  });

  it('restarts the buffer after a pause so a stale prefix does not pollute the code', () => {
    const d = new ScanDetector();
    d.feed('9', 100);
    const { t } = type(d, '4820024700016', 2000, 8);
    expect(d.feed('Enter', t + 8)).toEqual({ type: 'scan', code: '4820024700016' });
  });

  it('rejects when Enter comes long after the last char', () => {
    const d = new ScanDetector();
    const { t } = type(d, '4820024700016', 1000, 8);
    expect(d.feed('Enter', t + 500)).toEqual({ type: 'none' });
  });

  it('signals burst mid-scan and handles two scans back to back', () => {
    const d = new ScanDetector();
    const { last, t } = type(d, '111111', 0, 5);
    expect(last.type).toBe('burst');
    expect(d.feed('Enter', t + 5).type).toBe('scan');
    const second = type(d, '222222', t + 400, 5);
    expect(d.feed('Enter', second.t + 5)).toEqual({ type: 'scan', code: '222222' });
  });
});
