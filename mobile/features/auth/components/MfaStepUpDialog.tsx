import { useEffect, useRef, useState } from 'react';
import { Modal, Text, TextInput, TouchableOpacity, View } from 'react-native';
import { registerMfaPrompt } from '../mfaPrompt';
import { getMe } from '../api/authApi';
import { useAuthStore } from '../store';
import { MfaSetupPanel } from './MfaSetupPanel';

export function MfaStepUpDialog() {
  const resolve = useRef<((code: string | null) => void) | null>(null);
  const [open, setOpen] = useState(false);
  const [code, setCode] = useState('');
  // Keep enrollment visible until recovery codes are acknowledged, even after it updates the user.
  const [needsEnrollment, setNeedsEnrollment] = useState(false);
  useEffect(() => {
    const unregister = registerMfaPrompt(async () => {
      const token = useAuthStore.getState().workspaceAccessToken;
      const profile = await getMe();
      if (token !== useAuthStore.getState().workspaceAccessToken) return null;
      useAuthStore.getState().setUser(profile);
      setNeedsEnrollment(profile.twoFactorEnabled === false);
      return new Promise<string | null>(done => {
      resolve.current?.(null); resolve.current = done; setCode(''); setOpen(true);
      });
    });
    return () => { unregister(); resolve.current?.(null); resolve.current = null; };
  }, []);
  function finish(value: string | null) { resolve.current?.(value); resolve.current = null; setOpen(false); setCode(''); }
  return <Modal visible={open} transparent onRequestClose={() => finish(null)} animationType="fade">
    <View style={{ flex: 1, backgroundColor: '#0009', justifyContent: 'center', padding: 24 }}>
      <View style={{ backgroundColor: 'white', borderRadius: 12, padding: 24, gap: 20 }}>
        {needsEnrollment ? <MfaSetupPanel onClose={() => finish(null)} /> : <>
        <Text className="text-xl font-bold">Підтвердіть дію кодом MFA</Text>
        <Text>Код з автентифікатора або одноразовий код відновлення</Text>
        <TextInput autoFocus autoComplete="one-time-code" accessibilityLabel="Код MFA" maxLength={32} value={code} onChangeText={setCode} className="border border-gray-300 rounded-lg p-3" />
        <TouchableOpacity disabled={!code.trim()} onPress={() => finish(code.trim())}><Text className="text-green-700">Підтвердити</Text></TouchableOpacity>
        <TouchableOpacity onPress={() => finish(null)}><Text>Скасувати</Text></TouchableOpacity>
        </>}
      </View>
    </View>
  </Modal>;
}
