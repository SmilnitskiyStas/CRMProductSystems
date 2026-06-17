import { View, Text } from 'react-native';
import { STATUS_LABELS, STATUS_COLORS } from '../types';
import type { WorkOrderStatus } from '../types';

interface Props {
  status: WorkOrderStatus;
}

export function WorkOrderStatusBadge({ status }: Props) {
  const colorClass = STATUS_COLORS[status] ?? 'text-gray-700 bg-gray-100';
  const label = STATUS_LABELS[status] ?? status;

  return (
    <View className={`px-2.5 py-1 rounded-full ${colorClass}`}>
      <Text className="text-xs font-semibold">{label}</Text>
    </View>
  );
}
