import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, AppState, ScrollView, Text, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import QRCode from 'react-native-qrcode-svg';
import { useAuthStore } from '@/features/auth/store';
import { useAutoSelectMembership, useLoyaltyCode, useLoyaltyTierProgress, useMemberships } from '@/features/loyalty/hooks/useLoyalty';
import { Code128Barcode } from '@/features/loyalty/components/Code128Barcode';
import { MembershipSelector } from '@/features/loyalty/components/MembershipSelector';
import { useLoyaltyUiStore } from '@/features/loyalty/store';
import { selectMembershipForTenant } from '@/features/loyalty/selection';
import { trackConsumerEvent } from '@/features/consumer-analytics/analytics';

type ApiError = { response?: { status?: number; data?: { error?: string } } };

export default function WalletScreen() {
  const router = useRouter();
  const consumerUser = useAuthStore((state) => state.consumerUser);
  const { data: memberships, isLoading: membershipsLoading } = useMemberships();
  const selectedTenantId = useLoyaltyUiStore((state) => state.selectedTenantId);
  const setSelectedTenantId = useLoyaltyUiStore((state) => state.setSelectedTenantId);
  const [isFocused, setIsFocused] = useState(true);
  const [appActive, setAppActive] = useState(true);
  useAutoSelectMembership(memberships);

  useFocusEffect(useCallback(() => {
    setIsFocused(true);
    if (selectedTenantId) void trackConsumerEvent('loyalty_card_opened', selectedTenantId, {});
    return () => setIsFocused(false);
  }, [selectedTenantId]));

  useEffect(() => {
    const subscription = AppState.addEventListener('change', (state) => setAppActive(state === 'active'));
    return () => subscription.remove();
  }, []);

  const selectedMembership = selectMembershipForTenant(memberships, selectedTenantId);
  const tier = useLoyaltyTierProgress(selectedMembership?.tenantId ?? null);
  const refetchTier = tier.refetch;
  const { data: codeData, isLoading, isFetching, isError, error, refetch } = useLoyaltyCode(
    selectedMembership?.tenantId ?? null,
    isFocused && appActive
  );

  const responseStatus = (error as ApiError | null)?.response?.status;
  const responseError = (error as ApiError | null)?.response?.data?.error;
  const needsNetworkSelection = responseStatus === 409 && responseError === 'network_selection_required';
  const errorMessage = responseStatus === 404
    ? 'Сервіс картки покупця ще не оновлено на сервері.'
    : responseStatus === 401
      ? 'Сесію завершено. Увійдіть у застосунок ще раз.'
      : responseStatus === 403
        ? 'Ви більше не є учасником вибраної мережі. Оновіть список мереж.'
        : 'Не вдалося отримати код. Перевірте інтернет-з’єднання.';
  const displayedTierName = tier.data?.currentTierName
    ?? (tier.data?.nextTierName ? 'Ще не присвоєно' : 'Не налаштовано');

  useFocusEffect(useCallback(() => {
    if (selectedMembership?.tenantId) void refetchTier();
  }, [selectedMembership?.tenantId, refetchTier]));

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="flex-row items-center px-5 pt-4 pb-3 bg-white border-b border-gray-100">
        <Ionicons name="wallet-outline" size={24} color="#16a34a" />
        <Text className="text-xl font-bold text-gray-900 ml-3">
          Вітаємо, {consumerUser?.fullName ?? 'покупець'}
        </Text>
      </View>

      <ScrollView contentContainerStyle={{ paddingBottom: 40 }} showsVerticalScrollIndicator={false}>
        {memberships && memberships.length > 1 && (
          <View className="mt-4">
            <Text className="text-sm font-semibold text-gray-700 px-5 mb-2">Оберіть мережу магазинів</Text>
            <MembershipSelector
              memberships={memberships}
              selectedTenantId={selectedTenantId}
              onSelect={setSelectedTenantId}
            />
          </View>
        )}

        <View className="mx-4 mt-4 bg-white rounded-3xl p-6 items-center border border-gray-100 shadow-sm">
          <Text className="text-lg font-bold text-gray-900">
            {selectedMembership?.tenantName ?? 'Картка покупця'}
          </Text>
          <Text className="text-sm text-gray-500 mt-1 mb-5 text-center">
            Покажіть код на касі вибраної мережі.
          </Text>

          {membershipsLoading || (isLoading && !codeData) ? (
            <ActivityIndicator size="large" color="#16a34a" className="my-16" />
          ) : !selectedMembership ? (
            <View className="items-center py-10">
              <Ionicons name="card-outline" size={40} color="#9ca3af" />
              <Text className="mt-3 text-center font-semibold text-gray-600">
                Приєднайтеся до програми лояльності магазину
              </Text>
            </View>
          ) : needsNetworkSelection ? (
            <View className="items-center py-10">
              <Ionicons name="storefront-outline" size={40} color="#16a34a" />
              <Text className="text-gray-700 font-semibold mt-3 text-center">Оберіть мережу магазинів вище</Text>
              <Text className="text-gray-400 text-sm mt-1 text-center">
                Формат картки визначається налаштуваннями вибраної мережі.
              </Text>
            </View>
          ) : isError && !codeData ? (
            <View className="items-center py-10">
              <Ionicons name="warning-outline" size={36} color="#f59e0b" />
              <Text className="text-gray-500 text-sm mt-3 text-center">{errorMessage}</Text>
              <TouchableOpacity
                onPress={() => void refetch()}
                disabled={isFetching}
                className={`mt-3 px-4 py-2 rounded-xl ${isFetching ? 'bg-gray-200' : 'bg-gray-100'}`}
              >
                {isFetching ? (
                  <ActivityIndicator size="small" color="#16a34a" />
                ) : (
                  <Text className="text-gray-700 font-medium">Спробувати ще раз</Text>
                )}
              </TouchableOpacity>
            </View>
          ) : codeData ? (
            <>
              {codeData.displayFormat === 'qr' ? (
                <QRCode value={codeData.code} size={220} />
              ) : (
                <View style={{ width: '100%', alignItems: 'center', overflow: 'hidden' }}>
                  <Code128Barcode value={codeData.code} />
                </View>
              )}
              <Text className="text-[10px] text-gray-400 mt-3 font-mono text-center">{codeData.code}</Text>
              <Text className="text-[11px] text-gray-400 mt-1">Код оновлюється автоматично</Text>
            </>
          ) : null}

          {selectedMembership ? (
            <View className="mt-6 w-full flex-row rounded-2xl bg-gray-50 p-4">
              <View className="flex-1 items-center border-r border-gray-200">
                <Text className="text-xs text-gray-500">Баланс</Text>
                <Text className="mt-1 text-xl font-bold text-green-700">
                  {(codeData?.balance ?? selectedMembership.balance).toFixed(2)} ₴
                </Text>
              </View>
              <View className="flex-1 items-center">
                <Text className="text-xs text-gray-500">Рівень</Text>
                <Text className="mt-1 text-base font-bold text-gray-900">
                  {tier.isLoading ? 'Завантаження…' : displayedTierName}
                </Text>
              </View>
            </View>
          ) : null}
        </View>

        {selectedMembership && tier.data ? (
          <View className="mx-4 mt-4 rounded-2xl border border-amber-100 bg-white p-5">
            <View className="flex-row items-center justify-between"><Text className="text-base font-bold text-gray-900">Ваш ранг</Text><View className="rounded-full bg-amber-100 px-3 py-1"><Text className="font-bold text-amber-800">{tier.data.currentTierName ?? 'Ще не присвоєно'}</Text></View></View>
            <Text className="mt-3 text-sm text-gray-600">RFM-бали: {tier.data.compositeScore.toFixed(2)}</Text>
            {tier.data.nextTierName ? <Text className="mt-1 text-sm text-gray-600">До рівня «{tier.data.nextTierName}»: {tier.data.scoreToNextTier?.toFixed(2)} бала</Text> : <Text className="mt-1 text-sm font-medium text-green-700">Ви досягли найвищого рівня</Text>}
            <View className="mt-3 flex-row gap-2"><View className="flex-1 rounded-xl bg-green-50 p-3"><Text className="text-xs text-gray-500">Кешбек</Text><Text className="mt-1 font-bold text-green-700">{tier.data.accrualMultiplier.toFixed(2)}%</Text></View><View className="flex-1 rounded-xl bg-blue-50 p-3"><Text className="text-xs text-gray-500">Знижка</Text><Text className="mt-1 font-bold text-blue-700">{tier.data.discountPercent.toFixed(2)}%</Text></View></View>
          </View>
        ) : null}

        {selectedMembership ? (
          <TouchableOpacity
            accessibilityRole="button"
            accessibilityLabel={`Відкрити операції ${selectedMembership.tenantName}`}
            onPress={() => router.push('/(personal)/history')}
            className="mx-4 mt-4 flex-row items-center rounded-2xl border border-gray-100 bg-white p-4"
          >
            <View className="h-11 w-11 items-center justify-center rounded-xl bg-green-50">
              <Ionicons name="receipt-outline" size={22} color="#16a34a" />
            </View>
            <View className="ml-3 flex-1">
              <Text className="font-bold text-gray-900">Транзакції</Text>
              <Text className="mt-0.5 text-xs text-gray-500">Історія лише вибраної мережі</Text>
            </View>
            <Ionicons name="chevron-forward" size={20} color="#9ca3af" />
          </TouchableOpacity>
        ) : null}
      </ScrollView>
    </SafeAreaView>
  );
}
