import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  AppState,
  ScrollView,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import QRCode from 'react-native-qrcode-svg';
import { useAuthStore } from '@/features/auth/store';
import {
  useAutoSelectMembership,
  useJoinTenantProgram,
  useLoyaltyCode,
  useMemberships,
} from '@/features/loyalty/hooks/useLoyalty';
import { MembershipSelector } from '@/features/loyalty/components/MembershipSelector';
import { useLoyaltyUiStore } from '@/features/loyalty/store';

function formatMoney(n: number): string {
  return n.toFixed(2);
}

export default function WalletScreen() {
  const consumerUser = useAuthStore((s) => s.consumerUser);

  const { data: memberships, isLoading: membershipsLoading, refetch: refetchMemberships } = useMemberships();
  const selectedTenantId = useAutoSelectMembership(memberships);
  const setSelectedTenantId = useLoyaltyUiStore((s) => s.setSelectedTenantId);

  // The QR/code is a rotating security secret (plan §"живий QR") — poll it only while this
  // tab is focused AND the app itself is foregrounded, never in the background.
  const [isFocused, setIsFocused] = useState(true);
  const [appActive, setAppActive] = useState(true);

  useFocusEffect(
    useCallback(() => {
      setIsFocused(true);
      return () => setIsFocused(false);
    }, [])
  );

  useEffect(() => {
    const sub = AppState.addEventListener('change', (state) => setAppActive(state === 'active'));
    return () => sub.remove();
  }, []);

  const pollingEnabled = isFocused && appActive;
  const {
    data: codeData,
    isLoading: codeLoading,
    isError: codeError,
    refetch: refetchCode,
  } = useLoyaltyCode(selectedTenantId, pollingEnabled);

  // No discovery/browse endpoint exists yet for "which stores run a loyalty program" — this
  // manual tenant-id entry is an intentionally minimal stopgap so the wallet is reachable
  // end-to-end without a backend change (see task log: flagged for a nicer QR-at-checkout or
  // staff-assisted onboarding flow).
  const [showJoinForm, setShowJoinForm] = useState(false);
  const [tenantIdInput, setTenantIdInput] = useState('');
  const joinMutation = useJoinTenantProgram();

  const handleJoin = () => {
    const tenantId = tenantIdInput.trim();
    if (!tenantId) return;
    joinMutation.mutate(tenantId, {
      onSuccess: (membership) => {
        setShowJoinForm(false);
        setTenantIdInput('');
        setSelectedTenantId(membership.tenantId);
        void refetchMemberships();
      },
      onError: () => {
        Alert.alert('Помилка', 'Не вдалося приєднатися. Перевірте код і спробуйте ще раз.');
      },
    });
  };

  const selectedMembership = memberships?.find((m) => m.tenantId === selectedTenantId) ?? null;

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="flex-row items-center px-5 pt-4 pb-3 bg-white border-b border-gray-100">
        <Ionicons name="wallet-outline" size={24} color="#16a34a" />
        <Text className="text-xl font-bold text-gray-900 ml-3">
          Вітаємо, {consumerUser?.fullName ?? 'покупець'}
        </Text>
      </View>

      <ScrollView contentContainerStyle={{ paddingBottom: 40 }} showsVerticalScrollIndicator={false}>
        {membershipsLoading ? (
          <View className="items-center py-16">
            <ActivityIndicator size="large" color="#16a34a" />
          </View>
        ) : memberships && memberships.length > 0 ? (
          <>
            <View className="mt-4">
              <MembershipSelector
                memberships={memberships}
                selectedTenantId={selectedTenantId}
                onSelect={setSelectedTenantId}
              />
            </View>

            {selectedMembership && (
              <View className="mx-4 bg-white rounded-3xl p-6 items-center border border-gray-100 shadow-sm">
                <Text className="text-sm font-semibold text-gray-500 mb-4">
                  {selectedMembership.tenantName}
                </Text>

                {selectedMembership.status !== 'active' ? (
                  <View className="items-center py-10">
                    <Ionicons name="lock-closed-outline" size={40} color="#ef4444" />
                    <Text className="text-red-500 font-semibold mt-3">Картку заблоковано</Text>
                    <Text className="text-gray-400 text-sm mt-1 text-center">
                      Зверніться до магазину для розблокування
                    </Text>
                  </View>
                ) : codeLoading && !codeData ? (
                  <View className="items-center py-16">
                    <ActivityIndicator size="large" color="#16a34a" />
                  </View>
                ) : codeError && !codeData ? (
                  <View className="items-center py-10">
                    <Ionicons name="warning-outline" size={36} color="#f59e0b" />
                    <Text className="text-gray-500 text-sm mt-3 text-center">Не вдалося оновити код</Text>
                    <TouchableOpacity onPress={() => refetchCode()} className="mt-3 bg-gray-100 px-4 py-2 rounded-xl">
                      <Text className="text-gray-700 text-sm font-medium">Спробувати ще раз</Text>
                    </TouchableOpacity>
                  </View>
                ) : codeData ? (
                  <>
                    <QRCode value={codeData.code} size={220} />
                    <Text className="text-xs text-gray-400 mt-4 font-mono text-center">{codeData.code}</Text>
                    <Text className="text-[11px] text-gray-300 mt-1">Код оновлюється автоматично</Text>
                    <View className="h-px bg-gray-100 w-full my-4" />
                    <Text className="text-sm text-gray-500">Баланс бонусів</Text>
                    <Text className="text-3xl font-bold text-primary-600 mt-1">
                      {formatMoney(codeData.balance)} ₴
                    </Text>
                  </>
                ) : null}
              </View>
            )}

            <TouchableOpacity onPress={() => setShowJoinForm((v) => !v)} className="mx-4 mt-5 items-center py-2">
              <Text className="text-primary-600 text-sm font-medium">+ Приєднатися до іншої програми</Text>
            </TouchableOpacity>
          </>
        ) : (
          <View className="items-center px-6 py-16">
            <Ionicons name="wallet-outline" size={56} color="#d1d5db" />
            <Text className="text-gray-500 text-center mt-4">У вас ще немає активних бонусних програм</Text>
            {!showJoinForm && (
              <TouchableOpacity onPress={() => setShowJoinForm(true)} className="mt-5 bg-primary-600 px-6 py-3 rounded-xl">
                <Text className="text-white font-semibold">Приєднатися до програми</Text>
              </TouchableOpacity>
            )}
          </View>
        )}

        {showJoinForm && (
          <View className="mx-4 mt-4 bg-white rounded-2xl p-4 border border-gray-100">
            <Text className="text-sm font-semibold text-gray-700 mb-2">Код магазину</Text>
            <Text className="text-xs text-gray-400 mb-2">
              Дізнайтесь код бонусної програми у персоналу магазину.
            </Text>
            <TextInput
              className="border border-gray-300 rounded-xl px-4 py-3 text-sm text-gray-900 bg-gray-50"
              placeholder="Код закладу"
              autoCapitalize="none"
              value={tenantIdInput}
              onChangeText={setTenantIdInput}
            />
            <TouchableOpacity
              onPress={handleJoin}
              disabled={joinMutation.isPending || !tenantIdInput.trim()}
              className={`mt-3 rounded-xl py-3 items-center ${
                joinMutation.isPending || !tenantIdInput.trim() ? 'bg-gray-200' : 'bg-primary-600'
              }`}
            >
              {joinMutation.isPending ? (
                <ActivityIndicator color="white" />
              ) : (
                <Text className={`font-semibold ${!tenantIdInput.trim() ? 'text-gray-400' : 'text-white'}`}>
                  Приєднатись
                </Text>
              )}
            </TouchableOpacity>
          </View>
        )}
      </ScrollView>
    </SafeAreaView>
  );
}
