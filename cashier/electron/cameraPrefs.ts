import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import type { CameraPrefs } from '@shared/types';

/** Remembers whether the webcam scanner was on and which camera was chosen (userData/camera-prefs.json). */
export class CameraPrefsStore {
  private cached: CameraPrefs = { enabled: false, deviceId: null };

  constructor(private readonly file: string) {
    try {
      this.cached = CameraPrefsStore.normalize(JSON.parse(readFileSync(file, 'utf8')));
    } catch {
      /* first run */
    }
  }

  static normalize(raw: Partial<CameraPrefs> | null | undefined): CameraPrefs {
    return {
      enabled: raw?.enabled === true,
      deviceId: typeof raw?.deviceId === 'string' && raw.deviceId ? raw.deviceId : null,
    };
  }

  get(): CameraPrefs {
    return this.cached;
  }

  save(p: Partial<CameraPrefs>): CameraPrefs {
    this.cached = CameraPrefsStore.normalize(p);
    mkdirSync(dirname(this.file), { recursive: true });
    writeFileSync(this.file, JSON.stringify(this.cached), 'utf8');
    return this.cached;
  }
}
