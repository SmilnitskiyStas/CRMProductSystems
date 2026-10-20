export type CameraStatus = 'idle' | 'requesting' | 'scanning' | 'denied' | 'nocamera' | 'error';

export interface TrackLike {
  stop(): void;
}
export interface StreamLike {
  getTracks(): TrackLike[];
}
export interface DeviceInfoLike {
  kind: string;
  deviceId: string;
  label: string;
}
export interface MediaDevicesLike {
  getUserMedia(constraints: { video: MediaTrackConstraints | boolean; audio: false }): Promise<StreamLike>;
  enumerateDevices(): Promise<DeviceInfoLike[]>;
}

export interface CameraStartResult {
  status: CameraStatus;
  stream: StreamLike | null;
  devices: { deviceId: string; label: string }[];
  deviceId: string | null;
  message: string | null;
}

export const CAMERA_MESSAGES = {
  denied:
    'Доступ до камери заборонено. Windows: Параметри → Конфіденційність і безпека → Камера → увімкніть «Доступ до камери» та «Дозволити настільним застосункам доступ до камери». Потім натисніть «Спробувати знову».',
  nocamera: 'Камеру не знайдено. Підключіть веб-камеру або перевірте, що її не вимкнено в Диспетчері пристроїв.',
  busy: 'Камеру використовує інша програма (Teams, Zoom, браузер). Закрийте її та спробуйте знову.',
  error: 'Не вдалося запустити камеру.',
} as const;

const errName = (e: unknown): string => (e as { name?: string } | null)?.name ?? '';

/**
 * Owns the camera stream lifecycle: permission request, device list, error classification and
 * guaranteed track shutdown. DOM-free (media APIs are injected) so the paths are testable with a
 * fake MediaStream.
 */
export class CameraController {
  private stream: StreamLike | null = null;

  constructor(private readonly md: MediaDevicesLike) {}

  async start(preferredDeviceId?: string | null): Promise<CameraStartResult> {
    this.stop();
    const fail = (status: CameraStatus, message: string): CameraStartResult => ({
      status,
      stream: null,
      devices: [],
      deviceId: null,
      message,
    });

    try {
      let stream: StreamLike;
      try {
        stream = await this.md.getUserMedia({
          video: preferredDeviceId
            ? { deviceId: { exact: preferredDeviceId }, width: { ideal: 1280 }, height: { ideal: 720 } }
            : { width: { ideal: 1280 }, height: { ideal: 720 } },
          audio: false,
        });
      } catch (e) {
        // the remembered camera was unplugged: fall back to the default one
        if (preferredDeviceId && (errName(e) === 'OverconstrainedError' || errName(e) === 'NotFoundError')) {
          stream = await this.md.getUserMedia({ video: true, audio: false });
        } else throw e;
      }
      this.stream = stream;

      // labels are only available after permission was granted
      const all = await this.md.enumerateDevices();
      const devices = all.filter((d) => d.kind === 'videoinput').map((d, i) => ({
        deviceId: d.deviceId,
        label: d.label || `Камера ${i + 1}`,
      }));
      const active = (stream as { getVideoTracks?: () => { getSettings?: () => { deviceId?: string } }[] }).getVideoTracks?.()[0]?.getSettings?.().deviceId;
      return { status: 'scanning', stream, devices, deviceId: active ?? preferredDeviceId ?? devices[0]?.deviceId ?? null, message: null };
    } catch (e) {
      this.stop();
      switch (errName(e)) {
        case 'NotAllowedError':
        case 'SecurityError':
        case 'PermissionDeniedError':
          return fail('denied', CAMERA_MESSAGES.denied);
        case 'NotFoundError':
        case 'DevicesNotFoundError':
        case 'OverconstrainedError':
          return fail('nocamera', CAMERA_MESSAGES.nocamera);
        case 'NotReadableError':
        case 'TrackStartError':
          return fail('error', CAMERA_MESSAGES.busy);
        default:
          return fail('error', `${CAMERA_MESSAGES.error} ${(e as Error)?.message ?? ''}`.trim());
      }
    }
  }

  /** Stops every track of the current stream (idempotent). */
  stop(): void {
    this.stream?.getTracks().forEach((t) => t.stop());
    this.stream = null;
  }

  get active(): boolean {
    return this.stream !== null;
  }
}
