import { View, Text } from 'react-native';
import type { FiscalStatus } from '../types';

interface Props {
  status: FiscalStatus;
}

const CONFIG: Record<FiscalStatus, { label: string; bg: string; text: string }> = {
  fiscalized: {
    label: 'Фіскалізовано',
    bg: 'bg-green-100',
    text: 'text-green-800',
  },
  pending_fiscalization: {
    label: 'Очікує фіскалізації',
    bg: 'bg-amber-100',
    text: 'text-amber-800',
  },
  not_required: {
    label: 'Без фіскалізації',
    bg: 'bg-gray-100',
    text: 'text-gray-600',
  },
  error: {
    label: 'Помилка фіскалізації',
    bg: 'bg-red-100',
    text: 'text-red-800',
  },
};

export function FiscalBadge({ status }: Props) {
  const { label, bg, text } = CONFIG[status] ?? CONFIG.error;
  return (
    <View className={`${bg} px-3 py-1 rounded-full self-start`}>
      <Text className={`${text} text-xs font-semibold`}>{label}</Text>
    </View>
  );
}
