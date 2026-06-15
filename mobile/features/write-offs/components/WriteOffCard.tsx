import { View, Text, TouchableOpacity } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { STATUS_LABELS, STATUS_COLORS, WRITE_OFF_REASON_LABELS } from '../types';
import type { WriteOff } from '../types';

interface Props {
  item: WriteOff;
  onPress: () => void;
}

export function WriteOffCard({ item, onPress }: Props) {
  const statusStyle = STATUS_COLORS[item.status] ?? 'text-gray-600 bg-gray-100';
  const statusLabel = STATUS_LABELS[item.status] ?? item.status;
  const reasonLabel = item.reason ? WRITE_OFF_REASON_LABELS[item.reason] : null;

  return (
    <TouchableOpacity onPress={onPress} className="bg-white rounded-xl p-4">
      <View className="flex-row items-start justify-between">
        <View className="flex-1 mr-3">
          <Text className="text-base font-semibold text-gray-900">
            #{item.id.slice(0, 8).toUpperCase()}
          </Text>
          <Text className="text-sm text-gray-500 mt-0.5">{item.storeName}</Text>
          {reasonLabel && (
            <View className="flex-row items-center mt-1 gap-1">
              <Ionicons name="alert-circle-outline" size={13} color="#9ca3af" />
              <Text className="text-xs text-gray-400">{reasonLabel}</Text>
            </View>
          )}
        </View>

        <View className="items-end gap-1.5">
          <View className={`px-2 py-1 rounded-full ${statusStyle}`}>
            <Text className="text-xs font-medium">{statusLabel}</Text>
          </View>
          {item.totalLossAmount != null && item.totalLossAmount > 0 && (
            <Text className="text-xs text-red-500 font-medium">
              −{item.totalLossAmount.toFixed(2)} ₴
            </Text>
          )}
        </View>
      </View>

      <View className="flex-row items-center justify-between mt-3 pt-2 border-t border-gray-50">
        <Text className="text-xs text-gray-400">
          {item.items.length} {item.items.length === 1 ? 'позиція' : 'позицій'}
        </Text>
        <Text className="text-xs text-gray-400">
          {new Date(item.createdAt).toLocaleDateString('uk-UA')}
        </Text>
      </View>
    </TouchableOpacity>
  );
}
