import { ActivityIndicator, FlatList, Text, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Stack, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAwaitingReceiptOrders } from '@/features/marketplace-orders/hooks/useMarketplaceOrders';
import type { MarketplaceOrder } from '@/features/marketplace-orders/types';

function OrderCard({ order, onPress }: { order: MarketplaceOrder; onPress: () => void }) {
  return (
    <TouchableOpacity onPress={onPress} className="bg-white rounded-2xl p-4 border border-gray-100">
      <View className="flex-row justify-between items-start">
        <View className="flex-1 mr-3">
          <Text className="font-bold text-gray-900">{order.orderNumber}</Text>
          <Text className="text-sm text-gray-600 mt-1">{order.supplierName}</Text>
        </View>
        <View className="bg-blue-100 px-2 py-1 rounded-full">
          <Text className="text-xs font-medium text-blue-700">Відправлено</Text>
        </View>
      </View>
      <View className="flex-row items-center mt-3">
        <Ionicons name="cube-outline" size={15} color="#6b7280" />
        <Text className="text-xs text-gray-500 ml-1">{order.items.length} позицій</Text>
        {order.shippedAt ? (
          <Text className="text-xs text-gray-400 ml-3">
            {new Date(order.shippedAt).toLocaleDateString('uk-UA')}
          </Text>
        ) : null}
      </View>
    </TouchableOpacity>
  );
}

export default function MarketplaceOrdersScreen() {
  const router = useRouter();
  const query = useAwaitingReceiptOrders();
  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <Stack.Screen options={{ headerShown: true, title: 'Приймання замовлень', headerBackTitle: '' }} />
      {query.isLoading ? (
        <View className="flex-1 items-center justify-center"><ActivityIndicator size="large" color="#16a34a" /></View>
      ) : query.isError ? (
        <View className="flex-1 items-center justify-center px-6">
          <Text className="text-red-600 text-center">Не вдалося завантажити замовлення</Text>
          <TouchableOpacity onPress={() => void query.refetch()} className="mt-4"><Text className="text-primary-600">Спробувати знову</Text></TouchableOpacity>
        </View>
      ) : (
        <FlatList
          data={query.data}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <OrderCard order={item} onPress={() => router.push(`/(app)/marketplace-orders/${item.id}`)} />}
          ItemSeparatorComponent={() => <View className="h-3" />}
          contentContainerClassName="p-4"
          onRefresh={() => void query.refetch()}
          refreshing={query.isRefetching}
          ListEmptyComponent={<View className="items-center py-24"><Ionicons name="checkmark-circle-outline" size={48} color="#9ca3af" /><Text className="text-gray-500 mt-3">Немає замовлень, що очікують приймання</Text></View>}
        />
      )}
    </SafeAreaView>
  );
}
