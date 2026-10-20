import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import {
  DEFAULT_IDLE_LOGOUT_MINUTES,
  MAX_IDLE_LOGOUT_MINUTES,
  MIN_IDLE_LOGOUT_MINUTES,
  type AppPrefs,
} from '@shared/types';

/** General app preferences (userData/app-prefs.json); anything invalid falls back to a safe value. */
export class AppPrefsStore {
  private cached: AppPrefs = { idleLogoutMinutes: DEFAULT_IDLE_LOGOUT_MINUTES };

  constructor(private readonly file: string) {
    try {
      this.cached = AppPrefsStore.normalize(JSON.parse(readFileSync(file, 'utf8')));
    } catch {
      /* first run */
    }
  }

  static normalize(raw: Partial<AppPrefs> | null | undefined): AppPrefs {
    const n = Number(raw?.idleLogoutMinutes);
    const minutes = Number.isFinite(n) ? Math.round(n) : DEFAULT_IDLE_LOGOUT_MINUTES;
    return {
      idleLogoutMinutes: Math.min(MAX_IDLE_LOGOUT_MINUTES, Math.max(MIN_IDLE_LOGOUT_MINUTES, minutes)),
    };
  }

  get(): AppPrefs {
    return this.cached;
  }

  save(p: Partial<AppPrefs>): AppPrefs {
    this.cached = AppPrefsStore.normalize(p);
    mkdirSync(dirname(this.file), { recursive: true });
    writeFileSync(this.file, JSON.stringify(this.cached), 'utf8');
    return this.cached;
  }
}
