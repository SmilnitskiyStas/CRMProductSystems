import { useState } from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  FlatList,
  Modal,
  ActivityIndicator,
  Alert,
  KeyboardAvoidingView,
  Platform,
  TextInput,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { cssInterop } from 'nativewind';
import { useCreateTransfer, useStores } from '@/features/transfers/hooks/useTransfers';
import type { DraftTransferItem, StoreOption } from '@/features/transfers/types';
import { getProductByBarcode } from '@/features/stock/api/stockApi';
import { getStock } from '@/features/stock/api/stockApi';
import { useAuthStore } from '@/features/auth/store';

cssInterop(CameraView, { className: 'style' });

type Step = 'items' | 'destination';

export default function CreateTransferScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const createTransfer = useCreateTransfer();
  const { data: stores } = useStores();

  const [step, setStep] = useState<Step>('items');
  const [items, setItems] = useState<DraftTransferItem[]>([]);
  const [toStoreId, setToStoreId] = useState<string | null>(null);
  const [notes, setNotes] = useState('');

  // Scanner state
  const [scanOpen, setScanOpen] = useState(false);
  const [scanned, setScanned] = useState(false);
  const [scanLoading, setScanLoading] = useState(false);
  const [permission, requestPermission] = useCameraPermissions();

  // Batch picker state (shown after scan)
  const [batchPickerVisible, setBatchPickerVisible] = useState(false);
  const [availableBatches, setAvailableBatches] = useState<
    { id: string; batchNumber: string | null; expiryDate: string; quantity: number; productId: string; productName: string }[]
  >([]);

  // Store picker state
  const [storePickerVisible, setStorePickerVisible] = useState(false);

  const destinationStores = (stores ?? []).filter((s) => s.id !== user?.storeId);
  const selectedStore = destinationStores.find((s) => s.id === toStoreId);

  function openScanner() {
    if (!permission?.granted) {
      void requestPermission().then((p) => { if (p.granted) { setScanned(false); setScanOpen(true); } });
      return;
    }
    setScanned(false);
    setScanOpen(true);
  }

  async function handleBarcodeScanned({ data }: { data: string }) {
    if (scanned || scanLoading) return;
    setScanned(true);
    setScanLoading(true);
    try {
      const product = await getProductByBarcode(data);
      const allStock = await getStock({ storeId: user?.storeId ?? undefined });
      const batches = allStock
        .filter((s) => s.productId === product.id && s.quantity > 0)
        .map((s) => ({
          id: s.id,
          batchNumber: s.batchNumber,
          expiryDate: s.expiryDate,
          quantity: s.quantity,
          productId: product.id,
          productName: product.name,
        }));

      setScanOpen(false);

      if (batches.length === 0) {
        Alert.alert('Немає залишків', `"${product.name}" відсутній на складі цього магазину.`);
        setScanned(false);
        return;
      }

      setAvailableBatches(batches);
      setBatchPickerVisible(true);
    } catch {
      Alert.alert('Помилка', 'Товар не знайдено або сталася помилка.');
      setScanned(false);
    } finally {
      setScanLoading(false);
    }
  }

  function selectBatch(batch: typeof availableBatches[number]) {
    setBatchPickerVisible(false);
    setAvailableBatches([]);
    const existing = items.findIndex((i) => i.productStockId === batch.id);
    if (existing >= 0) {
      Alert.alert('Партія вже додана', 'Змініть кількість у списку.');
      return;
    }
    setItems((prev) => [
      ...prev,
      {
        productStockId: batch.id,
        productId: batch.productId,
        productName: batch.productName,
        batchNumber: batch.batchNumber,
        expiryDate: batch.expiryDate,
        quantity: 1,
        availableQty: batch.quantity,
      },
    ]);
  }

  function changeQty(productStockId: string, delta: number) {
    setItems((prev) =>
      prev
        .map((i) => {
          if (i.productStockId !== productStockId) return i;
          const next = Math.max(0, Math.min(i.availableQty, i.quantity + delta));
          return { ...i, quantity: next };
        })
        .filter((i) => i.quantity > 0)
    );
  }

  function handleSubmit() {
    if (!user?.storeId) {
      Alert.alert('Помилка', 'Магазин не призначено для вашого профілю.');
      return;
    }
    if (!toStoreId) {
      Alert.alert('Оберіть магазин призначення');
      return;
    }
    if (items.length === 0) {
      Alert.alert('Додайте хоча б один товар');
      return;
    }

    createTransfer.mutate(
      {
        fromStoreId: user.storeId,
        toStoreId,
        transferType: 'store_to_store',
        notes: notes.trim() || undefined,
        items: items.map((i) => ({ productStockId: i.productStockId, quantity: i.quantity })),
      },
      {
        onSuccess: () => {
          Alert.alert('Переміщення створено', 'Товар відправлено в дорогу.', [
            { text: 'OK', onPress: () => router.back() },
          ]);
        },
        onError: () => {
          Alert.alert('Помилка', 'Не вдалося створити переміщення. Спробуйте ще раз.');
        },
      }
    );
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <KeyboardAvoidingView
        className="flex-1"
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        {/* Header */}
        <View className="bg-white px-4 pt-4 pb-3 flex-row items-center gap-3 border-b border-gray-100">
          <TouchableOpacity
            onPress={() => router.back()}
            className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="close" size={20} color="#374151" />
          </TouchableOpacity>
          <Text className="text-lg font-bold text-gray-900 flex-1">Нове переміщення</Text>
        </View>

        {/* Destination store selector */}
        <TouchableOpacity
          onPress={() => setStorePickerVisible(true)}
          className="mx-4 mt-4 bg-white rounded-xl px-4 py-3.5 flex-row items-center justify-between border border-gray-200"
        >
          <View>
            <Text className="text-xs text-gray-400 mb-0.5">Магазин призначення</Text>
            <Text className={`text-sm font-semibold ${toStoreId ? 'text-gray-900' : 'text-gray-400'}`}>
              {selectedStore?.name ?? 'Оберіть магазин…'}
            </Text>
          </View>
          <Ionicons name="chevron-down" size={18} color="#9ca3af" />
        </TouchableOpacity>

        {/* Notes */}
        <View className="mx-4 mt-3 bg-white rounded-xl px-4 py-3 border border-gray-200">
          <Text className="text-xs text-gray-400 mb-1">Примітки (необов'язково)</Text>
          <TextInput
            value={notes}
            onChangeText={setNotes}
            placeholder="Причина переміщення…"
            placeholderTextColor="#9ca3af"
            className="text-sm text-gray-900"
            multiline
            maxLength={200}
          />
        </View>

        {/* Items list */}
        <FlatList
          data={items}
          keyExtractor={(item) => item.productStockId}
          contentContainerClassName="px-4 pt-3 pb-4 gap-2"
          ListHeaderComponent={
            items.length > 0 ? (
              <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-1">
                Товари ({items.length})
              </Text>
            ) : null
          }
          renderItem={({ item }) => (
            <View className="bg-white rounded-xl px-4 py-3">
              <View className="flex-row items-center">
                <View className="flex-1 mr-3">
                  <Text className="text-sm font-semibold text-gray-900" numberOfLines={2}>
                    {item.productName}
                  </Text>
                  <View className="flex-row gap-3 mt-0.5 flex-wrap">
                    {item.batchNumber && (
                      <Text className="text-xs text-gray-400">Партія: {item.batchNumber}</Text>
                    )}
                    <Text className="text-xs text-gray-400">
                      Доступно: {item.availableQty}
                    </Text>
                  </View>
                </View>
                <View className="flex-row items-center gap-2">
                  <TouchableOpacity
                    onPress={() => changeQty(item.productStockId, -1)}
                    className="w-8 h-8 rounded-full bg-gray-100 items-center justify-center"
                  >
                    <Ionicons name="remove" size={16} color="#374151" />
                  </TouchableOpacity>
                  <Text className="text-base font-bold text-gray-900 w-6 text-center">
                    {item.quantity}
                  </Text>
                  <TouchableOpacity
                    onPress={() => changeQty(item.productStockId, 1)}
                    disabled={item.quantity >= item.availableQty}
                    className={`w-8 h-8 rounded-full items-center justify-center ${
                      item.quantity >= item.availableQty ? 'bg-gray-50' : 'bg-gray-100'
                    }`}
                  >
                    <Ionicons
                      name="add"
                      size={16}
                      color={item.quantity >= item.availableQty ? '#d1d5db' : '#374151'}
                    />
                  </TouchableOpacity>
                </View>
              </View>
            </View>
          )}
          ListEmptyComponent={
            <View className="items-center justify-center py-12">
              <Ionicons name="scan-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 mt-3 text-center">
                Відскануйте товар для додавання
              </Text>
            </View>
          }
        />

        {/* Bottom actions */}
        <View className="bg-white border-t border-gray-100 px-4 py-3 gap-3">
          <TouchableOpacity
            onPress={openScanner}
            className="flex-row items-center justify-center gap-2 border border-primary-600 rounded-xl py-3"
          >
            <Ionicons name="scan-outline" size={20} color="#16a34a" />
            <Text className="text-primary-600 font-semibold">Сканувати товар</Text>
          </TouchableOpacity>

          <TouchableOpacity
            onPress={handleSubmit}
            disabled={createTransfer.isPending || items.length === 0 || !toStoreId}
            className={`rounded-xl py-3.5 items-center ${
              items.length === 0 || !toStoreId ? 'bg-gray-200' : 'bg-primary-600'
            }`}
          >
            {createTransfer.isPending ? (
              <ActivityIndicator size="small" color="white" />
            ) : (
              <Text
                className={`font-bold text-base ${
                  items.length === 0 || !toStoreId ? 'text-gray-400' : 'text-white'
                }`}
              >
                Відправити
              </Text>
            )}
          </TouchableOpacity>
        </View>
      </KeyboardAvoidingView>

      {/* Camera scanner modal */}
      <Modal visible={scanOpen} animationType="slide" onRequestClose={() => setScanOpen(false)}>
        <View className="flex-1 bg-black">
          <CameraView
            className="flex-1"
            facing="back"
            onBarcodeScanned={scanned ? undefined : handleBarcodeScanned}
            barcodeScannerSettings={{ barcodeTypes: ['ean8', 'ean13', 'qr', 'code128'] }}
          >
            <SafeAreaView className="flex-1">
              <TouchableOpacity
                onPress={() => setScanOpen(false)}
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
                {scanLoading ? (
                  <View className="mt-6 flex-row items-center gap-2">
                    <ActivityIndicator color="white" />
                    <Text className="text-white/80 text-sm">Завантаження партій…</Text>
                  </View>
                ) : (
                  <Text className="text-white/70 text-sm mt-6">Наведіть на штрихкод</Text>
                )}
              </View>
            </SafeAreaView>
          </CameraView>
        </View>
      </Modal>

      {/* Batch picker modal */}
      <Modal visible={batchPickerVisible} transparent animationType="slide">
        <TouchableOpacity
          activeOpacity={1}
          onPress={() => { setBatchPickerVisible(false); setScanned(false); }}
          className="flex-1 justify-end bg-black/50"
        >
          <View className="bg-white rounded-t-3xl p-6" onStartShouldSetResponder={() => true}>
            <Text className="text-lg font-bold text-gray-900 mb-1">
              {availableBatches[0]?.productName ?? 'Виберіть партію'}
            </Text>
            <Text className="text-sm text-gray-500 mb-4">Доступні партії у вашому магазині</Text>
            {availableBatches.map((batch) => (
              <TouchableOpacity
                key={batch.id}
                onPress={() => selectBatch(batch)}
                className="py-3.5 border-b border-gray-50"
              >
                <View className="flex-row items-center justify-between">
                  <View>
                    {batch.batchNumber && (
                      <Text className="text-sm font-semibold text-gray-900">
                        Партія: {batch.batchNumber}
                      </Text>
                    )}
                    <Text className="text-xs text-gray-500 mt-0.5">
                      Термін: {new Date(batch.expiryDate).toLocaleDateString('uk-UA')}
                    </Text>
                  </View>
                  <View className="bg-green-50 px-3 py-1 rounded-full">
                    <Text className="text-sm font-semibold text-green-700">
                      {batch.quantity} шт
                    </Text>
                  </View>
                </View>
              </TouchableOpacity>
            ))}
          </View>
        </TouchableOpacity>
      </Modal>

      {/* Store picker modal */}
      <Modal visible={storePickerVisible} transparent animationType="slide">
        <TouchableOpacity
          activeOpacity={1}
          onPress={() => setStorePickerVisible(false)}
          className="flex-1 justify-end bg-black/50"
        >
          <View className="bg-white rounded-t-3xl p-6" onStartShouldSetResponder={() => true}>
            <Text className="text-lg font-bold text-gray-900 mb-4">Магазин призначення</Text>
            {destinationStores.length === 0 ? (
              <Text className="text-gray-400 text-center py-4">Немає доступних магазинів</Text>
            ) : (
              destinationStores.map((store) => (
                <TouchableOpacity
                  key={store.id}
                  onPress={() => { setToStoreId(store.id); setStorePickerVisible(false); }}
                  className="flex-row items-center justify-between py-3.5 border-b border-gray-50"
                >
                  <Text className={`text-base ${store.id === toStoreId ? 'font-semibold text-primary-700' : 'text-gray-700'}`}>
                    {store.name}
                  </Text>
                  {store.id === toStoreId && (
                    <Ionicons name="checkmark" size={20} color="#15803d" />
                  )}
                </TouchableOpacity>
              ))
            )}
          </View>
        </TouchableOpacity>
      </Modal>
    </SafeAreaView>
  );
}
