import { View, Text, TouchableOpacity } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import type { Customer } from '../types';

interface Props {
  item: Customer;
  onPress: () => void;
}

export function CustomerCard({ item, onPress }: Props) {
  return (
    <TouchableOpacity
      onPress={onPress}
      className="bg-white rounded-xl p-4 flex-row items-center"
    >
      {/* Left: avatar placeholder */}
      <View className="w-10 h-10 rounded-full bg-primary-100 items-center justify-center mr-3">
        <Text className="text-primary-700 font-bold text-base">
          {item.name.charAt(0).toUpperCase()}
        </Text>
      </View>

      {/* Center: name + contacts */}
      <View className="flex-1 mr-2">
        <Text className="text-base font-bold text-gray-900" numberOfLines={1}>
          {item.name}
        </Text>
        {item.phone ? (
          <View className="flex-row items-center gap-1 mt-0.5">
            <Ionicons name="call-outline" size={12} color="#9ca3af" />
            <Text className="text-sm text-gray-500">{item.phone}</Text>
          </View>
        ) : item.email ? (
          <View className="flex-row items-center gap-1 mt-0.5">
            <Ionicons name="mail-outline" size={12} color="#9ca3af" />
            <Text className="text-sm text-gray-500" numberOfLines={1}>
              {item.email}
            </Text>
          </View>
        ) : (
          <Text className="text-sm text-gray-400 mt-0.5">Немає контактів</Text>
        )}
      </View>

      {/* Right: stats + chevron */}
      <View className="items-end mr-2">
        <Text className="text-sm font-semibold text-gray-900">
          {item.totalSpent.toFixed(0)} ₴
        </Text>
        <Text className="text-xs text-gray-400 mt-0.5">
          {item.totalOrders} {item.totalOrders === 1 ? 'замовлення' : 'замовлень'}
        </Text>
      </View>

      <Ionicons name="chevron-forward" size={16} color="#d1d5db" />
    </TouchableOpacity>
  );
}
