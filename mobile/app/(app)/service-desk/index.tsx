import { useState } from 'react';
import {
  View,
  Text,
  FlatList,
  ActivityIndicator,
  TouchableOpacity,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import { useMyTickets, useTickets } from '@/features/service-desk/hooks/useServiceDesk';
import { TicketCard } from '@/features/service-desk/components/TicketCard';
import { CreateTicketModal } from '@/features/service-desk/components/CreateTicketModal';
import type { Ticket } from '@/features/service-desk/types';
import { AT_LEAST_STORE_MANAGER_OR_PROVIDER, hasRole } from '@/lib/roles';

type Tab = 'my' | 'all';

export default function ServiceDeskScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const isManager = hasRole(user?.role, AT_LEAST_STORE_MANAGER_OR_PROVIDER);

  const [activeTab, setActiveTab] = useState<Tab>('my');
  const [showCreate, setShowCreate] = useState(false);

  const myTicketsQuery = useMyTickets();
  const allTicketsQuery = useTickets(undefined);

  const activeQuery = activeTab === 'my' ? myTicketsQuery : allTicketsQuery;
  const tickets: Ticket[] =
    activeTab === 'my'
      ? (myTicketsQuery.data ?? [])
      : (allTicketsQuery.data?.items ?? []);

  const { isLoading, isError, refetch } = activeQuery;

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="px-4 pt-4 pb-3 flex-row items-center gap-3">
        <TouchableOpacity
          onPress={() => router.back()}
          className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
        >
          <Ionicons name="arrow-back" size={20} color="#374151" />
        </TouchableOpacity>
        <Text className="text-2xl font-bold text-gray-900 flex-1">Підтримка</Text>
      </View>

      {/* Tabs */}
      {isManager && (
        <View className="flex-row mx-4 mb-3 bg-gray-100 rounded-xl p-1">
          <TouchableOpacity
            onPress={() => setActiveTab('my')}
            className={`flex-1 py-2 rounded-lg items-center ${
              activeTab === 'my' ? 'bg-white' : ''
            }`}
          >
            <Text
              className={`text-sm font-semibold ${
                activeTab === 'my' ? 'text-gray-900' : 'text-gray-500'
              }`}
            >
              Мої тікети
            </Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={() => setActiveTab('all')}
            className={`flex-1 py-2 rounded-lg items-center ${
              activeTab === 'all' ? 'bg-white' : ''
            }`}
          >
            <Text
              className={`text-sm font-semibold ${
                activeTab === 'all' ? 'text-gray-900' : 'text-gray-500'
              }`}
            >
              Всі тікети
            </Text>
          </TouchableOpacity>
        </View>
      )}

      {/* Content */}
      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : isError ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-red-500 text-center">Помилка завантаження тікетів</Text>
          <TouchableOpacity onPress={() => { void refetch(); }} className="mt-4">
            <Text className="text-primary-600 font-medium">Спробувати знову</Text>
          </TouchableOpacity>
        </View>
      ) : (
        <FlatList
          data={tickets}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <TicketCard
              item={item}
              onPress={() => router.push(`/(app)/service-desk/${item.id}`)}
            />
          )}
          ItemSeparatorComponent={() => <View className="h-2" />}
          contentContainerClassName="px-4 pb-24 pt-2"
          refreshing={false}
          onRefresh={() => { void refetch(); }}
          ListEmptyComponent={
            <View className="items-center justify-center py-20">
              <Ionicons name="chatbubble-ellipses-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 text-base mt-3">Немає тікетів</Text>
              <TouchableOpacity
                onPress={() => setShowCreate(true)}
                className="mt-4 bg-primary-600 px-5 py-2.5 rounded-xl"
              >
                <Text className="text-white font-semibold">Створити тікет</Text>
              </TouchableOpacity>
            </View>
          }
        />
      )}

      {/* FAB */}
      <TouchableOpacity
        onPress={() => setShowCreate(true)}
        className="absolute bottom-6 right-6 w-14 h-14 bg-primary-600 rounded-full items-center justify-center shadow-lg"
      >
        <Ionicons name="add" size={28} color="white" />
      </TouchableOpacity>

      <CreateTicketModal
        visible={showCreate}
        onClose={() => setShowCreate(false)}
      />
    </SafeAreaView>
  );
}
