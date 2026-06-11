import { View, Text, FlatList, ActivityIndicator, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useReceipts } from '@/features/receipt/hooks/useReceipts';
import { receiptNumber, type Receipt } from '@/features/receipt/types';

function ReceiptCard({ item, onPress }: { item: Receipt; onPress: () => void }) {
  const statusLabel: Record<Receipt['status'], string> = {
    draft: 'Очікує прийомки',
    in_transit: 'В дорозі',
    received: 'Прийнято',
    cancelled: 'Скасовано',
  };
  const statusColor: Record<Receipt['status'], string> = {
    draft: 'text-amber-700 bg-amber-100',
    in_transit: 'text-blue-700 bg-blue-100',
    received: 'text-green-700 bg-green-100',
    cancelled: 'text-red-700 bg-red-100',
  };

  return (
    <TouchableOpacity onPress={onPress} className="bg-white rounded-xl p-4">
      <View className="flex-row items-start justify-between">
        <View className="flex-1">
          <Text className="text-base font-semibold text-gray-900">№{receiptNumber(item)}</Text>
          <Text className="text-sm text-gray-500 mt-0.5">
            {item.supplierName ?? 'Без постачальника'} → {item.destinationStoreName}
          </Text>
        </View>
        <View className={`px-2 py-1 rounded-full ${statusColor[item.status] ?? 'text-gray-500 bg-gray-100'}`}>
          <Text className="text-xs font-medium">{statusLabel[item.status] ?? item.status}</Text>
        </View>
      </View>
      <Text className="text-xs text-gray-400 mt-2">
        {item.items.length} позицій ·{' '}
        {new Date(item.createdAt).toLocaleDateString('uk-UA')}
      </Text>
    </TouchableOpacity>
  );
}

export default function ReceiptListScreen() {
  const router = useRouter();
  const { data, isLoading, isError, refetch } = useReceipts();

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="px-4 pt-4 pb-2">
        <Text className="text-2xl font-bold text-gray-900">Прийомка</Text>
      </View>

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
            <ReceiptCard
              item={item}
              onPress={() => router.push(`/(app)/receipt/${item.id}`)}
            />
          )}
          ItemSeparatorComponent={() => <View className="h-2" />}
          contentContainerClassName="px-4 pb-4"
          refreshing={false}
          onRefresh={() => { void refetch(); }}
          ListEmptyComponent={
            <View className="flex-1 items-center justify-center py-20">
              <Text className="text-gray-400">Прийомок не знайдено</Text>
            </View>
          }
        />
      )}
    </SafeAreaView>
  );
}
