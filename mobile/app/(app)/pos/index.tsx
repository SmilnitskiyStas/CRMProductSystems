import { View, Text, TouchableOpacity, ActivityIndicator, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { FiscalBadge } from '@/features/pos/components/FiscalBadge';
import {
  useCurrentShift,
  useOpenShift,
  useCloseShift,
} from '@/features/pos/hooks/usePosApi';
import { getStores } from '@/features/pos/api/posApi';

function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString('uk-UA', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export default function PosHomeScreen() {
  const router = useRouter();
  const { data: shift, isLoading, refetch } = useCurrentShift();
  const openMutation = useOpenShift();
  const closeMutation = useCloseShift();

  const handleOpenShift = async () => {
    try {
      const stores = await getStores();
      if (!stores || stores.length === 0) {
        Alert.alert('Помилка', 'Немає доступних магазинів.');
        return;
      }
      if (stores.length === 1) {
        openMutation.mutate(stores[0].id, {
          onSuccess: () => refetch(),
          onError: () => Alert.alert('Помилка', 'Не вдалося відкрити зміну. Спробуйте ще раз.'),
        });
      } else {
        Alert.alert(
          'Оберіть магазин',
          'У вас кілька магазинів. Оберіть один для відкриття зміни.',
          [
            ...stores.map((store) => ({
              text: store.name,
              onPress: () => {
                openMutation.mutate(store.id, {
                  onSuccess: () => refetch(),
                  onError: () => Alert.alert('Помилка', 'Не вдалося відкрити зміну. Спробуйте ще раз.'),
                });
              },
            })),
            { text: 'Скасувати', style: 'cancel' as const },
          ]
        );
      }
    } catch {
      Alert.alert('Помилка', 'Не вдалося отримати список магазинів.');
    }
  };

  const handleCloseShift = () => {
    Alert.alert(
      'Закрити зміну',
      'Ви впевнені, що хочете закрити зміну? Всі наступні продажі будуть неможливі до відкриття нової зміни.',
      [
        { text: 'Скасувати', style: 'cancel' },
        {
          text: 'Закрити зміну',
          style: 'destructive',
          onPress: () => {
            closeMutation.mutate(undefined, {
              onSuccess: () => refetch(),
              onError: () => {
                Alert.alert('Помилка', 'Не вдалося закрити зміну. Спробуйте ще раз.');
              },
            });
          },
        },
      ]
    );
  };

  const isBusy = openMutation.isPending || closeMutation.isPending;

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="flex-row items-center px-5 pt-4 pb-3 bg-white border-b border-gray-100">
        <Ionicons name="receipt-outline" size={24} color="#16a34a" />
        <Text className="text-xl font-bold text-gray-900 ml-3">Каса</Text>
      </View>

      <View className="flex-1 px-5 pt-6">
        {isLoading ? (
          <View className="flex-1 items-center justify-center">
            <ActivityIndicator size="large" color="#16a34a" />
            <Text className="text-gray-500 mt-3 text-base">Завантаження...</Text>
          </View>
        ) : shift && (shift.status === 'Open' || shift.status === 'Opening') ? (
          /* ── Open shift card ── */
          <View className="gap-4">
            <View className="bg-white rounded-2xl p-5 shadow-sm border border-gray-100">
              <View className="flex-row items-center justify-between mb-3">
                <Text className="text-base font-semibold text-gray-700">Поточна зміна</Text>
                <View className="bg-green-100 px-3 py-1 rounded-full">
                  <Text className="text-green-800 text-xs font-bold">ВІДКРИТА</Text>
                </View>
              </View>

              <Text className="text-xs text-gray-400 mb-0.5">Відкрито</Text>
              <Text className="text-sm text-gray-700 font-medium mb-4">
                {shift.openedAt ? formatDateTime(shift.openedAt) : '—'}
              </Text>

              <Text className="text-xs text-gray-400 mb-1">Фіскальний статус</Text>
              <FiscalBadge status={shift.fiscalStatus} />

              <Text className="text-xs text-gray-400 mt-3">ID зміни</Text>
              <Text className="text-xs text-gray-500 font-mono">{shift.shiftId}</Text>
            </View>

            {/* Busy spinner during mutations */}
            {isBusy && (
              <View className="items-center py-2">
                <ActivityIndicator color="#16a34a" />
                <Text className="text-gray-500 text-sm mt-1">Обробка...</Text>
              </View>
            )}

            {!isBusy && (
              <>
                <TouchableOpacity
                  onPress={() =>
                    router.push({
                      pathname: '/(app)/pos/scanner',
                      params: { shiftId: shift.shiftId },
                    })
                  }
                  className="bg-primary-600 rounded-2xl py-5 flex-row items-center justify-center gap-3"
                  style={{ minHeight: 64 }}
                >
                  <Ionicons name="scan-outline" size={26} color="white" />
                  <Text className="text-white text-lg font-bold">Розпочати продаж</Text>
                </TouchableOpacity>

                <TouchableOpacity
                  onPress={handleCloseShift}
                  className="bg-white border border-red-300 rounded-2xl py-4 flex-row items-center justify-center gap-3"
                  style={{ minHeight: 56 }}
                >
                  <Ionicons name="lock-closed-outline" size={22} color="#ef4444" />
                  <Text className="text-red-500 text-base font-semibold">Закрити зміну</Text>
                </TouchableOpacity>
              </>
            )}
          </View>
        ) : (
          /* ── No open shift ── */
          <View className="flex-1 items-center justify-center gap-6">
            <View className="items-center">
              <View className="bg-gray-100 w-24 h-24 rounded-full items-center justify-center mb-4">
                <Ionicons name="lock-closed-outline" size={48} color="#9ca3af" />
              </View>
              <Text className="text-xl font-bold text-gray-900 text-center">
                Зміна не відкрита
              </Text>
              <Text className="text-gray-500 text-sm text-center mt-2">
                Відкрийте зміну, щоб почати приймати оплати
              </Text>
            </View>

            {isBusy ? (
              <View className="items-center gap-2">
                <ActivityIndicator size="large" color="#16a34a" />
                <Text className="text-gray-500 text-sm">
                  Відкриття зміни та фіскалізація...
                </Text>
              </View>
            ) : (
              <TouchableOpacity
                onPress={handleOpenShift}
                className="bg-primary-600 rounded-2xl px-10 py-5 flex-row items-center gap-3"
                style={{ minHeight: 64 }}
              >
                <Ionicons name="lock-open-outline" size={24} color="white" />
                <Text className="text-white text-lg font-bold">Відкрити зміну</Text>
              </TouchableOpacity>
            )}
          </View>
        )}
      </View>
    </SafeAreaView>
  );
}
