import { TouchableOpacity, View, Text } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useNotificationHistory } from '../hooks/useNotifications';
import { useNotificationReadStore } from '../store';

export function NotificationBell() {
  const router = useRouter();
  const { data: notifications } = useNotificationHistory();
  const readIds = useNotificationReadStore((s) => s.readIds);

  const unreadCount = notifications
    ? notifications.filter((n) => !readIds.has(n.id)).length
    : 0;

  return (
    <TouchableOpacity
      onPress={() => router.push('/(app)/notifications')}
      className="mr-4"
      hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
    >
      <Ionicons name="notifications-outline" size={24} color="#374151" />
      {unreadCount > 0 && (
        <View className="absolute -top-1 -right-1 min-w-[16px] h-4 rounded-full bg-red-500 items-center justify-center px-0.5">
          <Text className="text-white text-[10px] font-bold leading-none">
            {unreadCount > 9 ? '9+' : String(unreadCount)}
          </Text>
        </View>
      )}
    </TouchableOpacity>
  );
}
