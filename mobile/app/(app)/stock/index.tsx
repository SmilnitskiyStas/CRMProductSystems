import { View, Text, FlatList, ActivityIndicator, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useStock } from '@/features/stock/hooks/useStock';
import { StockBatchCard } from '@/features/stock/components/StockBatchCard';
import type { StockStatus } from '@/features/stock/types';

const STATUS_FILTERS: { label: string; value: StockStatus | 'all' }[] = [
  { label: 'Всі', value: 'all' },
  { label: 'Норма', value: 'safe' },
  { label: 'Увага', value: 'warning' },
  { label: 'Критично', value: 'critical' },
  { label: 'Прострочено', value: 'expired' },
];

export default function StockScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ status?: StockStatus }>();
  const activeStatus = params.status ?? 'all';

  const { data, isLoading, isError, refetch } = useStock(
    activeStatus !== 'all' ? { status: activeStatus } : undefined
  );

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="px-4 pt-4 pb-2">
        <Text className="text-2xl font-bold text-gray-900">Залишки</Text>
      </View>

      {/* Filter chips */}
      <View className="px-4 mb-2">
        <FlatList
          horizontal
          showsHorizontalScrollIndicator={false}
          data={STATUS_FILTERS}
          keyExtractor={(item) => item.value}
          contentContainerClassName="gap-2 py-1"
          renderItem={({ item }) => (
            <TouchableOpacity
              onPress={() =>
                router.setParams({ status: item.value === 'all' ? undefined : item.value })
              }
              className={`px-4 py-1.5 rounded-full border ${
                activeStatus === item.value
                  ? 'bg-primary-600 border-primary-600'
                  : 'bg-white border-gray-200'
              }`}
            >
              <Text
                className={`text-sm font-medium ${
                  activeStatus === item.value ? 'text-white' : 'text-gray-600'
                }`}
              >
                {item.label}
              </Text>
            </TouchableOpacity>
          )}
        />
      </View>

      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : isError ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-red-500 text-center">Помилка завантаження даних</Text>
          <TouchableOpacity onPress={() => { void refetch(); }} className="mt-4">
            <Text className="text-primary-600 font-medium">Спробувати знову</Text>
          </TouchableOpacity>
        </View>
      ) : (
        <FlatList
          data={data}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <StockBatchCard
              item={item}
              onPress={() => router.push(`/(app)/stock/${item.id}`)}
            />
          )}
          ItemSeparatorComponent={() => <View className="h-2" />}
          contentContainerClassName="px-4 pb-4"
          refreshing={false}
          onRefresh={() => { void refetch(); }}
          ListEmptyComponent={
            <View className="flex-1 items-center justify-center py-20">
              <Text className="text-gray-400 text-base">Партій не знайдено</Text>
            </View>
          }
        />
      )}
    </SafeAreaView>
  );
}
