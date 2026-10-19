import { useEffect, useState } from 'react';
import type { PrintResult, SaleReceipt } from '@shared/types';
import { formatMoney } from '@shared/money';

export function ReceiptDialog({ sale, onDone }: { sale: SaleReceipt; onDone: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Enter' || e.key === 'Escape') onDone();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onDone]);

  const [printError, setPrintError] = useState<string | null>(null);
  const [printing, setPrinting] = useState(false);
  const fiscalized = sale.fiscalStatus === 'fiscalized';
  const repriced = sale.localTotal !== undefined && Math.abs(sale.localTotal - sale.subtotal) >= 0.005;

  useEffect(
    () =>
      window.cashier.onPrintResult((r: PrintResult) => {
        if (r.clientTransactionId === sale.clientTransactionId) setPrintError(r.ok ? null : (r.error ?? 'Помилка друку'));
      }),
    [sale.clientTransactionId],
  );

  const reprint = async () => {
    if (!sale.clientTransactionId) return;
    setPrinting(true);
    const r = await window.cashier.printReceipt(sale.clientTransactionId);
    setPrinting(false);
    setPrintError(r.ok ? null : (r.error ?? 'Помилка друку'));
  };
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-6">
      <div className="card w-full max-w-xl space-y-4">
        <div className="text-center">
          <div className="mx-auto mb-3 flex h-16 w-16 items-center justify-center rounded-full bg-brand-500/20 text-3xl text-brand-400">
            ✓
          </div>
          <h2 className="text-2xl font-bold">Продаж проведено</h2>
          <p className="text-slate-400">
            Чек {sale.receiptNumber}
            {sale.local ? ' (локальний номер, очікує відправки)' : ''}
          </p>
        </div>

        <div className="max-h-40 overflow-auto rounded-xl bg-slate-800/60 px-4 py-2 text-sm">
          {sale.items.map((i, idx) => (
            <div key={idx} className="flex justify-between py-1">
              <span className="truncate pr-3">
                {i.name} × {i.quantity}
                {i.quantity !== 1 || i.promoLabel ? ` (${formatMoney(i.unitPrice)})` : ''}
                {i.promoLabel && <span className="ml-2 rounded-md bg-amber-500/20 px-2 py-0.5 text-xs text-amber-300">{i.promoLabel}</span>}
              </span>
              <span>{formatMoney(i.total)}</span>
            </div>
          ))}
        </div>
        {repriced && (
          <p className="rounded-lg bg-amber-500/10 px-4 py-3 text-amber-300">
            Ціну перераховано сервером: було {formatMoney(sale.localTotal!)}, стало {formatMoney(sale.subtotal)}.
          </p>
        )}

        <div className="space-y-1 text-lg">
          <div className="flex justify-between">
            <span className="text-slate-400">Сума</span>
            <span className="font-bold">{formatMoney(sale.subtotal)}</span>
          </div>
          <div className="flex justify-between">
            <span className="text-slate-400">{sale.paymentType.toLowerCase() === 'card' ? 'Картка' : 'Отримано'}</span>
            <span>{formatMoney(sale.paymentAmount)}</span>
          </div>
          {sale.change > 0 && (
            <div className="flex justify-between text-3xl font-extrabold text-emerald-400">
              <span>Решта</span>
              <span>{formatMoney(sale.change)}</span>
            </div>
          )}
        </div>

        <p className={`text-sm ${fiscalized ? 'text-emerald-400' : 'text-amber-300'}`}>
          {fiscalized
            ? `Фіскалізовано${sale.fiscalNumber ? ` · ${sale.fiscalNumber}` : ''}`
            : sale.local
              ? 'Чек не фіскалізовано (офлайн). Фіскалізація та відправка - після синхронізації.'
              : 'Фіскалізація очікує - чек буде проведено автоматично.'}
        </p>

        {printError && (
          <div className="flex items-center justify-between gap-3 rounded-lg bg-amber-500/10 px-4 py-3 text-amber-300">
            <span>Чек не надруковано: {printError}. Продаж збережено.</span>
            <button className="btn-secondary shrink-0 px-4 py-2 text-base" disabled={printing} onClick={() => void reprint()}>
              Повторити друк
            </button>
          </div>
        )}

        <button className="btn-primary w-full" autoFocus onClick={onDone}>
          Новий чек (Enter)
        </button>
      </div>
    </div>
  );
}
