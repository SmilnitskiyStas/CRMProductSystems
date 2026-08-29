import { useState } from 'react';
import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { cssInterop } from 'nativewind';
import {
  ActivityIndicator,
  Linking,
  Modal,
  Pressable,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { findConsumerProductByBarcode } from '@/features/shopping/products';
import type { NewsPromotionProduct } from '@/features/loyalty/news';
import { parseRetailerInvite } from '@/features/retailer-onboarding/invite';
import { useSelectedConsumerContext } from '@/features/consumer-content/hooks';
import { recordConsumerCatalogEvent } from '@/features/consumer-content/api';

cssInterop(CameraView, { className: 'style' });

function formatPrice(value: number | null) {
  if (value === null) return '—';
  return value.toLocaleString('uk-UA', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

export default function ConsumerBarcodeScannerScreen() {
  const router = useRouter();
  const { context } = useSelectedConsumerContext();
  const [permission, requestPermission] = useCameraPermissions();
  const [consentVisible, setConsentVisible] = useState(true);
  const [requestingPermission, setRequestingPermission] = useState(false);
  const [cameraReady, setCameraReady] = useState(false);
  const [scannedBarcode, setScannedBarcode] = useState<string | null>(null);
  const [product, setProduct] = useState<NewsPromotionProduct | null>(null);

  const cameraAllowed = permission?.granted === true;
  const shouldExplainPermission =
    permission !== null && !permission.granted && permission.canAskAgain;
  const permissionBlocked =
    permission !== null && !permission.granted && !permission.canAskAgain;

  async function allowCamera() {
    setRequestingPermission(true);
    try {
      await requestPermission();
      setConsentVisible(false);
    } finally {
      setRequestingPermission(false);
    }
  }

  function handleScan(data: string) {
    const normalized = data.trim();
    if (!normalized || scannedBarcode) return;
    setScannedBarcode(normalized);
    if (parseRetailerInvite(normalized)) {
      router.replace({ pathname: '/(personal)/retailer-onboarding', params: { code: normalized } });
      return;
    }
    const matchedProduct = findConsumerProductByBarcode(normalized) ?? null;
    setProduct(matchedProduct);
    if (context && matchedProduct?.catalogId) void recordConsumerCatalogEvent(context, { catalogId: matchedProduct.catalogId, eventType: 'product_scan', productId: matchedProduct.id });
  }

  function scanAgain() {
    setScannedBarcode(null);
    setProduct(null);
  }

  if (!permission) {
    return (
      <SafeAreaView className="flex-1 items-center justify-center bg-gray-950">
        <ActivityIndicator size="large" color="white" />
        <Text className="mt-3 text-sm text-gray-400">Готуємо сканер…</Text>
      </SafeAreaView>
    );
  }

  if (!cameraAllowed) {
    return (
      <SafeAreaView className="flex-1 bg-gray-950" edges={['top', 'left', 'right']}>
        <View className="flex-row items-center px-4 py-2">
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Повернутися назад"
            onPress={() => router.back()}
            className="h-11 w-11 items-center justify-center rounded-full bg-white/10"
          >
            <Ionicons name="arrow-back" size={22} color="white" />
          </Pressable>
          <Text className="ml-3 text-lg font-bold text-white">Сканування товару</Text>
        </View>

        <View className="flex-1 items-center justify-center px-6">
          <View className="h-24 w-24 items-center justify-center rounded-[32px] bg-white/10">
            <Ionicons name="barcode-outline" size={46} color="#86efac" />
          </View>
          <Text className="mt-7 text-center text-2xl font-bold text-white">
            Камера потрібна для сканування
          </Text>
          <Text className="mt-3 max-w-[320px] text-center text-sm leading-6 text-gray-400">
            Наведіть камеру на штрихкод, і ми покажемо інформацію про товар, ціну та наявну знижку.
          </Text>

          {permissionBlocked ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Відкрити налаштування застосунку"
              onPress={() => void Linking.openSettings()}
              className="mt-7 w-full max-w-[320px] items-center rounded-2xl bg-green-600 py-4"
            >
              <Text className="font-bold text-white">Відкрити налаштування</Text>
            </Pressable>
          ) : (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Надати доступ до камери"
              onPress={() => setConsentVisible(true)}
              className="mt-7 w-full max-w-[320px] items-center rounded-2xl bg-green-600 py-4"
            >
              <Text className="font-bold text-white">Увімкнути камеру</Text>
            </Pressable>
          )}
        </View>

        <Modal
          visible={consentVisible && shouldExplainPermission}
          transparent
          animationType="fade"
          statusBarTranslucent
          onRequestClose={() => {
            setConsentVisible(false);
            router.back();
          }}
        >
          <View className="flex-1 items-center justify-center bg-black/60 px-5">
            <View className="w-full max-w-[420px] overflow-hidden rounded-3xl bg-white">
              <View className="relative items-center overflow-hidden bg-green-700 px-6 pb-7 pt-8">
                <View className="absolute -right-12 -top-16 h-40 w-40 rounded-full bg-white/10" />
                <View className="absolute -bottom-14 -left-10 h-32 w-32 rounded-full bg-green-400/20" />
                <View className="h-16 w-16 items-center justify-center rounded-3xl bg-white/15">
                  <Ionicons name="camera" size={31} color="white" />
                </View>
                <Text className="mt-5 text-center text-2xl font-bold text-white">
                  Дозволити доступ до камери?
                </Text>
                <Text className="mt-2 text-center text-sm leading-6 text-green-100">
                  Камера допоможе швидко знайти товар за штрихкодом і перевірити його актуальну ціну.
                </Text>
              </View>

              <View className="px-5 pb-5 pt-5">
                <View className="flex-row items-start rounded-2xl bg-gray-50 p-4">
                  <View className="h-9 w-9 items-center justify-center rounded-xl bg-green-100">
                    <Ionicons name="shield-checkmark-outline" size={19} color="#15803d" />
                  </View>
                  <View className="ml-3 flex-1">
                    <Text className="text-sm font-bold text-gray-900">Без фото та запису відео</Text>
                    <Text className="mt-1 text-xs leading-5 text-gray-500">
                      Застосунок лише зчитує код у кадрі. Зображення не фотографуються, не зберігаються та не надсилаються.
                    </Text>
                  </View>
                </View>

                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel="Дозволити використання камери"
                  disabled={requestingPermission}
                  onPress={() => void allowCamera()}
                  className="mt-5 flex-row items-center justify-center rounded-2xl bg-green-700 py-4"
                >
                  {requestingPermission ? (
                    <ActivityIndicator size="small" color="white" />
                  ) : (
                    <>
                      <Ionicons name="camera-outline" size={20} color="white" />
                      <Text className="ml-2 text-base font-bold text-white">Дозволити</Text>
                    </>
                  )}
                </Pressable>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel="Не використовувати камеру зараз"
                  disabled={requestingPermission}
                  onPress={() => {
                    setConsentVisible(false);
                    router.back();
                  }}
                  className="mt-2 items-center rounded-2xl py-3.5"
                >
                  <Text className="text-sm font-semibold text-gray-500">Не зараз</Text>
                </Pressable>
              </View>
            </View>
          </View>
        </Modal>
      </SafeAreaView>
    );
  }

  return (
    <View className="flex-1 bg-black">
      <CameraView
        style={{ flex: 1 }}
        facing="back"
        onCameraReady={() => setCameraReady(true)}
        onBarcodeScanned={
          scannedBarcode
            ? undefined
            : ({ data }: { data: string }) => handleScan(data)
        }
        barcodeScannerSettings={{
          barcodeTypes: ['ean8', 'ean13', 'qr', 'code128', 'upc_a', 'upc_e'],
        }}
      >
        <SafeAreaView className="flex-1">
          <View className="flex-row items-center px-4 py-2">
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Закрити сканер"
              onPress={() => router.back()}
              className="h-11 w-11 items-center justify-center rounded-full bg-black/50"
            >
              <Ionicons name="close" size={23} color="white" />
            </Pressable>
            <Text className="ml-3 flex-1 text-lg font-bold text-white">Сканування товару</Text>
          </View>

          <View className="flex-1 items-center justify-center px-8">
            <View className="h-56 w-full max-w-[300px]">
              <View className="absolute left-0 top-0 h-10 w-10 rounded-tl-xl border-l-4 border-t-4 border-green-400" />
              <View className="absolute right-0 top-0 h-10 w-10 rounded-tr-xl border-r-4 border-t-4 border-green-400" />
              <View className="absolute bottom-0 left-0 h-10 w-10 rounded-bl-xl border-b-4 border-l-4 border-green-400" />
              <View className="absolute bottom-0 right-0 h-10 w-10 rounded-br-xl border-b-4 border-r-4 border-green-400" />
              <View className="absolute left-5 right-5 top-1/2 h-0.5 bg-green-400" />
            </View>
            <Text className="mt-7 text-center text-sm font-medium text-white">
              {cameraReady ? 'Наведіть камеру на штрихкод' : 'Запускаємо камеру…'}
            </Text>
            <Text className="mt-2 text-center text-xs text-white/60">
              Код буде зчитано автоматично
            </Text>
          </View>
        </SafeAreaView>
      </CameraView>

      <Modal visible={scannedBarcode !== null} transparent animationType="slide">
        <View className="flex-1 justify-end bg-black/40">
          <View className="rounded-t-3xl bg-white px-5 pb-8 pt-4">
            <View className="mb-4 h-1 w-10 self-center rounded-full bg-gray-300" />
            {product ? (
              <>
                <View className="flex-row">
                  <View
                    className="h-24 w-24 items-center justify-center rounded-2xl"
                    style={{ backgroundColor: product.background }}
                  >
                    <Ionicons name={product.icon} size={42} color="#374151" />
                  </View>
                  <View className="ml-4 flex-1">
                    <View className="self-start rounded-full bg-green-100 px-2.5 py-1">
                      <Text className="text-[11px] font-bold text-green-800">
                        Знижка підтверджена
                      </Text>
                    </View>
                    <Text className="mt-2 text-xl font-bold text-gray-900">{product.name}</Text>
                    <Text className="mt-1 text-sm text-gray-500">{product.unit}</Text>
                  </View>
                </View>
                <View className="mt-5 flex-row items-end rounded-2xl bg-green-50 p-4">
                  <View className="flex-1">
                    <Text className="text-xs text-green-700">Ціна зі знижкою</Text>
                    <Text className="mt-1 text-2xl font-bold text-green-800">
                      {formatPrice(product.appPrice)} ₴
                    </Text>
                  </View>
                  <View className="items-end">
                    <Text className="text-sm text-gray-400 line-through">
                      {formatPrice(product.regularPrice)} ₴
                    </Text>
                    <Text className="mt-1 font-bold text-red-500">
                      −{product.discountPercent}%
                    </Text>
                  </View>
                </View>
                <Pressable
                  onPress={() => {
                    const productId = product.id;
                    scanAgain();
                    router.replace({
                      pathname: '/(personal)/product/[id]',
                      params: { id: productId },
                    });
                  }}
                  className="mt-5 items-center rounded-2xl bg-green-700 py-4"
                >
                  <Text className="font-bold text-white">Переглянути інформацію про товар</Text>
                </Pressable>
              </>
            ) : (
              <View className="items-center py-4">
                <View className="h-16 w-16 items-center justify-center rounded-3xl bg-gray-100">
                  <Ionicons name="help-outline" size={32} color="#9ca3af" />
                </View>
                <Text className="mt-4 text-xl font-bold text-gray-900">Товар не знайдено</Text>
                <Text className="mt-2 text-center text-sm leading-6 text-gray-500">
                  Для цього штрихкоду немає товару в каталозі вибраного магазину.
                </Text>
                <Text className="mt-2 text-xs text-gray-400">Код: {scannedBarcode}</Text>
              </View>
            )}
            <Pressable
              onPress={scanAgain}
              className="mt-3 items-center rounded-2xl bg-gray-100 py-4"
            >
              <Text className="font-bold text-gray-700">Сканувати ще раз</Text>
            </Pressable>
          </View>
        </View>
      </Modal>
    </View>
  );
}
