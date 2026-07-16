import { View, Text, ScrollView, ActivityIndicator, TouchableOpacity, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import {
  useTransfer,
  useConfirmTransfer,
  useCancelTransfer,
} from '@/features/transfers/hooks/useTransfers';
import { STATUS_LABELS, STATUS_COLORS } from '@/features/transfers/types';
import { useAuthStore } from '@/features/auth/store';
import { AT_LEAST_STORE_MANAGER, hasRole } from '@/lib/roles';

export default function TransferDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const isManager = hasRole(user?.role, AT_LEAST_STORE_MANAGER);

  const { data, isLoading, isError } = useTransfer(id);
  const confirm = useConfirmTransfer();
  const cancel = useCancelTransfer();

  function handleConfirm() {
    Alert.alert(
      'Підтвердити отримання?',
      'Товар буде зарахований до вашого магазину.',
      [
        { text: 'Скасувати', style: 'cancel' },
        {
          text: 'Підтвердити',
          onPress: () => confirm.mutate(id, { onSuccess: () => router.back() }),
        },
      ]
    );
  }

  function handleCancel() {
    Alert.alert(
      'Скасувати переміщення?',
      'Залишки будуть повернуті до початкового магазину.',
      [
        { text: 'Ні', style: 'cancel' },
        {
          text: 'Скасувати переміщення',
          style: 'destructive',
          onPress: () => cancel.mutate(id, { onSuccess: () => router.back() }),
        },
      ]
    );
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
        <Text className="text-red-500 text-center">Не вдалося завантажити переміщення</Text>
        <TouchableOpacity onPress={() => router.back()} className="mt-4">
          <Text className="text-primary-600 font-medium">Назад</Text>
        </TouchableOpacity>
      </SafeAreaView>
    );
  }

  const statusStyle = STATUS_COLORS[data.status] ?? 'text-gray-600 bg-gray-100';
  const statusLabel = STATUS_LABELS[data.status] ?? data.status;

  const canConfirm = data.status === 'in_transit' && user?.locationId === data.toLocationId;
  const canCancel = isManager && data.status === 'in_transit';

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="bg-white px-4 pt-4 pb-3 border-b border-gray-100">
        <View className="flex-row items-center gap-3">
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
            <View className="flex-row items-center gap-1.5 mt-0.5">
              <Text className="text-xs text-gray-500">{data.fromLocationName}</Text>
              <Ionicons name="arrow-forward" size={11} color="#9ca3af" />
              <Text className="text-xs text-gray-500">{data.toLocationName}</Text>
            </View>
          </View>
          <View className={`px-3 py-1 rounded-full ${statusStyle}`}>
            <Text className="text-xs font-semibold">{statusLabel}</Text>
          </View>
        </View>
      </View>

      <ScrollView contentContainerClassName="p-4 gap-4">
        {/* Meta */}
        <View className="bg-white rounded-xl p-4 gap-3">
          <Row label="Звідки" value={data.fromLocationName} />
          <Row label="Куди" value={data.toLocationName} />
          <Row
            label="Дата"
            value={new Date(data.createdAt).toLocaleString('uk-UA')}
          />
          {data.notes && <Row label="Примітки" value={data.notes} />}
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
                <View className="flex-row gap-4 mt-1 flex-wrap">
                  <Text className="text-xs text-gray-500">
                    Кількість:{' '}
                    <Text className="font-medium text-gray-700">{item.quantity}</Text>
                  </Text>
                  {item.batchNumber && (
                    <Text className="text-xs text-gray-500">
                      Партія:{' '}
                      <Text className="font-medium text-gray-700">{item.batchNumber}</Text>
                    </Text>
                  )}
                  <Text className="text-xs text-gray-500">
                    Термін:{' '}
                    <Text className="font-medium text-gray-700">
                      {new Date(item.expiryDate).toLocaleDateString('uk-UA')}
                    </Text>
                  </Text>
                </View>
              </View>
            ))}
          </View>
        </View>
      </ScrollView>

      {/* Actions */}
      {(canConfirm || canCancel) && (
        <View className="bg-white border-t border-gray-100 px-4 py-3 flex-row gap-3">
          {canCancel && (
            <TouchableOpacity
              onPress={handleCancel}
              disabled={cancel.isPending}
              className="flex-1 py-3 rounded-xl border border-gray-300 items-center"
            >
              <Text className="text-gray-600 font-semibold">Скасувати</Text>
            </TouchableOpacity>
          )}
          {canConfirm && (
            <TouchableOpacity
              onPress={handleConfirm}
              disabled={confirm.isPending}
              className="flex-1 py-3 rounded-xl bg-primary-600 items-center"
            >
              {confirm.isPending ? (
                <ActivityIndicator size="small" color="white" />
              ) : (
                <Text className="text-white font-semibold">Підтвердити отримання</Text>
              )}
            </TouchableOpacity>
          )}
        </View>
      )}
    </SafeAreaView>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <View className="flex-row justify-between">
      <Text className="text-sm text-gray-500">{label}</Text>
      <Text className="text-sm font-medium text-gray-900 flex-shrink">{value}</Text>
    </View>
  );
}
