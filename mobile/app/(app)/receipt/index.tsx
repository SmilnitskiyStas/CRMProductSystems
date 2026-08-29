import { useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, Text, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import { useAwaitingReceiptOrders } from '@/features/marketplace-orders/hooks/useMarketplaceOrders';
import type { MarketplaceOrder } from '@/features/marketplace-orders/types';
import { useModulesSettings } from '@/features/navigation/hooks';
import { useReceipts } from '@/features/receipt/hooks/useReceipts';
import { receiptNumber, type Receipt } from '@/features/receipt/types';
import { useWorkspaceLocationStore } from '@/features/locations/store';

type Filter = 'all' | 'receipt' | 'marketplace';
type ReceivingEntry =
  | { key: string; kind: 'receipt'; date: string; receipt: Receipt }
  | { key: string; kind: 'marketplace'; date: string; order: MarketplaceOrder };

const FILTERS: { value: Filter; label: string }[] = [
  { value: 'all', label: 'Усі' },
  { value: 'receipt', label: 'Поставки' },
  { value: 'marketplace', label: 'Marketplace' },
];

function StandardReceiptCard({ item, onPress }: { item: Receipt; onPress: () => void }) {
  const statusLabel: Record<Receipt['status'], string> = {
    draft: 'Очікує приймання', in_transit: 'В дорозі', received: 'Прийнято', cancelled: 'Скасовано',
  };
  const statusColor: Record<Receipt['status'], string> = {
    draft: 'text-amber-700 bg-amber-100', in_transit: 'text-blue-700 bg-blue-100',
    received: 'text-green-700 bg-green-100', cancelled: 'text-red-700 bg-red-100',
  };
  return (
    <TouchableOpacity onPress={onPress} className="bg-white rounded-xl p-4" accessibilityRole="button">
      <View className="flex-row items-start justify-between gap-3">
        <View className="flex-1">
          <View className="flex-row items-center">
            <View className="bg-gray-100 rounded-md px-2 py-1 mr-2"><Text className="text-[10px] font-semibold text-gray-600">ПОСТАВКА</Text></View>
            <Text className="text-base font-semibold text-gray-900">№{receiptNumber(item)}</Text>
          </View>
          <Text className="text-sm text-gray-500 mt-2">{item.supplierName ?? 'Без постачальника'} → {item.destinationLocationName}</Text>
        </View>
        <View className={`px-2 py-1 rounded-full ${statusColor[item.status]}`}><Text className="text-xs font-medium">{statusLabel[item.status]}</Text></View>
      </View>
      <View className="flex-row items-center mt-3">
        <Ionicons name="cube-outline" size={14} color="#9ca3af" />
        <Text className="text-xs text-gray-400 ml-1">{item.items.length} позицій · {new Date(item.createdAt).toLocaleDateString('uk-UA')}</Text>
        <Ionicons name="chevron-forward" size={16} color="#d1d5db" style={{ marginLeft: 'auto' }} />
      </View>
    </TouchableOpacity>
  );
}

function MarketplaceReceiptCard({ item, onPress }: { item: MarketplaceOrder; onPress: () => void }) {
  return (
    <TouchableOpacity onPress={onPress} className="bg-white rounded-xl p-4 border border-blue-100" accessibilityRole="button">
      <View className="flex-row items-start justify-between gap-3">
        <View className="flex-1">
          <View className="flex-row items-center">
            <View className="bg-blue-100 rounded-md px-2 py-1 mr-2"><Text className="text-[10px] font-semibold text-blue-700">MARKETPLACE</Text></View>
            <Text className="text-base font-semibold text-gray-900">{item.orderNumber}</Text>
          </View>
          <Text className="text-sm text-gray-500 mt-2">{item.supplierName}</Text>
        </View>
        <View className="bg-blue-100 px-2 py-1 rounded-full"><Text className="text-xs font-medium text-blue-700">Відправлено</Text></View>
      </View>
      <View className="flex-row items-center mt-3">
        <Ionicons name="scan-outline" size={14} color="#0284c7" />
        <Text className="text-xs text-gray-400 ml-1">{item.items.length} позицій{item.shippedAt ? ` · ${new Date(item.shippedAt).toLocaleDateString('uk-UA')}` : ''}</Text>
        <Ionicons name="chevron-forward" size={16} color="#d1d5db" style={{ marginLeft: 'auto' }} />
      </View>
    </TouchableOpacity>
  );
}

export default function ReceivingListScreen() {
  const router = useRouter();
  const [filter, setFilter] = useState<Filter>('all');
  const user = useAuthStore((state) => state.user);
  const selectedLocationId = useWorkspaceLocationStore((state) => state.selectedLocationId);
  const locationId = selectedLocationId === undefined ? user?.locationId : selectedLocationId;
  const modulesQuery = useModulesSettings(Boolean(user?.tenantId));
  const marketplaceEnabled = modulesQuery.data?.modules.includes('marketplace') ?? false;
  const receiptsQuery = useReceipts(locationId ?? undefined);
  const marketplaceQuery = useAwaitingReceiptOrders(marketplaceEnabled);

  const entries = useMemo<ReceivingEntry[]>(() => {
    const standard: ReceivingEntry[] = (receiptsQuery.data ?? []).map((receipt) => ({
      key: `receipt:${receipt.id}`, kind: 'receipt', date: receipt.createdAt, receipt,
    }));
    const marketplace: ReceivingEntry[] = (marketplaceQuery.data ?? [])
      .filter((order) => !locationId || order.destinationStoreId === locationId)
      .map((order) => ({ key: `marketplace:${order.id}`, kind: 'marketplace', date: order.shippedAt ?? '', order }));
    return [...standard, ...marketplace]
      .filter((entry) => filter === 'all' || entry.kind === filter)
      .sort((a, b) => b.date.localeCompare(a.date));
  }, [filter, locationId, marketplaceQuery.data, receiptsQuery.data]);

  const isLoading = receiptsQuery.isLoading || (marketplaceEnabled && marketplaceQuery.isLoading);
  const allFailed = receiptsQuery.isError && (!marketplaceEnabled || marketplaceQuery.isError);
  const isRefreshing = receiptsQuery.isRefetching || marketplaceQuery.isRefetching;
  function refresh() {
    void receiptsQuery.refetch();
    if (marketplaceEnabled) void marketplaceQuery.refetch();
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="px-4 pt-4 pb-2">
        <Text className="text-2xl font-bold text-gray-900">Приймання</Text>
        <Text className="text-sm text-gray-500 mt-1">Поставки та замовлення в одному місці</Text>
      </View>
      <View className="px-4 py-2">
        <FlatList
          horizontal showsHorizontalScrollIndicator={false}
          data={FILTERS.filter((item) => item.value !== 'marketplace' || marketplaceEnabled)}
          keyExtractor={(item) => item.value} contentContainerClassName="gap-2"
          renderItem={({ item }) => (
            <TouchableOpacity onPress={() => setFilter(item.value)} className={`px-4 py-2 rounded-full border ${filter === item.value ? 'bg-primary-600 border-primary-600' : 'bg-white border-gray-200'}`}>
              <Text className={`text-sm font-semibold ${filter === item.value ? 'text-white' : 'text-gray-600'}`}>{item.label}</Text>
            </TouchableOpacity>
          )}
        />
      </View>
      {isLoading ? (
        <View className="flex-1 items-center justify-center"><ActivityIndicator size="large" color="#16a34a" /></View>
      ) : allFailed ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-red-500 text-center">Не вдалося завантажити документи приймання</Text>
          <TouchableOpacity onPress={refresh} className="mt-4"><Text className="text-primary-600 font-medium">Спробувати знову</Text></TouchableOpacity>
        </View>
      ) : (
        <FlatList
          data={entries} keyExtractor={(item) => item.key}
          renderItem={({ item }) => item.kind === 'receipt' ? (
            <StandardReceiptCard item={item.receipt} onPress={() => router.push(`/(app)/receipt/${item.receipt.id}`)} />
          ) : (
            <MarketplaceReceiptCard item={item.order} onPress={() => router.push(`/(app)/marketplace-orders/${item.order.id}`)} />
          )}
          ItemSeparatorComponent={() => <View className="h-3" />} contentContainerClassName="px-4 pt-2 pb-6"
          refreshing={isRefreshing} onRefresh={refresh}
          ListEmptyComponent={<View className="items-center justify-center py-20"><Ionicons name="checkmark-circle-outline" size={48} color="#d1d5db" /><Text className="text-gray-400 mt-3">Документів не знайдено</Text></View>}
        />
      )}
    </SafeAreaView>
  );
}
