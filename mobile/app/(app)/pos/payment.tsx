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
import type { PaymentType, SaleItem, SaleRequest } from '@/features/pos/types';
import { calculateNetTotal } from '@/features/pos/utils/calculateNetTotal';
import { usePosDraftStore } from '@/features/pos/draftStore';
import { submitSaleSingleFlight } from '@/features/pos/saleSubmission';
import { NetworkBanner } from '@/features/pos/components/NetworkBanner';
import NetInfo, { useNetInfo } from '@react-native-community/netinfo';
import { isPosOffline } from '@/features/pos/networkPolicy';

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

  const draft = usePosDraftStore();
  const network = useNetInfo();
  const offline = isPosOffline(network);
  const shiftId = params.shiftId ?? draft.shiftId;
  const cart: CartItem[] = useMemo(() => {
    try {
      return params.cartJson ? JSON.parse(params.cartJson) : draft.cart;
    } catch {
      return draft.cart;
    }
  }, [draft.cart, params.cartJson]);

  const [paymentType, setPaymentTypeState] = useState<PaymentType>(draft.paymentType);
  const [cashReceived, setCashReceivedState] = useState(draft.cashReceived);
  const setPaymentType = (value: PaymentType) => {
    setPaymentTypeState(value);
    draft.setPayment(value, cashReceived);
  };
  const setCashReceived = (value: string) => {
    setCashReceivedState(value);
    draft.setPayment(paymentType, value);
  };

  const subtotal = cart.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0);

  // TASK-405/407 (Loyalty): redeemAmount reduces what the customer actually owes — the
  // backend computes the sale's real total the same way (PosService.CreateSaleAsync
  // subtracts redemption from TotalAmount before tax/change), so cash-sufficiency and
  // change must be checked against this net figure, not the raw item subtotal, or the
  // cashier would be misled into asking for more cash than the customer owes.
  const redeemAmount = params.redeemAmount
    ? parseFloat(params.redeemAmount) || 0
    : draft.customer?.redeemAmount ?? 0;
  const netTotal = calculateNetTotal(subtotal, redeemAmount);

  const cashAmount = parseFloat(cashReceived) || 0;
  const change = paymentType === 'Cash' ? Math.max(0, cashAmount - netTotal) : 0;
  const cashInsufficient =
    paymentType === 'Cash' && cashReceived !== '' && cashAmount < netTotal;

  const saleMutation = useSale();

  const handleConfirm = async () => {
    if (isPosOffline(await NetInfo.fetch())) {
      Alert.alert('Немає мережі', 'Продаж можна завершити лише онлайн. Кошик збережено.');
      return;
    }
    if (paymentType === 'Cash' && cashAmount < netTotal) {
      Alert.alert('Недостатньо коштів', 'Введена сума менша за суму продажу.');
      return;
    }

    const body: SaleRequest = {
      shiftId,
      items: cart.map(({ barcode, quantity }) => ({ barcode, quantity })),
      paymentType,
      paymentAmount: paymentType === 'Cash' ? cashAmount : netTotal,
      ...(params.customerId || draft.customer?.customerId
        ? { customerId: params.customerId ?? draft.customer?.customerId }
        : {}),
      ...(params.membershipId || draft.customer?.membershipId
        ? { loyaltyMembershipId: params.membershipId ?? draft.customer?.membershipId }
        : {}),
      ...(redeemAmount > 0 ? { redeemAmount } : {}),
    };

    const result = await submitSaleSingleFlight(
      body,
      saleMutation.mutateAsync,
      ({ status, message, transactionId }) =>
        draft.setSubmission(status, message, transactionId)
    );
    if (result) {
      await draft.clearAfterConfirmedSale();
      router.replace({
        pathname: '/(app)/pos/receipt',
        params: { resultJson: JSON.stringify(result), shiftId },
      });
      return;
    }
    const submission = usePosDraftStore.getState().submission;
    Alert.alert(
      submission.status === 'uncertain' ? 'Результат не підтверджено' : 'Продаж не виконано',
      submission.message ?? 'Кошик збережено.'
    );
  };

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <NetworkBanner />
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
        {(params.customerName || params.membershipId || draft.customer) && (
          <View className="bg-green-50 mx-4 mt-3 rounded-2xl px-4 py-3 flex-row items-center">
            <Ionicons name="person-circle-outline" size={22} color="#15803d" />
            <View className="ml-2 flex-1">
              <Text className="text-green-800 font-semibold text-sm">
                {params.customerName ?? draft.customer?.customerName ?? 'Клієнт'}
              </Text>
              {(params.maskedPhone || draft.customer?.maskedPhone) && (
                <Text className="text-green-700 text-xs mt-0.5">
                  {params.maskedPhone ?? draft.customer?.maskedPhone}
                </Text>
              )}
            </View>
            {(params.membershipId || draft.customer?.membershipId) && (
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

        {draft.submission.status !== 'idle' && (
          <View
            className={`mx-4 mt-4 rounded-xl px-4 py-3 ${
              draft.submission.status === 'uncertain'
                ? 'bg-amber-100'
                : draft.submission.status === 'conflict'
                  ? 'bg-orange-100'
                  : draft.submission.status === 'failed'
                    ? 'bg-red-50'
                    : 'bg-blue-50'
            }`}
          >
            <Text className="font-semibold text-sm text-gray-900">
              {draft.submission.status === 'pending'
                ? 'Продаж надсилається'
                : draft.submission.status === 'uncertain'
                  ? 'Результат продажу невідомий'
                  : draft.submission.status === 'conflict'
                    ? 'Конфлікт даних'
                    : draft.submission.status === 'completed'
                      ? 'Продаж підтверджено'
                      : 'Продаж не виконано'}
            </Text>
            {draft.submission.message && (
              <Text className="text-gray-700 text-xs mt-1">{draft.submission.message}</Text>
            )}
            {(draft.submission.status === 'uncertain' ||
              draft.submission.status === 'conflict') && (
              <TouchableOpacity
                onPress={() =>
                  Alert.alert(
                    'Відкинути чернетку?',
                    'Робіть це лише після звірки продажів і залишків поточної зміни.',
                    [
                      { text: 'Скасувати', style: 'cancel' },
                      {
                        text: 'Звірено, відкинути',
                        style: 'destructive',
                        onPress: () => void draft.discard(),
                      },
                    ]
                  )
                }
                className="mt-3 border border-amber-700 rounded-lg py-2 items-center"
              >
                <Text className="text-amber-900 text-xs font-bold">
                  Звірено — відкинути чернетку
                </Text>
              </TouchableOpacity>
            )}
          </View>
        )}

        {/* Spacer */}
        <View className="flex-1" />

        {/* Confirm button */}
        <View className="px-4 pb-6 pt-3 bg-white border-t border-gray-100">
          <TouchableOpacity
            onPress={handleConfirm}
            disabled={
              saleMutation.isPending ||
              cashInsufficient ||
              offline ||
              draft.submission.status === 'uncertain' ||
              draft.submission.status === 'conflict'
            }
            className={`rounded-2xl py-5 items-center ${
              saleMutation.isPending ||
              cashInsufficient ||
              offline ||
              draft.submission.status === 'uncertain' ||
              draft.submission.status === 'conflict'
                ? 'bg-gray-200'
                : 'bg-primary-600'
            }`}
          >
            {saleMutation.isPending ? (
              <ActivityIndicator color="white" />
            ) : (
              <Text
                className={`text-lg font-bold ${
                  saleMutation.isPending ||
                  cashInsufficient ||
                  offline ||
                  draft.submission.status === 'uncertain' ||
                  draft.submission.status === 'conflict'
                    ? 'text-gray-400'
                    : 'text-white'
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
