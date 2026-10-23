import { useState } from 'react';
import { ScrollView, Text, TextInput, TouchableOpacity, View } from 'react-native';
import { useMutation } from '@tanstack/react-query';
import QRCode from 'react-native-qrcode-svg';
import { apiClient } from '@/lib/api-client';
import { mapAuthUser } from '../api/authApi';
import { useAuthStore } from '../store';
import { getSessionEpoch } from '../session';

/** Voluntary enrollment for a signed-in staff member before their first critical action. */
export function MfaSetupPanel({ onClose }: { onClose: () => void }) {
  const [code, setCode] = useState('');
  const setup = useMutation({ mutationFn: async () => (await apiClient.post<{ secret: string; otpauthUri: string }>('/auth/2fa/setup')).data });
  const enable = useMutation({ mutationFn: async () => {
    const epoch = getSessionEpoch();
    const token = useAuthStore.getState().workspaceAccessToken;
    const { data } = await apiClient.post<{ accessToken: string; user: Parameters<typeof mapAuthUser>[0]; recoveryCodes: string[] }>('/auth/2fa/enable', { code });
    if (epoch !== getSessionEpoch() || token !== useAuthStore.getState().workspaceAccessToken) throw new Error('SESSION_CHANGED_DURING_MFA');
    await useAuthStore.getState().setWorkspaceAuth(data.accessToken, mapAuthUser(data.user));
    return data;
  } });
  return <ScrollView contentContainerStyle={{ gap: 16, padding: 16 }}>
    <Text className="text-xl font-bold">Налаштування MFA</Text>
    <Text>Для цієї дії потрібна двофакторна автентифікація. Після налаштування повторіть дію.</Text>
    {enable.data ? <>
      <Text>Збережіть одноразові коди відновлення. Повторно їх показано не буде.</Text>
      <Text selectable>{enable.data.recoveryCodes.join('\n')}</Text>
      <TouchableOpacity onPress={onClose}><Text className="text-green-700">Коди збережено</Text></TouchableOpacity>
    </> : <>
      {!setup.data ? <TouchableOpacity disabled={setup.isPending} onPress={() => setup.mutate()}><Text className="text-green-700">Налаштувати автентифікатор</Text></TouchableOpacity> : <>
        <View className="items-center"><QRCode value={setup.data.otpauthUri} size={160} /></View>
        <Text>Або введіть ключ вручну:</Text><Text selectable>{setup.data.secret}</Text>
        <TextInput accessibilityLabel="Код з автентифікатора" autoComplete="one-time-code" keyboardType="number-pad" value={code} onChangeText={value => setCode(value.replace(/\D/g, '').slice(0, 6))} placeholder="6-значний код" className="border border-gray-300 p-3 rounded-lg" />
        <TouchableOpacity disabled={code.length !== 6 || enable.isPending} onPress={() => enable.mutate()}><Text className="text-green-700">Підтвердити</Text></TouchableOpacity>
      </>}
      {(setup.error || enable.error) && <Text className="text-red-600">Не вдалося налаштувати MFA. Перевірте код і з’єднання.</Text>}
      <TouchableOpacity disabled={enable.isPending} onPress={onClose}><Text>Скасувати</Text></TouchableOpacity>
    </>}
  </ScrollView>;
}
