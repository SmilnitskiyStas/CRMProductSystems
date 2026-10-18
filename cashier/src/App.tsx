import { useCallback, useEffect, useState } from 'react';
import type { AppState, OperatorInfo, QueueSummary, RegisterInfo, ShiftInfo } from '@shared/types';
import { StatusBar } from './components/StatusBar';
import { useIdleLogout } from './hooks/useIdleLogout';
import { ActivationScreen } from './screens/ActivationScreen';
import { CheckoutScreen } from './screens/CheckoutScreen';
import { PinScreen } from './screens/PinScreen';
import { PrintSettingsScreen } from './screens/PrintSettingsScreen';
import { QueueScreen } from './screens/QueueScreen';
import { ShiftScreen } from './screens/ShiftScreen';

const IDLE_LOGOUT_MS = 5 * 60 * 1000;

type View = 'checkout' | 'shift' | 'queue' | 'print';

export function App() {
  const [state, setState] = useState<AppState | null>(null);
  const [shift, setShift] = useState<ShiftInfo | null>(null);
  const [shiftLoaded, setShiftLoaded] = useState(false);
  const [view, setView] = useState<View>('checkout');
  const [notice, setNotice] = useState<string | null>(null);

  const refreshState = useCallback(async () => setState(await window.cashier.getState()), []);

  useEffect(() => {
    void refreshState();
    const offOnline = window.cashier.onOnlineChange((online) => setState((s) => (s ? { ...s, online } : s)));
    const offQueue = window.cashier.onQueueChange((queue: QueueSummary) =>
      setState((s) => (s ? { ...s, queue } : s)),
    );
    const offExpired = window.cashier.onSessionExpired(() => {
      setNotice('Сесію завершено. Увійдіть за PIN-кодом знову.');
      setShift(null);
      setShiftLoaded(false);
      void refreshState();
    });
    return () => {
      offOnline();
      offQueue();
      offExpired();
    };
  }, [refreshState]);

  const operator = state?.operator ?? null;
  const operatorSession = operator?.sessionId;

  // Load the register's current shift after sign-in (cached copy when the server is unreachable).
  useEffect(() => {
    if (!operatorSession) return;
    let cancelled = false;
    void window.cashier.getCurrentShift().then((res) => {
      if (cancelled) return;
      if (res.ok) {
        setShift(res.data && res.data.status !== 'Closed' ? res.data : null);
        setView(res.data && res.data.status !== 'Closed' ? 'checkout' : 'shift');
      } else {
        setView('shift');
      }
      setShiftLoaded(true);
    });
    return () => {
      cancelled = true;
    };
  }, [operatorSession]);

  const logout = useCallback(
    async (reason: 'logout' | 'idle') => {
      await window.cashier.logout(reason);
      setShift(null);
      setShiftLoaded(false);
      setView('checkout');
      if (reason === 'idle') setNotice('Автоматичний вихід через неактивність.');
      await refreshState();
    },
    [refreshState],
  );

  useIdleLogout(operator !== null, IDLE_LOGOUT_MS, () => void logout('idle'));

  if (!state) return <div className="flex h-full items-center justify-center text-slate-500">Завантаження…</div>;

  const onActivated = (_register: RegisterInfo) => void refreshState();
  const onLoggedIn = (_op: OperatorInfo) => {
    setNotice(null);
    void refreshState();
  };
  const goCheckout = () => setView('checkout');
  const goBack = shift ? goCheckout : () => setView('shift');

  let body;
  if (!state.register) {
    body = <ActivationScreen defaultServerUrl={state.serverUrl} onActivated={onActivated} />;
  } else if (!operator) {
    body = <PinScreen register={state.register} notice={notice} onLoggedIn={onLoggedIn} />;
  } else if (!shiftLoaded) {
    body = <div className="flex flex-1 items-center justify-center text-slate-500">Завантаження зміни…</div>;
  } else if (view === 'queue') {
    body = <QueueScreen onBack={goBack} />;
  } else if (view === 'print') {
    body = <PrintSettingsScreen onBack={goBack} />;
  } else if (view === 'shift' || !shift) {
    body = (
      <ShiftScreen
        shift={shift}
        online={state.online}
        onShiftChange={(s) => {
          const open = s && s.status !== 'Closed' ? s : null;
          setShift(open);
          // A freshly opened shift goes straight to the cashier workplace; closing keeps the
          // shift screen so the Z-report stays visible.
          if (open) setView('checkout');
        }}
        onBack={shift ? goCheckout : undefined}
        onOpenQueue={() => setView('queue')}
        onOpenPrint={() => setView('print')}
        onLogout={() => void logout('logout')}
      />
    );
  } else {
    body = (
      <CheckoutScreen
        shift={shift}
        online={state.online}
        onOpenShiftScreen={() => setView('shift')}
        onOpenQueue={() => setView('queue')}
        onOpenPrint={() => setView('print')}
        onLogout={() => void logout('logout')}
      />
    );
  }

  return (
    <div className="flex h-full flex-col bg-slate-950">
      {operator && state.queue.rejected > 0 && view !== 'queue' && (
        <button
          onClick={() => setView('queue')}
          className="bg-rose-600 px-5 py-2 text-left text-base font-semibold text-white hover:bg-rose-500"
        >
          Є відхилені продажі ({state.queue.rejected}) - потрібна увага. Натисніть, щоб відкрити чергу.
        </button>
      )}
      {!operator && state.register && state.queue.pending > 0 && (
        <p className="bg-amber-500/20 px-5 py-2 text-amber-200">
          У черзі є невідправлені продажі ({state.queue.pending}). Увійдіть за PIN-кодом, щоб їх відправити.
        </p>
      )}
      <div className="flex min-h-0 flex-1 flex-col">{body}</div>
      <StatusBar state={state} />
    </div>
  );
}
