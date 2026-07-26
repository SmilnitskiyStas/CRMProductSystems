import { useState, useCallback, useRef } from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  FlatList,
  ActivityIndicator,
  Alert,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { useRouter, useLocalSearchParams } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { cssInterop } from 'nativewind';
import { getProductByBarcode } from '@/features/stock/api/stockApi';
import type { SaleItem } from '@/features/pos/types';

cssInterop(CameraView, { className: 'style' });

// Product info returned from /items/by-barcode/:barcode
interface ProductInfo {
  id: string;
  name: string;
  barcode: string;
  price?: number;
  status?: string;
  itemType?: string;
}

// Cart item enriched with product info
interface CartItem extends SaleItem {
  productName: string;
  unitPrice: number;
}

function formatPrice(amount: number): string {
  return amount.toFixed(2);
}

export default function PosScannerScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ shiftId?: string }>();
  const shiftId = params.shiftId ?? '';

  const [permission, requestPermission] = useCameraPermissions();
  const [scanning, setScanning] = useState(true);
  const [loadingBarcode, setLoadingBarcode] = useState(false);
  const [cart, setCart] = useState<CartItem[]>([]);
  const lastScanRef = useRef<string | null>(null);

  const subtotal = cart.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0);

  const handleBarCodeScanned = useCallback(
    async ({ data }: { data: string }) => {
      if (!scanning || loadingBarcode) return;
      if (lastScanRef.current === data) return; // debounce same code
      lastScanRef.current = data;

      setScanning(false);
      setLoadingBarcode(true);

      try {
        const product: ProductInfo = await getProductByBarcode(data);

        setCart((prev) => {
          const existing = prev.find((i) => i.barcode === data);
          if (existing) {
            return prev.map((i) =>
              i.barcode === data ? { ...i, quantity: i.quantity + 1 } : i
            );
          }
          return [
            ...prev,
            {
              barcode: data,
              quantity: 1,
              productName: product.name,
              unitPrice: product.price ?? 0,
              isCritical: product.status === 'critical',
            },
          ];
        });
      } catch {
        Alert.alert('Товар не знайдено', `Штрихкод: ${data}`);
      } finally {
        setLoadingBarcode(false);
        // Resume scanning after a short pause
        setTimeout(() => {
          setScanning(true);
          lastScanRef.current = null;
        }, 1500);
      }
    },
    [scanning, loadingBarcode]
  );

  const increaseQty = (barcode: string) => {
    setCart((prev) =>
      prev.map((i) => (i.barcode === barcode ? { ...i, quantity: i.quantity + 1 } : i))
    );
  };

  const decreaseQty = (barcode: string) => {
    setCart((prev) => {
      const item = prev.find((i) => i.barcode === barcode);
      if (!item) return prev;
      if (item.quantity <= 1) return prev.filter((i) => i.barcode !== barcode);
      return prev.map((i) =>
        i.barcode === barcode ? { ...i, quantity: i.quantity - 1 } : i
      );
    });
  };

  const removeItem = (barcode: string) => {
    setCart((prev) => prev.filter((i) => i.barcode !== barcode));
  };

  const handleGoToPayment = () => {
    if (cart.length === 0) {
      Alert.alert('Кошик порожній', 'Відскануйте хоча б один товар.');
      return;
    }
    // Encode cart as JSON param. Routes through the loyalty step first (TASK-405/407) —
    // that screen forwards to /pos/payment itself (with or without loyalty fields attached).
    router.push({
      pathname: '/(app)/pos/loyalty',
      params: {
        shiftId,
        cartJson: JSON.stringify(cart),
      },
    });
  };

  // ── Permission states ──────────────────────────────────────────────────────

  if (!permission) {
    return (
      <SafeAreaView className="flex-1 bg-black items-center justify-center">
        <ActivityIndicator color="white" />
      </SafeAreaView>
    );
  }

  if (!permission.granted) {
    return (
      <SafeAreaView className="flex-1 bg-black items-center justify-center px-6">
        <Ionicons name="camera-outline" size={64} color="#9ca3af" />
        <Text className="text-white text-lg font-medium mt-4 text-center">
          Потрібен доступ до камери
        </Text>
        <TouchableOpacity
          onPress={requestPermission}
          className="mt-6 bg-primary-600 px-6 py-3 rounded-xl"
        >
          <Text className="text-white font-semibold">Надати доступ</Text>
        </TouchableOpacity>
      </SafeAreaView>
    );
  }

  // ── Main layout ────────────────────────────────────────────────────────────

  return (
    <SafeAreaView className="flex-1 bg-gray-900">
      {/* Top bar */}
      <View className="flex-row items-center px-4 py-3">
        <TouchableOpacity
          onPress={() => router.back()}
          className="w-10 h-10 bg-white/10 rounded-full items-center justify-center"
        >
          <Ionicons name="arrow-back" size={20} color="white" />
        </TouchableOpacity>
        <Text className="text-white text-lg font-bold ml-3">Сканування товару</Text>
        {loadingBarcode && (
          <ActivityIndicator color="white" style={{ marginLeft: 8 }} />
        )}
      </View>

      {/* Camera viewfinder — takes upper ~40% */}
      <View className="mx-4 rounded-2xl overflow-hidden" style={{ height: 260 }}>
        <CameraView
          className="flex-1"
          facing="back"
          onBarcodeScanned={scanning ? handleBarCodeScanned : undefined}
          barcodeScannerSettings={{
            barcodeTypes: ['ean8', 'ean13', 'qr', 'code128', 'upc_a', 'upc_e'],
          }}
        >
          {/* Corner frame */}
          <View className="flex-1 items-center justify-center">
            <View className="w-48 h-36 relative">
              <View className="absolute top-0 left-0 w-7 h-7 border-t-4 border-l-4 border-primary-500 rounded-tl-lg" />
              <View className="absolute top-0 right-0 w-7 h-7 border-t-4 border-r-4 border-primary-500 rounded-tr-lg" />
              <View className="absolute bottom-0 left-0 w-7 h-7 border-b-4 border-l-4 border-primary-500 rounded-bl-lg" />
              <View className="absolute bottom-0 right-0 w-7 h-7 border-b-4 border-r-4 border-primary-500 rounded-br-lg" />
            </View>
            <Text className="text-white/70 text-xs mt-3">
              {scanning ? 'Наведіть на штрихкод' : 'Зчитування...'}
            </Text>
          </View>
        </CameraView>
      </View>

      {/* Cart */}
      <View className="flex-1 bg-white rounded-t-3xl mt-3 overflow-hidden">
        {/* Cart header */}
        <View className="flex-row items-center justify-between px-5 pt-4 pb-2 border-b border-gray-100">
          <Text className="text-base font-bold text-gray-900">
            Кошик {cart.length > 0 ? `(${cart.length})` : ''}
          </Text>
          {cart.length > 0 && (
            <TouchableOpacity onPress={() => setCart([])}>
              <Text className="text-red-500 text-sm">Очистити</Text>
            </TouchableOpacity>
          )}
        </View>

        {cart.length === 0 ? (
          <View className="flex-1 items-center justify-center pb-10">
            <Ionicons name="cart-outline" size={48} color="#d1d5db" />
            <Text className="text-gray-400 mt-2">Відскануйте товар</Text>
          </View>
        ) : (
          <FlatList
            data={cart}
            keyExtractor={(item) => item.barcode}
            contentContainerStyle={{ paddingHorizontal: 20, paddingTop: 4, paddingBottom: 100 }}
            ItemSeparatorComponent={() => <View className="h-px bg-gray-100" />}
            renderItem={({ item }) => (
              <View className="py-3 flex-row items-center">
                {/* Name + critical badge */}
                <View className="flex-1 mr-3">
                  <View className="flex-row items-center gap-2">
                    <Text className="text-sm font-semibold text-gray-900 flex-shrink" numberOfLines={2}>
                      {item.productName}
                    </Text>
                    {item.isCritical && (
                      <View className="bg-amber-100 px-2 py-0.5 rounded">
                        <Text className="text-amber-700 text-xs font-bold">КРИТИЧНО</Text>
                      </View>
                    )}
                  </View>
                  <Text className="text-xs text-gray-400 mt-0.5">
                    {formatPrice(item.unitPrice)} ₴ × {item.quantity} = {formatPrice(item.unitPrice * item.quantity)} ₴
                  </Text>
                </View>

                {/* Qty controls */}
                <View className="flex-row items-center gap-1">
                  <TouchableOpacity
                    onPress={() => decreaseQty(item.barcode)}
                    className="w-8 h-8 bg-gray-100 rounded-lg items-center justify-center"
                  >
                    <Ionicons name="remove" size={16} color="#374151" />
                  </TouchableOpacity>
                  <Text className="text-sm font-bold text-gray-900 w-6 text-center">
                    {item.quantity}
                  </Text>
                  <TouchableOpacity
                    onPress={() => increaseQty(item.barcode)}
                    className="w-8 h-8 bg-gray-100 rounded-lg items-center justify-center"
                  >
                    <Ionicons name="add" size={16} color="#374151" />
                  </TouchableOpacity>
                  <TouchableOpacity
                    onPress={() => removeItem(item.barcode)}
                    className="w-8 h-8 bg-red-50 rounded-lg items-center justify-center ml-1"
                  >
                    <Ionicons name="trash-outline" size={15} color="#ef4444" />
                  </TouchableOpacity>
                </View>
              </View>
            )}
          />
        )}

        {/* Sticky footer */}
        <View className="absolute bottom-0 left-0 right-0 bg-white border-t border-gray-100 px-5 py-4">
          <View className="flex-row items-center justify-between mb-3">
            <Text className="text-base text-gray-600">Разом</Text>
            <Text className="text-xl font-bold text-gray-900">{formatPrice(subtotal)} ₴</Text>
          </View>
          <TouchableOpacity
            onPress={handleGoToPayment}
            disabled={cart.length === 0}
            className={`rounded-2xl py-4 items-center ${
              cart.length === 0 ? 'bg-gray-200' : 'bg-primary-600'
            }`}
          >
            <Text
              className={`font-bold text-base ${
                cart.length === 0 ? 'text-gray-400' : 'text-white'
              }`}
            >
              Перейти до оплати
            </Text>
          </TouchableOpacity>
        </View>
      </View>
    </SafeAreaView>
  );
}
