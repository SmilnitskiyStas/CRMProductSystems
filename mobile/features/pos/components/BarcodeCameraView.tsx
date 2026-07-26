import { ActivityIndicator, Text, TouchableOpacity, View } from 'react-native';
import { CameraView, useCameraPermissions, type BarcodeType } from 'expo-camera';
import { Ionicons } from '@expo/vector-icons';
import { cssInterop } from 'nativewind';

cssInterop(CameraView, { className: 'style' });

interface Props {
  barcodeTypes: BarcodeType[];
  scanning: boolean;
  onScan: (data: string) => void;
  caption?: string;
  height?: number;
}

/**
 * Minimal shared camera+permission chunk (TASK-407) — extracted for pos/loyalty.tsx rather
 * than duplicating pos/scanner.tsx's whole file. pos/scanner.tsx itself doesn't expose any
 * reusable piece (its camera markup is inlined in the default-export component) and is
 * intentionally left untouched here since it was already audited/fixed in TASK-366; a
 * future cleanup pass could migrate it to use this component too.
 */
export function BarcodeCameraView({ barcodeTypes, scanning, onScan, caption, height = 260 }: Props) {
  const [permission, requestPermission] = useCameraPermissions();

  if (!permission) {
    return (
      <View className="items-center justify-center py-10">
        <ActivityIndicator color="white" />
      </View>
    );
  }

  if (!permission.granted) {
    return (
      <View className="items-center justify-center py-10 px-6">
        <Ionicons name="camera-outline" size={48} color="#9ca3af" />
        <Text className="text-white text-base font-medium mt-3 text-center">
          Потрібен доступ до камери
        </Text>
        <TouchableOpacity onPress={requestPermission} className="mt-4 bg-primary-600 px-5 py-3 rounded-xl">
          <Text className="text-white font-semibold">Надати доступ</Text>
        </TouchableOpacity>
      </View>
    );
  }

  return (
    <View className="rounded-2xl overflow-hidden" style={{ height }}>
      <CameraView
        className="flex-1"
        facing="back"
        onBarcodeScanned={scanning ? ({ data }: { data: string }) => onScan(data) : undefined}
        barcodeScannerSettings={{ barcodeTypes }}
      >
        <View className="flex-1 items-center justify-center">
          <View className="w-48 h-36 relative">
            <View className="absolute top-0 left-0 w-7 h-7 border-t-4 border-l-4 border-primary-500 rounded-tl-lg" />
            <View className="absolute top-0 right-0 w-7 h-7 border-t-4 border-r-4 border-primary-500 rounded-tr-lg" />
            <View className="absolute bottom-0 left-0 w-7 h-7 border-b-4 border-l-4 border-primary-500 rounded-bl-lg" />
            <View className="absolute bottom-0 right-0 w-7 h-7 border-b-4 border-r-4 border-primary-500 rounded-br-lg" />
          </View>
          {caption ? <Text className="text-white/70 text-xs mt-3">{caption}</Text> : null}
        </View>
      </CameraView>
    </View>
  );
}
