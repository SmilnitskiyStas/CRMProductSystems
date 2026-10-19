import { useEffect, useState } from 'react';
import type { PrintCodepage, PrintSettings, PrintTransport } from '@shared/types';

const CODEPAGES: { id: PrintCodepage; label: string; escT: number }[] = [
  { id: 'cp866', label: 'CP866 (є ї без і ґ)', escT: 17 },
  { id: 'cp1125', label: 'CP1125 (повна українська)', escT: 17 },
  { id: 'cp1251', label: 'CP1251', escT: 46 },
  { id: 'utf8', label: 'UTF-8', escT: 255 },
];

const TRANSPORTS: { id: PrintTransport; label: string }[] = [
  { id: 'windows', label: 'Принтер Windows (RAW)' },
  { id: 'tcp', label: 'Мережа (TCP 9100)' },
  { id: 'serial', label: 'COM-порт' },
  { id: 'file', label: 'Файл (діагностика)' },
];

export function PrintSettingsScreen({ onBack }: { onBack: () => void }) {
  const [s, setS] = useState<PrintSettings | null>(null);
  const [printers, setPrinters] = useState<string[]>([]);
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null);

  useEffect(() => {
    void window.cashier.getPrintSettings().then(setS);
    void window.cashier.listPrinters().then(setPrinters);
  }, []);

  if (!s) return null;
  const set = <K extends keyof PrintSettings>(k: K, v: PrintSettings[K]) => setS({ ...s, [k]: v });

  const save = async () => {
    setS(await window.cashier.savePrintSettings(s));
    setMsg({ text: 'Налаштування збережено.', ok: true });
  };

  const test = async () => {
    await window.cashier.savePrintSettings(s);
    const r = await window.cashier.testPrint();
    setMsg(r.ok ? { text: 'Тестову сторінку надіслано. Перевірте літери і ї є ґ.', ok: true } : { text: `Помилка друку: ${r.error}`, ok: false });
  };

  const drawer = async () => {
    await window.cashier.savePrintSettings(s);
    const r = await window.cashier.openDrawer();
    setMsg(r.ok ? { text: 'Імпульс на скриньку надіслано.', ok: true } : { text: `Помилка: ${r.error}`, ok: false });
  };

  const Label = ({ children }: { children: string }) => <span className="text-sm text-slate-400">{children}</span>;

  return (
    <div className="flex min-h-0 flex-1 flex-col overflow-auto p-6">
      <div className="mb-4 flex items-center gap-3">
        <button className="btn-secondary px-4 py-2" onClick={onBack}>
          ← Назад
        </button>
        <h1 className="text-2xl font-bold">Налаштування друку</h1>
      </div>

      <div className="card mx-auto w-full max-w-2xl space-y-5">
        <label className="flex items-center gap-3 text-lg">
          <input type="checkbox" className="h-5 w-5" checked={s.autoPrint} onChange={(e) => set('autoPrint', e.target.checked)} />
          Друкувати чек автоматично після продажу
        </label>
        <label className="flex items-center gap-3 text-lg">
          <input type="checkbox" className="h-5 w-5" checked={s.openDrawerOnCash} onChange={(e) => set('openDrawerOnCash', e.target.checked)} />
          Відкривати касову скриньку при оплаті готівкою
        </label>

        <div className="grid grid-cols-2 gap-4">
          <label className="space-y-1">
            <Label>Підключення</Label>
            <select className="field" value={s.transport} onChange={(e) => set('transport', e.target.value as PrintTransport)}>
              {TRANSPORTS.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.label}
                </option>
              ))}
            </select>
          </label>
          <label className="space-y-1">
            <Label>Ширина стрічки</Label>
            <select className="field" value={s.widthMm} onChange={(e) => set('widthMm', Number(e.target.value) === 58 ? 58 : 80)}>
              <option value={58}>58 мм (32 колонки)</option>
              <option value={80}>80 мм (48 колонок)</option>
            </select>
          </label>
        </div>

        {s.transport === 'windows' && (
          <label className="block space-y-1">
            <Label>Встановлений принтер</Label>
            <select className="field" value={s.printerName} onChange={(e) => set('printerName', e.target.value)}>
              <option value="">- оберіть -</option>
              {printers.map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </select>
          </label>
        )}
        {s.transport === 'tcp' && (
          <div className="grid grid-cols-[1fr_8rem] gap-4">
            <label className="space-y-1">
              <Label>IP-адреса принтера</Label>
              <input className="field" value={s.tcpHost} onChange={(e) => set('tcpHost', e.target.value)} placeholder="192.168.1.50" />
            </label>
            <label className="space-y-1">
              <Label>Порт</Label>
              <input className="field" type="number" value={s.tcpPort} onChange={(e) => set('tcpPort', Number(e.target.value))} />
            </label>
          </div>
        )}
        {s.transport === 'serial' && (
          <div className="grid grid-cols-2 gap-4">
            <label className="space-y-1">
              <Label>COM-порт</Label>
              <input className="field" value={s.serialPort} onChange={(e) => set('serialPort', e.target.value.toUpperCase())} placeholder="COM3" />
            </label>
            <label className="space-y-1">
              <Label>Швидкість (baud)</Label>
              <input className="field" type="number" value={s.serialBaud} onChange={(e) => set('serialBaud', Number(e.target.value))} />
            </label>
          </div>
        )}
        {s.transport === 'file' && (
          <label className="block space-y-1">
            <Label>Шлях до файлу</Label>
            <input className="field" value={s.filePath} onChange={(e) => set('filePath', e.target.value)} placeholder="C:\\temp\\receipt.bin" />
          </label>
        )}

        <div className="grid grid-cols-3 gap-4">
          <label className="col-span-2 space-y-1">
            <Label>Кодування (кирилиця)</Label>
            <select
              className="field"
              value={s.codepage}
              onChange={(e) => {
                const cp = CODEPAGES.find((c) => c.id === e.target.value)!;
                setS({ ...s, codepage: cp.id, codepageId: cp.escT });
              }}
            >
              {CODEPAGES.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.label}
                </option>
              ))}
            </select>
          </label>
          <label className="space-y-1">
            <Label>Таблиця ESC t n</Label>
            <input className="field" type="number" min={0} max={255} value={s.codepageId} onChange={(e) => set('codepageId', Number(e.target.value))} />
          </label>
        </div>
        <label className="block w-40 space-y-1">
          <Label>Кількість копій</Label>
          <input className="field" type="number" min={1} max={5} value={s.copies} onChange={(e) => set('copies', Number(e.target.value))} />
        </label>

        {msg && <p className={`rounded-lg px-4 py-3 ${msg.ok ? 'bg-emerald-500/10 text-emerald-300' : 'bg-rose-500/10 text-rose-300'}`}>{msg.text}</p>}

        <div className="flex gap-3">
          <button className="btn-secondary" onClick={() => void test()}>
            Тестовий друк
          </button>
          <button className="btn-secondary" onClick={() => void drawer()}>
            Відкрити скриньку
          </button>
          <button className="btn-primary ml-auto" onClick={() => void save()}>
            Зберегти
          </button>
        </div>
      </div>
    </div>
  );
}
