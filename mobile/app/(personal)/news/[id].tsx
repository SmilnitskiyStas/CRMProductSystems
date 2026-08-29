import { Ionicons } from '@expo/vector-icons';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useState } from 'react';
import { ActivityIndicator, Alert, Image, Modal, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import type { NewsPromotionProduct } from '@/features/loyalty/news';
import { useMemberships } from '@/features/loyalty/hooks/useLoyalty';
import { useLoyaltyUiStore } from '@/features/loyalty/store';
import { useConsumerShoppingStore } from '@/features/shopping/store';
import { useConsumerBanners, useConsumerPromotionCampaigns } from '@/features/consumer-content/hooks';
import { registerConsumerProduct } from '@/features/shopping/products';

export default function ConsumerNewsDetailsScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id?: string | string[] }>();
  const id = Array.isArray(params.id) ? params.id[0] : params.id;
  const [selectedProduct, setSelectedProduct] = useState<NewsPromotionProduct | null>(null);
  const [quantity, setQuantity] = useState(1);
  const selectedTenantId = useLoyaltyUiStore((state) => state.selectedTenantId);
  const { data: memberships } = useMemberships();
  const favoriteProductIds = useConsumerShoppingStore((state) => state.favoriteProductIds);
  const toggleFavorite = useConsumerShoppingStore((state) => state.toggleFavorite);
  const addToCart = useConsumerShoppingStore((state) => state.addToCart);
  const selectedMembership =
    memberships?.find((membership) => membership.tenantId === selectedTenantId) ??
    memberships?.[0];
  const contentContext =
    selectedMembership?.tenantId && selectedMembership.preferredStoreId
      ? { tenantId: selectedMembership.tenantId, storeId: selectedMembership.preferredStoreId }
      : null;
  const bannersQuery = useConsumerBanners(contentContext);
  const campaignsQuery = useConsumerPromotionCampaigns(contentContext);
  const news = [...(bannersQuery.data ?? []), ...(campaignsQuery.data ?? [])].find((banner) => banner.id === id);
  const formatPrice = (value: number | null) =>
    value === null ? '—' : value.toLocaleString('uk-UA', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });

  function openQuantityPicker(product: NewsPromotionProduct) {
    setSelectedProduct(product);
    setQuantity(1);
  }

  function openProduct(product: NewsPromotionProduct) {
    registerConsumerProduct(product, contentContext?.tenantId);
    router.push({
      pathname: '/(personal)/product/[id]',
      params: { id: product.id, source: 'news' },
    });
  }

  function confirmAddToCart() {
    if (!selectedProduct || !news) return;
    addToCart(selectedProduct, quantity, {
      tenantId: selectedMembership?.tenantId ?? null,
      storeId: selectedMembership?.preferredStoreId ?? null,
      sourceNewsId: news.id,
    });
    setSelectedProduct(null);
    Alert.alert(
      'Додано до кошика',
      `${selectedProduct.name} — ${quantity} шт.`
    );
  }

  if (bannersQuery.isLoading || campaignsQuery.isLoading) {
    return (
      <SafeAreaView className="flex-1 items-center justify-center bg-gray-50">
        <ActivityIndicator size="large" color="#16a34a" />
      </SafeAreaView>
    );
  }

  if (!news) {
    return (
      <SafeAreaView className="flex-1 bg-gray-50">
        <View className="flex-1 items-center justify-center px-6">
          <View className="h-16 w-16 items-center justify-center rounded-3xl bg-gray-100">
            <Ionicons name="newspaper-outline" size={30} color="#9ca3af" />
          </View>
          <Text className="mt-5 text-xl font-bold text-gray-900">Новину не знайдено</Text>
          <Text className="mt-2 text-center text-sm leading-6 text-gray-500">
            Можливо, ця пропозиція вже завершилася або була видалена.
          </Text>
          <Pressable
            accessibilityRole="button"
            onPress={() => router.back()}
            className="mt-6 rounded-xl bg-green-700 px-5 py-3"
          >
            <Text className="font-semibold text-white">Повернутися назад</Text>
          </Pressable>
        </View>
      </SafeAreaView>
    );
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
          {news.imageUrl ? (
            <Image source={{ uri: news.imageUrl }} resizeMode="cover" className="absolute inset-0 h-full w-full" />
          ) : null}
          <Ionicons name="arrow-back" size={22} color="#374151" />
        </Pressable>
        <Text className="ml-3 flex-1 text-lg font-bold text-gray-900">Новина</Text>
      </View>

      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 40 }}>
        <View
          className="mx-4 mt-2 h-64 overflow-hidden rounded-3xl p-6"
          style={{ backgroundColor: news.background }}
        >
          <View
            className="absolute -right-12 -top-16 h-52 w-52 rounded-full opacity-20"
            style={{ backgroundColor: news.accent }}
          />
          <View
            className="absolute -bottom-20 right-16 h-44 w-44 rounded-full opacity-10"
            style={{ backgroundColor: news.accent }}
          />
          <View className="h-12 w-12 items-center justify-center rounded-2xl bg-white/15">
            <Ionicons name={news.icon} size={25} color={news.accent} />
          </View>
          <Text
            className="mt-5 text-xs font-bold uppercase tracking-wider"
            style={{ color: news.accent }}
          >
            {news.eyebrow}
          </Text>
          <Text className="mt-2 max-w-[90%] text-3xl font-bold leading-9 text-white">
            {news.title}
          </Text>
        </View>

        <View className="px-5 pt-6">
          <Text className="text-lg font-semibold leading-7 text-gray-800">
            {news.description}
          </Text>

          <View className="mt-5 flex-row items-center rounded-2xl bg-green-50 p-4">
            <View className="h-10 w-10 items-center justify-center rounded-xl bg-green-100">
              <Ionicons name="calendar-outline" size={20} color="#15803d" />
            </View>
            <View className="ml-3 flex-1">
              <Text className="text-xs font-semibold uppercase text-green-700">Термін дії</Text>
              <Text className="mt-1 text-sm font-bold text-green-950">{news.validUntil}</Text>
            </View>
          </View>

          <Text className="mt-7 text-xl font-bold text-gray-900">Про пропозицію</Text>
          <View className="mt-3 gap-4">
            {news.body.map((paragraph) => (
              <Text key={paragraph} className="text-base leading-7 text-gray-600">
                {paragraph}
              </Text>
            ))}
          </View>

          {news.promotionProducts?.length ? (
            <View className="mt-8">
              <Text className="text-xl font-bold text-gray-900">Товари в цій новині</Text>
              <Text className="mt-1 text-sm leading-6 text-gray-500">
                Перегляньте товари, які магазин додав до цієї пропозиції.
              </Text>
              <View className="mt-4 gap-3">
                {news.promotionProducts.map((product) => (
                  <View
                    key={product.id}
                    className="flex-row rounded-2xl border border-gray-100 bg-white p-3"
                  >
                    <Pressable
                      accessibilityRole="button"
                      accessibilityLabel={`Відкрити товар: ${product.name}`}
                      onPress={() => openProduct(product)}
                      className="h-24 w-24 items-center justify-center overflow-hidden rounded-2xl"
                      style={{ backgroundColor: product.background }}
                    >
                      {product.discountPercent !== null ? <View className="absolute right-1.5 top-1.5 z-10 rounded-full bg-red-500 px-2 py-1">
                        <Text className="text-[10px] font-bold text-white">
                          −{product.discountPercent}%
                        </Text>
                      </View> : null}
                      {product.imageUrl ? (
                        <Image source={{ uri: product.imageUrl }} resizeMode="contain" className="h-full w-full" />
                      ) : (
                        <Ionicons name={product.icon} size={40} color="#374151" />
                      )}
                    </Pressable>
                    <View className="ml-3 flex-1 justify-center">
                      <Pressable
                        accessibilityRole="button"
                        onPress={() => openProduct(product)}
                      >
                        <Text className="text-base font-bold text-gray-900" numberOfLines={2}>
                          {product.name}
                        </Text>
                      </Pressable>
                      <Text className="mt-1 text-xs text-gray-400">{product.unit}</Text>
                      <View className="mt-2 flex-row items-end">
                        <Text className="text-lg font-bold text-green-700">
                          {formatPrice(product.appPrice ?? product.regularPrice)} ₴
                        </Text>
                        {product.appPrice !== null && product.regularPrice !== null ? <Text className="mb-0.5 ml-2 text-xs text-gray-400 line-through">
                          {formatPrice(product.regularPrice)} ₴
                        </Text> : null}
                      </View>
                      {product.appPrice !== null ? <View className="mt-1 self-start rounded-full bg-green-50 px-2 py-1">
                        <Text className="text-[10px] font-bold text-green-800">
                          Ціна із застосунком
                        </Text>
                      </View> : null}
                      <View className="mt-3 flex-row gap-2">
                        <Pressable
                          accessibilityRole="button"
                          accessibilityLabel={
                            favoriteProductIds.includes(product.id)
                              ? `Видалити ${product.name} зі списку бажань`
                              : `Додати ${product.name} до списку бажань`
                          }
                          onPress={() => toggleFavorite(product.id)}
                          className={`h-11 w-11 items-center justify-center rounded-xl border ${
                            favoriteProductIds.includes(product.id)
                              ? 'border-red-200 bg-red-50'
                              : 'border-gray-200 bg-white'
                          }`}
                        >
                          <Ionicons
                            name={favoriteProductIds.includes(product.id) ? 'heart' : 'heart-outline'}
                            size={21}
                            color={favoriteProductIds.includes(product.id) ? '#ef4444' : '#6b7280'}
                          />
                        </Pressable>
                        <Pressable
                          accessibilityRole="button"
                          accessibilityLabel={`Додати ${product.name} до кошика`}
                          onPress={() => openQuantityPicker(product)}
                          className="h-11 flex-1 flex-row items-center justify-center rounded-xl bg-green-700 px-3"
                        >
                          <Ionicons name="cart-outline" size={19} color="white" />
                          <Text className="ml-2 text-sm font-bold text-white">До кошика</Text>
                        </Pressable>
                      </View>
                    </View>
                  </View>
                ))}
              </View>
            </View>
          ) : null}

          <Text className="mt-8 text-xl font-bold text-gray-900">Умови використання</Text>
          <View className="mt-3 rounded-2xl border border-gray-100 bg-white px-4 py-2">
            {news.terms.map((term, index) => (
              <View
                key={term}
                className={`flex-row py-3.5 ${
                  index < news.terms.length - 1 ? 'border-b border-gray-100' : ''
                }`}
              >
                <View className="mt-0.5 h-6 w-6 items-center justify-center rounded-full bg-green-100">
                  <Text className="text-xs font-bold text-green-800">{index + 1}</Text>
                </View>
                <Text className="ml-3 flex-1 text-sm leading-6 text-gray-700">{term}</Text>
              </View>
            ))}
          </View>

          <View className="mt-6 flex-row items-start rounded-2xl bg-amber-50 p-4">
            <Ionicons name="information-circle-outline" size={21} color="#b45309" />
            <Text className="ml-2 flex-1 text-xs leading-5 text-amber-900">
              Детальні правила програми та винятки можна уточнити у працівників вибраного магазину.
            </Text>
          </View>
        </View>
      </ScrollView>

      <Modal
        visible={selectedProduct !== null}
        transparent
        animationType="slide"
        onRequestClose={() => setSelectedProduct(null)}
      >
        <View className="flex-1 justify-end bg-black/40">
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Закрити вибір кількості"
            onPress={() => setSelectedProduct(null)}
            className="flex-1"
          />
          <View className="rounded-t-3xl bg-white px-5 pb-8 pt-4">
            <View className="mb-4 h-1 w-10 self-center rounded-full bg-gray-300" />
            <View className="flex-row items-start">
              <View className="flex-1">
                <Text className="text-xs font-semibold uppercase tracking-wide text-green-700">
                  Додати до кошика
                </Text>
                <Text className="mt-1 text-xl font-bold text-gray-900">
                  {selectedProduct?.name}
                </Text>
                <Text className="mt-1 text-sm text-gray-500">{selectedProduct?.unit}</Text>
              </View>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Закрити"
                onPress={() => setSelectedProduct(null)}
                className="h-10 w-10 items-center justify-center rounded-full bg-gray-100"
              >
                <Ionicons name="close" size={21} color="#374151" />
              </Pressable>
            </View>

            <View className="mt-6 flex-row items-center rounded-2xl bg-gray-50 p-4">
              <View className="flex-1">
                <Text className="text-xs text-gray-500">Ціна за одиницю</Text>
                <Text className="mt-1 text-lg font-bold text-green-700">
                  {selectedProduct
                    ? formatPrice(selectedProduct.appPrice ?? selectedProduct.regularPrice)
                    : '0,00'} ₴
                </Text>
              </View>
              <View className="flex-row items-center rounded-2xl border border-gray-200 bg-white p-1">
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel="Зменшити кількість"
                  accessibilityState={{ disabled: quantity <= 1 }}
                  disabled={quantity <= 1}
                  onPress={() => setQuantity((current) => Math.max(1, current - 1))}
                  className={`h-11 w-11 items-center justify-center rounded-xl ${
                    quantity <= 1 ? 'opacity-30' : ''
                  }`}
                >
                  <Ionicons name="remove" size={23} color="#374151" />
                </Pressable>
                <Text className="min-w-[48px] text-center text-xl font-bold text-gray-900">
                  {quantity}
                </Text>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel="Збільшити кількість"
                  onPress={() => setQuantity((current) => current + 1)}
                  className="h-11 w-11 items-center justify-center rounded-xl bg-green-100"
                >
                  <Ionicons name="add" size={23} color="#15803d" />
                </Pressable>
              </View>
            </View>

            <View className="mt-5 flex-row items-center">
              <Text className="flex-1 text-sm font-semibold text-gray-500">Разом</Text>
              <Text className="text-2xl font-bold text-gray-900">
                {selectedProduct
                  ? formatPrice(
                      (selectedProduct.appPrice ?? selectedProduct.regularPrice ?? 0) * quantity
                    )
                  : '0,00'} ₴
              </Text>
            </View>

            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Підтвердити додавання до кошика"
              onPress={confirmAddToCart}
              className="mt-5 flex-row items-center justify-center rounded-2xl bg-green-700 py-4"
            >
              <Ionicons name="cart" size={21} color="white" />
              <Text className="ml-2 text-base font-bold text-white">Додати до кошика</Text>
            </Pressable>
          </View>
        </View>
      </Modal>
    </SafeAreaView>
  );
}
