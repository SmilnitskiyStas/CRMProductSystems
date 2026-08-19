import type { DraftSubmissionStatus } from './storage';

export function classifyOperationalError(error: unknown): {
  status: DraftSubmissionStatus;
  message: string;
} {
  const e = error as { code?: string; response?: { status?: number; data?: { error?: string } } };
  if (e.response?.status === 409) {
    return {
      status: 'conflict',
      message: e.response.data?.error ?? 'Дані або залишки змінилися. Оновіть дані та перевірте операцію.',
    };
  }
  if (!e.response || e.code === 'ECONNABORTED' || e.code === 'ETIMEDOUT') {
    return {
      status: 'uncertain',
      message: 'Сервер не підтвердив результат. Не повторюйте операцію, доки не звірите її у списку документів.',
    };
  }
  return {
    status: 'failed',
    message: e.response.data?.error ?? 'Операцію не виконано. Чернетку збережено.',
  };
}
