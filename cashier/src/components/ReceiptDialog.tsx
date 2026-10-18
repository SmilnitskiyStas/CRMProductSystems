import { useEffect } from 'react';
import type { SaleReceipt } from '@shared/types';
import { formatMoney } from '@shared/money';

export function ReceiptDialog({ sale, onDone }: { sale: SaleReceipt; onDone: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Enter' || e.key === 'Escape') onDone();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onDone]);

  const fiscalized = sale.fiscalStatus === 'fiscalized';
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 p-6">
      <div className="card w-full max-w-xl space-y-4">
        <div className="text-center">
          <div className="mx-auto mb-3 flex h-16 w-16 items-center justify-center rounded-full bg-brand-500/20 text-3xl text-brand-400">
            ✓
          </div>
          <h2 className="text-2xl font-bold">Продаж проведено</h2>
          <p className="text-slate-400">Чек {sale.receiptNumber}</p>
        </div>

        <div className="max-h-40 overflow-auto rounded-xl bg-slate-800/60 px-4 py-2 text-sm">
          {sale.items.map((i, idx) => (
            <div key={idx} className="flex justify-between py-1">
              <span className="truncate pr-3">
                {i.name} × {i.quantity}
              </span>
              <span>{formatMoney(i.total)}</span>
            </div>
          ))}
        </div>

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
            : 'Фіскалізація очікує - чек буде проведено автоматично.'}
        </p>

        <button className="btn-primary w-full" autoFocus onClick={onDone}>
          Новий чек (Enter)
        </button>
      </div>
    </div>
  );
}
