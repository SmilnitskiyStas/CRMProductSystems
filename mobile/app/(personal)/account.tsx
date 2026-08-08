import { useState } from 'react';
import { ActivityIndicator, Alert, Text, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useAuthStore } from '@/features/auth/store';
import { terminateSession } from '@/features/auth/session';

export default function PersonalAccountScreen() {
  const router = useRouter();
  const staffUser = useAuthStore((state) => state.user);
  const consumerUser = useAuthStore((state) => state.consumerUser);
  const [isLoggingOut, setIsLoggingOut] = useState(false);
  const user = staffUser ?? consumerUser;

  const logout = () => {
    Alert.alert('Вийти з акаунта?', 'Для продовження потрібно буде увійти знову.', [
      { text: 'Скасувати', style: 'cancel' },
      {
        text: 'Вийти',
        style: 'destructive',
        onPress: async () => {
          setIsLoggingOut(true);
          await terminateSession();
          router.replace('/(auth)/select-role');
        },
      },
    ]);
  };

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="p-5">
        <Text className="text-3xl font-bold text-gray-900">Профіль</Text>
        <View className="mt-6 rounded-3xl bg-white p-5">
          <View className="h-16 w-16 items-center justify-center rounded-full bg-primary-100">
            <Text className="text-2xl font-bold text-primary-700">{user?.fullName?.charAt(0) ?? '?'}</Text>
          </View>
          <Text className="mt-4 text-xl font-semibold text-gray-900">{user?.fullName}</Text>
          <Text className="mt-1 text-sm text-gray-500">
            {staffUser?.email ?? consumerUser?.phone}
          </Text>
        </View>

        <TouchableOpacity
          onPress={logout}
          disabled={isLoggingOut}
          className="mt-6 items-center rounded-2xl border border-red-100 bg-red-50 py-4"
        >
          {isLoggingOut ? <ActivityIndicator color="#dc2626" /> : <Text className="font-semibold text-red-600">Вийти</Text>}
        </TouchableOpacity>
      </View>
    </SafeAreaView>
  );
}
