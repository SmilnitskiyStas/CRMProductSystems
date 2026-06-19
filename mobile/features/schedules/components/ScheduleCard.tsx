import { View, Text, TouchableOpacity } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import type { WorkSchedule } from '../types';

const UA_DAYS_SHORT = ['Нд', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];
const UA_MONTHS = [
  'січ', 'лют', 'бер', 'квіт', 'трав', 'черв',
  'лип', 'серп', 'вер', 'жовт', 'лист', 'груд',
];

const SCHEDULE_STATUS_STYLES: Record<string, string> = {
  draft: 'bg-gray-100 text-gray-600',
  published: 'bg-green-100 text-green-700',
  archived: 'bg-slate-100 text-slate-600',
};

const SCHEDULE_STATUS_LABELS: Record<string, string> = {
  draft: 'Чернетка',
  published: 'Опубліковано',
  archived: 'Архів',
};

function formatWeekRange(weekStart: string): string {
  const start = new Date(weekStart + 'T00:00:00');
  const end = new Date(start);
  end.setDate(start.getDate() + 6);

  const startDay = UA_DAYS_SHORT[start.getDay()];
  const endDay = UA_DAYS_SHORT[end.getDay()];
  const startNum = start.getDate();
  const endNum = end.getDate();
  const startMonth = UA_MONTHS[start.getMonth()];
  const endMonth = UA_MONTHS[end.getMonth()];

  if (start.getMonth() === end.getMonth()) {
    return `${startDay} ${startNum} – ${endDay} ${endNum} ${endMonth}`;
  }
  return `${startDay} ${startNum} ${startMonth} – ${endDay} ${endNum} ${endMonth}`;
}

interface Props {
  item: WorkSchedule;
  onPress: () => void;
}

export function ScheduleCard({ item, onPress }: Props) {
  const statusStyle = SCHEDULE_STATUS_STYLES[item.status] ?? 'bg-gray-100 text-gray-600';
  const statusLabel = SCHEDULE_STATUS_LABELS[item.status] ?? item.status;
  const weekRange = formatWeekRange(item.weekStart);

  return (
    <TouchableOpacity
      onPress={onPress}
      className="bg-white rounded-xl px-4 py-3.5 shadow-sm border border-gray-100 flex-row items-center gap-3"
      activeOpacity={0.7}
    >
      <View className="flex-1 gap-1.5">
        {/* Location + name */}
        <Text className="text-sm font-bold text-gray-900" numberOfLines={1}>
          {item.locationName}
        </Text>
        <Text className="text-xs text-gray-500" numberOfLines={1}>
          {item.name}
        </Text>

        {/* Week range */}
        <Text className="text-xs text-gray-400">{weekRange}</Text>

        {/* Bottom row: status + shift count */}
        <View className="flex-row items-center gap-2 mt-0.5">
          <View className={`px-2 py-0.5 rounded-full ${statusStyle}`}>
            <Text className="text-xs font-medium">{statusLabel}</Text>
          </View>
          <View className="flex-row items-center gap-1">
            <Ionicons name="people-outline" size={12} color="#9ca3af" />
            <Text className="text-xs text-gray-400">{item.shiftCount} змін</Text>
          </View>
        </View>
      </View>

      <Ionicons name="chevron-forward" size={18} color="#d1d5db" />
    </TouchableOpacity>
  );
}
