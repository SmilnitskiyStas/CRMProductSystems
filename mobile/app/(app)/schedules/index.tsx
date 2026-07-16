import { useState, useMemo } from 'react';
import {
  View,
  Text,
  FlatList,
  ActivityIndicator,
  TouchableOpacity,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import { useMyShifts, useSchedules } from '@/features/schedules/hooks/useSchedules';
import { ShiftCard } from '@/features/schedules/components/ShiftCard';
import { ScheduleCard } from '@/features/schedules/components/ScheduleCard';
import type { ScheduleShift, WorkSchedule } from '@/features/schedules/types';
import { AT_LEAST_STORE_MANAGER_OR_PROVIDER, hasRole } from '@/lib/roles';

const UA_DAYS = ['Нд', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];
const UA_MONTHS = [
  'січ', 'лют', 'бер', 'квіт', 'трав', 'черв',
  'лип', 'серп', 'вер', 'жовт', 'лист', 'груд',
];

type Tab = 'my' | 'all';

function getMonday(date: Date): Date {
  const d = new Date(date);
  const day = d.getDay();
  const diff = d.getDate() - day + (day === 0 ? -6 : 1);
  d.setDate(diff);
  return d;
}

function toDateStr(d: Date): string {
  return d.toISOString().slice(0, 10);
}

function addDays(d: Date, n: number): Date {
  const result = new Date(d);
  result.setDate(result.getDate() + n);
  return result;
}

function formatWeekLabel(monday: Date): string {
  const sunday = addDays(monday, 6);
  const monDay = monday.getDate();
  const sunDay = sunday.getDate();
  const monMonth = UA_MONTHS[monday.getMonth()];
  const sunMonth = UA_MONTHS[sunday.getMonth()];
  const monName = UA_DAYS[monday.getDay()];
  const sunName = UA_DAYS[sunday.getDay()];

  if (monday.getMonth() === sunday.getMonth()) {
    return `${monName} ${monDay} – ${sunName} ${sunDay} ${sunMonth}`;
  }
  return `${monName} ${monDay} ${monMonth} – ${sunName} ${sunDay} ${sunMonth}`;
}

export default function SchedulesScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const isManager = hasRole(user?.role, AT_LEAST_STORE_MANAGER_OR_PROVIDER);

  const [activeTab, setActiveTab] = useState<Tab>('my');
  const [weekOffset, setWeekOffset] = useState(0);

  const currentMonday = useMemo(() => {
    const base = getMonday(new Date());
    return addDays(base, weekOffset * 7);
  }, [weekOffset]);

  const weekFrom = toDateStr(currentMonday);
  const weekTo = toDateStr(addDays(currentMonday, 6));
  const weekLabel = formatWeekLabel(currentMonday);

  const myShiftsQuery = useMyShifts(weekFrom, weekTo);
  const schedulesQuery = useSchedules();

  const shifts: ScheduleShift[] = useMemo(() => {
    const list = myShiftsQuery.data ?? [];
    return [...list].sort((a, b) => {
      if (a.shiftDate !== b.shiftDate) return a.shiftDate.localeCompare(b.shiftDate);
      return a.startTime.localeCompare(b.startTime);
    });
  }, [myShiftsQuery.data]);

  const schedules: WorkSchedule[] = schedulesQuery.data ?? [];

  const activeQuery = activeTab === 'my' ? myShiftsQuery : schedulesQuery;
  const { isLoading, isError, refetch } = activeQuery;

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="px-4 pt-4 pb-3 flex-row items-center gap-3">
        <TouchableOpacity
          onPress={() => router.back()}
          className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
        >
          <Ionicons name="arrow-back" size={20} color="#374151" />
        </TouchableOpacity>
        <Text className="text-2xl font-bold text-gray-900 flex-1">Розклад</Text>
      </View>

      {/* Tabs (managers only) */}
      {isManager && (
        <View className="flex-row mx-4 mb-3 bg-gray-100 rounded-xl p-1">
          <TouchableOpacity
            onPress={() => setActiveTab('my')}
            className={`flex-1 py-2 rounded-lg items-center ${
              activeTab === 'my' ? 'bg-white shadow-sm' : ''
            }`}
          >
            <Text
              className={`text-sm font-semibold ${
                activeTab === 'my' ? 'text-gray-900' : 'text-gray-500'
              }`}
            >
              Мій розклад
            </Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={() => setActiveTab('all')}
            className={`flex-1 py-2 rounded-lg items-center ${
              activeTab === 'all' ? 'bg-white shadow-sm' : ''
            }`}
          >
            <Text
              className={`text-sm font-semibold ${
                activeTab === 'all' ? 'text-gray-900' : 'text-gray-500'
              }`}
            >
              Всі розклади
            </Text>
          </TouchableOpacity>
        </View>
      )}

      {/* Week navigator (only in "my" tab) */}
      {activeTab === 'my' && (
        <View className="flex-row items-center justify-between mx-4 mb-3 bg-white rounded-xl px-3 py-2.5 border border-gray-100">
          <TouchableOpacity
            onPress={() => setWeekOffset((o) => o - 1)}
            className="w-8 h-8 items-center justify-center"
          >
            <Ionicons name="chevron-back" size={20} color="#374151" />
          </TouchableOpacity>
          <Text className="text-sm font-semibold text-gray-700">{weekLabel}</Text>
          <TouchableOpacity
            onPress={() => setWeekOffset((o) => o + 1)}
            className="w-8 h-8 items-center justify-center"
          >
            <Ionicons name="chevron-forward" size={20} color="#374151" />
          </TouchableOpacity>
        </View>
      )}

      {/* Content */}
      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : isError ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-red-500 text-center">Помилка завантаження</Text>
          <TouchableOpacity onPress={() => { void refetch(); }} className="mt-4">
            <Text className="text-primary-600 font-medium">Спробувати знову</Text>
          </TouchableOpacity>
        </View>
      ) : activeTab === 'my' ? (
        <FlatList
          data={shifts}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <ShiftCard item={item} />}
          ItemSeparatorComponent={() => <View className="h-2" />}
          contentContainerClassName="px-4 pb-24 pt-2"
          refreshing={false}
          onRefresh={() => { void refetch(); }}
          ListEmptyComponent={
            <View className="items-center justify-center py-20">
              <Ionicons name="calendar-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 text-base mt-3">Немає змін на цей тиждень</Text>
            </View>
          }
        />
      ) : (
        <FlatList
          data={schedules}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <ScheduleCard
              item={item}
              onPress={() => router.push(`/(app)/schedules/${item.id}`)}
            />
          )}
          ItemSeparatorComponent={() => <View className="h-2" />}
          contentContainerClassName="px-4 pb-24 pt-2"
          refreshing={false}
          onRefresh={() => { void refetch(); }}
          ListEmptyComponent={
            <View className="items-center justify-center py-20">
              <Ionicons name="calendar-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 text-base mt-3">Немає розкладів</Text>
            </View>
          }
        />
      )}
    </SafeAreaView>
  );
}
