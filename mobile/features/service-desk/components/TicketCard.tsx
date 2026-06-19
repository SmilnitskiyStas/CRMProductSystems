import { View, Text, TouchableOpacity } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import type { Ticket } from '../types';

const PRIORITY_STYLES: Record<string, string> = {
  low: 'bg-gray-100 text-gray-600',
  medium: 'bg-blue-100 text-blue-700',
  high: 'bg-orange-100 text-orange-700',
  critical: 'bg-red-100 text-red-700',
};

const PRIORITY_LABELS: Record<string, string> = {
  low: 'Низький',
  medium: 'Середній',
  high: 'Високий',
  critical: 'Критичний',
};

const STATUS_STYLES: Record<string, string> = {
  open: 'bg-blue-100 text-blue-700',
  in_progress: 'bg-yellow-100 text-yellow-700',
  waiting: 'bg-purple-100 text-purple-700',
  resolved: 'bg-green-100 text-green-700',
  closed: 'bg-gray-100 text-gray-600',
};

const STATUS_LABELS: Record<string, string> = {
  open: 'Відкрито',
  in_progress: 'В роботі',
  waiting: 'Очікування',
  resolved: 'Вирішено',
  closed: 'Закрито',
};

const CATEGORY_LABELS: Record<string, string> = {
  general: 'Загальне',
  technical: 'Технічне',
  billing: 'Оплата',
  feature_request: 'Пропозиція',
  bug: 'Помилка',
};

interface Props {
  item: Ticket;
  onPress: () => void;
}

export function TicketCard({ item, onPress }: Props) {
  const priorityStyle = PRIORITY_STYLES[item.priority] ?? 'bg-gray-100 text-gray-600';
  const statusStyle = STATUS_STYLES[item.status] ?? 'bg-gray-100 text-gray-600';
  const priorityLabel = PRIORITY_LABELS[item.priority] ?? item.priority;
  const statusLabel = STATUS_LABELS[item.status] ?? item.status;
  const categoryLabel = CATEGORY_LABELS[item.category] ?? item.category;

  const formattedDate = new Date(item.createdAt).toLocaleDateString('uk-UA', {
    day: '2-digit',
    month: '2-digit',
    year: '2-digit',
  });

  return (
    <TouchableOpacity
      onPress={onPress}
      className="bg-white rounded-xl px-4 py-3 shadow-sm border border-gray-100"
      activeOpacity={0.7}
    >
      {/* Number + Title */}
      <View className="flex-row items-start gap-2 mb-2">
        <Text className="text-xs text-gray-400 mt-0.5 min-w-[36px]">#{item.number}</Text>
        <Text className="flex-1 text-sm font-bold text-gray-900" numberOfLines={2}>
          {item.title}
        </Text>
      </View>

      {/* Badges */}
      <View className="flex-row gap-2 mb-2.5">
        <View className={`px-2 py-0.5 rounded-full ${priorityStyle}`}>
          <Text className="text-xs font-medium">{priorityLabel}</Text>
        </View>
        <View className={`px-2 py-0.5 rounded-full ${statusStyle}`}>
          <Text className="text-xs font-medium">{statusLabel}</Text>
        </View>
      </View>

      {/* Bottom row */}
      <View className="flex-row items-center justify-between">
        <Text className="text-xs text-gray-400">{categoryLabel} · {formattedDate}</Text>
        <View className="flex-row items-center gap-1">
          <Ionicons name="chatbubble-outline" size={12} color="#9ca3af" />
          <Text className="text-xs text-gray-400">{item.commentCount}</Text>
        </View>
      </View>
    </TouchableOpacity>
  );
}
