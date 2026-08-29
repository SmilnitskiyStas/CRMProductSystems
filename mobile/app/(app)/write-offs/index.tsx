import { useCallback, useState } from 'react';
import { View, Text, FlatList, ActivityIndicator, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useWriteOffs } from '@/features/write-offs/hooks/useWriteOffs';
import { WriteOffCard } from '@/features/write-offs/components/WriteOffCard';
import { useAuthStore } from '@/features/auth/store';
import { useWorkspaceLocationStore } from '@/features/locations/store';
import { listQueuedWriteOffs, subscribeWriteOffQueue, type QueuedWriteOff } from '@/features/offline-mutations/writeOffQueue';
import { WRITE_OFF_REASON_LABELS } from '@/features/write-offs/types';

export default function WriteOffsScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const selectedLocationId = useWorkspaceLocationStore((state) => state.selectedLocationId);
  const locationId = selectedLocationId === undefined ? user?.locationId : selectedLocationId;
  const { data, isLoading, isError, refetch } = useWriteOffs(locationId ?? undefined);
  const [localItems, setLocalItems] = useState<QueuedWriteOff[]>([]);

  const loadLocalItems = useCallback(() => {
    if (!user?.tenantId) return;
    void listQueuedWriteOffs({ tenantId: user.tenantId, userId: user.id }).then((items) => {
      setLocalItems(items.filter((item) => !locationId || item.payload.locationId === locationId));
    });
  }, [locationId, user?.id, user?.tenantId]);

  useFocusEffect(useCallback(() => {
    loadLocalItems();
    return subscribeWriteOffQueue(loadLocalItems);
  }, [loadLocalItems]));

  const localHeader = localItems.length > 0 ? (
    <View className="mb-4 gap-2">
      <Text className="text-xs font-semibold text-gray-500 uppercase">Збережено на телефоні ({localItems.length})</Text>
      {localItems.map((item) => (
        <View key={item.operationId} className={`rounded-xl border p-4 ${item.status === 'uncertain' ? 'bg-amber-50 border-amber-200' : 'bg-blue-50 border-blue-200'}`}>
          <View className="flex-row items-center justify-between">
            <Text className="font-semibold text-gray-900">{item.payload.items.length} поз. · {WRITE_OFF_REASON_LABELS[item.payload.reason]}</Text>
            <Text className={`text-xs font-semibold ${item.status === 'uncertain' ? 'text-amber-700' : 'text-blue-700'}`}>
              {item.status === 'uncertain' ? 'Потребує перевірки' : item.status === 'failed' ? 'Очікує повтору' : 'Очікує синхронізації'}
            </Text>
          </View>
          <Text className="text-xs text-gray-500 mt-1">{new Date(item.createdAt).toLocaleString('uk-UA')}</Text>
          {item.message && <Text className="text-xs text-amber-800 mt-2">{item.message}</Text>}
        </View>
      ))}
    </View>
  ) : null;

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
          <Text className="text-2xl font-bold text-gray-900">Списання</Text>
        </View>
        <TouchableOpacity
          onPress={() => router.push('/(app)/write-offs/create')}
          className="w-10 h-10 bg-primary-600 rounded-full items-center justify-center shadow-sm"
        >
          <Ionicons name="add" size={24} color="white" />
        </TouchableOpacity>
      </View>

      {isLoading && localItems.length === 0 ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : isError && localItems.length === 0 ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-red-500 text-center">Помилка завантаження</Text>
          <TouchableOpacity onPress={() => { void refetch(); }} className="mt-4">
            <Text className="text-primary-600 font-medium">Спробувати знову</Text>
          </TouchableOpacity>
        </View>
      ) : (
        <FlatList
          data={data ?? []}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <WriteOffCard
              item={item}
              onPress={() => router.push(`/(app)/write-offs/${item.id}`)}
            />
          )}
          ItemSeparatorComponent={() => <View className="h-2" />}
          contentContainerClassName="px-4 pb-6 pt-2"
          ListHeaderComponent={localHeader}
          refreshing={false}
          onRefresh={() => { loadLocalItems(); void refetch(); }}
          ListEmptyComponent={
            localItems.length === 0 ? <View className="items-center justify-center py-20">
              <Ionicons name="document-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 text-base mt-3">Списань немає</Text>
              <TouchableOpacity
                onPress={() => router.push('/(app)/write-offs/create')}
                className="mt-4 bg-primary-600 px-5 py-2.5 rounded-xl"
              >
                <Text className="text-white font-semibold">Створити перше</Text>
              </TouchableOpacity>
            </View> : null
          }
        />
      )}
    </SafeAreaView>
  );
}
