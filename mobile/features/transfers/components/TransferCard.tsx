import { View, Text, TouchableOpacity } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { STATUS_LABELS, STATUS_COLORS } from '../types';
import type { Transfer } from '../types';

interface Props {
  item: Transfer;
  onPress: () => void;
}

export function TransferCard({ item, onPress }: Props) {
  const statusStyle = STATUS_COLORS[item.status] ?? 'text-gray-600 bg-gray-100';
  const statusLabel = STATUS_LABELS[item.status] ?? item.status;

  return (
    <TouchableOpacity onPress={onPress} className="bg-white rounded-xl p-4">
      <View className="flex-row items-start justify-between">
        <View className="flex-1 mr-3">
          <Text className="text-base font-semibold text-gray-900">
            #{item.id.slice(0, 8).toUpperCase()}
          </Text>
          <View className="flex-row items-center gap-1.5 mt-1">
            <Text className="text-sm text-gray-600 font-medium">{item.fromStoreName}</Text>
            <Ionicons name="arrow-forward" size={14} color="#9ca3af" />
            <Text className="text-sm text-gray-600 font-medium">{item.toStoreName}</Text>
          </View>
        </View>
        <View className={`px-2 py-1 rounded-full ${statusStyle}`}>
          <Text className="text-xs font-medium">{statusLabel}</Text>
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
