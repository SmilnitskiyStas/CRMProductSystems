import { useState } from 'react';
import { ActivityIndicator, Alert, Text, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import { useAuthStore } from '@/features/auth/store';

export default function ConsumerAccountScreen() {
  const router = useRouter();
  const consumerUser = useAuthStore((s) => s.consumerUser);
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
          // ConsumerAuthController exposes no /logout endpoint — the access token is a
          // long-lived, non-revocable JWT (TASK-405 flagged this for security-reviewer), so
          // "logout" here is purely local: drop the token and land back on consumer login.
          await clearAuth();
          router.replace('/(auth)/consumer-login');
        },
      },
    ]);
  };

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="flex-row items-center px-5 pt-4 pb-3 bg-white border-b border-gray-100">
        <Ionicons name="person-outline" size={24} color="#16a34a" />
        <Text className="text-xl font-bold text-gray-900 ml-3">Профіль</Text>
      </View>

      <View className="mx-4 mt-4 bg-white rounded-2xl p-5">
        <View className="w-16 h-16 bg-primary-100 rounded-full items-center justify-center mb-3">
          <Text className="text-2xl font-bold text-primary-600">
            {consumerUser?.fullName?.charAt(0) ?? '?'}
          </Text>
        </View>
        <Text className="text-lg font-semibold text-gray-900">{consumerUser?.fullName}</Text>
        <Text className="text-sm text-gray-500">{consumerUser?.phone}</Text>
      </View>

      <View className="mx-4 mt-6">
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
