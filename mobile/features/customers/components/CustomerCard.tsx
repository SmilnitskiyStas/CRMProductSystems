import { View, Text } from 'react-native';
import type { Customer } from '../types';
import { ListRow } from '@/components/ui';

interface Props {
  item: Customer;
  onPress: () => void;
}

export function CustomerCard({ item, onPress }: Props) {
  return (
    <ListRow title={item.name} onPress={onPress} accessibilityLabel={`Відкрити клієнта ${item.name}`} leading={
      <View className="w-10 h-10 rounded-full bg-primary-100 items-center justify-center">
        <Text className="text-primary-700 font-bold text-base">
          {item.name.charAt(0).toUpperCase()}
        </Text>
      </View>
    } subtitle={item.phone ?? item.email ?? 'Немає контактів'} trailing={
      <View className="items-end">
        <Text className="text-sm font-semibold text-gray-900">{item.totalSpent.toFixed(0)} ₴</Text>
        <Text className="text-xs text-gray-400 mt-0.5">{item.totalOrders} {item.totalOrders === 1 ? 'замовлення' : 'замовлень'}</Text>
      </View>
    } />
  );
}
