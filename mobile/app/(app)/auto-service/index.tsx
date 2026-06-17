import { useState } from 'react';
import {
  View,
  Text,
  FlatList,
  TouchableOpacity,
  ActivityIndicator,
  ScrollView,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useWorkOrders } from '@/features/auto-service/hooks/useAutoService';
import { WorkOrderCard } from '@/features/auto-service/components/WorkOrderCard';
import type { WorkOrderStatus } from '@/features/auto-service/types';
import { STATUS_LABELS } from '@/features/auto-service/types';

const FILTER_TABS: Array<{ key: WorkOrderStatus | 'all'; label: string }> = [
  { key: 'all', label: 'Всі' },
  { key: 'new', label: STATUS_LABELS.new },
  { key: 'in_progress', label: STATUS_LABELS.in_progress },
  { key: 'waiting_parts', label: STATUS_LABELS.waiting_parts },
  { key: 'done', label: STATUS_LABELS.done },
];

export default function AutoServiceListScreen() {
  const router = useRouter();
  const [activeFilter, setActiveFilter] = useState<WorkOrderStatus | 'all'>('all');

  const { data, isLoading, isError, refetch, isFetching } = useWorkOrders(
    activeFilter !== 'all' ? activeFilter : undefined
  );

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="px-4 pt-4 pb-2 flex-row items-center justify-between">
        <View className="flex-row items-center gap-3">
          <TouchableOpacity
            onPress={() => router.back()}
            className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="arrow-back" size={20} color="#374151" />
          </TouchableOpacity>
          <Text className="text-2xl font-bold text-gray-900">Авто Сервіс</Text>
        </View>
        <TouchableOpacity
          onPress={() => router.push('/(app)/auto-service/customers')}
          className="w-10 h-10 bg-gray-100 rounded-full items-center justify-center"
        >
          <Ionicons name="people-outline" size={20} color="#374151" />
        </TouchableOpacity>
      </View>

      {/* Filter tabs */}
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerClassName="px-4 pb-3 gap-2"
        className="flex-none"
      >
        {FILTER_TABS.map((tab) => (
          <TouchableOpacity
            key={tab.key}
            onPress={() => setActiveFilter(tab.key)}
            className={`px-4 py-2 rounded-full border ${
              activeFilter === tab.key
                ? 'border-primary-600 bg-primary-600'
                : 'border-gray-200 bg-white'
            }`}
          >
            <Text
              className={`text-sm font-medium ${
                activeFilter === tab.key ? 'text-white' : 'text-gray-600'
              }`}
            >
              {tab.label}
            </Text>
          </TouchableOpacity>
        ))}
      </ScrollView>

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
      ) : (
        <FlatList
          data={data}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <WorkOrderCard
              item={item}
              onPress={() => router.push(`/(app)/auto-service/${item.id}`)}
            />
          )}
          ItemSeparatorComponent={() => <View className="h-2" />}
          contentContainerClassName="px-4 pb-6 pt-2"
          refreshing={isFetching && !isLoading}
          onRefresh={() => { void refetch(); }}
          ListEmptyComponent={
            <View className="items-center justify-center py-20">
              <Ionicons name="construct-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 text-base mt-3">Нарядів немає</Text>
            </View>
          }
        />
      )}
    </SafeAreaView>
  );
}
