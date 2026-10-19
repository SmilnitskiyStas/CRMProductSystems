import { useEffect, useMemo, useRef, useState } from 'react';
import type { PaymentType } from '@shared/types';
import type { SubmissionState } from '@shared/saleSubmission';
import { computeChange, quickCashAmounts } from '@shared/cart';
import { formatMoney } from '@shared/money';

interface Props {
  total: number;
  initialType: PaymentType;
  submission: SubmissionState;
  onCancel: () => void;
  /** paymentAmount = tendered cash, or the total for card. */
  onConfirm: (type: PaymentType, paymentAmount: number) => void;
}

function parseAmount(raw: string): number {
  const n = Number(raw.replace(',', '.'));
  return Number.isFinite(n) ? n : 0;
}

export function PaymentDialog({ total, initialType, submission, onCancel, onConfirm }: Props) {
  const [type, setType] = useState<PaymentType>(initialType);
  const [tendered, setTendered] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);
  const pending = submission.status === 'pending';
  const retryable = submission.status === 'uncertain' || submission.status === 'failed';

  const amount = parseAmount(tendered);
  const calc = useMemo(() => computeChange(total, amount), [total, amount]);
  const canPay = type === 'Card' || calc.enough;

  useEffect(() => {
    if (type === 'Cash') inputRef.current?.focus();
  }, [type]);

  const confirm = () => {
    if (pending || !canPay) return;
    onConfirm(type, type === 'Card' ? total : amount);
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !pending) onCancel();
      if (e.key === 'F5') setType('Cash');
      if (e.key === 'F6') setType('Card');
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onCancel, pending]);

  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/70 p-6">
      <div className="card w-full max-w-xl space-y-5">
        <div className="text-center">
          <p className="text-slate-400">До сплати</p>
          <p className="text-5xl font-extrabold text-brand-400">{formatMoney(total)}</p>
        </div>

        <div className="grid grid-cols-2 gap-3">
          {(['Cash', 'Card'] as const).map((t) => (
            <button
              key={t}
              disabled={pending}
              onClick={() => setType(t)}
              className={`btn ${type === t ? 'bg-brand-500 text-slate-950' : 'bg-slate-800 text-slate-200 hover:bg-slate-700'}`}
            >
              {t === 'Cash' ? 'Готівка (F5)' : 'Картка (F6)'}
            </button>
          ))}
        </div>

        {type === 'Cash' ? (
          <div className="space-y-3">
            <input
              ref={inputRef}
              className="field text-right text-3xl font-bold"
              inputMode="decimal"
              placeholder="Отримано від клієнта"
              value={tendered}
              disabled={pending}
              onChange={(e) => setTendered(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && confirm()}
            />
            <div className="flex flex-wrap gap-2">
              {quickCashAmounts(total).map((a) => (
                <button key={a} className="btn-secondary px-4 py-2" disabled={pending} onClick={() => setTendered(String(a))}>
                  {a === total ? 'Без решти' : formatMoney(a)}
                </button>
              ))}
            </div>
            <div className="flex justify-between rounded-xl bg-slate-800 px-5 py-4 text-2xl">
              <span className="text-slate-400">{calc.enough || !tendered ? 'Решта' : 'Не вистачає'}</span>
              <span className={`font-bold ${calc.enough || !tendered ? 'text-emerald-400' : 'text-rose-400'}`}>
                {tendered ? formatMoney(calc.enough ? calc.change : calc.shortfall) : formatMoney(0)}
              </span>
            </div>
          </div>
        ) : (
          <p className="rounded-xl bg-slate-800 px-5 py-4 text-center text-slate-300">
            Проведіть оплату на банківському терміналі та підтвердіть. (Інтеграція з терміналом - у наступній версії.)
          </p>
        )}

        {submission.message && (
          <p
            className={`rounded-lg px-4 py-3 ${
              submission.status === 'uncertain' ? 'bg-amber-500/10 text-amber-300' : 'bg-rose-500/10 text-rose-300'
            }`}
          >
            {submission.message}
          </p>
        )}

        <div className="flex gap-3">
          <button className="btn-secondary" disabled={pending} onClick={onCancel}>
            {submission.status === 'conflict' || submission.status === 'locked' ? 'До кошика' : 'Скасувати'}
          </button>
          {submission.status !== 'conflict' && submission.status !== 'locked' && (
            <button className="btn-primary flex-1" disabled={pending || !canPay} onClick={confirm}>
              {pending ? 'Проведення…' : retryable ? 'Повторити' : 'Підтвердити оплату'}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
