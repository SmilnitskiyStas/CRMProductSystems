import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  KeyboardAvoidingView,
  Platform,
  ActivityIndicator,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useRouter } from 'expo-router';
import { useMobileLogin } from '@/features/auth/hooks/useMobileLogin';
import { mobileLoginSchema, type MobileLoginFormData } from '@/features/auth/validation';

export default function ConsumerLoginScreen() {
  const router = useRouter();
  const { mutate, isPending, error } = useMobileLogin();

  const { control, handleSubmit, formState: { errors } } = useForm<MobileLoginFormData>({
    resolver: zodResolver(mobileLoginSchema),
    defaultValues: { identifier: '', password: '' },
  });

  return (
    <SafeAreaView className="flex-1 bg-white">
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        className="flex-1 justify-center px-6"
      >
        <View className="mb-10">
          <Text className="text-3xl font-bold text-gray-900">Вхід</Text>
          <Text className="text-base text-gray-500 mt-2">Увійдіть у свій акаунт ShelfGuard</Text>
        </View>

        <View className="gap-4">
          <View>
            <Text className="text-sm font-medium text-gray-700 mb-1.5">Email або телефон</Text>
            <Controller
              control={control}
              name="identifier"
              render={({ field: { onChange, onBlur, value } }) => (
                <TextInput
                  className="border border-gray-300 rounded-xl px-4 py-3 text-base text-gray-900 bg-gray-50"
                  placeholder="you@example.com або +380XXXXXXXXX"
                  keyboardType="email-address"
                  autoCapitalize="none"
                  value={value}
                  onBlur={onBlur}
                  onChangeText={onChange}
                />
              )}
            />
            {errors.identifier && (
              <Text className="text-red-500 text-xs mt-1">{errors.identifier.message}</Text>
            )}
          </View>

          <View>
            <Text className="text-sm font-medium text-gray-700 mb-1.5">Пароль</Text>
            <Controller
              control={control}
              name="password"
              render={({ field: { onChange, onBlur, value } }) => (
                <TextInput
                  className="border border-gray-300 rounded-xl px-4 py-3 text-base text-gray-900 bg-gray-50"
                  placeholder="••••••••"
                  secureTextEntry
                  autoComplete="password"
                  value={value}
                  onBlur={onBlur}
                  onChangeText={onChange}
                />
              )}
            />
            {errors.password && (
              <Text className="text-red-500 text-xs mt-1">{errors.password.message}</Text>
            )}
          </View>

          {error && (
            <Text className="text-red-500 text-sm text-center">
              {(error as { response?: { status?: number } })?.response?.status === 401
                ? 'Невірний номер телефону або пароль.'
                : 'Не вдалося увійти. Спробуйте ще раз.'}
            </Text>
          )}

          <TouchableOpacity
            onPress={handleSubmit((data) => mutate(data))}
            disabled={isPending}
            className="bg-primary-600 rounded-xl py-4 items-center mt-2"
          >
            {isPending ? (
              <ActivityIndicator color="white" />
            ) : (
              <Text className="text-white font-semibold text-base">Увійти</Text>
            )}
          </TouchableOpacity>

          <TouchableOpacity onPress={() => router.push('/(auth)/consumer-register')} className="items-center mt-2">
            <Text className="text-primary-600 text-sm font-medium">Ще немає акаунту? Зареєструватися</Text>
          </TouchableOpacity>

        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
