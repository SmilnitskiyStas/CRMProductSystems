import { useRef, useState } from 'react';
import { View, Text, Image, ScrollView, ActivityIndicator, TouchableOpacity, Alert } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { CameraView, useCameraPermissions } from 'expo-camera';
import * as ImagePicker from 'expo-image-picker';
import { cssInterop } from 'nativewind';
import { useCreateShelfScan } from '@/features/shelf-check/hooks/useShelfCheck';
import { shelfScanErrorMessage } from '@/features/shelf-check/api/shelfCheckApi';
import { prepareShelfPhoto } from '@/features/shelf-check/prepareImage';
import { ScanResultView } from '@/features/shelf-check/components/ScanResultView';
import type { PreparedPhoto, ShelfScan } from '@/features/shelf-check/types';

cssInterop(CameraView, { className: 'style' });

export default function ShelfCheckCaptureScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{
    locationId: string; zoneId: string; zoneName?: string; shelfId?: string; shelfLabel?: string;
  }>();
  const [permission, requestPermission] = useCameraPermissions();
  const cameraRef = useRef<CameraView>(null);
  const [cameraReady, setCameraReady] = useState(false);
  const [busy, setBusy] = useState(false);
  const [photo, setPhoto] = useState<PreparedPhoto | null>(null);
  const [result, setResult] = useState<ShelfScan | null>(null);
  const [error, setError] = useState<string | null>(null);
  const create = useCreateShelfScan();

  async function takePhoto() {
    if (!cameraRef.current || busy) return;
    setBusy(true);
    try {
      const shot = await cameraRef.current.takePictureAsync({ quality: 0.7, skipProcessing: false });
      setPhoto(await prepareShelfPhoto(shot.uri, shot.width, shot.height));
      setError(null);
    } catch {
      Alert.alert('Помилка', 'Не вдалося зробити фото. Спробуйте ще раз.');
    } finally {
      setBusy(false);
    }
  }

  async function pickFromGallery() {
    if (busy) return;
    setBusy(true);
    try {
      const picked = await ImagePicker.launchImageLibraryAsync({ mediaTypes: ['images'], quality: 1 });
      if (picked.canceled || !picked.assets[0]) return;
      const asset = picked.assets[0];
      setPhoto(await prepareShelfPhoto(asset.uri, asset.width, asset.height));
      setError(null);
    } catch {
      Alert.alert('Помилка', 'Не вдалося обрати фото.');
    } finally {
      setBusy(false);
    }
  }

  function send() {
    if (!photo) return;
    setError(null);
    create.mutate(
      {
        locationId: params.locationId,
        zoneId: params.zoneId,
        shelfId: params.shelfId || null,
        photoUri: photo.uri,
      },
      {
        onSuccess: (scan) => setResult(scan),
        onError: (e) => setError(shelfScanErrorMessage(e)),
      },
    );
  }

  const header = (
    <View className="px-4 pt-4 pb-2 flex-row items-center gap-3">
      <TouchableOpacity
        onPress={() => router.back()}
        className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
        accessibilityLabel="Назад"
      >
        <Ionicons name="arrow-back" size={20} color="#374151" />
      </TouchableOpacity>
      <View className="flex-1">
        <Text className="text-xl font-bold text-gray-900">{params.shelfLabel || 'Вся зона'}</Text>
        {params.zoneName ? <Text className="text-xs text-gray-500">{params.zoneName}</Text> : null}
      </View>
    </View>
  );

  // Result
  if (result) {
    return (
      <SafeAreaView className="flex-1 bg-gray-50">
        {header}
        <ScrollView className="flex-1 px-4" contentContainerStyle={{ paddingBottom: 32 }}>
          <ScanResultView scan={result} />
          <TouchableOpacity
            onPress={() => router.back()}
            className="mt-6 bg-primary-600 rounded-2xl py-4 items-center"
            accessibilityRole="button"
          >
            <Text className="text-white font-semibold text-base">Готово</Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={() => { setResult(null); setPhoto(null); }}
            className="mt-3 bg-white border border-gray-200 rounded-2xl py-4 items-center"
            accessibilityRole="button"
          >
            <Text className="text-gray-700 font-medium text-base">Нова перевірка цієї полиці</Text>
          </TouchableOpacity>
        </ScrollView>
      </SafeAreaView>
    );
  }

  // Preview / confirm
  if (photo) {
    return (
      <SafeAreaView className="flex-1 bg-gray-50">
        {header}
        <View className="flex-1 px-4">
          <Image source={{ uri: photo.uri }} className="flex-1 rounded-2xl bg-black" resizeMode="contain" />
          {error ? <Text className="text-sm text-red-600 mt-3">{error}</Text> : null}
          {create.isPending ? (
            <View className="mt-4 mb-4 flex-row items-center justify-center gap-3 py-4">
              <ActivityIndicator color="#16a34a" />
              <Text className="text-gray-600">Аналізуємо фото, це може зайняти до хвилини…</Text>
            </View>
          ) : (
            <View className="flex-row gap-3 mt-4 mb-4">
              <TouchableOpacity
                onPress={() => { setPhoto(null); setError(null); }}
                className="flex-1 bg-white border border-gray-200 rounded-2xl py-4 items-center"
                accessibilityRole="button"
              >
                <Text className="text-gray-700 font-medium text-base">Перезняти</Text>
              </TouchableOpacity>
              <TouchableOpacity
                onPress={send}
                className="flex-1 bg-primary-600 rounded-2xl py-4 items-center"
                accessibilityRole="button"
              >
                <Text className="text-white font-semibold text-base">{error ? 'Повторити' : 'Надіслати'}</Text>
              </TouchableOpacity>
            </View>
          )}
        </View>
      </SafeAreaView>
    );
  }

  // Camera
  if (!permission) {
    return (
      <SafeAreaView className="flex-1 bg-gray-50 items-center justify-center">
        <ActivityIndicator color="#16a34a" />
      </SafeAreaView>
    );
  }

  if (!permission.granted) {
    return (
      <SafeAreaView className="flex-1 bg-gray-50">
        {header}
        <View className="flex-1 items-center justify-center px-6 gap-4">
          <Text className="text-gray-600 text-center">Для фото полиці потрібен доступ до камери.</Text>
          <TouchableOpacity
            onPress={() => { void requestPermission(); }}
            className="bg-primary-600 rounded-2xl py-4 px-8"
            accessibilityRole="button"
          >
            <Text className="text-white font-semibold">Дозволити камеру</Text>
          </TouchableOpacity>
          <TouchableOpacity onPress={() => { void pickFromGallery(); }} className="p-3">
            <Text className="text-primary-600 font-medium">Обрати з галереї</Text>
          </TouchableOpacity>
        </View>
      </SafeAreaView>
    );
  }

  return (
    <View className="flex-1 bg-black">
      <CameraView
        ref={cameraRef}
        className="flex-1"
        facing="back"
        onCameraReady={() => setCameraReady(true)}
      >
        <SafeAreaView className="flex-1 justify-between">
          <View className="flex-row items-center justify-between px-4 pt-2">
            <TouchableOpacity
              onPress={() => router.back()}
              className="w-11 h-11 bg-black/40 rounded-full items-center justify-center"
              accessibilityLabel="Закрити"
            >
              <Ionicons name="close" size={24} color="white" />
            </TouchableOpacity>
            <View className="bg-black/40 rounded-full px-3 py-1.5">
              <Text className="text-white text-xs">{params.shelfLabel || 'Вся зона'}</Text>
            </View>
          </View>
          <View className="flex-row items-center justify-around pb-8">
            <TouchableOpacity
              onPress={() => { void pickFromGallery(); }}
              className="w-14 h-14 bg-black/40 rounded-full items-center justify-center"
              accessibilityLabel="Обрати з галереї"
            >
              <Ionicons name="images-outline" size={26} color="white" />
            </TouchableOpacity>
            <TouchableOpacity
              onPress={() => { void takePhoto(); }}
              disabled={!cameraReady || busy}
              className="w-20 h-20 rounded-full border-4 border-white items-center justify-center"
              accessibilityLabel="Зробити фото"
            >
              {busy ? <ActivityIndicator color="white" /> : <View className="w-14 h-14 rounded-full bg-white" />}
            </TouchableOpacity>
            <View className="w-14 h-14" />
          </View>
        </SafeAreaView>
      </CameraView>
    </View>
  );
}
