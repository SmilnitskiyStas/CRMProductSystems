import { useMemo, useState } from 'react';
import { View, Text, ScrollView, ActivityIndicator, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import { useWorkspaceLocationStore } from '@/features/locations/store';
import {
  useShelfCheckLocations,
  useZoneScans,
  useZoneShelves,
} from '@/features/shelf-check/hooks/useShelfCheck';
import { ScanResultView, formatScanDate } from '@/features/shelf-check/components/ScanResultView';
import { AT_LEAST_STORE_MANAGER_OR_PROVIDER, hasRole } from '@/lib/roles';

function Chip({ label, active, onPress }: { label: string; active: boolean; onPress: () => void }) {
  return (
    <TouchableOpacity
      onPress={onPress}
      activeOpacity={0.7}
      accessibilityRole="button"
      className={`px-4 py-3 rounded-xl border mr-2 mb-2 ${
        active ? 'bg-primary-600 border-primary-600' : 'bg-white border-gray-200'
      }`}
    >
      <Text className={`text-sm font-medium ${active ? 'text-white' : 'text-gray-700'}`}>{label}</Text>
    </TouchableOpacity>
  );
}

function SectionTitle({ children }: { children: string }) {
  return <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2 mt-5">{children}</Text>;
}

export default function ShelfCheckScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const canScan = hasRole(user?.role, AT_LEAST_STORE_MANAGER_OR_PROVIDER);
  const workspaceLocationId = useWorkspaceLocationStore((s) => s.selectedLocationId);

  const { data: locations, isLoading, isError, refetch } = useShelfCheckLocations();
  const [pickedLocationId, setPickedLocationId] = useState<string | null>(null);
  const [zoneId, setZoneId] = useState<string | null>(null);
  const [shelfId, setShelfId] = useState<string | null>(null); // null = whole zone
  const [expandedScanId, setExpandedScanId] = useState<string | null>(null);

  const locationId = useMemo(() => {
    if (!locations?.length) return null;
    const candidates = [pickedLocationId, workspaceLocationId ?? undefined, user?.locationId ?? undefined];
    for (const id of candidates) {
      if (id && locations.some((l) => l.id === id)) return id;
    }
    return locations.length === 1 ? locations[0].id : null;
  }, [locations, pickedLocationId, workspaceLocationId, user?.locationId]);

  const location = locations?.find((l) => l.id === locationId) ?? null;
  const zones = location?.zones ?? [];
  const zone = zones.find((z) => z.id === zoneId) ?? null;

  const shelvesQuery = useZoneShelves(locationId ?? undefined, zone?.id);
  const scansQuery = useZoneScans(locationId ?? undefined, zone?.id);
  const shelves = shelvesQuery.data ?? [];
  const selectedShelf = shelves.find((s) => s.shelfId === shelfId) ?? null;
  const noPlanogram = !!zone && !shelvesQuery.isLoading && !shelvesQuery.isError && shelves.length === 0;

  function startCapture() {
    if (!locationId || !zone) return;
    router.push({
      pathname: '/(app)/shelf-check/capture',
      params: {
        locationId,
        zoneId: zone.id,
        zoneName: zone.name,
        shelfId: selectedShelf?.shelfId ?? '',
        shelfLabel: selectedShelf?.label ?? 'Вся зона',
      },
    });
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="px-4 pt-4 pb-2 flex-row items-center gap-3">
        <TouchableOpacity
          onPress={() => router.back()}
          className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
          accessibilityLabel="Назад"
        >
          <Ionicons name="arrow-back" size={20} color="#374151" />
        </TouchableOpacity>
        <Text className="text-2xl font-bold text-gray-900">Перевірка полиць</Text>
      </View>

      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : isError ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-red-500 text-center">Помилка завантаження магазинів</Text>
          <TouchableOpacity onPress={() => { void refetch(); }} className="mt-4 p-2">
            <Text className="text-primary-600 font-medium">Спробувати знову</Text>
          </TouchableOpacity>
        </View>
      ) : !locations?.length ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-gray-500 text-center">Немає доступних магазинів</Text>
        </View>
      ) : (
        <ScrollView className="flex-1 px-4" contentContainerStyle={{ paddingBottom: 32 }}>
          {!canScan ? (
            <View className="bg-amber-50 border border-amber-200 rounded-xl p-3 mt-2">
              <Text className="text-xs text-amber-800">
                Нові перевірки доступні менеджеру магазину. Ви можете переглядати історію.
              </Text>
            </View>
          ) : null}

          <SectionTitle>1. Магазин</SectionTitle>
          <View className="flex-row flex-wrap">
            {locations.map((l) => (
              <Chip
                key={l.id}
                label={l.name}
                active={l.id === locationId}
                onPress={() => { setPickedLocationId(l.id); setZoneId(null); setShelfId(null); }}
              />
            ))}
          </View>

          {location ? (
            <>
              <SectionTitle>2. Зона</SectionTitle>
              {zones.length === 0 ? (
                <Text className="text-sm text-gray-500">У цьому магазині немає зон</Text>
              ) : (
                <View className="flex-row flex-wrap">
                  {zones.map((z) => (
                    <Chip
                      key={z.id}
                      label={z.name}
                      active={z.id === zoneId}
                      onPress={() => { setZoneId(z.id); setShelfId(null); setExpandedScanId(null); }}
                    />
                  ))}
                </View>
              )}
            </>
          ) : null}

          {zone ? (
            <>
              <SectionTitle>3. Полиця</SectionTitle>
              {shelvesQuery.isLoading ? (
                <ActivityIndicator color="#16a34a" />
              ) : shelvesQuery.isError ? (
                <TouchableOpacity onPress={() => { void shelvesQuery.refetch(); }} className="p-2">
                  <Text className="text-red-500">Помилка завантаження полиць. Повторити</Text>
                </TouchableOpacity>
              ) : noPlanogram ? (
                <Text className="text-sm text-gray-500">
                  Для цієї зони не задано планограму. Додайте товари на полиці у веб-версії.
                </Text>
              ) : (
                <View className="flex-row flex-wrap">
                  <Chip label="Вся зона" active={shelfId === null} onPress={() => setShelfId(null)} />
                  {shelves.map((s) => (
                    <Chip
                      key={s.shelfId}
                      label={`${s.label} (${s.items.length})`}
                      active={s.shelfId === shelfId}
                      onPress={() => setShelfId(s.shelfId)}
                    />
                  ))}
                </View>
              )}

              {canScan && !noPlanogram && !shelvesQuery.isLoading ? (
                <TouchableOpacity
                  onPress={startCapture}
                  activeOpacity={0.8}
                  accessibilityRole="button"
                  className="mt-4 bg-primary-600 rounded-2xl py-4 flex-row items-center justify-center gap-2"
                >
                  <Ionicons name="camera-outline" size={22} color="white" />
                  <Text className="text-white text-base font-semibold">Сфотографувати полицю</Text>
                </TouchableOpacity>
              ) : null}

              <SectionTitle>Остання історія перевірок</SectionTitle>
              {scansQuery.isLoading ? (
                <ActivityIndicator color="#16a34a" />
              ) : scansQuery.isError ? (
                <TouchableOpacity onPress={() => { void scansQuery.refetch(); }} className="p-2">
                  <Text className="text-red-500">Помилка завантаження історії. Повторити</Text>
                </TouchableOpacity>
              ) : !scansQuery.data?.length ? (
                <Text className="text-sm text-gray-500">Перевірок ще не було</Text>
              ) : (
                scansQuery.data.map((scan) => {
                  const open = expandedScanId === scan.id;
                  const shelfLabel = shelves.find((s) => s.shelfId === scan.shelfId)?.label ?? 'Вся зона';
                  return (
                    <View key={scan.id} className="bg-white rounded-2xl p-4 mb-3">
                      <TouchableOpacity
                        onPress={() => setExpandedScanId(open ? null : scan.id)}
                        activeOpacity={0.7}
                        className="flex-row items-center justify-between"
                      >
                        <View className="flex-1">
                          <Text className="text-sm font-semibold text-gray-900">{shelfLabel}</Text>
                          <Text className="text-xs text-gray-500 mt-0.5">{formatScanDate(scan.createdAt)}</Text>
                        </View>
                        <Text className="text-xs font-semibold text-red-600 mr-2">Немає: {scan.missingCount}</Text>
                        <Text className="text-xs font-semibold text-amber-600 mr-2">Мало: {scan.lowCount}</Text>
                        <Ionicons name={open ? 'chevron-up' : 'chevron-down'} size={16} color="#9ca3af" />
                      </TouchableOpacity>
                      {open ? <View className="mt-3"><ScanResultView scan={scan} /></View> : null}
                    </View>
                  );
                })
              )}
            </>
          ) : null}
        </ScrollView>
      )}
    </SafeAreaView>
  );
}
