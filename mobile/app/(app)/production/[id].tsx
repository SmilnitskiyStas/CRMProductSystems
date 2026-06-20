import {
  View,
  Text,
  TouchableOpacity,
  ActivityIndicator,
  Alert,
  ScrollView,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useLocalSearchParams, useRouter } from 'expo-router';
import {
  useProductionOrderById,
  useCompleteProductionOrder,
  useCancelProductionOrder,
} from '@/features/production/hooks/useProduction';
import type { ProductionConsumption, ProductionStatus } from '@/features/production/types';

// ── Status helpers ─────────────────────────────────────────────────────────────

const STATUS_LABELS: Record<ProductionStatus, string> = {
  planned: 'Заплановано',
  in_progress: 'В роботі',
  done: 'Виконано',
  cancelled: 'Скасовано',
};

const STATUS_COLORS: Record<ProductionStatus, { bg: string; text: string }> = {
  planned: { bg: 'bg-blue-100', text: 'text-blue-700' },
  in_progress: { bg: 'bg-amber-100', text: 'text-amber-700' },
  done: { bg: 'bg-green-100', text: 'text-green-700' },
  cancelled: { bg: 'bg-gray-100', text: 'text-gray-500' },
};

function InfoRow({ icon, label, value }: { icon: string; label: string; value: string }) {
  return (
    <View className="flex-row items-start py-2.5 border-b border-gray-50">
      <Ionicons name={icon as never} size={15} color="#9ca3af" style={{ marginTop: 1 }} />
      <Text className="text-sm text-gray-500 w-28 ml-2">{label}</Text>
      <Text className="text-sm font-medium text-gray-800 flex-1">{value}</Text>
    </View>
  );
}

function ConsumptionRow({
  item,
  isLast,
}: {
  item: ProductionConsumption;
  isLast: boolean;
}) {
  return (
    <View className={`flex-row items-center py-3 ${isLast ? '' : 'border-b border-gray-50'}`}>
      <View className="flex-1">
        <Text className="text-sm font-medium text-gray-800">{item.itemName}</Text>
        {item.batchNumber ? (
          <Text className="text-xs text-gray-400 mt-0.5">Партія: {item.batchNumber}</Text>
        ) : null}
      </View>
      <View className="items-end ml-3">
        <Text className="text-sm font-semibold text-gray-700">{item.qtyConsumed}</Text>
        <Text className="text-xs text-gray-400 mt-0.5">
          {new Date(item.consumedAt).toLocaleTimeString('uk-UA', {
            hour: '2-digit',
            minute: '2-digit',
          })}
        </Text>
      </View>
    </View>
  );
}

// ── Main screen ────────────────────────────────────────────────────────────────

export default function ProductionOrderDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();

  const { data: order, isLoading, refetch } = useProductionOrderById(id);
  const { mutateAsync: complete, isPending: completing } = useCompleteProductionOrder();
  const { mutateAsync: cancel, isPending: cancelling } = useCancelProductionOrder();

  function handleComplete() {
    Alert.alert(
      'Завершити ордер',
      'Буде виконано FEFO-списання інгредієнтів і створено партію готового продукту. Продовжити?',
      [
        { text: 'Скасувати', style: 'cancel' },
        {
          text: 'Завершити',
          onPress: () => {
            void complete(id).then((result) => {
              Alert.alert(
                'Виконано!',
                `Вихід: ${result.outputQty} — ${result.outputItemName}\nНова партія: ${result.newStockBatchId.slice(0, 8).toUpperCase()}`,
                [{ text: 'OK', onPress: () => void refetch() }],
              );
            }).catch((err: unknown) => {
              const status = (err as { response?: { status?: number } })?.response?.status;
              if (status === 422) {
                Alert.alert('Недостатньо запасів', 'Не вистачає інгредієнтів для виробництва. Перевірте залишки.');
              } else if (status === 409) {
                Alert.alert('Неможливо завершити', 'Ордер вже завершено або скасовано.');
              } else {
                Alert.alert('Помилка', 'Не вдалося завершити ордер. Спробуйте ще раз.');
              }
            });
          },
        },
      ],
    );
  }

  function handleCancel() {
    Alert.alert(
      'Скасувати ордер',
      'Цю дію не можна скасувати. Продовжити?',
      [
        { text: 'Назад', style: 'cancel' },
        {
          text: 'Скасувати ордер',
          style: 'destructive',
          onPress: () => {
            void cancel(id).then(() => {
              Alert.alert('Скасовано', 'Ордер успішно скасовано.', [
                { text: 'OK', onPress: () => void refetch() },
              ]);
            }).catch(() => {
              Alert.alert('Помилка', 'Не вдалося скасувати ордер.');
            });
          },
        },
      ],
    );
  }

  if (isLoading) {
    return (
      <SafeAreaView className="flex-1 bg-gray-50 items-center justify-center">
        <ActivityIndicator size="large" color="#16a34a" />
      </SafeAreaView>
    );
  }

  if (!order) {
    return (
      <SafeAreaView className="flex-1 bg-gray-50 items-center justify-center">
        <Text className="text-gray-500">Ордер не знайдено</Text>
      </SafeAreaView>
    );
  }

  const sc = STATUS_COLORS[order.status];
  const isActive = order.status === 'planned' || order.status === 'in_progress';

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="flex-row items-center px-4 pt-3 pb-2 gap-3">
        <TouchableOpacity onPress={() => router.back()}>
          <Ionicons name="arrow-back" size={24} color="#111827" />
        </TouchableOpacity>
        <Text className="text-lg font-bold text-gray-900 flex-1" numberOfLines={1}>
          {order.recipeName}
        </Text>
        <View className={`px-2 py-0.5 rounded-full ${sc.bg}`}>
          <Text className={`text-xs font-semibold ${sc.text}`}>
            {STATUS_LABELS[order.status]}
          </Text>
        </View>
      </View>

      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 32 }}>
        {/* Info card */}
        <View className="mx-4 mt-2 bg-white rounded-2xl px-4 py-2">
          <InfoRow icon="location-outline" label="Локація" value={order.locationName} />
          <InfoRow icon="cube-outline" label="Кількість" value={String(order.plannedQty)} />
          <InfoRow
            icon="calendar-outline"
            label="Створено"
            value={new Date(order.createdAt).toLocaleDateString('uk-UA', {
              day: '2-digit',
              month: 'long',
              year: 'numeric',
            })}
          />
          {order.startedAt ? (
            <InfoRow
              icon="play-circle-outline"
              label="Розпочато"
              value={new Date(order.startedAt).toLocaleDateString('uk-UA', {
                day: '2-digit',
                month: 'long',
                year: 'numeric',
              })}
            />
          ) : null}
          {order.completedAt ? (
            <InfoRow
              icon="checkmark-circle-outline"
              label="Завершено"
              value={new Date(order.completedAt).toLocaleDateString('uk-UA', {
                day: '2-digit',
                month: 'long',
                year: 'numeric',
              })}
            />
          ) : null}
          {order.notes ? (
            <InfoRow icon="document-text-outline" label="Примітки" value={order.notes} />
          ) : null}
        </View>

        {/* Actions */}
        {isActive && (
          <View className="mx-4 mt-4 gap-3">
            <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wider">
              Дії
            </Text>
            <TouchableOpacity
              onPress={handleComplete}
              disabled={completing || cancelling}
              className="bg-primary-600 rounded-xl py-3.5 flex-row items-center justify-center gap-2"
            >
              {completing ? (
                <ActivityIndicator color="white" size="small" />
              ) : (
                <>
                  <Ionicons name="checkmark-circle-outline" size={20} color="white" />
                  <Text className="text-white font-semibold text-base">Завершити ордер</Text>
                </>
              )}
            </TouchableOpacity>

            <TouchableOpacity
              onPress={handleCancel}
              disabled={completing || cancelling}
              className="border border-red-300 rounded-xl py-3.5 flex-row items-center justify-center gap-2"
            >
              {cancelling ? (
                <ActivityIndicator color="#ef4444" size="small" />
              ) : (
                <>
                  <Ionicons name="close-circle-outline" size={20} color="#ef4444" />
                  <Text className="text-red-500 font-semibold text-base">Скасувати</Text>
                </>
              )}
            </TouchableOpacity>
          </View>
        )}

        {/* Consumptions */}
        {order.consumptions.length > 0 && (
          <View className="mx-4 mt-4">
            <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">
              Списані інгредієнти
            </Text>
            <View className="bg-white rounded-2xl px-4">
              {order.consumptions.map((c, idx) => (
                <ConsumptionRow
                  key={`${c.itemId}-${c.consumedAt}`}
                  item={c}
                  isLast={idx === order.consumptions.length - 1}
                />
              ))}
            </View>
          </View>
        )}

        {/* Status-specific info for done orders */}
        {order.status === 'done' && order.consumptions.length === 0 && (
          <View className="mx-4 mt-4 bg-green-50 rounded-2xl p-4 flex-row items-center gap-3">
            <Ionicons name="checkmark-circle" size={24} color="#16a34a" />
            <Text className="text-green-700 text-sm font-medium flex-1">
              Ордер успішно виконано. Готовий продукт додано до залишків.
            </Text>
          </View>
        )}

        {order.status === 'cancelled' && (
          <View className="mx-4 mt-4 bg-gray-100 rounded-2xl p-4 flex-row items-center gap-3">
            <Ionicons name="close-circle" size={24} color="#9ca3af" />
            <Text className="text-gray-500 text-sm font-medium flex-1">
              Ордер скасовано. Списань інгредієнтів не відбулось.
            </Text>
          </View>
        )}
      </ScrollView>
    </SafeAreaView>
  );
}
