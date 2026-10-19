import { useCallback, useEffect, useState } from 'react';
import type { OutboxStatus, QueueEntry, QueueSummary } from '@shared/types';
import { formatMoney } from '@shared/money';

const STATUS: Record<OutboxStatus, { label: string; cls: string }> = {
  pending: { label: 'Очікує', cls: 'bg-amber-500/20 text-amber-300' },
  sending: { label: 'Відправляється', cls: 'bg-sky-500/20 text-sky-300' },
  synced: { label: 'Відправлено', cls: 'bg-emerald-500/20 text-emerald-300' },
  rejected: { label: 'Відхилено', cls: 'bg-rose-500/20 text-rose-300' },
  dismissed: { label: 'Закрито', cls: 'bg-slate-700 text-slate-300' },
};

type Tab = 'attention' | 'history';

export function QueueScreen({ onBack }: { onBack: () => void }) {
  const [entries, setEntries] = useState<QueueEntry[]>([]);
  const [summary, setSummary] = useState<QueueSummary | null>(null);
  const [tab, setTab] = useState<Tab>('attention');
  const [busy, setBusy] = useState(false);
  const [confirmDismiss, setConfirmDismiss] = useState<QueueEntry | null>(null);
  const [printMsg, setPrintMsg] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setEntries(await window.cashier.queueList());
    setSummary(await window.cashier.queueSummary());
  }, []);

  useEffect(() => {
    void reload();
    return window.cashier.onQueueChange((s) => {
      setSummary(s);
      void window.cashier.queueList().then(setEntries);
    });
  }, [reload]);

  const shown = tab === 'attention' ? entries.filter((e) => e.status !== 'synced') : entries;

  const syncNow = async () => {
    setBusy(true);
    setSummary(await window.cashier.queueSyncNow());
    await reload();
    setBusy(false);
  };

  const retry = async (e: QueueEntry) => {
    setBusy(true);
    await window.cashier.queueRetry(e.clientTransactionId);
    await reload();
    setBusy(false);
  };

  const dismiss = async () => {
    if (!confirmDismiss) return;
    await window.cashier.queueDismiss(confirmDismiss.clientTransactionId);
    setConfirmDismiss(null);
    await reload();
  };

  const reprint = async (e: QueueEntry) => {
    const r = await window.cashier.printReceipt(e.clientTransactionId);
    setPrintMsg(r.ok ? 'Чек надіслано на друк.' : `Не вдалося надрукувати: ${r.error}`);
  };

  return (
    <div className="flex min-h-0 flex-1 flex-col p-6">
      <div className="mb-4 flex items-center gap-3">
        <button className="btn-secondary px-4 py-2" onClick={onBack}>
          ← Назад
        </button>
        <h1 className="text-2xl font-bold">Черга продажів</h1>
        <div className="ml-auto flex gap-2">
          <button className={tab === 'attention' ? 'btn-primary px-4 py-2' : 'btn-secondary px-4 py-2'} onClick={() => setTab('attention')}>
            Потребують уваги
          </button>
          <button className={tab === 'history' ? 'btn-primary px-4 py-2' : 'btn-secondary px-4 py-2'} onClick={() => setTab('history')}>
            Історія
          </button>
          <button className="btn-secondary px-4 py-2" disabled={busy} onClick={() => void syncNow()}>
            {busy || summary?.syncing ? 'Синхронізація…' : 'Синхронізувати'}
          </button>
        </div>
      </div>

      {summary?.needsLogin && (
        <p className="mb-3 rounded-lg bg-amber-500/10 px-4 py-3 text-amber-300">
          Потрібен повторний вхід за PIN-кодом, щоб відправити чергу.
        </p>
      )}
      {summary?.lastError && !summary.needsLogin && summary.pending > 0 && (
        <p className="mb-3 rounded-lg bg-slate-800 px-4 py-3 text-slate-300">{summary.lastError}</p>
      )}
      {printMsg && <p className="mb-3 rounded-lg bg-slate-800 px-4 py-3 text-slate-200">{printMsg}</p>}

      <div className="min-h-0 flex-1 overflow-auto rounded-2xl border border-slate-800">
        {shown.length === 0 ? (
          <div className="flex h-48 items-center justify-center text-xl text-slate-600">
            {tab === 'attention' ? 'Усі продажі відправлено' : 'Продажів ще немає'}
          </div>
        ) : (
          shown.map((e) => (
            <div key={e.clientTransactionId} className="flex items-start gap-4 border-b border-slate-800/70 px-5 py-4">
              <span className={`mt-1 shrink-0 rounded-lg px-3 py-1 text-sm font-semibold ${STATUS[e.status].cls}`}>
                {STATUS[e.status].label}
              </span>
              <div className="min-w-0 flex-1">
                <p className="text-lg font-semibold">
                  {e.serverReceiptNo ?? e.localReceiptNo}{' '}
                  <span className="text-base font-normal text-slate-400">
                    · {formatMoney(e.total)} · {e.paymentType === 'Card' ? 'картка' : 'готівка'} · {e.itemCount} поз.
                  </span>
                </p>
                <p className="text-sm text-slate-500">
                  {new Date(e.createdAt).toLocaleString('uk-UA')}
                  {e.cashierName && ` · касир: ${e.cashierName}`}
                  {e.attempts > 0 && ` · спроб: ${e.attempts}`}
                  {e.serverReceiptNo && ` · лок. ${e.localReceiptNo}`}
                </p>
                {e.totalDifference !== null && Math.abs(e.totalDifference) >= 0.005 && (
                  <p className="mt-1 text-sm text-amber-300">
                    Ціну перераховано сервером: {e.totalDifference > 0 ? '+' : '−'}
                    {formatMoney(Math.abs(e.totalDifference))} (стало {formatMoney(e.serverTotal ?? 0)}).
                  </p>
                )}
                {e.lastError && (
                  <p className={`mt-1 text-sm ${e.status === 'rejected' ? 'text-rose-300' : 'text-amber-300'}`}>{e.lastError}</p>
                )}
              </div>
              <div className="flex shrink-0 gap-2">
                <button className="btn-secondary px-3 py-2 text-base" onClick={() => void reprint(e)}>
                  Друк
                </button>
                {e.status === 'rejected' && (
                  <>
                    <button className="btn-secondary px-3 py-2 text-base" disabled={busy} onClick={() => void retry(e)}>
                      Повторити
                    </button>
                    <button className="btn-danger px-3 py-2 text-base" onClick={() => setConfirmDismiss(e)}>
                      Закрити
                    </button>
                  </>
                )}
              </div>
            </div>
          ))
        )}
      </div>

      {confirmDismiss && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/70">
          <div className="card w-full max-w-md space-y-4">
            <p className="text-xl font-semibold">Закрити відхилений продаж?</p>
            <p className="text-slate-400">
              Чек {confirmDismiss.localReceiptNo} на {formatMoney(confirmDismiss.total)} не потрапив на сервер. Після
              закриття він зникне зі списку, а залишки й виручка не будуть враховані. Переконайтесь, що менеджер
              знає про цю різницю.
            </p>
            <div className="flex gap-3">
              <button className="btn-secondary flex-1" autoFocus onClick={() => setConfirmDismiss(null)}>
                Скасувати
              </button>
              <button className="btn-danger flex-1" onClick={() => void dismiss()}>
                Так, закрити
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
