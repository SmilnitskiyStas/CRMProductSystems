import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import vectors from './golden-vectors.json';
import { applyPromotions, type PromotionContext, type PromotionLine, type PromotionRule, type PromotionSegment } from './engine';

interface Vector {
  name: string;
  context: PromotionContext;
  rules: PromotionRule[];
  lines: PromotionLine[];
  expected: PromotionSegment[][];
}

describe('promotion engine - golden vectors shared with the C# engine', () => {
  const all = (vectors as unknown as { vectors: Vector[] }).vectors;

  it('has vectors', () => {
    expect(all.length).toBeGreaterThan(20);
  });

  it.each(all.map((v) => [v.name, v] as const))('%s', (_name, v) => {
    const got = applyPromotions(v.lines, v.rules, v.context);
    expect(got).toEqual(v.expected);
  });
});

describe('golden vectors drift check', () => {
  const backend = join(process.cwd(), '..', 'backend', 'ShelfGuard.Tests', 'Promotions', 'golden-vectors.json');
  const local = join(process.cwd(), 'src', 'shared', 'promotions', 'golden-vectors.json');

  it.skipIf(!existsSync(backend))('local copy is byte-identical to the backend file (run `npm run sync-vectors` if not)', () => {
    expect(readFileSync(local).equals(readFileSync(backend))).toBe(true);
  });
});
