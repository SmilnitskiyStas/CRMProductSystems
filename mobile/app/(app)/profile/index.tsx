import { View, Text, TouchableOpacity, ActivityIndicator, Alert, Linking } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import Constants from 'expo-constants';
import { apiClient } from '@/lib/api-client';
import { useAuthStore } from '@/features/auth/store';
import { logout } from '@/features/auth/api/authApi';
import { useState } from 'react';
import { useRouter } from 'expo-router';

const ROLE_LABELS: Record<string, string> = {
  enterprise_admin: 'Адміністратор підприємства',
  network_manager: 'Менеджер мережі',
  store_manager: 'Менеджер магазину',
  merchandiser: 'Мерчандайзер',
  storekeeper: 'Комірник',
  cashier: 'Касир',
  provider: 'Провайдер',
};

export default function ProfileScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const clearAuth = useAuthStore((s) => s.clearAuth);
  const [isLoggingOut, setIsLoggingOut] = useState(false);

  const handleLogout = () => {
    Alert.alert('Вийти з акаунту?', 'Потрібно буде увійти знову.', [
      { text: 'Скасувати', style: 'cancel' },
      {
        text: 'Вийти',
        style: 'destructive',
        onPress: async () => {
          setIsLoggingOut(true);
          try {
            await logout();
          } finally {
            await clearAuth();
            router.replace('/(auth)/login');
          }
        },
      },
    ]);
  };

  const openNotifications = () => {
    Alert.alert(
      'Сповіщення',
      'Привʼязати ваш Telegram? Бот відкриється з одноразовим кодом — натисніть у ньому Start.',
      [
        { text: 'Скасувати', style: 'cancel' },
        {
          text: 'Привʼязати Telegram',
          onPress: async () => {
            try {
              const { data } = await apiClient.post<{ deepLink: string }>('/telegram/link-code');
              await Linking.openURL(data.deepLink);
            } catch {
              Alert.alert('Помилка', 'Не вдалося згенерувати код. Спробуйте пізніше.');
            }
          },
        },
      ],
    );
  };

  const openSupport = () => {
    Alert.alert('Підтримка', 'Як зручніше звʼязатися?', [
      { text: 'Скасувати', style: 'cancel' },
      { text: 'Telegram', onPress: () => { void Linking.openURL('https://t.me/shelfguard_bot'); } },
      { text: 'Email', onPress: () => { void Linking.openURL('mailto:support@shelfguard.app?subject=ShelfGuard%20Mobile'); } },
    ]);
  };

  const openAbout = () => {
    const version = Constants.expoConfig?.version ?? '1.0.0';
    const apiUrl = process.env.EXPO_PUBLIC_API_URL ?? 'не задано';
    Alert.alert(
      'Про застосунок',
      `ShelfGuard Mobile v${version}\n\nAPI: ${apiUrl}\nКористувач: ${user?.email ?? '—'}`,
    );
  };

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="px-4 pt-4 pb-2">
        <Text className="text-2xl font-bold text-gray-900">Профіль</Text>
      </View>

      {/* User info */}
      <View className="mx-4 mt-2 bg-white rounded-2xl p-5">
        <View className="w-16 h-16 bg-primary-100 rounded-full items-center justify-center mb-3">
          <Text className="text-2xl font-bold text-primary-600">
            {user?.fullName?.charAt(0) ?? '?'}
          </Text>
        </View>
        <Text className="text-lg font-semibold text-gray-900">{user?.fullName}</Text>
        <Text className="text-sm text-gray-500">{user?.email}</Text>
        <View className="mt-2 px-2 py-0.5 bg-gray-100 rounded-full self-start">
          <Text className="text-xs text-gray-600 font-medium">
            {ROLE_LABELS[user?.role ?? ''] ?? user?.role}
          </Text>
        </View>
      </View>

      <View className="mx-4 mt-4 bg-white rounded-2xl overflow-hidden">
        <MenuItem icon="notifications-outline" label="Сповіщення (Telegram)" onPress={openNotifications} />
        <MenuItem icon="help-circle-outline" label="Підтримка" onPress={openSupport} />
        <MenuItem icon="information-circle-outline" label="Про застосунок" onPress={openAbout} />
      </View>

      <View className="mx-4 mt-4">
        <TouchableOpacity
          onPress={handleLogout}
          disabled={isLoggingOut}
          className="bg-red-50 border border-red-100 py-4 rounded-xl items-center"
        >
          {isLoggingOut ? (
            <ActivityIndicator color="#dc2626" />
          ) : (
            <Text className="text-red-600 font-semibold">Вийти з акаунту</Text>
          )}
        </TouchableOpacity>
      </View>
    </SafeAreaView>
  );
}

function MenuItem({
  icon, label, onPress,
}: {
  icon: React.ComponentProps<typeof Ionicons>['name'];
  label: string;
  onPress: () => void;
}) {
  return (
    <TouchableOpacity onPress={onPress} className="flex-row items-center px-4 py-3.5 border-b border-gray-50">
      <Ionicons name={icon} size={20} color="#6b7280" />
      <Text className="text-base text-gray-700 ml-3 flex-1">{label}</Text>
      <Ionicons name="chevron-forward" size={16} color="#d1d5db" />
    </TouchableOpacity>
  );
}
