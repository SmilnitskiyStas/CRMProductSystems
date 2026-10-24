import { SafeAreaView } from 'react-native-safe-area-context';
import { Text, TouchableOpacity, View } from 'react-native';
import { useRouter } from 'expo-router';
import { useAuthStore } from '../store';

export function MfaSetupReminder() {
  const router = useRouter();
  const user = useAuthStore(state => state.user);
  if (!user || user.twoFactorEnabled !== false || user.role === 'consumer') return null;
  return <SafeAreaView edges={['top']} className="bg-blue-50"><View className="bg-blue-50 border-b border-blue-100 px-4 py-3" accessibilityRole="summary">
    <Text className="text-blue-900">Для більшого захисту даних рекомендуємо підключити двофакторну автентифікацію в налаштуваннях акаунту.</Text>
    <TouchableOpacity accessibilityRole="button" onPress={() => router.navigate('/(app)/profile')} className="mt-2 self-start">
      <Text className="text-blue-700 font-semibold">Налаштувати захист</Text>
    </TouchableOpacity>
  </View></SafeAreaView>;
}
