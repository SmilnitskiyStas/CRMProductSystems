import { useState } from 'react';
import type { ShiftInfo } from '@shared/types';
import { formatMoney } from '@shared/money';

interface Props {
  shift: ShiftInfo | null;
  onShiftChange: (shift: ShiftInfo | null) => void;
  online: boolean;
  onBack?: () => void;
  onOpenQueue: () => void;
  onOpenPrint: () => void;
  onLogout: () => void;
}

function parseAmount(raw: string): number | null {
  const n = Number(raw.replace(',', '.').trim());
  return Number.isFinite(n) && n >= 0 ? Math.round(n * 100) / 100 : null;
}

const Row = ({ label, value, tone }: { label: string; value: string; tone?: string }) => (
  <div className="flex justify-between border-b border-slate-800 py-3 text-lg last:border-0">
    <span className="text-slate-400">{label}</span>
    <span className={`font-semibold ${tone ?? ''}`}>{value}</span>
  </div>
);

export function ShiftScreen({ shift, online, onShiftChange, onBack, onOpenQueue, onOpenPrint, onLogout }: Props) {
  const [cash, setCash] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [report, setReport] = useState<ShiftInfo | null>(null);

  const amount = parseAmount(cash);

  const open = async () => {
    if (amount === null) return;
    setBusy(true);
    setError(null);
    const res = await window.cashier.openShift(amount);
    setBusy(false);
    if (res.ok) {
      setReport(null);
      setCash('');
      onShiftChange(res.data);
    } else setError(res.error.message);
  };

  const close = async () => {
    if (amount === null) return;
    setBusy(true);
    setError(null);
    const res = await window.cashier.closeShift(amount);
    setBusy(false);
    if (res.ok) {
      setReport(res.data);
      setCash('');
      onShiftChange(null);
    } else setError(res.error.message);
  };

  const onKey = (e: React.KeyboardEvent, action: () => void) => {
    if (e.key === 'Enter') action();
  };

  // Z-report summary after closing
  if (!shift && report) {
    const disc = report.cashDiscrepancy;
    return (
      <div className="flex flex-1 items-center justify-center p-8">
        <div className="card w-full max-w-xl">
          <h1 className="text-2xl font-bold">Зміну закрито · Z-звіт</h1>
          <p className="mb-4 text-slate-400">Зміна №{report.shiftNumber || '—'}</p>
          <Row label="Відкрито" value={new Date(report.openedAt).toLocaleString('uk-UA')} />
          {report.closedAt && <Row label="Закрито" value={new Date(report.closedAt).toLocaleString('uk-UA')} />}
          <Row label="Початкова готівка" value={formatMoney(report.openingCash ?? 0)} />
          <Row label="Обіг за зміну" value={formatMoney(report.totalSales)} />
          <Row label="Очікувана готівка" value={report.expectedCashAmount != null ? formatMoney(report.expectedCashAmount) : '—'} />
          <Row label="Фактична готівка" value={report.closingCash != null ? formatMoney(report.closingCash) : '—'} />
          <Row
            label="Розбіжність"
            value={disc == null ? '—' : `${disc > 0 ? '+' : ''}${formatMoney(disc)}`}
            tone={disc == null || disc === 0 ? '' : disc > 0 ? 'text-amber-300' : 'text-rose-400'}
          />
          <div className="mt-6 flex gap-3">
            <button className="btn-primary flex-1" onClick={() => setReport(null)}>
              Нова зміна
            </button>
            <button className="btn-secondary flex-1" onClick={onLogout}>
              Вийти
            </button>
          </div>
        </div>
      </div>
    );
  }

  const isOpen = shift !== null;
  return (
    <div className="flex flex-1 items-center justify-center p-8">
      <div className="card w-full max-w-xl space-y-5">
        <h1 className="text-2xl font-bold">{isOpen ? 'Закриття зміни' : 'Відкриття зміни'}</h1>
        {isOpen && (
          <div>
            <Row label="Зміна" value={`№${shift.shiftNumber || '—'}`} />
            <Row label="Відкрито" value={new Date(shift.openedAt).toLocaleString('uk-UA')} />
            <Row label="Початкова готівка" value={formatMoney(shift.openingCash ?? 0)} />
            <Row label="Обіг за зміну" value={formatMoney(shift.totalSales)} />
          </div>
        )}
        <label className="block space-y-2">
          <span className="text-sm text-slate-400">
            {isOpen ? 'Фактична готівка в касі, ₴' : 'Початкова готівка в касі, ₴'}
          </span>
          <input
            className="field text-right text-3xl font-bold"
            inputMode="decimal"
            value={cash}
            onChange={(e) => setCash(e.target.value)}
            onKeyDown={(e) => onKey(e, isOpen ? close : open)}
            placeholder="0.00"
            autoFocus
          />
        </label>
        {!online && (
          <p className="rounded-lg bg-amber-500/10 px-4 py-3 text-amber-300">
            Немає зв’язку з сервером: відкриття та закриття зміни потребують мережі.
          </p>
        )}
        {error && (
          <div className="rounded-lg bg-rose-500/10 px-4 py-3 text-rose-300">
            <p>{error}</p>
            <button className="mt-2 underline" onClick={onOpenQueue}>
              Відкрити чергу
            </button>
          </div>
        )}
        <div className="flex gap-3">
          {isOpen ? (
            <>
              {onBack && (
                <button className="btn-secondary flex-1" onClick={onBack}>
                  Назад
                </button>
              )}
              <button className="btn-danger flex-1" disabled={busy || amount === null || !online} onClick={close}>
                {busy ? 'Закриття…' : 'Закрити зміну'}
              </button>
            </>
          ) : (
            <>
              <button className="btn-secondary" onClick={onLogout}>
                Вийти
              </button>
              <button className="btn-primary flex-1" disabled={busy || amount === null || !online} onClick={open}>
                {busy ? 'Відкриття…' : 'Відкрити зміну'}
              </button>
            </>
          )}
        </div>
        <div className="flex gap-4 text-sm text-slate-400">
          <button className="underline" onClick={onOpenQueue}>
            Черга продажів
          </button>
          <button className="underline" onClick={onOpenPrint}>
            Налаштування друку
          </button>
        </div>
      </div>
    </div>
  );
}
