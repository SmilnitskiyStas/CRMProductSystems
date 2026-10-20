import { useEffect, useState } from 'react';
import type { ConnectionState } from '@shared/types';

interface Props {
  connection: ConnectionState;
  online: boolean;
  /** Outbox is being flushed right now. */
  syncing: boolean;
  /** Set for a few seconds after an offline -> online transition. */
  reconnectedAt: number | null;
}

/** Offline notice with a retry countdown, and a short success note after reconnecting. */
export function ConnectionBanner({ connection, online, syncing, reconnectedAt }: Props) {
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    if (online) return;
    const t = setInterval(() => setNow(Date.now()), 500);
    return () => clearInterval(t);
  }, [online]);

  if (!online) {
    const secs = connection.nextProbeAt ? Math.max(0, Math.ceil((new Date(connection.nextProbeAt).getTime() - now) / 1000)) : null;
    return (
      <div className="flex items-center gap-4 bg-rose-600/90 px-5 py-2 text-white">
        <span className="font-semibold">
          Немає зв’язку з сервером
          {connection.checking ? ' · перевірка…' : secs !== null ? ` · повторна спроба через ${secs} с` : ''}
        </span>
        <button
          className="ml-auto rounded-lg bg-white/20 px-4 py-1 font-semibold hover:bg-white/30 disabled:opacity-50"
          disabled={connection.checking}
          onClick={() => void window.cashier.retryConnection()}
        >
          Спробувати зараз
        </button>
      </div>
    );
  }

  if (reconnectedAt) {
    return (
      <div className="bg-emerald-600/90 px-5 py-2 font-semibold text-white">
        Зв’язок відновлено · {syncing ? 'Синхронізація…' : 'Синхронізовано'}
      </div>
    );
  }
  return null;
}
