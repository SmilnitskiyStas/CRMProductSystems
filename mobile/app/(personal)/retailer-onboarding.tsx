import { Ionicons } from '@expo/vector-icons';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { ActivityIndicator, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuthStore } from '@/features/auth/store';
import { useAvailableNetworks, useJoinTenantProgram } from '@/features/loyalty/hooks/useLoyalty';
import { useLoyaltyUiStore } from '@/features/loyalty/store';
import { parseRetailerInvite } from '@/features/retailer-onboarding/invite';
import { useSwitchActiveTenant } from '@/features/tenant/useSwitchActiveTenant';
import { trackConsumerEvent } from '@/features/consumer-analytics/analytics';

export default function RetailerOnboardingScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ code?: string | string[] }>();
  const rawCode = Array.isArray(params.code) ? params.code[0] : params.code;
  const invite = rawCode ? parseRetailerInvite(rawCode) : null;
  const hasPersonalAccess = useAuthStore((state) => state.personalAccessToken !== null);
  const networks = useAvailableNetworks(hasPersonalAccess && invite !== null);
  const join = useJoinTenantProgram();
  const setSelectedTenantId = useLoyaltyUiStore((state) => state.setSelectedTenantId);
  const switchTenant = useSwitchActiveTenant();
  const retailer = networks.data?.find((item) => item.tenantId.toLowerCase() === invite?.tenantId);

  async function confirmJoin() {
    if (!retailer) return;
    const membership = await join.mutateAsync(retailer.tenantId);
    setSelectedTenantId(membership.tenantId);
    await switchTenant(membership.tenantId);
    void trackConsumerEvent('retailer_joined', membership.tenantId, {
      source: invite?.source === 'payload' ? 'qr' : 'link',
    });
    router.replace('/(personal)');
  }

  const loading = invite && hasPersonalAccess && networks.isLoading;
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
      ) : !hasPersonalAccess ? (
        <View className="flex-1 items-center justify-center px-7"><Text className="text-center text-lg font-bold text-gray-900">Потрібен профіль покупця</Text><Text className="mt-2 text-center text-sm text-gray-500">Увійдіть як покупець, щоб приєднатися до програми магазину.</Text></View>
      ) : !invite || networks.isError || !retailer ? (
        <View className="flex-1 items-center justify-center px-7">
          <Ionicons name="warning-outline" size={48} color="#f59e0b" />
          <Text className="mt-4 text-center text-xl font-bold text-gray-900">Не вдалося підтвердити магазин</Text>
          <Text className="mt-2 text-center text-sm leading-6 text-gray-500">QR-код недійсний, мережа недоступна або більше не приймає учасників.</Text>
          <Pressable onPress={() => router.replace('/(personal)/scan')} className="mt-6 rounded-xl bg-green-700 px-5 py-3"><Text className="font-bold text-white">Сканувати інший QR</Text></Pressable>
        </View>
      ) : (
        <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 40 }}>
          <View className="items-center rounded-3xl bg-green-700 px-6 py-8">
            <View className="h-16 w-16 items-center justify-center rounded-3xl bg-white/15"><Ionicons name="storefront" size={31} color="white" /></View>
            <Text className="mt-5 text-center text-2xl font-bold text-white">{retailer.tenantName}</Text>
            <Text className="mt-2 text-center text-sm text-green-100">Перевірте магазин перед приєднанням</Text>
          </View>
          <View className="mt-4 rounded-2xl bg-white p-4">
            <Text className="font-bold text-gray-900">Магазини мережі</Text>
            {retailer.stores.map((store) => <View key={store.storeId} className="mt-3 flex-row"><Ionicons name="location-outline" size={18} color="#16a34a" /><View className="ml-2 flex-1"><Text className="font-semibold text-gray-800">{store.storeName}</Text>{store.address ? <Text className="mt-0.5 text-xs text-gray-500">{store.address}</Text> : null}</View></View>)}
          </View>
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
