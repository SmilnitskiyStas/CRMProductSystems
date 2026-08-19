import {
  View,
  Text,
  TouchableOpacity,
  ActivityIndicator,
  Alert,
  Linking,
  ScrollView,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import Constants from 'expo-constants';
import { apiClient } from '@/lib/api-client';
import { useAuthStore } from '@/features/auth/store';
import { logout } from '@/features/auth/api/authApi';
import { useState } from 'react';
import { useRouter } from 'expo-router';
import { useJoinAsStaff, useMyMembership } from '@/features/loyalty/hooks/useLoyalty';
import { terminateSession } from '@/features/auth/session';

const ROLE_LABELS: Record<string, string> = {
  enterprise_admin: 'Адміністратор підприємства',
  network_manager:  'Менеджер мережі',
  store_manager:    'Менеджер магазину',
  merchandiser:     'Мерчандайзер',
  storekeeper:      'Комірник',
  cashier:          'Касир',
  provider:         'Провайдер',
};

type Tool = {
  icon: React.ComponentProps<typeof Ionicons>['name'];
  label: string;
  href: string;
  color: string;
};

const ALL_TOOLS: Tool[] = [
  { icon: 'cash-outline',              label: 'Каса',         href: '/(app)/pos',          color: '#16a34a' },
  { icon: 'layers-outline',            label: 'Залишки',      href: '/(app)/stock/index',  color: '#3b82f6' },
  { icon: 'receipt-outline',           label: 'Прийомка',     href: '/(app)/receipt/index',color: '#0891b2' },
  { icon: 'swap-horizontal-outline',   label: 'Переміщення',  href: '/(app)/transfers',    color: '#10b981' },
  { icon: 'trash-outline',             label: 'Списання',     href: '/(app)/write-offs',   color: '#ef4444' },
  { icon: 'people-outline',            label: 'Клієнти',      href: '/(app)/customers',    color: '#8b5cf6' },
  { icon: 'calendar-outline',          label: 'Розклад',      href: '/(app)/schedules',    color: '#f59e0b' },
  { icon: 'headset-outline',           label: 'Service Desk', href: '/(app)/service-desk', color: '#6366f1' },
];

const ROLE_TOOL_KEYS: Record<string, string[]> = {
  cashier:          ['Каса'],
  storekeeper:      ['Залишки', 'Прийомка', 'Переміщення', 'Списання'],
  merchandiser:     ['Залишки'],
  store_manager:    ['Каса', 'Залишки', 'Прийомка', 'Переміщення', 'Списання', 'Клієнти', 'Розклад', 'Service Desk'],
  network_manager:  ['Каса', 'Залишки', 'Прийомка', 'Переміщення', 'Списання', 'Клієнти', 'Розклад', 'Service Desk'],
  enterprise_admin: ['Каса', 'Залишки', 'Прийомка', 'Переміщення', 'Списання', 'Клієнти', 'Розклад', 'Service Desk'],
  provider:         ['Каса', 'Залишки', 'Прийомка', 'Переміщення', 'Списання', 'Клієнти', 'Розклад', 'Service Desk'],
};

function toolsForRole(role: string | undefined): Tool[] {
  const keys = ROLE_TOOL_KEYS[role ?? ''] ?? [];
  return ALL_TOOLS.filter((t) => keys.includes(t.label));
}

export default function ProfileScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const [isLoggingOut, setIsLoggingOut] = useState(false);

  const myTools = toolsForRole(user?.role);

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
            await terminateSession();
            router.replace('/(auth)/select-role');
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
      { text: 'Email',    onPress: () => { void Linking.openURL('mailto:support@shelfguard.app?subject=ShelfGuard%20Mobile'); } },
    ]);
  };

  const openAbout = () => {
    const version = Constants.expoConfig?.version ?? '1.0.0';
    const apiUrl  = process.env.EXPO_PUBLIC_API_URL ?? 'не задано';
    Alert.alert(
      'Про застосунок',
      `ShelfGuard Mobile v${version}\n\nAPI: ${apiUrl}\nКористувач: ${user?.email ?? '—'}`,
    );
  };

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <ScrollView showsVerticalScrollIndicator={false}>
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

        {/* My tools */}
        {myTools.length > 0 && (
          <View className="mx-4 mt-4">
            <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">
              Мої інструменти
            </Text>
            <View className="bg-white rounded-2xl overflow-hidden">
              {myTools.map((tool, index) => (
                <TouchableOpacity
                  key={tool.href}
                  onPress={() => router.push(tool.href as Parameters<typeof router.push>[0])}
                  activeOpacity={0.7}
                  className={`flex-row items-center px-4 py-3.5 ${
                    index < myTools.length - 1 ? 'border-b border-gray-50' : ''
                  }`}
                >
                  <View
                    className="w-8 h-8 rounded-lg items-center justify-center mr-3"
                    style={{ backgroundColor: `${tool.color}1a` }}
                  >
                    <Ionicons name={tool.icon} size={17} color={tool.color} />
                  </View>
                  <Text className="text-base text-gray-700 flex-1">{tool.label}</Text>
                  <Ionicons name="chevron-forward" size={16} color="#d1d5db" />
                </TouchableOpacity>
              ))}
            </View>
          </View>
        )}

        {/* Loyalty program (plan §"Кейс 2" — staff joining their own employer's program) */}
        <LoyaltySection />

        {/* Settings */}
        <View className="mx-4 mt-4">
          <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">
            Налаштування
          </Text>
          <View className="bg-white rounded-2xl overflow-hidden">
            <MenuItem icon="notifications-outline"       label="Сповіщення (Telegram)" onPress={openNotifications} />
            <MenuItem icon="help-circle-outline"         label="Підтримка"              onPress={openSupport} />
            <MenuItem icon="information-circle-outline"  label="Про застосунок"         onPress={openAbout} isLast />
          </View>
        </View>

        <View className="mx-4 mt-4 mb-8">
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
      </ScrollView>
    </SafeAreaView>
  );
}

/**
 * TASK-405/407: staff joining their own employer's loyalty program (GET /api/loyalty/
 * my-membership, POST /api/loyalty/join-as-staff — LoyaltyController). Both are class-level
 * [RequireModule("loyalty")], so a tenant without the module enabled 403s here — hidden
 * entirely in that case rather than showing an error for a feature the tenant doesn't have,
 * matching how the rest of the app treats module-gated UI.
 */
function LoyaltySection() {
  const { data: membership, isLoading, isError, error } = useMyMembership();
  const joinMutation = useJoinAsStaff();

  const status = (error as { response?: { status?: number } } | null)?.response?.status;
  if (isError && status === 403) return null;

  return (
    <View className="mx-4 mt-4">
      <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">
        Бонусна програма
      </Text>
      <View className="bg-white rounded-2xl p-4">
        {isLoading ? (
          <ActivityIndicator color="#16a34a" />
        ) : membership ? (
          <View>
            <Text className="text-sm text-gray-500">Ваш баланс бонусів</Text>
            <Text className="text-2xl font-bold text-primary-600 mt-1">
              {membership.balance.toFixed(2)} ₴
            </Text>
          </View>
        ) : (
          <TouchableOpacity
            onPress={() => joinMutation.mutate()}
            disabled={joinMutation.isPending}
            className="flex-row items-center justify-between"
          >
            <View className="flex-1 mr-3">
              <Text className="text-base text-gray-900 font-medium">
                Приєднатися до бонусної програми
              </Text>
              <Text className="text-xs text-gray-400 mt-0.5">
                Отримуйте бонуси як клієнт свого магазину
              </Text>
            </View>
            {joinMutation.isPending ? (
              <ActivityIndicator color="#16a34a" />
            ) : (
              <Ionicons name="chevron-forward" size={18} color="#d1d5db" />
            )}
          </TouchableOpacity>
        )}
        {joinMutation.isError && (
          <Text className="text-red-500 text-xs mt-2">Не вдалося приєднатися. Спробуйте ще раз.</Text>
        )}
      </View>
    </View>
  );
}

function MenuItem({
  icon, label, onPress, isLast = false,
}: {
  icon: React.ComponentProps<typeof Ionicons>['name'];
  label: string;
  onPress: () => void;
  isLast?: boolean;
}) {
  return (
    <TouchableOpacity
      onPress={onPress}
      className={`flex-row items-center px-4 py-3.5 ${isLast ? '' : 'border-b border-gray-50'}`}
    >
      <Ionicons name={icon} size={20} color="#6b7280" />
      <Text className="text-base text-gray-700 ml-3 flex-1">{label}</Text>
      <Ionicons name="chevron-forward" size={16} color="#d1d5db" />
    </TouchableOpacity>
  );
}
