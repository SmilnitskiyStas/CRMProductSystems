import {
  View,
  Text,
  TouchableOpacity,
  ScrollView,
  ActivityIndicator,
  RefreshControl,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusCard } from '@/features/dashboard/components/StatusCard';
import {
  useDashboardStats,
  useAiOrders,
  useRecentMovements,
} from '@/features/dashboard/hooks/useDashboard';
import { useAuthStore } from '@/features/auth/store';
import {
  MOVEMENT_LABELS,
  AT_LEAST_STORE_MANAGER_ROLES,
  type RecentMovement,
} from '@/features/dashboard/types';

const MOVEMENT_ICONS: Record<string, React.ComponentProps<typeof Ionicons>['name']> = {
  receipt:    'download-outline',
  transfer:   'swap-horizontal-outline',
  write_off:  'trash-outline',
  adjustment: 'create-outline',
  pos_sale:   'bag-outline',
};

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString('uk-UA', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
}

function MovementRow({ item }: { item: RecentMovement }) {
  const iconName = MOVEMENT_ICONS[item.movementType] ?? 'ellipse-outline';
  const label = MOVEMENT_LABELS[item.movementType] ?? item.movementType;
  const store = item.toStoreName ?? item.fromStoreName ?? '';

  return (
    <View className="flex-row items-center gap-3 py-3">
      <View className="w-8 h-8 rounded-full bg-gray-100 items-center justify-center">
        <Ionicons name={iconName} size={16} color="#6b7280" />
      </View>
      <View className="flex-1">
        <Text className="text-sm font-medium text-gray-800">{label}</Text>
        {store ? <Text className="text-xs text-gray-500">{store}</Text> : null}
      </View>
      <View className="items-end">
        <Text className="text-sm font-semibold text-gray-700">
          {item.quantity > 0 ? '+' : ''}{item.quantity}
        </Text>
        <Text className="text-xs text-gray-400">{formatDate(item.createdAt)}</Text>
      </View>
    </View>
  );
}

export default function DashboardScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);

  const isManager = user ? AT_LEAST_STORE_MANAGER_ROLES.includes(user.role) : false;

  const {
    data: stats,
    isLoading: statsLoading,
    refetch: refetchStats,
    isRefetching: statsRefetching,
  } = useDashboardStats();

  const {
    data: aiOrders,
    refetch: refetchAiOrders,
    isRefetching: aiRefetching,
  } = useAiOrders();

  const {
    data: movementsPage,
    refetch: refetchMovements,
    isRefetching: movementsRefetching,
  } = useRecentMovements();

  const isRefreshing = statsRefetching || aiRefetching || movementsRefetching;

  function handleRefresh() {
    void refetchStats();
    void refetchMovements();
    if (isManager) void refetchAiOrders();
  }

  const pendingAiCount = aiOrders
    ? aiOrders.filter((o) => o.status === 'pending').length
    : 0;

  const recentMovements = movementsPage?.items ?? [];

  return (
    <SafeAreaView className="flex-1 bg-gray-50" edges={['bottom', 'left', 'right']}>
      <ScrollView
        contentContainerClassName="p-4 gap-4"
        refreshControl={
          <RefreshControl refreshing={isRefreshing} onRefresh={handleRefresh} tintColor="#16a34a" />
        }
      >
        {/* Greeting */}
        {user?.fullName ? (
          <View>
            <Text className="text-sm text-gray-500">{user.fullName}</Text>
          </View>
        ) : null}

        {/* Status Cards */}
        {statsLoading ? (
          <View className="h-48 items-center justify-center">
            <ActivityIndicator size="large" color="#16a34a" />
          </View>
        ) : (
          <View className="gap-3">
            <View className="flex-row gap-3">
              <StatusCard
                label="Норма"
                count={stats?.safe ?? 0}
                colorClass="text-green-700"
                bgClass="bg-green-50"
                onPress={() => router.push({ pathname: '/(app)/stock/index', params: { status: 'safe' } })}
              />
              <StatusCard
                label="Попередження"
                count={stats?.warning ?? 0}
                colorClass="text-amber-700"
                bgClass="bg-amber-50"
                onPress={() => router.push({ pathname: '/(app)/stock/index', params: { status: 'warning' } })}
              />
            </View>
            <View className="flex-row gap-3">
              <StatusCard
                label="Критично"
                count={stats?.critical ?? 0}
                colorClass="text-red-700"
                bgClass="bg-red-50"
                onPress={() => router.push({ pathname: '/(app)/stock/index', params: { status: 'critical' } })}
              />
              <StatusCard
                label="Прострочено"
                count={stats?.expired ?? 0}
                colorClass="text-purple-700"
                bgClass="bg-purple-50"
                onPress={() => router.push({ pathname: '/(app)/stock/index', params: { status: 'expired' } })}
              />
            </View>
          </View>
        )}

        {/* Scan CTA */}
        <TouchableOpacity
          onPress={() => router.push('/(app)/scan')}
          className="bg-primary-600 rounded-2xl p-5 flex-row items-center justify-between"
        >
          <View>
            <Text className="text-white text-lg font-bold">Сканувати товар</Text>
            <Text className="text-green-200 text-sm mt-0.5">Відскануйте штрихкод або QR</Text>
          </View>
          <View className="bg-white/20 w-12 h-12 rounded-full items-center justify-center">
            <Ionicons name="scan-outline" size={24} color="white" />
          </View>
        </TouchableOpacity>

        {/* Quick actions */}
        <View>
          <Text className="text-sm font-semibold text-gray-500 mb-2">Швидкі дії</Text>
          <View className="flex-row gap-3">
            <TouchableOpacity
              onPress={() => router.push('/(app)/write-offs/index')}
              className="flex-1 bg-white rounded-xl p-4 items-center gap-2 border border-gray-100"
            >
              <View className="w-10 h-10 rounded-full bg-red-50 items-center justify-center">
                <Ionicons name="trash-outline" size={20} color="#dc2626" />
              </View>
              <Text className="text-sm font-semibold text-gray-700 text-center">Списання</Text>
            </TouchableOpacity>
            <TouchableOpacity
              onPress={() => router.push('/(app)/transfers/index')}
              className="flex-1 bg-white rounded-xl p-4 items-center gap-2 border border-gray-100"
            >
              <View className="w-10 h-10 rounded-full bg-blue-50 items-center justify-center">
                <Ionicons name="swap-horizontal-outline" size={20} color="#2563eb" />
              </View>
              <Text className="text-sm font-semibold text-gray-700 text-center">Переміщення</Text>
            </TouchableOpacity>
            <TouchableOpacity
              onPress={() => router.push('/(app)/write-offs/create')}
              className="flex-1 bg-white rounded-xl p-4 items-center gap-2 border border-gray-100"
            >
              <View className="w-10 h-10 rounded-full bg-amber-50 items-center justify-center">
                <Ionicons name="add-circle-outline" size={20} color="#d97706" />
              </View>
              <Text className="text-sm font-semibold text-gray-700 text-center">Нове списання</Text>
            </TouchableOpacity>
          </View>
        </View>

        {/* AI Orders — only for managers+ */}
        {isManager && pendingAiCount > 0 && (
          <View className="bg-indigo-50 border border-indigo-200 rounded-xl p-4 flex-row items-center gap-3">
            <View className="w-10 h-10 rounded-full bg-indigo-100 items-center justify-center">
              <Ionicons name="sparkles-outline" size={20} color="#4338ca" />
            </View>
            <View className="flex-1">
              <Text className="text-sm font-bold text-indigo-800">AI замовлення</Text>
              <Text className="text-xs text-indigo-600 mt-0.5">
                {pendingAiCount} пропозиц{pendingAiCount === 1 ? 'ія' : pendingAiCount < 5 ? 'ії' : 'ій'} очікують підтвердження
              </Text>
            </View>
            <Ionicons name="chevron-forward" size={18} color="#4338ca" />
          </View>
        )}

        {/* Recent Events */}
        {recentMovements.length > 0 && (
          <View className="bg-white rounded-xl border border-gray-100 px-4">
            <Text className="text-sm font-semibold text-gray-500 pt-4 pb-2">Останні події</Text>
            {recentMovements.map((item, idx) => (
              <View key={item.id}>
                <MovementRow item={item} />
                {idx < recentMovements.length - 1 && (
                  <View className="h-px bg-gray-100" />
                )}
              </View>
            ))}
            <View className="h-4" />
          </View>
        )}
      </ScrollView>
    </SafeAreaView>
  );
}
