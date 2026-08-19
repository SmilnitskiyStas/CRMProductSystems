import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import { ActivityIndicator, FlatList, Image, Pressable, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useConsumerPromotions, useSelectedConsumerContext } from '@/features/consumer-content/hooks';
import { registerConsumerProduct } from '@/features/shopping/products';
import type { NewsPromotionProduct } from '@/features/loyalty/news';
import { trackConsumerEvent } from '@/features/consumer-analytics/analytics';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import { ConfiguredRetailPage } from '@/features/server-driven-ui/ConfiguredRetailPage';

function price(value: number | null) {
  return value === null ? '—' : value.toLocaleString('uk-UA', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function StaticPromotionsScreen() {
  const router = useRouter();
  const { context, membership, membershipsQuery } = useSelectedConsumerContext();
  const query = useConsumerPromotions(context);

  function openProduct(product: NewsPromotionProduct) {
    if (context) void trackConsumerEvent('promotion_opened', context.tenantId, { promotionId: product.id });
    registerConsumerProduct(product, context?.tenantId);
    router.push({ pathname: '/(personal)/product/[id]', params: { id: product.id, source: 'promotion' } });
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50" edges={['top', 'left', 'right']}>
      <View className="px-4 pb-3 pt-2">
        <Text className="text-3xl font-bold text-gray-900">Акції</Text>
        <Text className="mt-1 text-sm text-gray-500">{membership?.preferredStoreName ?? 'Оберіть магазин на головній'}</Text>
      </View>
      {membershipsQuery.isLoading || query.isLoading ? (
        <View className="flex-1 items-center justify-center"><ActivityIndicator size="large" color="#16a34a" /></View>
      ) : !context ? (
        <View className="flex-1 items-center justify-center px-7">
          <Ionicons name="storefront-outline" size={48} color="#9ca3af" />
          <Text className="mt-4 text-center text-lg font-bold text-gray-900">Магазин не вибрано</Text>
          <Text className="mt-2 text-center text-sm text-gray-500">Оберіть мережу та магазин, щоб побачити актуальні пропозиції.</Text>
        </View>
      ) : query.isError ? (
        <View className="flex-1 items-center justify-center px-7">
          <Text className="text-center text-gray-500">Не вдалося завантажити акції</Text>
          <Pressable onPress={() => void query.refetch()} className="mt-4 rounded-xl bg-green-700 px-5 py-3"><Text className="font-bold text-white">Спробувати ще раз</Text></Pressable>
        </View>
      ) : (
        <FlatList
          data={query.data ?? []}
          keyExtractor={(item) => item.id}
          contentContainerStyle={{ padding: 16, paddingBottom: 40, gap: 12 }}
          ListEmptyComponent={<Text className="py-20 text-center text-gray-500">Активних акцій поки немає</Text>}
          renderItem={({ item }) => (
            <Pressable onPress={() => openProduct(item)} className="flex-row rounded-2xl border border-gray-100 bg-white p-3">
              <View className="h-28 w-28 items-center justify-center overflow-hidden rounded-2xl bg-green-50">
                {item.imageUrl ? <Image source={{ uri: item.imageUrl }} resizeMode="contain" className="h-full w-full" /> : <Ionicons name="pricetag-outline" size={42} color="#15803d" />}
                {item.discountPercent !== null ? <View className="absolute right-1 top-1 rounded-full bg-red-500 px-2 py-1"><Text className="text-[10px] font-bold text-white">−{item.discountPercent}%</Text></View> : null}
              </View>
              <View className="ml-3 flex-1 justify-center">
                <Text className="text-base font-bold text-gray-900" numberOfLines={2}>{item.name}</Text>
                <Text className="mt-1 text-xs text-gray-500">{item.unit}</Text>
                <View className="mt-3 flex-row items-end">
                  <Text className="text-xl font-bold text-green-700">{price(item.appPrice)} ₴</Text>
                  <Text className="mb-0.5 ml-2 text-xs text-gray-400 line-through">{price(item.regularPrice)} ₴</Text>
                </View>
              </View>
            </Pressable>
          )}
        />
      )}
    </SafeAreaView>
  );
}

export default function PromotionsScreen() {
  const { config, source } = useMobileConfig();
  if ((source === 'published' || source === 'last-valid') && config.pages.promotions) {
    return <ConfiguredRetailPage pageKey="promotions" title="Акції" />;
  }
  return <StaticPromotionsScreen />;
}
