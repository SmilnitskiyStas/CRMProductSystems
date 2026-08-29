import { View, Text, ScrollView, ActivityIndicator, TouchableOpacity, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import {
  useWriteOff,
  useApproveWriteOff,
  useRejectWriteOff,
} from '@/features/write-offs/hooks/useWriteOffs';
import {
  STATUS_LABELS,
  STATUS_COLORS,
  WRITE_OFF_REASON_LABELS,
} from '@/features/write-offs/types';
import { useAuthStore } from '@/features/auth/store';
import { AT_LEAST_STORE_MANAGER, hasRole } from '@/lib/roles';
import { money } from '@/features/write-offs/calculations';

export default function WriteOffDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const isManager = hasRole(user?.role, AT_LEAST_STORE_MANAGER);

  const { data, isLoading, isError } = useWriteOff(id);
  const approve = useApproveWriteOff();
  const reject = useRejectWriteOff();

  function handleApprove() {
    Alert.alert('Затвердити списання?', 'Залишки будуть зменшені.', [
      { text: 'Скасувати', style: 'cancel' },
      {
        text: 'Затвердити',
        style: 'default',
        onPress: () => {
          approve.mutate(id, {
            onSuccess: () => router.back(),
            onError: (err: unknown) => {
              const axiosErr = err as { response?: { data?: { error?: string } } };
              Alert.alert(
                'Не вдалося затвердити',
                axiosErr?.response?.data?.error ?? 'Перевірте залишки і спробуйте ще раз.'
              );
            },
          });
        },
      },
    ]);
  }

  function handleReject() {
    Alert.alert('Відхилити списання?', undefined, [
      { text: 'Скасувати', style: 'cancel' },
      {
        text: 'Відхилити',
        style: 'destructive',
        onPress: () => {
          reject.mutate(id, {
            onSuccess: () => router.back(),
            onError: () => {
              Alert.alert('Помилка', 'Не вдалося відхилити списання. Спробуйте ще раз.');
            },
          });
        },
      },
    ]);
  }

  if (isLoading) {
    return (
      <SafeAreaView className="flex-1 bg-white items-center justify-center">
        <ActivityIndicator size="large" color="#16a34a" />
      </SafeAreaView>
    );
  }

  if (isError || !data) {
    return (
      <SafeAreaView className="flex-1 bg-white items-center justify-center px-4">
        <Text className="text-red-500 text-center">Не вдалося завантажити списання</Text>
        <TouchableOpacity onPress={() => router.back()} className="mt-4">
          <Text className="text-primary-600 font-medium">Назад</Text>
        </TouchableOpacity>
      </SafeAreaView>
    );
  }

  const statusStyle = STATUS_COLORS[data.status] ?? 'text-gray-600 bg-gray-100';
  const statusLabel = STATUS_LABELS[data.status] ?? data.status;
  const canAct = isManager && data.status === 'pending_approval';

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="bg-white px-4 pt-4 pb-3 flex-row items-center gap-3 border-b border-gray-100">
        <TouchableOpacity
          onPress={() => router.back()}
          className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
        >
          <Ionicons name="arrow-back" size={20} color="#374151" />
        </TouchableOpacity>
        <View className="flex-1">
          <Text className="text-lg font-bold text-gray-900">
            #{data.id.slice(0, 8).toUpperCase()}
          </Text>
          <Text className="text-xs text-gray-500">{data.locationName}</Text>
        </View>
        <View className={`px-3 py-1 rounded-full ${statusStyle}`}>
          <Text className="text-xs font-semibold">{statusLabel}</Text>
        </View>
      </View>

      <ScrollView contentContainerClassName="p-4 gap-4">
        {/* Meta */}
        <View className="bg-white rounded-xl p-4 gap-3">
          <Row
            label="Причина"
            value={data.reason ? WRITE_OFF_REASON_LABELS[data.reason] : '—'}
          />
          <Row
            label="Дата"
            value={new Date(data.createdAt).toLocaleString('uk-UA')}
          />
          {data.approvedAt && (
            <Row
              label="Затверджено"
              value={new Date(data.approvedAt).toLocaleString('uk-UA')}
            />
          )}
          {data.totalLossAmount != null && data.totalLossAmount > 0 && (
            <Row
              label="Сума збитку"
              value={`${data.totalLossAmount.toFixed(2)} ₴`}
              valueClass="text-red-600 font-semibold"
            />
          )}
          <Row label="Збиток за закупівлею" value={money(data.totalLossAmountPurchase)} valueClass="text-red-600 font-semibold" />
          <Row label="Відшкодування" value={money(data.totalReimbursementAmount)} valueClass="text-green-700 font-semibold" />
          <View className="border-t border-gray-100 pt-3">
            <Row label="Чистий збиток" value={money(data.netLossAmount)} valueClass="text-red-700 font-bold" />
          </View>
        </View>

        {/* Items */}
        <View>
          <Text className="text-sm font-semibold text-gray-500 uppercase tracking-wide mb-2 px-1">
            Позиції ({data.items.length})
          </Text>
          <View className="bg-white rounded-xl overflow-hidden">
            {data.items.map((item, idx) => (
              <View
                key={item.id}
                className={`p-4 ${idx > 0 ? 'border-t border-gray-50' : ''}`}
              >
                <Text className="text-sm font-semibold text-gray-900">
                  {item.productName}
                </Text>
                <View className="flex-row gap-4 mt-1">
                  <Text className="text-xs text-gray-500">
                    Кількість: <Text className="font-medium text-gray-700">{item.quantity}</Text>
                  </Text>
                  {item.batchNumber && (
                    <Text className="text-xs text-gray-500">
                      Партія: <Text className="font-medium text-gray-700">{item.batchNumber}</Text>
                    </Text>
                  )}
                  {item.expiryDate && (
                    <Text className="text-xs text-gray-500">
                      Термін: <Text className="font-medium text-gray-700">
                        {new Date(item.expiryDate).toLocaleDateString('uk-UA')}
                      </Text>
                    </Text>
                  )}
                </View>
                {item.lossAmount != null && (
                  <Text className="text-xs text-red-500 mt-0.5">
                    −{item.lossAmount.toFixed(2)} ₴
                  </Text>
                )}
                <View className="mt-2 gap-1">
                  <Text className="text-xs text-gray-500">
                    Ціна продажу: <Text className="font-medium text-gray-700">{money(item.unitPrice)}</Text>
                  </Text>
                  <Text className="text-xs text-gray-500">
                    Закупівельна ціна: <Text className="font-medium text-gray-700">{money(item.unitPricePurchase)}</Text>
                  </Text>
                  <Text className="text-xs text-red-600">Збиток за закупівлею: {money(item.lossAmountPurchase)}</Text>
                  {item.isReturnedToSupplier && (
                    <View className="bg-green-50 rounded-lg p-2 mt-1">
                      <Text className="text-xs font-semibold text-green-800">Повернуто постачальнику</Text>
                      <Text className="text-xs text-green-700 mt-0.5">
                        Умова: {item.reimbursementType === 'percent' ? `${item.reimbursementValue ?? 0}%` : `${money(item.reimbursementValue)} за одиницю`}
                      </Text>
                      <Text className="text-xs font-semibold text-green-800">Відшкодовано: {money(item.reimbursementAmount)}</Text>
                    </View>
                  )}
                </View>
              </View>
            ))}
          </View>
        </View>
      </ScrollView>

      {/* Manager actions */}
      {canAct && (
        <View className="bg-white border-t border-gray-100 px-4 py-3 flex-row gap-3">
          <TouchableOpacity
            onPress={handleReject}
            disabled={reject.isPending}
            className="flex-1 py-3 rounded-xl border border-red-300 items-center"
          >
            <Text className="text-red-600 font-semibold">Відхилити</Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={handleApprove}
            disabled={approve.isPending}
            className="flex-1 py-3 rounded-xl bg-primary-600 items-center"
          >
            {approve.isPending ? (
              <ActivityIndicator size="small" color="white" />
            ) : (
              <Text className="text-white font-semibold">Затвердити</Text>
            )}
          </TouchableOpacity>
        </View>
      )}
    </SafeAreaView>
  );
}

function Row({
  label,
  value,
  valueClass = 'text-gray-900',
}: {
  label: string;
  value: string;
  valueClass?: string;
}) {
  return (
    <View className="flex-row justify-between">
      <Text className="text-sm text-gray-500">{label}</Text>
      <Text className={`text-sm font-medium ${valueClass}`}>{value}</Text>
    </View>
  );
}
