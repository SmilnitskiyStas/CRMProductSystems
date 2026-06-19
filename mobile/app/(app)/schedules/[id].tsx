import { View, Text, ScrollView, ActivityIndicator, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSchedule } from '@/features/schedules/hooks/useSchedules';
import type { ScheduleShift } from '@/features/schedules/types';

const UA_DAYS = ['Нд', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];
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

function formatShiftDate(dateStr: string): string {
  const d = new Date(dateStr + 'T00:00:00');
  const dayName = UA_DAYS[d.getDay()];
  const dayNum = d.getDate();
  const monthName = UA_MONTHS[d.getMonth()];
  return `${dayName}, ${dayNum} ${monthName}`;
}

function formatWeekRange(weekStart: string): string {
  const start = new Date(weekStart + 'T00:00:00');
  const end = new Date(start);
  end.setDate(start.getDate() + 6);
  const startStr = `${start.getDate()} ${UA_MONTHS[start.getMonth()]}`;
  const endStr = `${end.getDate()} ${UA_MONTHS[end.getMonth()]} ${end.getFullYear()}`;
  return `${startStr} – ${endStr}`;
}

function groupShiftsByDate(shifts: ScheduleShift[]): Array<{ date: string; shifts: ScheduleShift[] }> {
  const map = new Map<string, ScheduleShift[]>();
  const sorted = [...shifts].sort((a, b) => {
    if (a.shiftDate !== b.shiftDate) return a.shiftDate.localeCompare(b.shiftDate);
    return a.startTime.localeCompare(b.startTime);
  });
  for (const shift of sorted) {
    const existing = map.get(shift.shiftDate);
    if (existing) {
      existing.push(shift);
    } else {
      map.set(shift.shiftDate, [shift]);
    }
  }
  return Array.from(map.entries()).map(([date, s]) => ({ date, shifts: s }));
}

interface ShiftRowProps {
  shift: ScheduleShift;
}

function ShiftRow({ shift }: ShiftRowProps) {
  const statusStyle = SHIFT_STATUS_STYLES[shift.status] ?? 'bg-gray-100 text-gray-500';
  const statusLabel = SHIFT_STATUS_LABELS[shift.status] ?? shift.status;

  return (
    <View className="bg-gray-50 rounded-lg px-3 py-2.5 flex-row items-center gap-3">
      <View className="flex-1">
        <Text className="text-sm font-semibold text-gray-900">{shift.userName}</Text>
        <Text className="text-xs text-gray-500 mt-0.5">
          {formatTime(shift.startTime)}–{formatTime(shift.endTime)}
          {shift.breakMinutes > 0 ? ` · Перерва: ${shift.breakMinutes} хв` : ''}
        </Text>
      </View>
      <View className={`px-2 py-0.5 rounded-full ${statusStyle}`}>
        <Text className="text-xs font-medium">{statusLabel}</Text>
      </View>
    </View>
  );
}

export default function ScheduleDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();

  const { data, isLoading, isError } = useSchedule(id);

  if (isLoading) {
    return (
      <SafeAreaView className="flex-1 bg-white items-center justify-center">
        <ActivityIndicator size="large" color="#16a34a" />
      </SafeAreaView>
    );
  }

  if (isError || !data) {
    return (
      <SafeAreaView className="flex-1 bg-white items-center justify-center px-4">
        <Text className="text-red-500 text-center">Не вдалося завантажити розклад</Text>
        <TouchableOpacity onPress={() => router.back()} className="mt-4">
          <Text className="text-primary-600 font-medium">Назад</Text>
        </TouchableOpacity>
      </SafeAreaView>
    );
  }

  const statusStyle = SCHEDULE_STATUS_STYLES[data.status] ?? 'bg-gray-100 text-gray-600';
  const statusLabel = SCHEDULE_STATUS_LABELS[data.status] ?? data.status;
  const weekRange = formatWeekRange(data.weekStart);
  const groupedShifts = groupShiftsByDate(data.shifts);

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="bg-white px-4 pt-4 pb-3 border-b border-gray-100">
        <View className="flex-row items-center gap-3">
          <TouchableOpacity
            onPress={() => router.back()}
            className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="arrow-back" size={20} color="#374151" />
          </TouchableOpacity>
          <View className="flex-1">
            <Text className="text-base font-bold text-gray-900" numberOfLines={1}>
              {data.name}
            </Text>
            <Text className="text-xs text-gray-500">{data.locationName}</Text>
          </View>
        </View>
      </View>

      <ScrollView contentContainerClassName="p-4 gap-4">
        {/* Status + week range */}
        <View className="flex-row items-center gap-2 flex-wrap">
          <View className={`px-3 py-1 rounded-full ${statusStyle}`}>
            <Text className="text-sm font-semibold">{statusLabel}</Text>
          </View>
          <Text className="text-sm text-gray-500">{weekRange}</Text>
        </View>

        {/* Shifts section */}
        <View>
          <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-3">
            Зміни ({data.shifts.length})
          </Text>

          {groupedShifts.length === 0 ? (
            <View className="bg-white rounded-xl p-6 items-center">
              <Ionicons name="calendar-outline" size={32} color="#d1d5db" />
              <Text className="text-gray-400 text-sm mt-2">Немає змін</Text>
            </View>
          ) : (
            <View className="gap-4">
              {groupedShifts.map(({ date, shifts }) => (
                <View key={date}>
                  {/* Date section header */}
                  <Text className="text-xs font-bold text-gray-500 uppercase mb-2">
                    {formatShiftDate(date)}
                  </Text>
                  <View className="gap-2">
                    {shifts.map((shift) => (
                      <ShiftRow key={shift.id} shift={shift} />
                    ))}
                  </View>
                </View>
              ))}
            </View>
          )}
        </View>

        <View className="h-4" />
      </ScrollView>
    </SafeAreaView>
  );
}
