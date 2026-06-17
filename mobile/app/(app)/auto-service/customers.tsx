import { useState, useMemo } from 'react';
import {
  View,
  Text,
  FlatList,
  TouchableOpacity,
  ActivityIndicator,
  TextInput,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useCustomers } from '@/features/auto-service/hooks/useAutoService';
import {
  CustomerSheet,
  CreateCustomerModal,
} from '@/features/auto-service/components/CustomerSheet';
import type { AsCustomer } from '@/features/auto-service/types';

export default function CustomersScreen() {
  const router = useRouter();
  const { data: customers = [], isLoading, isError, refetch } = useCustomers();

  const [search, setSearch] = useState('');
  const [selectedCustomer, setSelectedCustomer] = useState<AsCustomer | null>(null);
  const [showSheet, setShowSheet] = useState(false);
  const [showCreate, setShowCreate] = useState(false);

  const filtered = useMemo(() => {
    const q = search.toLowerCase().trim();
    if (!q) return customers;
    return customers.filter(
      (c) =>
        c.name.toLowerCase().includes(q) ||
        (c.phone ?? '').toLowerCase().includes(q) ||
        (c.email ?? '').toLowerCase().includes(q)
    );
  }, [customers, search]);

  function handleSelectCustomer(customer: AsCustomer) {
    setSelectedCustomer(customer);
    setShowSheet(true);
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="px-4 pt-4 pb-3 flex-row items-center justify-between">
        <View className="flex-row items-center gap-3">
          <TouchableOpacity
            onPress={() => router.back()}
            className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="arrow-back" size={20} color="#374151" />
          </TouchableOpacity>
          <Text className="text-2xl font-bold text-gray-900">Клієнти</Text>
        </View>
      </View>

      {/* Search bar */}
      <View className="px-4 pb-3">
        <View className="flex-row items-center bg-white border border-gray-200 rounded-xl px-3 gap-2">
          <Ionicons name="search-outline" size={18} color="#9ca3af" />
          <TextInput
            className="flex-1 py-3 text-sm text-gray-900"
            placeholder="Пошук за ім'ям або телефоном..."
            value={search}
            onChangeText={setSearch}
            placeholderTextColor="#9ca3af"
            clearButtonMode="while-editing"
          />
        </View>
      </View>

      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : isError ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-red-500 text-center">Помилка завантаження</Text>
          <TouchableOpacity onPress={() => { void refetch(); }} className="mt-4">
            <Text className="text-primary-600 font-medium">Спробувати знову</Text>
          </TouchableOpacity>
        </View>
      ) : (
        <FlatList
          data={filtered}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <TouchableOpacity
              onPress={() => handleSelectCustomer(item)}
              className="bg-white mx-4 rounded-xl px-4 py-3.5"
            >
              <View className="flex-row items-center justify-between">
                <View className="flex-1 mr-3">
                  <Text className="text-sm font-semibold text-gray-900">{item.name}</Text>
                  {item.phone && (
                    <View className="flex-row items-center gap-1.5 mt-0.5">
                      <Ionicons name="call-outline" size={12} color="#9ca3af" />
                      <Text className="text-xs text-gray-400">{item.phone}</Text>
                    </View>
                  )}
                </View>
                <View className="flex-row items-center gap-1">
                  <Ionicons name="car-outline" size={14} color="#9ca3af" />
                  <Text className="text-xs text-gray-400">{item.vehicleCount ?? 0}</Text>
                </View>
              </View>
            </TouchableOpacity>
          )}
          ItemSeparatorComponent={() => <View className="h-2" />}
          contentContainerClassName="pb-24 pt-1"
          refreshing={false}
          onRefresh={() => { void refetch(); }}
          ListEmptyComponent={
            <View className="items-center justify-center py-20">
              <Ionicons name="people-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 text-base mt-3">
                {search ? 'Нічого не знайдено' : 'Клієнтів немає'}
              </Text>
            </View>
          }
        />
      )}

      {/* FAB — create customer */}
      <TouchableOpacity
        onPress={() => setShowCreate(true)}
        className="absolute bottom-8 right-5 w-14 h-14 bg-primary-600 rounded-full items-center justify-center shadow-lg"
      >
        <Ionicons name="add" size={28} color="white" />
      </TouchableOpacity>

      {/* Customer detail sheet */}
      <CustomerSheet
        customer={selectedCustomer}
        visible={showSheet}
        onClose={() => setShowSheet(false)}
      />

      {/* Create customer modal */}
      <CreateCustomerModal
        visible={showCreate}
        onClose={() => setShowCreate(false)}
      />
    </SafeAreaView>
  );
}
