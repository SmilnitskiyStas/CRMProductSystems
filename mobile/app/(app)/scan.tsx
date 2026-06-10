import { useState, useRef } from 'react';
import { View, Text, TouchableOpacity, Modal, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { CameraView, useCameraPermissions } from 'expo-camera';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { getProductByBarcode } from '@/features/stock/api/stockApi';

export default function ScanScreen() {
  const router = useRouter();
  const [permission, requestPermission] = useCameraPermissions();
  const [scanned, setScanned] = useState(false);
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<{ name: string; id: string } | null>(null);

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

  const handleBarCodeScanned = async ({ data }: { data: string }) => {
    if (scanned || loading) return;
    setScanned(true);
    setLoading(true);
    try {
      const product = await getProductByBarcode(data);
      setResult({ name: product.name, id: product.id });
    } catch {
      setResult(null);
    } finally {
      setLoading(false);
    }
  };

  return (
    <View className="flex-1 bg-black">
      <CameraView
        className="flex-1"
        facing="back"
        onBarcodeScanned={scanned ? undefined : handleBarCodeScanned}
        barcodeScannerSettings={{
          barcodeTypes: ['ean8', 'ean13', 'qr', 'code128'],
        }}
      >
        {/* Overlay */}
        <SafeAreaView className="flex-1">
          <TouchableOpacity
            onPress={() => router.back()}
            className="m-4 w-10 h-10 bg-black/40 rounded-full items-center justify-center"
          >
            <Ionicons name="close" size={22} color="white" />
          </TouchableOpacity>

          {/* Scanner frame */}
          <View className="flex-1 items-center justify-center">
            <View className="w-64 h-64 relative">
              {/* Corner indicators */}
              <View className="absolute top-0 left-0 w-8 h-8 border-t-4 border-l-4 border-primary-500 rounded-tl-lg" />
              <View className="absolute top-0 right-0 w-8 h-8 border-t-4 border-r-4 border-primary-500 rounded-tr-lg" />
              <View className="absolute bottom-0 left-0 w-8 h-8 border-b-4 border-l-4 border-primary-500 rounded-bl-lg" />
              <View className="absolute bottom-0 right-0 w-8 h-8 border-b-4 border-r-4 border-primary-500 rounded-br-lg" />
            </View>
            <Text className="text-white/70 text-sm mt-6">
              Наведіть на штрихкод або QR-код
            </Text>
          </View>
        </SafeAreaView>
      </CameraView>

      {/* Result bottom sheet */}
      <Modal visible={scanned} transparent animationType="slide">
        <TouchableOpacity
          activeOpacity={1}
          onPress={() => setScanned(false)}
          className="flex-1 justify-end bg-black/50"
        >
          <View className="bg-white rounded-t-3xl p-6">
            {loading ? (
              <View className="items-center py-6">
                <ActivityIndicator size="large" color="#16a34a" />
                <Text className="text-gray-500 mt-3">Пошук товару...</Text>
              </View>
            ) : result ? (
              <>
                <Text className="text-lg font-bold text-gray-900">{result.name}</Text>
                <View className="flex-row gap-3 mt-6">
                  <TouchableOpacity
                    onPress={() => {
                      setScanned(false);
                      router.push(`/(app)/stock/${result.id}`);
                    }}
                    className="flex-1 bg-primary-600 py-3.5 rounded-xl items-center"
                  >
                    <Text className="text-white font-semibold">Переглянути залишки</Text>
                  </TouchableOpacity>
                </View>
                <TouchableOpacity
                  onPress={() => setScanned(false)}
                  className="mt-3 py-3 items-center"
                >
                  <Text className="text-gray-500">Сканувати знову</Text>
                </TouchableOpacity>
              </>
            ) : (
              <>
                <Text className="text-lg font-bold text-gray-900">Товар не знайдено</Text>
                <Text className="text-gray-500 mt-1">Перевірте штрихкод або додайте товар вручну</Text>
                <TouchableOpacity
                  onPress={() => setScanned(false)}
                  className="mt-6 py-3.5 bg-gray-100 rounded-xl items-center"
                >
                  <Text className="text-gray-700 font-semibold">Спробувати знову</Text>
                </TouchableOpacity>
              </>
            )}
          </View>
        </TouchableOpacity>
      </Modal>
    </View>
  );
}
