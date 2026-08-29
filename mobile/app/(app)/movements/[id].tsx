import { ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Stack, useLocalSearchParams } from 'expo-router';
import { MOVEMENT_LABELS, MOVEMENT_REFERENCE_LABELS, movementNumber, type RecentMovement } from '@/features/dashboard/types';
import { useMovementProduct } from '@/features/dashboard/hooks/useDashboard';
import { useWorkspaceLocations } from '@/features/locations/hooks';

function parseMovement(value: string | undefined): RecentMovement | null {
  if (!value) return null;
  try {
    const movement = JSON.parse(value) as RecentMovement;
    return movement?.id && movement?.movementType ? movement : null;
  } catch {
    return null;
  }
}

function formatMoney(value: number) {
  return `${value.toLocaleString('uk-UA', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₴`;
}

export default function MovementDetailScreen() {
  const { movement: rawMovement } = useLocalSearchParams<{ id: string; movement?: string }>();
  const movement = parseMovement(rawMovement);
  const productQuery = useMovementProduct(movement?.productId ?? '');
  const locationsQuery = useWorkspaceLocations();
  const locations = locationsQuery.data ?? [];
  const fromLocationName = movement?.fromLocationId
    ? locations.find((location) => location.id === movement.fromLocationId)?.name
    : null;
  const toLocationName = movement?.toLocationId
    ? locations.find((location) => location.id === movement.toLocationId)?.name
    : null;

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <Stack.Screen options={{ headerShown: true, title: 'Деталі події', headerBackTitle: '' }} />
      {!movement ? (
        <View className="flex-1 items-center justify-center px-6"><Text className="text-gray-500 text-center">Дані події недоступні. Відкрийте її з історії ще раз.</Text></View>
      ) : (
        <ScrollView contentContainerClassName="p-4 gap-4">
          <View className="bg-white rounded-2xl p-4">
            <Text className="text-xs uppercase text-gray-400">Подія №{movementNumber(movement)}</Text>
            <Text className="text-xl font-bold text-gray-900 mt-2">{MOVEMENT_LABELS[movement.movementType] ?? movement.movementType}</Text>
            <Text className="text-sm text-gray-500 mt-1">{new Date(movement.createdAt).toLocaleString('uk-UA')}</Text>
          </View>
          <View className="bg-white rounded-2xl p-4 gap-3">
            <Row
              label="Товар"
              value={movement.productName ?? productQuery.data?.name ?? (productQuery.isLoading ? 'Завантаження…' : 'Товар не знайдено')}
            />
            <Row label="Кількість" value={String(movement.quantity)} />
            {movement.quantityBefore !== null ? <Row label="Було" value={String(movement.quantityBefore)} /> : null}
            {movement.quantityAfter !== null ? <Row label="Стало" value={String(movement.quantityAfter)} /> : null}
            {movement.unitPrice !== null ? <Row label="Ціна" value={formatMoney(movement.unitPrice)} /> : null}
            {movement.totalAmount !== null ? <Row label="Сума" value={formatMoney(movement.totalAmount)} /> : null}
            {movement.fromLocationId ? <Row label="Звідки" value={movement.fromLocationName ?? fromLocationName ?? 'Магазин не знайдено'} /> : null}
            {movement.toLocationId ? <Row label="Куди" value={movement.toLocationName ?? toLocationName ?? 'Магазин не знайдено'} /> : null}
            {movement.referenceType ? <Row label="Джерело" value={MOVEMENT_REFERENCE_LABELS[movement.referenceType] ?? 'Інша операція'} /> : null}
          </View>
          {movement.notes ? <View className="bg-white rounded-2xl p-4"><Text className="text-xs uppercase text-gray-400">Примітка</Text><Text className="text-sm text-gray-800 mt-2 leading-5">{movement.notes}</Text></View> : null}
        </ScrollView>
      )}
    </SafeAreaView>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return <View className="flex-row justify-between gap-4 border-b border-gray-50 pb-2"><Text className="text-sm text-gray-500">{label}</Text><Text className="text-sm font-medium text-gray-900 flex-1 text-right">{value}</Text></View>;
}
