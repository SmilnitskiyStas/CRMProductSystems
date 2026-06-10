import { View, Text } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import type { ReceiptItem } from '../types';

interface Props {
  item: ReceiptItem;
}

export function ReceiptItemRow({ item }: Props) {
  return (
    <View className="flex-row items-center py-3 px-4 bg-white border-b border-gray-100">
      <View className="flex-1">
        <Text className="text-sm font-medium text-gray-900" numberOfLines={1}>
          {item.productName}
        </Text>
        <Text className="text-xs text-gray-500 mt-0.5">
          {item.receivedQty} / {item.orderedQty} {item.unit}
        </Text>
      </View>
      {item.isProcessed ? (
        <Ionicons name="checkmark-circle" size={22} color="#16a34a" />
      ) : (
        <Ionicons name="ellipse-outline" size={22} color="#d1d5db" />
      )}
    </View>
  );
}
