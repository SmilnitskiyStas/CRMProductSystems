import { View, Text, TouchableOpacity, ScrollView, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusCard } from '@/features/dashboard/components/StatusCard';
import { useDashboardStats } from '@/features/dashboard/hooks/useDashboard';
import { useAuthStore } from '@/features/auth/store';

export default function DashboardScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const { data: stats, isLoading } = useDashboardStats();

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <ScrollView contentContainerClassName="p-4 gap-4">
        {/* Header */}
        <View className="flex-row items-center justify-between">
          <View>
            <Text className="text-2xl font-bold text-gray-900">Дашборд</Text>
            <Text className="text-sm text-gray-500">{user?.fullName ?? ''}</Text>
          </View>
          <Ionicons name="notifications-outline" size={24} color="#6b7280" />
        </View>

        {/* Status Cards */}
        {isLoading ? (
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
      </ScrollView>
    </SafeAreaView>
  );
}
