import { View, Text } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import type { ScheduleShift } from '../types';

const UA_DAYS = ['Нд', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];
const UA_MONTHS = [
  'січ', 'лют', 'бер', 'квіт', 'трав', 'черв',
  'лип', 'серп', 'вер', 'жовт', 'лист', 'груд',
];

const SHIFT_STATUS_STYLES: Record<string, string> = {
  scheduled: 'bg-blue-100 text-blue-700',
  confirmed: 'bg-green-100 text-green-700',
  completed: 'bg-gray-100 text-gray-500',
  cancelled: 'bg-red-100 text-red-700',
};

const SHIFT_STATUS_LABELS: Record<string, string> = {
  scheduled: 'Заплановано',
  confirmed: 'Підтверджено',
  completed: 'Завершено',
  cancelled: 'Скасовано',
};

function formatTime(t: string): string {
  return t.slice(0, 5);
}

interface Props {
  item: ScheduleShift;
}

export function ShiftCard({ item }: Props) {
  const date = new Date(item.shiftDate + 'T00:00:00');
  const dayName = UA_DAYS[date.getDay()];
  const dayNum = date.getDate();
  const monthName = UA_MONTHS[date.getMonth()];

  const statusStyle = SHIFT_STATUS_STYLES[item.status] ?? 'bg-gray-100 text-gray-500';
  const statusLabel = SHIFT_STATUS_LABELS[item.status] ?? item.status;
  const isCancelled = item.status === 'cancelled';

  return (
    <View className="bg-white rounded-xl px-4 py-3 shadow-sm border border-gray-100 flex-row gap-3">
      {/* Date badge */}
      <View className="items-center justify-center bg-primary-50 rounded-xl px-3 py-2 min-w-[52px]">
        <Text className="text-xs font-semibold text-primary-600 uppercase">{dayName}</Text>
        <Text className="text-lg font-bold text-primary-700 leading-tight">{dayNum}</Text>
        <Text className="text-xs text-primary-500">{monthName}</Text>
      </View>

      {/* Content */}
      <View className="flex-1 gap-1.5">
        {/* Time range */}
        <Text
          className={`text-base font-bold ${isCancelled ? 'text-gray-400 line-through' : 'text-gray-900'}`}
        >
          {formatTime(item.startTime)}–{formatTime(item.endTime)}
        </Text>

        {/* Location */}
        <View className="flex-row items-center gap-1">
          <Ionicons name="location-outline" size={13} color="#9ca3af" />
          <Text className="text-sm text-gray-500" numberOfLines={1}>
            {item.locationId}
          </Text>
        </View>

        {/* Break */}
        {item.breakMinutes > 0 && (
          <Text className="text-xs text-gray-400">Перерва: {item.breakMinutes} хв</Text>
        )}

        {/* Notes */}
        {item.notes ? (
          <Text className="text-xs text-gray-400 italic" numberOfLines={2}>
            {item.notes}
          </Text>
        ) : null}

        {/* Status badge */}
        <View className="flex-row mt-0.5">
          <View className={`px-2 py-0.5 rounded-full self-start ${statusStyle}`}>
            <Text className="text-xs font-medium">{statusLabel}</Text>
          </View>
        </View>
      </View>
    </View>
  );
}
