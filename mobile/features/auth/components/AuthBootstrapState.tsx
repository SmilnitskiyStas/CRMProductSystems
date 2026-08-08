import { Text, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { retrySessionBootstrap } from '../bootstrap';

export function AuthBootstrapState() {
  return (
    <SafeAreaView className="flex-1 bg-gray-50 items-center justify-center px-8">
      <View className="bg-white rounded-2xl p-6 w-full items-center border border-gray-100">
        <Text className="text-xl font-bold text-gray-900">Немає з’єднання</Text>
        <Text className="text-sm text-gray-500 text-center mt-2">
          Сесію збережено. Підключіться до мережі та повторіть перевірку.
        </Text>
        <TouchableOpacity
          className="bg-primary-600 rounded-xl px-5 py-3 mt-5"
          onPress={() => void retrySessionBootstrap()}
        >
          <Text className="text-white font-semibold">Спробувати ще раз</Text>
        </TouchableOpacity>
      </View>
    </SafeAreaView>
  );
}
