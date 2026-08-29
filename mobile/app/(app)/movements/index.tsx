import { ActivityIndicator, FlatList, Text, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Stack, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import { useRecentMovements } from '@/features/dashboard/hooks/useDashboard';
import { MOVEMENT_LABELS, type RecentMovement } from '@/features/dashboard/types';
import { useWorkspaceLocationStore } from '@/features/locations/store';

export default function MovementsScreen() {
  const router = useRouter();
  const assignedLocationId = useAuthStore((state) => state.user?.locationId);
  const selectedLocationId = useWorkspaceLocationStore((state) => state.selectedLocationId);
  const locationId = selectedLocationId === undefined ? assignedLocationId : selectedLocationId;
  const query = useRecentMovements(locationId ?? undefined, 100);

  function openMovement(item: RecentMovement) {
    router.push({
      pathname: '/(app)/movements/[id]',
      params: { id: item.id, movement: JSON.stringify(item) },
    });
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <Stack.Screen options={{ headerShown: true, title: 'Історія подій', headerBackTitle: '' }} />
      {query.isLoading ? (
        <View className="flex-1 items-center justify-center"><ActivityIndicator size="large" color="#16a34a" /></View>
      ) : query.isError ? (
        <View className="flex-1 items-center justify-center px-6">
          <Text className="text-red-500 text-center">Не вдалося завантажити історію</Text>
          <TouchableOpacity onPress={() => void query.refetch()} className="mt-4"><Text className="text-primary-600">Спробувати знову</Text></TouchableOpacity>
        </View>
      ) : (
        <FlatList
          data={query.data?.items ?? []}
          keyExtractor={(item) => item.id}
          contentContainerClassName="p-4"
          ItemSeparatorComponent={() => <View className="h-2" />}
          renderItem={({ item }) => (
            <TouchableOpacity onPress={() => openMovement(item)} className="bg-white rounded-xl p-4 flex-row items-center">
              <View className="w-10 h-10 rounded-full bg-gray-100 items-center justify-center">
                <Ionicons name="swap-horizontal-outline" size={19} color="#16a34a" />
              </View>
              <View className="flex-1 ml-3">
                <Text className="font-semibold text-gray-900">{MOVEMENT_LABELS[item.movementType] ?? item.movementType}</Text>
                <Text className="text-sm text-gray-500 mt-1">{item.productName ?? 'Товар'} · {item.quantity}</Text>
                <Text className="text-xs text-gray-400 mt-1">{new Date(item.createdAt).toLocaleString('uk-UA')}</Text>
              </View>
              <Ionicons name="chevron-forward" size={17} color="#d1d5db" />
            </TouchableOpacity>
          )}
          refreshing={query.isRefetching}
          onRefresh={() => void query.refetch()}
          ListEmptyComponent={<Text className="text-center text-gray-400 py-20">Подій не знайдено</Text>}
        />
      )}
    </SafeAreaView>
  );
}
