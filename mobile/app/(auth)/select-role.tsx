import { View, Text, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';

/**
 * Entry point of the (auth) group (TASK-407) — the explicit "who are you" choice the plan
 * requires, so the staff and consumer login forms never get mixed on one screen. Reached
 * automatically by the (app)/(consumer) guards when there's no session; staff/consumer
 * logout keep their own fast paths straight back to their respective login screen.
 */
export default function SelectRoleScreen() {
  const router = useRouter();

  return (
    <SafeAreaView className="flex-1 bg-white">
      <View className="flex-1 justify-center px-6">
        <View className="mb-10 items-center">
          <Text className="text-3xl font-bold text-gray-900">ShelfGuard</Text>
          <Text className="text-base text-gray-500 mt-2 text-center">Оберіть, як ви хочете увійти</Text>
        </View>

        <View className="gap-4">
          <TouchableOpacity
            onPress={() => router.push('/(auth)/consumer-login')}
            className="bg-primary-600 rounded-2xl py-5 px-5 flex-row items-center gap-4"
          >
            <View className="w-12 h-12 bg-white/20 rounded-full items-center justify-center">
              <Ionicons name="qr-code-outline" size={24} color="white" />
            </View>
            <View className="flex-1">
              <Text className="text-white font-bold text-lg">Я покупець</Text>
              <Text className="text-white/80 text-sm mt-0.5">Бонусна картка та історія покупок</Text>
            </View>
            <Ionicons name="chevron-forward" size={20} color="white" />
          </TouchableOpacity>

          <TouchableOpacity
            onPress={() => router.push('/(auth)/login')}
            className="bg-gray-100 rounded-2xl py-5 px-5 flex-row items-center gap-4"
          >
            <View className="w-12 h-12 bg-gray-200 rounded-full items-center justify-center">
              <Ionicons name="briefcase-outline" size={24} color="#374151" />
            </View>
            <View className="flex-1">
              <Text className="text-gray-900 font-bold text-lg">Я співробітник</Text>
              <Text className="text-gray-500 text-sm mt-0.5">Каса, склад, зміни</Text>
            </View>
            <Ionicons name="chevron-forward" size={20} color="#374151" />
          </TouchableOpacity>
        </View>
      </View>
    </SafeAreaView>
  );
}
