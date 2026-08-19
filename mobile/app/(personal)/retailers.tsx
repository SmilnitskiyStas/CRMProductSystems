import { useMemo, useState } from 'react';
import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  Pressable,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import {
  useAvailableNetworks,
  useJoinTenantProgram,
  useMemberships,
} from '@/features/loyalty/hooks/useLoyalty';
import type { LoyaltyNetworkSummary } from '@/features/loyalty/types';
import { useActiveTenant } from '@/features/tenant/ActiveTenantProvider';
import { useSwitchActiveTenant } from '@/features/tenant/useSwitchActiveTenant';

export default function RetailersScreen() {
  const router = useRouter();
  const { activeTenantId } = useActiveTenant();
  const switchTenant = useSwitchActiveTenant();
  const memberships = useMemberships();
  const networks = useAvailableNetworks();
  const joinProgram = useJoinTenantProgram();
  const [search, setSearch] = useState('');

  const membershipTenantIds = useMemo(
    () => new Set((memberships.data ?? []).map((membership) => membership.tenantId)),
    [memberships.data]
  );
  const availableNetworks = useMemo(() => {
    const query = search.trim().toLocaleLowerCase('uk-UA');
    return (networks.data ?? []).filter(
      (network) =>
        !membershipTenantIds.has(network.tenantId) &&
        (!query || network.tenantName.toLocaleLowerCase('uk-UA').includes(query))
    );
  }, [membershipTenantIds, networks.data, search]);

  async function join(network: LoyaltyNetworkSummary) {
    try {
      const membership = await joinProgram.mutateAsync(network.tenantId);
      await switchTenant(membership.tenantId);
    } catch {
      Alert.alert('Не вдалося підключити магазин', 'Перевірте з’єднання та спробуйте ще раз.');
    }
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="flex-row items-center border-b border-gray-100 bg-white px-4 py-3">
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Назад"
          onPress={() => router.back()}
          className="h-11 w-11 items-center justify-center rounded-full bg-gray-100"
        >
          <Ionicons name="arrow-back" size={22} color="#374151" />
        </Pressable>
        <View className="ml-3 flex-1">
          <Text className="text-xl font-bold text-gray-900">Мої магазини</Text>
          <Text className="text-xs text-gray-500">Перемикайте середовище ритейлера</Text>
        </View>
      </View>

      <FlatList
        data={availableNetworks}
        keyExtractor={(item) => item.tenantId}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={{ padding: 16, paddingBottom: 40 }}
        ListHeaderComponent={
          <>
            <Text className="mb-3 text-sm font-bold uppercase tracking-wide text-gray-500">
              Підключені
            </Text>
            {memberships.isLoading ? (
              <ActivityIndicator color="#16a34a" className="py-8" />
            ) : memberships.isError ? (
              <View className="rounded-2xl bg-white p-5">
                <Text className="text-center text-sm text-gray-500">
                  Не вдалося завантажити підключені магазини
                </Text>
                <Pressable
                  onPress={() => void memberships.refetch()}
                  className="mt-3 items-center rounded-xl bg-green-700 py-3"
                >
                  <Text className="font-semibold text-white">Повторити</Text>
                </Pressable>
              </View>
            ) : (memberships.data ?? []).length === 0 ? (
              <View className="items-center rounded-2xl bg-white p-7">
                <Ionicons name="storefront-outline" size={36} color="#d1d5db" />
                <Text className="mt-3 text-center text-sm text-gray-500">
                  Ви ще не підключили жодного магазину
                </Text>
              </View>
            ) : (
              <View className="gap-3">
                {(memberships.data ?? []).map((membership) => {
                  const active = membership.tenantId === activeTenantId;
                  return (
                    <Pressable
                      key={membership.membershipId}
                      accessibilityRole="button"
                      accessibilityState={{ selected: active }}
                      onPress={() => void switchTenant(membership.tenantId)}
                      className={`flex-row items-center rounded-2xl border p-4 ${
                        active ? 'border-green-300 bg-green-50' : 'border-gray-100 bg-white'
                      }`}
                    >
                      <View className="h-11 w-11 items-center justify-center rounded-xl bg-white">
                        <Ionicons name="business-outline" size={21} color="#15803d" />
                      </View>
                      <View className="ml-3 flex-1">
                        <Text className="font-bold text-gray-900">{membership.tenantName}</Text>
                        <Text className="mt-1 text-xs text-gray-500">
                          {membership.balance.toFixed(2)} ₴ бонусів
                        </Text>
                      </View>
                      {active ? (
                        <View className="rounded-full bg-green-700 px-3 py-1.5">
                          <Text className="text-xs font-bold text-white">Активний</Text>
                        </View>
                      ) : (
                        <Ionicons name="chevron-forward" size={20} color="#9ca3af" />
                      )}
                    </Pressable>
                  );
                })}
              </View>
            )}

            <View className="mb-3 mt-8">
              <Text className="text-sm font-bold uppercase tracking-wide text-gray-500">
                Додати магазин
              </Text>
              <View className="mt-3 flex-row items-center rounded-2xl border border-gray-200 bg-white px-4">
                <Ionicons name="search" size={20} color="#9ca3af" />
                <TextInput
                  value={search}
                  onChangeText={setSearch}
                  placeholder="Пошук за назвою"
                  placeholderTextColor="#9ca3af"
                  className="ml-2 h-12 flex-1 text-gray-900"
                />
              </View>
            </View>

            {networks.isLoading ? <ActivityIndicator color="#16a34a" className="py-8" /> : null}
            {networks.isError ? (
              <Pressable
                onPress={() => void networks.refetch()}
                className="mb-3 items-center rounded-2xl bg-white p-5"
              >
                <Text className="text-sm text-gray-500">Не вдалося виконати пошук</Text>
                <Text className="mt-2 font-semibold text-green-700">Повторити</Text>
              </Pressable>
            ) : null}
          </>
        }
        renderItem={({ item }) => (
          <View className="mb-3 flex-row items-center rounded-2xl bg-white p-4">
            <View className="h-11 w-11 items-center justify-center rounded-xl bg-green-50">
              <Ionicons name="storefront-outline" size={21} color="#15803d" />
            </View>
            <View className="ml-3 flex-1">
              <Text className="font-bold text-gray-900">{item.tenantName}</Text>
              <Text className="mt-1 text-xs text-gray-500">
                {item.stores.length} торгових точок
              </Text>
            </View>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={`Підключити ${item.tenantName}`}
              disabled={joinProgram.isPending}
              onPress={() => void join(item)}
              className="rounded-xl bg-green-700 px-4 py-3"
            >
              {joinProgram.isPending && joinProgram.variables === item.tenantId ? (
                <ActivityIndicator size="small" color="white" />
              ) : (
                <Text className="font-bold text-white">Додати</Text>
              )}
            </Pressable>
          </View>
        )}
        ListEmptyComponent={
          !networks.isLoading && !networks.isError ? (
            <Text className="py-8 text-center text-sm text-gray-400">
              {search.trim() ? 'Нічого не знайдено' : 'Усі доступні магазини вже підключено'}
            </Text>
          ) : null
        }
      />
    </SafeAreaView>
  );
}
