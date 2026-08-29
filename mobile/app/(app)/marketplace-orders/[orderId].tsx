import { useEffect, useMemo, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  Image,
  Modal,
  Platform,
  ScrollView,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { Stack, useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import DateTimePicker from '@react-native-community/datetimepicker';
import NetInfo from '@react-native-community/netinfo';
import { cssInterop } from 'nativewind';
import type { AxiosError } from 'axios';
import { resolveApiAssetUrl } from '@/lib/api-client';
import { getProductByBarcode } from '@/features/stock/api/stockApi';
import { searchCatalogProducts } from '@/features/marketplace-orders/api/marketplaceOrdersApi';
import {
  useMarketplaceReceipt,
} from '@/features/marketplace-orders/hooks/useMarketplaceOrders';
import type {
  CatalogProduct,
  MarketplaceOrderReceipt,
  MarketplaceOrderReceiptItem,
} from '@/features/marketplace-orders/types';
import { useAuthStore } from '@/features/auth/store';
import { enqueueOperationalMutation, syncOperationalMutations } from '@/features/offline-mutations/operationalQueue';
import { queryClient } from '@/lib/query-client';

cssInterop(CameraView, { className: 'style' });

type ApiError = AxiosError<{ error?: string }>;

function errorText(error: unknown, fallback: string) {
  return (error as ApiError)?.response?.data?.error ?? fallback;
}

function isoDate(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function formatPrice(value: number) {
  return `${value.toLocaleString('uk-UA', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₴`;
}

function ItemRow({ item, onPress }: { item: MarketplaceOrderReceiptItem; onPress: () => void }) {
  const imageUrl = resolveApiAssetUrl(item.referenceImageUrl);
  return (
    <TouchableOpacity onPress={onPress} className="bg-white px-4 py-4 border-b border-gray-100">
      <View className="flex-row items-start">
        <View className="w-14 h-14 rounded-xl bg-gray-100 overflow-hidden items-center justify-center">
          {imageUrl ? (
            <Image source={{ uri: imageUrl }} resizeMode="contain" className="w-full h-full" />
          ) : (
            <Ionicons name="cube-outline" size={27} color="#9ca3af" />
          )}
          <View className={`absolute right-0 bottom-0 w-5 h-5 rounded-full items-center justify-center ${item.isResolved ? 'bg-green-500' : 'bg-gray-500'}`}>
            <Ionicons name={item.isResolved ? 'checkmark' : 'scan-outline'} size={12} color="white" />
          </View>
        </View>
        <View className="flex-1 ml-3">
          <Text className="font-semibold text-gray-900">{item.itemNameSnapshot}</Text>
          <Text className="text-xs text-gray-500 mt-1">
            Замовлено: {item.quantityOrdered} · {formatPrice(item.price)}
          </Text>
          {item.productName ? <Text className="text-sm text-blue-700 mt-1">Зіставлено: {item.productName}</Text> : null}
          {item.isResolved ? (
            <Text className="text-xs text-gray-500 mt-1">
              Отримано: {item.quantityReceived} · до {item.expiryDate}{item.batchNumber ? ` · партія ${item.batchNumber}` : ''}
            </Text>
          ) : null}
        </View>
        <Ionicons name="chevron-forward" size={18} color="#9ca3af" />
      </View>
    </TouchableOpacity>
  );
}

export default function MarketplaceReceiptScreen() {
  const { orderId = '' } = useLocalSearchParams<{ orderId: string }>();
  const router = useRouter();
  const receiptQuery = useMarketplaceReceipt(orderId);
  const user = useAuthStore((state) => state.user);
  const owner = user?.tenantId ? { tenantId: user.tenantId, userId: user.id } : null;
  const [saving, setSaving] = useState(false);
  const [permission, requestPermission] = useCameraPermissions();
  const [activeItem, setActiveItem] = useState<MarketplaceOrderReceiptItem | null>(null);
  const [product, setProduct] = useState<CatalogProduct | null>(null);
  const [quantity, setQuantity] = useState('');
  const [expiry, setExpiry] = useState('');
  const [batch, setBatch] = useState('');
  const [notes, setNotes] = useState('');
  const [scanning, setScanning] = useState(false);
  const [scanBusy, setScanBusy] = useState(false);
  const [showDatePicker, setShowDatePicker] = useState(false);
  const [searchMode, setSearchMode] = useState(false);
  const [search, setSearch] = useState('');
  const [searchResults, setSearchResults] = useState<CatalogProduct[]>([]);
  const [searching, setSearching] = useState(false);

  useEffect(() => {
    if (scanning && permission && !permission.granted && permission.canAskAgain) void requestPermission();
  }, [permission, requestPermission, scanning]);

  const receipt = receiptQuery.data;
  const resolvedCount = receipt?.items.filter((item) => item.isResolved).length ?? 0;
  const allResolved = Boolean(receipt?.items.length) && receipt!.items.every((item) => item.isResolved);
  const progress: `${number}%` = receipt?.items.length
    ? `${(resolvedCount / receipt.items.length) * 100}%`
    : '0%';
  const selectedDate = useMemo(() => expiry ? new Date(`${expiry}T12:00:00`) : new Date(), [expiry]);

  function openItem(item: MarketplaceOrderReceiptItem) {
    setActiveItem(item);
    setProduct(item.productId && item.productName ? { id: item.productId, name: item.productName } : null);
    setQuantity(String(item.quantityReceived ?? item.quantityOrdered));
    setExpiry(item.expiryDate ?? '');
    setBatch(item.batchNumber ?? '');
    setNotes(item.discrepancyNotes ?? '');
    setScanning(!item.productId);
    setSearchMode(false);
  }

  function closeEditor() {
    setActiveItem(null);
    setScanning(false);
    setSearchMode(false);
    setProduct(null);
  }

  async function handleScan({ data }: { data: string }) {
    if (scanBusy) return;
    setScanBusy(true);
    try {
      const found = await getProductByBarcode(data) as CatalogProduct;
      setProduct(found);
      setScanning(false);
    } catch {
      Alert.alert('Товар не знайдено', 'Товар не знайдено у вашому каталозі. Спробуйте інший штрихкод або знайдіть товар вручну.', [
        { text: 'Сканувати ще', onPress: () => setScanBusy(false) },
        { text: 'Пошук', onPress: () => { setScanning(false); setSearchMode(true); setScanBusy(false); } },
      ]);
      return;
    }
    setScanBusy(false);
  }

  async function runSearch() {
    if (search.trim().length < 2) return;
    setSearching(true);
    try {
      setSearchResults(await searchCatalogProducts(search.trim()));
    } catch (error) {
      Alert.alert('Помилка', errorText(error, 'Не вдалося виконати пошук товарів.'));
    } finally {
      setSearching(false);
    }
  }

  async function saveItem() {
    if (!activeItem || !product) return Alert.alert('Оберіть товар', 'Відскануйте штрихкод або знайдіть товар вручну.');
    const parsedQuantity = Number(quantity.replace(',', '.'));
    if (!Number.isFinite(parsedQuantity) || parsedQuantity < 0) return Alert.alert('Некоректна кількість', 'Введіть число, не менше нуля.');
    if (!expiry) return Alert.alert('Вкажіть термін', 'Оберіть термін придатності партії.');
    if (!owner || saving) return;
    const body = {
        productId: product.id,
        quantityReceived: parsedQuantity,
        expiryDate: expiry,
        batchNumber: batch.trim() || null,
        discrepancyNotes: notes.trim() || null,
    };
    setSaving(true);
    try {
      await enqueueOperationalMutation(owner, { kind: 'marketplace-receipt.item', payload: { orderId, itemId: activeItem.id, body } });
      queryClient.setQueryData<MarketplaceOrderReceipt>(['marketplace-orders', orderId, 'receipt'], (current) => current ? {
        ...current,
        items: current.items.map((item) => item.id === activeItem.id ? { ...item, ...body, productName: product.name, isResolved: true } : item),
      } : current);
      closeEditor();
      const network = await NetInfo.fetch();
      if (network.isConnected && network.isInternetReachable !== false) await syncOperationalMutations(owner);
    } catch (error) {
      Alert.alert('Не вдалося зберегти', errorText(error, 'Локальне збереження не вдалося.'));
    } finally { setSaving(false); }
  }

  async function finalizeLocally() {
    if (!owner || saving) return;
    setSaving(true);
    try {
      await enqueueOperationalMutation(owner, { kind: 'marketplace-receipt.finalize', payload: { orderId } });
      const network = await NetInfo.fetch();
      if (network.isConnected && network.isInternetReachable !== false) await syncOperationalMutations(owner);
      router.replace('/(app)/marketplace-orders');
    } catch (error) {
      Alert.alert('Не вдалося підтвердити', errorText(error, 'Локальне збереження не вдалося.'));
    } finally { setSaving(false); }
  }

  if (receiptQuery.isLoading) return <SafeAreaView className="flex-1 items-center justify-center"><ActivityIndicator size="large" color="#16a34a" /></SafeAreaView>;
  if (!receipt) return (
    <SafeAreaView className="flex-1 items-center justify-center px-6">
      <Text className="text-red-600 text-center">{errorText(receiptQuery.error, 'Не вдалося відкрити приймання.')}</Text>
      <TouchableOpacity onPress={() => router.back()} className="mt-4"><Text className="text-primary-600">Назад</Text></TouchableOpacity>
    </SafeAreaView>
  );

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <Stack.Screen options={{ headerShown: true, title: 'Приймання замовлення', headerBackTitle: '' }} />
      <View className="bg-white px-4 py-3 border-b border-gray-100">
        <Text className="text-sm text-gray-600">Магазин: {receipt.destinationStoreName}</Text>
        <Text className="text-sm text-gray-700 mt-1">Оброблено: {resolvedCount} / {receipt.items.length}</Text>
        <View className="h-1.5 bg-gray-200 rounded-full mt-2"><View className="h-1.5 bg-primary-600 rounded-full" style={{ width: progress }} /></View>
      </View>
      <FlatList data={receipt.items} keyExtractor={(item) => item.id} renderItem={({ item }) => <ItemRow item={item} onPress={() => openItem(item)} />} contentContainerClassName="pb-32" />
      <View className="absolute bottom-0 left-0 right-0 bg-white border-t border-gray-100 px-4 pt-4 pb-8">
        <TouchableOpacity
          disabled={!allResolved || saving}
          onPress={() => void finalizeLocally()}
          className={`py-4 rounded-xl items-center ${allResolved ? 'bg-primary-600' : 'bg-gray-200'}`}
        >
          {saving ? <ActivityIndicator color="white" /> : <Text className={`font-semibold ${allResolved ? 'text-white' : 'text-gray-400'}`}>Підтвердити прийомку</Text>}
        </TouchableOpacity>
      </View>

      <Modal visible={Boolean(activeItem)} animationType="slide" onRequestClose={closeEditor}>
        {scanning ? (
          <View className="flex-1 bg-black">
            {!permission ? <ActivityIndicator className="flex-1" color="white" /> : !permission.granted ? (
              <SafeAreaView className="flex-1 items-center justify-center px-6">
                <Ionicons name="camera-outline" size={56} color="white" />
                <Text className="text-white text-center mt-4">Для сканування потрібен доступ до камери</Text>
                <TouchableOpacity onPress={() => void requestPermission()} className="bg-primary-600 px-5 py-3 rounded-xl mt-5"><Text className="text-white font-semibold">Надати доступ</Text></TouchableOpacity>
                <TouchableOpacity onPress={() => { setScanning(false); setSearchMode(true); }} className="mt-5"><Text className="text-white">Знайти товар вручну</Text></TouchableOpacity>
              </SafeAreaView>
            ) : (
              <CameraView className="flex-1" facing="back" onBarcodeScanned={scanBusy ? undefined : handleScan} barcodeScannerSettings={{ barcodeTypes: ['ean8', 'ean13', 'qr', 'code128'] }}>
                <SafeAreaView className="flex-1">
                  <TouchableOpacity onPress={closeEditor} className="m-4 w-10 h-10 bg-black/50 rounded-full items-center justify-center"><Ionicons name="close" size={22} color="white" /></TouchableOpacity>
                  <View className="flex-1 items-center justify-center">
                    <View className="w-64 h-64 border-4 border-primary-500 rounded-2xl" />
                    <View className="mt-5 mx-6 self-stretch bg-black/60 rounded-2xl p-3 flex-row items-center">
                      <View className="w-16 h-16 rounded-xl bg-gray-100 overflow-hidden items-center justify-center">
                        {resolveApiAssetUrl(activeItem?.referenceImageUrl) ? (
                          <Image source={{ uri: resolveApiAssetUrl(activeItem?.referenceImageUrl) as string }} resizeMode="contain" className="w-full h-full" />
                        ) : (
                          <Ionicons name="cube-outline" size={30} color="#9ca3af" />
                        )}
                      </View>
                      <View className="flex-1 ml-3">
                        <Text className="text-white font-semibold" numberOfLines={2}>{activeItem?.itemNameSnapshot}</Text>
                        {activeItem ? <Text className="text-gray-200 text-sm mt-1">{formatPrice(activeItem.price)}</Text> : null}
                      </View>
                    </View>
                  </View>
                  <TouchableOpacity onPress={() => { setScanning(false); setSearchMode(true); }} className="mx-6 mb-8 bg-white/90 py-3 rounded-xl items-center"><Text className="font-semibold text-gray-900">Знайти вручну</Text></TouchableOpacity>
                </SafeAreaView>
              </CameraView>
            )}
          </View>
        ) : searchMode ? (
          <SafeAreaView className="flex-1 bg-gray-50">
            <View className="flex-row items-center px-4 py-3 bg-white"><TouchableOpacity onPress={closeEditor}><Ionicons name="close" size={24} /></TouchableOpacity><Text className="text-lg font-bold ml-4">Пошук у каталозі</Text></View>
            <View className="flex-row p-4"><TextInput value={search} onChangeText={setSearch} onSubmitEditing={() => void runSearch()} placeholder="Назва товару" className="flex-1 bg-white border border-gray-200 rounded-xl px-4 py-3" /><TouchableOpacity onPress={() => void runSearch()} className="bg-primary-600 ml-2 px-4 rounded-xl justify-center"><Ionicons name="search" size={20} color="white" /></TouchableOpacity></View>
            {searching ? <ActivityIndicator color="#16a34a" /> : <FlatList data={searchResults} keyExtractor={(item) => item.id} renderItem={({ item }) => <TouchableOpacity onPress={() => { setProduct(item); setSearchMode(false); }} className="bg-white px-4 py-4 border-b border-gray-100"><Text className="font-medium text-gray-900">{item.name}</Text>{item.barcode ? <Text className="text-xs text-gray-500 mt-1">{item.barcode}</Text> : null}</TouchableOpacity>} ListEmptyComponent={<Text className="text-gray-400 text-center mt-12">Введіть щонайменше 2 символи</Text>} />}
          </SafeAreaView>
        ) : (
          <SafeAreaView className="flex-1 bg-gray-50">
            <View className="flex-row items-center px-4 py-3 bg-white border-b border-gray-100"><TouchableOpacity onPress={closeEditor}><Ionicons name="close" size={24} /></TouchableOpacity><Text className="text-lg font-bold ml-4 flex-1">Дані позиції</Text><TouchableOpacity onPress={() => setScanning(true)}><Ionicons name="scan-outline" size={24} color="#16a34a" /></TouchableOpacity></View>
            <ScrollView contentContainerClassName="p-4 pb-12" keyboardShouldPersistTaps="handled">
              <Text className="text-xs uppercase text-gray-400">Очікується</Text><Text className="text-lg font-bold text-gray-900 mt-1">{activeItem?.itemNameSnapshot}</Text>
              <View className="mt-4 bg-white border border-gray-100 rounded-2xl p-3 flex-row items-center">
                <View className="w-24 h-24 rounded-xl bg-gray-100 overflow-hidden items-center justify-center">
                  {resolveApiAssetUrl(activeItem?.referenceImageUrl) ? (
                    <Image source={{ uri: resolveApiAssetUrl(activeItem?.referenceImageUrl) as string }} resizeMode="contain" className="w-full h-full" />
                  ) : (
                    <Ionicons name="cube-outline" size={44} color="#9ca3af" />
                  )}
                </View>
                <View className="flex-1 ml-4">
                  <Text className="text-xs uppercase text-gray-400">Ціна замовлення</Text>
                  <Text className="text-xl font-bold text-gray-900 mt-1">{activeItem ? formatPrice(activeItem.price) : '—'}</Text>
                  <Text className="text-xs text-gray-500 mt-2">Замовлено: {activeItem?.quantityOrdered}</Text>
                </View>
              </View>
              <View className="bg-blue-50 border border-blue-100 rounded-xl p-3 mt-4"><Text className="text-xs text-blue-600">Товар із вашого каталогу</Text><Text className="font-semibold text-blue-900 mt-1">{product?.name}</Text><Text className="text-xs text-blue-700 mt-1">Переконайтеся, що це той самий товар.</Text></View>
              <Text className="text-sm font-medium text-gray-700 mt-5 mb-2">Кількість отримано *</Text><TextInput value={quantity} onChangeText={setQuantity} keyboardType="decimal-pad" className="bg-white border border-gray-200 rounded-xl px-4 py-3 text-base" />
              <Text className="text-sm font-medium text-gray-700 mt-5 mb-2">Термін придатності *</Text><TouchableOpacity onPress={() => setShowDatePicker(true)} className="bg-white border border-gray-200 rounded-xl px-4 py-3 flex-row justify-between"><Text className={expiry ? 'text-gray-900' : 'text-gray-400'}>{expiry || 'Оберіть дату'}</Text><Ionicons name="calendar-outline" size={20} color="#6b7280" /></TouchableOpacity>
              {showDatePicker ? <DateTimePicker value={selectedDate} mode="date" minimumDate={new Date()} onChange={(_, date) => { if (Platform.OS === 'android') setShowDatePicker(false); if (date) setExpiry(isoDate(date)); }} /> : null}
              <Text className="text-sm font-medium text-gray-700 mt-5 mb-2">Номер партії</Text><TextInput value={batch} onChangeText={setBatch} placeholder="Необов’язково" className="bg-white border border-gray-200 rounded-xl px-4 py-3" />
              <Text className="text-sm font-medium text-gray-700 mt-5 mb-2">Примітка про розбіжності</Text><TextInput value={notes} onChangeText={setNotes} placeholder="Необов’язково" multiline className="bg-white border border-gray-200 rounded-xl px-4 py-3 min-h-24" textAlignVertical="top" />
              <TouchableOpacity disabled={saving} onPress={() => void saveItem()} className="bg-primary-600 py-4 rounded-xl items-center mt-7">{saving ? <ActivityIndicator color="white" /> : <Text className="text-white font-semibold">Зберегти позицію</Text>}</TouchableOpacity>
            </ScrollView>
          </SafeAreaView>
        )}
      </Modal>
    </SafeAreaView>
  );
}
