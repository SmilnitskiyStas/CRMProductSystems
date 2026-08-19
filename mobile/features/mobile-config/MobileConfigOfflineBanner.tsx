import { Ionicons } from '@expo/vector-icons';
import { useNetInfo } from '@react-native-community/netinfo';
import { Text, View } from 'react-native';
import { useMobileConfig } from './MobileConfigProvider';
import { deriveMobileConfigOfflineUx } from './offlineUx';

export function MobileConfigOfflineBanner() {
  const network = useNetInfo();
  const { source, cachedAt, status } = useMobileConfig();
  const online = network.isConnected !== false && network.isInternetReachable !== false;
  const state = deriveMobileConfigOfflineUx({ online, source, cachedAt, loading: status === 'loading' });
  if (!state.visible || !state.message) return null;
  return (
    <View accessibilityRole="alert" className="absolute bottom-16 left-3 right-3 flex-row items-center rounded-xl border border-amber-200 bg-amber-50 px-3 py-2">
      <Ionicons name="cloud-offline-outline" size={18} color="#b45309" />
      <Text className="ml-2 flex-1 text-xs text-amber-900">{state.message}</Text>
    </View>
  );
}
