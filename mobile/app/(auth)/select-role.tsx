import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import { Text, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

/**
 * A single public entry point for every user. Roles and extended access are assigned
 * by the business and resolved only after authentication, never chosen on this screen.
 */
export default function OnboardingScreen() {
  const router = useRouter();

  return (
    <SafeAreaView className="flex-1 bg-white">
      <View className="flex-1 px-6 pb-6">
        <View className="flex-1 justify-center">
          <View className="mb-8 h-20 w-20 items-center justify-center rounded-3xl bg-primary-100">
            <Ionicons name="shield-checkmark" size={42} color="#16a34a" />
          </View>

          <Text className="text-4xl font-bold tracking-tight text-gray-900">
            ShelfGuard
          </Text>
          <Text className="mt-4 max-w-sm text-lg leading-7 text-gray-500">
            Усе необхідне для покупок і роботи — в одному застосунку.
          </Text>

          <View className="mt-10 gap-3">
            <View className="flex-row items-center">
              <View className="h-9 w-9 items-center justify-center rounded-full bg-primary-50">
                <Ionicons name="checkmark" size={20} color="#16a34a" />
              </View>
              <Text className="ml-3 flex-1 text-base text-gray-700">
                Один акаунт для всіх можливостей
              </Text>
            </View>
            <View className="flex-row items-center">
              <View className="h-9 w-9 items-center justify-center rounded-full bg-primary-50">
                <Ionicons name="lock-closed-outline" size={18} color="#16a34a" />
              </View>
              <Text className="ml-3 flex-1 text-base text-gray-700">
                Доступи визначаються автоматично
              </Text>
            </View>
          </View>
        </View>

        <View className="gap-3">
          <TouchableOpacity
            accessibilityRole="button"
            accessibilityLabel="Увійти в акаунт"
            onPress={() => router.push('/(auth)/consumer-login')}
            className="items-center rounded-2xl bg-primary-600 py-4"
          >
            <Text className="text-base font-semibold text-white">Увійти</Text>
          </TouchableOpacity>

          <TouchableOpacity
            accessibilityRole="button"
            accessibilityLabel="Створити новий акаунт"
            onPress={() => router.push('/(auth)/consumer-register')}
            className="items-center rounded-2xl border border-gray-200 bg-white py-4"
          >
            <Text className="text-base font-semibold text-gray-900">Створити акаунт</Text>
          </TouchableOpacity>

          <Text className="mt-2 text-center text-xs leading-5 text-gray-400">
            Продовжуючи, ви погоджуєтеся з умовами використання та політикою конфіденційності
          </Text>
        </View>
      </View>
    </SafeAreaView>
  );
}
