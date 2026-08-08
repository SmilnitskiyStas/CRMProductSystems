import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import { ScrollView, Text, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuthStore } from '@/features/auth/store';

export default function PersonalHomeScreen() {
  const router = useRouter();
  const staffUser = useAuthStore((state) => state.user);
  const consumerUser = useAuthStore((state) => state.consumerUser);
  const workspaceAccessToken = useAuthStore((state) => state.workspaceAccessToken);
  const canAccessWorkspace = workspaceAccessToken !== null;
  const fullName = staffUser?.fullName ?? consumerUser?.fullName ?? 'користувачу';

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 40 }}>
        <Text className="text-sm font-medium text-primary-600">ShelfGuard</Text>
        <Text className="mt-2 text-3xl font-bold text-gray-900">Вітаємо, {fullName}</Text>
        <Text className="mt-2 text-base leading-6 text-gray-500">
          Це ваш особистий простір. Тут з’являтимуться покупки, бонуси та інші можливості.
        </Text>

        {canAccessWorkspace && (
          <TouchableOpacity
            accessibilityRole="button"
            accessibilityLabel="Перейти до робочого простору"
            onPress={() => router.push('/(app)')}
            className="mt-8 rounded-3xl bg-gray-900 p-5"
          >
            <View className="flex-row items-center">
              <View className="h-12 w-12 items-center justify-center rounded-2xl bg-white/10">
                <Ionicons name="briefcase-outline" size={25} color="white" />
              </View>
              <View className="ml-4 flex-1">
                <Text className="text-lg font-bold text-white">Робочий простір</Text>
                <Text className="mt-1 text-sm leading-5 text-gray-300">
                  Каса, склад та доступні вам робочі модулі
                </Text>
              </View>
              <Ionicons name="arrow-forward" size={22} color="white" />
            </View>
          </TouchableOpacity>
        )}

        <View className="mt-8 rounded-3xl border border-gray-100 bg-white p-5">
          <View className="h-12 w-12 items-center justify-center rounded-2xl bg-primary-50">
            <Ionicons name="sparkles-outline" size={24} color="#16a34a" />
          </View>
          <Text className="mt-4 text-lg font-semibold text-gray-900">Особисті можливості</Text>
          <Text className="mt-2 text-sm leading-6 text-gray-500">
            Цей розділ підготовлено для наступних функцій звичайного користувача.
          </Text>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}
