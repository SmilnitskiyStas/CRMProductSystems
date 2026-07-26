import { useState, useMemo } from 'react';
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  FlatList,
  ActivityIndicator,
  Alert,
  KeyboardAvoidingView,
  Platform,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter, useLocalSearchParams } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSale } from '@/features/pos/hooks/usePosApi';
import type { PaymentType, SaleRequest } from '@/features/pos/types';
import type { SaleItem } from '@/features/pos/types';

interface CartItem extends SaleItem {
  productName: string;
  unitPrice: number;
}

function formatPrice(amount: number): string {
  return amount.toFixed(2);
}

export default function PosPaymentScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{
    shiftId?: string;
    cartJson?: string;
    customerId?: string;
    membershipId?: string;
    redeemAmount?: string;
    customerName?: string;
    maskedPhone?: string;
  }>();

  const shiftId = params.shiftId ?? '';
  const cart: CartItem[] = useMemo(() => {
    try {
      return params.cartJson ? JSON.parse(params.cartJson) : [];
    } catch {
      return [];
    }
  }, [params.cartJson]);

  const [paymentType, setPaymentType] = useState<PaymentType>('Cash');
  const [cashReceived, setCashReceived] = useState('');

  const subtotal = cart.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0);

  // TASK-405/407 (Loyalty): redeemAmount reduces what the customer actually owes — the
  // backend computes the sale's real total the same way (PosService.CreateSaleAsync
  // subtracts redemption from TotalAmount before tax/change), so cash-sufficiency and
  // change must be checked against this net figure, not the raw item subtotal, or the
  // cashier would be misled into asking for more cash than the customer owes.
  const redeemAmount = params.redeemAmount ? parseFloat(params.redeemAmount) || 0 : 0;
  const netTotal = Math.max(0, subtotal - redeemAmount);

  const cashAmount = parseFloat(cashReceived) || 0;
  const change = paymentType === 'Cash' ? Math.max(0, cashAmount - netTotal) : 0;
  const cashInsufficient =
    paymentType === 'Cash' && cashReceived !== '' && cashAmount < netTotal;

  const saleMutation = useSale();

  const handleConfirm = () => {
    if (paymentType === 'Cash' && cashAmount < netTotal) {
      Alert.alert('Недостатньо коштів', 'Введена сума менша за суму продажу.');
      return;
    }

    const body: SaleRequest = {
      shiftId,
      items: cart.map(({ barcode, quantity }) => ({ barcode, quantity })),
      paymentType,
      paymentAmount: paymentType === 'Cash' ? cashAmount : netTotal,
      ...(params.customerId ? { customerId: params.customerId } : {}),
      ...(params.membershipId ? { loyaltyMembershipId: params.membershipId } : {}),
      ...(redeemAmount > 0 ? { redeemAmount } : {}),
    };

    saleMutation.mutate(body, {
      onSuccess: (result) => {
        router.replace({
          pathname: '/(app)/pos/receipt',
          params: { resultJson: JSON.stringify(result), shiftId },
        });
      },
      onError: (err: unknown) => {
        const axiosErr = err as { response?: { status?: number; data?: { error?: string; blockedProduct?: string } } };
        const status = axiosErr?.response?.status;
        const errData = axiosErr?.response?.data;

        if (status === 423) {
          const productName = errData?.blockedProduct ?? 'товар';
          Alert.alert(
            'Продаж заблоковано',
            `Товар "${productName}" прострочений і не може бути проданий.`,
            [{ text: 'Зрозуміло' }]
          );
        } else if (status === 400 || status === 409) {
          Alert.alert(
            'Помилка продажу',
            errData?.error ?? 'Перевірте дані та спробуйте знову.'
          );
        } else {
          Alert.alert('Помилка', 'Не вдалося провести продаж. Спробуйте ще раз.');
        }
      },
    });
  };

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        className="flex-1"
      >
        {/* Header */}
        <View className="flex-row items-center px-5 pt-4 pb-3 bg-white border-b border-gray-100">
          <TouchableOpacity
            onPress={() => router.back()}
            className="w-10 h-10 bg-gray-100 rounded-full items-center justify-center mr-3"
          >
            <Ionicons name="arrow-back" size={20} color="#374151" />
          </TouchableOpacity>
          <Text className="text-xl font-bold text-gray-900">Оплата</Text>
        </View>

        {/* Cart summary */}
        <View className="bg-white mx-4 mt-4 rounded-2xl border border-gray-100 shadow-sm overflow-hidden">
          <View className="px-4 py-3 border-b border-gray-100">
            <Text className="text-sm font-semibold text-gray-600">Товари</Text>
          </View>
          <FlatList
            data={cart}
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
                  {formatPrice(item.unitPrice * item.quantity)} ₴
                </Text>
              </View>
            )}
          />
          {redeemAmount > 0 && (
            <View className="flex-row justify-between items-center px-4 py-2 bg-gray-50 border-t border-gray-100">
              <Text className="text-sm text-gray-500">Списано бонусів</Text>
              <Text className="text-sm font-semibold text-red-500">-{formatPrice(redeemAmount)} ₴</Text>
            </View>
          )}
          <View className="flex-row justify-between items-center px-4 py-3 bg-gray-50 border-t border-gray-100">
            <Text className="text-base font-bold text-gray-900">
              {redeemAmount > 0 ? 'До сплати' : 'Разом'}
            </Text>
            <Text className="text-base font-bold text-gray-900">{formatPrice(netTotal)} ₴</Text>
          </View>
        </View>

        {/* Loyalty customer info (TASK-405/407) */}
        {(params.customerName || params.membershipId) && (
          <View className="bg-green-50 mx-4 mt-3 rounded-2xl px-4 py-3 flex-row items-center">
            <Ionicons name="person-circle-outline" size={22} color="#15803d" />
            <View className="ml-2 flex-1">
              <Text className="text-green-800 font-semibold text-sm">
                {params.customerName ?? 'Клієнт'}
              </Text>
              {params.maskedPhone && (
                <Text className="text-green-700 text-xs mt-0.5">{params.maskedPhone}</Text>
              )}
            </View>
            {params.membershipId && (
              <Ionicons name="qr-code-outline" size={18} color="#15803d" />
            )}
          </View>
        )}

        {/* Payment type toggle */}
        <View className="mx-4 mt-4">
          <Text className="text-sm font-semibold text-gray-600 mb-2">Спосіб оплати</Text>
          <View className="flex-row bg-white rounded-2xl border border-gray-200 overflow-hidden">
            {(['Cash', 'Card'] as PaymentType[]).map((type) => (
              <TouchableOpacity
                key={type}
                onPress={() => setPaymentType(type)}
                className={`flex-1 py-4 flex-row items-center justify-center gap-2 ${
                  paymentType === type ? 'bg-primary-600' : 'bg-white'
                }`}
              >
                <Ionicons
                  name={type === 'Cash' ? 'cash-outline' : 'card-outline'}
                  size={20}
                  color={paymentType === type ? 'white' : '#6b7280'}
                />
                <Text
                  className={`font-semibold text-base ${
                    paymentType === type ? 'text-white' : 'text-gray-600'
                  }`}
                >
                  {type === 'Cash' ? 'Готівка' : 'Картка'}
                </Text>
              </TouchableOpacity>
            ))}
          </View>
        </View>

        {/* Cash input */}
        {paymentType === 'Cash' && (
          <View className="mx-4 mt-4">
            <Text className="text-sm font-semibold text-gray-600 mb-2">
              Отримано від покупця
            </Text>
            <TextInput
              className={`bg-white border rounded-2xl px-4 py-4 text-xl font-bold text-gray-900 ${
                cashInsufficient ? 'border-red-400' : 'border-gray-200'
              }`}
              keyboardType="decimal-pad"
              placeholder="0.00"
              placeholderTextColor="#9ca3af"
              value={cashReceived}
              onChangeText={setCashReceived}
            />
            {cashInsufficient && (
              <Text className="text-red-500 text-xs mt-1">
                Введена сума менша за суму продажу
              </Text>
            )}
            {!cashInsufficient && cashReceived !== '' && (
              <View className="flex-row justify-between mt-3 bg-green-50 rounded-xl px-4 py-3">
                <Text className="text-green-800 font-semibold">Решта</Text>
                <Text className="text-green-800 font-bold text-lg">{formatPrice(change)} ₴</Text>
              </View>
            )}
          </View>
        )}

        {paymentType === 'Card' && (
          <View className="mx-4 mt-4 bg-blue-50 rounded-xl px-4 py-3">
            <Text className="text-blue-800 text-sm">
              Прийміть оплату через термінал та підтвердіть.
            </Text>
          </View>
        )}

        {/* Spacer */}
        <View className="flex-1" />

        {/* Confirm button */}
        <View className="px-4 pb-6 pt-3 bg-white border-t border-gray-100">
          <TouchableOpacity
            onPress={handleConfirm}
            disabled={saleMutation.isPending || cashInsufficient}
            className={`rounded-2xl py-5 items-center ${
              saleMutation.isPending || cashInsufficient
                ? 'bg-gray-200'
                : 'bg-primary-600'
            }`}
          >
            {saleMutation.isPending ? (
              <ActivityIndicator color="white" />
            ) : (
              <Text
                className={`text-lg font-bold ${
                  saleMutation.isPending || cashInsufficient ? 'text-gray-400' : 'text-white'
                }`}
              >
                Провести продаж — {formatPrice(netTotal)} ₴
              </Text>
            )}
          </TouchableOpacity>
        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
