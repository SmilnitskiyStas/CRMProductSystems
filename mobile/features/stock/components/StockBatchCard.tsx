import { View, Text, TouchableOpacity } from 'react-native';
import type { StockBatch, StockStatus } from '../types';

const statusConfig: Record<StockStatus, { bg: string; text: string; label: string }> = {
  safe: { bg: 'bg-green-100', text: 'text-green-700', label: 'Норма' },
  warning: { bg: 'bg-amber-100', text: 'text-amber-700', label: 'Попередження' },
  critical: { bg: 'bg-red-100', text: 'text-red-700', label: 'Критично' },
  expired: { bg: 'bg-purple-100', text: 'text-purple-700', label: 'Прострочено' },
  sold_out: { bg: 'bg-gray-100', text: 'text-gray-500', label: 'Продано' },
  needs_verification: { bg: 'bg-blue-100', text: 'text-blue-700', label: 'Перевірка' },
};

interface Props {
  item: StockBatch;
  onPress?: () => void;
}

export function StockBatchCard({ item, onPress }: Props) {
  const cfg = statusConfig[item.status];
  const expiry = new Date(item.expiryDate).toLocaleDateString('uk-UA');

  return (
    <TouchableOpacity onPress={onPress} className="bg-white rounded-xl p-4 shadow-sm">
      <View className="flex-row items-start justify-between">
        <View className="flex-1 mr-3">
          <Text className="text-base font-semibold text-gray-900" numberOfLines={2}>
            {item.productName}
          </Text>
          {item.zoneName && (
            <Text className="text-sm text-gray-500 mt-0.5">{item.zoneName}</Text>
          )}
        </View>
        <View className={`px-2 py-1 rounded-full ${cfg.bg}`}>
          <Text className={`text-xs font-medium ${cfg.text}`}>{cfg.label}</Text>
        </View>
      </View>

      <View className="flex-row mt-3 gap-4">
        <View>
          <Text className="text-xs text-gray-400">К-сть</Text>
          <Text className="text-sm font-medium text-gray-700">{item.quantity}</Text>
        </View>
        <View>
          <Text className="text-xs text-gray-400">Термін</Text>
          <Text className="text-sm font-medium text-gray-700">{expiry}</Text>
        </View>
        <View>
          <Text className="text-xs text-gray-400">Днів</Text>
          <Text className={`text-sm font-bold font-mono ${cfg.text}`}>{item.daysLeft}</Text>
        </View>
      </View>
    </TouchableOpacity>
  );
}
