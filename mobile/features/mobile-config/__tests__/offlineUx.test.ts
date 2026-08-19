import { deriveMobileConfigOfflineUx } from '../offlineUx';

describe('mobile config offline UX', () => {
  test('keeps fresh and preview configuration quiet', () => {
    expect(deriveMobileConfigOfflineUx({ online: true, source: 'mock', cachedAt: 1 }).visible).toBe(false);
    expect(deriveMobileConfigOfflineUx({ online: false, source: 'preview', cachedAt: null }).visible).toBe(false);
  });

  test('does not flash a fallback warning during initial loading', () => {
    expect(
      deriveMobileConfigOfflineUx({ online: false, source: 'safe-default', cachedAt: null, loading: true })
        .visible
    ).toBe(false);
  });

  test('announces cached offline configuration without blocking the app', () => {
    const state = deriveMobileConfigOfflineUx({ online: false, source: 'last-valid', cachedAt: 1_700_000_000_000 });
    expect(state.visible).toBe(true);
    expect(state.message).toContain('Офлайн-режим');
    expect(state.message).toContain('Показано збережене оформлення');
  });

  test('distinguishes safe default from a validated cache', () => {
    const state = deriveMobileConfigOfflineUx({ online: false, source: 'safe-default', cachedAt: null });
    expect(state.message).toContain('збереженої конфігурації');
  });
});
