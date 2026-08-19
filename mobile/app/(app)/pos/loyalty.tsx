import { useMemo, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  KeyboardAvoidingView,
  Platform,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter, useLocalSearchParams } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { BarcodeCameraView } from '@/features/pos/components/BarcodeCameraView';
import { useResolveLoyaltyCode } from '@/features/loyalty/hooks/useLoyalty';
import type { ResolveLoyaltyCodeResult } from '@/features/loyalty/types';
import { useCustomers } from '@/features/customers/hooks/useCustomers';
import { CustomerCard } from '@/features/customers/components/CustomerCard';
import type { SaleItem } from '@/features/pos/types';
import { usePosDraftStore } from '@/features/pos/draftStore';
import { NetworkBanner } from '@/features/pos/components/NetworkBanner';

interface CartItem extends SaleItem {
  productName: string;
  unitPrice: number;
}

type Mode = 'scan' | 'code-entry' | 'customer-search';

function formatPrice(amount: number): string {
  return amount.toFixed(2);
}

/**
 * Loyalty step inserted between pos/scanner.tsx and pos/payment.tsx (TASK-405/407): scan or
 * type a customer's rotating loyalty code (POST /api/loyalty/resolve-code), optionally pick a
 * redemption amount, or fall back to a plain customer search — then forward everything to
 * payment.tsx via router params. Entirely skippable ("Пропустити") for a normal sale.
 */
export default function PosLoyaltyScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ shiftId?: string; cartJson?: string }>();
  const draftCart = usePosDraftStore((state) => state.cart);
  const draftShiftId = usePosDraftStore((state) => state.shiftId);
  const setCustomerDraft = usePosDraftStore((state) => state.setCustomer);
  const shiftId = params.shiftId ?? draftShiftId;

  const cart: CartItem[] = useMemo(() => {
    try {
      return params.cartJson ? JSON.parse(params.cartJson) : draftCart;
    } catch {
      return draftCart;
    }
  }, [draftCart, params.cartJson]);
  const cartJson = JSON.stringify(cart);
  const subtotal = cart.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0);

  const [mode, setMode] = useState<Mode>('scan');
  const [scanning, setScanning] = useState(true);
  const lastScanRef = useRef<string | null>(null);

  const [resolved, setResolved] = useState<ResolveLoyaltyCodeResult | null>(null);
  const [manualCustomer, setManualCustomer] = useState<{ id: string; name: string } | null>(null);
  const [redeemText, setRedeemText] = useState('');
  const [codeText, setCodeText] = useState('');
  const [customerSearch, setCustomerSearch] = useState('');

  const resolveMutation = useResolveLoyaltyCode();
  const { data: customersPage, isLoading: customersLoading } = useCustomers(customerSearch);

  const resumeScanning = () => {
    setScanning(true);
    lastScanRef.current = null;
  };

  const handleResolve = (code: string) => {
    resolveMutation.mutate(code, {
      onSuccess: (result) => {
        setResolved(result);
        setManualCustomer(null);
      },
      onError: (err: unknown) => {
        const axiosErr = err as { response?: { status?: number; data?: { error?: string } } };
        const status = axiosErr?.response?.status;
        const message = axiosErr?.response?.data?.error;
        Alert.alert(
          status === 429 ? 'Забагато спроб' : 'Код не розпізнано',
          message ?? 'Перевірте код і спробуйте ще раз.'
        );
        resumeScanning();
      },
    });
  };

  const handleBarcodeScanned = (data: string) => {
    if (!scanning || resolveMutation.isPending) return;
    if (lastScanRef.current === data) return;
    lastScanRef.current = data;
    setScanning(false);
    handleResolve(data);
  };

  const redeemAmount = parseFloat(redeemText.replace(',', '.')) || 0;
  const redeemInvalid = resolved !== null && redeemAmount > 0 && redeemAmount > resolved.balance;

  const clearResult = () => {
    setResolved(null);
    setManualCustomer(null);
    setRedeemText('');
    setCustomerDraft(null);
    resumeScanning();
  };

  const goToPayment = (opts?: { skip?: boolean }) => {
    const extra: Record<string, string> = {};
    if (!opts?.skip) {
      if (resolved) {
        extra.membershipId = resolved.membershipId;
        if (resolved.customerId) extra.customerId = resolved.customerId;
        if (resolved.customerName) extra.customerName = resolved.customerName;
        if (resolved.maskedPhone) extra.maskedPhone = resolved.maskedPhone;
        if (redeemAmount > 0 && !redeemInvalid) extra.redeemAmount = String(redeemAmount);
      } else if (manualCustomer) {
        extra.customerId = manualCustomer.id;
        extra.customerName = manualCustomer.name;
      }
    }
    setCustomerDraft(
      opts?.skip
        ? null
        : {
            ...(extra.customerId ? { customerId: extra.customerId } : {}),
            ...(extra.customerName ? { customerName: extra.customerName } : {}),
            ...(extra.maskedPhone ? { maskedPhone: extra.maskedPhone } : {}),
            ...(extra.membershipId ? { membershipId: extra.membershipId } : {}),
            ...(extra.redeemAmount ? { redeemAmount: Number(extra.redeemAmount) } : {}),
          }
    );
    router.push({
      pathname: '/(app)/pos/payment',
      params: { shiftId, cartJson, ...extra },
    });
  };

  const hasResult = !!resolved || !!manualCustomer;

  return (
    <SafeAreaView className="flex-1 bg-gray-900">
      <NetworkBanner />
      {/* Top bar */}
      <View className="flex-row items-center px-4 py-3">
        <TouchableOpacity
          onPress={() => router.back()}
          className="w-10 h-10 bg-white/10 rounded-full items-center justify-center"
        >
          <Ionicons name="arrow-back" size={20} color="white" />
        </TouchableOpacity>
        <Text className="text-white text-lg font-bold ml-3">Бонусна картка клієнта</Text>
      </View>

      <View className="mx-4 mb-3 bg-white/10 rounded-xl px-4 py-2">
        <Text className="text-white/70 text-xs">Сума продажу</Text>
        <Text className="text-white text-lg font-bold">{formatPrice(subtotal)} ₴</Text>
      </View>

      <View className="flex-1 bg-white rounded-t-3xl overflow-hidden">
        <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : 'height'} className="flex-1">
          {resolved ? (
            /* ── Resolved membership result ── */
            <View className="p-5">
              <View className="flex-row items-center justify-between mb-4">
                <Text className="text-base font-bold text-gray-900">Клієнт знайдений</Text>
                <TouchableOpacity onPress={clearResult}>
                  <Ionicons name="close-circle" size={24} color="#9ca3af" />
                </TouchableOpacity>
              </View>

              <View className="bg-green-50 rounded-2xl p-4 mb-4">
                <Text className="text-gray-900 font-semibold text-base">
                  {resolved.customerName ?? 'Клієнт без картки CRM'}
                </Text>
                {resolved.maskedPhone && (
                  <Text className="text-gray-500 text-sm mt-0.5">{resolved.maskedPhone}</Text>
                )}
                <Text className="text-gray-500 text-sm mt-2">Баланс бонусів</Text>
                <Text className="text-green-700 font-bold text-2xl">{formatPrice(resolved.balance)} ₴</Text>
              </View>

              {resolved.balance > 0 && (
                <View className="mb-4">
                  <Text className="text-sm font-semibold text-gray-600 mb-2">
                    Списати бонусів (необов&apos;язково)
                  </Text>
                  <TextInput
                    className={`border rounded-xl px-4 py-3 text-base text-gray-900 bg-gray-50 ${
                      redeemInvalid ? 'border-red-400' : 'border-gray-200'
                    }`}
                    keyboardType="decimal-pad"
                    placeholder="0.00"
                    value={redeemText}
                    onChangeText={setRedeemText}
                  />
                  {redeemInvalid ? (
                    <Text className="text-red-500 text-xs mt-1">Сума перевищує баланс клієнта</Text>
                  ) : (
                    <Text className="text-gray-400 text-xs mt-1">
                      Доступно до {formatPrice(Math.min(resolved.balance, subtotal))} ₴. Точний ліміт
                      магазину перевіряється при оплаті.
                    </Text>
                  )}
                </View>
              )}

              <TouchableOpacity
                onPress={() => goToPayment()}
                disabled={redeemInvalid}
                className={`rounded-2xl py-4 items-center ${redeemInvalid ? 'bg-gray-200' : 'bg-primary-600'}`}
              >
                <Text className={`font-bold text-base ${redeemInvalid ? 'text-gray-400' : 'text-white'}`}>
                  Продовжити до оплати
                </Text>
              </TouchableOpacity>
            </View>
          ) : manualCustomer ? (
            /* ── Manually picked customer (no loyalty membership) ── */
            <View className="p-5">
              <View className="flex-row items-center justify-between mb-4">
                <Text className="text-base font-bold text-gray-900">Клієнт обраний</Text>
                <TouchableOpacity onPress={clearResult}>
                  <Ionicons name="close-circle" size={24} color="#9ca3af" />
                </TouchableOpacity>
              </View>
              <View className="bg-gray-50 rounded-2xl p-4 mb-4">
                <Text className="text-gray-900 font-semibold text-base">{manualCustomer.name}</Text>
                <Text className="text-gray-400 text-xs mt-1">
                  Без бонусної картки — лише прив&apos;язка продажу до клієнта
                </Text>
              </View>
              <TouchableOpacity onPress={() => goToPayment()} className="rounded-2xl py-4 items-center bg-primary-600">
                <Text className="font-bold text-base text-white">Продовжити до оплати</Text>
              </TouchableOpacity>
            </View>
          ) : (
            <>
              {/* Mode toggle */}
              <View className="flex-row mx-4 mt-4 bg-gray-100 rounded-xl p-1">
                {(['scan', 'code-entry', 'customer-search'] as Mode[]).map((m) => (
                  <TouchableOpacity
                    key={m}
                    onPress={() => setMode(m)}
                    className={`flex-1 py-2 rounded-lg items-center ${mode === m ? 'bg-white' : ''}`}
                  >
                    <Text className={`text-xs font-semibold ${mode === m ? 'text-gray-900' : 'text-gray-400'}`}>
                      {m === 'scan' ? 'QR-скан' : m === 'code-entry' ? 'Код вручну' : 'Пошук клієнта'}
                    </Text>
                  </TouchableOpacity>
                ))}
              </View>

              {mode === 'scan' && (
                <View className="mx-4 mt-4">
                  <BarcodeCameraView
                    barcodeTypes={['qr', 'code128']}
                    scanning={scanning && !resolveMutation.isPending}
                    onScan={handleBarcodeScanned}
                    caption={resolveMutation.isPending ? 'Перевірка...' : 'Наведіть на QR-код клієнта'}
                  />
                </View>
              )}

              {mode === 'code-entry' && (
                <View className="mx-4 mt-4">
                  <Text className="text-sm font-semibold text-gray-600 mb-2">Повний код з екрану клієнта</Text>
                  <TextInput
                    className="border border-gray-300 rounded-xl px-4 py-3 text-base text-gray-900 bg-gray-50"
                    placeholder="SGCUS1.xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.123456"
                    autoCapitalize="none"
                    autoCorrect={false}
                    value={codeText}
                    onChangeText={setCodeText}
                  />
                  <TouchableOpacity
                    onPress={() => codeText.trim() && handleResolve(codeText.trim())}
                    disabled={resolveMutation.isPending || !codeText.trim()}
                    className={`mt-3 rounded-xl py-3 items-center ${
                      resolveMutation.isPending || !codeText.trim() ? 'bg-gray-200' : 'bg-primary-600'
                    }`}
                  >
                    {resolveMutation.isPending ? (
                      <ActivityIndicator color="white" />
                    ) : (
                      <Text className={`font-semibold ${!codeText.trim() ? 'text-gray-400' : 'text-white'}`}>
                        Перевірити
                      </Text>
                    )}
                  </TouchableOpacity>
                </View>
              )}

              {mode === 'customer-search' && (
                <View className="flex-1 mx-4 mt-4">
                  <TextInput
                    className="border border-gray-300 rounded-xl px-4 py-3 text-base text-gray-900 bg-gray-50"
                    placeholder="Ім'я або телефон клієнта"
                    value={customerSearch}
                    onChangeText={setCustomerSearch}
                  />
                  {customersLoading ? (
                    <ActivityIndicator className="mt-4" color="#16a34a" />
                  ) : (
                    <FlatList
                      className="mt-3"
                      data={customersPage?.items ?? []}
                      keyExtractor={(item) => item.id}
                      ItemSeparatorComponent={() => <View className="h-2" />}
                      renderItem={({ item }) => (
                        <CustomerCard item={item} onPress={() => setManualCustomer({ id: item.id, name: item.name })} />
                      )}
                      ListEmptyComponent={
                        customerSearch ? (
                          <Text className="text-gray-400 text-center mt-6">Клієнтів не знайдено</Text>
                        ) : null
                      }
                    />
                  )}
                </View>
              )}
            </>
          )}
        </KeyboardAvoidingView>

        {!hasResult && (
          <View className="px-5 pb-6 pt-3 border-t border-gray-100">
            <TouchableOpacity onPress={() => goToPayment({ skip: true })} className="items-center py-2">
              <Text className="text-gray-400 text-sm font-medium">Пропустити — продовжити без бонусів</Text>
            </TouchableOpacity>
          </View>
        )}
      </View>
    </SafeAreaView>
  );
}
