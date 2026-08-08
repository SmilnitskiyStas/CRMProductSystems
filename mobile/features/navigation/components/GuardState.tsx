import { Text, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';

export function GuardState({
  moduleDisabled = false,
  contextUnavailable = false,
  onRetry,
}: {
  moduleDisabled?: boolean;
  contextUnavailable?: boolean;
  onRetry?: () => void;
}) {
  const router = useRouter();
  return (
    <SafeAreaView className="flex-1 bg-gray-50 items-center justify-center px-8">
      <View className="bg-white rounded-2xl p-6 w-full items-center border border-gray-100">
        <Text className="text-xl font-bold text-gray-900">
          {contextUnavailable ? 'Не вдалося перевірити доступ' : moduleDisabled ? 'Модуль вимкнено' : 'Доступ заборонено'}
        </Text>
        <Text className="text-sm text-gray-500 text-center mt-2">
          {contextUnavailable
            ? 'Перевірте з’єднання та спробуйте ще раз.'
            : moduleDisabled
            ? 'Цей модуль не активований для вашої компанії.'
            : 'Вашій ролі не надано доступ до цього розділу.'}
        </Text>
        <TouchableOpacity className="bg-primary-600 rounded-xl px-5 py-3 mt-5" onPress={onRetry ?? (() => router.replace('/(app)'))}>
          <Text className="text-white font-semibold">{onRetry ? 'Спробувати ще раз' : 'На головну'}</Text>
        </TouchableOpacity>
      </View>
    </SafeAreaView>
  );
}
