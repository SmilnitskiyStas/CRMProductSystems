import { View, Text, FlatList, TouchableOpacity, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Stack, useLocalSearchParams, useRouter } from 'expo-router';
import { useReceipt, useConfirmReceipt, useProcessItem } from '@/features/receipt/hooks/useReceipts';
import { ReceiptItemRow } from '@/features/receipt/components/ReceiptItemRow';
import { receiptNumber } from '@/features/receipt/types';

export default function ReceiptDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const { data: receipt, isLoading } = useReceipt(id);
  const { mutate: confirm, isPending } = useConfirmReceipt();
  const { mutate: process } = useProcessItem();

  if (isLoading) {
    return (
      <SafeAreaView className="flex-1 bg-white items-center justify-center">
        <ActivityIndicator size="large" color="#16a34a" />
      </SafeAreaView>
    );
  }

  if (!receipt) {
    return (
      <SafeAreaView className="flex-1 bg-white items-center justify-center">
        <Text className="text-red-500">Прийомку не знайдено</Text>
        <TouchableOpacity onPress={() => router.back()} className="mt-4">
          <Text className="text-primary-600">Назад</Text>
        </TouchableOpacity>
      </SafeAreaView>
    );
  }

  const allProcessed = receipt.items.every((i) => i.isProcessed);
  const processedCount = receipt.items.filter((i) => i.isProcessed).length;

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <Stack.Screen
        options={{ headerShown: true, title: `Прийомка №${receiptNumber(receipt)}`, headerBackTitle: '' }}
      />

      <View className="px-4 py-3 bg-white border-b border-gray-100">
        <Text className="text-sm text-gray-500">
          {receipt.supplierName ?? 'Без постачальника'} → {receipt.destinationLocationName}
        </Text>
        <View className="flex-row items-center mt-1">
          <Text className="text-sm text-gray-700">
            Оброблено: {processedCount} / {receipt.items.length}
          </Text>
        </View>
        {/* Progress bar */}
        <View className="h-1.5 bg-gray-200 rounded-full mt-2">
          <View
            className="h-1.5 bg-primary-600 rounded-full"
            style={{ width: `${(processedCount / receipt.items.length) * 100}%` }}
          />
        </View>
      </View>

      {receipt.status === 'draft' && (
        <Text className="text-xs text-gray-400 px-4 py-2">
          Торкніться позиції, щоб прийняти її із замовленою кількістю
        </Text>
      )}

      <FlatList
        data={receipt.items}
        keyExtractor={(item) => item.id}
        renderItem={({ item }) => (
          <ReceiptItemRow
            item={item}
            onPress={
              receipt.status === 'draft' && !item.isProcessed
                ? () => process({
                    receiptId: receipt.id,
                    itemId: item.id,
                    quantityReceived: item.quantityOrdered,
                  })
                : undefined
            }
          />
        )}
        contentContainerClassName="pb-32"
      />

      {receipt.status === 'draft' && (
        <View className="absolute bottom-0 left-0 right-0 px-4 pb-8 pt-4 bg-white border-t border-gray-100">
          <TouchableOpacity
            onPress={() => confirm(receipt.id, { onSuccess: () => router.back() })}
            disabled={!allProcessed || isPending}
            className={`py-4 rounded-xl items-center ${
              allProcessed ? 'bg-primary-600' : 'bg-gray-200'
            }`}
          >
            {isPending ? (
              <ActivityIndicator color="white" />
            ) : (
              <Text
                className={`font-semibold text-base ${
                  allProcessed ? 'text-white' : 'text-gray-400'
                }`}
              >
                Підтвердити прийомку
              </Text>
            )}
          </TouchableOpacity>
        </View>
      )}
    </SafeAreaView>
  );
}
