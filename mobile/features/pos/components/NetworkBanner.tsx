import NetInfo, { useNetInfo } from '@react-native-community/netinfo';
import { Text, TouchableOpacity, View } from 'react-native';

export function NetworkBanner() {
  const network = useNetInfo();
  const offline = network.isConnected === false || network.isInternetReachable === false;
  if (!offline) return null;

  return (
    <View className="bg-amber-100 border-b border-amber-200 px-4 py-2 flex-row items-center">
      <Text className="text-amber-900 text-xs flex-1">
        Немає мережі. Кошик збережено, проведення продажу недоступне.
      </Text>
      <TouchableOpacity onPress={() => NetInfo.refresh()}>
        <Text className="text-amber-900 text-xs font-bold ml-3">Перевірити</Text>
      </TouchableOpacity>
    </View>
  );
}
