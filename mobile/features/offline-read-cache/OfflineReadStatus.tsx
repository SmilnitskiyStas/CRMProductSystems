import { Pressable, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors } from '@/components/ui';
import type { OfflineReadUxState } from './ux';

export function OfflineReadStatus({ state, onRetry }: { state: OfflineReadUxState; onRetry?: () => void }) {
  if (state.kind === 'hidden' || !state.message) return null;
  const warning = state.kind === 'offline-cached' || state.kind === 'stale' || state.kind === 'no-data';
  return (
    <View
      accessibilityRole="alert"
      accessibilityLabel={state.message}
      accessibilityLiveRegion="polite"
      className={`mx-4 mb-3 rounded-xl border px-3 py-2.5 flex-row items-center gap-2 ${warning ? 'bg-amber-50 border-amber-200' : 'bg-blue-50 border-blue-200'}`}
    >
      <Ionicons name={warning ? 'cloud-offline-outline' : 'sync-outline'} size={18} color={warning ? colors.status.warning : colors.status.info} />
      <Text className={`text-sm flex-1 ${warning ? 'text-amber-900' : 'text-blue-900'}`}>{state.message}</Text>
      {state.canRetry && onRetry ? (
        <Pressable accessibilityRole="button" accessibilityLabel="Оновити дані" onPress={onRetry} className="min-h-[44px] min-w-[44px] px-2 items-center justify-center">
          <Text className="text-sm font-semibold text-primary-700">Оновити</Text>
        </Pressable>
      ) : null}
    </View>
  );
}
