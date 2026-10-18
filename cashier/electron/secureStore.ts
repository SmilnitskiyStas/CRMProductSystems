import { app, safeStorage } from 'electron';
import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import type { RegisterInfo } from '@shared/types';

/** On-disk config. The device token is only ever stored encrypted with the OS keystore (safeStorage). */
interface PersistedConfig {
  serverUrl: string;
  register: RegisterInfo | null;
  deviceTokenEnc: string | null; // base64 of safeStorage ciphertext
}

export interface ConfigSnapshot {
  serverUrl: string;
  register: RegisterInfo | null;
  deviceToken: string | null;
}

export const DEFAULT_SERVER_URL = process.env.CASHIER_SERVER_URL ?? 'http://localhost:5000';

export class SecureConfigStore {
  private readonly file = join(app.getPath('userData'), 'config.json');
  private data: ConfigSnapshot = { serverUrl: DEFAULT_SERVER_URL, register: null, deviceToken: null };

  get snapshot(): ConfigSnapshot {
    return this.data;
  }

  async load(): Promise<void> {
    try {
      const raw = JSON.parse(await readFile(this.file, 'utf8')) as PersistedConfig;
      let token: string | null = null;
      if (raw.deviceTokenEnc && safeStorage.isEncryptionAvailable()) {
        try {
          token = safeStorage.decryptString(Buffer.from(raw.deviceTokenEnc, 'base64'));
        } catch {
          token = null; // keystore changed (other OS user / machine): re-activation required
        }
      }
      this.data = {
        serverUrl: raw.serverUrl || DEFAULT_SERVER_URL,
        register: token ? raw.register : null,
        deviceToken: token,
      };
    } catch {
      /* first run */
    }
  }

  async setServerUrl(serverUrl: string): Promise<void> {
    this.data = { ...this.data, serverUrl };
    await this.save();
  }

  /** Throws when the OS cannot encrypt: the token is never written in plaintext. */
  async setActivation(serverUrl: string, register: RegisterInfo, deviceToken: string): Promise<void> {
    if (!safeStorage.isEncryptionAvailable()) {
      throw new Error('Захищене сховище ОС недоступне: неможливо безпечно зберегти токен каси.');
    }
    this.data = { serverUrl, register, deviceToken };
    await this.save();
  }

  async clearActivation(): Promise<void> {
    this.data = { ...this.data, register: null, deviceToken: null };
    await this.save();
  }

  private async save(): Promise<void> {
    const out: PersistedConfig = {
      serverUrl: this.data.serverUrl,
      register: this.data.register,
      deviceTokenEnc: this.data.deviceToken
        ? safeStorage.encryptString(this.data.deviceToken).toString('base64')
        : null,
    };
    await mkdir(app.getPath('userData'), { recursive: true });
    const tmp = this.file + '.tmp';
    await writeFile(tmp, JSON.stringify(out), 'utf8');
    await rename(tmp, this.file);
  }
}
