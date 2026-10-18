import { useCallback, useEffect, useState } from 'react';
import type { AppState, OperatorInfo, RegisterInfo, ShiftInfo } from '@shared/types';
import { StatusBar } from './components/StatusBar';
import { useIdleLogout } from './hooks/useIdleLogout';
import { ActivationScreen } from './screens/ActivationScreen';
import { CheckoutScreen } from './screens/CheckoutScreen';
import { PinScreen } from './screens/PinScreen';
import { ShiftScreen } from './screens/ShiftScreen';

const IDLE_LOGOUT_MS = 5 * 60 * 1000;

type View = 'checkout' | 'shift';

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
    const offExpired = window.cashier.onSessionExpired(() => {
      setNotice('Сесію завершено. Увійдіть за PIN-кодом знову.');
      setShift(null);
      setShiftLoaded(false);
      void refreshState();
    });
    return () => {
      offOnline();
      offExpired();
    };
  }, [refreshState]);

  const operator = state?.operator ?? null;
  const operatorSession = operator?.sessionId;

  // Load the register's current shift after sign-in.
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

  let body;
  if (!state.register) {
    body = <ActivationScreen defaultServerUrl={state.serverUrl} onActivated={onActivated} />;
  } else if (!operator) {
    body = <PinScreen register={state.register} notice={notice} onLoggedIn={onLoggedIn} />;
  } else if (!shiftLoaded) {
    body = <div className="flex flex-1 items-center justify-center text-slate-500">Завантаження зміни…</div>;
  } else if (view === 'shift' || !shift) {
    body = (
      <ShiftScreen
        shift={shift}
        onShiftChange={(s) => {
          setShift(s && s.status !== 'Closed' ? s : null);
        }}
        onBack={shift ? () => setView('checkout') : undefined}
        onLogout={() => void logout('logout')}
      />
    );
  } else {
    body = (
      <CheckoutScreen
        shift={shift}
        online={state.online}
        onOpenShiftScreen={() => setView('shift')}
        onLogout={() => void logout('logout')}
      />
    );
  }

  return (
    <div className="flex h-full flex-col bg-slate-950">
      <div className="flex min-h-0 flex-1 flex-col">{body}</div>
      <StatusBar state={state} />
    </div>
  );
}
