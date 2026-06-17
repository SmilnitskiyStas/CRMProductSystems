import { View, Text, TouchableOpacity } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { WorkOrderStatusBadge } from './WorkOrderStatusBadge';
import type { AsWorkOrder } from '../types';

interface Props {
  item: AsWorkOrder;
  onPress: () => void;
}

export function WorkOrderCard({ item, onPress }: Props) {
  return (
    <TouchableOpacity onPress={onPress} className="bg-white rounded-xl p-4">
      <View className="flex-row items-start justify-between">
        <View className="flex-1 mr-3">
          <Text className="text-base font-bold text-gray-900">
            {item.vehicleBrand} {item.vehicleModel}
          </Text>
          <View className="flex-row items-center gap-1.5 mt-0.5">
            <Ionicons name="car-outline" size={13} color="#9ca3af" />
            <Text className="text-sm text-gray-500">{item.vehicleLicensePlate}</Text>
          </View>
          {item.mechanicName && (
            <View className="flex-row items-center gap-1.5 mt-0.5">
              <Ionicons name="person-outline" size={13} color="#9ca3af" />
              <Text className="text-xs text-gray-400">{item.mechanicName}</Text>
            </View>
          )}
        </View>

        <View className="items-end gap-1.5">
          <WorkOrderStatusBadge status={item.status} />
          {item.totalAmount > 0 && (
            <Text className="text-sm font-semibold text-gray-700">
              {item.totalAmount.toFixed(2)} ₴
            </Text>
          )}
        </View>
      </View>

      <View className="flex-row items-center justify-between mt-3 pt-2 border-t border-gray-50">
        <Text className="text-xs text-gray-400">
          {item.lines.length} {item.lines.length === 1 ? 'позиція' : 'позицій'}
        </Text>
        <Text className="text-xs text-gray-400">
          {new Date(item.createdAt).toLocaleDateString('uk-UA')}
        </Text>
      </View>
    </TouchableOpacity>
  );
}
