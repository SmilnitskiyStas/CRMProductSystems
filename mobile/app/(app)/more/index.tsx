import { View, Text, TouchableOpacity, ScrollView } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import { useAuthStore } from '@/features/auth/store';

const ROLE_LABELS: Record<string, string> = {
  enterprise_admin: 'Адміністратор підприємства',
  network_manager: 'Менеджер мережі',
  store_manager: 'Менеджер магазину',
  merchandiser: 'Мерчандайзер',
  storekeeper: 'Комірник',
  cashier: 'Касир',
  provider: 'Провайдер',
};

type ModuleItem = {
  icon: React.ComponentProps<typeof Ionicons>['name'];
  label: string;
  description: string;
  href: string;
  color: string;
};

const MODULES: ModuleItem[] = [
  {
    icon: 'storefront-outline',
    label: 'Маркетплейс',
    description: 'Каталог постачальників',
    href: '/(app)/marketplace',
    color: '#16a34a',
  },
  {
    icon: 'flask-outline',
    label: 'Виробництво',
    description: 'Рецепти та виробничі ордери',
    href: '/(app)/production',
    color: '#7c3aed',
  },
  {
    icon: 'people-outline',
    label: 'Клієнти',
    description: 'База клієнтів та контакти',
    href: '/(app)/customers',
    color: '#3b82f6',
  },
  {
    icon: 'headset-outline',
    label: 'Service Desk',
    description: 'Заявки та підтримка',
    href: '/(app)/service-desk',
    color: '#8b5cf6',
  },
  {
    icon: 'calendar-outline',
    label: 'Розклад',
    description: 'Графіки змін персоналу',
    href: '/(app)/schedules',
    color: '#f59e0b',
  },
  {
    icon: 'trash-outline',
    label: 'Списання',
    description: 'Акти списання товарів',
    href: '/(app)/write-offs',
    color: '#ef4444',
  },
  {
    icon: 'swap-horizontal-outline',
    label: 'Переміщення',
    description: 'Переміщення між складами',
    href: '/(app)/transfers',
    color: '#10b981',
  },
];

export default function MoreScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <ScrollView showsVerticalScrollIndicator={false}>
        <View className="px-4 pt-4 pb-2">
          <Text className="text-2xl font-bold text-gray-900">Ще</Text>
        </View>

        {/* User info card */}
        <TouchableOpacity
          className="mx-4 mt-2 bg-white rounded-2xl p-4 flex-row items-center"
          onPress={() => router.push('/(app)/profile')}
          activeOpacity={0.7}
        >
          <View className="w-14 h-14 bg-primary-100 rounded-full items-center justify-center">
            <Text className="text-xl font-bold text-primary-600">
              {user?.fullName?.charAt(0) ?? '?'}
            </Text>
          </View>
          <View className="ml-3 flex-1">
            <Text className="text-base font-semibold text-gray-900">{user?.fullName}</Text>
            <Text className="text-sm text-gray-500">{user?.email}</Text>
            <View className="mt-1 px-2 py-0.5 bg-gray-100 rounded-full self-start">
              <Text className="text-xs text-gray-600 font-medium">
                {ROLE_LABELS[user?.role ?? ''] ?? user?.role}
              </Text>
            </View>
          </View>
          <Ionicons name="chevron-forward" size={18} color="#d1d5db" />
        </TouchableOpacity>

        {/* Modules section */}
        <View className="px-4 mt-6 mb-2">
          <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wider">
            Розділи
          </Text>
        </View>

        <View className="mx-4 bg-white rounded-2xl overflow-hidden">
          {MODULES.map((item, index) => (
            <TouchableOpacity
              key={item.href}
              onPress={() => router.push(item.href as Parameters<typeof router.push>[0])}
              activeOpacity={0.7}
              className={`flex-row items-center px-4 py-3.5 ${
                index < MODULES.length - 1 ? 'border-b border-gray-50' : ''
              }`}
            >
              <View
                className="w-9 h-9 rounded-xl items-center justify-center mr-3"
                style={{ backgroundColor: `${item.color}1a` }}
              >
                <Ionicons name={item.icon} size={19} color={item.color} />
              </View>
              <View className="flex-1">
                <Text className="text-base text-gray-800 font-medium">{item.label}</Text>
                <Text className="text-xs text-gray-400 mt-0.5">{item.description}</Text>
              </View>
              <Ionicons name="chevron-forward" size={16} color="#d1d5db" />
            </TouchableOpacity>
          ))}
        </View>

        <View className="h-8" />
      </ScrollView>
    </SafeAreaView>
  );
}
