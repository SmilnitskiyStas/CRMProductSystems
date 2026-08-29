import type { ComponentProps } from 'react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Ionicons } from '@expo/vector-icons';
import { requireOptionalNativeModule } from 'expo-modules-core';
import { useRouter } from 'expo-router';
import {
  ActivityIndicator,
  Alert,
  Image,
  Linking,
  Modal,
  Pressable,
  RefreshControl,
  ScrollView,
  Text,
  useWindowDimensions,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuthStore } from '@/features/auth/store';
import {
  useAutoSelectMembership,
  useAvailableNetworks,
  useJoinTenantProgram,
  useMemberships,
  useSetPreferredStore,
} from '@/features/loyalty/hooks/useLoyalty';
import { useLoyaltyUiStore } from '@/features/loyalty/store';
import type {
  LoyaltyNetworkStore,
  LoyaltyNetworkSummary,
} from '@/features/loyalty/types';
import type { NewsPromotionProduct } from '@/features/loyalty/news';
import { useConsumerShoppingStore } from '@/features/shopping/store';
import { registerConsumerProducts } from '@/features/shopping/products';
import { useConsumerBanners, useConsumerPromotionCampaigns, useConsumerPromotions } from '@/features/consumer-content/hooks';
import { recordBannerClick, recordBannerView, recordPromotionCampaignEvent } from '@/features/consumer-content/api';
import { PageRenderer } from '@/features/server-driven-ui/PageRenderer';
import { useRetailTheme } from '@/features/theme/RetailThemeProvider';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import { mergeStoreNetworks } from '@/features/loyalty/selection';

type IconName = ComponentProps<typeof Ionicons>['name'];

function firstName(fullName?: string) {
  return fullName?.trim().split(/\s+/)[0] || 'друже';
}

function formatBalance(value: number | null) {
  if (value === null) return '—';
  return value.toLocaleString('uk-UA', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

export default function PersonalHomeScreen() {
  const router = useRouter();
  const theme = useRetailTheme();
  const { config, source: configSource, refresh: refreshMobileConfig } = useMobileConfig();
  const { width: screenWidth } = useWindowDimensions();
  const [storeSelectorOpen, setStoreSelectorOpen] = useState(false);
  const [activeNewsIndex, setActiveNewsIndex] = useState(0);
  const [selectedDiscountProduct, setSelectedDiscountProduct] =
    useState<NewsPromotionProduct | null>(null);
  const [discountProductQuantity, setDiscountProductQuantity] = useState(1);
  const [locatingStore, setLocatingStore] = useState(false);
  const [nearestStore, setNearestStore] = useState<{
    network: LoyaltyNetworkSummary;
    store: LoyaltyNetworkStore;
    distanceKm: number;
  } | null>(null);
  const [locationConsentOpen, setLocationConsentOpen] = useState(false);
  const [resolveLocationConsent, setResolveLocationConsent] = useState<
    ((confirmed: boolean) => void) | null
  >(null);
  const viewedBannerIds = useRef(new Set<string>());
  const consumerUser = useAuthStore((state) => state.consumerUser);
  const staffUser = useAuthStore((state) => state.user);
  const personalAccessToken = useAuthStore((state) => state.personalAccessToken);
  const selectedTenantId = useLoyaltyUiStore((state) => state.selectedTenantId);
  const setSelectedTenantId = useLoyaltyUiStore((state) => state.setSelectedTenantId);
  const hasPersonalAccess = personalAccessToken !== null;

  const membershipsQuery = useMemberships(hasPersonalAccess);
  const networksQuery = useAvailableNetworks(hasPersonalAccess);
  const joinProgram = useJoinTenantProgram();
  const preferredStore = useSetPreferredStore();
  const favoriteProductIds = useConsumerShoppingStore((state) => state.favoriteProductIds);
  const toggleFavorite = useConsumerShoppingStore((state) => state.toggleFavorite);
  const addToCart = useConsumerShoppingStore((state) => state.addToCart);
  useAutoSelectMembership(membershipsQuery.data);

  const memberships = membershipsQuery.data ?? [];
  const storeNetworks = useMemo(
    () => mergeStoreNetworks(networksQuery.data, memberships),
    [memberships, networksQuery.data]
  );
  const selectedMembership =
    memberships.find((membership) => membership.tenantId === selectedTenantId) ??
    memberships[0];
  const contentContext =
    selectedMembership?.tenantId && selectedMembership.preferredStoreId
      ? {
          tenantId: selectedMembership.tenantId,
          storeId: selectedMembership.preferredStoreId,
        }
      : null;
  const bannersQuery = useConsumerBanners(contentContext);
  const promotionCampaignsQuery = useConsumerPromotionCampaigns(contentContext);
  const promotionsQuery = useConsumerPromotions(contentContext);
  const banners = [...(bannersQuery.data ?? []), ...(promotionCampaignsQuery.data ?? [])];
  const discountedProducts = promotionsQuery.data ?? [];
  const fullName = consumerUser?.fullName ?? staffUser?.fullName;
  const isRefreshing =
    membershipsQuery.isRefetching ||
    networksQuery.isRefetching ||
    bannersQuery.isRefetching ||
    promotionCampaignsQuery.isRefetching ||
    promotionsQuery.isRefetching;
  const newsCardWidth = Math.max(280, screenWidth - 48);
  // A published empty Home page is intentional: the retailer removed every block. Only the
  // bundled mock/safe fallback should retain the legacy Home below.
  const hasConfiguredHome =
    configSource === 'published' || configSource === 'last-valid'
      ? config.pages.home !== undefined
      : Boolean(config.pages.home?.blocks.length);

  useEffect(() => {
    if (!storeSelectorOpen || !hasPersonalAccess) return;
    void Promise.all([membershipsQuery.refetch(), networksQuery.refetch()]);
  }, [storeSelectorOpen]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    registerConsumerProducts(discountedProducts, contentContext?.tenantId);
    registerConsumerProducts(
      banners.flatMap((banner) => banner.promotionProducts ?? []),
      contentContext?.tenantId
    );
  }, [banners, contentContext?.tenantId, discountedProducts]);

  useEffect(() => {
    const banner = banners[activeNewsIndex];
    if (!banner || !contentContext) return;
    const impressionKey = `${contentContext.storeId}:${banner.id}`;
    if (viewedBannerIds.current.has(impressionKey)) return;
    viewedBannerIds.current.add(impressionKey);
    void (banner.contentType === 'promotion_campaign'
      ? recordPromotionCampaignEvent(contentContext, banner.id, 'impression')
      : recordBannerView(contentContext, banner.id)).catch(() => undefined);
  }, [activeNewsIndex, banners, contentContext]);

  function refresh() {
    refreshMobileConfig();
    void membershipsQuery.refetch();
    void networksQuery.refetch();
    void bannersQuery.refetch();
    void promotionCampaignsQuery.refetch();
    void promotionsQuery.refetch();
  }

  async function openBanner(news: (typeof banners)[number]) {
    if (!contentContext) return;
    void (news.contentType === 'promotion_campaign'
      ? recordPromotionCampaignEvent(contentContext, news.id, 'open')
      : recordBannerClick(contentContext, news.id)).catch(() => undefined);
    if (news.detailMode === 'external' && news.externalUrl) {
      await Linking.openURL(news.externalUrl);
      return;
    }
    router.push({
      pathname: '/(personal)/news/[id]',
      params: { id: news.id },
    });
  }

  function distanceInKm(
    from: { latitude: number; longitude: number },
    to: { latitude: number; longitude: number }
  ) {
    const radians = (degrees: number) => (degrees * Math.PI) / 180;
    const earthRadiusKm = 6371;
    const latitudeDelta = radians(to.latitude - from.latitude);
    const longitudeDelta = radians(to.longitude - from.longitude);
    const a =
      Math.sin(latitudeDelta / 2) ** 2 +
      Math.cos(radians(from.latitude)) *
        Math.cos(radians(to.latitude)) *
        Math.sin(longitudeDelta / 2) ** 2;
    return earthRadiusKm * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  }

  function confirmLocationUse() {
    return new Promise<boolean>((resolve) => {
      setResolveLocationConsent(() => resolve);
      setLocationConsentOpen(true);
    });
  }

  function closeLocationConsent(confirmed: boolean) {
    setLocationConsentOpen(false);
    resolveLocationConsent?.(confirmed);
    setResolveLocationConsent(null);
  }

  async function findNearestStore() {
    const stores = storeNetworks.flatMap((network) =>
      network.stores
        .filter((store) => Boolean(store.address))
        .map((store) => ({ network, store }))
    );

    if (!stores.length) {
      Alert.alert('Адреси відсутні', 'Наразі немає магазинів з указаною адресою.');
      return;
    }

    setLocatingStore(true);
    setNearestStore(null);
    try {
      if (!requireOptionalNativeModule('ExpoLocation')) {
        Alert.alert(
          'Потрібно оновити застосунок',
          'Пошук найближчого магазину потребує нової збірки застосунку з підтримкою геолокації.'
        );
        return;
      }
      // Load the native module only when the user requests this feature. This keeps the
      // personal route bootable in an older dev client until it is rebuilt with expo-location.
      const Location = await import('expo-location');
      let permission = await Location.getForegroundPermissionsAsync();
      if (!permission.granted) {
        if (!permission.canAskAgain) {
          Alert.alert(
            'Геолокацію вимкнено',
            'Дозвольте доступ до геолокації в системних налаштуваннях, щоб знайти найближчий магазин.',
            [
              { text: 'Скасувати', style: 'cancel' },
              {
                text: 'Відкрити налаштування',
                onPress: () => void Linking.openSettings(),
              },
            ]
          );
          return;
        }

        const confirmed = await confirmLocationUse();
        if (!confirmed) return;
        permission = await Location.requestForegroundPermissionsAsync();
      }
      if (!permission.granted) {
        Alert.alert(
          'Геолокацію не дозволено',
          'Дозвольте доступ до геолокації в налаштуваннях пристрою, щоб знайти найближчий магазин.'
        );
        return;
      }

      const current = await Location.getCurrentPositionAsync({
        accuracy: Location.Accuracy.Balanced,
      });
      const candidates: Array<{
        network: LoyaltyNetworkSummary;
        store: LoyaltyNetworkStore;
        distanceKm: number;
      }> = [];

      for (const candidate of stores) {
        try {
          const coordinates = await Location.geocodeAsync(candidate.store.address as string);
          const point = coordinates[0];
          if (!point) continue;
          candidates.push({
            ...candidate,
            distanceKm: distanceInKm(current.coords, point),
          });
        } catch {
          // One invalid address should not prevent other stores from being compared.
        }
      }

      candidates.sort((first, second) => first.distanceKm - second.distanceKm);
      if (!candidates[0]) {
        Alert.alert(
          'Не вдалося визначити відстань',
          'Перевірте підключення до інтернету або виберіть магазин зі списку.'
        );
        return;
      }
      setNearestStore(candidates[0]);
    } catch {
      Alert.alert(
        'Не вдалося визначити місцезнаходження',
        'Увімкніть геолокацію на пристрої та спробуйте ще раз.'
      );
    } finally {
      setLocatingStore(false);
    }
  }

  function openDiscountQuantityPicker(product: NewsPromotionProduct) {
    setSelectedDiscountProduct(product);
    setDiscountProductQuantity(1);
  }

  function confirmDiscountProduct() {
    if (!selectedDiscountProduct) return;
    addToCart(selectedDiscountProduct, discountProductQuantity, {
      tenantId: selectedMembership?.tenantId ?? null,
      storeId: selectedMembership?.preferredStoreId ?? null,
      sourceNewsId: 'discounted-products',
    });
    Alert.alert(
      'Додано до кошика',
      `${selectedDiscountProduct.name} — ${discountProductQuantity} шт.`
    );
    setSelectedDiscountProduct(null);
  }

  async function selectStore(network: LoyaltyNetworkSummary, store: LoyaltyNetworkStore) {
    try {
      const membership =
        memberships.find((item) => item.tenantId === network.tenantId) ??
        await joinProgram.mutateAsync(network.tenantId);
      const updated = await preferredStore.mutateAsync({
        tenantId: membership.tenantId,
        storeId: store.storeId,
      });
      setSelectedTenantId(updated.tenantId);
      setStoreSelectorOpen(false);
    } catch (error: unknown) {
      const status = (error as { response?: { status?: number } })?.response?.status;
      Alert.alert(
        'Не вдалося вибрати магазин',
        status === 400
          ? 'Цей магазин зараз недоступний. Оновіть список і виберіть інший.'
          : status === 403
            ? 'Не вдалося приєднатися до програми лояльності цієї мережі.'
            : 'Перевірте з’єднання та спробуйте ще раз.'
      );
    }
  }

  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: theme.colors.background }} edges={['top', 'left', 'right']}>
      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingHorizontal: 16, paddingBottom: 36 }}
        refreshControl={
          <RefreshControl refreshing={isRefreshing} onRefresh={refresh} tintColor="#16a34a" />
        }
      >
        <View className="flex-row items-center pb-5 pt-2">
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Відкрити профіль"
            onPress={() => router.push('/(personal)/account')}
            className="h-12 w-12 items-center justify-center"
            style={{ borderRadius: theme.radius.button, backgroundColor: theme.colors.border }}
          >
            <Text className="text-lg font-bold" style={{ color: theme.colors.primary }}>
              {fullName?.trim().charAt(0).toUpperCase() || 'S'}
            </Text>
          </Pressable>
          <View className="ml-3 flex-1">
            <Text className="text-sm" style={{ color: theme.colors.textSecondary }}>Вітаємо,</Text>
            <Text className="text-xl font-bold" style={{ color: theme.colors.textPrimary }}>{firstName(fullName)}!</Text>
          </View>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Сканувати штрихкод товару"
            onPress={() => router.push('/(personal)/scan')}
            className="h-11 w-11 items-center justify-center rounded-full"
            style={{ backgroundColor: theme.colors.primary }}
          >
            <Ionicons name="scan-outline" size={21} color="white" />
          </Pressable>
        </View>

        <PageRenderer pageKey="home" />

        {!hasConfiguredHome ? (
          <>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Вибрати магазин"
          accessibilityState={{ expanded: storeSelectorOpen }}
          onPress={() => setStoreSelectorOpen(true)}
          className="mb-4 flex-row items-center border px-4 py-3"
          style={{ borderRadius: theme.radius.card, borderColor: theme.colors.border, backgroundColor: theme.colors.surface }}
        >
          <View className="h-10 w-10 items-center justify-center" style={{ borderRadius: theme.radius.button, backgroundColor: theme.colors.border }}>
            <Ionicons name="storefront-outline" size={20} color={theme.colors.primary} />
          </View>
          <View className="ml-3 flex-1">
            <Text className="text-[11px] font-semibold uppercase tracking-wide text-gray-400">
              Магазин
            </Text>
            <Text className="mt-0.5 text-sm font-bold" style={{ color: theme.colors.textPrimary }} numberOfLines={1}>
              {selectedMembership?.preferredStoreName || 'Оберіть зручний магазин'}
            </Text>
            {selectedMembership?.preferredStoreAddress ? (
              <Text className="mt-0.5 text-xs text-gray-500" numberOfLines={1}>
                {selectedMembership.preferredStoreAddress}
              </Text>
            ) : null}
          </View>
          <View className="h-9 w-9 items-center justify-center rounded-full bg-gray-50">
            <Ionicons name="chevron-down" size={18} color="#4b5563" />
          </View>
        </Pressable>

        {membershipsQuery.isLoading ? (
          <View className="h-56 items-center justify-center rounded-3xl bg-green-700">
            <ActivityIndicator color="white" size="large" />
          </View>
        ) : selectedMembership ? (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Відкрити картку покупця"
            onPress={() => router.push('/(personal)/wallet')}
            className="overflow-hidden p-5"
            style={{ borderRadius: theme.radius.card, backgroundColor: theme.colors.primary }}
          >
            <View className="absolute -right-9 -top-12 h-40 w-40 rounded-full bg-white/10" />
            <View className="absolute -bottom-16 right-12 h-36 w-36 rounded-full bg-green-400/20" />
            <View className="flex-row items-start">
              <View className="h-11 w-11 items-center justify-center rounded-2xl bg-white/15">
                <Ionicons name="sparkles-outline" size={22} color="white" />
              </View>
              <View className="ml-3 flex-1">
                <Text className="text-xs font-medium uppercase tracking-wider text-green-100">
                  {selectedMembership.tenantName}
                </Text>
                <Text className="mt-1 text-sm text-white/80">Ваш бонусний баланс</Text>
              </View>
              <View className="rounded-full bg-white/15 px-3 py-1.5">
                <Text className="text-xs font-semibold text-white">Активна</Text>
              </View>
            </View>
            <View className="mt-7 flex-row items-end">
              <Text className="text-4xl font-bold text-white">
                {formatBalance(selectedMembership.balance)}
              </Text>
              <Text className="mb-1.5 ml-2 text-base font-semibold text-green-100">бонусів</Text>
            </View>
          </Pressable>
        ) : (
          <View className="overflow-hidden rounded-3xl bg-gray-900 p-6">
            <View className="h-12 w-12 items-center justify-center rounded-2xl bg-white/10">
              <Ionicons name="gift-outline" size={25} color="white" />
            </View>
            <Text className="mt-6 text-2xl font-bold text-white">Ваша вигода починається тут</Text>
            <Text className="mt-2 text-sm leading-6 text-gray-300">
              Приєднайтеся до мережі, накопичуйте бонуси та використовуйте їх під час покупок.
            </Text>
          </View>
        )}

        <View className="mb-3 mt-7 flex-row items-end justify-between">
          <View>
            <Text className="text-xl font-bold text-gray-900">Новини</Text>
            <Text className="mt-1 text-sm text-gray-500">Події та пропозиції вашого магазину</Text>
          </View>
          <Text className="text-xs font-semibold text-gray-400">
            {banners.length ? activeNewsIndex + 1 : 0}/{banners.length}
          </Text>
        </View>
        {!contentContext ? (
          <View className="mb-2 items-center rounded-2xl bg-white px-5 py-8">
            <Ionicons name="storefront-outline" size={32} color="#9ca3af" />
            <Text className="mt-3 text-center text-sm text-gray-500">
              Оберіть магазин, щоб побачити його новини
            </Text>
          </View>
        ) : bannersQuery.isLoading ? (
          <View className="h-44 items-center justify-center rounded-3xl bg-white">
            <ActivityIndicator color="#16a34a" />
          </View>
        ) : bannersQuery.isError ? (
          <Pressable
            onPress={() => void bannersQuery.refetch()}
            className="items-center rounded-2xl bg-white px-5 py-7"
          >
            <Text className="text-sm font-semibold text-gray-700">Не вдалося завантажити новини</Text>
            <Text className="mt-1 text-xs text-green-700">Натисніть, щоб повторити</Text>
          </Pressable>
        ) : !banners.length ? (
          <View className="items-center rounded-2xl bg-white px-5 py-8">
            <Ionicons name="newspaper-outline" size={32} color="#d1d5db" />
            <Text className="mt-3 text-sm text-gray-500">Новин для цього магазину поки немає</Text>
          </View>
        ) : null}
        <ScrollView
          horizontal
          pagingEnabled={false}
          snapToInterval={newsCardWidth + 12}
          decelerationRate="fast"
          showsHorizontalScrollIndicator={false}
          contentContainerStyle={{ gap: 12, paddingRight: 16 }}
          onMomentumScrollEnd={(event) => {
            const index = Math.round(event.nativeEvent.contentOffset.x / (newsCardWidth + 12));
            setActiveNewsIndex(Math.max(0, Math.min(index, banners.length - 1)));
          }}
        >
          {banners.map((news) => (
            <Pressable
              key={news.id}
              accessibilityRole="button"
              accessibilityLabel={`Відкрити новину: ${news.title}`}
              onPress={() => void openBanner(news)}
              className="h-44 overflow-hidden rounded-3xl p-5"
              style={{ width: newsCardWidth, backgroundColor: news.background }}
            >
              {news.imageUrl ? (
                <Image
                  source={{ uri: news.imageUrl }}
                  resizeMode="cover"
                  className="absolute inset-0 h-full w-full"
                />
              ) : null}
              <View
                className="absolute -right-8 -top-10 h-36 w-36 rounded-full opacity-20"
                style={{ backgroundColor: news.accent }}
              />
              <View
                className="absolute -bottom-16 right-20 h-32 w-32 rounded-full opacity-10"
                style={{ backgroundColor: news.accent }}
              />
              <View className="flex-row items-start">
                <View className="h-10 w-10 items-center justify-center rounded-2xl bg-white/15">
                  <Ionicons name={news.icon} size={21} color={news.accent} />
                </View>
                {news.eyebrow ? (
                  <Text
                    className="ml-3 mt-1 text-xs font-bold uppercase tracking-wider"
                    style={{ color: news.accent }}
                  >
                    {news.eyebrow}
                  </Text>
                ) : null}
              </View>
              <Text className="mt-4 max-w-[85%] text-xl font-bold text-white">{news.title}</Text>
              <Text className="mt-2 max-w-[88%] text-xs leading-5 text-white/70">
                {news.description}
              </Text>
              <View className="absolute bottom-4 right-4 h-9 w-9 items-center justify-center rounded-full bg-white/15">
                <Ionicons name="arrow-forward" size={18} color="white" />
              </View>
            </Pressable>
          ))}
        </ScrollView>
        <View className="mt-3 flex-row justify-center gap-1.5">
          {banners.map((news, index) => (
            <View
              key={news.id}
              className={`h-1.5 rounded-full ${
                index === activeNewsIndex ? 'w-5 bg-green-600' : 'w-1.5 bg-gray-300'
              }`}
            />
          ))}
        </View>

        <View className="mb-3 mt-7">
          <Text className="text-xl font-bold text-gray-900">Товари зі знижками</Text>
          <Text className="mt-1 text-sm text-gray-500" numberOfLines={1}>
            {selectedMembership?.preferredStoreName
              ? `Актуально в магазині «${selectedMembership.preferredStoreName}»`
              : 'Оберіть магазин, щоб бачити актуальні пропозиції'}
          </Text>
        </View>
        {promotionsQuery.isLoading ? (
          <View className="h-52 items-center justify-center rounded-2xl bg-white">
            <ActivityIndicator color="#16a34a" />
          </View>
        ) : promotionsQuery.isError ? (
          <Pressable
            onPress={() => void promotionsQuery.refetch()}
            className="items-center rounded-2xl bg-white px-5 py-7"
          >
            <Text className="text-sm font-semibold text-gray-700">Не вдалося завантажити знижки</Text>
            <Text className="mt-1 text-xs text-green-700">Натисніть, щоб повторити</Text>
          </Pressable>
        ) : contentContext && !discountedProducts.length ? (
          <View className="items-center rounded-2xl bg-white px-5 py-8">
            <Ionicons name="pricetag-outline" size={32} color="#d1d5db" />
            <Text className="mt-3 text-sm text-gray-500">Активних знижок зараз немає</Text>
          </View>
        ) : null}
        <ScrollView
          horizontal
          showsHorizontalScrollIndicator={false}
          contentContainerStyle={{ gap: 12, paddingRight: 16 }}
        >
          {discountedProducts.map((product) => (
            <View
              key={product.id}
              className="w-40 rounded-2xl border border-gray-100 bg-white p-3"
            >
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={`Відкрити товар: ${product.name}`}
                onPress={() => {
                  registerConsumerProducts([product], contentContext?.tenantId);
                  router.push({
                    pathname: '/(personal)/product/[id]',
                    params: { id: product.id },
                  });
                }}
                className="h-32 items-center justify-center overflow-hidden rounded-2xl"
                style={{ backgroundColor: product.background }}
              >
                {product.imageUrl ? (
                  <Image
                    source={{ uri: product.imageUrl }}
                    resizeMode="contain"
                    className="h-full w-full"
                  />
                ) : (
                  <Ionicons name={product.icon} size={52} color="#374151" />
                )}
                <View className="absolute right-2 top-2 rounded-full bg-red-500 px-2 py-1">
                        <Text className="text-[11px] font-bold text-white">
                          −{product.discountPercent}%
                        </Text>
                </View>
              </Pressable>
              <Pressable
                accessibilityRole="button"
                onPress={() => {
                  registerConsumerProducts([product], contentContext?.tenantId);
                  router.push({
                    pathname: '/(personal)/product/[id]',
                    params: { id: product.id },
                  });
                }}
              >
                <Text className="mt-3 min-h-[40px] text-sm font-bold leading-5 text-gray-900" numberOfLines={2}>
                  {product.name}
                </Text>
              </Pressable>
              <Text className="mt-1 text-xs text-gray-400">{product.unit}</Text>
              <View className="mt-3 flex-row items-end">
                <Text className="text-base font-bold text-green-700">
                  {formatBalance(product.appPrice)} ₴
                </Text>
              </View>
              <Text className="mt-0.5 text-xs text-gray-400 line-through">
                {formatBalance(product.regularPrice)} ₴
              </Text>
              <View className="mt-3 flex-row gap-2">
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={
                    favoriteProductIds.includes(product.id)
                      ? `Видалити ${product.name} зі списку бажань`
                      : `Додати ${product.name} до списку бажань`
                  }
                  onPress={() => toggleFavorite(product.id)}
                  className={`h-10 w-10 items-center justify-center rounded-xl border ${
                    favoriteProductIds.includes(product.id)
                      ? 'border-red-200 bg-red-50'
                      : 'border-gray-200 bg-white'
                  }`}
                >
                  <Ionicons
                    name={favoriteProductIds.includes(product.id) ? 'heart' : 'heart-outline'}
                    size={20}
                    color={favoriteProductIds.includes(product.id) ? '#ef4444' : '#6b7280'}
                  />
                </Pressable>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={`Додати ${product.name} до кошика`}
                  onPress={() => openDiscountQuantityPicker(product)}
                  className="h-10 flex-1 items-center justify-center rounded-xl bg-green-700"
                >
                  <Ionicons name="cart-outline" size={20} color="white" />
                </Pressable>
              </View>
            </View>
          ))}
        </ScrollView>

        <View className="mt-7 overflow-hidden rounded-3xl bg-amber-100 p-5">
          <View className="absolute -bottom-10 -right-8 h-28 w-28 rounded-full bg-amber-200" />
          <View className="flex-row items-center">
            <View className="h-11 w-11 items-center justify-center rounded-2xl bg-white/70">
              <Ionicons name="gift-outline" size={22} color="#b45309" />
            </View>
            <View className="ml-3 flex-1">
              <Text className="text-base font-bold text-amber-950">Більше покупок — більше бонусів</Text>
              <Text className="mt-1 text-xs leading-5 text-amber-800">
                Показуйте картку на касі та стежте за нарахуваннями в історії.
              </Text>
            </View>
          </View>
        </View>
          </>
        ) : null}
      </ScrollView>

      <Modal
        visible={storeSelectorOpen}
        transparent
        animationType="slide"
        onRequestClose={() => setStoreSelectorOpen(false)}
      >
        <View className="flex-1 justify-end bg-black/40">
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Закрити вибір магазину"
            onPress={() => setStoreSelectorOpen(false)}
            className="flex-1"
          />
          <View className="max-h-[76%] rounded-t-3xl bg-gray-50 px-4 pb-8 pt-4">
            <View className="mb-3 h-1 w-10 self-center rounded-full bg-gray-300" />
            <View className="flex-row items-center pb-4">
              <View className="flex-1">
                <Text className="text-xl font-bold text-gray-900">Оберіть магазин</Text>
                <Text className="mt-1 text-sm text-gray-500">
                  Тут ви накопичуватимете та використовуватимете бонуси
                </Text>
              </View>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Закрити"
                onPress={() => setStoreSelectorOpen(false)}
                className="ml-3 h-10 w-10 items-center justify-center rounded-full bg-white"
              >
                <Ionicons name="close" size={21} color="#374151" />
              </Pressable>
            </View>

            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Знайти найближчий магазин"
              accessibilityState={{ busy: locatingStore, disabled: locatingStore }}
              disabled={locatingStore}
              onPress={() => void findNearestStore()}
              className="mb-4 flex-row items-center rounded-2xl bg-green-700 p-4"
            >
              <View className="h-10 w-10 items-center justify-center rounded-xl bg-white/15">
                {locatingStore ? (
                  <ActivityIndicator size="small" color="white" />
                ) : (
                  <Ionicons name="navigate-outline" size={21} color="white" />
                )}
              </View>
              <View className="ml-3 flex-1">
                <Text className="font-bold text-white">
                  {locatingStore ? 'Шукаємо найближчий…' : 'Знайти найближчий'}
                </Text>
                <Text className="mt-0.5 text-xs text-green-100">
                  Використати ваше місцезнаходження
                </Text>
              </View>
              {!locatingStore ? <Ionicons name="arrow-forward" size={19} color="white" /> : null}
            </Pressable>

            {nearestStore ? (
              <View className="mb-4 rounded-2xl border border-green-200 bg-green-50 p-4">
                <View className="flex-row items-start">
                  <View className="h-10 w-10 items-center justify-center rounded-xl bg-green-100">
                    <Ionicons name="location" size={21} color="#15803d" />
                  </View>
                  <View className="ml-3 flex-1">
                    <Text className="text-xs font-bold uppercase tracking-wide text-green-700">
                      Найближчий · {nearestStore.distanceKm < 1
                        ? `${Math.round(nearestStore.distanceKm * 1000)} м`
                        : `${nearestStore.distanceKm.toFixed(1)} км`}
                    </Text>
                    <Text className="mt-1 text-base font-bold text-green-950">
                      {nearestStore.store.storeName}
                    </Text>
                    <Text className="mt-1 text-xs leading-5 text-green-800">
                      {nearestStore.store.address}
                    </Text>
                  </View>
                </View>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={`Обрати магазин ${nearestStore.store.storeName}`}
                  disabled={joinProgram.isPending || preferredStore.isPending}
                  onPress={() => void selectStore(nearestStore.network, nearestStore.store)}
                  className="mt-3 items-center rounded-xl bg-green-700 py-3"
                >
                  {preferredStore.isPending ? (
                    <ActivityIndicator size="small" color="white" />
                  ) : (
                    <Text className="font-bold text-white">Обрати цей магазин</Text>
                  )}
                </Pressable>
              </View>
            ) : null}

            {networksQuery.isLoading ? (
              <View className="items-center py-12">
                <ActivityIndicator size="large" color="#16a34a" />
              </View>
            ) : networksQuery.isError ? (
              <View className="items-center rounded-2xl bg-white px-5 py-10">
                <Ionicons name="cloud-offline-outline" size={34} color="#9ca3af" />
                <Text className="mt-3 text-center text-sm text-gray-500">
                  Не вдалося завантажити магазини
                </Text>
                <Pressable
                  onPress={() => void networksQuery.refetch()}
                  className="mt-4 rounded-xl bg-green-700 px-4 py-3"
                >
                  <Text className="font-semibold text-white">Спробувати ще раз</Text>
                </Pressable>
              </View>
            ) : (
              <ScrollView showsVerticalScrollIndicator={false}>
                <View className="gap-3 pb-3">
                  {storeNetworks.map((network) => (
                    <View
                      key={network.tenantId}
                      className="rounded-2xl border border-gray-100 bg-white p-4"
                    >
                      <View className="mb-3 flex-row items-center">
                        <View className="h-9 w-9 items-center justify-center rounded-xl bg-green-50">
                          <Ionicons name="business-outline" size={18} color="#16a34a" />
                        </View>
                        <Text className="ml-3 flex-1 font-bold text-gray-900">
                          {network.tenantName}
                        </Text>
                      </View>
                      <View className="gap-2">
                        {network.stores.map((store) => {
                          const selected =
                            selectedMembership?.tenantId === network.tenantId &&
                            selectedMembership.preferredStoreId === store.storeId;
                          const pending =
                            preferredStore.isPending &&
                            preferredStore.variables?.storeId === store.storeId;
                          return (
                            <Pressable
                              key={store.storeId}
                              accessibilityRole="button"
                              accessibilityState={{ selected, disabled: preferredStore.isPending }}
                              disabled={joinProgram.isPending || preferredStore.isPending}
                              onPress={() => void selectStore(network, store)}
                              className={`flex-row items-center rounded-xl border px-3 py-3 ${
                                selected
                                  ? 'border-green-300 bg-green-50'
                                  : 'border-gray-100 bg-gray-50'
                              }`}
                            >
                              <Ionicons
                                name={selected ? 'location' : 'location-outline'}
                                size={19}
                                color={selected ? '#15803d' : '#6b7280'}
                              />
                              <View className="ml-2 flex-1">
                                <Text
                                  className={`text-sm font-semibold ${
                                    selected ? 'text-green-900' : 'text-gray-900'
                                  }`}
                                >
                                  {store.storeName}
                                </Text>
                                {store.address ? (
                                  <Text className="mt-0.5 text-xs text-gray-500" numberOfLines={2}>
                                    {store.address}
                                  </Text>
                                ) : null}
                              </View>
                              {pending ? (
                                <ActivityIndicator size="small" color="#16a34a" />
                              ) : selected ? (
                                <Ionicons name="checkmark-circle" size={22} color="#16a34a" />
                              ) : (
                                <Ionicons name="chevron-forward" size={18} color="#9ca3af" />
                              )}
                            </Pressable>
                          );
                        })}
                        {network.stores.length === 0 ? (
                          <View className="rounded-xl bg-gray-50 px-3 py-4">
                            <Text className="text-center text-sm text-gray-500">
                              Торгові точки цієї мережі ще завантажуються
                            </Text>
                          </View>
                        ) : null}
                      </View>
                    </View>
                  ))}
                  {!storeNetworks.length ? (
                    <View className="items-center rounded-2xl bg-white px-5 py-12">
                      <Ionicons name="storefront-outline" size={38} color="#d1d5db" />
                      <Text className="mt-3 text-center text-sm text-gray-500">
                        Наразі немає доступних магазинів
                      </Text>
                    </View>
                  ) : null}
                </View>
              </ScrollView>
            )}
          </View>
        </View>
      </Modal>

      <Modal
        visible={locationConsentOpen}
        transparent
        animationType="fade"
        statusBarTranslucent
        onRequestClose={() => closeLocationConsent(false)}
      >
        <View className="flex-1 items-center justify-center bg-black/50 px-5">
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Закрити запит геолокації"
            onPress={() => closeLocationConsent(false)}
            className="absolute inset-0"
          />
          <View className="w-full max-w-[420px] overflow-hidden rounded-3xl bg-white">
            <View className="relative items-center overflow-hidden bg-green-700 px-6 pb-7 pt-8">
              <View className="absolute -right-12 -top-16 h-40 w-40 rounded-full bg-white/10" />
              <View className="absolute -bottom-16 -left-10 h-36 w-36 rounded-full bg-green-400/20" />
              <View className="h-16 w-16 items-center justify-center rounded-3xl bg-white/15">
                <Ionicons name="navigate" size={31} color="white" />
              </View>
              <Text className="mt-5 text-center text-2xl font-bold text-white">
                Знайдемо магазин поруч?
              </Text>
              <Text className="mt-2 text-center text-sm leading-6 text-green-100">
                Дозвольте доступ до геолокації, щоб побачити найближчу торгову точку та відстань до неї.
              </Text>
            </View>

            <View className="px-5 pb-5 pt-5">
              <View className="flex-row items-start rounded-2xl bg-gray-50 p-4">
                <View className="h-9 w-9 items-center justify-center rounded-xl bg-green-100">
                  <Ionicons name="shield-checkmark-outline" size={19} color="#15803d" />
                </View>
                <View className="ml-3 flex-1">
                  <Text className="text-sm font-bold text-gray-900">Ваші дані залишаються приватними</Text>
                  <Text className="mt-1 text-xs leading-5 text-gray-500">
                    Ми використовуємо координати лише під час пошуку та не зберігаємо історію місцезнаходжень.
                  </Text>
                </View>
              </View>

              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Дозволити використання геолокації"
                onPress={() => closeLocationConsent(true)}
                className="mt-5 flex-row items-center justify-center rounded-2xl bg-green-700 py-4"
              >
                <Ionicons name="location-outline" size={20} color="white" />
                <Text className="ml-2 text-base font-bold text-white">Дозволити</Text>
              </Pressable>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Не використовувати геолокацію зараз"
                onPress={() => closeLocationConsent(false)}
                className="mt-2 items-center rounded-2xl py-3.5"
              >
                <Text className="text-sm font-semibold text-gray-500">Не зараз</Text>
              </Pressable>
            </View>
          </View>
        </View>
      </Modal>

      <Modal
        visible={selectedDiscountProduct !== null}
        transparent
        animationType="slide"
        onRequestClose={() => setSelectedDiscountProduct(null)}
      >
        <View className="flex-1 justify-end bg-black/40">
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Закрити вибір кількості"
            onPress={() => setSelectedDiscountProduct(null)}
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
                  {selectedDiscountProduct?.name}
                </Text>
                <Text className="mt-1 text-sm text-gray-500">
                  {selectedDiscountProduct?.unit}
                </Text>
              </View>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Закрити"
                onPress={() => setSelectedDiscountProduct(null)}
                className="h-10 w-10 items-center justify-center rounded-full bg-gray-100"
              >
                <Ionicons name="close" size={21} color="#374151" />
              </Pressable>
            </View>

            <View className="mt-6 flex-row items-center rounded-2xl bg-gray-50 p-4">
              <View className="flex-1">
                <Text className="text-xs text-gray-500">Ціна за одиницю</Text>
                <Text className="mt-1 text-lg font-bold text-green-700">
                  {selectedDiscountProduct
                    ? formatBalance(selectedDiscountProduct.appPrice)
                    : '0,00'} ₴
                </Text>
              </View>
              <View className="flex-row items-center rounded-2xl border border-gray-200 bg-white p-1">
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel="Зменшити кількість"
                  accessibilityState={{ disabled: discountProductQuantity <= 1 }}
                  disabled={discountProductQuantity <= 1}
                  onPress={() =>
                    setDiscountProductQuantity((current) => Math.max(1, current - 1))
                  }
                  className={`h-11 w-11 items-center justify-center rounded-xl ${
                    discountProductQuantity <= 1 ? 'opacity-30' : ''
                  }`}
                >
                  <Ionicons name="remove" size={23} color="#374151" />
                </Pressable>
                <Text className="min-w-[48px] text-center text-xl font-bold text-gray-900">
                  {discountProductQuantity}
                </Text>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel="Збільшити кількість"
                  onPress={() => setDiscountProductQuantity((current) => current + 1)}
                  className="h-11 w-11 items-center justify-center rounded-xl bg-green-100"
                >
                  <Ionicons name="add" size={23} color="#15803d" />
                </Pressable>
              </View>
            </View>

            <View className="mt-5 flex-row items-center">
              <Text className="flex-1 text-sm font-semibold text-gray-500">Разом</Text>
              <Text className="text-2xl font-bold text-gray-900">
                {selectedDiscountProduct
                  ? formatBalance(
                      (selectedDiscountProduct.appPrice ??
                        selectedDiscountProduct.regularPrice ??
                        0) * discountProductQuantity
                    )
                  : '0,00'} ₴
              </Text>
            </View>

            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Підтвердити додавання до кошика"
              onPress={confirmDiscountProduct}
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
