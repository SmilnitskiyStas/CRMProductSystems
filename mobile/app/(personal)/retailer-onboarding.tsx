import { Ionicons } from '@expo/vector-icons';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { ActivityIndicator, Image, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuthStore } from '@/features/auth/store';
import { useJoinRetailerBySlug, usePublicRetailer } from '@/features/loyalty/hooks/useLoyalty';
import { useLoyaltyUiStore } from '@/features/loyalty/store';
import { parseRetailerInvite } from '@/features/retailer-onboarding/invite';
import { useSwitchActiveTenant } from '@/features/tenant/useSwitchActiveTenant';
import { trackConsumerEvent } from '@/features/consumer-analytics/analytics';
import { resolveApiAssetUrl } from '@/lib/api-client';

export default function RetailerOnboardingScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ code?: string | string[] }>();
  const rawCode = Array.isArray(params.code) ? params.code[0] : params.code;
  const invite = rawCode ? parseRetailerInvite(rawCode) : null;
  const hasPersonalAccess = useAuthStore((state) => state.personalAccessToken !== null);
  const retailerQuery = usePublicRetailer(invite?.slug ?? null);
  const join = useJoinRetailerBySlug();
  const setSelectedTenantId = useLoyaltyUiStore((state) => state.setSelectedTenantId);
  const switchTenant = useSwitchActiveTenant();
  const retailer = retailerQuery.data;

  async function confirmJoin() {
    if (!retailer) return;
    const membership = await join.mutateAsync(retailer.slug);
    setSelectedTenantId(membership.tenantId);
    await switchTenant(membership.tenantId);
    void trackConsumerEvent('retailer_joined', membership.tenantId, {
      source: invite?.source === 'custom-link' ? 'qr' : 'link',
    });
    router.replace('/(personal)');
  }

  const loading = invite && retailerQuery.isLoading;
  return (
    <SafeAreaView className="flex-1 bg-gray-50" edges={['top', 'left', 'right']}>
      <View className="flex-row items-center px-4 py-2">
        <Pressable onPress={() => router.back()} className="h-11 w-11 items-center justify-center rounded-full bg-white">
          <Ionicons name="arrow-back" size={22} color="#374151" />
        </Pressable>
        <Text className="ml-3 text-lg font-bold text-gray-900">Додати магазин</Text>
      </View>
      {loading ? (
        <View className="flex-1 items-center justify-center"><ActivityIndicator size="large" color="#16a34a" /></View>
      ) : !invite || retailerQuery.isError || !retailer ? (
        <View className="flex-1 items-center justify-center px-7">
          <Ionicons name="warning-outline" size={48} color="#f59e0b" />
          <Text className="mt-4 text-center text-xl font-bold text-gray-900">Магазин недоступний</Text>
          <Text className="mt-2 text-center text-sm leading-6 text-gray-500">Посилання недійсне або магазин зараз недоступний.</Text>
          <Pressable onPress={() => router.replace('/(personal)/scan')} className="mt-6 rounded-xl bg-green-700 px-5 py-3"><Text className="font-bold text-white">Сканувати інший QR</Text></Pressable>
        </View>
      ) : !hasPersonalAccess ? (
        <View className="flex-1 items-center justify-center px-7"><Text className="text-center text-lg font-bold text-gray-900">Потрібен профіль покупця</Text><Text className="mt-2 text-center text-sm text-gray-500">Увійдіть як покупець, щоб приєднатися до програми магазину.</Text></View>
      ) : (
        <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 40 }}>
          <View className="items-center rounded-3xl bg-green-700 px-6 py-8">
            <View className="h-16 w-16 items-center justify-center overflow-hidden rounded-3xl bg-white/15">
              {resolveApiAssetUrl(retailer.logoUrl) ? <Image source={{ uri: resolveApiAssetUrl(retailer.logoUrl) as string }} className="h-16 w-16" /> : <Ionicons name="storefront" size={31} color="white" />}
            </View>
            <Text className="mt-5 text-center text-2xl font-bold text-white">{retailer.name}</Text>
            <Text className="mt-2 text-center text-sm text-green-100">Перевірте магазин перед приєднанням</Text>
          </View>
          <View className="mt-4 rounded-2xl bg-white p-4"><Text className="text-center text-sm leading-6 text-gray-600">Після приєднання програма лояльності цього магазину з’явиться у вашому гаманці.</Text></View>
          {join.isError ? <Text className="mt-4 text-center text-sm text-red-600">Не вдалося приєднатися. Спробуйте ще раз.</Text> : null}
          <Pressable disabled={join.isPending} onPress={() => void confirmJoin()} className="mt-6 items-center rounded-2xl bg-green-700 py-4">
            {join.isPending ? <ActivityIndicator color="white" /> : <Text className="font-bold text-white">Приєднатися до мережі</Text>}
          </Pressable>
          <Pressable disabled={join.isPending} onPress={() => router.back()} className="mt-2 items-center py-4"><Text className="font-semibold text-gray-500">Скасувати</Text></Pressable>
        </ScrollView>
      )}
    </SafeAreaView>
  );
}
