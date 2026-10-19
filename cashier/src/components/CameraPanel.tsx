import { useCallback, useEffect, useRef, useState } from 'react';
import { ScanGate } from '@shared/cameraScan';
import { FrameDecoder } from '../lib/barcodeDecoder';
import { playTone } from '../lib/beep';
import { CameraController, type CameraStatus } from '../lib/cameraController';

export type ScanOutcome = { kind: 'added'; name: string } | { kind: 'notfound' } | { kind: 'noprice'; name: string };

interface Props {
  /** Remembered camera (from config). */
  initialDeviceId: string | null;
  /** True while a modal (payment, receipt...) is open: the stream stays, decoding pauses. */
  paused: boolean;
  /** Same handler the USB scanner uses; resolves with what happened to the code. */
  onCode: (code: string) => Promise<ScanOutcome>;
  onDeviceChange: (deviceId: string) => void;
  onClose: () => void;
}

const TICK_MS = 110; // ~9 fps
const MAX_WIDTH = 640;

export function CameraPanel({ initialDeviceId, paused, onCode, onDeviceChange, onClose }: Props) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const controller = useRef<CameraController | null>(null);
  const [status, setStatus] = useState<CameraStatus>('requesting');
  const [message, setMessage] = useState<string | null>(null);
  const [devices, setDevices] = useState<{ deviceId: string; label: string }[]>([]);
  const [deviceId, setDeviceId] = useState<string | null>(initialDeviceId);
  const [flash, setFlash] = useState<'ok' | 'error' | null>(null);
  const [attempt, setAttempt] = useState(0);
  const pausedRef = useRef(paused);
  pausedRef.current = paused;
  const onCodeRef = useRef(onCode);
  onCodeRef.current = onCode;

  const showFlash = useCallback((kind: 'ok' | 'error') => {
    setFlash(kind);
    setTimeout(() => setFlash(null), 450);
  }, []);

  // Stream lifecycle: (re)start when the chosen camera changes, always stop all tracks on cleanup.
  useEffect(() => {
    let cancelled = false;
    const ctl = new CameraController(navigator.mediaDevices);
    controller.current = ctl;
    const video = videoRef.current;
    setStatus('requesting');
    setMessage(null);

    void ctl.start(deviceId).then(async (res) => {
      if (cancelled) {
        ctl.stop();
        return;
      }
      setStatus(res.status);
      setMessage(res.message);
      if (res.status !== 'scanning' || !res.stream || !video) return;
      setDevices(res.devices);
      if (res.deviceId && res.deviceId !== deviceId) onDeviceChange(res.deviceId);
      video.srcObject = res.stream as MediaStream;
      try {
        await video.play();
      } catch {
        /* autoplay edge: the muted video starts on the next frame */
      }
    });

    return () => {
      cancelled = true;
      ctl.stop();
      if (video) video.srcObject = null;
    };
    // onDeviceChange is intentionally not a dependency: a new camera selection restarts the stream.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deviceId, attempt]);

  // Decode loop.
  useEffect(() => {
    if (status !== 'scanning') return;
    const gate = new ScanGate();
    const decoder = new FrameDecoder();
    const canvas = document.createElement('canvas');
    const g = canvas.getContext('2d', { willReadFrequently: true });
    let busy = false;

    const timer = setInterval(() => {
      const video = videoRef.current;
      if (!g || !video || busy || pausedRef.current || video.readyState < 2 || !video.videoWidth) return;
      busy = true;
      try {
        const scale = Math.min(1, MAX_WIDTH / video.videoWidth);
        canvas.width = Math.round(video.videoWidth * scale);
        canvas.height = Math.round(video.videoHeight * scale);
        g.drawImage(video, 0, 0, canvas.width, canvas.height);
        const frame = g.getImageData(0, 0, canvas.width, canvas.height);
        const text = decoder.decode(frame.data, canvas.width, canvas.height);
        const accepted = text ? gate.feed(text, performance.now()) : null;
        if (accepted) {
          void onCodeRef.current(accepted).then((outcome) => {
            if (outcome.kind === 'added') {
              playTone('ok');
              showFlash('ok');
            } else {
              playTone('error');
              showFlash('error');
            }
          });
        }
      } finally {
        busy = false;
      }
    }, TICK_MS);
    return () => clearInterval(timer);
  }, [status, showFlash]);

  const ring = flash === 'ok' ? 'ring-4 ring-emerald-400' : flash === 'error' ? 'ring-4 ring-rose-500' : 'ring-1 ring-slate-700';

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-2">
        <span className="text-sm font-semibold text-slate-300">Камера-сканер</span>
        <button className="rounded-lg px-2 py-1 text-slate-400 hover:bg-slate-800" onClick={onClose} title="Закрити (F3)">
          ✕
        </button>
      </div>

      <div className={`relative aspect-video overflow-hidden rounded-xl bg-black transition ${ring}`}>
        <video ref={videoRef} muted playsInline className="h-full w-full object-cover" />

        {status === 'scanning' && (
          <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
            <div className="relative h-[52%] w-[78%] rounded-lg border-2 border-white/60">
              <span className="absolute -left-0.5 -top-0.5 h-4 w-4 border-l-4 border-t-4 border-brand-400" />
              <span className="absolute -right-0.5 -top-0.5 h-4 w-4 border-r-4 border-t-4 border-brand-400" />
              <span className="absolute -bottom-0.5 -left-0.5 h-4 w-4 border-b-4 border-l-4 border-brand-400" />
              <span className="absolute -bottom-0.5 -right-0.5 h-4 w-4 border-b-4 border-r-4 border-brand-400" />
              <span className="absolute left-2 right-2 top-1/2 h-0.5 animate-pulse bg-rose-500/80" />
            </div>
          </div>
        )}

        {status === 'requesting' && (
          <div className="absolute inset-0 flex items-center justify-center bg-slate-900/90 p-3 text-center text-sm text-slate-300">
            Запит доступу до камери…
          </div>
        )}
        {(status === 'denied' || status === 'nocamera' || status === 'error') && (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 overflow-auto bg-slate-900/95 p-3 text-center text-xs text-amber-200">
            <p>{message}</p>
            <button className="btn-secondary px-3 py-1.5 text-sm" onClick={() => setAttempt((a) => a + 1)}>
              Спробувати знову
            </button>
          </div>
        )}
      </div>

      {devices.length > 1 && (
        <select
          className="field py-2 text-sm"
          value={deviceId ?? ''}
          onChange={(e) => {
            setDeviceId(e.target.value);
            onDeviceChange(e.target.value);
          }}
        >
          {devices.map((d) => (
            <option key={d.deviceId} value={d.deviceId}>
              {d.label}
            </option>
          ))}
        </select>
      )}
      {status === 'scanning' && (
        <p className="text-xs text-slate-500">Піднесіть штрихкод у рамку, 15-25 см від камери, при хорошому світлі.</p>
      )}
    </div>
  );
}
