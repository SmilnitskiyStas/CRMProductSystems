import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ConnectionState } from '@shared/types';
import { ConnectivityMonitor, DEFAULT_CONNECTIVITY } from './connectivity';

function setup(initial = false) {
  let reachable = initial;
  const probe = vi.fn(async () => reachable);
  const states: ConnectionState[] = [];
  const reconnected = vi.fn();
  const m = new ConnectivityMonitor(probe, { onState: (s) => states.push(s), onReconnected: reconnected }, DEFAULT_CONNECTIVITY, () => Date.now());
  return { m, probe, states, reconnected, setReachable: (v: boolean) => (reachable = v) };
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('ConnectivityMonitor', () => {
  it('starts offline, keeps probing with capped backoff, flips online once the server appears and fires the sync hook once', async () => {
    const { m, probe, reconnected, setReachable } = setup(false);
    m.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(m.state.online).toBe(false);
    expect(probe).toHaveBeenCalledTimes(1);

    // 5s, 7.5s, 11.25s, then capped at 15s
    await vi.advanceTimersByTimeAsync(5_000);
    await vi.advanceTimersByTimeAsync(7_500);
    await vi.advanceTimersByTimeAsync(11_250);
    expect(probe).toHaveBeenCalledTimes(4);
    const next = new Date(m.state.nextProbeAt!).getTime() - Date.now();
    expect(next).toBe(15_000);
    await vi.advanceTimersByTimeAsync(15_000);
    expect(m.state.nextProbeAt && new Date(m.state.nextProbeAt).getTime() - Date.now()).toBe(15_000);

    setReachable(true); // API started
    await vi.advanceTimersByTimeAsync(15_000);
    expect(m.state.online).toBe(true);
    expect(reconnected).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(30_000); // periodic online probe: no second reconnect hook
    expect(reconnected).toHaveBeenCalledTimes(1);
    m.stop();
  });

  it('online -> offline needs 2 consecutive failures (flap debounce); one success flips back', async () => {
    const { m, setReachable, reconnected, states } = setup(true);
    m.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(m.state.online).toBe(true);
    expect(reconnected).toHaveBeenCalledTimes(1);

    setReachable(false);
    await vi.advanceTimersByTimeAsync(30_000); // first failure: still online, confirm probe scheduled
    expect(m.state.online).toBe(true);
    setReachable(true); // blip over
    await vi.advanceTimersByTimeAsync(2_000);
    expect(m.state.online).toBe(true);
    expect(states.every((s) => s.online || s === states[0])).toBe(true);

    setReachable(false);
    await vi.advanceTimersByTimeAsync(30_000);
    await vi.advanceTimersByTimeAsync(2_000); // second consecutive failure
    expect(m.state.online).toBe(false);

    setReachable(true);
    await m.probeNow(); // a single success is enough
    expect(m.state.online).toBe(true);
    expect(reconnected).toHaveBeenCalledTimes(2);
    m.stop();
  });

  it('manual retry probes immediately and is single-flight', async () => {
    const { m, probe, setReachable } = setup(false);
    m.start();
    await vi.advanceTimersByTimeAsync(0);
    setReachable(true);
    const [a, b] = [m.probeNow(), m.probeNow()];
    await Promise.all([a, b]);
    expect(probe).toHaveBeenCalledTimes(2); // start + one manual (the second call joined it)
    expect(m.state.online).toBe(true);
    m.stop();
  });

  it('trigger() (failed API call / window focus) is throttled', async () => {
    const { m, probe } = setup(true);
    m.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(probe).toHaveBeenCalledTimes(1);
    m.trigger();
    m.trigger();
    await vi.advanceTimersByTimeAsync(0);
    expect(probe).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1_100);
    m.trigger();
    await vi.advanceTimersByTimeAsync(0);
    expect(probe).toHaveBeenCalledTimes(3);
    m.stop();
  });

  it('a probe that resolves true for any HTTP status counts as reachable (probe result is just a boolean from the caller)', async () => {
    // the real probe is `!res.networkError`; model a 401 response as "reachable"
    const respond = async (status: number | 'neterr') => status !== 'neterr';
    const m = new ConnectivityMonitor(() => respond(401), { onState: () => undefined, onReconnected: () => undefined });
    await m.probeNow();
    expect(m.state.online).toBe(true);
    m.stop();
  });

  it('stop() clears timers: nothing is left pending and no further probes run', async () => {
    const { m, probe } = setup(true);
    m.start();
    await vi.advanceTimersByTimeAsync(0);
    m.stop();
    expect(vi.getTimerCount()).toBe(0);
    await vi.advanceTimersByTimeAsync(120_000);
    expect(probe).toHaveBeenCalledTimes(1);
  });

  it('a throwing reconnect hook does not break monitoring', async () => {
    const m = new ConnectivityMonitor(
      async () => true,
      {
        onState: () => undefined,
        onReconnected: () => {
          throw new Error('sync failed');
        },
      },
    );
    await m.probeNow();
    await vi.advanceTimersByTimeAsync(0);
    expect(m.state.online).toBe(true);
    m.stop();
  });
});
