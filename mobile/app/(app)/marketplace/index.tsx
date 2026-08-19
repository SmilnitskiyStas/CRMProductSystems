import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  FlatList,
  ActivityIndicator,
  RefreshControl,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import { useState } from 'react';
import { useSuppliers, useSearchSuppliers } from '@/features/marketplace/hooks/useMarketplace';
import type { SupplierListItem } from '@/features/marketplace/types';
import { OfflineReadStatus } from '@/features/offline-read-cache/OfflineReadStatus';
import { useOfflineReadUx } from '@/features/offline-read-cache/ux';
import { useAuthStore } from '@/features/auth/store';

function StarRating({ value }: { value: number | null }) {
  if (value === null) return <Text className="text-xs text-gray-400">—</Text>;
  const full = Math.round(value);
  return (
    <View className="flex-row gap-0.5">
      {[1, 2, 3, 4, 5].map((i) => (
        <Ionicons
          key={i}
          name={i <= full ? 'star' : 'star-outline'}
          size={11}
          color={i <= full ? '#f59e0b' : '#d1d5db'}
        />
      ))}
      <Text className="text-xs text-gray-500 ml-1">{value.toFixed(1)}</Text>
    </View>
  );
}

function PlanBadge({ plan }: { plan: 'free' | 'premium' }) {
  return plan === 'premium' ? (
    <View className="bg-amber-100 px-2 py-0.5 rounded-full">
      <Text className="text-xs font-semibold text-amber-700">Premium</Text>
    </View>
  ) : (
    <View className="bg-gray-100 px-2 py-0.5 rounded-full">
      <Text className="text-xs text-gray-500">Free</Text>
    </View>
  );
}

function SupplierCard({ item, offline }: { item: SupplierListItem; offline: boolean }) {
  const router = useRouter();
  return (
    <TouchableOpacity
      onPress={() => { if (!offline) router.push(`/(app)/marketplace/${item.id}` as Parameters<typeof router.push>[0]); }}
      disabled={offline}
      activeOpacity={0.7}
      className="bg-white mx-4 mb-3 rounded-2xl p-4 border border-gray-100"
    >
      <View className="flex-row items-start justify-between mb-2">
        <View className="flex-1 mr-3">
          <Text className="text-base font-semibold text-gray-900">{item.name}</Text>
          {item.region ? (
            <View className="flex-row items-center mt-0.5 gap-1">
              <Ionicons name="location-outline" size={12} color="#9ca3af" />
              <Text className="text-xs text-gray-500">{item.region}</Text>
            </View>
          ) : null}
        </View>
        <PlanBadge plan={item.plan} />
      </View>

      {item.categories && item.categories.length > 0 ? (
        <View className="flex-row flex-wrap gap-1 mb-2">
          {item.categories.slice(0, 3).map((cat) => (
            <View key={cat} className="bg-blue-50 px-2 py-0.5 rounded-full">
              <Text className="text-xs text-blue-700">{cat}</Text>
            </View>
          ))}
          {item.categories.length > 3 ? (
            <Text className="text-xs text-gray-400 self-center">+{item.categories.length - 3}</Text>
          ) : null}
        </View>
      ) : null}

      <View className="flex-row items-center justify-between">
        <StarRating value={item.rating} />
        {item.avgDeliveryDays ? (
          <View className="flex-row items-center gap-1">
            <Ionicons name="time-outline" size={12} color="#9ca3af" />
            <Text className="text-xs text-gray-500">{item.avgDeliveryDays} дн.</Text>
          </View>
        ) : null}
        <Ionicons name="chevron-forward" size={14} color="#d1d5db" />
      </View>
    </TouchableOpacity>
  );
}

export default function MarketplaceScreen() {
  const offline = useAuthStore((state) => state.hydrationStatus === 'offline_read_ready');
  const [searchText, setSearchText] = useState('');
  const [activeSearch, setActiveSearch] = useState('');

  const isSearching = activeSearch.trim().length > 0;

  const supplierParams = { page: 1, pageSize: 50 };
  const {
    data: page,
    isLoading: listLoading,
    refetch: refetchList,
    isRefetching: listRefetching,
    isError: listError,
  } = useSuppliers(supplierParams, !offline);
  const offlineState = useOfflineReadUx(['marketplace-suppliers', supplierParams], {
    hasData: page !== undefined,
    isFetching: listRefetching,
    isError: listError,
  });

  const {
    data: searchResults,
    isLoading: searchLoading,
    isRefetching: searchRefetching,
  } = useSearchSuppliers(activeSearch, undefined, !offline);

  const items: SupplierListItem[] = isSearching
    ? (searchResults ?? [])
    : (page?.items ?? []);

  const isLoading = isSearching ? searchLoading : listLoading;
  const isRefreshing = isSearching ? searchRefetching : listRefetching;

  function handleSearch() {
    setActiveSearch(searchText.trim());
  }

  function clearSearch() {
    setSearchText('');
    setActiveSearch('');
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="px-4 pt-4 pb-3">
        <Text className="text-2xl font-bold text-gray-900 mb-3">Маркетплейс</Text>
        <View className="flex-row gap-2">
          <View className="flex-1 flex-row items-center bg-white rounded-xl px-3 border border-gray-200">
            <Ionicons name="search-outline" size={18} color="#9ca3af" />
            <TextInput
              className="flex-1 ml-2 py-2.5 text-base text-gray-800"
              placeholder="Шукати постачальника або товар..."
              placeholderTextColor="#9ca3af"
              value={searchText}
              onChangeText={setSearchText}
              onSubmitEditing={handleSearch}
              returnKeyType="search"
            />
            {searchText.length > 0 ? (
              <TouchableOpacity onPress={clearSearch}>
                <Ionicons name="close-circle" size={18} color="#9ca3af" />
              </TouchableOpacity>
            ) : null}
          </View>
          <TouchableOpacity
            onPress={handleSearch}
            disabled={offline}
            className="bg-primary-600 rounded-xl px-4 items-center justify-center"
          >
            <Text className="text-white font-semibold text-sm">Знайти</Text>
          </TouchableOpacity>
        </View>
        {isSearching ? (
          <View className="flex-row items-center mt-2 gap-2">
            <Text className="text-xs text-gray-500">
              Результати для: <Text className="font-semibold">"{activeSearch}"</Text>
            </Text>
            <TouchableOpacity onPress={clearSearch}>
              <Text className="text-xs text-primary-600 font-semibold">Скинути</Text>
            </TouchableOpacity>
          </View>
        ) : null}
      </View>

      {!isSearching ? (
        <OfflineReadStatus state={offlineState} onRetry={() => { void refetchList(); }} />
      ) : null}

      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : listError && page === undefined && !isSearching ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-red-500 text-center">Не вдалося завантажити постачальників</Text>
          <TouchableOpacity accessibilityRole="button" onPress={() => { void refetchList(); }} className="min-h-[44px] mt-3 justify-center">
            <Text className="text-primary-600 font-semibold">Спробувати знову</Text>
          </TouchableOpacity>
        </View>
      ) : (
        <FlatList
          data={items}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <SupplierCard item={item} offline={offline} />}
          contentContainerClassName="pt-2 pb-6"
          refreshControl={
            <RefreshControl
              refreshing={isRefreshing}
              onRefresh={() => { if (!offline) void refetchList(); }}
              tintColor="#16a34a"
            />
          }
          ListEmptyComponent={
            <View className="flex-1 items-center justify-center py-20">
              <Ionicons name="storefront-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 mt-3 text-base">
                {isSearching ? 'Нічого не знайдено' : 'Постачальники відсутні'}
              </Text>
            </View>
          }
        />
      )}
    </SafeAreaView>
  );
}
