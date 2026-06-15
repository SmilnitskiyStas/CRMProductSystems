import { View, Text, FlatList, TouchableOpacity, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useNotificationHistory } from '@/features/notifications/hooks/useNotifications';
import { useNotificationReadStore } from '@/features/notifications/store';
import { NotificationItem } from '@/features/notifications/components/NotificationItem';
import type { Notification } from '@/features/notifications/types';

export default function NotificationsScreen() {
  const router = useRouter();
  const { data: notifications, isLoading, refetch, isRefetching } = useNotificationHistory();
  const { readIds, markRead, markAllRead } = useNotificationReadStore();

  const unreadCount = notifications
    ? notifications.filter((n) => !readIds.has(n.id)).length
    : 0;

  function handleItemPress(id: string) {
    markRead(id);
  }

  function handleMarkAll() {
    if (notifications) {
      markAllRead(notifications.map((n) => n.id));
    }
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="flex-row items-center px-4 py-3 bg-white border-b border-gray-100">
        <TouchableOpacity onPress={() => router.back()} className="mr-3">
          <Ionicons name="arrow-back" size={24} color="#374151" />
        </TouchableOpacity>
        <View className="flex-1">
          <Text className="text-lg font-bold text-gray-900">Сповіщення</Text>
          {unreadCount > 0 && (
            <Text className="text-xs text-gray-500">{unreadCount} непрочитаних</Text>
          )}
        </View>
        {unreadCount > 0 && (
          <TouchableOpacity onPress={handleMarkAll}>
            <Text className="text-sm text-primary-600 font-medium">Всі прочитані</Text>
          </TouchableOpacity>
        )}
      </View>

      {/* List */}
      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : (
        <FlatList<Notification>
          data={notifications ?? []}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <NotificationItem
              notification={item}
              isRead={readIds.has(item.id)}
              onPress={handleItemPress}
            />
          )}
          ItemSeparatorComponent={() => <View className="h-px bg-gray-100" />}
          onRefresh={refetch}
          refreshing={isRefetching}
          contentContainerStyle={
            !notifications || notifications.length === 0
              ? { flex: 1 }
              : undefined
          }
          ListEmptyComponent={
            <View className="flex-1 items-center justify-center py-20">
              <Ionicons name="notifications-off-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 text-sm mt-3">Немає сповіщень</Text>
            </View>
          }
        />
      )}
    </SafeAreaView>
  );
}
