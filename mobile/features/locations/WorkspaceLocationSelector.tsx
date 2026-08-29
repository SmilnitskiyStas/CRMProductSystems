import { useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, Modal, Text, TouchableOpacity, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useAuthStore } from '@/features/auth/store';
import { useWorkspaceLocations } from './hooks';
import { useWorkspaceLocationStore } from './store';

export function WorkspaceLocationSelector() {
  const [open, setOpen] = useState(false);
  const assignedLocationId = useAuthStore((state) => state.user?.locationId ?? null);
  const locationsQuery = useWorkspaceLocations();
  const selectedLocationId = useWorkspaceLocationStore((state) => state.selectedLocationId);
  const selectLocation = useWorkspaceLocationStore((state) => state.selectLocation);
  const initializeLocation = useWorkspaceLocationStore((state) => state.initializeLocation);
  const locations = useMemo(() => locationsQuery.data ?? [], [locationsQuery.data]);

  useEffect(() => {
    if (!locationsQuery.isSuccess) return;
    const assignedIsAvailable = Boolean(
      assignedLocationId && locations.some((location) => location.id === assignedLocationId)
    );
    initializeLocation(assignedIsAvailable ? assignedLocationId : locations[0]?.id ?? null);
  }, [assignedLocationId, initializeLocation, locations, locationsQuery.isSuccess]);

  const selectedName = useMemo(
    () => locations.find((location) => location.id === selectedLocationId)?.name ?? 'Оберіть магазин',
    [locations, selectedLocationId]
  );

  return (
    <>
      <TouchableOpacity
        onPress={() => setOpen(true)}
        disabled={locationsQuery.isLoading}
        className="flex-row items-center bg-white border border-gray-200 rounded-xl px-3 py-2"
        accessibilityRole="button"
        accessibilityLabel={`Обраний магазин: ${selectedName}`}
      >
        {locationsQuery.isLoading ? (
          <ActivityIndicator size="small" color="#16a34a" />
        ) : (
          <Ionicons name="storefront-outline" size={18} color="#16a34a" />
        )}
        <Text className="flex-1 ml-2 text-sm font-semibold text-gray-800" numberOfLines={1}>{selectedName}</Text>
        <Ionicons name="chevron-down" size={16} color="#6b7280" />
      </TouchableOpacity>

      <Modal visible={open} animationType="slide" onRequestClose={() => setOpen(false)}>
        <SafeAreaView className="flex-1 bg-gray-50">
          <View className="flex-row items-center bg-white px-4 py-4 border-b border-gray-100">
            <TouchableOpacity onPress={() => setOpen(false)} accessibilityLabel="Закрити вибір магазину">
              <Ionicons name="close" size={24} color="#374151" />
            </TouchableOpacity>
            <Text className="ml-4 text-lg font-bold text-gray-900">Оберіть магазин</Text>
          </View>

          <View className="p-4 gap-3">
            <LocationOption
              name="Усі магазини"
              description="Зведені дані всіх доступних магазинів"
              selected={selectedLocationId === null}
              onPress={() => { selectLocation(null); setOpen(false); }}
            />
            {locations.map((location) => (
              <LocationOption
                key={location.id}
                name={location.name}
                description={location.address}
                selected={selectedLocationId === location.id}
                onPress={() => { selectLocation(location.id); setOpen(false); }}
              />
            ))}
          </View>
        </SafeAreaView>
      </Modal>
    </>
  );
}

function LocationOption({
  name,
  description,
  selected,
  onPress,
}: {
  name: string;
  description: string | null;
  selected: boolean;
  onPress: () => void;
}) {
  return (
    <TouchableOpacity
      onPress={onPress}
      className={`bg-white border rounded-2xl p-4 flex-row items-center ${selected ? 'border-primary-500' : 'border-gray-100'}`}
    >
      <View className={`w-10 h-10 rounded-xl items-center justify-center ${selected ? 'bg-green-100' : 'bg-gray-100'}`}>
        <Ionicons name={selected ? 'checkmark' : 'storefront-outline'} size={20} color={selected ? '#16a34a' : '#6b7280'} />
      </View>
      <View className="flex-1 ml-3">
        <Text className="font-semibold text-gray-900">{name}</Text>
        {description ? <Text className="text-xs text-gray-500 mt-1">{description}</Text> : null}
      </View>
    </TouchableOpacity>
  );
}
