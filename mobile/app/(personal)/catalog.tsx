import { useDeferredValue, useState } from 'react';
import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import {
  ActivityIndicator,
  FlatList,
  Image,
  Pressable,
  ScrollView,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useConsumerCatalog, useSelectedConsumerContext } from '@/features/consumer-content/hooks';
import { registerConsumerProduct } from '@/features/shopping/products';
import type { ConsumerCatalogItem } from '@/features/consumer-content/types';
import type { NewsPromotionProduct } from '@/features/loyalty/news';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import { ConfiguredRetailPage } from '@/features/server-driven-ui/ConfiguredRetailPage';

function formatPrice(value: number | null) {
  if (value === null) return 'Ціну уточнюйте';
  return `${value.toLocaleString('uk-UA', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })} ₴`;
}

function mapCatalogProduct(item: ConsumerCatalogItem): NewsPromotionProduct {
  return {
    id: item.id,
    barcode: null,
    name: item.name,
    unit: item.unit,
    regularPrice: item.priceRetail,
    appPrice: null,
    discountPercent: null,
    imageUrl: item.imageUrl,
    icon: 'cube-outline',
    background: '#f3f4f6',
    manufacturer: null,
    countryOfOrigin: null,
  };
}

function StaticConsumerCatalogScreen() {
  const router = useRouter();
  const [search, setSearch] = useState('');
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const deferredSearch = useDeferredValue(search.trim());
  const { context, membership: selectedMembership, membershipsQuery } = useSelectedConsumerContext();
  const categoriesQuery = useConsumerCatalog(context, { page: 1, pageSize: 100 });
  const catalogQuery = useConsumerCatalog(context, {
    search: deferredSearch || undefined,
    categoryId: categoryId || undefined,
    page,
    pageSize: 20,
  });
  const categories = Array.from(
    new Map(
      (categoriesQuery.data?.items ?? [])
        .filter((item) => item.categoryId && item.categoryName)
        .map((item) => [item.categoryId as string, item.categoryName as string])
    )
  );

  function openProduct(item: ConsumerCatalogItem) {
    const product = mapCatalogProduct(item);
    registerConsumerProduct(product, context?.tenantId);
    router.push({ pathname: '/(personal)/product/[id]', params: { id: item.id, source: 'catalog' } });
  }

  if (membershipsQuery.isLoading) {
    return (
      <SafeAreaView className="flex-1 items-center justify-center bg-gray-50">
        <ActivityIndicator size="large" color="#16a34a" />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50" edges={['top', 'left', 'right']}>
      <View className="px-4 pb-3 pt-2">
        <Text className="text-3xl font-bold text-gray-900">Каталог</Text>
        <Text className="mt-1 text-sm text-gray-500" numberOfLines={1}>
          {selectedMembership?.preferredStoreName || 'Спочатку оберіть магазин на головній'}
        </Text>
        <View className="mt-4 flex-row items-center rounded-2xl border border-gray-200 bg-white px-4">
          <Ionicons name="search-outline" size={20} color="#6b7280" />
          <TextInput
            value={search}
            onChangeText={(value) => {
              setSearch(value);
              setPage(1);
            }}
            placeholder="Пошук товарів"
            placeholderTextColor="#9ca3af"
            className="min-h-[50px] flex-1 px-3 text-base text-gray-900"
          />
          {search ? (
            <Pressable onPress={() => setSearch('')} className="h-9 w-9 items-center justify-center">
              <Ionicons name="close-circle" size={19} color="#9ca3af" />
            </Pressable>
          ) : null}
        </View>
        {categories.length ? (
          <ScrollView horizontal showsHorizontalScrollIndicator={false} className="mt-3">
            <View className="flex-row gap-2 pr-4">
              <Pressable
                onPress={() => { setCategoryId(null); setPage(1); }}
                className={`rounded-full px-4 py-2 ${categoryId === null ? 'bg-green-700' : 'bg-white'}`}
              >
                <Text className={categoryId === null ? 'font-semibold text-white' : 'text-gray-700'}>Усі</Text>
              </Pressable>
              {categories.map(([id, name]) => (
                <Pressable
                  key={id}
                  onPress={() => { setCategoryId(id); setPage(1); }}
                  className={`rounded-full px-4 py-2 ${categoryId === id ? 'bg-green-700' : 'bg-white'}`}
                >
                  <Text className={categoryId === id ? 'font-semibold text-white' : 'text-gray-700'}>{name}</Text>
                </Pressable>
              ))}
            </View>
          </ScrollView>
        ) : null}
      </View>

      {!context ? (
        <View className="flex-1 items-center justify-center px-7">
          <Ionicons name="storefront-outline" size={48} color="#9ca3af" />
          <Text className="mt-4 text-xl font-bold text-gray-900">Магазин не вибрано</Text>
          <Text className="mt-2 text-center text-sm leading-6 text-gray-500">
            Поверніться на головну та оберіть магазин, щоб переглянути його каталог.
          </Text>
          <Pressable onPress={() => router.push('/(personal)')} className="mt-5 rounded-xl bg-green-700 px-5 py-3">
            <Text className="font-bold text-white">На головну</Text>
          </Pressable>
        </View>
      ) : catalogQuery.isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : catalogQuery.isError ? (
        <View className="flex-1 items-center justify-center px-7">
          <Ionicons name="cloud-offline-outline" size={44} color="#9ca3af" />
          <Text className="mt-4 text-lg font-bold text-gray-900">Не вдалося завантажити каталог</Text>
          <Pressable onPress={() => void catalogQuery.refetch()} className="mt-5 rounded-xl bg-green-700 px-5 py-3">
            <Text className="font-bold text-white">Спробувати ще раз</Text>
          </Pressable>
        </View>
      ) : (
        <FlatList
          data={catalogQuery.data?.items ?? []}
          keyExtractor={(item) => item.id}
          numColumns={2}
          columnWrapperStyle={{ gap: 12 }}
          contentContainerStyle={{ padding: 16, paddingBottom: 36, gap: 12 }}
          ListEmptyComponent={
            <View className="items-center py-20">
              <Ionicons name="search-outline" size={42} color="#d1d5db" />
              <Text className="mt-3 text-gray-500">Товарів не знайдено</Text>
            </View>
          }
          ListFooterComponent={
            (catalogQuery.data?.totalPages ?? 1) > 1 ? (
              <View className="col-span-2 mt-4 flex-row items-center justify-center gap-5">
                <Pressable
                  disabled={page <= 1}
                  onPress={() => setPage((current) => Math.max(1, current - 1))}
                >
                  <Ionicons name="chevron-back-circle" size={34} color={page <= 1 ? '#d1d5db' : '#16a34a'} />
                </Pressable>
                <Text className="text-sm font-semibold text-gray-600">
                  {page} / {catalogQuery.data?.totalPages}
                </Text>
                <Pressable
                  disabled={page >= (catalogQuery.data?.totalPages ?? 1)}
                  onPress={() => setPage((current) => current + 1)}
                >
                  <Ionicons
                    name="chevron-forward-circle"
                    size={34}
                    color={page >= (catalogQuery.data?.totalPages ?? 1) ? '#d1d5db' : '#16a34a'}
                  />
                </Pressable>
              </View>
            ) : null
          }
          renderItem={({ item }) => (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={`Відкрити товар: ${item.name}`}
              onPress={() => openProduct(item)}
              className="flex-1 overflow-hidden rounded-2xl border border-gray-100 bg-white p-3"
            >
              <View className="h-32 items-center justify-center overflow-hidden rounded-2xl bg-gray-100">
                {item.imageUrl ? (
                  <Image source={{ uri: item.imageUrl }} resizeMode="contain" className="h-full w-full" />
                ) : (
                  <Ionicons name="cube-outline" size={48} color="#9ca3af" />
                )}
              </View>
              <Text className="mt-3 min-h-[40px] text-sm font-bold leading-5 text-gray-900" numberOfLines={2}>
                {item.name}
              </Text>
              <Text className="mt-1 text-xs text-gray-400">{item.unit}</Text>
              <Text className="mt-3 text-base font-bold text-green-700">{formatPrice(item.priceRetail)}</Text>
              <View className={`mt-2 self-start rounded-full px-2 py-1 ${
                item.isAvailableAtStore ? 'bg-green-50' : 'bg-gray-100'
              }`}>
                <Text className={`text-[10px] font-semibold ${
                  item.isAvailableAtStore ? 'text-green-700' : 'text-gray-500'
                }`}>
                  {item.isAvailableAtStore ? 'Є в магазині' : 'Немає в наявності'}
                </Text>
              </View>
            </Pressable>
          )}
        />
      )}
    </SafeAreaView>
  );
}

export default function ConsumerCatalogScreen() {
  const { config, source } = useMobileConfig();
  if ((source === 'published' || source === 'last-valid') && config.pages.catalog) {
    return <ConfiguredRetailPage pageKey="catalog" title="Каталог" />;
  }
  return <StaticConsumerCatalogScreen />;
}
