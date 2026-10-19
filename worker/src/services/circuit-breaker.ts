// Minimal circuit breaker for optional outbound dependencies (Telegram, ...).
// closed → (N consecutive transient failures) → open for `openMs` → half-open (one trial call).
// Only failures the caller marks as transient count: a 4xx (bad chat id) must not trip it.

export class CircuitOpenError extends Error {
  constructor(public readonly name_: string) {
    super(`Circuit '${name_}' is open — skipping call`);
    this.name = "CircuitOpenError";
  }
}

export class TransientError extends Error {}

export class CircuitBreaker {
  private failures = 0;
  private openedAt = 0;
  private trialInFlight = false;

  constructor(
    private readonly name: string,
    private readonly threshold = 5,
    private readonly openMs = 30_000,
  ) {}

  get isOpen(): boolean {
    return this.failures >= this.threshold && Date.now() - this.openedAt < this.openMs;
  }

  async run<T>(fn: () => Promise<T>): Promise<T> {
    if (this.isOpen) throw new CircuitOpenError(this.name);

    const halfOpen = this.failures >= this.threshold;
    if (halfOpen) {
      // Only one trial call while half-open; others keep failing fast.
      if (this.trialInFlight) throw new CircuitOpenError(this.name);
      this.trialInFlight = true;
    }

    try {
      const result = await fn();
      this.failures = 0;
      return result;
    } catch (err) {
      if (err instanceof TransientError) {
        this.failures++;
        if (this.failures >= this.threshold) this.openedAt = Date.now();
      } else {
        // Non-transient (e.g. 4xx): the dependency itself is healthy.
        this.failures = 0;
      }
      throw err;
    } finally {
      if (halfOpen) this.trialInFlight = false;
    }
  }
}
