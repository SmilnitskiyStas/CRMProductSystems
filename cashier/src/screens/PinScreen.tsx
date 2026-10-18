import { useCallback, useEffect, useRef, useState } from 'react';
import type { OperatorInfo, RegisterInfo } from '@shared/types';

/** PINs issued by the server are always 6 digits: nothing is checked before the 6th digit. */
const PIN_LENGTH = 6;
/** A half-typed PIN is discarded after this long without input. */
const INCOMPLETE_PIN_RESET_MS = 60_000;

interface Props {
  register: RegisterInfo;
  notice: string | null;
  onLoggedIn: (op: OperatorInfo) => void;
}

const KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', 'C', '0', '⌫'] as const;

export function PinScreen({ register, notice, onLoggedIn }: Props) {
  const [pin, setPin] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const pinRef = useRef(pin);
  pinRef.current = pin;
  const busyRef = useRef(false);

  const submit = useCallback(
    async (value: string) => {
      if (busyRef.current || value.length !== PIN_LENGTH) return;
      busyRef.current = true;
      setBusy(true);
      setError(null);
      const res = await window.cashier.login(value);
      busyRef.current = false;
      setBusy(false);
      if (res.ok) {
        onLoggedIn(res.data);
      } else {
        setPin('');
        setError(
          res.error.kind === 'locked'
            ? `${res.error.message} Зверніться до менеджера.`
            : res.error.kind === 'unauthorized'
              ? res.error.message || 'Невірний PIN-код.'
              : res.error.message,
        );
      }
    },
    [onLoggedIn],
  );

  const press = useCallback((k: string) => {
    if (busyRef.current) return;
    setError(null);
    if (k === 'C') setPin('');
    else if (k === '⌫') setPin((p) => p.slice(0, -1));
    else setPin((p) => (p.length < PIN_LENGTH ? p + k : p));
  }, []);

  // Physical keyboard.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (/^\d$/.test(e.key)) press(e.key);
      else if (e.key === 'Backspace') press('⌫');
      else if (e.key === 'Escape' || e.key === 'Delete') press('C');
      else if (e.key === 'Enter') void submit(pinRef.current);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [press, submit]);

  // Auto-submit only once all 6 digits are in — never on a partial PIN.
  useEffect(() => {
    if (pin.length !== PIN_LENGTH) return;
    const t = setTimeout(() => void submit(pin), 80);
    return () => clearTimeout(t);
  }, [pin, submit]);

  // A PIN left incomplete is cleared after a minute (restarts on every keystroke).
  useEffect(() => {
    if (pin.length === 0 || pin.length >= PIN_LENGTH) return;
    const t = setTimeout(() => setPin(''), INCOMPLETE_PIN_RESET_MS);
    return () => clearTimeout(t);
  }, [pin]);

  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-6 p-6">
      <div className="text-center">
        <p className="text-slate-400">Каса №{register.number}</p>
        <h1 className="text-3xl font-bold">{register.name}</h1>
        <p className="mt-2 text-slate-400">Введіть PIN-код для входу</p>
      </div>

      <div className="flex h-8 gap-3" aria-label="PIN">
        {Array.from({ length: PIN_LENGTH }).map((_, i) => (
          <span
            key={i}
            className={`h-5 w-5 rounded-full border-2 ${i < pin.length ? 'border-brand-400 bg-brand-400' : 'border-slate-600'}`}
          />
        ))}
      </div>

      <div className="min-h-[3rem] w-full max-w-sm text-center">
        {error && <p className="rounded-lg bg-rose-500/10 px-4 py-2 text-rose-300">{error}</p>}
        {!error && notice && <p className="rounded-lg bg-amber-500/10 px-4 py-2 text-amber-300">{notice}</p>}
        {busy && <p className="text-slate-400">Перевірка…</p>}
      </div>

      <div className="grid grid-cols-3 gap-3">
        {KEYS.map((k) => (
          <button
            key={k}
            onClick={() => press(k)}
            disabled={busy}
            className={`h-20 w-24 rounded-2xl text-3xl font-semibold transition active:scale-95 disabled:opacity-50 ${
              k === 'C' || k === '⌫' ? 'bg-slate-800 text-slate-300 hover:bg-slate-700' : 'bg-slate-800 hover:bg-slate-700'
            }`}
          >
            {k}
          </button>
        ))}
      </div>
    </div>
  );
}
