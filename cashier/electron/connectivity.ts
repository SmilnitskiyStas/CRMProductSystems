import type { ConnectionState } from '@shared/types';

export interface ConnectivityOptions {
  /** First re-probe delay while offline; grows by `backoffFactor` up to `offlineMaxMs`. */
  offlineBaseMs: number;
  offlineMaxMs: number;
  backoffFactor: number;
  /** Probe period while online. */
  onlineMs: number;
  /** Re-probe delay after the first failure while online (to confirm before flipping offline). */
  confirmMs: number;
  /** Consecutive failures needed to flip online -> offline. */
  failuresToOffline: number;
  /** Minimum gap between failure/focus-triggered probes. */
  minTriggerGapMs: number;
}

export const DEFAULT_CONNECTIVITY: ConnectivityOptions = {
  offlineBaseMs: 5_000,
  offlineMaxMs: 15_000,
  backoffFactor: 1.5,
  onlineMs: 30_000,
  confirmMs: 2_000,
  failuresToOffline: 2,
  minTriggerGapMs: 1_000,
};

export interface ConnectivityHooks {
  /** Every state change worth showing (flip, probe start/end, next-probe time). */
  onState(state: ConnectionState): void;
  /** Fired exactly once per offline -> online transition (also the first successful probe after start). */
  onReconnected(): void | Promise<void>;
}

/**
 * Single source of truth for "is the server reachable". The probe reports whether ANY HTTP
 * response came back (401/404 count as reachable); only network errors / timeouts mean offline.
 * Pure of Electron and timers beyond setTimeout, so it is tested with fake timers.
 */
export class ConnectivityMonitor {
  private online = false;
  private failures = 0;
  private offlineAttempts = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private nextProbeAt: number | null = null;
  private checking = false;
  private inFlight: Promise<ConnectionState> | null = null;
  private lastTrigger = -Infinity;
  private stopped = false;

  constructor(
    private readonly probe: () => Promise<boolean>,
    private readonly hooks: ConnectivityHooks,
    private readonly opts: ConnectivityOptions = DEFAULT_CONNECTIVITY,
    private readonly now: () => number = Date.now,
  ) {}

  get state(): ConnectionState {
    return {
      online: this.online,
      checking: this.checking,
      nextProbeAt: this.nextProbeAt ? new Date(this.nextProbeAt).toISOString() : null,
    };
  }

  start(): void {
    this.stopped = false;
    void this.probeNow();
  }

  stop(): void {
    this.stopped = true;
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    this.nextProbeAt = null;
  }

  /** Manual "Спробувати зараз", window focus, startup. Single-flight. */
  probeNow(): Promise<ConnectionState> {
    if (this.inFlight) return this.inFlight;
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    this.nextProbeAt = null;
    this.checking = true;
    this.hooks.onState(this.state);
    this.inFlight = this.runProbe().finally(() => {
      this.inFlight = null;
    });
    return this.inFlight;
  }

  /** Throttled trigger for "an API call just failed" / "window got focus". */
  trigger(): void {
    const t = this.now();
    if (this.stopped || t - this.lastTrigger < this.opts.minTriggerGapMs) return;
    this.lastTrigger = t;
    void this.probeNow();
  }

  private async runProbe(): Promise<ConnectionState> {
    let reachable = false;
    try {
      reachable = await this.probe();
    } catch {
      reachable = false;
    }
    this.checking = false;
    if (this.stopped) return this.state;

    if (reachable) {
      this.failures = 0;
      this.offlineAttempts = 0;
      const flipped = !this.online;
      this.online = true;
      this.schedule(this.opts.onlineMs);
      this.hooks.onState(this.state);
      if (flipped) {
        // fire-and-forget: a slow or failing sync must neither block nor break monitoring
        void Promise.resolve()
          .then(() => this.hooks.onReconnected())
          .catch(() => undefined);
      }
    } else {
      this.failures++;
      if (this.online && this.failures < this.opts.failuresToOffline) {
        this.schedule(this.opts.confirmMs); // debounce: confirm before declaring offline
      } else {
        this.online = false;
        const delay = Math.min(
          this.opts.offlineBaseMs * this.opts.backoffFactor ** this.offlineAttempts,
          this.opts.offlineMaxMs,
        );
        this.offlineAttempts++;
        this.schedule(delay);
      }
      this.hooks.onState(this.state);
    }
    return this.state;
  }

  private schedule(delayMs: number): void {
    if (this.timer) clearTimeout(this.timer);
    this.nextProbeAt = this.now() + delayMs;
    this.timer = setTimeout(() => void this.probeNow(), delayMs);
    this.timer.unref?.(); // never keep the process alive
  }
}
