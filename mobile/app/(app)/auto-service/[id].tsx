import { useState } from 'react';
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  ActivityIndicator,
  Alert,
  Modal,
  Platform,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { cssInterop } from 'nativewind';
import { CameraView, useCameraPermissions } from 'expo-camera';
import {
  useWorkOrder,
  useCompleteWorkOrder,
} from '@/features/auto-service/hooks/useAutoService';
import { WorkOrderStatusBadge } from '@/features/auto-service/components/WorkOrderStatusBadge';
import { AddLineModal } from '@/features/auto-service/components/AddLineModal';
import { getSparePartByBarcode } from '@/features/auto-service/api';
import { COMPLETABLE_STATUSES } from '@/features/auto-service/types';
import type { SparePartItem } from '@/features/auto-service/types';

cssInterop(CameraView, { className: 'style' });

function Toast({ message, visible }: { message: string; visible: boolean }) {
  if (!visible) return null;
  return (
    <View className="absolute top-16 left-4 right-4 bg-green-600 px-4 py-3 rounded-xl shadow-lg z-50">
      <Text className="text-white font-semibold text-center">{message}</Text>
    </View>
  );
}

export default function WorkOrderDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();

  const { data, isLoading, isError, refetch } = useWorkOrder(id);
  const complete = useCompleteWorkOrder();

  const [showAddLine, setShowAddLine] = useState(false);
  const [prefillPart, setPrefillPart] = useState<SparePartItem | null>(null);
  const [showScanner, setShowScanner] = useState(false);
  const [scanLoading, setScanLoading] = useState(false);
  const [scanned, setScanned] = useState(false);
  const [toastVisible, setToastVisible] = useState(false);
  const [permission, requestPermission] = useCameraPermissions();

  async function handleOpenScanner() {
    if (!permission?.granted) {
      const result = await requestPermission();
      if (!result.granted) {
        Alert.alert('Потрібен доступ до камери', 'Дозвольте доступ у налаштуваннях');
        return;
      }
    }
    setScanned(false);
    setShowScanner(true);
  }

  async function handleBarcodeScan({ data: barcode }: { data: string }) {
    if (scanned || scanLoading) return;
    setScanned(true);
    setScanLoading(true);
    setShowScanner(false);
    try {
      const item = await getSparePartByBarcode(barcode);
      if (item) {
        setPrefillPart(item);
        setShowAddLine(true);
      } else {
        Alert.alert('Не знайдено', 'Запчастину з таким штрихкодом не знайдено');
      }
    } catch {
      Alert.alert('Помилка', 'Не вдалося знайти запчастину за штрихкодом');
    } finally {
      setScanLoading(false);
      setScanned(false);
    }
  }

  function handleComplete() {
    Alert.alert(
      'Завершити наряд?',
      'Запчастини будуть списані зі складу (FEFO). Дію не можна скасувати.',
      [
        { text: 'Скасувати', style: 'cancel' },
        {
          text: 'Завершити',
          style: 'default',
          onPress: () => {
            complete.mutate(id, {
              onSuccess: () => {
                setToastVisible(true);
                setTimeout(() => setToastVisible(false), 2500);
                void refetch();
              },
              onError: (err: unknown) => {
                // Handle 422 — insufficient parts
                const axiosErr = err as { response?: { status: number; data?: { itemName?: string; message?: string } } };
                if (axiosErr?.response?.status === 422) {
                  const itemName =
                    axiosErr.response.data?.itemName ??
                    axiosErr.response.data?.message ??
                    'невідома запчастина';
                  Alert.alert('Недостатньо запчастин', `Недостатньо: ${itemName}`);
                } else {
                  Alert.alert('Помилка', 'Не вдалося завершити наряд');
                }
              },
            });
          },
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
        <Text className="text-red-500 text-center">Не вдалося завантажити наряд</Text>
        <TouchableOpacity onPress={() => router.back()} className="mt-4">
          <Text className="text-primary-600 font-medium">Назад</Text>
        </TouchableOpacity>
      </SafeAreaView>
    );
  }

  const canComplete = COMPLETABLE_STATUSES.includes(data.status);

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <Toast message="Наряд завершено" visible={toastVisible} />

      {/* Header */}
      <View className="bg-white px-4 pt-4 pb-3 flex-row items-center gap-3 border-b border-gray-100">
        <TouchableOpacity
          onPress={() => router.back()}
          className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
        >
          <Ionicons name="arrow-back" size={20} color="#374151" />
        </TouchableOpacity>
        <View className="flex-1">
          <Text className="text-lg font-bold text-gray-900" numberOfLines={1}>
            {data.vehicleBrand} {data.vehicleModel}
          </Text>
          <Text className="text-xs text-gray-500">{data.vehicleLicensePlate}</Text>
        </View>
        <WorkOrderStatusBadge status={data.status} />
      </View>

      <ScrollView contentContainerClassName="p-4 gap-4">
        {/* Vehicle info */}
        <View className="bg-white rounded-xl p-4 gap-2.5">
          <Text className="text-sm font-semibold text-gray-500 uppercase tracking-wide">
            Автомобіль
          </Text>
          <Row label="Марка / Модель" value={`${data.vehicleBrand} ${data.vehicleModel}`} />
          <Row label="Держ. номер" value={data.vehicleLicensePlate} />
          {data.vehicleVin && <Row label="VIN" value={data.vehicleVin} />}
          {data.mechanicName && <Row label="Механік" value={data.mechanicName} />}
          <Row label="Клієнт" value={data.customerName} />
          <Row
            label="Дата"
            value={new Date(data.createdAt).toLocaleString('uk-UA')}
          />
          {data.completedAt && (
            <Row
              label="Завершено"
              value={new Date(data.completedAt).toLocaleString('uk-UA')}
            />
          )}
        </View>

        {/* Lines */}
        <View>
          <View className="flex-row items-center justify-between mb-2 px-1">
            <Text className="text-sm font-semibold text-gray-500 uppercase tracking-wide">
              Позиції ({data.lines.length})
            </Text>
            {canComplete && (
              <TouchableOpacity
                onPress={() => {
                  setPrefillPart(null);
                  setShowAddLine(true);
                }}
                className="flex-row items-center gap-1"
              >
                <Ionicons name="add-circle-outline" size={16} color="#16a34a" />
                <Text className="text-sm text-primary-600 font-medium">Додати</Text>
              </TouchableOpacity>
            )}
          </View>

          <View className="bg-white rounded-xl overflow-hidden">
            {data.lines.length === 0 ? (
              <View className="p-6 items-center">
                <Text className="text-gray-400 text-sm">Позицій немає</Text>
              </View>
            ) : (
              data.lines.map((line, idx) => (
                <View
                  key={line.id}
                  className={`p-4 ${idx > 0 ? 'border-t border-gray-50' : ''}`}
                >
                  <View className="flex-row items-start gap-2">
                    <Text className="text-base mt-0.5">
                      {line.type === 'service' ? '🔧' : '⚙️'}
                    </Text>
                    <View className="flex-1">
                      <Text className="text-sm font-semibold text-gray-900">
                        {line.name}
                      </Text>
                      <View className="flex-row gap-3 mt-1">
                        <Text className="text-xs text-gray-500">
                          {line.qty} × {line.price.toFixed(2)} ₴
                        </Text>
                        {line.discount > 0 && (
                          <Text className="text-xs text-orange-500">
                            -{line.discount}%
                          </Text>
                        )}
                      </View>
                    </View>
                    <Text className="text-sm font-semibold text-gray-700">
                      {line.total.toFixed(2)} ₴
                    </Text>
                  </View>
                </View>
              ))
            )}
          </View>
        </View>

        {/* Total */}
        {data.lines.length > 0 && (
          <View className="bg-white rounded-xl px-4 py-3 flex-row justify-between items-center">
            <Text className="text-base font-semibold text-gray-700">Разом</Text>
            <Text className="text-xl font-bold text-gray-900">
              {data.totalAmount.toFixed(2)} ₴
            </Text>
          </View>
        )}
      </ScrollView>

      {/* Action buttons */}
      {canComplete && (
        <View className="bg-white border-t border-gray-100 px-4 py-3 gap-2">
          <TouchableOpacity
            onPress={handleOpenScanner}
            disabled={scanLoading}
            className="flex-row items-center justify-center gap-2 border border-gray-300 py-3 rounded-xl"
          >
            {scanLoading ? (
              <ActivityIndicator size="small" color="#374151" />
            ) : (
              <>
                <Ionicons name="scan-outline" size={18} color="#374151" />
                <Text className="text-gray-700 font-semibold">Сканувати запчастину</Text>
              </>
            )}
          </TouchableOpacity>

          <TouchableOpacity
            onPress={handleComplete}
            disabled={complete.isPending}
            className="bg-primary-600 py-3.5 rounded-xl items-center"
          >
            {complete.isPending ? (
              <ActivityIndicator color="white" />
            ) : (
              <Text className="text-white font-bold text-base">Завершити наряд</Text>
            )}
          </TouchableOpacity>
        </View>
      )}

      {/* Barcode scanner modal */}
      <Modal visible={showScanner} animationType="slide">
        <View className="flex-1 bg-black">
          <CameraView
            className="flex-1"
            facing="back"
            onBarcodeScanned={scanned ? undefined : handleBarcodeScan}
            barcodeScannerSettings={{
              barcodeTypes: ['ean8', 'ean13', 'qr', 'code128'],
            }}
          >
            <SafeAreaView className="flex-1">
              <TouchableOpacity
                onPress={() => setShowScanner(false)}
                className="m-4 w-10 h-10 bg-black/40 rounded-full items-center justify-center"
              >
                <Ionicons name="close" size={22} color="white" />
              </TouchableOpacity>
              <View className="flex-1 items-center justify-center">
                <View className="w-64 h-64 relative">
                  <View className="absolute top-0 left-0 w-8 h-8 border-t-4 border-l-4 border-primary-500 rounded-tl-lg" />
                  <View className="absolute top-0 right-0 w-8 h-8 border-t-4 border-r-4 border-primary-500 rounded-tr-lg" />
                  <View className="absolute bottom-0 left-0 w-8 h-8 border-b-4 border-l-4 border-primary-500 rounded-bl-lg" />
                  <View className="absolute bottom-0 right-0 w-8 h-8 border-b-4 border-r-4 border-primary-500 rounded-br-lg" />
                </View>
                <Text className="text-white/70 text-sm mt-6">
                  Скануйте штрихкод запчастини
                </Text>
              </View>
            </SafeAreaView>
          </CameraView>
        </View>
      </Modal>

      {/* Add line modal */}
      <AddLineModal
        workOrderId={id}
        visible={showAddLine}
        onClose={() => {
          setShowAddLine(false);
          setPrefillPart(null);
        }}
        prefillItem={prefillPart}
      />
    </SafeAreaView>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <View className="flex-row justify-between">
      <Text className="text-sm text-gray-500">{label}</Text>
      <Text className="text-sm font-medium text-gray-900 flex-shrink ml-4 text-right">
        {value}
      </Text>
    </View>
  );
}
