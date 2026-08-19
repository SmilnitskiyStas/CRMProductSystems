import type { MobileConfigSource } from './loader';

export interface MobileConfigOfflineUx {
  visible: boolean;
  message: string | null;
}

export function deriveMobileConfigOfflineUx({
  online,
  source,
  cachedAt,
  loading = false,
}: {
  online: boolean;
  source: MobileConfigSource;
  cachedAt: number | null;
  loading?: boolean;
}): MobileConfigOfflineUx {
  if (loading) return { visible: false, message: null };
  if (source === 'safe-default') {
    return {
      visible: true,
      message: online
        ? 'Не вдалося оновити оформлення магазину. Використовується безпечний режим.'
        : 'Немає мережі та збереженої конфігурації. Використовується безпечний режим.',
    };
  }
  if (source === 'last-valid') {
    const suffix = cachedAt
      ? ` Оновлено ${new Date(cachedAt).toLocaleString('uk-UA', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })}.`
      : '';
    return {
      visible: true,
      message: `${online ? 'Не вдалося оновити конфігурацію.' : 'Офлайн-режим.'} Показано збережене оформлення.${suffix}`,
    };
  }
  return { visible: false, message: null };
}
