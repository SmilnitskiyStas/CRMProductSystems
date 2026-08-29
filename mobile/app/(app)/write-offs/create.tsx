import { useEffect, useState } from 'react';
import NetInfo, { useNetInfo } from '@react-native-community/netinfo';
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
  Switch,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { cssInterop } from 'nativewind';
import { WRITE_OFF_REASON_LABELS } from '@/features/write-offs/types';
import type { ReimbursementType, WriteOffReason } from '@/features/write-offs/types';
import { calculateReimbursement, money } from '@/features/write-offs/calculations';
import { getProductByBarcode } from '@/features/stock/api/stockApi';
import { useAuthStore } from '@/features/auth/store';
import { useOperationalDraft } from '@/features/operational-drafts/useOperationalDraft';
import { useWorkspaceLocationStore } from '@/features/locations/store';
import { enqueueWriteOff, syncQueuedWriteOffs } from '@/features/offline-mutations/writeOffQueue';
import { queryClient } from '@/lib/query-client';

cssInterop(CameraView, { className: 'style' });

const REASONS: WriteOffReason[] = ['expired', 'damaged', 'theft', 'production_loss', 'other'];

interface DraftItem {
  productId: string;
  productName: string;
  quantity: number;
  unitPrice: number | null;
  unitPriceInput: string;
  unitPricePurchase: number | null;
  isReturnedToSupplier: boolean;
  reimbursementType: ReimbursementType | null;
  reimbursementValue: number | null;
  reimbursementValueInput: string;
}

function parseAmount(value: string): number | null {
  if (value.trim() === '') return null;
  const parsed = Number(value.replace(',', '.'));
  return Number.isFinite(parsed) ? parsed : null;
}

export default function CreateWriteOffScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const selectedLocationId = useWorkspaceLocationStore((s) => s.selectedLocationId);
  const locationId = selectedLocationId === undefined ? user?.locationId : selectedLocationId;
  const netInfo = useNetInfo();
  const owner = user?.tenantId ? { tenantId: user.tenantId, userId: user.id } : null;
  const draft = useOperationalDraft(owner, 'write-off');

  const [items, setItems] = useState<DraftItem[]>([]);
  const [reason, setReason] = useState<WriteOffReason>('expired');
  const [scanOpen, setScanOpen] = useState(false);
  const [scanLoading, setScanLoading] = useState(false);
  const [scanned, setScanned] = useState(false);
  const [reasonOpen, setReasonOpen] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const [permission, requestPermission] = useCameraPermissions();
  const draftPayload = {
    kind: 'write-off' as const,
    locationId: locationId ?? '',
    reason,
    notes: '',
    items: items.map(({ unitPriceInput: _unitPriceInput, reimbursementValueInput: _reimbursementValueInput, ...item }) => item),
  };

  useEffect(() => {
    if (draft.restored?.payload.kind !== 'write-off') return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setReason(draft.restored.payload.reason as WriteOffReason);
    setItems(draft.restored.payload.items.map((item) => ({
      productId: item.productId,
      productName: item.productName,
      quantity: item.quantity,
      unitPrice: item.unitPrice ?? null,
      unitPriceInput: item.unitPrice == null ? '' : String(item.unitPrice),
      unitPricePurchase: item.unitPricePurchase ?? null,
      isReturnedToSupplier: item.isReturnedToSupplier ?? false,
      reimbursementType: item.reimbursementType ?? null,
      reimbursementValue: item.reimbursementValue ?? null,
      reimbursementValueInput: item.reimbursementValue == null ? '' : String(item.reimbursementValue),
    })));
  }, [draft.restored]);

  useEffect(() => {
    if (!draft.hydrated || items.length === 0) return;
    void draft.persist(draftPayload);
  }, [items, reason, draft.hydrated]); // eslint-disable-line react-hooks/exhaustive-deps

  function openScanner() {
    if (!permission?.granted) {
      void requestPermission().then((p) => { if (p.granted) setScanOpen(true); });
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
      setScanOpen(false);
      setItems((prev) => {
        const existing = prev.findIndex((i) => i.productId === product.id);
        if (existing >= 0) {
          const next = [...prev];
          next[existing] = { ...next[existing], quantity: next[existing].quantity + 1 };
          return next;
        }
        return [...prev, {
          productId: product.id,
          productName: product.name,
          quantity: 1,
          unitPrice: product.priceRetail,
          unitPriceInput: product.priceRetail == null ? '' : String(product.priceRetail),
          unitPricePurchase: product.pricePurchase,
          isReturnedToSupplier: false,
          reimbursementType: product.defaultReimbursementType,
          reimbursementValue: product.defaultReimbursementValue,
          reimbursementValueInput: product.defaultReimbursementValue == null ? '' : String(product.defaultReimbursementValue),
        }];
      });
    } catch {
      Alert.alert('Товар не знайдено', 'Перевірте штрихкод і спробуйте знову.');
      setScanned(false);
    } finally {
      setScanLoading(false);
    }
  }

  function updateItem(productId: string, changes: Partial<DraftItem>) {
    setItems((prev) => prev.map((item) => item.productId === productId ? { ...item, ...changes } : item));
  }

  function changeQty(productId: string, delta: number) {
    setItems((prev) =>
      prev
        .map((i) => i.productId === productId ? { ...i, quantity: i.quantity + delta } : i)
        .filter((i) => i.quantity > 0)
    );
  }

  async function handleSubmit() {
    if (!locationId) {
      Alert.alert('Оберіть магазин', 'Для створення списання потрібно вибрати конкретний магазин, а не «Усі магазини».');
      return;
    }
    if (items.length === 0) {
      Alert.alert('Додайте хоча б один товар');
      return;
    }
    const invalid = items.find((item) => item.unitPrice == null || !Number.isFinite(item.unitPrice) || item.unitPrice < 0
      || (item.isReturnedToSupplier && (
        !item.reimbursementType || item.reimbursementValue == null || !Number.isFinite(item.reimbursementValue) || item.reimbursementValue <= 0
        || (item.reimbursementType === 'percent' && item.reimbursementValue > 100)
      )));
    if (invalid) {
      Alert.alert('Перевірте дані', `Заповніть коректну ціну та відшкодування для «${invalid.productName}».`);
      return;
    }
    if (!owner || isSubmitting) return;
    setIsSubmitting(true);
    try {
      const payload = {
        locationId,
        reason,
        items: items.map((i) => ({
          productId: i.productId,
          quantity: i.quantity,
          unitPrice: i.unitPrice ?? undefined,
          isReturnedToSupplier: i.isReturnedToSupplier,
          ...(i.isReturnedToSupplier ? {
            reimbursementType: i.reimbursementType ?? undefined,
            reimbursementValue: i.reimbursementValue ?? undefined,
          } : {}),
        })),
      };
      await enqueueWriteOff(owner, payload);
      await draft.clear();
      const network = await NetInfo.fetch();
      if (!network.isConnected || network.isInternetReachable === false) {
        Alert.alert('Збережено офлайн', 'Списання збережено на телефоні та буде передано автоматично після появи інтернету.', [
          { text: 'OK', onPress: () => router.back() },
        ]);
        return;
      }
      const result = await syncQueuedWriteOffs(owner);
      if (result.synced > 0) {
        await queryClient.invalidateQueries({ queryKey: ['write-offs'] });
        Alert.alert('Успішно', 'Списання синхронізовано і відправлено на затвердження.', [
          { text: 'OK', onPress: () => router.back() },
        ]);
        return;
      }
      Alert.alert(
        result.uncertain > 0 ? 'Очікує перевірки' : 'Збережено локально',
        result.uncertain > 0
          ? 'Сервер не підтвердив результат. Автоматичний повтор зупинено, щоб не створити дублікат.'
          : 'Операція залишилась у локальній черзі та буде синхронізована пізніше.',
        [
        { text: 'OK', onPress: () => router.back() },
        ],
      );
    } catch {
      Alert.alert('Помилка локального збереження', 'Не вдалося надійно зберегти операцію. Дані форми залишено.');
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {(netInfo.isConnected === false || draft.submission.message) && (
        <Text className="bg-amber-50 text-amber-800 px-4 py-2 text-sm">
          {netInfo.isConnected === false ? 'Немає мережі. Чернетка збережена.' : draft.submission.message}
        </Text>
      )}
      <KeyboardAvoidingView
        className="flex-1"
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        {/* Header */}
        <View className="bg-white px-4 pt-4 pb-3 flex-row items-center gap-3 border-b border-gray-100">
          <TouchableOpacity
            onPress={() => items.length === 0
              ? router.back()
              : Alert.alert('Зберегти чернетку?', 'Можна повернутися пізніше або видалити введені дані.', [
                { text: 'Зберегти', onPress: () => router.back() },
                { text: 'Видалити', style: 'destructive', onPress: () => void draft.clear().then(() => router.back()) },
                { text: 'Скасувати', style: 'cancel' },
              ])}
            className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="close" size={20} color="#374151" />
          </TouchableOpacity>
          <Text className="text-lg font-bold text-gray-900 flex-1">Нове списання</Text>
        </View>

        {/* Reason picker */}
        <TouchableOpacity
          onPress={() => setReasonOpen(true)}
          className="mx-4 mt-4 bg-white rounded-xl px-4 py-3.5 flex-row items-center justify-between border border-gray-200"
        >
          <View>
            <Text className="text-xs text-gray-400 mb-0.5">Причина</Text>
            <Text className="text-sm font-semibold text-gray-900">
              {WRITE_OFF_REASON_LABELS[reason]}
            </Text>
          </View>
          <Ionicons name="chevron-down" size={18} color="#9ca3af" />
        </TouchableOpacity>

        {/* Items list */}
        <FlatList
          data={items}
          keyExtractor={(item) => item.productId}
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
              </View>
              <View className="flex-row items-center gap-2">
                <TouchableOpacity
                  onPress={() => changeQty(item.productId, -1)}
                  className="w-8 h-8 rounded-full bg-gray-100 items-center justify-center"
                >
                  <Ionicons name="remove" size={16} color="#374151" />
                </TouchableOpacity>
                <Text className="text-base font-bold text-gray-900 w-6 text-center">
                  {item.quantity}
                </Text>
                <TouchableOpacity
                  onPress={() => changeQty(item.productId, 1)}
                  className="w-8 h-8 rounded-full bg-gray-100 items-center justify-center"
                >
                  <Ionicons name="add" size={16} color="#374151" />
                </TouchableOpacity>
              </View>
              </View>
              <View className="mt-3 pt-3 border-t border-gray-100 gap-2">
                <View className="flex-row gap-3">
                  <View className="flex-1">
                    <Text className="text-xs text-gray-500 mb-1">Ціна продажу, ₴</Text>
                    <TextInput
                      value={item.unitPriceInput}
                      onChangeText={(value) => updateItem(item.productId, { unitPriceInput: value, unitPrice: parseAmount(value) })}
                      keyboardType="decimal-pad"
                      placeholder="0.00"
                      className="border border-gray-200 rounded-lg px-3 py-2 text-gray-900"
                    />
                  </View>
                  <View className="flex-1">
                    <Text className="text-xs text-gray-500 mb-1">Закупівельна ціна</Text>
                    <View className="bg-gray-100 rounded-lg px-3 py-2.5"><Text>{money(item.unitPricePurchase)}</Text></View>
                  </View>
                </View>
                <View className="flex-row justify-between">
                  <Text className="text-xs text-gray-500">Збиток за продажем: {money((item.unitPrice ?? 0) * item.quantity)}</Text>
                  <Text className="text-xs text-gray-500">За закупівлею: {money((item.unitPricePurchase ?? 0) * item.quantity)}</Text>
                </View>
                <View className="flex-row items-center justify-between mt-1">
                  <Text className="text-sm font-medium text-gray-800">Повернуто постачальнику</Text>
                  <Switch
                    value={item.isReturnedToSupplier}
                    onValueChange={(value) => updateItem(item.productId, { isReturnedToSupplier: value })}
                    trackColor={{ false: '#d1d5db', true: '#86efac' }}
                    thumbColor={item.isReturnedToSupplier ? '#16a34a' : '#f3f4f6'}
                  />
                </View>
                {item.isReturnedToSupplier && (
                  <View className="gap-2">
                    <View className="flex-row gap-2">
                      {(['fixed', 'percent'] as ReimbursementType[]).map((type) => (
                        <TouchableOpacity
                          key={type}
                          onPress={() => updateItem(item.productId, { reimbursementType: type })}
                          className={`flex-1 rounded-lg py-2 items-center border ${item.reimbursementType === type ? 'bg-green-50 border-primary-600' : 'border-gray-200'}`}
                        >
                          <Text className={item.reimbursementType === type ? 'text-primary-700 font-semibold' : 'text-gray-600'}>
                            {type === 'fixed' ? 'Фіксовано / од.' : 'Відсоток'}
                          </Text>
                        </TouchableOpacity>
                      ))}
                    </View>
                    <TextInput
                      value={item.reimbursementValueInput}
                      onChangeText={(value) => updateItem(item.productId, { reimbursementValueInput: value, reimbursementValue: parseAmount(value) })}
                      keyboardType="decimal-pad"
                      placeholder={item.reimbursementType === 'percent' ? 'Відсоток, 0–100' : 'Сума за одиницю'}
                      className="border border-gray-200 rounded-lg px-3 py-2 text-gray-900"
                    />
                    <Text className="text-xs font-medium text-green-700">
                      Відшкодування: {money(calculateReimbursement(item.quantity, item.unitPricePurchase, item.reimbursementType, item.reimbursementValue))}
                    </Text>
                  </View>
                )}
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
            onPress={() => void handleSubmit()}
            disabled={isSubmitting || items.length === 0}
            className={`rounded-xl py-3.5 items-center ${
              items.length === 0 ? 'bg-gray-200' : 'bg-primary-600'
            }`}
          >
            {isSubmitting ? (
              <ActivityIndicator size="small" color="white" />
            ) : (
              <Text
                className={`font-bold text-base ${
                  items.length === 0 ? 'text-gray-400' : 'text-white'
                }`}
              >
                Створити списання
              </Text>
            )}
          </TouchableOpacity>
        </View>
      </KeyboardAvoidingView>

      {/* Scanner modal */}
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
                    <Text className="text-white/80 text-sm">Пошук товару...</Text>
                  </View>
                ) : (
                  <Text className="text-white/70 text-sm mt-6">Наведіть на штрихкод</Text>
                )}
              </View>
            </SafeAreaView>
          </CameraView>
        </View>
      </Modal>

      {/* Reason picker modal */}
      <Modal visible={reasonOpen} transparent animationType="slide">
        <TouchableOpacity
          activeOpacity={1}
          onPress={() => setReasonOpen(false)}
          className="flex-1 justify-end bg-black/50"
        >
          <View className="bg-white rounded-t-3xl p-6">
            <Text className="text-lg font-bold text-gray-900 mb-4">Оберіть причину</Text>
            {REASONS.map((r) => (
              <TouchableOpacity
                key={r}
                onPress={() => { setReason(r); setReasonOpen(false); }}
                className={`flex-row items-center justify-between py-3.5 border-b border-gray-50 ${
                  r === reason ? 'opacity-100' : 'opacity-70'
                }`}
              >
                <Text className={`text-base ${r === reason ? 'font-semibold text-primary-700' : 'text-gray-700'}`}>
                  {WRITE_OFF_REASON_LABELS[r]}
                </Text>
                {r === reason && <Ionicons name="checkmark" size={20} color="#15803d" />}
              </TouchableOpacity>
            ))}
          </View>
        </TouchableOpacity>
      </Modal>
    </SafeAreaView>
  );
}
