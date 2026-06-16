import { useState } from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  FlatList,
  Modal,
  ActivityIndicator,
  Alert,
  TextInput,
  KeyboardAvoidingView,
  Platform,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { cssInterop } from 'nativewind';
import { useCreateWriteOff } from '@/features/write-offs/hooks/useWriteOffs';
import { WRITE_OFF_REASON_LABELS } from '@/features/write-offs/types';
import type { WriteOffReason } from '@/features/write-offs/types';
import { getProductByBarcode } from '@/features/stock/api/stockApi';
import { useAuthStore } from '@/features/auth/store';

cssInterop(CameraView, { className: 'style' });

const REASONS: WriteOffReason[] = ['expired', 'damaged', 'theft', 'production_loss', 'other'];

interface DraftItem {
  productId: string;
  productName: string;
  quantity: number;
}

export default function CreateWriteOffScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const createWriteOff = useCreateWriteOff();

  const [items, setItems] = useState<DraftItem[]>([]);
  const [reason, setReason] = useState<WriteOffReason>('expired');
  const [scanOpen, setScanOpen] = useState(false);
  const [scanLoading, setScanLoading] = useState(false);
  const [scanned, setScanned] = useState(false);
  const [reasonOpen, setReasonOpen] = useState(false);

  const [permission, requestPermission] = useCameraPermissions();

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
        return [...prev, { productId: product.id, productName: product.name, quantity: 1 }];
      });
    } catch {
      Alert.alert('Товар не знайдено', 'Перевірте штрихкод і спробуйте знову.');
      setScanned(false);
    } finally {
      setScanLoading(false);
    }
  }

  function changeQty(productId: string, delta: number) {
    setItems((prev) =>
      prev
        .map((i) => i.productId === productId ? { ...i, quantity: i.quantity + delta } : i)
        .filter((i) => i.quantity > 0)
    );
  }

  function handleSubmit() {
    if (!user?.locationId) {
      Alert.alert('Помилка', 'Локацію не призначено для вашого профілю.');
      return;
    }
    if (items.length === 0) {
      Alert.alert('Додайте хоча б один товар');
      return;
    }

    createWriteOff.mutate(
      {
        locationId: user.locationId,
        reason,
        items: items.map((i) => ({ productId: i.productId, quantity: i.quantity })),
      },
      {
        onSuccess: () => {
          Alert.alert('Успішно', 'Списання створено і відправлено на затвердження.', [
            { text: 'OK', onPress: () => router.back() },
          ]);
        },
        onError: () => {
          Alert.alert('Помилка', 'Не вдалося створити списання. Спробуйте ще раз.');
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
            <View className="bg-white rounded-xl px-4 py-3 flex-row items-center">
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
            disabled={createWriteOff.isPending || items.length === 0}
            className={`rounded-xl py-3.5 items-center ${
              items.length === 0 ? 'bg-gray-200' : 'bg-primary-600'
            }`}
          >
            {createWriteOff.isPending ? (
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
