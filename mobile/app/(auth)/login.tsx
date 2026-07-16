import { View, Text, TextInput, TouchableOpacity, KeyboardAvoidingView, Platform, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useLogin } from '@/features/auth/hooks/useLogin';

const schema = z.object({
  email: z.string().email('Невірний email'),
  password: z.string().min(1, "Введіть пароль"),
});

type FormData = z.infer<typeof schema>;

export default function LoginScreen() {
  const { mutate, isPending, error } = useLogin();

  const { control, handleSubmit, formState: { errors } } = useForm<FormData>({
    resolver: zodResolver(schema),
  });

  return (
    <SafeAreaView className="flex-1 bg-white">
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        className="flex-1 justify-center px-6"
      >
        <View className="mb-10">
          <Text className="text-3xl font-bold text-gray-900">ShelfGuard</Text>
          <Text className="text-base text-gray-500 mt-2">Увійдіть у свій акаунт</Text>
        </View>

        <View className="gap-4">
          <View>
            <Text className="text-sm font-medium text-gray-700 mb-1.5">Email</Text>
            <Controller
              control={control}
              name="email"
              render={({ field: { onChange, onBlur, value } }) => (
                <TextInput
                  className="border border-gray-300 rounded-xl px-4 py-3 text-base text-gray-900 bg-gray-50"
                  placeholder="manager@store.com"
                  keyboardType="email-address"
                  autoCapitalize="none"
                  autoComplete="email"
                  value={value}
                  onBlur={onBlur}
                  onChangeText={onChange}
                />
              )}
            />
            {errors.email && (
              <Text className="text-red-500 text-xs mt-1">{errors.email.message}</Text>
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
              {error instanceof Error && error.message === 'TWO_FACTOR_REQUIRED'
                ? 'Двофакторна автентифікація ще не підтримується в мобільному застосунку. Увійдіть через веб-версію або вимкніть 2FA у профілі.'
                : 'Невірний email або пароль'}
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
        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
