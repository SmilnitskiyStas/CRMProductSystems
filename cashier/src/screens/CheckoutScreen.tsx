import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { CatalogItem, CatalogStatus, PaymentType, ShiftInfo } from '@shared/types';
import {
  addItem,
  cartTotal,
  isWholeUnit,
  lineTotal,
  removeLine,
  setQuantity,
  type CartLine,
} from '@shared/cart';
import { formatMoney } from '@shared/money';
import { SaleSubmitter, type SubmissionState } from '@shared/saleSubmission';
import { CASHIER_ACTIVITY_EVENT } from '../hooks/useIdleLogout';
import { PaymentDialog } from '../components/PaymentDialog';
import { ReceiptDialog } from '../components/ReceiptDialog';
import { CameraPanel, type ScanOutcome } from '../components/CameraPanel';
import { useScanner } from '../hooks/useScanner';

interface Props {
  shift: ShiftInfo;
  online: boolean;
  onOpenShiftScreen: () => void;
  onOpenQueue: () => void;
  onOpenPrint: () => void;
  onLogout: () => void;
}

const isTextTarget = (t: EventTarget | null) =>
  t instanceof HTMLInputElement || t instanceof HTMLTextAreaElement;

export function CheckoutScreen({ shift, online, onOpenShiftScreen, onOpenQueue, onOpenPrint, onLogout }: Props) {
  const [cart, setCart] = useState<CartLine[]>([]);
  const [selected, setSelected] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<CatalogItem[]>([]);
  const [toast, setToast] = useState<{ text: string; tone: 'info' | 'error' } | null>(null);
  const [catalog, setCatalog] = useState<CatalogStatus | null>(null);
  const [payment, setPayment] = useState<PaymentType | null>(null);
  const [confirmClear, setConfirmClear] = useState(false);
  const [submission, setSubmission] = useState<SubmissionState>({ status: 'idle' });
  const searchRef = useRef<HTMLInputElement>(null);
  const toastTimer = useRef<ReturnType<typeof setTimeout>>();

  const submitter = useMemo(
    () => new SaleSubmitter((req) => window.cashier.submitSale(req), () => crypto.randomUUID(), setSubmission),
    [],
  );

  const total = cartTotal(cart);
  const dialogOpen = payment !== null || submission.status === 'completed' || confirmClear;

  const flash = useCallback((text: string, tone: 'info' | 'error' = 'info') => {
    setToast({ text, tone });
    clearTimeout(toastTimer.current);
    toastTimer.current = setTimeout(() => setToast(null), 3500);
  }, []);

  // Catalog status
  useEffect(() => {
    void window.cashier.catalogStatus().then(setCatalog);
    return window.cashier.onCatalogChange(setCatalog);
  }, []);

  // A changed cart means a different sale: it must not reuse the previous ClientTransactionId.
  const changeCart = useCallback(
    (fn: (c: CartLine[]) => CartLine[]) => {
      submitter.reset();
      setCart(fn);
    },
    [submitter],
  );

  const addScanned = useCallback(
    async (item: CatalogItem, barcode: string): Promise<boolean> => {
      if (item.price == null) {
        flash(`«${item.name}»: ціну не задано, продаж неможливий.`, 'error');
        return false;
      }
      changeCart((c) => addItem(c, item, barcode));
      setSelected(item.id);
      return true;
    },
    [changeCart, flash],
  );

  /** One pipeline for every scanner: USB wedge and the webcam both end up here. */
  const onScan = useCallback(
    async (code: string): Promise<ScanOutcome> => {
      let item = await window.cashier.lookupBarcode(code);
      if (!item && code.length === 12) item = await window.cashier.lookupBarcode('0' + code); // UPC-A stored as EAN-13
      if (!item) {
        flash(`Штрихкод ${code} не знайдено в каталозі.`, 'error');
        return { kind: 'notfound' };
      }
      const added = await addScanned(item, code);
      return added ? { kind: 'added', name: item.name } : { kind: 'noprice', name: item.name };
    },
    [addScanned, flash],
  );

  useScanner(!dialogOpen, (code) => void onScan(code));

  // Webcam scanner (panel visibility and last camera are remembered in config)
  const [cameraOn, setCameraOn] = useState(false);
  const [cameraDevice, setCameraDevice] = useState<string | null>(null);
  const [prefsLoaded, setPrefsLoaded] = useState(false);
  useEffect(() => {
    void window.cashier.getCameraPrefs().then((p) => {
      setCameraOn(p.enabled);
      setCameraDevice(p.deviceId);
      setPrefsLoaded(true);
    });
  }, []);
  const saveCamera = useCallback((enabled: boolean, deviceId: string | null) => {
    void window.cashier.saveCameraPrefs({ enabled, deviceId });
  }, []);
  const toggleCamera = useCallback(() => {
    setCameraOn((on) => {
      saveCamera(!on, cameraDevice);
      return !on;
    });
  }, [saveCamera, cameraDevice]);
  const onCameraCode = useCallback(
    async (code: string): Promise<ScanOutcome> => {
      window.dispatchEvent(new Event(CASHIER_ACTIVITY_EVENT)); // a camera scan counts as activity
      const out = await onScan(code);
      if (out.kind === 'added') flash(`Додано: ${out.name}`);
      return out;
    },
    [onScan, flash],
  );

  // Manual search (debounced)
  useEffect(() => {
    const q = query.trim();
    if (!q) {
      setResults([]);
      return;
    }
    const t = setTimeout(() => void window.cashier.searchCatalog(q).then(setResults), 120);
    return () => clearTimeout(t);
  }, [query]);

  const pickResult = async (item: CatalogItem) => {
    await addScanned(item, item.barcodes[0] ?? '');
    setQuery('');
    setResults([]);
  };

  const onSearchEnter = async () => {
    const q = query.trim();
    if (!q) return;
    const exact = await window.cashier.lookupBarcode(q);
    const item = exact ?? results[0];
    if (!item) return flash('Нічого не знайдено.', 'error');
    await addScanned(item, exact ? q : (item.barcodes[0] ?? ''));
    setQuery('');
    setResults([]);
  };

  const openPayment = useCallback(
    (type: PaymentType) => {
      if (cart.length === 0) return flash('Кошик порожній.', 'error');
      setPayment(type);
    },
    [cart.length, flash],
  );

  const closePayment = () => {
    if (submission.status !== 'pending') {
      // keep the id only while retrying an uncertain result; leaving the dialog starts fresh
      if (submission.status !== 'completed') submitter.reset();
      setPayment(null);
    }
  };

  const confirmPayment = (type: PaymentType, paymentAmount: number) => {
    void submitter.submit({
      shiftId: shift.shiftId,
      items: cart.map((l) => ({ barcode: l.barcode, quantity: l.quantity })),
      paymentType: type,
      paymentAmount,
    });
  };

  const finishSale = useCallback(() => {
    submitter.reset();
    setCart([]);
    setSelected(null);
    setPayment(null);
    searchRef.current?.focus();
  }, [submitter]);

  // Hotkeys
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (dialogOpen) return;
      const inText = isTextTarget(e.target);
      switch (e.key) {
        case 'F2':
          e.preventDefault();
          searchRef.current?.focus();
          searchRef.current?.select();
          return;
        case 'F3':
          e.preventDefault();
          return toggleCamera();
        case 'F5':
          e.preventDefault();
          return openPayment('Cash');
        case 'F6':
          e.preventDefault();
          return openPayment('Card');
        case 'F8':
          e.preventDefault();
          if (cart.length) setConfirmClear(true);
          return;
        case 'F9':
          e.preventDefault();
          return onOpenShiftScreen();
        case 'F10':
          e.preventDefault();
          return onOpenQueue();
        case 'F12':
          e.preventDefault();
          return onLogout();
      }
      if (inText) return;
      const idx = cart.findIndex((l) => l.itemId === selected);
      if (e.key === 'ArrowDown' && cart.length) setSelected(cart[Math.min(idx + 1, cart.length - 1)].itemId);
      else if (e.key === 'ArrowUp' && cart.length) setSelected(cart[Math.max(idx - 1, 0)].itemId);
      else if (selected && (e.key === '+' || e.key === '=')) {
        const l = cart[idx];
        if (l) changeCart((c) => setQuantity(c, l.itemId, l.quantity + 1));
      } else if (selected && e.key === '-') {
        const l = cart[idx];
        if (l) changeCart((c) => setQuantity(c, l.itemId, l.quantity - 1));
      } else if (selected && e.key === 'Delete') changeCart((c) => removeLine(c, selected));
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [dialogOpen, cart, selected, openPayment, onOpenShiftScreen, onOpenQueue, onLogout, changeCart, toggleCamera]);

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {/* header */}
      <header className="flex items-center gap-3 border-b border-slate-800 px-5 py-3">
        <div className="relative flex-1">
          <input
            ref={searchRef}
            className="field"
            placeholder="Пошук за назвою або штрихкодом (F2) · скануйте товар у будь-який момент"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && void onSearchEnter()}
            autoFocus
          />
          {results.length > 0 && (
            <div className="absolute left-0 right-0 top-full z-30 mt-1 max-h-96 overflow-auto rounded-xl border border-slate-700 bg-slate-900 shadow-2xl">
              {results.map((r) => (
                <button
                  key={r.id}
                  onClick={() => void pickResult(r)}
                  className="flex w-full items-center justify-between gap-4 px-4 py-3 text-left hover:bg-slate-800"
                >
                  <span className="truncate">{r.name}</span>
                  <span className="shrink-0 font-semibold text-brand-400">
                    {r.price != null ? formatMoney(r.price) : 'без ціни'}
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>
        <button className="btn-secondary px-4 py-2 text-base" onClick={() => void window.cashier.syncCatalog()}>
          {catalog?.syncing ? 'Синхронізація…' : `Каталог: ${catalog?.count ?? 0}`}
        </button>
        <button
          className={`px-4 py-2 text-base ${cameraOn ? 'btn-primary' : 'btn-secondary'}`}
          onClick={toggleCamera}
          title="Сканувати штрихкод веб-камерою"
        >
          Камера (F3)
        </button>
        <button className="btn-secondary px-4 py-2 text-base" onClick={onOpenQueue}>
          Черга (F10)
        </button>
        <button className="btn-secondary px-4 py-2 text-base" onClick={onOpenPrint} title="Налаштування">
          Друк
        </button>
        <button className="btn-secondary px-4 py-2 text-base" onClick={onOpenShiftScreen}>
          Зміна (F9)
        </button>
        <button className="btn-secondary px-4 py-2 text-base" onClick={onLogout}>
          Вийти (F12)
        </button>
      </header>

      {catalog?.error && (
        <p className="bg-amber-500/10 px-5 py-2 text-sm text-amber-300">
          Каталог не оновлено: {catalog.error}. Використовується локальна копія ({catalog.count} товарів).
        </p>
      )}
      {!online && (
        <p className="bg-amber-500/10 px-5 py-2 text-sm text-amber-300">
          Офлайн-режим: продажі зберігаються в локальній черзі й відправляться автоматично, коли зв’язок відновиться. Ціни - з локального каталогу.
        </p>
      )}

      {/* body */}
      <div className="flex min-h-0 flex-1">
        <section className="flex min-w-0 flex-1 flex-col">
          <div className="grid grid-cols-[1fr_9rem_8rem_8rem_3rem] gap-3 border-b border-slate-800 px-5 py-2 text-sm text-slate-500">
            <span>Товар</span>
            <span className="text-center">Кількість</span>
            <span className="text-right">Ціна</span>
            <span className="text-right">Сума</span>
            <span />
          </div>
          <div className="min-h-0 flex-1 overflow-auto">
            {cart.length === 0 ? (
              <div className="flex h-full items-center justify-center text-xl text-slate-600">
                Скануйте товар або скористайтесь пошуком
              </div>
            ) : (
              cart.map((l) => (
                <div
                  key={l.itemId}
                  onClick={() => setSelected(l.itemId)}
                  className={`grid cursor-pointer grid-cols-[1fr_9rem_8rem_8rem_3rem] items-center gap-3 border-b border-slate-800/60 px-5 py-3 text-lg ${
                    selected === l.itemId ? 'bg-brand-500/10' : 'hover:bg-slate-900'
                  }`}
                >
                  <span className="truncate">{l.name}</span>
                  <span className="flex items-center justify-center gap-1">
                    <button
                      className="h-10 w-10 rounded-lg bg-slate-800 text-2xl hover:bg-slate-700"
                      onClick={(e) => {
                        e.stopPropagation();
                        changeCart((c) => setQuantity(c, l.itemId, l.quantity - 1));
                      }}
                    >
                      −
                    </button>
                    <input
                      key={l.quantity}
                      className="h-10 w-16 rounded-lg bg-slate-950 text-center"
                      inputMode="decimal"
                      defaultValue={l.quantity}
                      onClick={(e) => e.stopPropagation()}
                      onBlur={(e) => {
                        const n = Number(e.target.value.replace(',', '.'));
                        if (Number.isFinite(n) && n !== l.quantity) changeCart((c) => setQuantity(c, l.itemId, n));
                      }}
                      onKeyDown={(e) => e.key === 'Enter' && (e.target as HTMLInputElement).blur()}
                      title={isWholeUnit(l.unit) ? 'шт.' : l.unit}
                    />
                    <button
                      className="h-10 w-10 rounded-lg bg-slate-800 text-2xl hover:bg-slate-700"
                      onClick={(e) => {
                        e.stopPropagation();
                        changeCart((c) => setQuantity(c, l.itemId, l.quantity + 1));
                      }}
                    >
                      +
                    </button>
                  </span>
                  <span className="text-right text-slate-400">{formatMoney(l.price)}</span>
                  <span className="text-right font-semibold">{formatMoney(lineTotal(l))}</span>
                  <button
                    className="h-10 w-10 rounded-lg text-xl text-rose-400 hover:bg-rose-500/10"
                    title="Видалити (Del)"
                    onClick={(e) => {
                      e.stopPropagation();
                      changeCart((c) => removeLine(c, l.itemId));
                    }}
                  >
                    ✕
                  </button>
                </div>
              ))
            )}
          </div>
          {toast && (
            <div
              className={`mx-5 mb-3 rounded-xl px-4 py-3 ${
                toast.tone === 'error' ? 'bg-rose-500/15 text-rose-300' : 'bg-slate-800 text-slate-200'
              }`}
            >
              {toast.text}
            </div>
          )}
        </section>

        {/* totals + actions */}
        <aside className="flex w-80 shrink-0 flex-col gap-4 overflow-auto border-l border-slate-800 bg-slate-900/50 p-5">
          {cameraOn && prefsLoaded && (
            <CameraPanel
              initialDeviceId={cameraDevice}
              paused={dialogOpen}
              onCode={onCameraCode}
              onDeviceChange={(id) => {
                setCameraDevice(id);
                saveCamera(true, id);
              }}
              onClose={toggleCamera}
            />
          )}
          <div>
            <p className="text-slate-400">Позицій: {cart.length}</p>
            <p className="mt-1 text-slate-400">До сплати</p>
            <p className="text-5xl font-extrabold text-brand-400">{formatMoney(total)}</p>
          </div>
          <button className="btn-primary py-5 text-2xl" disabled={!cart.length} onClick={() => openPayment('Cash')}>
            Готівка (F5)
          </button>
          <button className="btn-secondary py-5 text-2xl" disabled={!cart.length} onClick={() => openPayment('Card')}>
            Картка (F6)
          </button>
          <button className="btn-secondary" disabled={!cart.length} onClick={() => setConfirmClear(true)}>
            Очистити (F8)
          </button>
          <div className="mt-auto space-y-1 text-sm text-slate-500">
            <p>Зміна №{shift.shiftNumber || '—'}</p>
            <p>↑↓ вибір · +/− кількість · Del видалити</p>
            <p>F2 пошук · F3 камера · F5/F6 оплата · F8 очистити · F9 зміна · F10 черга · F12 вийти</p>
          </div>
        </aside>
      </div>

      {payment && submission.status !== 'completed' && (
        <PaymentDialog
          total={total}
          initialType={payment}
          submission={submission}
          onCancel={closePayment}
          onConfirm={confirmPayment}
        />
      )}
      {submission.status === 'completed' && submission.sale && (
        <ReceiptDialog sale={submission.sale} onDone={finishSale} />
      )}
      {confirmClear && (
        <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/70">
          <div className="card w-full max-w-sm space-y-4 text-center">
            <p className="text-xl">Очистити кошик?</p>
            <div className="flex gap-3">
              <button className="btn-secondary flex-1" onClick={() => setConfirmClear(false)} autoFocus>
                Ні
              </button>
              <button
                className="btn-danger flex-1"
                onClick={() => {
                  changeCart(() => []);
                  setSelected(null);
                  setConfirmClear(false);
                }}
              >
                Так
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
