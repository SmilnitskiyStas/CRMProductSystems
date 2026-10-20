import { useEffect, useRef, useState } from 'react';

type IdType = 'phone' | 'card' | 'account';

interface Props {
  online: boolean;
  busy: boolean;
  error: string | null;
  cameraOn: boolean;
  /** Manual entry by phone / card / account number (resolve-identifier). */
  onResolveIdentifier: (type: IdType, value: string) => void;
  onToggleCamera: () => void;
  onClose: () => void;
}

const LABELS: Record<IdType, string> = { phone: 'Телефон', card: 'Номер картки', account: 'Номер рахунку' };

/**
 * "Клієнт" panel: while it is open the NEXT scan (USB wedge or camera) is routed to the loyalty
 * resolve instead of the catalog lookup. Manual entry covers customers who cannot show a QR.
 */
export function CustomerPanel({ online, busy, error, cameraOn, onResolveIdentifier, onToggleCamera, onClose }: Props) {
  const [type, setType] = useState<IdType>('phone');
  const [value, setValue] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const submit = () => {
    if (busy || !online || value.trim().length < 3) return;
    onResolveIdentifier(type, value);
  };

  return (
    <div className="border-b border-slate-800 bg-sky-500/10 px-5 py-3">
      <div className="flex flex-wrap items-center gap-3">
        <span className="font-semibold text-sky-300">Скануйте картку/QR клієнта</span>
        <span className="text-sm text-slate-400">наступне сканування (сканер або камера) піде на ідентифікацію клієнта</span>
        <button className="btn-secondary ml-auto px-3 py-1.5 text-sm" onClick={onToggleCamera}>
          {cameraOn ? 'Камера увімкнена' : 'Сканувати камерою'}
        </button>
        <button className="btn-secondary px-3 py-1.5 text-sm" onClick={onClose}>
          Скасувати (Esc)
        </button>
      </div>

      {!online ? (
        <p className="mt-2 rounded-lg bg-amber-500/10 px-4 py-2 text-amber-300">
          Немає зв’язку — клієнта можна ідентифікувати лише онлайн. Продаж можна провести без клієнта.
        </p>
      ) : (
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <select className="field w-44 py-2 text-base" value={type} onChange={(e) => setType(e.target.value as IdType)}>
            {(Object.keys(LABELS) as IdType[]).map((k) => (
              <option key={k} value={k}>
                {LABELS[k]}
              </option>
            ))}
          </select>
          <input
            ref={inputRef}
            className="field w-64 py-2 text-base"
            placeholder={type === 'phone' ? '+380…' : 'введіть номер'}
            value={value}
            inputMode="tel"
            onChange={(e) => setValue(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
          />
          <button className="btn-primary px-4 py-2 text-base" disabled={busy || value.trim().length < 3} onClick={submit}>
            {busy ? 'Пошук…' : 'Знайти'}
          </button>
        </div>
      )}
      {error && <p className="mt-2 rounded-lg bg-rose-500/10 px-4 py-2 text-rose-300">{error}</p>}
    </div>
  );
}
