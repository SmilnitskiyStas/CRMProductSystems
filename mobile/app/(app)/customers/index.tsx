import { useState, useCallback } from 'react';
import {
  View,
  Text,
  FlatList,
  ActivityIndicator,
  TouchableOpacity,
  TextInput,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import { useCustomers } from '@/features/customers/hooks/useCustomers';
import { CustomerCard } from '@/features/customers/components/CustomerCard';
import { CreateCustomerModal } from '@/features/customers/components/CreateCustomerModal';

const ALLOWED_ROLES = ['StoreManager', 'NetworkManager', 'Admin'];

let searchTimeout: ReturnType<typeof setTimeout> | null = null;

export default function CustomersScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);

  // Redirect if insufficient role
  if (user && !ALLOWED_ROLES.includes(user.role)) {
    router.back();
    return null;
  }

  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [showCreate, setShowCreate] = useState(false);

  const { data, isLoading, isError, refetch } = useCustomers(search);

  const handleSearchChange = useCallback((text: string) => {
    setSearchInput(text);
    if (searchTimeout) clearTimeout(searchTimeout);
    searchTimeout = setTimeout(() => {
      setSearch(text);
    }, 300);
  }, []);

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="px-4 pt-4 pb-2 flex-row items-center justify-between">
        <View className="flex-row items-center gap-3">
          <TouchableOpacity
            onPress={() => router.back()}
            className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="arrow-back" size={20} color="#374151" />
          </TouchableOpacity>
          <Text className="text-2xl font-bold text-gray-900">Клієнти</Text>
        </View>
        <TouchableOpacity
          onPress={() => setShowCreate(true)}
          className="w-10 h-10 bg-primary-600 rounded-full items-center justify-center shadow-sm"
        >
          <Ionicons name="add" size={24} color="white" />
        </TouchableOpacity>
      </View>

      {/* Search bar */}
      <View className="px-4 pb-3">
        <View className="flex-row items-center bg-white border border-gray-200 rounded-xl px-3 gap-2">
          <Ionicons name="search-outline" size={18} color="#9ca3af" />
          <TextInput
            className="flex-1 py-2.5 text-sm text-gray-900"
            placeholder="Пошук клієнтів..."
            placeholderTextColor="#9ca3af"
            value={searchInput}
            onChangeText={handleSearchChange}
            returnKeyType="search"
          />
          {searchInput.length > 0 && (
            <TouchableOpacity onPress={() => handleSearchChange('')}>
              <Ionicons name="close-circle" size={18} color="#9ca3af" />
            </TouchableOpacity>
          )}
        </View>
      </View>

      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : isError ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-red-500 text-center">Помилка завантаження клієнтів</Text>
          <TouchableOpacity onPress={() => { void refetch(); }} className="mt-4">
            <Text className="text-primary-600 font-medium">Спробувати знову</Text>
          </TouchableOpacity>
        </View>
      ) : (
        <FlatList
          data={data?.items ?? []}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <CustomerCard
              item={item}
              onPress={() => router.push(`/(app)/customers/${item.id}`)}
            />
          )}
          ItemSeparatorComponent={() => <View className="h-2" />}
          contentContainerClassName="px-4 pb-6 pt-2"
          refreshing={false}
          onRefresh={() => { void refetch(); }}
          ListEmptyComponent={
            <View className="items-center justify-center py-20">
              <Ionicons name="people-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 text-base mt-3">
                {search ? 'Клієнтів не знайдено' : 'Клієнтів ще немає'}
              </Text>
              {!search && (
                <TouchableOpacity
                  onPress={() => setShowCreate(true)}
                  className="mt-4 bg-primary-600 px-5 py-2.5 rounded-xl"
                >
                  <Text className="text-white font-semibold">Додати клієнта</Text>
                </TouchableOpacity>
              )}
            </View>
          }
        />
      )}

      <CreateCustomerModal
        visible={showCreate}
        onClose={() => setShowCreate(false)}
      />
    </SafeAreaView>
  );
}
