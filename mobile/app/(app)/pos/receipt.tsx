import { useEffect, useMemo, useRef, useState } from 'react';
import {
  View,
  Text,
  TouchableOpacity,
  FlatList,
  ScrollView,
  ActivityIndicator,
  Alert,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter, useLocalSearchParams } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { FiscalBadge } from '@/features/pos/components/FiscalBadge';
import type { SaleResponse } from '@/features/pos/types';
import { printSaleReceipt } from '@/features/pos/receiptPrinting';

function formatPrice(amount: number): string {
  return amount.toFixed(2);
}

export default function PosReceiptScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ resultJson?: string; shiftId?: string; autoPrint?: string }>();
  const autoPrintStarted = useRef(false);
  const [printing, setPrinting] = useState(false);

  const shiftId = params.shiftId ?? '';

  const result: SaleResponse | null = useMemo(() => {
    try {
      return params.resultJson ? JSON.parse(params.resultJson) : null;
    } catch {
      return null;
    }
  }, [params.resultJson]);

  const printReceipt = async () => {
    if (!result || printing) return;
    setPrinting(true);
    try { await printSaleReceipt(result); }
    catch { Alert.alert('Не вдалося надрукувати чек', 'Перевірте підключення принтера та спробуйте ще раз.'); }
    finally { setPrinting(false); }
  };

  useEffect(() => {
    if (!result || params.autoPrint !== '1' || autoPrintStarted.current) return;
    autoPrintStarted.current = true;
    void printReceipt();
  }, [params.autoPrint, result]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!result) {
    return (
      <SafeAreaView className="flex-1 bg-gray-50 items-center justify-center">
        <Text className="text-gray-500">Дані не знайдено</Text>
        <TouchableOpacity
          onPress={() => router.replace('/(app)/pos')}
          className="mt-4 bg-primary-600 px-6 py-3 rounded-xl"
        >
          <Text className="text-white font-semibold">На головну</Text>
        </TouchableOpacity>
      </SafeAreaView>
    );
  }

  const paymentLabel = result.paymentType === 'Cash' ? 'Готівка' : 'Картка';

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <ScrollView contentContainerStyle={{ paddingBottom: 190 }}>
        {/* Success header */}
        <View className="bg-white px-5 pt-6 pb-5 border-b border-gray-100 items-center">
          <View className="bg-green-100 w-16 h-16 rounded-full items-center justify-center mb-3">
            <Ionicons name="checkmark-circle" size={40} color="#16a34a" />
          </View>
          <Text className="text-xl font-bold text-gray-900">Продаж проведено</Text>
          <Text className="text-xs text-gray-400 mt-1 font-mono">
            #{result.transactionId.slice(0, 8).toUpperCase()}
          </Text>
        </View>

        {/* Fiscal status block */}
        <View className="bg-white mx-4 mt-4 rounded-2xl p-4 border border-gray-100 gap-3">
          <View className="flex-row items-center justify-between">
            <Text className="text-sm font-semibold text-gray-600">Фіскальний статус</Text>
            <FiscalBadge status={result.fiscalStatus} />
          </View>

          {result.fiscalNumber ? (
            <View>
              <Text className="text-xs text-gray-400">Фіскальний номер</Text>
              <Text className="text-sm font-mono text-gray-800">{result.fiscalNumber}</Text>
            </View>
          ) : result.fiscalStatus === 'pending_fiscalization' ? (
            <View className="bg-amber-50 rounded-xl px-3 py-2">
              <Text className="text-amber-700 text-xs">
                Фіскалізація в процесі... Чек буде надіслано після підтвердження.
              </Text>
            </View>
          ) : null}
        </View>

        {/* Items */}
        <View className="bg-white mx-4 mt-4 rounded-2xl border border-gray-100 overflow-hidden">
          <View className="px-4 py-3 border-b border-gray-100">
            <Text className="text-sm font-semibold text-gray-600">Товари</Text>
          </View>
          <FlatList
            data={result.items}
            keyExtractor={(item) => item.barcode}
            scrollEnabled={false}
            ItemSeparatorComponent={() => <View className="h-px bg-gray-50" />}
            renderItem={({ item }) => (
              <View className="flex-row justify-between items-center px-4 py-3">
                <View className="flex-1 mr-2">
                  <Text className="text-sm text-gray-900" numberOfLines={1}>
                    {item.productName}
                  </Text>
                  <Text className="text-xs text-gray-400">
                    {formatPrice(item.unitPrice)} ₴ × {item.quantity}
                  </Text>
                </View>
                <Text className="text-sm font-semibold text-gray-900">
                  {formatPrice(item.lineTotal)} ₴
                </Text>
              </View>
            )}
          />
        </View>

        {/* Payment summary */}
        <View className="bg-white mx-4 mt-4 rounded-2xl p-4 border border-gray-100 gap-2">
          <View className="flex-row justify-between">
            <Text className="text-sm text-gray-500">Сума</Text>
            <Text className="text-sm font-semibold text-gray-900">
              {formatPrice(result.subtotal)} ₴
            </Text>
          </View>
          <View className="flex-row justify-between">
            <Text className="text-sm text-gray-500">Спосіб оплати</Text>
            <Text className="text-sm font-semibold text-gray-900">{paymentLabel}</Text>
          </View>
          {result.paymentType === 'Cash' && (
            <>
              <View className="flex-row justify-between">
                <Text className="text-sm text-gray-500">Отримано</Text>
                <Text className="text-sm font-semibold text-gray-900">
                  {formatPrice(result.paymentAmount)} ₴
                </Text>
              </View>
              <View className="h-px bg-gray-100" />
              <View className="flex-row justify-between">
                <Text className="text-base font-bold text-gray-900">Решта</Text>
                <Text className="text-base font-bold text-green-700">
                  {formatPrice(result.change)} ₴
                </Text>
              </View>
            </>
          )}
        </View>

        {/* Loyalty summary (TASK-405/407) — undefined when the sale carried no membership */}
        {(result.loyaltyAccrued != null || result.loyaltyRedeemed != null) && (
          <View className="bg-white mx-4 mt-4 rounded-2xl p-4 border border-gray-100 gap-2">
            <Text className="text-sm font-semibold text-gray-600 mb-1">Бонусна програма</Text>
            {result.loyaltyRedeemed != null && result.loyaltyRedeemed > 0 && (
              <View className="flex-row justify-between">
                <Text className="text-sm text-gray-500">Списано</Text>
                <Text className="text-sm font-semibold text-red-500">
                  -{formatPrice(result.loyaltyRedeemed)} ₴
                </Text>
              </View>
            )}
            {result.loyaltyAccrued != null && result.loyaltyAccrued > 0 && (
              <View className="flex-row justify-between">
                <Text className="text-sm text-gray-500">Нараховано</Text>
                <Text className="text-sm font-semibold text-green-600">
                  +{formatPrice(result.loyaltyAccrued)} ₴
                </Text>
              </View>
            )}
            {result.loyaltyBalance != null && (
              <>
                <View className="h-px bg-gray-100" />
                <View className="flex-row justify-between">
                  <Text className="text-base font-bold text-gray-900">Баланс клієнта</Text>
                  <Text className="text-base font-bold text-primary-600">
                    {formatPrice(result.loyaltyBalance)} ₴
                  </Text>
                </View>
              </>
            )}
          </View>
        )}
      </ScrollView>

      {/* Bottom buttons */}
      <View className="absolute bottom-0 left-0 right-0 bg-white border-t border-gray-100 px-4 py-4 gap-3">
        <TouchableOpacity disabled={printing} onPress={() => void printReceipt()} className="flex-row items-center justify-center rounded-2xl border border-green-600 py-3">
          {printing ? <ActivityIndicator color="#16a34a" /> : <><Ionicons name="print-outline" size={19} color="#15803d" /><Text className="ml-2 font-semibold text-green-700">Надрукувати чек</Text></>}
        </TouchableOpacity>
        <TouchableOpacity
          onPress={() =>
            router.replace({
              pathname: '/(app)/pos/scanner',
              params: { shiftId },
            })
          }
          className="bg-primary-600 rounded-2xl py-4 items-center"
        >
          <Text className="text-white font-bold text-base">Новий продаж</Text>
        </TouchableOpacity>
        <TouchableOpacity
          onPress={() => router.replace('/(app)/pos')}
          className="bg-white border border-gray-200 rounded-2xl py-4 items-center"
        >
          <Text className="text-gray-700 font-semibold text-base">Головна каси</Text>
        </TouchableOpacity>
      </View>
    </SafeAreaView>
  );
}
