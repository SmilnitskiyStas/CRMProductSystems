import { TouchableOpacity, View, Text } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import type { Notification, NotificationCategory } from '../types';
import { categorize } from '../types';

const CATEGORY_ICON: Record<NotificationCategory, { name: React.ComponentProps<typeof Ionicons>['name']; color: string; bg: string }> = {
  expiry:  { name: 'time-outline',          color: '#d97706', bg: 'bg-amber-100' },
  stock:   { name: 'layers-outline',        color: '#2563eb', bg: 'bg-blue-100'  },
  system:  { name: 'notifications-outline', color: '#6b7280', bg: 'bg-gray-100'  },
};

const EVENT_LABELS: Record<string, string> = {
  expiry_warning:  'Попередження про термін',
  expiry_critical: 'Критичний термін',
  expiry_expired:  'Товар прострочено',
  stock_low:       'Залишок низький',
  low_stock:       'Залишок низький',
  stock_out:       'Товар відсутній',
  system:          'Системне сповіщення',
};

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString('uk-UA', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
}

interface Props {
  notification: Notification;
  isRead: boolean;
  onPress: (id: string) => void;
}

export function NotificationItem({ notification, isRead, onPress }: Props) {
  const category = categorize(notification.eventType);
  const icon = CATEGORY_ICON[category];
  const label = EVENT_LABELS[notification.eventType] ?? notification.eventType;

  return (
    <TouchableOpacity
      onPress={() => onPress(notification.id)}
      className={`flex-row gap-3 p-4 ${isRead ? 'bg-white' : 'bg-green-50'}`}
      activeOpacity={0.7}
    >
      {!isRead && (
        <View className="absolute left-0 top-0 bottom-0 w-1 bg-primary-500 rounded-l-sm" />
      )}
      <View className={`w-10 h-10 rounded-full items-center justify-center ${icon.bg}`}>
        <Ionicons name={icon.name} size={20} color={icon.color} />
      </View>
      <View className="flex-1">
        <View className="flex-row items-center justify-between">
          <Text className={`text-sm font-semibold ${isRead ? 'text-gray-700' : 'text-gray-900'}`}>
            {label}
          </Text>
          {!isRead && (
            <View className="w-2 h-2 rounded-full bg-primary-500" />
          )}
        </View>
        {notification.payload ? (
          <Text className="text-xs text-gray-500 mt-0.5" numberOfLines={2}>
            {notification.payload}
          </Text>
        ) : null}
        <Text className="text-xs text-gray-400 mt-1">
          {formatDate(notification.createdAt)}
        </Text>
      </View>
    </TouchableOpacity>
  );
}
