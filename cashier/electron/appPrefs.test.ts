import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { AppPrefsStore } from './appPrefs';

describe('AppPrefsStore', () => {
  it('defaults to 15 minutes when there is no file', () => {
    const dir = mkdtempSync(join(tmpdir(), 'prefs-'));
    expect(new AppPrefsStore(join(dir, 'app-prefs.json')).get().idleLogoutMinutes).toBe(15);
  });

  it('clamps to 1..120, rounds, and falls back on garbage', () => {
    expect(AppPrefsStore.normalize({ idleLogoutMinutes: 0 }).idleLogoutMinutes).toBe(1);
    expect(AppPrefsStore.normalize({ idleLogoutMinutes: -5 }).idleLogoutMinutes).toBe(1);
    expect(AppPrefsStore.normalize({ idleLogoutMinutes: 9999 }).idleLogoutMinutes).toBe(120);
    expect(AppPrefsStore.normalize({ idleLogoutMinutes: 7.6 }).idleLogoutMinutes).toBe(8);
    expect(AppPrefsStore.normalize({ idleLogoutMinutes: Number.NaN }).idleLogoutMinutes).toBe(15);
    expect(AppPrefsStore.normalize({ idleLogoutMinutes: 'abc' as unknown as number }).idleLogoutMinutes).toBe(15);
    expect(AppPrefsStore.normalize(null).idleLogoutMinutes).toBe(15);
  });

  it('persists and reloads the saved value', () => {
    const file = join(mkdtempSync(join(tmpdir(), 'prefs-')), 'nested', 'app-prefs.json');
    const store = new AppPrefsStore(file);
    expect(store.save({ idleLogoutMinutes: 30 }).idleLogoutMinutes).toBe(30);
    expect(JSON.parse(readFileSync(file, 'utf8')).idleLogoutMinutes).toBe(30);
    expect(new AppPrefsStore(file).get().idleLogoutMinutes).toBe(30);
  });

  it('ignores a corrupt file', () => {
    const file = join(mkdtempSync(join(tmpdir(), 'prefs-')), 'app-prefs.json');
    writeFileSync(file, '{not json', 'utf8');
    expect(new AppPrefsStore(file).get().idleLogoutMinutes).toBe(15);
  });
});
