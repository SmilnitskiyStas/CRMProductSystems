import { useState } from 'react';
import { Alert, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useAuthStore } from '@/features/auth/store';
import { terminateSession } from '@/features/auth/session';
import { useActiveTenantStore } from '@/features/tenant/store';
import {
  RetailCard,
  RetailPressableCard,
  RetailPrimaryButton,
  RetailScreen,
} from '@/features/theme/components/RetailPrimitives';
import { useRetailTheme } from '@/features/theme/RetailThemeProvider';

export default function PersonalAccountScreen() {
  const router = useRouter();
  const staffUser = useAuthStore((state) => state.user);
  const consumerUser = useAuthStore((state) => state.consumerUser);
  const [isLoggingOut, setIsLoggingOut] = useState(false);
  const resetActiveTenant = useActiveTenantStore((state) => state.reset);
  const theme = useRetailTheme();
  const user = staffUser ?? consumerUser;

  const logout = () => {
    Alert.alert('Вийти з акаунта?', 'Для продовження потрібно буде увійти знову.', [
      { text: 'Скасувати', style: 'cancel' },
      {
        text: 'Вийти',
        style: 'destructive',
        onPress: async () => {
          setIsLoggingOut(true);
          await resetActiveTenant();
          await terminateSession();
          router.replace('/(auth)/select-role');
        },
      },
    ]);
  };

  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <RetailScreen className="flex-1 p-5">
        <Text className="text-3xl font-bold" style={{ color: theme.colors.textPrimary }}>Профіль</Text>
        <RetailCard className="mt-6 p-5">
          <View className="h-16 w-16 items-center justify-center rounded-full" style={{ backgroundColor: theme.colors.border }}>
            <Text className="text-2xl font-bold" style={{ color: theme.colors.primary }}>{user?.fullName?.charAt(0) ?? '?'}</Text>
          </View>
          <Text className="mt-4 text-xl font-semibold" style={{ color: theme.colors.textPrimary }}>{user?.fullName}</Text>
          <Text className="mt-1 text-sm" style={{ color: theme.colors.textSecondary }}>
            {staffUser?.email ?? consumerUser?.phone}
          </Text>
        </RetailCard>

        <RetailPressableCard
          accessibilityRole="button"
          onPress={() => router.push('/(personal)/retailers')}
          className="mt-4 flex-row items-center p-4"
        >
          <View className="h-11 w-11 items-center justify-center rounded-xl" style={{ backgroundColor: theme.colors.border }}>
            <Text className="text-xl">🏪</Text>
          </View>
          <View className="ml-3 flex-1">
            <Text className="font-semibold" style={{ color: theme.colors.textPrimary }}>Мої магазини</Text>
            <Text className="mt-1 text-xs" style={{ color: theme.colors.textSecondary }}>Підключення та перемикання мереж</Text>
          </View>
          <Text className="text-xl" style={{ color: theme.colors.textSecondary }}>›</Text>
        </RetailPressableCard>

        <RetailPrimaryButton
          onPress={logout}
          disabled={isLoggingOut}
          pending={isLoggingOut}
          className="mt-6 py-4"
        >
          Вийти
        </RetailPrimaryButton>
      </RetailScreen>
    </SafeAreaView>
  );
}
