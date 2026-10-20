let ctx: AudioContext | null = null;

/** Short feedback tone: 'ok' = high single beep, 'error' = low double buzz. Never throws. */
export function playTone(kind: 'ok' | 'error'): void {
  try {
    ctx ??= new AudioContext();
    if (ctx.state === 'suspended') void ctx.resume();
    const notes = kind === 'ok' ? [{ f: 1200, at: 0, d: 0.09 }] : [{ f: 220, at: 0, d: 0.14 }, { f: 180, at: 0.18, d: 0.18 }];
    for (const n of notes) {
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = kind === 'ok' ? 'sine' : 'square';
      osc.frequency.value = n.f;
      gain.gain.value = 0.15;
      osc.connect(gain).connect(ctx.destination);
      const t0 = ctx.currentTime + n.at;
      osc.start(t0);
      osc.stop(t0 + n.d);
    }
  } catch {
    /* audio unavailable: visual feedback still shows */
  }
}
