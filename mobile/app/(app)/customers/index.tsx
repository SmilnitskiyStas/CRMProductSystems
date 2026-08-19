import { useState, useCallback, useEffect } from 'react';
import {
  View,
  FlatList,
} from 'react-native';
import { useRouter } from 'expo-router';
import { useAuthStore } from '@/features/auth/store';
import { useCustomers } from '@/features/customers/hooks/useCustomers';
import { CustomerCard } from '@/features/customers/components/CustomerCard';
import { CreateCustomerModal } from '@/features/customers/components/CreateCustomerModal';
import { AT_LEAST_STORE_MANAGER, hasRole } from '@/lib/roles';
import { EmptyState, ErrorState, Header, IconButton, Screen, Skeleton, TextField } from '@/components/ui';

let searchTimeout: ReturnType<typeof setTimeout> | null = null;

export default function CustomersScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const canAccess = !user || hasRole(user.role, AT_LEAST_STORE_MANAGER);

  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [showCreate, setShowCreate] = useState(false);

  const { data, isLoading, isError, refetch } = useCustomers(search, canAccess);

  useEffect(() => {
    if (!canAccess) router.back();
  }, [canAccess, router]);

  const handleSearchChange = useCallback((text: string) => {
    setSearchInput(text);
    if (searchTimeout) clearTimeout(searchTimeout);
    searchTimeout = setTimeout(() => {
      setSearch(text);
    }, 300);
  }, []);

  if (!canAccess) return null;

  return (
    <Screen>
      <Header title="Клієнти" onBack={() => router.back()} action={<IconButton icon="add" label="Додати клієнта" onPress={() => setShowCreate(true)} color="#15803d" />} />

      {/* Search bar */}
      <View className="px-4 pb-3">
        <TextField
            label="Пошук"
            accessibilityLabel="Пошук клієнтів"
            placeholder="Пошук клієнтів..."
            placeholderTextColor="#9ca3af"
            value={searchInput}
            onChangeText={handleSearchChange}
            returnKeyType="search"
            trailing={searchInput.length > 0 ? <IconButton icon="close" label="Очистити пошук" onPress={() => handleSearchChange('')} /> : undefined}
          />
      </View>

      {isLoading ? (
        <View className="px-4 gap-3"><Skeleton className="h-20 w-full" /><Skeleton className="h-20 w-full" /><Skeleton className="h-20 w-full" /></View>
      ) : isError ? (
        <ErrorState title="Не вдалося завантажити клієнтів" onAction={() => { void refetch(); }} />
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
            <EmptyState title={search ? 'Клієнтів не знайдено' : 'Клієнтів ще немає'} icon="people-outline" actionLabel={!search ? 'Додати клієнта' : undefined} onAction={!search ? () => setShowCreate(true) : undefined} />
          }
        />
      )}

      <CreateCustomerModal
        visible={showCreate}
        onClose={() => setShowCreate(false)}
      />
    </Screen>
  );
}
