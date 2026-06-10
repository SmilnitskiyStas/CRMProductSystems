import { View, Text, ScrollView, TouchableOpacity, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Stack, useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useStockBatch, useVerifyBatch } from '@/features/stock/hooks/useStock';

export default function StockBatchDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const { data: batch, isLoading, isError } = useStockBatch(id);
  const { mutate: verify, isPending: isVerifying } = useVerifyBatch();

  if (isLoading) {
    return (
      <SafeAreaView className="flex-1 bg-white items-center justify-center">
        <ActivityIndicator size="large" color="#16a34a" />
      </SafeAreaView>
    );
  }

  if (isError || !batch) {
    return (
      <SafeAreaView className="flex-1 bg-white items-center justify-center px-6">
        <Text className="text-red-500 text-center">Партію не знайдено</Text>
        <TouchableOpacity onPress={() => router.back()} className="mt-4">
          <Text className="text-primary-600 font-medium">Назад</Text>
        </TouchableOpacity>
      </SafeAreaView>
    );
  }

  const expiry = new Date(batch.expiryDate).toLocaleDateString('uk-UA');

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <Stack.Screen options={{ headerShown: true, title: 'Деталі партії', headerBackTitle: '' }} />
      <ScrollView contentContainerClassName="p-4 gap-4">
        <View className="bg-white rounded-2xl p-4 gap-3">
          <Text className="text-xl font-bold text-gray-900">{batch.productName}</Text>
          {batch.barcode && (
            <Text className="text-sm text-gray-500 font-mono">{batch.barcode}</Text>
          )}
        </View>

        <View className="bg-white rounded-2xl p-4 gap-3">
          <Row label="К-сть" value={`${batch.quantity} ${batch.unit}`} />
          <Row label="Термін придатності" value={expiry} />
          <Row label="Днів залишилось" value={String(batch.daysLeft)} highlight />
          {batch.batchNumber && <Row label="Номер партії" value={batch.batchNumber} />}
          {batch.zoneName && <Row label="Зона" value={batch.zoneName} />}
          {batch.shelfNumber && <Row label="Полиця" value={batch.shelfNumber} />}
        </View>

        {batch.status === 'needs_verification' && (
          <TouchableOpacity
            onPress={() => verify(batch.id)}
            disabled={isVerifying}
            className="bg-primary-600 py-4 rounded-xl items-center"
          >
            {isVerifying ? (
              <ActivityIndicator color="white" />
            ) : (
              <Text className="text-white font-semibold text-base">Підтвердити наявність</Text>
            )}
          </TouchableOpacity>
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

function Row({ label, value, highlight }: { label: string; value: string; highlight?: boolean }) {
  return (
    <View className="flex-row justify-between items-center py-1 border-b border-gray-50">
      <Text className="text-sm text-gray-500">{label}</Text>
      <Text className={`text-sm font-medium ${highlight ? 'text-primary-600 font-bold' : 'text-gray-900'}`}>
        {value}
      </Text>
    </View>
  );
}
