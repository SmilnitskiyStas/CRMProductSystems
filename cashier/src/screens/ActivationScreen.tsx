import { useState } from 'react';
import type { RegisterInfo } from '@shared/types';

interface Props {
  defaultServerUrl: string;
  onActivated: (register: RegisterInfo) => void;
}

export function ActivationScreen({ defaultServerUrl, onActivated }: Props) {
  const [serverUrl, setServerUrl] = useState(defaultServerUrl);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<RegisterInfo | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const res = await window.cashier.activate(serverUrl, code);
    setBusy(false);
    if (res.ok) setDone(res.data);
    else setError(res.error.message);
  };

  if (done) {
    return (
      <div className="flex flex-1 items-center justify-center p-8">
        <div className="card w-full max-w-lg text-center">
          <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-brand-500/20 text-3xl text-brand-400">
            ✓
          </div>
          <h1 className="text-2xl font-bold">Касу активовано</h1>
          <p className="mt-4 text-5xl font-extrabold text-brand-400">№{done.number}</p>
          <p className="mt-2 text-xl">{done.name}</p>
          {done.storeName && <p className="mt-1 text-slate-400">{done.storeName}</p>}
          <button className="btn-primary mt-8 w-full" onClick={() => onActivated(done)} autoFocus>
            Продовжити
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-1 items-center justify-center p-8">
      <form onSubmit={submit} className="card w-full max-w-lg space-y-5">
        <div>
          <h1 className="text-3xl font-bold">ShelfGuard Cashier</h1>
          <p className="mt-1 text-slate-400">Активація каси. Код видає менеджер на сторінці «Каси».</p>
        </div>
        <label className="block space-y-2">
          <span className="text-sm text-slate-400">Адреса сервера</span>
          <input
            className="field"
            value={serverUrl}
            onChange={(e) => setServerUrl(e.target.value)}
            placeholder="https://agrusystems.pp.ua"
            spellCheck={false}
          />
        </label>
        <label className="block space-y-2">
          <span className="text-sm text-slate-400">Код активації</span>
          <input
            className="field text-center font-mono text-2xl tracking-widest"
            value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            placeholder="XXXX-XXXX"
            autoFocus
            spellCheck={false}
          />
        </label>
        {error && <p className="rounded-lg bg-rose-500/10 px-4 py-3 text-rose-300">{error}</p>}
        <button className="btn-primary w-full" disabled={busy || !code.trim() || !serverUrl.trim()}>
          {busy ? 'Активація…' : 'Активувати'}
        </button>
      </form>
    </div>
  );
}
