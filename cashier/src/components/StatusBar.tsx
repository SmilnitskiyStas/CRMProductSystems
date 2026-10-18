import type { AppState } from '@shared/types';

export function StatusBar({ state }: { state: AppState }) {
  const { register, operator, online, version } = state;
  return (
    <footer className="flex items-center gap-6 border-t border-slate-800 bg-slate-900 px-5 py-2 text-sm text-slate-400">
      <span className="flex items-center gap-2">
        <span className={`h-2.5 w-2.5 rounded-full ${online ? 'bg-emerald-400' : 'bg-rose-500'}`} />
        {online ? 'На зв’язку' : 'Немає зв’язку'}
      </span>
      {register && (
        <span>
          Каса №{register.number} · {register.name}
          {register.storeName ? ` · ${register.storeName}` : ''}
        </span>
      )}
      {operator && <span>Оператор: {operator.cashierName}</span>}
      <span className="ml-auto">ShelfGuard Cashier v{version}</span>
    </footer>
  );
}
