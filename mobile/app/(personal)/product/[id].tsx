import { useEffect, useRef, useState } from 'react';
import { Ionicons } from '@expo/vector-icons';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Alert, Image, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useSelectedConsumerContext } from '@/features/consumer-content/hooks';
import { findConsumerProduct } from '@/features/shopping/products';
import { useConsumerShoppingStore } from '@/features/shopping/store';
import { trackConsumerEvent } from '@/features/consumer-analytics/analytics';

function formatPrice(value: number | null) {
  if (value === null) return '—';
  return value.toLocaleString('uk-UA', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

export default function ConsumerProductDetailsScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id?: string | string[]; source?: string | string[] }>();
  const id = Array.isArray(params.id) ? params.id[0] : params.id;
  const [quantity, setQuantity] = useState(1);
  const { context, membership: selectedMembership } = useSelectedConsumerContext();
  const product = findConsumerProduct(id, context?.tenantId);
  const trackedKey = useRef<string | null>(null);
  const favoriteProductIds = useConsumerShoppingStore((state) => state.favoriteProductIds);
  const toggleFavorite = useConsumerShoppingStore((state) => state.toggleFavorite);
  const addToCart = useConsumerShoppingStore((state) => state.addToCart);

  useEffect(() => {
    if (!product || !context) return;
    const key = `${context.tenantId}:${product.id}`;
    if (trackedKey.current === key) return;
    trackedKey.current = key;
    const rawSource = Array.isArray(params.source) ? params.source[0] : params.source;
    const source = ['catalog', 'promotion', 'news'].includes(rawSource ?? '')
      ? rawSource as 'catalog' | 'promotion' | 'news'
      : 'direct';
    void trackConsumerEvent('product_opened', context.tenantId, { productId: product.id, source });
  }, [context, params.source, product]);

  if (!product) {
    return (
      <SafeAreaView className="flex-1 items-center justify-center bg-gray-50 px-6">
        <Ionicons name="cube-outline" size={48} color="#9ca3af" />
        <Text className="mt-4 text-xl font-bold text-gray-900">Товар не знайдено</Text>
        <Text className="mt-2 text-center text-sm text-gray-500">
          Можливо, товар більше не бере участі в акції.
        </Text>
        <Pressable onPress={() => router.back()} className="mt-6 rounded-xl bg-green-700 px-5 py-3">
          <Text className="font-semibold text-white">Повернутися назад</Text>
        </Pressable>
      </SafeAreaView>
    );
  }

  const favorite = favoriteProductIds.includes(product.id);

  function addProductToCart() {
    addToCart(product!, quantity, {
      tenantId: selectedMembership?.tenantId ?? null,
      storeId: selectedMembership?.preferredStoreId ?? null,
      sourceNewsId: 'product-details',
    });
    Alert.alert('Додано до кошика', `${product!.name} — ${quantity} шт.`);
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50" edges={['top', 'left', 'right']}>
      <View className="flex-row items-center px-4 py-2">
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Назад"
          onPress={() => router.back()}
          className="h-11 w-11 items-center justify-center rounded-full bg-white"
        >
          <Ionicons name="arrow-back" size={22} color="#374151" />
        </Pressable>
        <Text className="ml-3 flex-1 text-lg font-bold text-gray-900">Про товар</Text>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={favorite ? 'Видалити зі списку бажань' : 'Додати до списку бажань'}
          onPress={() => toggleFavorite(product.id)}
          className={`h-11 w-11 items-center justify-center rounded-full ${
            favorite ? 'bg-red-50' : 'bg-white'
          }`}
        >
          <Ionicons
            name={favorite ? 'heart' : 'heart-outline'}
            size={22}
            color={favorite ? '#ef4444' : '#374151'}
          />
        </Pressable>
      </View>

      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 150 }}>
        <View
          className="mx-4 mt-2 h-80 items-center justify-center overflow-hidden rounded-3xl"
          style={{ backgroundColor: product.background }}
        >
          {product.discountPercent !== null ? <View className="absolute right-4 top-4 z-10 rounded-full bg-red-500 px-3 py-2">
            <Text className="text-sm font-bold text-white">−{product.discountPercent}%</Text>
          </View> : null}
          {product.imageUrl ? (
            <Image source={{ uri: product.imageUrl }} resizeMode="contain" className="h-full w-full" />
          ) : (
            <Ionicons name={product.icon} size={112} color="#374151" />
          )}
        </View>

        <View className="px-5 pt-6">
          <Text className="text-3xl font-bold leading-10 text-gray-900">{product.name}</Text>
          <Text className="mt-2 text-base text-gray-500">{product.unit}</Text>
          <View className="mt-5 flex-row items-end">
            <Text className="text-3xl font-bold text-green-700">
              {formatPrice(product.appPrice ?? product.regularPrice)} ₴
            </Text>
            {product.appPrice !== null && product.regularPrice !== null ? <Text className="mb-1 ml-3 text-base text-gray-400 line-through">
              {formatPrice(product.regularPrice)} ₴
            </Text> : null}
          </View>
          {product.appPrice !== null ? <View className="mt-2 self-start rounded-full bg-green-100 px-3 py-1.5">
            <Text className="text-xs font-bold text-green-800">Спеціальна ціна</Text>
          </View> : null}

          <View className="mt-7 rounded-2xl border border-gray-100 bg-white p-4">
            <Text className="text-lg font-bold text-gray-900">Опис</Text>
            <Text className="mt-3 text-sm leading-6 text-gray-600">
              Якісний товар для щоденних покупок. Спеціальна ціна доступна учасникам
              програми лояльності у вибраному магазині за наявності товару.
            </Text>
          </View>

          <View className="mt-3 rounded-2xl border border-gray-100 bg-white px-4">
            {[
              ['Магазин', selectedMembership?.preferredStoreName || 'Не вибрано'],
              ['Фасування', product.unit],
              ['Виробник', product.manufacturer],
              ['Країна виробництва', product.countryOfOrigin],
              ['Умови', 'Покажіть картку покупця на касі'],
            ].filter(([, value]) => Boolean(value)).map(([label, value], index, rows) => (
              <View
                key={label}
                className={`flex-row py-4 ${
                  index < rows.length - 1 ? 'border-b border-gray-100' : ''
                }`}
              >
                <Text className="w-28 text-sm text-gray-500">{label}</Text>
                <Text className="flex-1 text-right text-sm font-semibold text-gray-900">{value}</Text>
              </View>
            ))}
          </View>

          {product.nutrition ? (
            <View className="mt-3 rounded-2xl border border-gray-100 bg-white p-4">
              <View className="flex-row items-center">
                <View className="h-10 w-10 items-center justify-center rounded-xl bg-amber-50">
                  <Ionicons name="fitness-outline" size={20} color="#b45309" />
                </View>
                <View className="ml-3 flex-1">
                  <Text className="text-lg font-bold text-gray-900">КБЖВ</Text>
                  <Text className="mt-0.5 text-xs text-gray-500">
                    Харчова цінність на {product.nutrition.basis}
                  </Text>
                </View>
              </View>
              <View className="mt-4 flex-row rounded-2xl bg-gray-50 px-2 py-4">
                {[
                  {
                    label: 'Ккал',
                    value: product.nutrition.caloriesKcal,
                    color: '#b45309',
                  },
                  {
                    label: 'Білки',
                    value: `${product.nutrition.proteinG} г`,
                    color: '#2563eb',
                  },
                  {
                    label: 'Жири',
                    value: `${product.nutrition.fatG} г`,
                    color: '#7c3aed',
                  },
                  {
                    label: 'Вуглеводи',
                    value: `${product.nutrition.carbsG} г`,
                    color: '#15803d',
                  },
                ].map((nutrient, index) => (
                  <View
                    key={nutrient.label}
                    className={`flex-1 items-center ${
                      index ? 'border-l border-gray-200' : ''
                    }`}
                  >
                    <Text className="text-base font-bold" style={{ color: nutrient.color }}>
                      {nutrient.value}
                    </Text>
                    <Text className="mt-1 text-[10px] text-gray-500">{nutrient.label}</Text>
                  </View>
                ))}
              </View>
              <Text className="mt-3 text-[11px] leading-4 text-gray-400">
                Значення є довідковими та можуть незначно відрізнятися залежно від партії товару.
              </Text>
            </View>
          ) : null}
        </View>
      </ScrollView>

      <View className="absolute bottom-0 left-0 right-0 border-t border-gray-100 bg-white px-4 pb-7 pt-3">
        <View className="flex-row gap-3">
          <View className="flex-row items-center rounded-2xl border border-gray-200 bg-white p-1">
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Зменшити кількість"
              disabled={quantity <= 1}
              onPress={() => setQuantity((current) => Math.max(1, current - 1))}
              className={`h-11 w-10 items-center justify-center ${quantity <= 1 ? 'opacity-30' : ''}`}
            >
              <Ionicons name="remove" size={21} color="#374151" />
            </Pressable>
            <Text className="min-w-[34px] text-center text-lg font-bold text-gray-900">{quantity}</Text>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Збільшити кількість"
              onPress={() => setQuantity((current) => current + 1)}
              className="h-11 w-10 items-center justify-center"
            >
              <Ionicons name="add" size={21} color="#15803d" />
            </Pressable>
          </View>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Додати товар до кошика"
            onPress={addProductToCart}
            className="flex-1 flex-row items-center justify-center rounded-2xl bg-green-700"
          >
            <Ionicons name="cart" size={21} color="white" />
            <Text className="ml-2 font-bold text-white">
              До кошика · {
                formatPrice((product.appPrice ?? product.regularPrice ?? 0) * quantity)
              } ₴
            </Text>
          </Pressable>
        </View>
      </View>
    </SafeAreaView>
  );
}
