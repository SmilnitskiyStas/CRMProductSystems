import { useCallback, useRef, useState } from 'react';
import {
  ActivityIndicator,
  BackHandler,
  Keyboard,
  KeyboardAvoidingView,
  Platform,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import { Redirect, useFocusEffect, useRouter } from 'expo-router';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import { useVerifyTwoFactor } from '@/features/auth/hooks/useLogin';
import {
  isValidTwoFactorCode,
  normalizeTwoFactorCode,
  type TwoFactorCodeMode,
} from '@/features/auth/twoFactorCode';
import { cancelTwoFactorChallenge } from '@/features/auth/twoFactorNavigation';

function twoFactorErrorMessage(error: unknown): string {
  const response = (error as {
    response?: { status?: number; data?: { error?: string } };
  })?.response;

  if (response?.status === 429) {
    return 'Забагато спроб. Зачекайте та спробуйте пізніше.';
  }
  if (response?.data?.error?.toLowerCase().includes('expired')) {
    return 'Час підтвердження вичерпано. Поверніться та увійдіть знову.';
  }
  if (response?.status === 401) {
    return 'Невірний код. Перевірте його та спробуйте ще раз.';
  }
  return 'Не вдалося підтвердити вхід. Перевірте з’єднання та спробуйте ще раз.';
}

export default function TwoFactorScreen() {
  const router = useRouter();
  const challenge = useAuthStore((s) => s.twoFactorChallenge);
  const clearChallenge = useAuthStore((s) => s.clearTwoFactorChallenge);
  const [mode, setMode] = useState<TwoFactorCodeMode>('totp');
  const [code, setCode] = useState('');
  const verifyMutation = useVerifyTwoFactor();
  const cancellationStarted = useRef(false);

  const goBackToLogin = useCallback(
    () => {
      if (cancellationStarted.current) return true;
      cancellationStarted.current = true;
      return cancelTwoFactorChallenge({
        clearChallenge,
        resetVerification: verifyMutation.reset,
        returnToLogin: () => router.replace('/(auth)/consumer-login'),
      });
    },
    [clearChallenge, router, verifyMutation.reset]
  );

  useFocusEffect(
    useCallback(() => {
      const backSubscription = BackHandler.addEventListener(
        'hardwareBackPress',
        goBackToLogin
      );
      // Android consumes the first Back press to close an open IME before React Native
      // receives hardwareBackPress. Treat that keyboard dismissal as the same explicit
      // cancellation so one Back action always leaves the challenge.
      const keyboardSubscription =
        Platform.OS === 'android'
          ? Keyboard.addListener('keyboardDidHide', goBackToLogin)
          : null;

      return () => {
        backSubscription.remove();
        keyboardSubscription?.remove();
        clearChallenge();
      };
    }, [clearChallenge, goBackToLogin])
  );

  if (!challenge) return <Redirect href="/(auth)/consumer-login" />;

  const valid = isValidTwoFactorCode(code, mode);

  function switchMode(nextMode: TwoFactorCodeMode) {
    setMode(nextMode);
    setCode('');
    verifyMutation.reset();
  }

  function submit() {
    if (!valid || verifyMutation.isPending || !challenge) return;
    verifyMutation.mutate({
      challengeToken: challenge.challengeToken,
      code,
    });
  }

  return (
    <SafeAreaView className="flex-1 bg-white">
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        className="flex-1 justify-center px-6"
      >
        <TouchableOpacity
          onPress={goBackToLogin}
          className="absolute top-4 left-5 w-10 h-10 rounded-full bg-gray-100 items-center justify-center"
          accessibilityRole="button"
          accessibilityLabel="Повернутися до входу"
        >
          <Ionicons name="arrow-back" size={20} color="#374151" />
        </TouchableOpacity>

        <View className="items-center mb-8">
          <View className="w-16 h-16 rounded-full bg-primary-100 items-center justify-center">
            <Ionicons name="shield-checkmark-outline" size={32} color="#16a34a" />
          </View>
          <Text className="text-2xl font-bold text-gray-900 mt-4">
            Двофакторна автентифікація
          </Text>
          <Text className="text-sm text-gray-500 text-center mt-2">
            {mode === 'totp'
              ? 'Введіть 6-значний код із застосунку автентифікації.'
              : 'Введіть один із кодів відновлення, збережених під час налаштування 2FA.'}
          </Text>
          <Text className="text-xs text-gray-400 mt-2">{challenge.email}</Text>
        </View>

        <TextInput
          autoFocus
          value={code}
          onChangeText={(value) => setCode(normalizeTwoFactorCode(value, mode))}
          onSubmitEditing={submit}
          keyboardType={mode === 'totp' ? 'number-pad' : 'default'}
          autoCapitalize={mode === 'recovery' ? 'characters' : 'none'}
          autoCorrect={false}
          autoComplete={mode === 'totp' ? 'one-time-code' : 'off'}
          textContentType={mode === 'totp' ? 'oneTimeCode' : 'none'}
          maxLength={mode === 'totp' ? 6 : 9}
          placeholder={mode === 'totp' ? '000000' : 'XXXX-XXXX'}
          placeholderTextColor="#9ca3af"
          className="border border-gray-300 rounded-xl px-4 py-4 text-2xl tracking-widest text-center text-gray-900 bg-gray-50"
          accessibilityLabel={
            mode === 'totp' ? 'Код автентифікації' : 'Код відновлення'
          }
        />

        {verifyMutation.isError && (
          <Text className="text-red-500 text-sm text-center mt-3">
            {twoFactorErrorMessage(verifyMutation.error)}
          </Text>
        )}

        <TouchableOpacity
          onPress={submit}
          disabled={!valid || verifyMutation.isPending}
          className={`rounded-xl py-4 items-center mt-5 ${
            valid && !verifyMutation.isPending ? 'bg-primary-600' : 'bg-gray-200'
          }`}
        >
          {verifyMutation.isPending ? (
            <ActivityIndicator color="white" />
          ) : (
            <Text
              className={`font-semibold text-base ${
                valid ? 'text-white' : 'text-gray-400'
              }`}
            >
              Підтвердити
            </Text>
          )}
        </TouchableOpacity>

        <TouchableOpacity
          onPress={() => switchMode(mode === 'totp' ? 'recovery' : 'totp')}
          className="py-4 items-center"
        >
          <Text className="text-primary-600 font-medium">
            {mode === 'totp'
              ? 'Використати код відновлення'
              : 'Використати код з автентифікатора'}
          </Text>
        </TouchableOpacity>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
