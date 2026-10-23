import { useEffect, useState } from 'react';
import { Redirect, useRouter } from 'expo-router';
import { ScrollView, Text, TextInput, TouchableOpacity, View } from 'react-native';
import QRCode from 'react-native-qrcode-svg';
import { useMutation } from '@tanstack/react-query';
import { apiClient } from '@/lib/api-client';
import { useAuthStore } from '@/features/auth/store';
import { mapAuthUser } from '@/features/auth/api/authApi';

export default function MfaEnrollmentScreen() {
  const token = useAuthStore(s => s.mfaEnrollmentToken);
  const router = useRouter();
  const [code, setCode] = useState('');
  const setup = useMutation({ mutationFn: async () => (await apiClient.post<{ secret: string; otpauthUri: string }>('/auth/mfa-enrollment/setup', { enrollmentToken: token })).data });
  const enable = useMutation({ mutationFn: async () => {
    const { data } = await apiClient.post<{ accessToken: string; user: Parameters<typeof mapAuthUser>[0]; recoveryCodes: string[] }>('/auth/mfa-enrollment/enable', { enrollmentToken: token, code });
    return data;
  } });
  useEffect(() => () => { useAuthStore.getState().setMfaEnrollmentToken(null); }, []);
  // Keep the proof until the recovery codes have been acknowledged.
  if (!token) return <Redirect href="/(auth)/consumer-login" />;
  async function finish() {
    if (!enable.data) return;
    await useAuthStore.getState().setWorkspaceAuth(enable.data.accessToken, mapAuthUser(enable.data.user));
    router.replace('/(app)');
  }
  return <ScrollView contentContainerStyle={{ padding: 24, paddingTop: 64, gap: 20 }}>
    <Text className="text-xl font-bold">Налаштування MFA</Text>
    {enable.data ? <>
      <Text>Збережіть одноразові коди відновлення в надійному місці. Повторно їх показано не буде.</Text>
      <Text selectable className="font-mono">{enable.data.recoveryCodes.join('\n')}</Text>
      <TouchableOpacity accessibilityRole="button" onPress={() => void finish()}><Text className="text-green-700">Коди збережено — продовжити</Text></TouchableOpacity>
    </> : <>
      <Text>Для вашої ролі потрібна двофакторна автентифікація. Додайте обліковий запис у застосунок автентифікації.</Text>
      {!setup.data ? <TouchableOpacity disabled={setup.isPending} onPress={() => setup.mutate()}><Text className="text-green-700">Налаштувати автентифікатор</Text></TouchableOpacity> : <>
        <View className="items-center"><QRCode value={setup.data.otpauthUri} size={180} /></View>
        <Text>Або введіть ключ вручну:</Text><Text selectable>{setup.data.secret}</Text>
        <TextInput accessibilityLabel="Код з автентифікатора" placeholder="6-значний код" keyboardType="number-pad" autoComplete="one-time-code" value={code} onChangeText={s => setCode(s.replace(/\D/g, '').slice(0, 6))} className="border border-gray-300 rounded-lg p-3" />
        <TouchableOpacity disabled={enable.isPending || code.length !== 6} onPress={() => enable.mutate()}><Text className="text-green-700">Підтвердити</Text></TouchableOpacity>
      </>}
      {(setup.error || enable.error) && <Text accessibilityRole="alert" className="text-red-600">Не вдалося підтвердити MFA. Перевірте код; якщо час входу минув, увійдіть знову.</Text>}
      <TouchableOpacity disabled={enable.isPending} onPress={() => { useAuthStore.getState().setMfaEnrollmentToken(null); router.replace('/(auth)/consumer-login'); }}><Text>Повернутися до входу</Text></TouchableOpacity>
    </>}
  </ScrollView>;
}
