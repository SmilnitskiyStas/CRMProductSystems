import { useState, useEffect } from 'react';
import {
  View,
  Text,
  Modal,
  TouchableOpacity,
  TextInput,
  ScrollView,
  ActivityIndicator,
  Alert,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { cssInterop } from 'nativewind';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { useServiceCatalog, useAddWorkOrderLine } from '../hooks/useAutoService';
import { getSparePartByBarcode, searchSpareParts } from '../api';
import type { AsServiceCatalogItem, SparePartItem, WorkOrderLineType } from '../types';

// Register CameraView for NativeWind className support
cssInterop(CameraView, { className: 'style' });

interface Props {
  workOrderId: string;
  visible: boolean;
  onClose: () => void;
  prefillItem?: SparePartItem | null;
}

export function AddLineModal({ workOrderId, visible, onClose, prefillItem }: Props) {
  const [lineType, setLineType] = useState<WorkOrderLineType>('service');
  const [selectedService, setSelectedService] = useState<AsServiceCatalogItem | null>(null);
  const [selectedPart, setSelectedPart] = useState<SparePartItem | null>(null);
  const [qty, setQty] = useState('1');
  const [price, setPrice] = useState('');
  const [discount, setDiscount] = useState('0');
  const [showCamera, setShowCamera] = useState(false);
  const [partSearch, setPartSearch] = useState('');
  const [searchResults, setSearchResults] = useState<SparePartItem[]>([]);
  const [searching, setSearching] = useState(false);
  const [scanLoading, setScanLoading] = useState(false);
  const [scanned, setScanned] = useState(false);
  const [permission, requestPermission] = useCameraPermissions();

  const { data: catalog = [], isLoading: catalogLoading } = useServiceCatalog();
  const addLine = useAddWorkOrderLine(workOrderId);

  // Pre-fill if an item is passed (e.g. from barcode scan on detail screen)
  useEffect(() => {
    if (prefillItem && visible) {
      setLineType('part');
      setSelectedPart(prefillItem);
      setPrice(prefillItem.priceRetail.toString());
    }
  }, [prefillItem, visible]);

  function reset() {
    setLineType('service');
    setSelectedService(null);
    setSelectedPart(null);
    setQty('1');
    setPrice('');
    setDiscount('0');
    setShowCamera(false);
    setPartSearch('');
    setSearchResults([]);
    setScanned(false);
  }

  function handleClose() {
    reset();
    onClose();
  }

  function handleSelectService(item: AsServiceCatalogItem) {
    setSelectedService(item);
    setPrice(item.defaultPrice.toString());
  }

  function handleSelectPart(item: SparePartItem) {
    setSelectedPart(item);
    setPrice(item.priceRetail.toString());
    setSearchResults([]);
    setPartSearch('');
  }

  async function handlePartSearch(text: string) {
    setPartSearch(text);
    if (text.length < 2) {
      setSearchResults([]);
      return;
    }
    setSearching(true);
    try {
      const results = await searchSpareParts(text);
      setSearchResults(results);
    } catch {
      setSearchResults([]);
    } finally {
      setSearching(false);
    }
  }

  async function handleBarcodeScan({ data }: { data: string }) {
    if (scanned || scanLoading) return;
    setScanned(true);
    setScanLoading(true);
    try {
      const item = await getSparePartByBarcode(data);
      if (item) {
        handleSelectPart(item);
        setShowCamera(false);
      } else {
        Alert.alert('Не знайдено', 'Запчастину з таким штрихкодом не знайдено');
        setScanned(false);
      }
    } catch {
      Alert.alert('Помилка', 'Не вдалося знайти запчастину');
      setScanned(false);
    } finally {
      setScanLoading(false);
    }
  }

  async function handleOpenCamera() {
    if (!permission?.granted) {
      const result = await requestPermission();
      if (!result.granted) {
        Alert.alert('Потрібен доступ до камери', 'Дозвольте доступ у налаштуваннях');
        return;
      }
    }
    setScanned(false);
    setShowCamera(true);
  }

  function handleSubmit() {
    const qtyNum = parseFloat(qty);
    const priceNum = parseFloat(price);
    const discountNum = parseFloat(discount) || 0;

    if (isNaN(qtyNum) || qtyNum <= 0) {
      Alert.alert('Невірна кількість');
      return;
    }
    if (isNaN(priceNum) || priceNum < 0) {
      Alert.alert('Невірна ціна');
      return;
    }
    if (lineType === 'service' && !selectedService) {
      Alert.alert('Оберіть послугу');
      return;
    }
    if (lineType === 'part' && !selectedPart) {
      Alert.alert('Оберіть запчастину');
      return;
    }

    addLine.mutate(
      {
        type: lineType,
        serviceCatalogId: lineType === 'service' ? selectedService?.id : undefined,
        itemId: lineType === 'part' ? selectedPart?.id : undefined,
        qty: qtyNum,
        price: priceNum,
        discount: discountNum,
      },
      {
        onSuccess: () => {
          handleClose();
        },
        onError: () => {
          Alert.alert('Помилка', 'Не вдалося додати позицію');
        },
      }
    );
  }

  if (showCamera) {
    return (
      <Modal visible={visible} animationType="slide">
        <View className="flex-1 bg-black">
          <CameraView
            className="flex-1"
            facing="back"
            onBarcodeScanned={scanned ? undefined : handleBarcodeScan}
            barcodeScannerSettings={{
              barcodeTypes: ['ean8', 'ean13', 'qr', 'code128'],
            }}
          >
            <View className="flex-1">
              <TouchableOpacity
                onPress={() => setShowCamera(false)}
                className="m-4 w-10 h-10 bg-black/40 rounded-full items-center justify-center"
              >
                <Ionicons name="close" size={22} color="white" />
              </TouchableOpacity>
              <View className="flex-1 items-center justify-center">
                {scanLoading ? (
                  <ActivityIndicator size="large" color="white" />
                ) : (
                  <View className="w-64 h-64 relative">
                    <View className="absolute top-0 left-0 w-8 h-8 border-t-4 border-l-4 border-primary-500 rounded-tl-lg" />
                    <View className="absolute top-0 right-0 w-8 h-8 border-t-4 border-r-4 border-primary-500 rounded-tr-lg" />
                    <View className="absolute bottom-0 left-0 w-8 h-8 border-b-4 border-l-4 border-primary-500 rounded-bl-lg" />
                    <View className="absolute bottom-0 right-0 w-8 h-8 border-b-4 border-r-4 border-primary-500 rounded-br-lg" />
                  </View>
                )}
                <Text className="text-white/70 text-sm mt-6">
                  Скануйте штрихкод запчастини
                </Text>
              </View>
            </View>
          </CameraView>
        </View>
      </Modal>
    );
  }

  return (
    <Modal visible={visible} animationType="slide" presentationStyle="pageSheet">
      <View className="flex-1 bg-white">
        {/* Header */}
        <View className="flex-row items-center justify-between px-4 pt-5 pb-3 border-b border-gray-100">
          <Text className="text-lg font-bold text-gray-900">Додати позицію</Text>
          <TouchableOpacity
            onPress={handleClose}
            className="w-8 h-8 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="close" size={18} color="#374151" />
          </TouchableOpacity>
        </View>

        <ScrollView className="flex-1 p-4" keyboardShouldPersistTaps="handled">
          {/* Type selector */}
          <Text className="text-sm font-semibold text-gray-700 mb-2">Тип</Text>
          <View className="flex-row gap-2 mb-4">
            <TouchableOpacity
              onPress={() => {
                setLineType('service');
                setSelectedPart(null);
                setPrice('');
              }}
              className={`flex-1 py-2.5 rounded-xl border items-center ${
                lineType === 'service'
                  ? 'border-primary-600 bg-primary-50'
                  : 'border-gray-200 bg-white'
              }`}
            >
              <Text
                className={`text-sm font-semibold ${
                  lineType === 'service' ? 'text-primary-700' : 'text-gray-500'
                }`}
              >
                Послуга
              </Text>
            </TouchableOpacity>
            <TouchableOpacity
              onPress={() => {
                setLineType('part');
                setSelectedService(null);
                setPrice('');
              }}
              className={`flex-1 py-2.5 rounded-xl border items-center ${
                lineType === 'part'
                  ? 'border-primary-600 bg-primary-50'
                  : 'border-gray-200 bg-white'
              }`}
            >
              <Text
                className={`text-sm font-semibold ${
                  lineType === 'part' ? 'text-primary-700' : 'text-gray-500'
                }`}
              >
                Запчастина
              </Text>
            </TouchableOpacity>
          </View>

          {/* Service selector */}
          {lineType === 'service' && (
            <View className="mb-4">
              <Text className="text-sm font-semibold text-gray-700 mb-2">Послуга</Text>
              {catalogLoading ? (
                <ActivityIndicator color="#16a34a" />
              ) : (
                <View className="border border-gray-200 rounded-xl overflow-hidden">
                  {catalog.map((svc, idx) => (
                    <TouchableOpacity
                      key={svc.id}
                      onPress={() => handleSelectService(svc)}
                      className={`flex-row items-center justify-between px-4 py-3 ${
                        idx > 0 ? 'border-t border-gray-100' : ''
                      } ${selectedService?.id === svc.id ? 'bg-primary-50' : 'bg-white'}`}
                    >
                      <View className="flex-1 mr-2">
                        <Text className="text-sm font-medium text-gray-900">{svc.name}</Text>
                        {svc.description && (
                          <Text className="text-xs text-gray-400 mt-0.5" numberOfLines={1}>
                            {svc.description}
                          </Text>
                        )}
                      </View>
                      <View className="flex-row items-center gap-2">
                        <Text className="text-sm text-gray-600">
                          {svc.defaultPrice.toFixed(2)} ₴
                        </Text>
                        {selectedService?.id === svc.id && (
                          <Ionicons name="checkmark-circle" size={18} color="#16a34a" />
                        )}
                      </View>
                    </TouchableOpacity>
                  ))}
                  {catalog.length === 0 && (
                    <View className="p-4 items-center">
                      <Text className="text-sm text-gray-400">Каталог порожній</Text>
                    </View>
                  )}
                </View>
              )}
            </View>
          )}

          {/* Part selector */}
          {lineType === 'part' && (
            <View className="mb-4">
              <Text className="text-sm font-semibold text-gray-700 mb-2">Запчастина</Text>

              {selectedPart ? (
                <View className="flex-row items-center justify-between p-3 bg-primary-50 rounded-xl border border-primary-200">
                  <View className="flex-1 mr-2">
                    <Text className="text-sm font-semibold text-primary-900">
                      {selectedPart.name}
                    </Text>
                    <Text className="text-xs text-primary-600 mt-0.5">
                      {selectedPart.priceRetail.toFixed(2)} ₴
                    </Text>
                  </View>
                  <TouchableOpacity
                    onPress={() => {
                      setSelectedPart(null);
                      setPrice('');
                    }}
                    className="w-7 h-7 items-center justify-center rounded-full bg-primary-100"
                  >
                    <Ionicons name="close" size={14} color="#15803d" />
                  </TouchableOpacity>
                </View>
              ) : (
                <>
                  <View className="flex-row gap-2 mb-2">
                    <View className="flex-1 flex-row items-center border border-gray-200 rounded-xl px-3 bg-gray-50">
                      <Ionicons name="search-outline" size={16} color="#9ca3af" />
                      <TextInput
                        className="flex-1 py-2.5 px-2 text-sm text-gray-900"
                        placeholder="Пошук запчастини..."
                        value={partSearch}
                        onChangeText={handlePartSearch}
                        placeholderTextColor="#9ca3af"
                      />
                      {searching && <ActivityIndicator size="small" color="#9ca3af" />}
                    </View>
                    <TouchableOpacity
                      onPress={handleOpenCamera}
                      className="w-11 h-11 bg-gray-100 rounded-xl items-center justify-center border border-gray-200"
                    >
                      <Ionicons name="scan-outline" size={20} color="#374151" />
                    </TouchableOpacity>
                  </View>

                  {searchResults.length > 0 && (
                    <View className="border border-gray-200 rounded-xl overflow-hidden">
                      {searchResults.slice(0, 6).map((part, idx) => (
                        <TouchableOpacity
                          key={part.id}
                          onPress={() => handleSelectPart(part)}
                          className={`flex-row items-center justify-between px-4 py-3 bg-white ${
                            idx > 0 ? 'border-t border-gray-100' : ''
                          }`}
                        >
                          <Text className="flex-1 text-sm text-gray-900 mr-2" numberOfLines={1}>
                            {part.name}
                          </Text>
                          <Text className="text-sm text-gray-500">
                            {part.priceRetail.toFixed(2)} ₴
                          </Text>
                        </TouchableOpacity>
                      ))}
                    </View>
                  )}
                </>
              )}
            </View>
          )}

          {/* Qty / Price / Discount */}
          <View className="flex-row gap-3 mb-4">
            <View className="flex-1">
              <Text className="text-sm font-semibold text-gray-700 mb-1.5">Кількість</Text>
              <TextInput
                className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
                value={qty}
                onChangeText={setQty}
                keyboardType="decimal-pad"
                placeholderTextColor="#9ca3af"
              />
            </View>
            <View className="flex-1">
              <Text className="text-sm font-semibold text-gray-700 mb-1.5">Ціна (₴)</Text>
              <TextInput
                className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
                value={price}
                onChangeText={setPrice}
                keyboardType="decimal-pad"
                placeholder="0.00"
                placeholderTextColor="#9ca3af"
              />
            </View>
            <View className="flex-1">
              <Text className="text-sm font-semibold text-gray-700 mb-1.5">Знижка (%)</Text>
              <TextInput
                className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
                value={discount}
                onChangeText={setDiscount}
                keyboardType="decimal-pad"
                placeholder="0"
                placeholderTextColor="#9ca3af"
              />
            </View>
          </View>
        </ScrollView>

        {/* Submit */}
        <View className="px-4 py-4 border-t border-gray-100">
          <TouchableOpacity
            onPress={handleSubmit}
            disabled={addLine.isPending}
            className="bg-primary-600 py-3.5 rounded-xl items-center"
          >
            {addLine.isPending ? (
              <ActivityIndicator color="white" />
            ) : (
              <Text className="text-white font-bold text-base">Додати позицію</Text>
            )}
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
}
