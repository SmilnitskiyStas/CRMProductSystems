import { useState } from 'react';
import { Redirect, useRouter } from 'expo-router';
import { Pressable, Text, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import { useMobilePreviewStore } from '@/features/mobile-preview/store';

export default function MobilePreviewScreen() {
  const router = useRouter();
  const [input, setInput] = useState('');
  const { source, error } = useMobileConfig();
  const token = useMobilePreviewStore((state) => state.token);
  const enable = useMobilePreviewStore((state) => state.enable);
  const disable = useMobilePreviewStore((state) => state.disable);
  if (!__DEV__) return <Redirect href="/(personal)" />;

  return (
    <SafeAreaView className="flex-1 bg-gray-50 px-5 pt-6">
      <Text className="text-3xl font-bold text-gray-900">Internal preview</Text>
      <Text className="mt-2 text-sm leading-6 text-gray-500">Draft не кешується і діє лише в поточній dev-сесії.</Text>
      <TextInput value={input} onChangeText={setInput} autoCapitalize="none" autoCorrect={false} secureTextEntry placeholder="Preview token" className="mt-6 rounded-2xl border border-gray-200 bg-white px-4 py-4 text-gray-900" />
      <Pressable onPress={() => enable(input)} className="mt-4 items-center rounded-2xl bg-green-700 py-4"><Text className="font-bold text-white">Завантажити draft</Text></Pressable>
      {token && source === 'preview' ? (
        <View className="mt-5 rounded-2xl bg-amber-50 p-4"><Text className="font-bold text-amber-900">PREVIEW активний</Text><Pressable onPress={() => router.replace('/(personal)')} className="mt-3"><Text className="font-semibold text-amber-800">Відкрити застосунок</Text></Pressable></View>
      ) : token && error ? <Text className="mt-4 text-sm text-red-600">Preview недоступний або конфігурація не пройшла валідацію.</Text> : null}
      {token ? <Pressable onPress={disable} className="mt-4 items-center py-3"><Text className="font-semibold text-gray-500">Вийти з preview</Text></Pressable> : null}
    </SafeAreaView>
  );
}
